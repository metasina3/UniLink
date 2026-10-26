#!/usr/bin/env python3
"""Build ~/Applications/UniLink.app — self-contained macOS menu-bar app bundle.

The bundle carries its own embedded venv (rumps + pyobjc) so it runs
regardless of system Python or Homebrew changes.
"""

from __future__ import annotations

import argparse
import os
import plistlib
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
APP_NAME = "UniLink"
BUNDLE_ID = "ir.unilink.desktop"
DEST_DIR = os.path.expanduser("~/Applications")
APP = os.path.join(DEST_DIR, f"{APP_NAME}.app")

LAUNCHER = """#!/bin/bash
DIR="$(cd "$(dirname "$0")/../Resources" && pwd)"
export PYTHONPATH="$DIR"
export PYTHONUNBUFFERED=1
exec "$DIR/venv/bin/python" -m unilink.menubar "$@" >>"$HOME/Library/Logs/UniLink.log" 2>&1
"""

INFO = {
    "CFBundleName": APP_NAME,
    "CFBundleDisplayName": APP_NAME,
    "CFBundleIdentifier": BUNDLE_ID,
    "CFBundleVersion": "1.3.0",
    "CFBundleShortVersionString": "1.3.0",
    "CFBundleExecutable": APP_NAME,
    "CFBundlePackageType": "APPL",
    "CFBundleSignature": "????",
    "LSMinimumSystemVersion": "12.0",
    "LSUIElement": True,  # Menu bar only, no dock icon
    "NSHighResolutionCapable": True,
    "NSRequiresAquaSystemAppearance": False,
    "CFBundleInfoDictionaryVersion": "6.0",
}


def run(cmd, **kw):
    print("$", " ".join(cmd))
    res = subprocess.run(cmd, **kw)
    if res.returncode != 0:
        raise SystemExit(f"Command failed: {' '.join(cmd)}")
    return res


def build(force_venv: bool = False) -> str:
    res = os.path.join(APP, "Contents", "Resources")
    macos = os.path.join(APP, "Contents", "MacOS")
    os.makedirs(res, exist_ok=True)
    os.makedirs(macos, exist_ok=True)

    # 1. Info.plist
    with open(os.path.join(APP, "Contents", "Info.plist"), "wb") as f:
        plistlib.dump(INFO, f)

    # 2. Launcher executable
    exe = os.path.join(macos, APP_NAME)
    with open(exe, "w") as f:
        f.write(LAUNCHER)
    os.chmod(exe, 0o755)

    # 3. Bundled venv
    venv = os.path.join(res, "venv")
    if force_venv and os.path.isdir(venv):
        shutil.rmtree(venv)
    if not os.path.isdir(venv):
        base = "/usr/bin/python3" if os.path.exists("/usr/bin/python3") else sys.executable
        run([base, "-m", "venv", venv])
    pip = os.path.join(venv, "bin", "pip")
    run([pip, "install", "-q", "--upgrade", "pip"])
    run([
        pip, "install", "-q",
        "rumps>=0.4.0",
        "pyobjc-core>=11.0",
        "pyobjc-framework-Cocoa>=11.0",
        "pyobjc-framework-SystemConfiguration>=11.0",
        "pyobjc-framework-WebKit>=11.0"
    ])

    # 4. App code
    pkg_dst = os.path.join(res, "unilink")
    if os.path.isdir(pkg_dst):
        shutil.rmtree(pkg_dst)
    shutil.copytree(os.path.join(HERE, "unilink"), pkg_dst, ignore=shutil.ignore_patterns("__pycache__", "*.pyc"))

    # 5. Codesign & Register
    subprocess.run([
        "/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister",
        "-f", APP
    ], capture_output=True)
    subprocess.run(["codesign", "--force", "--deep", "--sign", "-", APP], capture_output=True)

    print(f"\n✔ Successfully built {APP}")
    return APP


def autostart(enable: bool = True):
    plist_path = os.path.expanduser(f"~/Library/LaunchAgents/{BUNDLE_ID}.plist")
    if not enable:
        if os.path.exists(plist_path):
            subprocess.run(["launchctl", "unload", plist_path], capture_output=True)
            try:
                os.remove(plist_path)
            except OSError:
                pass
        print("✔ Autostart disabled.")
        return

    exe = os.path.join(APP, "Contents", "MacOS", APP_NAME)
    plist_content = f"""<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>Label</key><string>{BUNDLE_ID}</string>
  <key>ProgramArguments</key><array><string>{exe}</string></array>
  <key>RunAtLoad</key><true/>
  <key>KeepAlive</key><dict><key>SuccessfulExit</key><false/></dict>
  <key>StandardOutPath</key><string>{os.path.expanduser('~/Library/Logs/UniLink.log')}</string>
  <key>StandardErrorPath</key><string>{os.path.expanduser('~/Library/Logs/UniLink.log')}</string>
</dict></plist>"""
    os.makedirs(os.path.dirname(plist_path), exist_ok=True)
    with open(plist_path, "w") as f:
        f.write(plist_content)
    subprocess.run(["launchctl", "load", "-w", plist_path], capture_output=True)
    print("✔ Autostart enabled via LaunchAgent.")


def main():
    p = argparse.ArgumentParser(description="UniLink macOS bundle installer")
    p.add_argument("action", choices=["build", "autostart", "noautostart", "status"], default="build", nargs="?")
    p.add_argument("--force-venv", action="store_true", help="Reinstall embedded venv")
    args = p.parse_args()

    if args.action == "build":
        build(force_venv=args.force_venv)
    elif args.action == "autostart":
        autostart(True)
    elif args.action == "noautostart":
        autostart(False)
    elif args.action == "status":
        print(f"App exists: {os.path.isdir(APP)}")
        plist = os.path.expanduser(f"~/Library/LaunchAgents/{BUNDLE_ID}.plist")
        print(f"Autostart enabled: {os.path.exists(plist)}")


if __name__ == "__main__":
    main()
