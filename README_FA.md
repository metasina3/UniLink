<div align="center">
<img src="src/UniLink.Windows/app.svg" width="92" alt="UniLink">

# UniLink | یونی‌لینک

**اتصال خودکار به اینترنت دانشگاه، در سه سیستم‌عامل**

[English](README.md) · [مستندات](docs/README.md) · [نسخه‌های منتشرشده](https://github.com/metasina3/UniLink/releases)

</div>

<div dir="rtl">

یونی‌لینک به پرتال اینترنت دانشگاه وارد می‌شود و نشست را فعال نگه می‌دارد. برنامه برای ویندوز، مک و اندروید در دسترس است. شبکهٔ پیش‌فرض آن `UUWN` دانشگاه ارومیه است.

## دانلود

سیستم‌عامل خود را انتخاب کنید. [آخرین بستهٔ جامع](https://github.com/metasina3/UniLink/releases/latest) و [همهٔ ریلیزها](https://github.com/metasina3/UniLink/releases) در دسترس هستند. نسخهٔ ۱٫۱۴٫۱ اندروید از ریپوی توسعه‌دهنده نیز قابل دریافت است.

| سیستم‌عامل | نسخه | دانلود | راهنما |
| --- | --- | --- | --- |
| ویندوز ۱۰ از نسخهٔ ۲۰۰۴ و ویندوز ۱۱، نسخهٔ x64 | ۱٫۴٫۰ | [UniLink.exe](https://github.com/metasina3/UniLink/releases/download/bundle-2026.11.22/UniLink.exe) | [راهنمای فارسی ویندوز](docs/README_FA.html) |
| macOS ۱۲ به بالا | ۱٫۳٫۱ | [DMG](https://github.com/metasina3/UniLink/releases/download/bundle-2026.11.22/UniLink-1.3.1-macOS.dmg) · [ZIP](https://github.com/metasina3/UniLink/releases/download/bundle-2026.11.22/UniLink-1.3.1-macOS.zip) | [راهنمای مک](docs/MACOS_GUIDE.md) |
| اندروید ۸ به بالا | ۱٫۱۴٫۱ | [APK](https://github.com/Mrzzan05/UniLink-Android/releases/download/v1.14.1/UniLink-v1.14.1.apk) | [راهنمای فارسی اندروید](src/UniLink.Android/README_FA.md) |

- **ویندوز** — فایل `UniLink.exe` را مستقیم اجرا کن. محیط اجرای ‎.NET داخل همین فایل است. برای به‌روزرسانی، ابتدا برنامهٔ قبلی را ببند و فایل اجرایی را جایگزین کن. حجم باقی‌ماندهٔ حساب متصل از کل سهمیه در Connection نمایش داده می‌شود. برای بررسی یک حساب در Accounts، دکمهٔ Check data را بزن.
- **مک** — فایل DMG را باز کن و `UniLink.app` را به پوشهٔ Applications بکش.
- **اندروید** — فایل APK امضاشده را برای نصب یا به‌روزرسانی باز کنید. در نسخهٔ ۱٫۱۴٫۱ دکمهٔ اتصال/توقف ویجت اصلاح شده است. [توضیحات نسخه](https://github.com/Mrzzan05/UniLink-Android/releases/tag/v1.14.1).

[همهٔ ریلیزها](https://github.com/metasina3/UniLink/releases) · [آرشیو نسخه‌های قبلی اندروید](https://github.com/Mrzzan05/UniLink-Android/releases)

## قابلیت‌های اصلی

- ورود خودکار به پرتال دانشگاه و فعال نگه‌داشتن نشست.
- مدیریت چند حساب و جابه‌جایی خودکار در نسخه‌های پشتیبانی‌شده.
- مسیریابی از رابط فیزیکی شبکه و بررسی گواهی پرتال، طبق مستندات هر نسخه.
- نمایش حجم باقی‌مانده در ویندوز و اندروید.

## سورس و ساخت برنامه

سورس هر سه نسخه روی شاخهٔ `main` قرار دارد. هر پلتفرم مسیر ساخت و شماره‌نسخهٔ خودش را دارد.

| نسخه | سورس | ساخت از پوشهٔ اصلی ریپو |
| --- | --- | --- |
| ویندوز | [برنامهٔ ویندوز](src/UniLink.Windows) · [هستهٔ ‎.NET](src/UniLink.Core) · [تست‌ها](tests/UniLink.Tests) | اجرای `./build.ps1` با PowerShell و ‎.NET 8 SDK |
| مک | [برنامهٔ Swift و ابزارهای Python](src/UniLink.macOS) · [تست‌ها](tests/UniLink.macOS.Tests) | اجرای `./src/UniLink.macOS/build-swift.sh` با ابزارهای خط فرمان Xcode |
| اندروید | [پروژهٔ Gradle](src/UniLink.Android) | اجرای `./gradlew assembleDebug` در `src/UniLink.Android` با JDK 17 و Android SDK 35 |

[راهنمای انتشار](docs/RELEASING.md) · [مستندات پروتکل](docs/PROTOCOL.md) · [رفتار شبکه در ویندوز](docs/NETWORK_BEHAVIOR.md)

## مستندات پروژه

| مستند | کاربرد |
| --- | --- |
| [فهرست مستندات](docs/README.md) | راهنماها، پروتکل، تست و انتشار |
| [تغییرات ویندوز](docs/CHANGELOG.md) | تاریخچهٔ نسخه‌های دسکتاپ |
| [تغییرات اندروید](src/UniLink.Android/CHANGELOG.md) | تاریخچهٔ نسخه‌های اندروید |
| [همگام‌سازی اندروید](docs/ANDROID_SYNC.md) | مرجع سورس و فایل نصب امضاشده |
| [Issues](https://github.com/metasina3/UniLink/issues) | گزارش خطا و پیشنهاد قابلیت |

## مشارکت‌کنندگان

| حساب گیت‌هاب | مشارکت |
| --- | --- |
| [@metasina3](https://github.com/metasina3) | توسعهٔ نسخهٔ ویندوز و نگه‌داری ریپو |
| [متین سجودی (@Matinsojoudi)](https://github.com/Matinsojoudi) | توسعهٔ نسخهٔ بومی مک با Swift |
| [@Mrzzan05](https://github.com/Mrzzan05) | توسعهٔ نسخهٔ اندروید |

## مجوزها

ویندوز و مک با [مجوز MIT](LICENSE) و اندروید با [GNU GPL نسخهٔ ۳ یا بالاتر](src/UniLink.Android/LICENSE) منتشر می‌شوند. متن مجوزهای وابستگی‌های ویندوز داخل فایل اجرایی و در [پوشهٔ notices](notices) در دسترس است.

یونی‌لینک پروژه‌ای مستقل است. از حساب‌هایی استفاده کن که اجازهٔ استفاده از آن‌ها را داری و مقررات شبکهٔ دانشگاه را رعایت کن.

</div>
