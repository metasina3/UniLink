# UniLink R8 rules. The app has no reflection of its own; these only silence optional
# OkHttp platform classes that are absent on Android, and keep readable crash traces.
-keepattributes SourceFile,LineNumberTable
-renamesourcefileattribute SourceFile

-dontwarn org.conscrypt.**
-dontwarn org.bouncycastle.**
-dontwarn org.openjsse.**
-dontwarn okhttp3.internal.platform.**
