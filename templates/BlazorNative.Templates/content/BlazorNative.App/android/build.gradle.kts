import org.gradle.api.tasks.testing.logging.TestExceptionFormat

plugins {
    id("com.android.application") version "9.3.0"
}

// AGP 9 ships built-in Kotlin and the org.jetbrains.kotlin.android plugin is gone, so the
// `kotlin("…")` helper no longer has a version injected for custom configurations — pin it
// explicitly to the KGP that AGP bundles.
val kotlinVersion = "2.2.10"

// ─────────────────────────────────────────────────────────────────────────────
// The Android shell for your BlazorNative app.
//
// WHAT THIS BUILDS: an APK that loads your app's NativeAOT library
// (libBlazorNative.Runtime.so) through JNA, renders its frames into real Android
// widgets (WidgetMapper) and lays them out with Yoga.
//
// THE ONE PREREQUISITE: the .so must exist. `verifyNativeAssets` below fails fast
// and tells you the exact publish command when it does not — so from a clean
// clone the order is always:
//
//     dotnet publish .. -c Release -r linux-bionic-x64      (the emulator's ABI)
//     dotnet publish .. -c Release -r linux-bionic-arm64    (real devices)
//     ./gradlew assembleDebug
//
// EVERY VERSION HERE IS PINNED, DELIBERATELY. A floating version would break your
// app unpredictably — and Yoga in particular is ONE ENGINE shared with the iOS
// shell: two different Yoga versions lay out differently, silently. If you bump
// Yoga, bump it everywhere.
//
// THE SHELL SOURCES UNDER src/ ARE LIBRARY CODE. They are byte-identical copies of
// the BlazorNative reference shell (which is why they stay in the
// io.blazornative.shell package while YOUR app id is whatever you chose — AGP's
// `namespace` and `applicationId` are separate identities from a source package).
// You are not meant to edit them; the day the shell ships as an .aar they become a
// deletion.
// ─────────────────────────────────────────────────────────────────────────────

dependencies {
    // JNA — JVM ↔ NativeAOT runtime FFI binding.
    //
    // The :aar variant bundles libjnidispatch.so for Android ABIs (arm64-v8a,
    // x86_64) so the APK's lib/<abi>/ directory has JNA's native dispatch.
    // jna-platform transitively pulls jna:.jar — exclude it so the :aar is the
    // only JNA on the APK runtime classpath.
    implementation("net.java.dev.jna:jna:5.19.1@aar")
    implementation("net.java.dev.jna:jna-platform:5.19.1") {
        exclude(group = "net.java.dev.jna", module = "jna")
    }

    // Yoga — the flexbox engine. The prebuilt JNI bindings React Native Android
    // uses (YogaNode Java API over the C++ core); libyoga.so ships in the APK
    // alongside libBlazorNative.Runtime.so. THE SAME ENGINE VERSION the iOS shell
    // builds from source: identical frames on both platforms is the whole reason
    // for choosing Yoga, and two versions would erode that silently.
    implementation("com.facebook.yoga:yoga:3.2.1")

    // Yoga declares soloader (its native-lib loader) at RUNTIME scope, so it ships
    // in the APK but is OFF the compile classpath. The shell itself calls
    // SoLoader.init(context) before the first YogaNode (YogaLayout / MainActivity),
    // so it needs it on the MAIN compile classpath too — pinned to the version yoga
    // 3.2.1 resolves, so compile and runtime agree.
    implementation("com.facebook.soloader:soloader:0.12.1")

    // Coil — the image loader behind BnImage (fetch, decode, downsampling,
    // cancellation, caching). Coil 2.x, not 3.x: 3.x is the coil3/multiplatform
    // rewrite with a different package and a different ImageLoader surface.
    implementation("io.coil-kt:coil:2.7.0")

    // AndroidX Biometric — the Jetpack BiometricPrompt and the CryptoObject plumbing
    // the shell's biometrics + OS-key-bound secure storage use (AndroidShellBridge).
    // The shell references BiometricPrompt, so this dependency is REQUIRED to compile
    // the generated app; it must stay pinned to the same version the reference shell
    // builds against. It transitively supplies androidx.fragment (MainActivity extends
    // FragmentActivity, BiometricPrompt's required host).
    implementation("androidx.biometric:biometric:1.1.0")

    // Kotlin stdlib
    implementation(kotlin("stdlib-jdk8", kotlinVersion))
}

android {
    // AGP's `namespace` (the R/BuildConfig package) and `applicationId` (your app's
    // identity on the device) are BOTH yours — and neither has to match a source
    // package, which is why the shell's Kotlin can stay in io.blazornative.shell.
    namespace = "com.example.starterapp"
    compileSdk = 34

    defaultConfig {
        applicationId = "com.example.starterapp"
        // minSdk 24 is a real floor, not a preference: the NDK and Coil assumptions
        // below it do not hold. Raising it is a one-line edit and yours to make.
        minSdk = 24
        targetSdk = 34
        versionCode = 1
        versionName = "1.0"

        ndk {
            // Both ABIs: arm64-v8a for devices, x86_64 for the emulator. Each needs
            // its own `dotnet publish -r linux-bionic-<abi>`; verifyNativeAssets
            // below says so when one is missing. Narrow this list if you only ever
            // ship one — but then JNA's own arm64 libjnidispatch.so (from the :aar)
            // would pair with a missing arm64 runtime .so, so narrow the ABI, not
            // the publish.
            abiFilters += listOf("arm64-v8a", "x86_64")
        }
    }

    sourceSets {
        getByName("main") {
            java.srcDirs("src/main/kotlin", "src/androidMain/kotlin")
            // LOAD-BEARING, and the reason is a real incident: AGP 9's built-in
            // Kotlin does NOT feed `java.srcDirs` to the KOTLIN compiler — it only
            // picks up its own defaults (`src/<name>/kotlin`, `src/<name>/java`).
            // `src/androidMain/kotlin` is a NON-DEFAULT name, so without this line
            // the whole shell — MainActivity, WidgetMapper, YogaLayout — silently
            // STOPS BEING COMPILED. The app still "builds" (nothing references
            // MainActivity at compile time; only the manifest names it). Declare it
            // for Kotlin explicitly:
            kotlin.srcDirs("src/main/kotlin", "src/androidMain/kotlin")
            manifest.srcFile("src/androidMain/AndroidManifest.xml")
            res.srcDirs("src/androidMain/res")
            assets.srcDirs("src/androidMain/assets")
            jniLibs.srcDirs("src/androidMain/jniLibs")
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

    packaging {
        // Both JNA artifacts ship the LGPL2.1 license text under META-INF —
        // exclude duplicates from the APK.
        resources {
            excludes += setOf(
                "META-INF/LGPL2.1",
                "META-INF/AL2.0",
                "META-INF/*.kotlin_module"
            )
        }
    }
}

// Kotlin JVM target. Set through the `compilerOptions` DSL rather than the android
// `kotlinOptions { jvmTarget = "17" }` block: Kotlin 2.4 turned the String-typed
// `jvmTarget` setter into a hard error, so the enum is the only accepted form.
kotlin {
    compilerOptions {
        jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17)
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// WHERE YOUR .NET PUBLISH OUTPUT LIVES.
//
// This gradle root is <YourApp>/android/, so the app — and its bin/ — is the
// PARENT directory. `-PappPubRoot=<dir>` overrides it for the cases where the
// publish tree is somewhere else (CI hands the artifact over between jobs; a
// monorepo puts the app elsewhere). Absolute paths pass through resolve()
// unchanged.
// ─────────────────────────────────────────────────────────────────────────────
val appPubRoot: File = (findProperty("appPubRoot") as String?)
    ?.let { rootProject.projectDir.resolve(it) }
    ?: rootProject.projectDir.resolve("../bin/Release/net10.0")

// JNA's library search path for host-JVM tests — your app's win-x64 publish
// output, where BlazorNative.Runtime.dll lands. (No tests ship with this
// template; this is wired so the ones you add can load the runtime.)
val winX64PublishPath: String = appPubRoot.resolve("win-x64/publish").absolutePath

tasks.withType<Test>().configureEach {
    useJUnitPlatform()
    testLogging {
        events("passed", "skipped", "failed")
        exceptionFormat = TestExceptionFormat.FULL
        showStandardStreams = true
    }
    systemProperty("jna.library.path", winX64PublishPath)
}

// ─────────────────────────────────────────────────────────────────────────────
// Keep the per-ABI NativeAOT .so in sync with its publish output. The preBuild
// dependency guarantees every APK assembly picks up the latest `dotnet publish`
// — stale native assets are otherwise a genuinely confusing class of bug.
// ─────────────────────────────────────────────────────────────────────────────

val runtimeSoX64 = appPubRoot.resolve("linux-bionic-x64/publish/BlazorNative.Runtime.so")
val runtimeSoArm64 = appPubRoot.resolve("linux-bionic-arm64/publish/BlazorNative.Runtime.so")

// A Copy task whose every `from` source is missing goes NO-SOURCE and skips ALL
// actions — including a doFirst fail-fast — silently producing an APK with
// stale/absent native assets. Verification therefore lives in a plain task (no
// inputs → never skipped) that the copy depends on.
val verifyNativeAssets = tasks.register("verifyNativeAssets") {
    description = "Fails fast when an expected NativeAOT .so publish output is missing"
    group = "blazornative"
    doLast {
        val expected = mapOf(
            runtimeSoX64 to "dotnet publish .. -c Release -r linux-bionic-x64",
            runtimeSoArm64 to "dotnet publish .. -c Release -r linux-bionic-arm64",
        )
        val missing = expected.filterKeys { !it.exists() }
        if (missing.isNotEmpty()) {
            throw GradleException(
                "Native build outputs missing:\n" +
                    missing.entries.joinToString("\n") { (file, fix) ->
                        "  $file\n    → produce it via: $fix"
                    } +
                    "\n\n(Publishing from a different directory? Point gradle at it with " +
                    "-PappPubRoot=<path to bin/Release/net10.0>.)"
            )
        }
    }
}

// The NativeAOT .so per ABI → jniLibs. Renamed to lib-prefix so JNA's
// Native.load("BlazorNative.Runtime") resolves on Android (dlopen expects
// lib<name>.so inside the APK's native-lib dir).
val copyRuntimeSo = tasks.register<Copy>("copyRuntimeSo") {
    description = "Copies NativeAOT BlazorNative.Runtime.so (per ABI) into androidMain/jniLibs/"
    group = "blazornative"
    dependsOn(verifyNativeAssets)
    from(runtimeSoX64) { into("x86_64") }
    from(runtimeSoArm64) { into("arm64-v8a") }
    rename { "libBlazorNative.Runtime.so" }
    into(layout.projectDirectory.dir("src/androidMain/jniLibs"))
}

tasks.named("preBuild") {
    dependsOn(copyRuntimeSo)
}
