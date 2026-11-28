# Android upstream synchronization

## Source of truth

Android is developed by [@Mrzzan05](https://github.com/Mrzzan05) in [UniLink-Android](https://github.com/Mrzzan05/UniLink-Android). The unified repository keeps the integrated Android project in [src/UniLink.Android](../src/UniLink.Android).

| Item | Value |
| --- | --- |
| Imported release | [v1.14.1](https://github.com/Mrzzan05/UniLink-Android/releases/tag/v1.14.1) |
| Upstream source | [`c33b18e`](https://github.com/Mrzzan05/UniLink-Android/tree/c33b18e6415df6c2eeb7881cdcd2ec2bd60eb93b) |
| Previous upstream source | [`595fad3`](https://github.com/Mrzzan05/UniLink-Android/tree/595fad3c55253c10af0a75b7ba7cb179c046a3a3) |
| Android versionCode | 31 |
| Signed APK SHA-256 | `46423ae586492db2b99a29844bb903dbad0daf6999bc6ae59ef52b035d15b91a` |
| Signing certificate SHA-256 (upstream release notes) | `b009878e34f72deb8619011ab1a4dcdb4238cf18e70e7bb3b9512c33e018a175` |

The upstream 1.14.1 commit changes eight files: Gradle version, manifest, engine, widget, release notes, changelog, and the two Fastlane changelogs. All eight were imported without changing their upstream contents. The upstream tag points to the pinned commit above. The original signed APK is built from upstream source, not from the adapted monorepo source. Its exact corresponding GPL source is the upstream commit above.

## Local integration differences to preserve

- `.github/workflows/android.yml` builds the nested Gradle project.
- `UpdateCheck.kt`, `Parsers.kt` and `ParsersTest.kt` recognize the unified repository's stable `android-vX.Y.Z` release tags rather than desktop tags.
- Platform README files point users to the appropriate downloads. The Android GPL license and upstream credits are retained.

## Future updates

1. Compare upstream release commits and the integrated Android tree against the last imported revision.
2. Apply only upstream changes, resolving conflicts with the local adaptations above.
3. Build and test Android in the unified repository, keeping Windows and macOS source untouched.
4. Mirror the original signed APK (never re-sign with the public debug key), verify SHA-256 and signing certificate, and publish an Android release plus an updated combined bundle when appropriate. Link the exact upstream corresponding source in the release notes.
5. Update download links, version and this record. Do not silently overwrite an existing published release.
