Title: Inclusion request: UniLink (ir.unilink.android)

**App name:** UniLink
**Package:** ir.unilink.android
**Source code:** https://github.com/Mrzzan05/UniLink-Android
**License:** GPL-3.0-or-later
**Releases (APK):** https://github.com/Mrzzan05/UniLink-Android/releases
**Metadata:** fastlane (`fastlane/metadata/android`, en-US and fa)

**What it does:** signs in to Sophos / Cyberoam captive portals (built for a university Wi-Fi) and keeps the session alive from a foreground service. Portal traffic is bound to the Wi-Fi network so it works with a VPN on; the portal's self-signed certificate is pinned after the user approves its fingerprint.

**Anti-features / FOSS check:** no ads, no trackers, no analytics, no Google Play Services, no proprietary libraries. Dependencies: AndroidX (Compose, Core, Activity, ProfileInstaller), OkHttp, kotlinx-coroutines. The only network traffic is to the portal URL the user configures.

**Permissions:** INTERNET, ACCESS_NETWORK_STATE, ACCESS_WIFI_STATE, ACCESS_FINE_LOCATION (only to read the Wi-Fi name, which Android hides otherwise; never stored or sent), FOREGROUND_SERVICE(+SPECIAL_USE), POST_NOTIFICATIONS, RECEIVE_BOOT_COMPLETED, WAKE_LOCK, REQUEST_IGNORE_BATTERY_OPTIMIZATIONS.

**Languages:** English, Persian (RTL).
**Signing:** release APKs on GitHub are signed with a fixed key so updates install over older builds.

Thanks for considering it!
