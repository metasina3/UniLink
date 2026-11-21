# UniLink

[Read in Persian](README_FA.md)

UniLink signs you in to university captive portals and keeps your session connected. It supports Windows, macOS, and Android. The default campus network is Urmia University's `UUWN`.

## Download

The current Windows executable, macOS installer, and Android APK are available in one release. [Download the release](https://github.com/metasina3/UniLink/releases/tag/bundle-2026.11.21)

| Platform | Version | Download | Guide |
| --- | --- | --- | --- |
| Windows 10 (2004+) and Windows 11, x64 | 1.3.1 | [UniLink.exe](https://github.com/metasina3/UniLink/releases/download/bundle-2026.11.21/UniLink.exe) | [Windows user guide (Persian)](docs/README_FA.html) |
| macOS 12+ | 1.3.1 | [DMG](https://github.com/metasina3/UniLink/releases/download/bundle-2026.11.21/UniLink-1.3.1-macOS.dmg) · [ZIP](https://github.com/metasina3/UniLink/releases/download/bundle-2026.11.21/UniLink-1.3.1-macOS.zip) | [macOS guide](docs/MACOS_GUIDE.md) |
| Android 8+ | 1.14.0 | [APK](https://github.com/metasina3/UniLink/releases/download/bundle-2026.11.21/UniLink-v1.14.0.apk) | [Android guide](src/UniLink.Android/README.md) |

- **Windows** — Run `UniLink.exe` directly. The standalone executable includes the .NET runtime. To update, exit the previous app before replacing its executable.
- **macOS** — Open the DMG and drag `UniLink.app` to Applications.
- **Android** — Open the APK to install it.

[All releases](https://github.com/metasina3/UniLink/releases) · [Earlier Android releases](https://github.com/Mrzzan05/UniLink-Android/releases)

## Source and builds

All editions live on `main`. Each has its own source, build, and release version.

| Edition | Source | Build from the repository root |
| --- | --- | --- |
| Windows | [Windows app](src/UniLink.Windows) · [.NET core](src/UniLink.Core) · [Tests](tests/UniLink.Tests) | `./build.ps1` with PowerShell and the .NET 8 SDK |
| macOS | [Swift app and Python tools](src/UniLink.macOS) · [Tests](tests/UniLink.macOS.Tests) | `./src/UniLink.macOS/build-swift.sh` with Xcode Command Line Tools |
| Android | [Gradle project](src/UniLink.Android) | Run `./gradlew assembleDebug` in `src/UniLink.Android` with JDK 17 and Android SDK 35 |

[Release instructions](docs/RELEASING.md) · [Protocol reference](docs/PROTOCOL.md) · [Windows network behavior](docs/NETWORK_BEHAVIOR.md)

## Contributors

| GitHub account | Contribution |
| --- | --- |
| [@metasina3](https://github.com/metasina3) | Windows development and repository maintenance |
| [Matin Sojoudi (@Matinsojoudi)](https://github.com/Matinsojoudi) | Native Swift app for macOS |
| [@Mrzzan05](https://github.com/Mrzzan05) | Android development |

## Licenses

Windows and macOS use the [MIT license](LICENSE). Android uses [GNU GPL v3 or later](src/UniLink.Android/LICENSE). Windows third-party notices are included in the executable and available in [notices](notices).

UniLink is an independent project. Use it with accounts you are authorized to use and follow your institution's network policy.
