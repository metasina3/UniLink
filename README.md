# UniLink 1.2.1

[دانلود نسخه‌ها و سورس (Releases)](https://github.com/metasina3/UniLink/releases) · [راهنمای فارسی آفلاین](docs/README_FA.html) · [راهنمای نسخه مک (macOS Guide)](docs/MACOS_GUIDE.md)

UniLink is an automated, multi-platform client for university captive portals (Cyberoam / Sophos / Ivanti). It features automatic campus network detection, seamless sign-in, background keepalive heartbeats, and multi-account quota failover.

Default network: `UUWN` · Default portal: `https://192.168.100.40:8090/` and `https://gate1.urmia.ac.ir:8090/`.

---

## Platform Editions

| Feature | Windows Edition | macOS Edition |
| :--- | :--- | :--- |
| **Interface** | Native WinForms Dark GUI + System Tray | Native Menu Bar (`NSStatusBar`) + Modern Dark Glassmorphism Dashboard (`WKWebView`) |
| **Runtime** | Self-contained .NET 8 (Zero setup required) | Embedded Python 3 app bundle / CLI (Zero external deps) |
| **Startup** | Windows Run Registry key | macOS `LaunchAgent` (`ir.unilink.desktop.plist`) |
| **Security** | Windows DPAPI encrypted settings | Secure local storage (`0600` permissions) |
| **Network Engine** | Hardware interface & Win32 IP_UNICAST_IF binding | Native SystemConfiguration, BSSID detection & VPN fake-IP bypass |
| **Multi-Account** | Default + sequential failover | Active + sequential quota failover |

---

## macOS Edition

### 1. Build and Run App Bundle
Build `~/Applications/UniLink.app` (includes embedded runtime):
```bash
python3 src/UniLink.macOS/install.py build
open ~/Applications/UniLink.app
```

### 2. Autostart on Login
```bash
python3 src/UniLink.macOS/install.py autostart
# To remove:
python3 src/UniLink.macOS/install.py noautostart
```

### 3. Headless CLI
```bash
# Check network and gate status
python3 -m unilink.cli status
python3 -m unilink.cli probe

# Manage accounts
python3 -m unilink.cli accounts add --label "حساب ۱" --username "1401..."
python3 -m unilink.cli accounts list

# Login / Logout
python3 -m unilink.cli login
python3 -m unilink.cli logout

# Open GUI Dashboard
python3 -m unilink.cli gui
```

---

## Windows Edition

Download the Windows x64 release and run `UniLink.exe`.
Requires Windows 10 (version 2004+) or Windows 11 on x64. Contains its own .NET runtime; no separate installation or admin privileges needed.

Build locally with .NET 8 SDK:
```powershell
./build.ps1
```

---

## Repository Structure

```
UniLink/
├── .github/
│   └── workflows/
│       ├── windows.yml             # Windows x64 build, test & release workflow
│       └── macos.yml               # macOS app bundle build, test & release workflow
├── docs/
│   ├── CHANGELOG.md                # Release changelog
│   ├── MACOS_GUIDE.md              # Full macOS guide & troubleshooting
│   ├── NETWORK_BEHAVIOR.md         # Low-level network adapter binding rules
│   ├── PROTOCOL.md                 # Sophos/Cyberoam captive portal protocol
│   └── README_FA.html              # راهنمای فارسی جامع
├── notices/                        # Third-party licenses & SDK notices
├── src/
│   ├── UniLink.Core/               # .NET Core protocol models & engine
│   ├── UniLink.Windows/            # Windows WinForms client & DPAPI store
│   └── UniLink.macOS/              # macOS MenuBar, Glass Dashboard & CLI
│       ├── install.py              # macOS bundle & LaunchAgent builder
│       └── unilink/
│           ├── client.py           # Captive portal HTTP client (191/192/193)
│           ├── config.py           # Multi-account & settings store
│           ├── netinfo.py          # macOS Wi-Fi & network inspector
│           ├── watcher.py          # Background auto-connect daemon
│           ├── menubar.py          # Native macOS Menu Bar status app
│           ├── window.py           # WKWebView modern glass window
│           ├── cli.py              # Terminal commands & CLI
│           └── gui/                # Dark glassmorphism dashboard UI
└── tests/
    ├── UniLink.Tests/              # .NET test harness
    └── UniLink.macOS.Tests/        # macOS Python test suite (mock portal server)
```

---

## Testing

### macOS Tests
```bash
python3 -m unittest discover tests/UniLink.macOS.Tests
```
Passes 15 tests covering protocol parsing, quota phrases, login, heartbeat, failover, and secure storage against a local mock portal server.

### Windows / Core Tests
```sh
dotnet run --project tests/UniLink.Tests/UniLink.Tests.csproj -c Release
```
Passes 49 tests verifying credential handling, synthetic TLS Schannel keys, physical adapter eligibility, and multi-account rotation.

---

## Protocol Reference
UniLink speaks the Sophos / Cyberoam credential web-client protocol:
- **Login**: `POST /login.xml` (`mode=191`, `username`, `password`, `producttype=0`, `a=<timestamp_ms>`)
- **Heartbeat**: `GET /live.xml?username=...` or `/live` (`mode=192`)
- **Logout**: `POST /logout.xml` (`mode=193`)
- **Quota Exceeded Detection**: Automatic recognition of university exhaustion messages (`"your data transfer has been exceeded"`, `"حجم شما به پایان"`, etc.).
