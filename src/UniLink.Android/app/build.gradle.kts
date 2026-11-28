plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}

android {
    namespace = "ir.unilink.android"
    compileSdk = 35

    defaultConfig {
        applicationId = "ir.unilink.android"
        minSdk = 26
        targetSdk = 35
        versionCode = 31
        versionName = "1.14.1"

        // Drop translations of libraries we don't ship a UI for (the app itself is en + fa).
        @Suppress("DEPRECATION")
        resourceConfigurations += listOf("en", "fa")
    }

    // Release builds are signed with a PRIVATE key that is never stored in the repository; CI gets
    // it from GitHub Secrets (see SECURITY.md). The committed debug keystore is public, so it must
    // only ever sign debug builds: anyone could sign an APK with it.
    fun secret(name: String): String? = System.getenv(name)?.takeIf { it.isNotBlank() }
    val releaseKeystore = secret("UNILINK_KEYSTORE_FILE")

    signingConfigs {
        getByName("debug") {
            storeFile = file("debug.keystore")
            storePassword = "android"
            keyAlias = "androiddebugkey"
            keyPassword = "android"
        }
        if (releaseKeystore != null) {
            create("release") {
                storeFile = file(releaseKeystore)
                storePassword = secret("UNILINK_KEYSTORE_PASSWORD")
                keyAlias = secret("UNILINK_KEY_ALIAS")
                keyPassword = secret("UNILINK_KEY_PASSWORD")
            }
        }
    }

    buildTypes {
        release {
            // R8 shrinks and optimizes code, and unused resources are removed: smaller, faster APK.
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            // Private release key when provided (CI); otherwise the public debug key, for local
            // testing only. The release workflow refuses to publish a debug-signed APK.
            signingConfig = signingConfigs.findByName("release") ?: signingConfigs.getByName("debug")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions {
        jvmTarget = "17"
    }
    // Do not embed the Google-signed dependency blob: F-Droid rejects it and it makes builds less reproducible.
    dependenciesInfo {
        includeInApk = false
        includeInBundle = false
    }
    buildFeatures {
        compose = true
        buildConfig = true
    }
    packaging {
        resources.excludes += "/META-INF/{AL2.0,LGPL2.1}"
    }
}

dependencies {
    implementation(platform("androidx.compose:compose-bom:2024.09.03"))
    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.activity:activity-compose:1.9.2")
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.material:material-icons-core")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.8.1")
    implementation("com.squareup.okhttp3:okhttp:4.12.0")
    // Installs the baseline profiles that ship with Compose, so the first launch is faster.
    implementation("androidx.profileinstaller:profileinstaller:1.3.1")

    testImplementation(kotlin("test"))
}

// Run the pure-logic unit tests (portal/usage parsers, account ordering, version compare) before any
// APK is assembled, so a portal-format regression or a bad change fails the build instead of shipping.
tasks.matching { it.name == "assembleDebug" || it.name == "assembleRelease" }.configureEach {
    dependsOn("testDebugUnitTest")
}
