"""Mock Sophos/Cyberoam portal server for UniLink macOS integration tests."""

from __future__ import annotations

import threading
import urllib.parse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Dict, List, Optional

LOGIN_PAGE = """<!DOCTYPE html><html><head>
<script>var loginType='0';var userportalport=8450;var liveReqTimeInJS=180;</script>
<script src="/javascript/validation/httpclient.js"></script></head>
<body onload='setup()'>
<div id='credentials' class='loggedout'>
  <input name='username' type='text' id='username'>
  <input name='password' type='password' id='password'>
</div>
</body></html>"""


def xml(status: str, message: str = "", state: str = "", user: str = "") -> bytes:
    return (
        "<?xml version='1.0' ?><requestresponse>"
        f"<status><![CDATA[{status}]]></status>"
        f"<message><![CDATA[{message}]]></message>"
        "<logoutmessage><![CDATA[You have successfully logged off]]></logoutmessage>"
        f"<state><![CDATA[{state}]]></state>"
        f"<user><![CDATA[{user}]]></user>"
        "</requestresponse>"
    ).encode("utf-8")


class MockPortal:
    """Mock captive portal running on localhost."""

    def __init__(self, accounts: Optional[Dict[str, str]] = None, port: int = 0):
        self.accounts = accounts if accounts is not None else {"student1": "pass123", "student2": "pass456"}
        self.sessions: Dict[str, str] = {}
        self.requests: List[str] = []
        self.blocked: Dict[str, str] = {}
        self.require_challenge = False
        portal = self

        class Handler(BaseHTTPRequestHandler):
            protocol_version = "HTTP/1.1"

            def log_message(self, format: str, *args):
                pass

            def _ip(self) -> str:
                return self.client_address[0]

            def _send(self, code: int, body: bytes, ctype: str = "text/xml"):
                self.send_response(code)
                self.send_header("Content-Type", ctype)
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)

            def do_GET(self):
                portal.requests.append("GET " + self.path)
                parsed = urllib.parse.urlsplit(self.path)
                path = parsed.path
                if path == "/":
                    self._send(200, LOGIN_PAGE.encode("utf-8"), "text/html; charset=utf-8")
                elif path in ("/login.xml", "/live.xml", "/live"):
                    who = portal.sessions.get(self._ip())
                    if who:
                        self._send(200, xml("LIVE", "You are signed in", user=who))
                    else:
                        self._send(200, xml("LOGIN", "Please sign in"))
                elif path == "/healthcheck":
                    if self._ip() in portal.sessions:
                        self._send(204, b"", "text/plain")
                    else:
                        self._send(200, LOGIN_PAGE.encode("utf-8"), "text/html")
                else:
                    self._send(404, b"not found", "text/plain")

            def do_POST(self):
                length = int(self.headers.get("Content-Length") or 0)
                body = self.rfile.read(length).decode("utf-8", "replace")
                portal.requests.append("POST " + self.path)
                fields = dict(urllib.parse.parse_qsl(body))
                path = urllib.parse.urlsplit(self.path).path

                if path == "/login.xml":
                    user = fields.get("username", "")
                    pw = fields.get("password", "")
                    ip = self._ip()
                    if portal.require_challenge:
                        self._send(200, xml("CHALLENGE", "Enter OTP code"))
                    elif user in portal.blocked:
                        self._send(200, xml("LOGIN", portal.blocked[user]))
                    elif user in portal.accounts and portal.accounts[user] == pw:
                        portal.sessions[ip] = user
                        self._send(200, xml("LIVE", "You have successfully logged in", user=user))
                    else:
                        self._send(200, xml("LOGIN", "Invalid username or password"))
                elif path == "/logout.xml":
                    portal.sessions.pop(self._ip(), None)
                    self._send(200, xml("LOGIN", "You have successfully logged off"))
                else:
                    self._send(404, b"not found", "text/plain")

        self.server = ThreadingHTTPServer(("127.0.0.1", port), Handler)
        self.server.daemon_threads = True
        self.port = self.server.server_address[1]
        self.url = f"http://127.0.0.1:{self.port}/"
        self._thread: Optional[threading.Thread] = None

    def start(self) -> "MockPortal":
        self._thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self._thread.start()
        return self

    def stop(self) -> None:
        self.server.shutdown()
        self.server.server_close()
        if self._thread:
            self._thread.join(timeout=2.0)

    def logged_in_as(self, ip: str = "127.0.0.1") -> Optional[str]:
        return self.sessions.get(ip)
