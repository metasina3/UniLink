# UniLink for macOS — Guide & Documentation

## Overview
UniLink for macOS is a native captive-portal client for Cyberoam / Sophos university networks (such as Urmia University's `UUWN`). It runs silently in the macOS menu bar, detects campus Wi-Fi networks in real-time, authenticates automatically, maintains heartbeat sessions, and seamlessly fails over to backup student accounts if data quotas are exhausted.

---

## Features
- **Native Menu Bar App**: Uses native macOS status bar (`NSStatusBar` / SF Symbols) with real-time connection state indicators.
- **Glassmorphism Dashboard**: Dark, modern UI built with native `WKWebView` featuring Lucide vector icons (zero emojis), live connection telemetry, and an account manager.
- **Auto-Discovery & BSSID Detection**: Evaluates Wi-Fi SSIDs (`UUWN`, campus matchers), gateway addresses, DNS servers, and gate reachability.
- **VPN / Fake-IP Resilience**: Filters out fake-IP DNS intercepts caused by local VPN tun devices (sing-box, Clash, V2Box).
- **Multi-Account Failover**: Configurable list of student accounts; automatically switches to the next account if quota runs out or login fails.
- **Headless CLI**: Complete command-line support (`unilink probe`, `unilink status`, `unilink login`, `unilink logout`, `unilink accounts`, `unilink watch`).
- **Autostart Support**: Integrates cleanly with macOS `LaunchAgents` (`ir.unilink.desktop.plist`).
- **Secure Storage**: Credentials stored under `~/Library/Application Support/UniLink/config.json` with user-only permissions (`chmod 0600`).

---

## Installation & Running

### Option 1: Build the Self-Contained `.app` Bundle
To build `~/Applications/UniLink.app` (includes its own embedded Python virtualenv):
```bash
python3 src/UniLink.macOS/install.py build
open ~/Applications/UniLink.app
```

### Option 2: Autostart on macOS Login
```bash
python3 src/UniLink.macOS/install.py autostart
# To remove autostart:
python3 src/UniLink.macOS/install.py noautostart
```

### Option 3: Command-Line Interface (CLI)
```bash
# Check portal & network state
python3 -m unilink.cli status
python3 -m unilink.cli probe

# Manage accounts
python3 -m unilink.cli accounts add --label "Main Account" --username "1401..."
python3 -m unilink.cli accounts list

# Manual connect/disconnect
python3 -m unilink.cli login
python3 -m unilink.cli logout

# Open GUI dashboard
python3 -m unilink.cli gui
```

---

## Testing
Run the automated test suite against the built-in mock captive portal server:
```bash
python3 -m unittest discover tests/UniLink.macOS.Tests
```
