"""Unit tests for UniLink macOS captive portal client."""

from __future__ import annotations

import os
import sys
import unittest

# Ensure src/UniLink.macOS is importable
MAC_SRC = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "src", "UniLink.macOS"))
if MAC_SRC not in sys.path:
    sys.path.insert(0, MAC_SRC)

from mock_portal import MockPortal
from unilink.client import PortalClient, ReplyKind


class TestPortalClient(unittest.TestCase):
    def setUp(self):
        self.portal = MockPortal({"student1": "pass123", "student2": "pass456"}).start()
        self.probe_url = f"http://127.0.0.1:{self.portal.port}/healthcheck"

    def tearDown(self):
        self.portal.stop()

    def _client(self, username="student1", password="pass123"):
        return PortalClient(
            portal_url=self.portal.url,
            username=username,
            password=password,
            timeout=3.0,
            probe_urls=[self.probe_url],
        )

    def test_probe_cyberoam_page(self):
        client = self._client()
        resp, is_cy = client.probe()
        self.assertTrue(resp.ok)
        self.assertTrue(is_cy)

    def test_successful_login(self):
        client = self._client("student1", "pass123")
        reply = client.login(verify_internet=True)
        self.assertEqual(reply.kind, ReplyKind.ONLINE)
        self.assertEqual(self.portal.logged_in_as(), "student1")

    def test_wrong_password_reports_credentials(self):
        client = self._client("student1", "wrongpass")
        reply = client.login(verify_internet=False)
        self.assertEqual(reply.kind, ReplyKind.CREDENTIALS)
        self.assertIsNone(self.portal.logged_in_as())

    def test_quota_exhaustion_phrase_detection(self):
        self.portal.blocked["student1"] = "Your data transfer has been exceeded"
        client = self._client("student1", "pass123")
        reply = client.login(verify_internet=False)
        self.assertEqual(reply.kind, ReplyKind.QUOTA)

    def test_persian_quota_phrase_detection(self):
        self.portal.blocked["student1"] = "حجم شما به پایان رسیده است"
        client = self._client("student1", "pass123")
        reply = client.login(verify_internet=False)
        self.assertEqual(reply.kind, ReplyKind.QUOTA)

    def test_challenge_response(self):
        self.portal.require_challenge = True
        client = self._client("student1", "pass123")
        reply = client.login(verify_internet=False)
        self.assertEqual(reply.kind, ReplyKind.CHALLENGE)

    def test_heartbeat_keepalive(self):
        client = self._client("student1", "pass123")
        client.login(verify_internet=False)
        reply = client.heartbeat()
        self.assertEqual(reply.kind, ReplyKind.ONLINE)

    def test_logout(self):
        client = self._client("student1", "pass123")
        client.login(verify_internet=False)
        self.assertEqual(self.portal.logged_in_as(), "student1")
        reply = client.logout()
        self.assertEqual(reply.kind, ReplyKind.SIGNED_OUT)
        self.assertIsNone(self.portal.logged_in_as())


if __name__ == "__main__":
    unittest.main()
