package io.blazornative.shell

import android.os.Bundle
import android.system.Os
import androidx.test.runner.AndroidJUnitRunner

/**
 * Phase 3.5 Gate 0 — the strict-mode test runner (the PRE-3.5 MUST).
 *
 * Sets BLAZORNATIVE_STRICT=1 BEFORE super.onCreate — i.e. before the
 * instrumented Application is created and before ANY test class loads — so
 * HostSession's one-shot env read at first-session creation always sees
 * strict, no matter which class owns the process's first mount.
 *
 * Replaces the per-class @BeforeClass Os.setenv pattern (BnDemoAndroidTest /
 * CompositionAndroidTest carried idempotent copies keyed to the runner's
 * alphabetical class order): every instrumented process is strict now,
 * FILTERED runs included — the previous gap where a filtered run of a class
 * without its own setenv (EventRoundTripAndroidTest, WidgetMapperTest)
 * silently ran non-strict is closed by construction.
 *
 * Wired via testInstrumentationRunner in build.gradle.kts.
 */
class BlazorNativeTestRunner : AndroidJUnitRunner() {

    override fun onCreate(arguments: Bundle?) {
        // Must precede super.onCreate: the Application (and with it any path
        // to blazornative_mount) does not exist yet, so the one-shot env read
        // in HostSession.EnsureSession cannot have happened.
        Os.setenv("BLAZORNATIVE_STRICT", "1", true)
        super.onCreate(arguments)
    }
}
