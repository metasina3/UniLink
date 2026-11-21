# Publishing UniLink releases

Each platform uses its own version and tag prefix. Existing desktop releases retain their original tags.

| Platform | Tag | Version in source | Publish |
| --- | --- | --- | --- |
| Windows | `windows-vX.Y.Z` | `Directory.Build.props` → `Version` | Publish a GitHub Release on the tag. The Windows workflow attaches the ZIP, source, and SHA-256 list. |
| macOS | `macos-vX.Y.Z` | `src/UniLink.macOS/build-swift.sh` → `CFBundleShortVersionString` | Publish a GitHub Release on the tag. The macOS workflow attaches the DMG, ZIP, and SHA-256 files. |
| Android | `android-vX.Y.Z` | `src/UniLink.Android/app/build.gradle.kts` → `versionName` and `versionCode` | Push the tag. The Android workflow builds, signs, and publishes the APK and its SHA-256 file. |

Create each tag from the commit containing that platform's matching version. For Android, increment `versionCode` with each release. Update the platform's download links in the [root README](../README.md) once the release assets are available.

## Android signing

Use the original private Android release key so the APK can update existing installs. The maintainers configure `UNILINK_KEYSTORE_B64`, `UNILINK_KEYSTORE_PASSWORD`, `UNILINK_KEY_ALIAS`, and `UNILINK_KEY_PASSWORD` in the repository's Actions secrets. See the [Android signing notes](../src/UniLink.Android/SECURITY.md).

The initial Android release in this repository mirrors the upstream v1.14.0 APK and checksum. The [upstream release archive](https://github.com/Mrzzan05/UniLink-Android/releases) also serves existing APKs that check that repository for updates.
