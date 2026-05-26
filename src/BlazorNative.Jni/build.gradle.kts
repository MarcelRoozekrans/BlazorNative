import org.gradle.api.tasks.testing.logging.TestExceptionFormat

plugins {
    id("com.android.application") version "8.7.3"
    kotlin("android") version "2.0.21"
}

group = "io.blazornative"
version = "0.1.0-SNAPSHOT"

dependencies {
    // JNA — JVM ↔ libwasmtime FFI binding
    implementation("net.java.dev.jna:jna:5.14.0")
    implementation("net.java.dev.jna:jna-platform:5.14.0")

    // Kotlin stdlib
    implementation(kotlin("stdlib-jdk8"))

    // JVM unit tests (Phase 2.1)
    testImplementation("org.junit.jupiter:junit-jupiter-api:5.11.3")
    testImplementation("org.junit.jupiter:junit-jupiter-params:5.11.3")
    testRuntimeOnly("org.junit.jupiter:junit-jupiter-engine:5.11.3")
    testRuntimeOnly("org.junit.platform:junit-platform-launcher:1.11.3")

    // Android instrumented tests (Phase 2.2 Task 7 fills in)
    androidTestImplementation("androidx.test.ext:junit:1.2.1")
    androidTestImplementation("androidx.test:runner:1.6.2")
    androidTestImplementation("androidx.test:rules:1.6.1")
}

android {
    namespace = "io.blazornative.shell"
    compileSdk = 34

    defaultConfig {
        applicationId = "io.blazornative.shell"
        minSdk = 24
        targetSdk = 34
        versionCode = 1
        versionName = "0.1.0-phase-2.2"

        ndk {
            abiFilters += listOf("arm64-v8a", "x86_64")
        }

        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }

    sourceSets {
        getByName("main") {
            // Shared Kotlin sources from Phase 2.1 stay in src/main/kotlin
            java.srcDirs("src/main/kotlin", "src/androidMain/kotlin")
            manifest.srcFile("src/androidMain/AndroidManifest.xml")
            res.srcDirs("src/androidMain/res")
            assets.srcDirs("src/androidMain/assets")
            jniLibs.srcDirs("src/androidMain/jniLibs")
        }
        getByName("androidTest") {
            java.srcDirs("src/androidTest/kotlin")
        }
        getByName("test") {
            java.srcDirs("src/test/kotlin")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    buildTypes {
        getByName("debug") {
            isMinifyEnabled = false
        }
    }

    testOptions {
        unitTests.isIncludeAndroidResources = false
    }
}

tasks.withType<Test>().configureEach {
    useJUnitPlatform()
    testLogging {
        events("passed", "skipped", "failed")
        exceptionFormat = TestExceptionFormat.FULL
        showStandardStreams = true
    }
    // Path to the AOT'd .wasm — emitted by `dotnet publish -r wasi-wasm -c Release`
    systemProperty(
        "wasm.path",
        rootProject.projectDir
            .resolve("../../src/BlazorNative.WasiHost/bin/Release/net10.0/wasi-wasm/AppBundle/BlazorNative.WasiHost.wasm")
            .absolutePath
    )
    // JNA's library search path — where setup.ps1 copies wasmtime.dll
    systemProperty(
        "jna.library.path",
        rootProject.projectDir
            .resolve("../../vendor/wasmtime")
            .absolutePath
    )
}
