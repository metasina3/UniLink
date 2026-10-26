"""Menu-bar application for UniLink on macOS.

Maintains top menu bar status item, quick connect actions, and launches
the modern WKWebView settings dashboard.
"""

from __future__ import annotations

import os
import subprocess
import sys
import tempfile
import threading
import time
from typing import Optional

try:
    import rumps
    from AppKit import (
        NSAlert, NSBitmapImageFileTypePNG, NSBitmapImageRep,
        NSGraphicsContext, NSImage, NSImageSymbolConfiguration,
        NSCompositingOperationSourceOver
    )
    HAS_RUMPS = True
except ImportError:
    HAS_RUMPS = False

from . import __version__, netinfo
from .client import DEFAULT_PORTAL, check_internet
from .config import ConfigStore
from .watcher import Watcher
from .window import BUNDLE_ID, SettingsController

APP_NAME = "UniLink"

ICONS = {
    "online": ("wifi", "متصل"),
    "campus": ("wifi.exclamationmark", "شبکه دانشگاه — نیاز به ورود"),
    "logging-in": ("arrow.triangle.2.circlepath", "در حال ورود"),
    "failed": ("wifi.slash", "ورود ناموفق"),
    "idle": ("wifi.slash", "خارج از شبکه دانشگاه"),
    "paused": ("pause.circle", "غیرفعال"),
    "error": ("exclamationmark.triangle", "خطا"),
}

ICON_DIR = os.path.join(tempfile.gettempdir(), "unilink-icons")


def _symbol_png(name: str) -> Optional[str]:
    """Render an SF Symbol to a template PNG rumps can hand to the menu bar."""
    if not HAS_RUMPS:
        return None
    os.makedirs(ICON_DIR, exist_ok=True)
    path = os.path.join(ICON_DIR, f"{name}.png")
    if os.path.exists(path):
        return path
    try:
        img = NSImage.imageWithSystemSymbolName_accessibilityDescription_(name, None)
    except Exception:
        return None
    if img is None:
        return None
    cfg = NSImageSymbolConfiguration.configurationWithPointSize_weight_(15.0, 0)
    img = img.imageWithSymbolConfiguration_(cfg) or img
    size = img.size()
    w, h = int(size.width) or 18, int(size.height) or 18
    rep = NSBitmapImageRep.alloc().initWithBitmapDataPlanes_pixelsWide_pixelsHigh_bitsPerSample_samplesPerPixel_hasAlpha_isPlanar_colorSpaceName_bytesPerRow_bitsPerPixel_(
        None, w, h, 8, 4, True, False, "NSCalibratedRGBColorSpace", 0, 0
    )
    ctx = NSGraphicsContext.graphicsContextWithBitmapImageRep_(rep)
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.setCurrentContext_(ctx)
    img.drawInRect_fromRect_operation_fraction_(((0, 0), (w, h)), ((0, 0), (0, 0)), NSCompositingOperationSourceOver, 1.0)
    NSGraphicsContext.restoreGraphicsState()
    data = rep.representationUsingType_properties_(NSBitmapImageFileTypePNG, {})
    if data is None:
        return None
    with open(path, "wb") as f:
        f.write(bytes(data))
    return path


class UniLinkApp(rumps.App if HAS_RUMPS else object):
    def __init__(self):
        if not HAS_RUMPS:
            raise RuntimeError("rumps and pyobjc are required to run UniLink MenuBar")
        super().__init__(APP_NAME, title="", quit_button=None)
        self.store = ConfigStore()
        self.watcher = Watcher(self.store, on_event=self._on_event)
        self.settings: Optional[SettingsController] = None
        self._icon_state = ""
        self._build_menu()
        self.refresh_menu()
        self.watcher.start()

    def _build_menu(self):
        self.item_status = rumps.MenuItem("در حال بررسی...")
        self.item_net = rumps.MenuItem("شبکه: —")
        self.item_account = rumps.MenuItem("حساب: —")
        self.item_login = rumps.MenuItem("ورود همین حالا", callback=self.do_login)
        self.item_logout = rumps.MenuItem("خروج از پورتال", callback=self.do_logout)
        self.item_toggle = rumps.MenuItem("اتصال خودکار", callback=self.toggle_auto)
        self.accounts_menu = rumps.MenuItem("تغییر حساب")

        self.menu = [
            self.item_status,
            self.item_net,
            self.item_account,
            None,
            self.item_login,
            self.item_logout,
            self.accounts_menu,
            self.item_toggle,
            None,
            rumps.MenuItem("داشبورد و تنظیمات UniLink...", key=",", callback=self.open_settings),
            rumps.MenuItem("بررسی وضعیت شبکه (Probe)", callback=self.do_probe),
            None,
            rumps.MenuItem("اجرای خودکار هنگام ورود به سیستم", callback=self.toggle_launch_agent),
            rumps.MenuItem(f"نسخه {__version__}"),
            rumps.MenuItem("خروج از UniLink", key="q", callback=self.quit_app),
        ]

    def _set_icon(self, status: str):
        if status == self._icon_state:
            return
        self._icon_state = status
        symbol, _ = ICONS.get(status, ("wifi.slash", ""))
        png = _symbol_png(symbol)
        if png:
            self.icon = png
            self.template = True
        else:
            self.title = "●" if status == "online" else "○"

    def refresh_menu(self):
        snap = self.watcher.snapshot()
        st = snap.get("status", "idle")
        self._set_icon(st)

        status_fa = {
            "online": "متصل به اینترنت دانشگاه",
            "campus": "شبکه دانشگاه — نیاز به ورود",
            "logging-in": "در حال ورود...",
            "failed": "ورود ناموفق",
            "idle": "خارج از شبکه دانشگاه",
            "paused": "غیرفعال",
            "error": "خطا",
        }.get(st, st)
        self.item_status.title = f"وضعیت: {status_fa}"

        ssid = snap.get("ssid") or "بدون اتصال Wi-Fi"
        ip = snap.get("ip") or "—"
        self.item_net.title = f"شبکه: {ssid} ({ip})"

        act = self.store.active_account()
        self.item_account.title = f"حساب: {act.label if act else 'تعریف نشده'}"
        self.item_toggle.state = 1 if self.store.get("auto_connect", True) else 0

        # Accounts list
        self.accounts_menu.clear()
        accs = self.store.accounts
        if not accs:
            self.accounts_menu.add(rumps.MenuItem("هیچ حسابی افزوده نشده"))
        else:
            for a in accs:
                item = rumps.MenuItem(
                    f"{a.label} ({a.username})",
                    callback=lambda sender, acc_id=a.id: self.switch_account(acc_id),
                )
                if act and a.id == act.id:
                    item.state = 1
                self.accounts_menu.add(item)

    def _on_event(self, kind: str, payload: dict):
        self.refresh_menu()
        if self.settings and self.settings.win and self.settings.win.isVisible():
            self.settings.sync_state()

    def do_login(self, _):
        self.watcher.poke()

    def do_logout(self, _):
        portal = self.store.get("portal_url", DEFAULT_PORTAL)
        act = self.store.active_account()
        if act:
            from .client import PortalClient
            client = PortalClient(portal, act.username, act.password)
            client.logout()
            self.watcher.poke()

    def toggle_auto(self, sender):
        cur = self.store.get("auto_connect", True)
        self.store.set("auto_connect", not cur)
        sender.state = 0 if cur else 1
        self.watcher.poke()

    def switch_account(self, acc_id: str):
        self.store.set_active(acc_id)
        self.refresh_menu()
        self.watcher.poke()

    def open_settings(self, _):
        if self.settings is None:
            self.settings = SettingsController.alloc().initWithApp_(self)
        self.settings.show()

    def do_probe(self, _):
        self.watcher.poke()

    def toggle_launch_agent(self, sender):
        path = os.path.expanduser(f"~/Library/LaunchAgents/{BUNDLE_ID}.plist")
        enable = not os.path.exists(path)
        if enable:
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
            sender.state = 1
        else:
            subprocess.run(["launchctl", "unload", path], capture_output=True)
            try:
                os.remove(path)
            except OSError:
                pass
            sender.state = 0

    def quit_app(self, _):
        self.watcher.stop()
        rumps.quit_application()


def main():
    if not HAS_RUMPS:
        print("Error: rumps and PyObjC are required to run UniLink MenuBar.", file=sys.stderr)
        sys.exit(1)
    app = UniLinkApp()
    app.run()


if __name__ == "__main__":
    main()
