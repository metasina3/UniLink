# UniLink

UniLink keeps university captive-portal sessions connected on Windows, macOS, and Android. The default campus network is `UUWN` at Urmia University.

## Downloads · دانلود

| Platform | Release | Installer | Guide |
| --- | --- | --- | --- |
| Windows 10/11 x64 | [v1.2.1](https://github.com/metasina3/UniLink/releases/tag/v1.2.1) | [Windows ZIP](https://github.com/metasina3/UniLink/releases/download/v1.2.1/UniLink-1.2.1-Windows-x64.zip) | [راهنمای فارسی](docs/README_FA.html) |
| macOS 12+ | [v1.3.1](https://github.com/metasina3/UniLink/releases/tag/v1.3.1) | [macOS DMG](https://github.com/metasina3/UniLink/releases/download/v1.3.1/UniLink-1.3.1-macOS.dmg) | [macOS guide](docs/MACOS_GUIDE.md) |
| Android 8+ | [v1.14.0](https://github.com/metasina3/UniLink/releases/tag/android-v1.14.0) | [Android APK](https://github.com/metasina3/UniLink/releases/download/android-v1.14.0/UniLink-v1.14.0.apk) | [راهنمای فارسی](src/UniLink.Android/README_FA.md) · [English](src/UniLink.Android/README.md) |

Extract the Windows ZIP and run `UniLink.exe`. The ZIP includes the .NET runtime. On macOS, open the DMG and drag `UniLink.app` to Applications. On Android, open the APK to install.

[All UniLink releases](https://github.com/metasina3/UniLink/releases) · [Earlier Android releases](https://github.com/Mrzzan05/UniLink-Android/releases)

## Source and builds

All editions live on `main`. Each platform has its own source, build, and release version.

| Edition | Source | Build from the repository root |
| --- | --- | --- |
| Windows | [Windows app](src/UniLink.Windows), [.NET core](src/UniLink.Core), [tests](tests/UniLink.Tests) | `./build.ps1` with PowerShell and the .NET 8 SDK |
| macOS | [Swift app and Python tools](src/UniLink.macOS), [tests](tests/UniLink.macOS.Tests) | `./src/UniLink.macOS/build-swift.sh` with Xcode Command Line Tools |
| Android | [Gradle project](src/UniLink.Android) | `cd src/UniLink.Android` and `./gradlew assembleDebug` with JDK 17 and Android SDK 35 |

See [release instructions](docs/RELEASING.md) for platform tags and signing. See the [protocol reference](docs/PROTOCOL.md) and [Windows network behavior](docs/NETWORK_BEHAVIOR.md) for implementation details.

## Contributors

| Edition | Developer |
| --- | --- |
| Windows | [@metasina3](https://github.com/metasina3) |
| macOS | [Matin Sojoudi (@Matinsojoudi)](https://github.com/Matinsojoudi) |
| Android | [@Mrzzan05](https://github.com/Mrzzan05) |

## Licenses

Windows and macOS use the [MIT license](LICENSE). The Android project uses [GNU GPL v3 or later](src/UniLink.Android/LICENSE). See [notices](notices) for Windows third-party notices.

UniLink is an independent project. Use it with accounts you are authorized to use and follow your institution's network policy.
