# راهنمای ارسال به IzzyOnDroid و F-Droid

## پیش‌نیاز (یک‌بار)
1. نسخهٔ ۱٫۴٫۰ را منتشر کن و مطمئن شو در Releases هست و برنامه روی گوشی خوب کار می‌کند.
2. در ریشهٔ مخزن باید این‌ها باشد: `LICENSE`، پوشهٔ `fastlane/`، و Gradle Wrapper (`gradlew`، `gradle/wrapper/`).
   - اول ببین مخزنت wrapper دارد یا نه:
     `Get-Content gradle\wrapper\gradle-wrapper.properties`
   - اگر نبود، پوشهٔ `gradle-wrapper-8.9` را کپی کن (محتوایش را در ریشهٔ مخزن بگذار).
   - اگر بود و `distributionUrl` روی Gradle 8.9 نبود (مثلاً 9.x)، با نسخهٔ 8.9 جایگزینش کن، چون AGP 8.5.2 با Gradle 9 کار نمی‌کند.
3. دو تا اسکرین‌شات واقعی از برنامه بگیر (تب Connection و Settings) و اینجا بگذار:
   `fastlane/metadata/android/en-US/images/phoneScreenshots/1.png` و `2.png`

## IzzyOnDroid (سریع‌تر، اول این)
1. در https://gitlab.com/IzzyOnDroid/repo/-/issues یک Issue جدید با قالب «Inclusion request» باز کن.
2. متن `submissions/IzzyOnDroid-request.md` را بچسبان.
3. Izzy خودش APK را از GitHub Releases برمی‌دارد. معمولاً چند روز تا چند هفته طول می‌کشد.

## F-Droid (کندتر؛ از سورس می‌سازد)
1. مخزن https://gitlab.com/fdroid/fdroiddata را Fork کن.
2. فایل `submissions/ir.unilink.android.yml` را در شاخهٔ جدید به این مسیر ببر: `metadata/ir.unilink.android.yml`
3. Merge Request بساز. ربات‌ها ساخت را آزمایش می‌کنند و نگه‌دارنده‌ها ممکن است ایراد بگیرند. جوابشان را همان‌جا بده.
4. توجه: F-Droid با کلید خودش امضا می‌کند، پس APK گیت‌هاب و F-Droid روی هم نصب نمی‌شوند (باید حذف و نصب شود).
