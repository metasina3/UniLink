<div align="center">

# UniLink for Android

**Stay signed in to your university Wi-Fi: automatically, reliably, and without touching the browser.**

[![Build APK](../../actions/workflows/build-apk.yml/badge.svg)](../../actions/workflows/build-apk.yml)
[![Release](https://img.shields.io/github/v/release/Mrzzan05/UniLink-Android)](../../releases/latest)
![Platform](https://img.shields.io/badge/platform-Android%208.0%2B-3DDC84?logo=android&logoColor=white)
![License](https://img.shields.io/badge/license-GPL--3.0--or--later-blue)
![Kotlin](https://img.shields.io/badge/Kotlin-Jetpack%20Compose-7F52FF?logo=kotlin&logoColor=white)

[**Download the latest APK**](../../releases/latest) · [فارسی](README_FA.md) · [Changelog](CHANGELOG.md)

<img src="docs/showcase.png" alt="UniLink screenshots" width="900">

</div>

---

UniLink signs you in to Sophos / Cyberoam captive portals (built for Urmia University's **UUWN**) and keeps the session alive in the background. It is the Android companion to the Windows version of UniLink.

Phones close browser tabs, sleep the screen and often run a VPN, and each of those breaks a portal session. UniLink does the sign-in itself from a lightweight background service, so none of that matters.

## Features

| | |
|---|---|
| **VPN-safe** | Portal traffic is bound to the physical Wi-Fi interface, so it works while a VPN is on. If your VPN forbids that, UniLink retries over the VPN route and tells you what to change (see [Troubleshooting](#troubleshooting)). The system proxy is ignored. |
| **Multiple accounts** | Accounts are tried one at a time, in your order. When the portal reports the monthly allowance is used up, the account is skipped until the next month (Asia/Tehran time by default) and the next one is used. |
| **Smart error handling** | A rejected password blocks only that account. Network errors and unknown replies are retried with growing delays and never burn an account. |
| **Certificate pinning** | The portal uses a self-signed certificate. UniLink shows its SHA-256 fingerprint once, you approve it, and from then on only that certificate is accepted. No password is sent before approval. |
| **Redirects refused** | The portal can't bounce your credentials to another address. |
| **Encrypted storage** | Accounts and settings are encrypted with a key held in the Android Keystore and never leave the phone. |
| **Runs in the background** | Foreground service with a persistent notification, optional wake lock, and start on boot. |
| **Persian and English** | Full Persian interface with right-to-left layout, or English. Follows the phone's language, or choose in Settings. |
| **Quick tile, widget and checklist** | A Quick Settings tile and a small home-screen widget to connect or pause, and a first-run checklist that walks you through permissions. |
| **Glass interface** | Frosted-glass cards over a gradient backdrop, in light and dark themes that follow the phone. |
| **Remaining data** | One tap reads each account's remaining data from the university user portal and shows a bar on the Accounts and Connection tabs (and in the widget and tile). The usage page's certificate is pinned the same way as the sign-in portal's. |
| **Smart account order** | Optional **Most data first** signs in with the account that has the most data left; accounts under 200 MB are tried last. |
| **Quiet notifications** | One notice when no account can sign in, and one when a newer UniLink release exists. No data is sent for either, and both can be switched off. |
| **Guided start** | A short first-run tour with a Skip button, and a battery guide for Samsung, Xiaomi, Huawei, Oppo, Vivo and Pixel phones in Settings. |
| **Keeps existing sessions** | If the portal already has a live session for your account, UniLink keeps it alive instead of signing in again. |
| **Diagnostics** | In-memory activity log you can share. Passwords and student numbers are never logged. |

## Screenshots

| Connection | Accounts | Networks | Settings |
|---|---|---|---|
| <img src="docs/screenshots/connection.png" width="200"> | <img src="docs/screenshots/accounts.png" width="200"> | <img src="docs/screenshots/networks.png" width="200"> | <img src="docs/screenshots/settings.png" width="200"> |

## Install

1. Open the [latest release](../../releases/latest) on your phone and download `UniLink-vX.Y.Z.apk`.
2. Open the file and allow **Install unknown apps** if Android asks.
3. Allow **Location** (Android only reveals the Wi-Fi name to apps that have it; location is never stored or sent), **Notifications**, and **Unrestricted battery** so Android doesn't pause the service.
4. **Accounts** tab → **+** → add your student number and password.
5. Connect to `UUWN`. On the **Connection** tab a certificate card appears: compare the fingerprint with the one your browser shows on the normal login page, then tap **Trust this certificate**.

The first time you open the app a short tour explains these steps; tap **Skip** to close it (Settings → **Show the tour again** brings it back).

After that it signs in on its own whenever you're on campus Wi-Fi. New versions install over old ones (same signing key) and keep your accounts and settings.

Optionally add the **Quick Settings tile** (edit the quick panel) or the **home-screen widget** (long-press the home screen → Widgets → UniLink).

## The five tabs

- **Connection**: current state, active account, last keepalive, Connect / Pause & sign out.
- **Accounts**: add, edit, reorder, disable, set default, clear a block, **Refresh data usage** and per-account data bars.
- **Networks**: Wi-Fi names, portal URLs, keepalive interval (30–180 s), time zone, extra quota phrases, pinned certificates.
- **Activity**: what happened and why, with a share button.
- **Settings**: automatic sign-in, start on boot, wake lock, most-data-first, notifications, permission shortcuts, the battery guide and the tour.

## Battery

Many phones (Samsung, Xiaomi, Huawei, Oppo, Vivo…) stop background apps on their own. **Settings → Battery guide** has step-by-step instructions for each brand, with your own brand first and a button that opens the battery permission.

UniLink keeps a background service running, so it does use some battery. Settings has two switches to tune that:

- **Keep CPU awake** keeps keepalive timers on time with the screen off. Most reliable, most power.
- **Keep Wi-Fi at full power** stops the Wi-Fi radio from power-saving with the screen off.

To find your lightest reliable setting: run a day with both on, a day with only Wi-Fi power off, and compare UniLink's share in phone **Settings → Battery**. The Connection tab counts successful, failed and late keepalives, so you can tell whether a change cost reliability.

## Troubleshooting

**It stopped working while my VPN is on** (the log says `Binding socket to network … EPERM`)
Some VPN apps, in strict mode, forbid other apps from connecting to the Wi-Fi directly. Best fix: in your VPN app, **exclude UniLink from the tunnel** (split tunneling / bypass apps / per-app VPN), or turn on **Allow LAN / bypass local network**. Since v1.4.1 UniLink also retries over the VPN route, checks again every minute at most, and reacts at once when a VPN starts or stops.

**"Waiting for campus Wi-Fi" although I'm connected**
Allow **Location** (Android hides the Wi-Fi name without it), or turn on portal-only matching for the network on the **Networks** tab.

**Remaining data shows an error or nothing** (log lines mention `usage`)
The usage page (`gate1.urmia.ac.ir:8450`) is reached over the same Wi-Fi binding, so a strict VPN blocks it too; exclude UniLink from the VPN as above. The first reading asks you to trust that page's certificate, the same way as the sign-in portal.

**No status notification**
Notifications may be blocked for the app; the checklist on the Connection tab and **Settings → Permissions** show the real state.

**Anything else**
Open an issue and paste the **Share log** output from the **Activity** tab. Passwords and student numbers are never in it.

## Build it yourself

You don't need Android Studio: push the repo to GitHub and the **Build APK** workflow builds it for you.

- Every push to `main` produces a minified release APK (`UniLink-release-apk`) and an unminified debug APK (`UniLink-debug-apk`, a fallback) under **Actions → latest run → Artifacts**.
- Pushing a tag such as `v1.0.2` also publishes a **GitHub Release** with the APK and its SHA-256:

```bash
git tag v1.0.2
git push origin v1.0.2
```

Locally: open the folder in Android Studio (JDK 17, Android SDK 35) and press Run, or use the included Gradle wrapper (`./gradlew assembleRelease`, Gradle 8.9).

The build first runs the unit tests (portal and usage-page parsers, account ordering, version comparison); a failing test stops the build.

Release APKs are signed with the project's private release key, kept in GitHub Secrets and never in the repo. The tag workflow checks the signer before publishing, and the signing certificate's SHA-256 is at the bottom of each release's notes (see [SECURITY.md](SECURITY.md)). The debug APK uses the public `app/debug.keystore`, so it is only a fallback and does not update a release install.

## Status and limitations

- The portal protocol was reimplemented from the Windows version's documented behavior and has been used day to day on UUWN. Portals that reply differently may need small adjustments. If something doesn't work, open an issue with the **Share log** output from the Activity tab.
- If a VPN blocks the local network or direct Wi-Fi access, exclude UniLink from the VPN or allow LAN (see Troubleshooting).
- Android only. There is no iOS version: iOS can't keep a background session alive the way this app does.
- Activity log lines are English only (by design, so they are easy to share).

## Roadmap

Import/export of network profiles · more battery tuning · IzzyOnDroid and F-Droid listing (in progress)

## License

UniLink is free software, licensed under the **GNU GPL v3 or later**. See [LICENSE](LICENSE).

## Disclaimer

UniLink is an independent, unofficial project. It is not affiliated with or endorsed by any university or by Sophos. Use it only with accounts you are authorized to use, and follow your institution's network policy.
