"""UniLink captive-portal client protocol engine.

Supports Cyberoam / Sophos captive portals (mode 191 / 192 / 193):
  POST /login.xml    mode=191&username=U&password=P&a=<ms>&producttype=0
  GET  /live.xml     GET /live.xml?username=U&a=<ms>  (or /live)
  POST /logout.xml   mode=193&username=U&a=<ms>&producttype=0
"""

from __future__ import annotations

import base64
import enum
import hashlib
import html as html_mod
import os
import re
import socket
import ssl
import time
import urllib.error
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field
from typing import Callable, Dict, Iterable, List, Optional, Tuple

UA = "UniLink/1.2.1 (macOS; University Wi-Fi Assistant)"

CAPTIVE_PROBES = (
    "http://captive.apple.com/hotspot-detect.html",
    "http://connectivitycheck.gstatic.com/generate_204",
    "http://detectportal.firefox.com/success.txt",
)

DEFAULT_PORTAL = "https://192.168.100.40:8090/"
FALLBACK_PORTAL = "https://gate1.urmia.ac.ir:8090/"

DEFAULT_QUOTA_PHRASES = [
    "your data transfer has been exceeded",
    "data transfer limit",
    "data transfer quota",
    "maximum data transfer",
    "data usage limit",
    "internet usage limit",
    "quota exceeded",
    "quota exhausted",
    "traffic quota exhausted",
    "حجم شما به پایان",
    "حجم اینترنت شما تمام",
    "اتمام حجم",
    "سهمیه شما به پایان",
]

DEFAULT_CREDENTIAL_PHRASES = [
    "invalid username/password",
    "invalid username or password",
    "incorrect password",
    "wrong password",
    "invalid credentials",
    "نام کاربری یا رمز عبور اشتباه",
]


class ReplyKind(str, enum.Enum):
    ONLINE = "Online"
    SIGNED_OUT = "SignedOut"
    QUOTA = "Quota"
    CREDENTIALS = "Credentials"
    CHALLENGE = "Challenge"
    UNKNOWN = "Unknown"
    LIVE_OFF = "LiveOff"


@dataclass
class PortalReply:
    kind: ReplyKind
    message: str = ""
    status: str = ""
    raw: str = ""


@dataclass
class CertificateInfo:
    portal_id: str
    url: str
    fingerprint: str
    subject: str
    issuer: str
    not_after: str


@dataclass
class Response:
    ok: bool
    status: int
    url: str
    raw: bytes
    headers: Dict[str, str] = field(default_factory=dict)
    error: str = ""

    @property
    def text(self) -> str:
        for enc in ("utf-8", "windows-1256", "cp1256", "latin-1"):
            try:
                return self.raw.decode(enc)
            except UnicodeDecodeError:
                continue
        return self.raw.decode("utf-8", "replace")


def split_host_port(url: str, default_port: int = 8090) -> Tuple[str, int]:
    parsed = urllib.parse.urlparse(url if "://" in url else "https://" + url)
    host = parsed.hostname or "127.0.0.1"
    port = parsed.port or (443 if parsed.scheme == "https" else (80 if parsed.scheme == "http" else default_port))
    return host, port


def check_internet(probe_urls: Iterable[str] = CAPTIVE_PROBES, timeout: float = 3.5) -> Tuple[bool, str]:
    """Verify raw internet reachability bypassing local captive portal."""
    for url in probe_urls:
        try:
            req = urllib.request.Request(url, headers={"User-Agent": UA})
            with urllib.request.urlopen(req, timeout=timeout) as resp:
                status = resp.status
                body = resp.read(512)
                if "apple.com" in url and b"<HTML>" in body and b"Success" in body:
                    return True, "apple"
                if "gstatic.com" in url and status == 204:
                    return True, "google-204"
                if "firefox.com" in url and b"success" in body.lower():
                    return True, "firefox"
                if 200 <= status < 400 and not any(k in body.lower() for k in (b"captive", b"login", b"cyberoam")):
                    return True, f"http-{status}"
        except Exception:
            continue
    return False, ""


class PortalClient:
    """HTTP/TLS client talking the Sophos/Cyberoam captive portal protocol."""

    def __init__(
        self,
        portal_url: str = DEFAULT_PORTAL,
        username: str = "",
        password: str = "",
        timeout: float = 10.0,
        expected_sha256: str = "",
        quota_phrases: Optional[List[str]] = None,
        credential_phrases: Optional[List[str]] = None,
        logger: Optional[Callable[[str], None]] = None,
        probe_urls: Iterable[str] = CAPTIVE_PROBES,
    ):
        self.portal_url = portal_url.rstrip("/") + "/"
        self.username = username
        self.password = password
        self.timeout = timeout
        self.expected_sha256 = expected_sha256.strip().upper()
        self.quota_phrases = quota_phrases or DEFAULT_QUOTA_PHRASES
        self.credential_phrases = credential_phrases or DEFAULT_CREDENTIAL_PHRASES
        self.log = logger or (lambda _: None)
        self.probe_urls = probe_urls
        self.cookies: Dict[str, str] = {}

    def _timestamp(self) -> str:
        return str(int(time.time() * 1000))

    def _ssl_context(self) -> ssl.SSLContext:
        ctx = ssl.create_default_context()
        ctx.check_hostname = False
        ctx.verify_mode = ssl.CERT_NONE
        try:
            ctx.set_ciphers("DEFAULT:@SECLEVEL=1")
        except ssl.SSLError:
            pass
        return ctx

    def _http_request(
        self,
        method: str,
        path: str,
        data: Optional[Dict[str, str]] = None,
        timeout: Optional[float] = None,
    ) -> Response:
        url = urllib.parse.urljoin(self.portal_url, path)
        to = timeout or self.timeout
        post_bytes = None
        headers = {
            "User-Agent": UA,
            "Accept": "*/*",
            "Referer": self.portal_url,
            "Cache-Control": "no-cache",
        }
        if self.cookies:
            headers["Cookie"] = "; ".join(f"{k}={v}" for k, v in self.cookies.items())

        if data is not None:
            post_bytes = urllib.parse.urlencode(data).encode("utf-8")
            headers["Content-Type"] = "application/x-www-form-urlencoded"
            headers["Origin"] = self.portal_url.rstrip("/")

        req = urllib.request.Request(url, data=post_bytes, headers=headers, method=method)
        ctx = self._ssl_context()
        try:
            with urllib.request.urlopen(req, timeout=to, context=ctx) as r:
                raw = r.read()
                hdrs = {k: v for k, v in r.headers.items()}
                # Store cookies
                set_cookie = hdrs.get("Set-Cookie") or hdrs.get("set-cookie") or ""
                if set_cookie:
                    for part in re.split(r",(?=[^;]+?=)", set_cookie):
                        first = part.split(";")[0].strip()
                        if "=" in first:
                            k, v = first.split("=", 1)
                            if k.strip():
                                self.cookies[k.strip()] = v.strip()
                return Response(ok=True, status=r.status, url=url, raw=raw, headers=hdrs)
        except urllib.error.HTTPError as e:
            raw = e.read() if hasattr(e, "read") else b""
            return Response(ok=False, status=e.code, url=url, raw=raw, error=str(e))
        except Exception as e:
            return Response(ok=False, status=0, url=url, raw=b"", error=f"{type(e).__name__}: {e}")

    def probe(self) -> Tuple[Response, bool]:
        """Probe portal to verify Cyberoam/Sophos client support."""
        resp = self._http_request("GET", "")
        t = resp.text.lower()
        is_cyberoam = (
            "httpclient.js" in t
            or "keepaliverequest" in t
            or "login.xml" in t
            or "mode=191" in t
        )
        return resp, is_cyberoam

    def parse_reply(self, xml_text: str, is_live: bool = False) -> PortalReply:
        """Parse XML response from Cyberoam/Sophos portal."""
        if not xml_text or not xml_text.strip():
            return PortalReply(ReplyKind.UNKNOWN, "پاسخی از پورتال دریافت نشد")

        cleaned = xml_text.strip()
        # Handle unescaped CDATA or messy root
        try:
            root = ET.fromstring(cleaned)
        except ET.ParseError:
            m_stat = re.search(r"<(?:status|ack)>(?:<!\[CDATA\[)?(.*?)(?:\]\]>)?</(?:status|ack)>", cleaned, re.S | re.I)
            m_msg = re.search(r"<(?:message|livemessage)>(?:<!\[CDATA\[)?(.*?)(?:\]\]>)?</(?:message|livemessage)>", cleaned, re.S | re.I)
            st = m_stat.group(1).strip() if m_stat else ""
            msg = m_msg.group(1).strip() if m_msg else ""
            return self._classify(st, msg, is_live, cleaned)

        st = (root.findtext("status") or root.findtext("ack") or "").strip()
        msg = (root.findtext("message") or root.findtext("livemessage") or "").strip()
        return self._classify(st, msg, is_live, cleaned)

    def _classify(self, st: str, msg: str, is_live: bool, raw: str) -> PortalReply:
        st_upper = st.upper()
        msg_clean = html_mod.unescape(msg).strip()
        msg_lower = msg_clean.lower()

        # 1. Check Quota exhaustion first
        if any(phrase in msg_lower for phrase in self.quota_phrases):
            return PortalReply(ReplyKind.QUOTA, msg_clean or "حجم اینترنت به پایان رسیده است", st, raw)

        # 2. Check Credential failure
        if any(phrase in msg_lower for phrase in self.credential_phrases):
            return PortalReply(ReplyKind.CREDENTIALS, msg_clean or "نام کاربری یا رمز عبور اشتباه است", st, raw)

        # 3. Check Live status
        if st_upper in ("LIVE", "ACK"):
            return PortalReply(ReplyKind.ONLINE, msg_clean or "متصل به اینترنت", st, raw)

        # 4. Check Challenge (OTP, CAPTCHA, web form challenge)
        if st_upper == "CHALLENGE":
            return PortalReply(ReplyKind.CHALLENGE, msg_clean or "نیاز به تأیید هویت دومرحله‌ای", st, raw)

        # 5. Check SignedOut / NACK
        if st_upper in ("LOGIN", "NACK", "LOGOUT"):
            if "maximum login limit" in msg_lower or "limit reached" in msg_lower or "سقف مجاز" in msg_lower:
                return PortalReply(ReplyKind.CREDENTIALS, msg_clean or "سقف مجاز دستگاه‌ها پر است", st, raw)
            if "suspended" in msg_lower or "disabled" in msg_lower or "مسدود" in msg_lower:
                return PortalReply(ReplyKind.CREDENTIALS, msg_clean or "حساب کاربری مسدود است", st, raw)
            return PortalReply(ReplyKind.SIGNED_OUT, msg_clean or "ورود انجام نشد", st, raw)

        return PortalReply(ReplyKind.UNKNOWN, msg_clean or f"پاسخ ناشناخته ({st})", st, raw)

    def login(self, verify_internet: bool = True) -> PortalReply:
        """Submit login form via POST /login.xml (mode 191)."""
        data = {
            "mode": "191",
            "username": self.username.replace("'", "''"),
            "password": self.password,
            "a": self._timestamp(),
            "producttype": "0",
        }
        self.log(f"در حال ارسال درخواست ورود برای کاربر {self.username}...")
        resp = self._http_request("POST", "login.xml", data)
        if not resp.ok:
            return PortalReply(ReplyKind.UNKNOWN, f"خطای ارتباط با پورتال: {resp.error or resp.status}")

        reply = self.parse_reply(resp.text, is_live=False)
        if reply.kind == ReplyKind.ONLINE and verify_internet:
            online, why = check_internet(self.probe_urls)
            if online:
                self.log(f"ورود با موفقیت انجام شد و اینترنت فعال است ({why}).")
            else:
                self.log("گیت پاسخ LIVE داد، اما اتصال به اینترنت هنوز برقرار نیست.")
        return reply

    def heartbeat(self) -> PortalReply:
        """Send keepalive heartbeat to /live.xml or /live."""
        ts = self._timestamp()
        path = f"live.xml?mode=192&username={urllib.parse.quote(self.username)}&a={ts}&producttype=0"
        resp = self._http_request("GET", path)
        if not resp.ok or not resp.text.strip():
            # Fallback to /live without .xml
            path = f"live?mode=192&username={urllib.parse.quote(self.username)}&a={ts}&producttype=0"
            resp = self._http_request("GET", path)

        if not resp.ok:
            return PortalReply(ReplyKind.UNKNOWN, f"خطای بررسی نشست زنده: {resp.error or resp.status}")
        return self.parse_reply(resp.text, is_live=True)

    def logout(self) -> PortalReply:
        """Send logout request via POST /logout.xml (mode 193)."""
        data = {
            "mode": "193",
            "username": self.username,
            "a": self._timestamp(),
            "producttype": "0",
        }
        self.log(f"در حال خروج کاربر {self.username}...")
        resp = self._http_request("POST", "logout.xml", data)
        if not resp.ok:
            return PortalReply(ReplyKind.UNKNOWN, f"خطای خروج: {resp.error or resp.status}")
        return self.parse_reply(resp.text, is_live=False)
