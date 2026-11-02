plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}

android {
    namespace = "com.kwartotv.remote"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.kwartotv.remote"
        minSdk = 26
        targetSdk = 35
        versionCode = (System.getenv("KWARTO_VERSION_CODE") ?: "1").toInt()
        versionName = System.getenv("KWARTO_VERSION_NAME") ?: "0.0.0-dev"
    }

    signingConfigs {
        // A fixed sideload key, so every build installs over the previous one. Not a store-signing key.
        create("sideload") {
            storeFile = rootProject.file("keystore/kwartotv-sideload.jks")
            storePassword = "kwartotv-sideload"
            keyAlias = "kwartotv"
            keyPassword = "kwartotv-sideload"
        }
    }

    buildTypes {
        release {
            // Drops the unused parts of libraries (the icon set is large); names are kept (see proguard-rules.pro).
            isMinifyEnabled = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            signingConfig = signingConfigs.getByName("sideload")
        }
        debug {
            signingConfig = signingConfigs.getByName("sideload")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }
    buildFeatures { compose = true }
    lint { abortOnError = false; checkReleaseBuilds = true }
    testOptions {
        unitTests.isReturnDefaultValues = true
        // The phone UI tests (Robolectric): every button pressed, every screen drawn to a picture.
        unitTests.isIncludeAndroidResources = true
        unitTests.all {
            it.testLogging { showStandardStreams = true; events("passed", "failed", "started"); exceptionFormat = org.gradle.api.tasks.testing.logging.TestExceptionFormat.FULL }; it.maxHeapSize = "2g"
            // Robolectric downloads its Android jar itself; repo1 answers CI machines with 429 (too many requests).
            it.systemProperty("robolectric.dependency.repo.url", "https://repo.maven.apache.org/maven2")
        }
    }
}

dependencies {
    implementation("androidx.core:core-ktx:1.15.0")
    implementation("androidx.activity:activity-compose:1.10.1")
    implementation("androidx.lifecycle:lifecycle-runtime-compose:2.8.7")
    implementation("androidx.lifecycle:lifecycle-viewmodel-compose:2.8.7")
    implementation(platform("androidx.compose:compose-bom:2025.04.01"))
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.foundation:foundation")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.material:material-icons-extended")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.9.0")
    implementation("com.squareup.okhttp3:okhttp:4.12.0")
    implementation("com.journeyapps:zxing-android-embedded:4.3.0")

    testImplementation("junit:junit:4.13.2")
    testImplementation("org.robolectric:robolectric:4.14.1")
    testImplementation("androidx.compose.ui:ui-test-junit4")
    testImplementation("androidx.test:core:1.6.1")
    testImplementation("androidx.test.ext:junit:1.2.1")
    debugImplementation("androidx.compose.ui:ui-test-manifest")
    testImplementation("org.json:json:20240303")
}
