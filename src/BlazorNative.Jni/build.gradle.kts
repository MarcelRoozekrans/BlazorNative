import org.gradle.api.tasks.testing.logging.TestExceptionFormat

plugins {
    id("com.android.application") version "8.7.3"
    kotlin("android") version "2.0.21"
}

group = "io.blazornative"
version = "0.1.0-SNAPSHOT"

dependencies {
    // JNA — JVM ↔ libwasmtime FFI binding. :aar variant bundles
    // libjnidispatch.so for all Android ABIs (arm64-v8a, x86_64, etc.) so the
    // APK's jniLibs section contains JNA's native dispatch. Same artifact works
    // for JVM unit tests since the .aar metadata exposes the same JVM classes.
    implementation("net.java.dev.jna:jna:5.14.0@aar")
    // jna-platform transitively pulls jna:.jar — exclude it so the :aar above
    // is the only JNA on the classpath (otherwise duplicate-class build error).
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

// ─────────────────────────────────────────────────────────────────────────────
// Phase 2.2: keep .wasm + per-ABI libwasmtime.so in sync with their build outputs.
// Stale-asset bugs were the #1 risk in Phase 2.2 design — preBuild dependency
// guarantees every APK assembly picks up the latest from `dotnet publish` and
// `cargo ndk build`.
// ─────────────────────────────────────────────────────────────────────────────

val copyWasm = tasks.register<Copy>("copyWasm") {
    description = "Copies the AOT'd BlazorNative.WasiHost.wasm into androidMain/assets/"
    group = "blazornative"
    val sourceWasm = rootProject.projectDir
        .resolve("../../src/BlazorNative.WasiHost/bin/Release/net10.0/wasi-wasm/AppBundle/BlazorNative.WasiHost.wasm")
    from(sourceWasm)
    into(layout.projectDirectory.dir("src/androidMain/assets"))
    doFirst {
        if (!sourceWasm.exists()) {
            throw GradleException(
                "BlazorNative.WasiHost.wasm not found at expected path:\n  $sourceWasm\n" +
                "Run from repo root: dotnet publish src/BlazorNative.WasiHost -c Release -r wasi-wasm"
            )
        }
    }
}

val copyJniLibs = tasks.register<Copy>("copyJniLibs") {
    description = "Copies cross-compiled libwasmtime.so (per ABI) into androidMain/jniLibs/"
    group = "blazornative"
    val sourceDir = rootProject.projectDir.resolve("../../vendor/wasmtime/jniLibs")
    from(sourceDir)
    into(layout.projectDirectory.dir("src/androidMain/jniLibs"))
    doFirst {
        val arm64 = sourceDir.resolve("arm64-v8a/libwasmtime.so")
        val x86_64 = sourceDir.resolve("x86_64/libwasmtime.so")
        if (!arm64.exists() || !x86_64.exists()) {
            throw GradleException(
                "libwasmtime.so missing for one or both ABIs under $sourceDir.\n" +
                "Run setup.ps1 section 8c (cargo-ndk cross-compile)."
            )
        }
    }
}

// Wire both into preBuild so every APK build picks up the latest assets.
tasks.named("preBuild") {
    dependsOn(copyWasm, copyJniLibs)
}
