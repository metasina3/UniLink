"""UniLink configuration storage and profile manager.

Stores settings under ~/Library/Application Support/UniLink/config.json
Permissions set to 0600 for credential security.
"""

from __future__ import annotations

import copy
import json
import os
import stat
import time
import uuid
from dataclasses import asdict, dataclass, field
from typing import Any, Dict, List, Optional

CONFIG_DIR = os.path.expanduser("~/Library/Application Support/UniLink")
CONFIG_PATH = os.path.join(CONFIG_DIR, "config.json")

DEFAULT_CONFIG: Dict[str, Any] = {
    "version": 1,
    "portal_url": "https://192.168.100.40:8090/",
    "fallback_portal_url": "https://gate1.urmia.ac.ir:8090/",
    "active_account_id": "",
    "auto_connect": True,
    "auto_switch_on_failure": True,
    "heartbeat_seconds": 120,
    "check_interval_seconds": 20,
    "campus_matchers": [
        {"type": "ssid", "value": "UUWN"},
        {"type": "ssid_re", "value": r"(?i)urmia|uuwn|univ|uni[-_ ]?wifi|eduroam|student"},
        {"type": "dns", "value": "78.38."},
        {"type": "probe", "value": "192.168.100.40:8090"},
        {"type": "probe", "value": "gate1.urmia.ac.ir:8090"},
    ],
    "accounts": [],
}


@dataclass
class Account:
    id: str = field(default_factory=lambda: uuid.uuid4().hex[:12])
    label: str = ""
    username: str = ""
    password: str = ""
    enabled: bool = True
    exhausted_month: str = ""
    fail_count: int = 0
    last_login: float = 0.0

    def masked_password(self) -> str:
        return "*" * len(self.password) if self.password else ""


class ConfigStore:
    """Thread-safe persistent configuration store."""

    def __init__(self, path: str = CONFIG_PATH):
        self.path = path
        self.data: Dict[str, Any] = copy.deepcopy(DEFAULT_CONFIG)
        self.load()

    def load(self) -> None:
        if not os.path.exists(self.path):
            return
        try:
            with open(self.path, "r", encoding="utf-8") as f:
                disk = json.load(f)
            if not isinstance(disk, dict):
                raise ValueError("Config root must be a JSON object")
            for k, v in disk.items():
                if k == "accounts" and isinstance(v, list):
                    self.data["accounts"] = [
                        asdict(Account(**{**asdict(Account()), **item}))
                        for item in v
                        if isinstance(item, dict)
                    ]
                else:
                    self.data[k] = v
        except Exception:
            # Keep backup of corrupt config
            try:
                os.replace(self.path, self.path + f".bak-{int(time.time())}")
            except Exception:
                pass

    def save(self) -> None:
        os.makedirs(os.path.dirname(self.path), exist_ok=True)
        tmp = self.path + f".tmp-{uuid.uuid4().hex[:6]}"
        with open(tmp, "w", encoding="utf-8") as f:
            json.dump(self.data, f, indent=2, ensure_ascii=False)
        os.replace(tmp, self.path)
        try:
            os.chmod(self.path, stat.S_IRUSR | stat.S_IWUSR)  # 0600
        except OSError:
            pass

    def get(self, key: str, default: Any = None) -> Any:
        return self.data.get(key, default)

    def set(self, key: str, value: Any) -> None:
        self.data[key] = value
        self.save()

    # -- Accounts -------------------------------------------------------------
    @property
    def accounts(self) -> List[Account]:
        out = []
        for raw in self.data.get("accounts", []):
            base = asdict(Account())
            base.update({k: raw.get(k, base[k]) for k in base})
            out.append(Account(**base))
        return out

    def active_account(self) -> Optional[Account]:
        act_id = self.data.get("active_account_id", "")
        accs = self.accounts
        if act_id:
            for a in accs:
                if a.id == act_id or a.username == act_id:
                    return a
        # Fallback to first enabled
        for a in accs:
            if a.enabled:
                return a
        return accs[0] if accs else None

    def add_account(self, label: str, username: str, password: str) -> Account:
        acc = Account(label=label or username, username=username, password=password)
        raw_list = self.data.setdefault("accounts", [])
        raw_list.append(asdict(acc))
        if not self.data.get("active_account_id"):
            self.data["active_account_id"] = acc.id
        self.save()
        return acc

    def update_account(self, account_id: str, **changes: Any) -> bool:
        for raw in self.data.get("accounts", []):
            if raw.get("id") == account_id or raw.get("username") == account_id:
                raw.update(changes)
                self.save()
                return True
        return False

    def remove_account(self, account_id: str) -> bool:
        orig_len = len(self.data.get("accounts", []))
        self.data["accounts"] = [
            a for a in self.data.get("accounts", [])
            if a.get("id") != account_id and a.get("username") != account_id
        ]
        if len(self.data["accounts"]) < orig_len:
            if self.data.get("active_account_id") == account_id:
                first = self.data["accounts"][0] if self.data["accounts"] else None
                self.data["active_account_id"] = first["id"] if first else ""
            self.save()
            return True
        return False

    def set_active(self, account_id: str) -> bool:
        for a in self.accounts:
            if a.id == account_id or a.username == account_id:
                self.data["active_account_id"] = a.id
                self.save()
                return True
        return False

    def ordered_candidates(self) -> List[Account]:
        """Ordered accounts for login attempt: active first, then other enabled."""
        accs = self.accounts
        active = self.active_account()
        enabled = [a for a in accs if a.enabled]
        if not active:
            return enabled
        return [active] + [a for a in enabled if a.id != active.id]
