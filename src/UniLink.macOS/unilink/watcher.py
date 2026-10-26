"""UniLink background watcher: network observer, automatic sign-in, and keepalive."""

from __future__ import annotations

import logging
import subprocess
import threading
import time
from dataclasses import dataclass, field
from typing import Any, Callable, Dict, List, Optional

from . import netinfo
from .client import DEFAULT_PORTAL, PortalClient, ReplyKind
from .config import Account, ConfigStore

log = logging.getLogger("unilink.watcher")


def notify_macos(title: str, message: str, subtitle: str = "") -> None:
    """Send native macOS notification using osascript."""
    script = f'display notification "{message}" with title "{title}"'
    if subtitle:
        script += f' subtitle "{subtitle}"'
    try:
        subprocess.run(["osascript", "-e", script], capture_output=True, timeout=2.0)
    except Exception:
        pass


@dataclass
class WatcherState:
    campus: bool = False
    online: bool = False
    ssid: str = ""
    ip: str = ""
    status: str = "idle"  # idle | campus | logging-in | online | failed | error | paused
    message: str = "آماده"
    active_account: str = ""
    last_attempt: float = 0.0
    last_ok: float = 0.0
    attempts: List[str] = field(default_factory=list)


class Watcher:
    """Background monitoring and connection daemon."""

    def __init__(self, store: ConfigStore, on_event: Optional[Callable[[str, Any], None]] = None):
        self.store = store
        self.on_event = on_event or (lambda *_: None)
        self._state = WatcherState()
        self._lock = threading.Lock()
        self._stop = threading.Event()
        self._wake = threading.Event()
        self._thread: Optional[threading.Thread] = None
        self._last_heartbeat = 0.0
        self._backoff = 0.0

    def snapshot(self) -> Dict[str, Any]:
        with self._lock:
            s = self._state
            return {
                "campus": s.campus,
                "online": s.online,
                "ssid": s.ssid,
                "ip": s.ip,
                "status": s.status,
                "message": s.message,
                "active_account": s.active_account,
                "last_attempt": s.last_attempt,
                "last_ok": s.last_ok,
                "attempts": list(s.attempts[-8:]),
            }

    def _update(self, **kw: Any) -> None:
        with self._lock:
            for k, v in kw.items():
                setattr(self._state, k, v)
        self.on_event("state", self.snapshot())

    def start(self) -> None:
        if self._thread and self._thread.is_alive():
            return
        self._stop.clear()
        self._thread = threading.Thread(target=self._run, name="unilink-watcher", daemon=True)
        self._thread.start()

    def stop(self) -> None:
        self._stop.set()
        self._wake.set()
        if self._thread:
            self._thread.join(timeout=4.0)

    def poke(self) -> None:
        """Trigger immediate loop evaluation."""
        self._wake.set()

    def _sleep(self, seconds: float) -> None:
        self._wake.wait(timeout=max(1.0, seconds))
        self._wake.clear()

    def _run(self) -> None:
        while not self._stop.is_set():
            try:
                self._tick()
            except Exception as e:
                log.exception("Watcher tick error")
                self._update(status="error", message=f"خطا در ناظر: {type(e).__name__}")
            interval = float(self.store.get("check_interval_seconds", 20) or 20)
            if self._backoff:
                interval = min(interval, self._backoff)
            self._sleep(interval)

    def _tick(self) -> None:
        if not self.store.get("auto_connect", True):
            self._update(status="paused", message="اتصال خودکار غیرفعال است")
            return

        portal = self.store.get("portal_url", DEFAULT_PORTAL)
        ctx = netinfo.inspect(self.store.get("campus_matchers", []), portal)

        active = self.store.active_account()
        active_label = active.label if active else "بدون حساب"

        self._update(
            campus=ctx.campus,
            online=ctx.online,
            ssid=ctx.ssid,
            ip=ctx.ip,
            active_account=active_label,
        )

        if not ctx.campus:
            self._update(status="idle", message="خارج از شبکه دانشگاه")
            self._backoff = 0.0
            return

        # On campus
        if ctx.online:
            # Check if heartbeat is due
            now = time.time()
            hb_interval = float(self.store.get("heartbeat_seconds", 120) or 120)
            if now - self._last_heartbeat >= hb_interval and active:
                client = PortalClient(portal, active.username, active.password)
                reply = client.heartbeat()
                self._last_heartbeat = now
                if reply.kind in (ReplyKind.SIGNED_OUT, ReplyKind.QUOTA):
                    log.warning("Heartbeat indicated session dropped: %s", reply.message)
                    self._update(status="campus", message=f"نشست قطع شد: {reply.message}")
                    self.poke()
                    return
            self._update(status="online", message="متصل به اینترنت دانشگاه")
            self._backoff = 0.0
            return

        # On campus and offline -> auto-login
        self._update(status="logging-in", message=f"در حال ورود با {active_label}...")
        self._attempt_login(portal)

    def _attempt_login(self, portal: str) -> None:
        candidates = self.store.ordered_candidates()
        if not candidates:
            self._update(status="failed", message="هیچ حساب کاربری فعالی تعریف نشده است")
            self._backoff = 60.0
            return

        auto_switch = self.store.get("auto_switch_on_failure", True)
        to_try = candidates if auto_switch else candidates[:1]

        for acc in to_try:
            client = PortalClient(portal, acc.username, acc.password)
            reply = client.login(verify_internet=True)
            now = time.time()
            record = f"{acc.label} ({acc.username}): {reply.kind.value} - {reply.message}"

            with self._lock:
                self._state.attempts.append(record)
                self._state.last_attempt = now

            if reply.kind == ReplyKind.ONLINE:
                self.store.update_account(acc.id, last_login=now, fail_count=0)
                self.store.set_active(acc.id)
                self._last_heartbeat = now
                self._backoff = 0.0
                self._update(
                    status="online",
                    online=True,
                    message="ورود موفق",
                    active_account=acc.label,
                    last_ok=now,
                )
                notify_macos("UniLink", f"ورود موفق با حساب {acc.label}", "متصل به اینترنت دانشگاه")
                return

            # Failed on this account
            self.store.update_account(acc.id, fail_count=acc.fail_count + 1)
            if reply.kind == ReplyKind.QUOTA:
                cur_month = time.strftime("%Y-%m")
                self.store.update_account(acc.id, exhausted_month=cur_month)
                notify_macos("UniLink", f"حجم حساب {acc.label} به پایان رسیده است", "در حال تغییر حساب...")

        # If all candidates failed
        self._backoff = 30.0
        self._update(status="failed", message="ورود ناموفق با تمام حساب‌ها")
