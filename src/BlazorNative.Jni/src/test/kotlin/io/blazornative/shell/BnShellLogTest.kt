package io.blazornative.shell

import io.blazornative.jni.BnLogFormat
import io.blazornative.jni.BnLogLevel
import org.junit.jupiter.api.AfterEach
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

// ─────────────────────────────────────────────────────────────────────────────
// BnShellLogTest — issue #200. The BEHAVIOURAL half of the fix: the shell's own
// narration is now suppressed at the default threshold and reappears when the
// documented knob raises it.
//
// ASSERTED AGAINST THE INJECTED SINK, not by scraping `logcat -d` — the rule
// `BnStderrPumpTest` already established for this module (design §8.1 pin 5). A
// scrape needs a device and is timing-sensitive; this runs in the JVM lane on
// every PR. The default sink's `Log.println` is never invoked here, which is
// also what keeps this file runnable in the AGP unit-test lane where every
// android.jar method throws "not mocked".
//
// WHAT THIS CANNOT SEE, stated plainly: that `MainActivity.onCreate` actually
// calls `setLevelFromOrdinal` with the resolved ordinal. That is an Activity
// lifecycle fact and belongs to the device observation #200 asks for (silent at
// Warn, narration back at Verbose). What IS pinned off-device is that the gate
// itself decides correctly and that no bare Log.i/d/v survives in the shell
// source (AndroidLogDriftTests, .NET lane).
// ─────────────────────────────────────────────────────────────────────────────

class BnShellLogTest {

    companion object {
        /** The PRISTINE production sink, captured before any test can replace it
         * (the companion initialises on first use of this class, and JUnit builds
         * a fresh test instance per method — an instance field would capture a
         * sink an earlier test had already swapped). */
        private val productionSink = BnShellLog.sink
    }

    private val captured = mutableListOf<Triple<Int, String, String>>()

    private fun collect() {
        captured.clear()
        BnShellLog.sink = { priority, tag, message ->
            captured += Triple(priority, tag, message)
        }
    }

    @AfterEach
    fun restore() {
        // The object is process-global; leaving a test's sink or threshold
        // installed would make the NEXT test's result depend on ordering.
        BnShellLog.sink = productionSink
        BnShellLog.setLevelFromOrdinal(BnLogLevel.UNSET)
    }

    // ── the default: quiet ───────────────────────────────────────────────────

    @Test
    fun `the four BOOT lines are suppressed at the default threshold`() {
        collect()
        BnShellLog.setLevelFromOrdinal(BnLogLevel.UNSET) // the shell declared nothing

        // Verbatim from the #200 hardware capture.
        BnShellLog.info("BlazorNative", "[BOOT] native init ok — BlazorNative.Runtime 0.5.1")
        BnShellLog.info("BlazorNative", "[BOOT] frame callback registered")
        BnShellLog.info("BlazorNative", "[BOOT] shell bridge registered")
        BnShellLog.info("BlazorNative", "[BOOT] mounted BnDemo")

        assertTrue(captured.isEmpty(),
            "the shell's boot narration reached the sink at the DEFAULT threshold — this is " +
                "issue #200 exactly, observed on a Xiaomi/Android 16 device against 0.5.1: four " +
                "Info lines in logcat at a Warn threshold. Got: $captured")
    }

    @Test
    fun `an unset ordinal resolves to Warn, the runtime's default — never to silence`() {
        BnShellLog.setLevelFromOrdinal(BnLogLevel.UNSET)
        assertEquals(BnLogLevel.WARN, BnShellLog.level())
        assertEquals(BnLogLevel.WARN, BnShellLog.DEFAULT_LEVEL,
            "DEFAULT_LEVEL must mirror BnLog.DefaultLevel (C#). AndroidLogDriftTests holds the " +
                "two equal across the languages; this is the Kotlin-side half.")
    }

    @Test
    fun `an out-of-range ordinal resolves to the default rather than to an accidental verbosity`() {
        BnShellLog.setLevelFromOrdinal(99)
        assertEquals(BnLogLevel.WARN, BnShellLog.level())
        BnShellLog.setLevelFromOrdinal(-1)
        assertEquals(BnLogLevel.WARN, BnShellLog.level())
    }

    // ── raised: the narration comes back ─────────────────────────────────────

    @Test
    fun `the narration reappears at Info — the paired control`() {
        collect()
        BnShellLog.setLevelFromOrdinal(BnLogLevel.INFO)

        BnShellLog.info("BlazorNative", "[BOOT] mounted BnDemo")

        assertEquals(1, captured.size,
            "a silence that is not paired with a control proves nothing (#191). At Info the " +
                "boot narration MUST come back, or the fix is indistinguishable from deletion.")
        assertEquals(BnLogFormat.INFO, captured[0].first,
            "the line must reach logcat at android.util.Log.INFO (4), the priority it had " +
                "before the gate — a gate that also changed the severity would be two changes.")
        assertEquals("BlazorNative", captured[0].second,
            "the tag stays the bare `BlazorNative` — `adb logcat -s BlazorNative` and " +
                "scripts/devloop.ps1's boot-marker wait both filter on it.")
        assertEquals("[BOOT] mounted BnDemo", captured[0].third)
    }

    @Test
    fun `Verbose ships everything the shell narrates`() {
        collect()
        BnShellLog.setLevelFromOrdinal(BnLogLevel.VERBOSE)

        BnShellLog.info("BlazorNative", "[deep-link] startup route → /settings")
        BnShellLog.debug("BlazorNative", "detail")
        BnShellLog.verbose("BlazorNative", "trace")

        assertEquals(
            listOf(BnLogFormat.INFO, BnLogFormat.DEBUG, BnLogFormat.VERBOSE),
            captured.map { it.first })
    }

    @Test
    fun `Debug ships info and debug but not verbose`() {
        collect()
        BnShellLog.setLevelFromOrdinal(BnLogLevel.DEBUG)

        BnShellLog.info("BlazorNative", "i")
        BnShellLog.debug("BlazorNative", "d")
        BnShellLog.verbose("BlazorNative", "v")

        assertEquals(listOf(BnLogFormat.INFO, BnLogFormat.DEBUG), captured.map { it.first })
    }

    @Test
    fun `Error is quieter than Warn and still suppresses narration`() {
        collect()
        BnShellLog.setLevelFromOrdinal(BnLogLevel.ERROR)

        BnShellLog.info("BlazorNative", "i")
        assertTrue(captured.isEmpty())
    }

    // ── the gate as a predicate ──────────────────────────────────────────────

    @Test
    fun `isEnabled agrees with what the sink actually receives`() {
        collect()
        for (threshold in BnLogLevel.ERROR..BnLogLevel.VERBOSE) {
            BnShellLog.setLevelFromOrdinal(threshold)
            for (level in listOf(BnLogLevel.INFO, BnLogLevel.DEBUG, BnLogLevel.VERBOSE)) {
                captured.clear()
                when (level) {
                    BnLogLevel.INFO -> BnShellLog.info("t", "m")
                    BnLogLevel.DEBUG -> BnShellLog.debug("t", "m")
                    else -> BnShellLog.verbose("t", "m")
                }
                assertEquals(BnShellLog.isEnabled(level), captured.isNotEmpty(),
                    "isEnabled($level) disagreed with the emission at threshold $threshold — a " +
                        "call site that guards itself with isEnabled would then build a message " +
                        "the gate drops, or skip one it would have shipped.")
            }
        }
    }

    @Test
    fun `UNSET is never a message level`() {
        BnShellLog.setLevelFromOrdinal(BnLogLevel.VERBOSE)
        assertFalse(BnShellLog.isEnabled(BnLogLevel.UNSET),
            "ordinal 0 is the wire's 'the shell said nothing'. It is a threshold input, never a " +
                "message level — BnLog.IsEnabled rejects it for the same reason.")
    }

    // ── a throwing sink cannot fault the caller ──────────────────────────────

    @Test
    fun `a sink that throws is swallowed`() {
        BnShellLog.sink = { _, _, _ -> throw IllegalStateException("boom") }
        BnShellLog.setLevelFromOrdinal(BnLogLevel.VERBOSE)

        // No assertion needed beyond "this returns": a logger that faults its
        // caller is worse than a quiet one, and these calls sit on the boot
        // thread and inside the bridge's navigate.
        BnShellLog.info("BlazorNative", "[BOOT] mounted BnDemo")
    }
}
