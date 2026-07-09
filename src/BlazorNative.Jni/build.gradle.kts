import org.gradle.api.tasks.testing.logging.TestExceptionFormat
import java.io.File

plugins {
    id("com.android.application") version "8.7.3"
    kotlin("android") version "2.0.21"
    kotlin("plugin.serialization") version "2.0.21"
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

    // Phase 2.4: kotlinx.serialization for RenderFrame / RenderPatch wire format.
    // Sealed-class polymorphism keyed on "op" discriminator matches the .NET
    // [JsonPolymorphic(TypeDiscriminatorPropertyName = "op")] contract in
    // src/BlazorNative.Renderer/PatchProtocol.cs.
    implementation("org.jetbrains.kotlinx:kotlinx-serialization-json:1.7.3")

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
    // Path to the AOT'd .wasm — emitted by `dotnet publish -r wasi-wasm -c Release`
    systemProperty(
        "wasm.path",
        rootProject.projectDir
            .resolve("../../src/BlazorNative.WasiHost/bin/Release/net10.0/wasi-wasm/AppBundle/BlazorNative.WasiHost.wasm")
            .absolutePath
    )
    // JNA's library search path — where setup.ps1 copies wasmtime.dll, plus
    // the NativeAOT publish output for BlazorNative.NativeHost.dll (Phase 3.0b).
    // Both paths coexist through 3.0b; 3.0c's atomic cleanup removes wasmtime.
    systemProperty(
        "jna.library.path",
        listOf(
            // Existing: wasmtime — keeps BootSmokeTest (wasmtime path) green
            rootProject.projectDir.resolve("../../vendor/wasmtime"),
            // New: NativeAOT publish output (Phase 3.0b Gate 2)
            rootProject.projectDir.resolve(
                "../../src/BlazorNative.NativeHost/bin/Release/net10.0/win-x64/publish")
        ).joinToString(File.pathSeparator) { it.absolutePath }
    )
}

// ─────────────────────────────────────────────────────────────────────────────
// Phase 2.2: keep .wasm + per-ABI libwasmtime.so in sync with their build outputs.
// Stale-asset bugs were the #1 risk in Phase 2.2 design — preBuild dependency
// guarantees every APK assembly picks up the latest from `dotnet publish` and
// `cargo ndk build`.
// ─────────────────────────────────────────────────────────────────────────────

// Expected native build outputs — shared by verifyNativeAssets + the copy tasks.
val wasmSource = rootProject.projectDir
    .resolve("../../src/BlazorNative.WasiHost/bin/Release/net10.0/wasi-wasm/AppBundle/BlazorNative.WasiHost.wasm")
val wasmtimeJniLibsDir = rootProject.projectDir.resolve("../../vendor/wasmtime/jniLibs")
val nativeHostPubRoot = rootProject.projectDir.resolve("../../src/BlazorNative.NativeHost/bin/Release/net10.0")
val nativeHostSoX64 = nativeHostPubRoot.resolve("linux-bionic-x64/publish/BlazorNative.NativeHost.so")
val nativeHostSoArm64 = nativeHostPubRoot.resolve("linux-bionic-arm64/publish/BlazorNative.NativeHost.so")

// Gate 3 review follow-up: a Copy task whose every `from` source is missing
// goes NO-SOURCE and skips ALL actions — including a doFirst fail-fast —
// silently producing an APK with stale/absent native assets. Verification
// therefore lives in a plain task (no inputs → never skipped) that every
// copy task depends on.
val verifyNativeAssets = tasks.register("verifyNativeAssets") {
    description = "Fails fast when any expected native build output (.wasm, libwasmtime.so, NativeHost .so) is missing"
    group = "blazornative"
    doLast {
        val expected = mapOf(
            wasmSource to "dotnet publish src/BlazorNative.WasiHost -c Release -r wasi-wasm",
            wasmtimeJniLibsDir.resolve("arm64-v8a/libwasmtime.so") to "setup.ps1 section 8c (cargo-ndk cross-compile)",
            wasmtimeJniLibsDir.resolve("x86_64/libwasmtime.so") to "setup.ps1 section 8c (cargo-ndk cross-compile)",
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

val copyWasm = tasks.register<Copy>("copyWasm") {
    description = "Copies the AOT'd BlazorNative.WasiHost.wasm into androidMain/assets/"
    group = "blazornative"
    dependsOn(verifyNativeAssets)
    from(wasmSource)
    into(layout.projectDirectory.dir("src/androidMain/assets"))
}

val copyJniLibs = tasks.register<Copy>("copyJniLibs") {
    description = "Copies cross-compiled libwasmtime.so (per ABI) into androidMain/jniLibs/"
    group = "blazornative"
    dependsOn(verifyNativeAssets)
    from(wasmtimeJniLibsDir)
    into(layout.projectDirectory.dir("src/androidMain/jniLibs"))
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

// Wire all three into preBuild so every APK build picks up the latest assets.
tasks.named("preBuild") {
    dependsOn(copyWasm, copyJniLibs, copyNativeHostSo)
}
