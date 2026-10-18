# UniLink 1.2.1

A dark blue interface that fits the working area of your display. Pages and dialogs use compact layouts without outer page scrolling. Replace the old executable after exiting it; the existing account file and settings format are unchanged.

A portable Windows desktop client for Sophos/Cyberoam university captive portals.
Default network: `UUWN`; default portal: `https://192.168.100.40:8090/`.
No accounts, student numbers, passwords, or original HAR files are included.

Open **docs/README_FA.html** for the complete Persian setup and troubleshooting guide.

## Run

Download/extract the Windows x64 release and run `UniLink.exe`.
Windows 10 version 2004 (build 19041) or later, including Windows 11, on x64 is required.
The release contains its .NET runtime; users do not need Python, a browser extension, administrator access or a separate .NET installation.
Add accounts in **Accounts**, select the default, connect to campus Wi-Fi, and approve the portal's certificate on first connection.

## Features

- SSID-based automatic sign-in; editable groups, SSIDs and multiple portal endpoints.
- Optional portal-only discovery with a previously pinned certificate, including Ethernet.
- One active login at a time: default account, then enabled fallbacks in saved order.
- The captured protocol: POST `login.xml` (191), GET `live` (192), POST `logout.xml` (193), `producttype=0`.
- Background heartbeat (180 seconds by default), tray controls and Windows sign-in startup.
- Network-interface binding; automatic recovery after network changes or sleep/resume.
- Persistent quota exclusions, Gregorian month rollover in the university time zone, hourly reset-day probes if quota is not yet renewed.
- Wrong passwords require editing/explicit retry. Timeouts, network failures and ambiguous errors never imply quota exhaustion.
- Whole settings file encrypted with current-user Windows DPAPI; atomic writes and an encrypted previous-version backup.
- Certificate approval/pinning before credentials; redirects/proxies are disabled. Changed certificates stop authentication.
- Sanitized, in-memory diagnostic log and account-free network template import/export.

## Build and tests

Install a supported .NET 8 SDK (8.0.426 or a later compatible feature band) from Microsoft, then:

```powershell
./build.ps1
```

Optional ARM64 build on a developer machine:

```powershell
./build.ps1 -Runtime win-arm64
```

Only the x64 build is included with this release. ARM64 has not been built or validated here.
Tests can run separately on Linux, macOS or Windows with the SDK:

```sh
dotnet run --project tests/UniLink.Tests/UniLink.Tests.csproj -c Release
```

The test executable is a dependency-free assertion harness (46 Linux tests plus 3 guarded native Windows cases); it is deliberately invoked with `dotnet run`, rather than `dotnet test`.
It includes an actual local HTTPS server with self-signed certificates, checking form encoding, heartbeat and logout, pin failures, redirects and response-size limits.

## Scope of verification

The x64 binary was cross-built, and the protocol/state-machine tests ran in Linux.
The user confirmed that version 1.0 works on Windows and UUWN. Version 1.2.1 retains the 1.2 layout and adds verified base-adapter selection, interface-scoped Windows DNS and recognition of the university exhaustion message for both new and existing settings. The new layout was rendered and checked with a Mono WinForms compatibility harness in Linux: 39 views at each of 100%, 125%, 150% and 200% (156 checks, zero layout failures); this is not execution of the Windows binary. Windows-specific DPI behavior remains a target-platform check.
The supplied HAR contained successful login/keepalive. The later university log supplied the exact exhaustion phrase now covered by regression tests and built-in detection. Other provider-specific quota phrases remain configurable.

This release implements the Sophos/Cyberoam credential web-client protocol. Changing a URL does not add support for unrelated portal protocols, SSO, OTP/MFA or CAPTCHA.
The supplied university portal uses IPv4; this build selects IPv4 interface addresses. It does not manage Wi-Fi passwords or prevent Windows sleep.
“Connected” means the portal acknowledged authentication; an upstream internet outage or a policy block can still prevent internet access.

## Files and privacy

User data: `%LOCALAPPDATA%\UniLink\settings.dpapi` and `settings.dpapi.bak`.
Encrypted files belong to the Windows user who created them and are not a portable account-sharing format.
Network templates contain no accounts or certificate approvals. The app makes no analytics or cloud-account requests.
Windows startup uses the current user's Run entry named `UniLink` and launches the same executable with `--background`.
Exit and disable startup from the app before removing it. After exit, removing the data directory removes saved accounts.

App source: MIT. Bundled .NET components retain their own licenses under `notices/`.
The distributed executable is not Authenticode-signed. It is built from the included source, and its checksum is supplied in the portable package.
The included Windows workflow tests, publishes and packages the project. A published v-prefixed GitHub release triggers attachment of the tested executable, exact source ZIP and checksums. Only release attachment has write permissions. No credentials are imported automatically.

## UI checks

On a Windows developer machine, run `UniLink.exe --ui-check C:\Temp\UniLink-UI` to inspect the real Windows controls using synthetic accounts. This mode does not read saved credentials, register startup or contact a portal. It writes screenshots and a layout report. The normal app has no dependency on Mono, Wine or the compatibility harness. See `docs/UI_VERIFICATION.md`.

## Base network interface and VPNs

Login, heartbeat and logout verify the selected hardware Wi-Fi/Ethernet adapter and bind both its local IPv4 and Windows IP_UNICAST_IF. Ethernet-like virtual/VPN adapters are excluded. Invalid, removed or unverified adapters fail without a default-route fallback. Adapter GUID/index is checked again before new connections. System HTTP proxies are disabled. Numeric portal URLs do not use DNS; Windows hostname queries specify the same interface in DnsQueryEx.

This does not override VPN firewall/kill-switch rules: allow LAN traffic in the VPN when its firewall blocks private addresses. No system route, proxy or firewall setting is modified. See [network behavior](docs/NETWORK_BEHAVIOR.md).

The exact error `Your data transfer has been exceeded, Please contact the administrator` is classified as exhausted quota even with previously saved/custom phrase lists. The rejected account is blocked for the current university Gregorian month and enabled backups are tried one at a time, normally after 3 seconds. Successful status still wins; transport errors never consume quota.
