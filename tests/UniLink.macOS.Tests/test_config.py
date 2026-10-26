"""Unit tests for UniLink macOS configuration store."""

from __future__ import annotations

import os
import stat
import sys
import tempfile
import unittest

MAC_SRC = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "src", "UniLink.macOS"))
if MAC_SRC not in sys.path:
    sys.path.insert(0, MAC_SRC)

from unilink.config import ConfigStore, Account


class TestConfigStore(unittest.TestCase):
    def setUp(self):
        self.temp_dir = tempfile.mkdtemp()
        self.config_path = os.path.join(self.temp_dir, "config.json")
        self.store = ConfigStore(self.config_path)

    def tearDown(self):
        if os.path.exists(self.config_path):
            os.remove(self.config_path)
        if os.path.exists(self.temp_dir):
            os.rmdir(self.temp_dir)

    def test_default_config_created(self):
        self.assertTrue(self.store.get("auto_connect"))
        self.assertEqual(self.store.get("heartbeat_seconds"), 120)
        self.assertEqual(len(self.store.accounts), 0)

    def test_add_and_get_account(self):
        acc = self.store.add_account("Test Acc", "stu123", "secret999")
        self.assertEqual(acc.username, "stu123")
        self.assertEqual(len(self.store.accounts), 1)
        self.assertEqual(self.store.active_account().id, acc.id)

    def test_candidate_ordering(self):
        acc1 = self.store.add_account("Acc 1", "u1", "p1")
        acc2 = self.store.add_account("Acc 2", "u2", "p2")
        acc3 = self.store.add_account("Acc 3", "u3", "p3")

        # By default acc1 is active
        candidates = self.store.ordered_candidates()
        self.assertEqual([c.id for c in candidates], [acc1.id, acc2.id, acc3.id])

        # Switch active to acc2
        self.store.set_active(acc2.id)
        candidates = self.store.ordered_candidates()
        self.assertEqual(candidates[0].id, acc2.id)

    def test_remove_account(self):
        acc1 = self.store.add_account("Acc 1", "u1", "p1")
        acc2 = self.store.add_account("Acc 2", "u2", "p2")
        self.assertTrue(self.store.remove_account(acc1.id))
        self.assertEqual(len(self.store.accounts), 1)
        self.assertEqual(self.store.active_account().id, acc2.id)

    def test_file_permissions_secure(self):
        self.store.add_account("Acc", "u", "p")
        mode = os.stat(self.config_path).st_mode
        self.assertTrue(mode & stat.S_IRUSR)
        self.assertTrue(mode & stat.S_IWUSR)
        self.assertFalse(mode & stat.S_IROTH)
        self.assertFalse(mode & stat.S_IWOTH)


if __name__ == "__main__":
    unittest.main()
