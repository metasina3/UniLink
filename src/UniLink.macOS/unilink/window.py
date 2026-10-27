"""Native macOS Dashboard & Settings window for UniLink using WKWebView.

Presents a dark glassmorphism dashboard with real-time status telemetry,
account manager, connection settings, and live event console.
"""

from __future__ import annotations

import json
import os
import subprocess
import sys
import threading
import time
from typing import Any, Dict, Optional

try:
    import objc
    from AppKit import (
        NSApplication, NSBackingStoreBuffered, NSMakeRect, NSPanel,
        NSWindowStyleMaskClosable, NSWindowStyleMaskMiniaturizable,
        NSWindowStyleMaskResizable, NSWindowStyleMaskTitled,
        NSWindowStyleMaskFullSizeContentView
    )
    from Foundation import NSObject
    from WebKit import (
        WKUserContentController, WKWebView, WKWebViewConfiguration
    )
    HAS_PYOBJC = True
except ImportError:
    HAS_PYOBJC = False
    NSObject = object  # type: ignore

from . import netinfo
from .client import DEFAULT_PORTAL, PortalClient, ReplyKind
from .config import Account, ConfigStore

HTML_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "gui", "index.html")
BUNDLE_ID = "ir.unilink.desktop"


if HAS_PYOBJC:
    class ScriptBridge(NSObject):
        """Bridge JavaScript -> Python via WKScriptMessageHandler."""

        def initWithController_(self, controller: "SettingsController"):
            self = objc.super(ScriptBridge, self).init()
            if self is None:
                return None
            self.controller = controller
            return self

        def userContentController_didReceiveScriptMessage_(self, ucc, message):
            try:
                body = message.body()
                action = str(body.get("action", ""))
                payload = body.get("payload", {})
                if hasattr(payload, "items"):
                    py_payload = {
                        str(k): (str(v) if not isinstance(v, (bool, int, float, list, dict)) else v)
                        for k, v in payload.items()
                    }
                else:
                    py_payload = {}
                self.controller.handle_action(action, py_payload)
            except Exception as e:
                print(f"[UniLink Bridge Error] {e}", file=sys.stderr)


class SettingsController(NSObject if HAS_PYOBJC else object):
    """Controls the modern UniLink dashboard window."""

    def initWithApp_(self, app):
        if not HAS_PYOBJC:
            return None
        self = objc.super(SettingsController, self).init()
        if self is None:
            return None
        self.app = app
        self.store: ConfigStore = app.store
        self.win = None
        self.webview = None
        self._build_window()
        return self

    def _build_window(self):
        style = (
            NSWindowStyleMaskTitled
            | NSWindowStyleMaskClosable
            | NSWindowStyleMaskMiniaturizable
            | NSWindowStyleMaskResizable
            | NSWindowStyleMaskFullSizeContentView
        )
        self.win = NSPanel.alloc().initWithContentRect_styleMask_backing_defer_(
            NSMakeRect(0, 0, 900, 640), style, NSBackingStoreBuffered, False
        )
        self.win.setTitle_("UniLink — سامانه هوشمند اتصال دانشگاه")
        self.win.setTitlebarAppearsTransparent_(True)
        self.win.center()
        self.win.setReleasedWhenClosed_(False)
        self.win.setMinSize_((780, 540))

        config = WKWebViewConfiguration.alloc().init()
        ucc = WKUserContentController.alloc().init()
        bridge = ScriptBridge.alloc().initWithController_(self)
        ucc.addScriptMessageHandler_name_(bridge, "pybridge")
        config.setUserContentController_(ucc)

        self.webview = WKWebView.alloc().initWithFrame_configuration_(
            NSMakeRect(0, 0, 900, 640), config
        )
        self.win.setContentView_(self.webview)
        self._load_html()

    def _load_html(self):
        if os.path.exists(HTML_PATH):
            with open(HTML_PATH, "r", encoding="utf-8") as f:
                content = f.read()
            self.webview.loadHTMLString_baseURL_(content, None)

    def eval_js(self, js: str):
        if self.webview:
            def _run():
                self.webview.evaluateJavaScript_completionHandler_(js, None)

            if threading.current_thread() is threading.main_thread():
                _run()
            else:
                try:
                    import PyObjCTools.AppHelper as AppHelper
                    AppHelper.callAfter(_run)
                except Exception:
                    pass

    def handle_action(self, action: str, payload: Dict[str, Any]):
        if action == "ready":
            self.sync_all()
        elif action == "login":
            threading.Thread(target=self._action_login, daemon=True).start()
        elif action == "logout":
            threading.Thread(target=self._action_logout, daemon=True).start()
        elif action == "recheck":
            if hasattr(self.app, "watcher"):
                self.app.watcher.poke()
            self.log_to_ui("sys", "دستور بررسی مجدد شبکه صادر شد")
        elif action == "probe":
            threading.Thread(target=self._action_probe, daemon=True).start()
        elif action == "set_active_account":
            acc_id = payload.get("id") or payload.get("name", "")
            if acc_id:
                self.store.set_active(acc_id)
                if hasattr(self.app, "refresh_menu"):
                    self.app.refresh_menu()
                self.sync_accounts()
                if hasattr(self.app, "watcher"):
                    self.app.watcher.poke()
                self.log_to_ui("auth", f"حساب فعال تغییر یافت: {acc_id}")
        elif action == "save_account":
            self._action_save_account(payload)
        elif action == "delete_account":
            acc_id = payload.get("id") or payload.get("name", "")
            if acc_id:
                self.store.remove_account(acc_id)
                if hasattr(self.app, "refresh_menu"):
                    self.app.refresh_menu()
                self.sync_accounts()
                self.log_to_ui("auth", "حساب کاربری حذف شد")
        elif action == "test_account":
            threading.Thread(target=self._action_test_account, args=(payload,), daemon=True).start()
        elif action == "save_settings":
            self._action_save_settings(payload)
        elif action == "detect_current_wifi":
            ssid = netinfo.wifi_ssid()
            self.eval_js(f"window.onCurrentWifiDetected({json.dumps(ssid)});")

    def _action_login(self):
        self.log_to_ui("auth", "در حال ارسال درخواست ورود به پورتال دانشگاه...")
        portal = self.store.get("portal_url", DEFAULT_PORTAL)
        act = self.store.active_account()
        if not act:
            self.log_to_ui("err", "هیچ حسابی برای ورود انتخاب نشده است")
            return
        client = PortalClient(portal, act.username, act.password)
        reply = client.login(verify_internet=True)
        tag = "ok" if reply.kind == ReplyKind.ONLINE else "err"
        self.log_to_ui(tag, f"نتیجه ورود: {reply.message}")
        self.sync_state()

    def _action_logout(self):
        self.log_to_ui("auth", "درخواست خروج از پورتال...")
        portal = self.store.get("portal_url", DEFAULT_PORTAL)
        act = self.store.active_account()
        if not act:
            return
        client = PortalClient(portal, act.username, act.password)
        reply = client.logout()
        tag = "ok" if reply.kind != ReplyKind.UNKNOWN else "warn"
        self.log_to_ui(tag, f"نتیجه خروج: {reply.message}")
        self.sync_state()

    def _action_probe(self):
        self.log_to_ui("sys", "در حال استعلام وضعیت پورتال...")
        portal = self.store.get("portal_url", DEFAULT_PORTAL)
        client = PortalClient(portal)
        resp, is_cy = client.probe()
        msg = f"پورتال: {portal} | پاسخ: HTTP {resp.status} | نوع Cyberoam: {'بله' if is_cy else 'خیر'}"
        if resp.error:
            msg += f" (خطا: {resp.error})"
        self.log_to_ui("sys", msg)
        self.eval_js(f"document.getElementById('latestPortalMsg').innerText = {json.dumps(msg)};")

    def _action_test_account(self, payload: Dict[str, Any]):
        acc_id = payload.get("id") or payload.get("name", "")
        act = None
        for a in self.store.accounts:
            if a.id == acc_id or a.label == acc_id or a.username == acc_id:
                act = a
                break
        if not act:
            self.log_to_ui("err", f"حساب یافت نشد: {acc_id}")
            return
        self.log_to_ui("auth", f"آزمایش ورود با حساب «{act.label}» ({act.username})...")
        portal = self.store.get("portal_url", DEFAULT_PORTAL)
        client = PortalClient(portal, act.username, act.password)
        reply = client.login(verify_internet=False)
        tag = "ok" if reply.kind == ReplyKind.ONLINE else "err"
        self.log_to_ui(tag, f"نتیجه آزمایش: {reply.kind.value} — {reply.message}")

    def _action_save_account(self, payload: Dict[str, Any]):
        acc_id = payload.get("id") or payload.get("old_name", "")
        label = payload.get("label") or payload.get("name", "").strip()
        user = payload.get("username", "").strip()
        pw = payload.get("password", "")

        if not user:
            return

        found = False
        if acc_id:
            for a in self.store.accounts:
                if a.id == acc_id or a.username == acc_id or a.label == acc_id:
                    self.store.update_account(a.id, label=label or user, username=user, password=pw or a.password)
                    found = True
                    self.log_to_ui("auth", f"حساب «{label or user}» ویرایش شد")
                    break
        if not found:
            for a in self.store.accounts:
                if a.username == user:
                    self.store.update_account(a.id, label=label or user, username=user, password=pw or a.password)
                    found = True
                    self.log_to_ui("auth", f"حساب «{label or user}» ذخیره شد")
                    break
        if not found:
            self.store.add_account(label or user, user, pw)
            self.log_to_ui("auth", f"حساب جدید «{label or user}» اضافه شد")

        if hasattr(self.app, "refresh_menu"):
            self.app.refresh_menu()
        self.sync_accounts()

    def _action_save_settings(self, payload: Dict[str, Any]):
        self.store.set("auto_connect", bool(payload.get("auto_login", True)))
        self.store.set("auto_switch_on_failure", bool(payload.get("auto_switch_on_failure", True)))
        self.store.set("check_interval_seconds", int(payload.get("check_interval_seconds", 20)))
        self.store.set("heartbeat_seconds", int(payload.get("keepalive_seconds", 120)))

        portal = payload.get("gate_url", "").strip()
        if portal:
            self.store.set("portal_url", portal)

        ssids = payload.get("ssids", [])
        if isinstance(ssids, list):
            matchers = [{"type": "ssid", "value": s} for s in ssids if s]
            matchers += [
                {"type": "ssid_re", "value": r"(?i)urmia|uuwn|univ|uni[-_ ]?wifi|eduroam|student"},
                {"type": "dns", "value": "78.38."},
                {"type": "probe", "value": "192.168.100.40:8090"},
                {"type": "probe", "value": "gate1.urmia.ac.ir:8090"},
            ]
            self.store.set("campus_matchers", matchers)

        want_autostart = bool(payload.get("launch_at_login", False))
        self._toggle_launch_agent(want_autostart)

        if hasattr(self.app, "refresh_menu"):
            self.app.refresh_menu()
        if hasattr(self.app, "watcher"):
            self.app.watcher.poke()
        self.log_to_ui("sys", "تنظیمات اتصال ذخیره شدند")

    def _is_autostart_enabled(self) -> bool:
        path = os.path.expanduser(f"~/Library/LaunchAgents/{BUNDLE_ID}.plist")
        return os.path.exists(path)

    def _toggle_launch_agent(self, enable: bool):
        path = os.path.expanduser(f"~/Library/LaunchAgents/{BUNDLE_ID}.plist")
        if not enable:
            if os.path.exists(path):
                subprocess.run(["launchctl", "unload", path], capture_output=True)
                try:
                    os.remove(path)
                except OSError:
                    pass
        else:
            exe = os.path.expanduser("~/Applications/UniLink.app/Contents/MacOS/UniLink")
            plist = f"""<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>Label</key><string>{BUNDLE_ID}</string>
  <key>ProgramArguments</key><array><string>{exe}</string></array>
  <key>RunAtLoad</key><true/>
  <key>KeepAlive</key><dict><key>SuccessfulExit</key><false/></dict>
  <key>StandardOutPath</key><string>{os.path.expanduser('~/Library/Logs/UniLink.log')}</string>
  <key>StandardErrorPath</key><string>{os.path.expanduser('~/Library/Logs/UniLink.log')}</string>
</dict></plist>"""
            os.makedirs(os.path.dirname(path), exist_ok=True)
            with open(path, "w") as f:
                f.write(plist)
            subprocess.run(["launchctl", "load", "-w", path], capture_output=True)

    def sync_all(self):
        self.sync_state()
        self.sync_accounts()
        self.sync_settings()

    def sync_state(self):
        snap = self.app.watcher.snapshot() if hasattr(self.app, "watcher") else {}
        act = self.store.active_account()
        state = {
            "status": snap.get("status", "idle"),
            "message": snap.get("message", "آماده"),
            "ssid": snap.get("ssid", ""),
            "ip": snap.get("ip", ""),
            "campus": snap.get("campus", False),
            "online": snap.get("online", False),
            "active_account": act.label if act else "بدون حساب",
        }
        self.eval_js(f"window.updateAppState({json.dumps(state)});")

    def sync_accounts(self):
        raw_list = []
        for a in self.store.accounts:
            raw_list.append({
                "id": a.id,
                "name": a.label,
                "username": a.username,
                "password": a.password,
                "gate_url": "",
                "note": "",
                "enabled": a.enabled,
                "fail_count": a.fail_count,
            })
        active = self.store.active_account()
        active_name = active.label if active else ""
        self.eval_js(f"window.updateAccounts({json.dumps(raw_list)}, {json.dumps(active_name)});")

    def sync_settings(self):
        ssids = [m.get("value", "") for m in self.store.get("campus_matchers", []) if m.get("type") == "ssid"]
        cfg = {
            "auto_login": self.store.get("auto_connect", True),
            "auto_switch_on_failure": self.store.get("auto_switch_on_failure", True),
            "check_interval_seconds": self.store.get("check_interval_seconds", 20),
            "keepalive_seconds": self.store.get("heartbeat_seconds", 120),
            "gate_url": self.store.get("portal_url", DEFAULT_PORTAL),
            "ssids": ssids or ["UUWN"],
            "launch_at_login": self._is_autostart_enabled(),
        }
        self.eval_js(f"window.updateSettings({json.dumps(cfg)});")

    def log_to_ui(self, tag: str, msg: str):
        self.eval_js(f"window.addLogMessage({json.dumps(tag)}, {json.dumps(msg)});")

    def show(self):
        if not HAS_PYOBJC:
            print("PyObjC is not installed; GUI cannot be shown.", file=sys.stderr)
            return
        self.sync_all()
        NSApplication.sharedApplication().activateIgnoringOtherApps_(True)
        self.win.makeKeyAndOrderFront_(None)


def show_dashboard(store: ConfigStore):
    """Standalone runner for dashboard GUI."""
    if not HAS_PYOBJC:
        print("Error: PyObjC is required to open the macOS dashboard.", file=sys.stderr)
        return

    from AppKit import NSApplication, NSApplicationActivationPolicyRegular
    app = NSApplication.sharedApplication()
    app.setActivationPolicy_(NSApplicationActivationPolicyRegular)

    class DummyApp:
        def __init__(self, s):
            self.store = s

    dummy = DummyApp(store)
    controller = SettingsController.alloc().initWithApp_(dummy)
    controller.show()
    app.run()
