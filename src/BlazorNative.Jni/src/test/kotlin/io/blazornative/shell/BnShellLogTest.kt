package io.blazornative.shell

import io.blazornative.jni.BnLogFormat
import io.blazornative.jni.BnLogLevel
import io.blazornative.jni.BnLogRecord
import io.blazornative.jni.BnPrivacy
import org.junit.jupiter.api.Assertions.assertSame
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
        private val productionThrowableSink = BnShellLog.throwableSink
    }

    private val captured = mutableListOf<Triple<Int, String, String>>()

    private val capturedThrowables = mutableListOf<Throwable?>()

    private fun collect() {
        captured.clear()
        capturedThrowables.clear()
        BnShellLog.throwableSink = { priority, tag, message, t ->
            captured += Triple(priority, tag, message)
            capturedThrowables += t
        }
        BnShellLog.sink = { priority, tag, message ->
            captured += Triple(priority, tag, message)
        }
    }

    @AfterEach
    fun restore() {
        // The object is process-global; leaving a test's sink or threshold
        // installed would make the NEXT test's result depend on ordering.
        BnShellLog.sink = productionSink
        BnShellLog.throwableSink = productionThrowableSink
        BnShellLog.reveal = false
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
        BnShellLog.reveal = true // this control is about the level gate, not privacy

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

    // ── #426: one privacy seam ───────────────────────────────────────────────

    @Test
    fun `an unrevealed REDACTED message is written as private, with its throwable dropped`() {
        collect()
        BnShellLog.reveal = false

        BnShellLog.error("BlazorNative", "node 7 faulted: secret-route", IllegalStateException("secret"))

        assertEquals(1, captured.size)
        assertEquals(BnShellLog.PRIVATE, captured[0].third)
        assertEquals(BnLogFormat.ERROR, captured[0].first, "the level stays readable")
        assertEquals("BlazorNative", captured[0].second, "the tag stays readable")
        assertTrue(capturedThrowables.isEmpty(), "a redacted line must not carry the throwable")
    }

    @Test
    fun `an unrevealed SAFE message keeps its text and drops its throwable`() {
        collect()
        BnShellLog.reveal = false

        BnShellLog.warn("BlazorNative", "[bridge] constant text", IllegalStateException("secret"), BnPrivacy.SAFE)

        assertEquals(1, captured.size)
        assertEquals("[bridge] constant text", captured[0].third)
        assertTrue(capturedThrowables.isEmpty())
    }

    @Test
    fun `a revealed line is written as today, text and throwable`() {
        collect()
        BnShellLog.reveal = true
        val t = IllegalStateException("secret")

        BnShellLog.error("BlazorNative", "node 7 faulted", t)
        BnShellLog.warn("BlazorNative", "w", null, BnPrivacy.SAFE)

        assertEquals("node 7 faulted", captured[0].third, "the anchor: the unredacted path returns the input")
        assertEquals(BnLogFormat.ERROR, captured[0].first)
        assertEquals("BlazorNative", captured[0].second)
        assertSame(t, capturedThrowables[0])
        assertEquals(BnLogFormat.WARN, captured[1].first)
        assertEquals("w", captured[1].third)
    }

    @Test
    fun `redaction is the default privacy`() {
        collect()
        BnShellLog.reveal = false
        BnShellLog.setLevelFromOrdinal(BnLogLevel.VERBOSE)

        BnShellLog.info("t", "i-text")
        BnShellLog.debug("t", "d-text")
        BnShellLog.verbose("t", "v-text")
        BnShellLog.warn("t", "w-text")
        BnShellLog.error("t", "e-text")

        assertEquals(List(5) { BnShellLog.PRIVATE }, captured.map { it.third })
    }

    @Test
    fun `info debug and verbose honour SAFE and stay level gated`() {
        collect()
        BnShellLog.reveal = false
        BnShellLog.info("t", "quiet", BnPrivacy.SAFE)
        assertTrue(captured.isEmpty(), "info is still gated at the default threshold")

        BnShellLog.setLevelFromOrdinal(BnLogLevel.VERBOSE)
        BnShellLog.info("t", "i", BnPrivacy.SAFE)
        BnShellLog.debug("t", "d", BnPrivacy.SAFE)
        BnShellLog.verbose("t", "v", BnPrivacy.SAFE)
        assertEquals(listOf("i", "d", "v"), captured.map { it.third })
    }

    @Test
    fun `warn and error are never gated by level`() {
        // There is no Off level: ERROR (1) is the lowest threshold setLevelFromOrdinal
        // accepts, so the error half cannot fail by gating. error is ungated by
        // structure, because it calls writeUngated; this fact pins only warn.
        collect()
        BnShellLog.setLevelFromOrdinal(BnLogLevel.ERROR)
        BnShellLog.warn("t", "w", null, BnPrivacy.SAFE)
        BnShellLog.error("t", "e", null, BnPrivacy.SAFE)
        assertEquals(listOf(BnLogFormat.WARN, BnLogFormat.ERROR), captured.map { it.first })
    }

    @Test
    fun `a throwing throwable sink is swallowed`() {
        BnShellLog.reveal = true
        BnShellLog.throwableSink = { _, _, _, _ -> throw IllegalStateException("boom") }
        BnShellLog.error("t", "m", RuntimeException("x"))
    }

    @Test
    fun `the pump forwards a record as private unrevealed and verbatim revealed`() {
        collect()
        val record = BnLogRecord(BnLogFormat.ERROR, "Exports", "faulted: secret stack")

        BnShellLog.reveal = false
        BnStderrLogcatPump.forward(record)
        BnShellLog.reveal = true
        BnStderrLogcatPump.forward(record)

        assertEquals(BnShellLog.PRIVATE, captured[0].third)
        assertEquals("faulted: secret stack", captured[1].third)
        assertEquals("BlazorNative/Exports", captured[0].second, "the category tag is unchanged")
        assertEquals(BnLogFormat.ERROR, captured[0].first, "the priority is unchanged")
        assertTrue(capturedThrowables.isEmpty(), "a pumped line carries no throwable")
    }
}
