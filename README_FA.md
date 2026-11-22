<div dir="rtl">

# UniLink

[نسخهٔ انگلیسی](README.md)

یونی‌لینک به پرتال اینترنت دانشگاه وارد می‌شود و نشست را فعال نگه می‌دارد. برنامه برای ویندوز، مک و اندروید در دسترس است. شبکهٔ پیش‌فرض آن `UUWN` دانشگاه ارومیه است.

## دانلود

فایل اجرایی ویندوز، نصب‌کنندهٔ مک و فایل نصب اندروید در یک ریلیز قرار دارند. [دانلود نسخه‌های فعلی](https://github.com/metasina3/UniLink/releases/tag/bundle-2026.11.22)

| سیستم‌عامل | نسخه | دانلود | راهنما |
| --- | --- | --- | --- |
| ویندوز ۱۰ از نسخهٔ ۲۰۰۴ و ویندوز ۱۱، نسخهٔ x64 | ۱٫۴٫۰ | [UniLink.exe](https://github.com/metasina3/UniLink/releases/download/bundle-2026.11.22/UniLink.exe) | [راهنمای فارسی ویندوز](docs/README_FA.html) |
| macOS ۱۲ به بالا | ۱٫۳٫۱ | [DMG](https://github.com/metasina3/UniLink/releases/download/bundle-2026.11.22/UniLink-1.3.1-macOS.dmg) · [ZIP](https://github.com/metasina3/UniLink/releases/download/bundle-2026.11.22/UniLink-1.3.1-macOS.zip) | [راهنمای مک](docs/MACOS_GUIDE.md) |
| اندروید ۸ به بالا | ۱٫۱۴٫۰ | [APK](https://github.com/metasina3/UniLink/releases/download/bundle-2026.11.22/UniLink-v1.14.0.apk) | [راهنمای فارسی اندروید](src/UniLink.Android/README_FA.md) |

- **ویندوز** — فایل `UniLink.exe` را مستقیم اجرا کن. محیط اجرای ‎.NET داخل همین فایل است. برای به‌روزرسانی، ابتدا برنامهٔ قبلی را ببند و فایل اجرایی را جایگزین کن. حجم باقی‌ماندهٔ حساب متصل از کل سهمیه در Connection نمایش داده می‌شود. برای بررسی یک حساب در Accounts، دکمهٔ Check data را بزن.
- **مک** — فایل DMG را باز کن و `UniLink.app` را به پوشهٔ Applications بکش.
- **اندروید** — فایل APK را باز کن تا برنامه نصب شود.

[همهٔ ریلیزها](https://github.com/metasina3/UniLink/releases) · [آرشیو نسخه‌های قبلی اندروید](https://github.com/Mrzzan05/UniLink-Android/releases)

## سورس و ساخت برنامه

سورس هر سه نسخه روی شاخهٔ `main` قرار دارد. هر پلتفرم مسیر ساخت و شماره‌نسخهٔ خودش را دارد.

| نسخه | سورس | ساخت از پوشهٔ اصلی ریپو |
| --- | --- | --- |
| ویندوز | [برنامهٔ ویندوز](src/UniLink.Windows) · [هستهٔ ‎.NET](src/UniLink.Core) · [تست‌ها](tests/UniLink.Tests) | اجرای `./build.ps1` با PowerShell و ‎.NET 8 SDK |
| مک | [برنامهٔ Swift و ابزارهای Python](src/UniLink.macOS) · [تست‌ها](tests/UniLink.macOS.Tests) | اجرای `./src/UniLink.macOS/build-swift.sh` با ابزارهای خط فرمان Xcode |
| اندروید | [پروژهٔ Gradle](src/UniLink.Android) | اجرای `./gradlew assembleDebug` در `src/UniLink.Android` با JDK 17 و Android SDK 35 |

[راهنمای انتشار](docs/RELEASING.md) · [مستندات پروتکل](docs/PROTOCOL.md) · [رفتار شبکه در ویندوز](docs/NETWORK_BEHAVIOR.md)

## مشارکت‌کنندگان

| حساب گیت‌هاب | مشارکت |
| --- | --- |
| [@metasina3](https://github.com/metasina3) | توسعهٔ نسخهٔ ویندوز و نگه‌داری ریپو |
| [متین سجودی (@Matinsojoudi)](https://github.com/Matinsojoudi) | توسعهٔ نسخهٔ بومی مک با Swift |
| [مهران (@Mrzzan05)](https://github.com/Mrzzan05) | توسعهٔ نسخهٔ اندروید |

## مجوزها

ویندوز و مک با [مجوز MIT](LICENSE) و اندروید با [GNU GPL نسخهٔ ۳ یا بالاتر](src/UniLink.Android/LICENSE) منتشر می‌شوند. متن مجوزهای وابستگی‌های ویندوز داخل فایل اجرایی و در [پوشهٔ notices](notices) در دسترس است.

یونی‌لینک پروژه‌ای مستقل است. از حساب‌هایی استفاده کن که اجازهٔ استفاده از آن‌ها را داری و مقررات شبکهٔ دانشگاه را رعایت کن.

</div>
