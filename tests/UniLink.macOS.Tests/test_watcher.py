"""Unit tests for UniLink macOS background watcher."""

from __future__ import annotations

import os
import sys
import tempfile
import time
import unittest

MAC_SRC = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "src", "UniLink.macOS"))
if MAC_SRC not in sys.path:
    sys.path.insert(0, MAC_SRC)

from mock_portal import MockPortal
from unilink import netinfo
from unilink.config import ConfigStore
from unilink.watcher import Watcher


class FakeNet:
    def __init__(self, campus: bool = True, online: bool = False):
        self.campus = campus
        self.online = online

    def inspect(self, matchers, portal_url="", quick=False):
        return netinfo.NetContext(
            ssid="UUWN" if self.campus else "HomeWi-Fi",
            ip="10.0.0.45",
            gateway="10.0.0.1",
            online=self.online,
            campus=self.campus,
            portal_reachable=self.campus,
        )


class TestWatcher(unittest.TestCase):
    def setUp(self):
        self.portal = MockPortal({
            "primary": "pass1",
            "backup": "pass2",
        }).start()
        self.temp_dir = tempfile.mkdtemp()
        self.config_path = os.path.join(self.temp_dir, "config.json")
        self.store = ConfigStore(self.config_path)
        self.store.set("portal_url", self.portal.url)
        self.store.set("check_interval_seconds", 1)
        self.store.add_account("Primary Account", "primary", "pass1")
        self.events = []

    def tearDown(self):
        self.portal.stop()
        if os.path.exists(self.config_path):
            os.remove(self.config_path)
        if os.path.exists(self.temp_dir):
            os.rmdir(self.temp_dir)

    def test_auto_login_on_campus_network(self):
        orig_inspect = netinfo.inspect
        fake = FakeNet(campus=True, online=False)
        netinfo.inspect = fake.inspect
        try:
            watcher = Watcher(self.store, on_event=lambda k, v: self.events.append((k, v)))
            watcher._tick()
            self.assertEqual(self.portal.logged_in_as(), "primary")
            snap = watcher.snapshot()
            self.assertEqual(snap["status"], "online")
        finally:
            netinfo.inspect = orig_inspect

    def test_failover_when_primary_exhausted(self):
        self.store.add_account("Backup Account", "backup", "pass2")
        self.portal.blocked["primary"] = "Your data transfer has been exceeded"

        orig_inspect = netinfo.inspect
        fake = FakeNet(campus=True, online=False)
        netinfo.inspect = fake.inspect
        try:
            watcher = Watcher(self.store, on_event=lambda k, v: self.events.append((k, v)))
            watcher._tick()
            # Primary exhausted, backup should be logged in
            self.assertEqual(self.portal.logged_in_as(), "backup")
            self.assertEqual(self.store.active_account().username, "backup")
        finally:
            netinfo.inspect = orig_inspect


if __name__ == "__main__":
    unittest.main()
