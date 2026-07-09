import org.gradle.api.tasks.testing.logging.TestExceptionFormat
import java.io.File

plugins {
    id("com.android.application") version "8.7.3"
    kotlin("android") version "2.0.21"
}

group = "io.blazornative"
version = "0.1.0-SNAPSHOT"

dependencies {
    // JNA — JVM ↔ libwasmtime FFI binding.
    //
    // The :aar variant bundles libjnidispatch.so for Android ABIs (arm64-v8a,
    // x86_64) so the APK's lib/<abi>/ directory has JNA's native dispatch.
    // But the .aar does NOT include the desktop-JVM dispatch resources
    // (com/sun/jna/win32-x86-64/jnidispatch.dll etc.) needed for unit tests
    // to load JNA on the host JVM.
    //
    // Solution: scope each variant per classpath.
    //   - main classpath (compiled into APK): jna:.aar (Android dispatch)
    //   - testImplementation (JVM unit tests only): jna:.jar (desktop dispatch)
    implementation("net.java.dev.jna:jna:5.14.0@aar")
    testImplementation("net.java.dev.jna:jna:5.14.0")
    // jna-platform transitively pulls jna:.jar — exclude it so the :aar above
    // is the only JNA on the APK runtime classpath.
    implementation("net.java.dev.jna:jna-platform:5.14.0") {
        exclude(group = "net.java.dev.jna", module = "jna")
    }

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
    androidTestImplementation("androidx.test:core:1.6.1")
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

    kotlinOptions {
        jvmTarget = "17"
    }

    buildTypes {
        getByName("debug") {
            isMinifyEnabled = false
        }
    }

    testOptions {
        unitTests.isIncludeAndroidResources = false
    }

    packaging {
        // Both JNA artifacts (jna:.aar + jna-platform:.jar) ship the LGPL2.1
        // license text under META-INF — exclude duplicates from the APK.
        resources {
            excludes += setOf(
                "META-INF/LGPL2.1",
                "META-INF/AL2.0",
                "META-INF/*.kotlin_module"
            )
        }
    }
}

tasks.withType<Test>().configureEach {
    useJUnitPlatform()
    testLogging {
        events("passed", "skipped", "failed")
        exceptionFormat = TestExceptionFormat.FULL
        showStandardStreams = true
    }
    // JNA's library search path — the NativeAOT publish output for
    // BlazorNative.NativeHost.dll (host-JVM unit tests).
    systemProperty(
        "jna.library.path",
        rootProject.projectDir
            .resolve("../../src/BlazorNative.NativeHost/bin/Release/net10.0/win-x64/publish")
            .absolutePath
    )
}

// ─────────────────────────────────────────────────────────────────────────────
// Keep the per-ABI NativeAOT .so in sync with its publish output. Stale-asset
// bugs were the #1 risk in Phase 2.2 design — the preBuild dependency
// guarantees every APK assembly picks up the latest from `dotnet publish`.
// ─────────────────────────────────────────────────────────────────────────────

// Expected native build outputs — shared by verifyNativeAssets + the copy task.
val nativeHostPubRoot = rootProject.projectDir.resolve("../../src/BlazorNative.NativeHost/bin/Release/net10.0")
val nativeHostSoX64 = nativeHostPubRoot.resolve("linux-bionic-x64/publish/BlazorNative.NativeHost.so")
val nativeHostSoArm64 = nativeHostPubRoot.resolve("linux-bionic-arm64/publish/BlazorNative.NativeHost.so")

// Gate 3 review follow-up: a Copy task whose every `from` source is missing
// goes NO-SOURCE and skips ALL actions — including a doFirst fail-fast —
// silently producing an APK with stale/absent native assets. Verification
// therefore lives in a plain task (no inputs → never skipped) that every
// copy task depends on.
val verifyNativeAssets = tasks.register("verifyNativeAssets") {
    description = "Fails fast when an expected NativeAOT .so publish output is missing"
    group = "blazornative"
    doLast {
        val expected = mapOf(
            nativeHostSoX64 to "dotnet publish src/BlazorNative.NativeHost -c Release -r linux-bionic-x64",
            nativeHostSoArm64 to "dotnet publish src/BlazorNative.NativeHost -c Release -r linux-bionic-arm64",
        )
        val missing = expected.filterKeys { !it.exists() }
        if (missing.isNotEmpty()) {
            throw GradleException(
                "Native build outputs missing:\n" +
                    missing.entries.joinToString("\n") { (file, fix) ->
                        "  $file\n    → produce it via: $fix"
                    }
            )
        }
    }
}

// Phase 3.0c: NativeAOT .so per ABI → jniLibs. Renamed to lib-prefix so
// JNA's Native.load("BlazorNative.NativeHost") resolves on Android
// (dlopen expects lib<name>.so inside the APK's native-lib dir).
val copyNativeHostSo = tasks.register<Copy>("copyNativeHostSo") {
    description = "Copies NativeAOT BlazorNative.NativeHost.so (per ABI) into androidMain/jniLibs/"
    group = "blazornative"
    dependsOn(verifyNativeAssets)
    from(nativeHostSoX64) { into("x86_64") }
    from(nativeHostSoArm64) { into("arm64-v8a") }
    rename { "libBlazorNative.NativeHost.so" }
    into(layout.projectDirectory.dir("src/androidMain/jniLibs"))
}

// Wire the copy into preBuild so every APK build picks up the latest .so.
tasks.named("preBuild") {
    dependsOn(copyNativeHostSo)
}
