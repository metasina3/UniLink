# Security

## Reporting a vulnerability
Please report security problems privately through GitHub: **Security → Report a vulnerability** on this repository. Don't post them in a public issue. You'll get a reply as soon as possible.

## How releases are signed
Every release APK is signed with a **private release key** that is kept only in the maintainer's GitHub Secrets and offline backups. It is never stored in this repository. The CI workflow refuses to publish a release that is unsigned or signed with the public debug key.

The signing certificate's SHA-256 fingerprint is printed at the bottom of each release's notes. To check a download:

```bash
apksigner verify --print-certs UniLink-vX.Y.Z.apk
```

The `certificate SHA-256 digest` must match the fingerprint in the release notes. If it doesn't, don't install it.

`app/debug.keystore` is a public debug key that only signs debug builds. An APK signed with it is **not** an official release.

## Your data
Accounts and settings are encrypted with a key held in the Android Keystore and never leave the phone. The only network traffic goes to the portal address you configured, over TLS with the portal's certificate pinned after you approve its fingerprint.
