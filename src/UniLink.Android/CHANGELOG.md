# Changelog

## 1.14.1
- Fix: the **Connect / Pause button on the home-screen widget** did nothing on some phones. It now runs through a tiny invisible screen that is always allowed to start the keep-alive service, opens the Wi-Fi switch when Wi-Fi is off, and shows why when there is no campus Wi-Fi. The widget also updates the moment you tap.

## 1.14.0
- New: **Use this account** button on every usable account card. It makes the account the default, signs out of the one in use, signs in with it and opens the Connection tab. It tries only that account: if its password is rejected or its data is used up, the Connection tab says so instead of silently using another account. Settings has "Switch right after choosing an account" (on by default); when off, the button only makes the account the default.
- New: the **Connection tab** now shows a red "Password rejected" card whenever an enabled account has a rejected password (also while another account is signed in), with a button to the Accounts tab. Accounts with a rejected password get a red **Fix password** button.

## 1.13.1
- Fix: the battery guide button did nothing when battery use was already unrestricted. It now opens the app settings page (with the per-app Battery entry) in that case, and the system allow dialog otherwise.

## 1.13.0
- New: **first-run tour**. New installs see six short cards (accounts, trusting the portal, connecting, remaining data, background permissions) with a Skip button; Back also skips. It is shown once; Settings has "Show the tour again". Existing installs do not see it.
- New: **battery guide** in Settings with step-by-step instructions for Samsung, Xiaomi/Redmi/POCO, Huawei/Honor, Oppo/Realme/OnePlus, Vivo/iQOO and Pixel/other phones (your phone's brand opens first), plus a button for the battery permission. Persian and English. The battery button opens the system dialog when battery use is restricted, otherwise the app settings page (where the per-app Battery entry is).

## 1.12.1
- Fix: sign-in could stall for minutes while the portal answered logins instantly. The quick "is an account already live?" check was treated as a failed network whenever it merely timed out, so UniLink waited and retried instead of signing in (only pressing Connect, which skips that check, got through). A slow answer no longer blocks the sign-in; only a portal that cannot be reached at all triggers the quick-retry path. The check also gives up after 4 s of silence instead of 15 s.
- Fix: pressing Connect now shows "Signing in…" immediately instead of looking frozen while an earlier attempt finishes.
- Fix: if Pause & sign out is pressed while a sign-in is in progress, that attempt is discarded (and signed out if it had just logged in) instead of coming back online.

## 1.12.0
- New: **Most data first** (Settings, off by default). Signs in with the account that has the most data left, using the last "Refresh data usage" readings (readings older than 10 days are ignored). Accounts known to have under 200 MB left are always tried last.
- New: **one quiet notification when no account can sign in** (all out of data, disabled or rejected). Shown once until a sign-in works again; switch off in Settings.
- New: the home-screen widget and the Quick Settings tile show the data left of the account in use.
- Faster sign-in right after joining the Wi-Fi (reported on a Galaxy A51: 48 s from joining to signed in): the "is an account already live?" check now gives up after 3 s instead of 6 s, and when the portal cannot be reached at all UniLink retries quickly (3, 5, 8, 12, 20, 30 s) for the first two minutes instead of signing in blindly and backing off 5/15/30 s.
- After UniLink signs in, it tells Android the Wi-Fi works so the system re-checks it (signing in inside the app never showed Android the captive-portal page, so the Wi-Fi could stay marked "no internet", which can make phones prefer mobile data and break Wi-Fi tethering). The result is written to the log ("Android network check after sign-in: validated=...").
- Internal: the portal/usage parsers, account ordering and version comparison are plain Kotlin with unit tests (including a real usage page with the user name removed). The build runs them before assembling any APK.

## 1.11.0
- New: **update notice**. Once a day, while the UniLink service is running, the app asks GitHub for the latest release. Only if that version is newer than the installed one, and only once per version, it shows one quiet notification that opens the release page. Nothing is installed, no account data is sent, and it can be switched off in Settings ("Tell me about new versions"). Failed checks (for example before sign-in gives internet) retry after 30 minutes.

## 1.10.6
- New: the **Connection** tab shows the data bar of the account in use, with a Refresh button. When you open the tab and the numbers are older than 30 minutes, it refreshes once by itself (only while the app is on screen).
- The bar now shows what is **left**: it shrinks as data is used, turns amber below 30% and red below 10%.
- "Updated 0 minutes ago" now reads "just now".

## 1.10.5
- Fix: the portal's usage page marks its rows with single-quoted ids, which the reader did not recognise. It now accepts both, so the remaining data, allowance and renewal date are read.

## 1.10.4
- Fix: the usage page address was built wrongly (its query string was encoded into the path), so the portal answered 404. Now the address is exact, as the portal's own script requests it.

## 1.10.3
- The usage requests now identify as a normal mobile browser (some portals answer only those) and an unreadable usage page reports a short, masked excerpt of the reply so the cause can be found.

## 1.10.2
- Fix: the usage page needs the portal's anti-forgery token, which the home page hands to its script as `Cyberoam.c$rFt0k3n`. UniLink now reads it from there, so the usage page loads.

## 1.10.1
- Fix: the data-usage reading found no table because the portal's home page is only a shell. UniLink now also loads the usage page (AccountStatus.jsp) the way the portal's own script does, sending the portal's anti-forgery token when the page provides one.
- A DNS miss right after Wi-Fi comes up is retried once. If the table still cannot be found, the error now lists non-sensitive diagnostics (page sizes, whether a token was found) so it can be traced.

## 1.10.0
- New: **data usage per account**. The Accounts tab shows a bar with the remaining data, the monthly allowance and the renewal date for each account, read from the university user portal (`gate1.urmia.ac.ir:8450`) with the same user name and password. Tap **Refresh data usage** (or the account's own refresh) when you want fresh numbers; nothing is fetched in the background.
- Safety: the user-portal certificate is pinned like the Wi-Fi portal (you approve its fingerprint once, and nothing is sent before that), the proxy is ignored, redirects are refused, and the account signs out of the user portal after each reading. Only the user name and password of that account are sent, and only to the configured portal address.
- Groups saved by older versions get the Urmia usage address automatically; other groups keep the feature off.

## 1.9.2
- Diagnostics: the "Signed in" and "Retry in" log lines now include how long the attempt took and how long after Wi-Fi was detected, so slow connections can be traced from a shared log.
- The connection timeout to the portal is 6 seconds instead of 10, so a first attempt made just as Wi-Fi comes up fails and retries sooner.

## 1.9.1
- New: if the phone's Wi-Fi is off when you tap **Connect**, UniLink opens the system Wi-Fi switch so one tap turns it on and the app can join campus Wi-Fi. The Connection tab also shows a "Wi-Fi is off" card with a Turn on Wi-Fi button. (Android 10 and newer do not allow apps to switch Wi-Fi on by themselves, so this is the closest permitted behaviour.)

## 1.9.0
- New: **Check for update** in Settings > About. It asks GitHub for the latest release of this project and tells you whether a newer version exists, with a button that opens the release page. It only runs when you tap it, sends no accounts or personal data, and never installs anything by itself.
- New: the author's name and a **GitHub project** button in the same panel.

## 1.8.1
- Fix: on the Activity tab the log card ran underneath the tab bar and showed a double outline. It now ends above the bar.
- Fix: scrolled text showed through the translucent tab bar on all three themes. The bar now has an opaque underlay.
- Content that scrolls under the status bar now fades out softly instead of being cut with a hard edge.

## 1.8.0
- **Midnight is now a true AMOLED theme**: pure black background, a deep blue glow at the top only, darker cards and a darker tab bar with a faint edge, and brighter borders on choice buttons.
- Accounts: round initials avatars, and the Default tag moves next to the account name so every card has the same height.
- Settings: Language and Appearance are full-width choice rows with centred labels.
- Networks: Delete is shown in red.
- Activity: the time is dim and monospaced, "Signed in" is green, drops and failures are amber, and entries are separated by a thin line.
- Light theme: a visible card border and a softer glow behind the status icon.
- The last item in a scrolling tab is no longer cut off by the floating tab bar. The Connection tab has a Wi-Fi icon, and Pause looks dimmer when you are not signed in.

## 1.7.1
- New: **swipe left or right** to change tabs. The tab bar follows your finger and the page under it shrinks and fades slightly.
- New: **Appearance** setting with System, Light, Dark and **Midnight**. Midnight is a deeper, near-black version of the dark look with a stronger blue and violet glow. The status bar icons follow the chosen look.
- Fix: the add (+) button sat a little higher on Accounts than on Networks; both now share the same position.

## 1.7.0
- **Smoother interface.** The floating navigation bar now slides: the selected tab expands to show its label with a spring, others shrink to an icon. Switching tabs slides and fades in the direction you are moving. The status badge morphs between icons, changes colour smoothly, pops when the state changes and shows a ring while connecting. Cards enter one after another, panels animate when their content changes, buttons shrink slightly under your finger and give a light haptic tick, list items animate when added, removed or moved, and Back returns to the first tab before closing the app (with the Android 13+ back preview).
- No new libraries; the animation code is original and uses only Jetpack Compose.
- Includes the 1.6.1 battery fix.

## 1.6.1
- **Battery fix.** The CPU wake lock and the Wi-Fi lock were held for as long as the service ran, even when the phone was not on campus Wi-Fi (for example at home, or while waiting for the network). On one test phone that meant a wake lock for about 12 h 42 m in a day and roughly 10% of the battery. The locks are now held only while the app is connecting, signed in, or retrying on campus Wi-Fi, and released otherwise.

## 1.6.0
- New: a **Network events** counter on the Connection tab. It counts the Wi-Fi and VPN callbacks the app receives, to help find out whether network chatter keeps the CPU awake (battery tuning).
- New: the activity log notes which XML fields the portal's login and keepalive replies contain (names only, never values or credentials), once per kind. This shows whether the portal reports data usage, which a usage bar would need.

## 1.5.0
- **Security: releases are now signed with a private key.** Until now releases were signed with a debug key that is public in the repository, which meant anyone could build an APK that Android would accept as an update. The new key lives only in GitHub Secrets and offline backups, and the release workflow refuses to publish an APK signed with the debug key. The signing certificate's SHA-256 is printed in each release's notes (see `SECURITY.md`).
- **Because the signing key changed, Android will not update 1.4.x in place.** Uninstall the old version and install 1.5.0, then add your accounts again. Future updates install normally.
- Added `SECURITY.md`.

## 1.4.1
- Fix: with a VPN on, sign-in and keepalives could stop after a while with "Binding socket to network … failed: EPERM". Some VPN setups make Android refuse a direct connection to the Wi-Fi. UniLink now retries once over the default route (safe, because the portal certificate is pinned and checked before any password is sent), says clearly what is wrong if that fails too, retries every minute at most, and retries immediately when a VPN starts or stops.
- Polish: language chips, the activity log in a glass panel, a clearer Pause button.

## 1.4.0
- New: glass-style interface with a soft gradient backdrop, frosted cards, a floating navigation bar and an animated status badge on the Connection tab. It follows the phone's light or dark theme.
- New: small home-screen widget showing the status with a Connect / Pause button (plain RemoteViews, no extra library, so the APK stays small).
- The project is now free software under **GPL-3.0-or-later** (see `LICENSE`) and carries F-Droid / IzzyOnDroid metadata in `fastlane/`.
- Build: the dependency-info blob is no longer embedded in the APK (required by F-Droid), and CI uploads the R8 `mapping.txt` as an artifact.

## 1.3.0
- New: battery tools. The old single lock is now two switches in Settings: **Keep CPU awake** and **Keep Wi-Fi at full power**, so you can find the lightest setting that still keeps your session alive.
- New: the Connection tab shows keepalive health (successful, failed, and late wake-ups), and the Activity log notes when a keepalive ran late because the phone was asleep.
- Smaller and faster: release builds now use R8 code shrinking, unused resources are removed, only English and Persian translations are bundled, and baseline profiles are installed for a faster first launch.
- The published APK is now the minified release build, signed with the same key as before, so it installs over earlier versions. An unminified debug APK is still produced as a fallback artifact.

## 1.2.0
- New: Persian (فارسی) interface with right-to-left layout. It follows the phone's language by default; change it in Settings → Language (System / English / فارسی).
- All screens, dialogs, status messages, the notification and the Quick Settings tile are translated. The Activity log stays in English on purpose so it is easy to share.

## 1.1.0
- New: first-run checklist on the Connection tab (account, location, notifications, battery, certificate). It hides itself when everything is ready.
- New: Quick Settings tile. Tap to connect, tap again to pause and sign out. Add it from the edit panel of Quick Settings.
- New: existing sessions are detected. If the portal already has a live session for one of your accounts (signed in by hand, or before the app restarted), UniLink keeps it alive instead of signing in again. Only the user name is sent for this check. The Connect button always signs in fresh.
- Fix: notification status now detects notifications blocked in system settings (the old check said "OK" even when they were blocked), and the Allow button opens the right settings page.

## 1.0.1
- Fix: Wi-Fi name (SSID) not detected on some phones, leaving the app stuck on "Waiting for campus Wi-Fi".
- Fallback to the portal when the SSID is hidden and only one network is configured.
- Activity log now shows the detected Wi-Fi and matched network.

## 1.0.0
- First Android debug release.
- Five tabs: Connection, Accounts, Networks, Activity, Settings.
- Wi-Fi-bound requests (VPN-safe), system proxy ignored, redirects refused.
- Portal certificate pinning with user approval.
- Multi-account rotation with monthly quota detection and automatic reset.
- Encrypted storage, foreground service, start on boot.
