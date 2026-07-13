import org.gradle.api.tasks.testing.logging.TestExceptionFormat

plugins {
    id("com.android.application") version "8.7.3"
    kotlin("android") version "2.0.21"
}

group = "io.blazornative"
version = "0.1.0-SNAPSHOT"

// ─────────────────────────────────────────────────────────────────────────────
// Phase 4.0 Gate 3: `-PciSoDir=<dir>` — CI override for the NativeAOT .so
// source. The nightly instrumented workflow publishes linux-bionic-x64 on a
// Windows job and runs the emulator suite on an ubuntu job that has no .NET
// publish tree; ciSoDir points the verify/copy chain at the downloaded
// artifact directory (must contain BlazorNative.Runtime.so).
//
// CI shape (deliberate): the workflow ships ONLY the x86_64 .so and the CI
// emulator is x86_64-only, so with ciSoDir set just the x86_64 ABI is staged
// and verified — and abiFilters narrows to x86_64 so the APK cannot pair
// JNA's arm64-v8a libjnidispatch.so (from the :aar) with a missing arm64
// runtime .so. With the property unset, behavior is byte-identical to the
// local flow: both bionic publishes are verified and staged per ABI.
// ─────────────────────────────────────────────────────────────────────────────
val ciSoDir: File? = (findProperty("ciSoDir") as String?)
    ?.let { rootProject.projectDir.resolve(it) } // absolute paths pass through resolve() unchanged

dependencies {
    // JNA — JVM ↔ NativeAOT runtime FFI binding.
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
    implementation("net.java.dev.jna:jna:5.19.1@aar")
    testImplementation("net.java.dev.jna:jna:5.19.1")
    // jna-platform transitively pulls jna:.jar — exclude it so the :aar above
    // is the only JNA on the APK runtime classpath.
    implementation("net.java.dev.jna:jna-platform:5.19.1") {
        exclude(group = "net.java.dev.jna", module = "jna")
    }

    // Phase 6.0 Yoga spike (M6): Facebook's Yoga flexbox engine — the prebuilt
    // JNI bindings React Native Android uses (YogaNode Java API over the C++ core).
    // `implementation` so libyoga.so ships in the APK alongside libBlazorNative.
    // Runtime.so — the coexistence proof. Pulls fbjni + soloader (the native-lib
    // loader) transitively. 6.1 builds the real BnWidgetMapper-over-Yoga placement.
    implementation("com.facebook.yoga:yoga:3.2.1")

    // Kotlin stdlib
    implementation(kotlin("stdlib-jdk8"))

    // JVM unit tests (Phase 2.1)
    testImplementation("org.junit.jupiter:junit-jupiter-api:5.14.4")
    testImplementation("org.junit.jupiter:junit-jupiter-params:5.14.4")
    testRuntimeOnly("org.junit.jupiter:junit-jupiter-engine:5.14.4")
    testRuntimeOnly("org.junit.platform:junit-platform-launcher:1.14.4")

    // Phase 6.0 Yoga spike: yoga declares soloader (its native-lib loader) at
    // RUNTIME scope, so it's in the APK but off the compile classpath — the Yoga
    // instrumented test needs it on the test COMPILE classpath to call
    // SoLoader.init(context) before the first YogaNode. Pinned to the version yoga
    // 3.2.1 resolves (0.10.5) so the compile + runtime SoLoader agree.
    androidTestImplementation("com.facebook.soloader:soloader:0.10.5")

    // Android instrumented tests (Phase 2.2 Task 7 fills in)
    androidTestImplementation("androidx.test.ext:junit:1.3.0")
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
            // ciSoDir set (CI instrumented job) → x86_64-only APK; see the
            // ciSoDir declaration above for why.
            abiFilters += if (ciSoDir != null) listOf("x86_64") else listOf("arm64-v8a", "x86_64")
        }

        // Phase 3.5 Gate 0: custom runner sets BLAZORNATIVE_STRICT=1 before
        // any test class loads — every instrumented process runs strict
        // (BlazorNativeTestRunner.kt has the full contract).
        testInstrumentationRunner = "io.blazornative.shell.BlazorNativeTestRunner"
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
        // Phase 4.4: InspectorServerTest drives InspectorServer, which is
        // compiled by compileJvmHostKotlin (see its block comment below) —
        // its output must join the unit-test RUNTIME classpath through THIS
        // sanctioned hook (AGP overwrites a Test.classpath set anywhere
        // else). The COMPILE classpath is wired on the Kotlin task below.
        unitTests.all { test ->
            test.dependsOn("compileJvmHostKotlin")
            test.classpath += files(layout.buildDirectory.dir("tmp/kotlin-classes/jvmHost"))
        }
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

// JNA's library search path — the NativeAOT win-x64 publish output for
// BlazorNative.Runtime.dll. Shared by the host-JVM unit tests and the
// Phase 4.3 runPreviewHost JavaExec below.
val winX64PublishPath: String = rootProject.projectDir
    .resolve("../../src/BlazorNative.Runtime/bin/Release/net10.0/win-x64/publish")
    .absolutePath

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
// Phase 4.3 Gate 1: PreviewHost — the devloop fast-lane surface. A JavaExec
// over the debug-variant Kotlin classes (PreviewHost.kt lives in main/kotlin;
// see its placement note) with the DESKTOP JNA jar — the main classpath's
// jna:.aar carries only Android dispatch binaries, not win32-x86-64.
//
// Task-graph note: compileDebugKotlin rides preBuild → copyRuntimeSo →
// verifyNativeAssets, so both bionic .so publishes must exist — the exact
// prerequisite testDebugUnitTest already has (see ci.yml's JVM step comment).
// No .NET publish task is wired here: the devloop script owns publishing.
// ─────────────────────────────────────────────────────────────────────────────
val previewHostRuntime: Configuration by configurations.creating {
    description = "Runtime classpath for the runPreviewHost JavaExec (desktop-JVM JNA dispatch)"
}

dependencies {
    previewHostRuntime("net.java.dev.jna:jna:5.19.1")
    previewHostRuntime(kotlin("stdlib-jdk8"))
}

tasks.register<JavaExec>("runPreviewHost") {
    description = "Boots the NativeAOT dll, mounts -Pcomponent= (default BnDemo), prints the widget tree + stage timings"
    group = "blazornative"
    dependsOn("compileDebugKotlin")
    mainClass.set("io.blazornative.jni.PreviewHostKt")
    classpath = files(layout.buildDirectory.dir("tmp/kotlin-classes/debug")) + previewHostRuntime
    systemProperty("jna.library.path", winX64PublishPath)
    // Deterministic UTF-8 on Windows consoles (BnDemo's "Settings →" label).
    systemProperty("stdout.encoding", "UTF-8")
    systemProperty("stderr.encoding", "UTF-8")
    (findProperty("component") as String?)?.let { args(it) }
}

// ─────────────────────────────────────────────────────────────────────────────
// Phase 4.4 Gate 1: InspectorHost — PreviewHost's long-lived sibling. Its two
// files (InspectorServer/InspectorHost, src/jvmHost/kotlin) need the JDK's
// com.sun.net.httpserver, which does NOT exist in android.jar — and EVERY AGP
// compilation (main, unitTest) compiles against android.jar, so they cannot
// join any Android source set. compileJvmHostKotlin is therefore a dedicated
// plain-JVM KotlinCompile over just those files, with the debug classes on
// its classpath (no duplicate compilation of the shared sources; nothing
// added to the APK). The Android-safe inspector pieces (InspectorState,
// InspectorJson, TreeSnapshot.renderJson) stay in main/kotlin.
//
// The e2e test (InspectorServerTest, test source set) compiles against the
// jvmHost OUTPUT (wired below onto the *UnitTestKotlin `libraries` + the
// unit-test runtime classpath in testOptions.unitTests.all): the test only
// touches InspectorServer's public API — whose signatures carry no httpserver
// types — and at RUNTIME unit tests execute on the host JDK, where
// jdk.httpserver is present.
// ─────────────────────────────────────────────────────────────────────────────
val inspectorHostRuntime: Configuration by configurations.creating {
    description = "Runtime classpath for the runInspectorHost JavaExec (desktop-JVM JNA dispatch)"
}

dependencies {
    inspectorHostRuntime("net.java.dev.jna:jna:5.19.1")
    inspectorHostRuntime(kotlin("stdlib-jdk8"))
}

// Registering a KotlinCompile outside the plugin's source-set machinery needs
// KGP's sanctioned factory (KotlinBaseApiPlugin — the same API AGP's built-in
// Kotlin support uses): it supplies the constructor's KotlinJvmCompilerOptions
// and every internal task convention a bare tasks.register() leaves unset.
val kotlinBaseApi = plugins.apply(org.jetbrains.kotlin.gradle.plugin.KotlinBaseApiPlugin::class.java)

val compileJvmHostKotlin = kotlinBaseApi.registerKotlinJvmCompileTask(
    "compileJvmHostKotlin",
    "BlazorNative.Jni-jvmHost",
)
compileJvmHostKotlin.configure {
    description = "Compiles the host-JVM-only inspector surface (src/jvmHost/kotlin) against the full JDK"
    dependsOn("compileDebugKotlin")
    source(layout.projectDirectory.dir("src/jvmHost/kotlin"))
    libraries.from(layout.buildDirectory.dir("tmp/kotlin-classes/debug"), inspectorHostRuntime)
    destinationDirectory.set(layout.buildDirectory.dir("tmp/kotlin-classes/jvmHost"))
    compilerOptions.jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17)
    // The factory leaves this REQUIRED internal convention unset (its getter
    // is Kotlin-internal → name-mangled), so assign it reflectively.
    // KGP-bump watch (Renovate): a Kotlin plugin bump may rename/remove the
    // mangled getter — the failure is LOUD, never silent: .first{} throws
    // NoSuchElementException at task realization on every testDebugUnitTest
    // run. Delete or adjust this reflective line when that fires.
    @Suppress("UNCHECKED_CAST")
    (javaClass.methods.first { it.name.startsWith("getMultiPlatformEnabled") }
        .invoke(this) as org.gradle.api.provider.Property<Boolean>).set(false)
}

// The unit-test COMPILE classpath half of the e2e-test wiring (a plain
// `testImplementation(files(...))` never reaches AGP's variant classpaths;
// the RUNTIME half lives in android.testOptions.unitTests.all above).
// configureEach because AGP only creates its variant tasks in afterEvaluate;
// flatMap carries the compileJvmHostKotlin task dependency.
tasks.withType<org.jetbrains.kotlin.gradle.tasks.KotlinCompile>().configureEach {
    if (name.endsWith("UnitTestKotlin")) {
        libraries.from(compileJvmHostKotlin.flatMap { it.destinationDirectory })
    }
}

tasks.register<JavaExec>("runInspectorHost") {
    description = "Boots the NativeAOT dll, mounts -Pcomponent= (default BnDemo), serves the inspector API/page on -Pport= (default 5199) until Ctrl+C"
    group = "blazornative"
    dependsOn(compileJvmHostKotlin)
    mainClass.set("io.blazornative.jni.InspectorHostKt")
    classpath = files(
        layout.buildDirectory.dir("tmp/kotlin-classes/jvmHost"),
        layout.buildDirectory.dir("tmp/kotlin-classes/debug"),
    ) + inspectorHostRuntime
    systemProperty("jna.library.path", winX64PublishPath)
    // Deterministic UTF-8 on Windows consoles (BnDemo's "Settings →" label).
    systemProperty("stdout.encoding", "UTF-8")
    systemProperty("stderr.encoding", "UTF-8")
    args(
        (findProperty("component") as String?) ?: "BnDemo",
        (findProperty("port") as String?) ?: "5199",
    )
}

// ─────────────────────────────────────────────────────────────────────────────
// Keep the per-ABI NativeAOT .so in sync with its publish output. Stale-asset
// bugs were the #1 risk in Phase 2.2 design — the preBuild dependency
// guarantees every APK assembly picks up the latest from `dotnet publish`.
// ─────────────────────────────────────────────────────────────────────────────

// Expected native build outputs — shared by verifyNativeAssets + the copy task.
// With -PciSoDir the x86_64 .so comes from the CI artifact dir instead, and no
// arm64 .so is expected at all (x86_64-only CI shape — see ciSoDir above).
val runtimePubRoot = rootProject.projectDir.resolve("../../src/BlazorNative.Runtime/bin/Release/net10.0")
val runtimeSoX64 = ciSoDir?.resolve("BlazorNative.Runtime.so")
    ?: runtimePubRoot.resolve("linux-bionic-x64/publish/BlazorNative.Runtime.so")
val runtimeSoArm64: File? =
    if (ciSoDir == null) runtimePubRoot.resolve("linux-bionic-arm64/publish/BlazorNative.Runtime.so") else null

// Gate 3 review follow-up: a Copy task whose every `from` source is missing
// goes NO-SOURCE and skips ALL actions — including a doFirst fail-fast —
// silently producing an APK with stale/absent native assets. Verification
// therefore lives in a plain task (no inputs → never skipped) that every
// copy task depends on.
val verifyNativeAssets = tasks.register("verifyNativeAssets") {
    description = "Fails fast when an expected NativeAOT .so publish output is missing"
    group = "blazornative"
    doLast {
        val expected = buildMap {
            put(
                runtimeSoX64,
                if (ciSoDir != null) "download the linux-bionic-x64 CI artifact into $ciSoDir"
                else "dotnet publish src/BlazorNative.Runtime -c Release -r linux-bionic-x64"
            )
            runtimeSoArm64?.let {
                put(it, "dotnet publish src/BlazorNative.Runtime -c Release -r linux-bionic-arm64")
            }
        }
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
// JNA's Native.load("BlazorNative.Runtime") resolves on Android
// (dlopen expects lib<name>.so inside the APK's native-lib dir).
val copyRuntimeSo = tasks.register<Copy>("copyRuntimeSo") {
    description = "Copies NativeAOT BlazorNative.Runtime.so (per ABI) into androidMain/jniLibs/"
    group = "blazornative"
    dependsOn(verifyNativeAssets)
    from(runtimeSoX64) { into("x86_64") }
    runtimeSoArm64?.let { arm64 -> from(arm64) { into("arm64-v8a") } }
    rename { "libBlazorNative.Runtime.so" }
    into(layout.projectDirectory.dir("src/androidMain/jniLibs"))
}

// Wire the copy into preBuild so every APK build picks up the latest .so.
tasks.named("preBuild") {
    dependsOn(copyRuntimeSo)
}
