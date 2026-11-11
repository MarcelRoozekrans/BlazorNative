package io.blazornative.shell

import io.blazornative.jni.BnLogFormat
import io.blazornative.jni.BnLogLevel
import org.junit.jupiter.api.AfterEach
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

// ─────────────────────────────────────────────────────────────────────────────
// BnStderrPumpTwinTest — the Android half of the #401 twin pin (18.7).
//
// The stderr pump forwards what .NET wrote, at the level .NET wrote it, WITHOUT
// re-applying the shell's level: .NET already applied the managed one. Every
// case in `BnStderrPumpVectors` (generated from src/stderr-pump-vectors.json,
// which the Swift suite asserts too) is pushed through the production forward
// path, `BnStderrLogcatPump.forward`, with the shell set to the vector's level.
//
// THE LEVEL CONTROL. A pump that forwards everything would also pass if the
// shell level were never applied at all. So each vector first writes a line the
// shell level must gate through BnShellLog itself and asserts it did NOT reach
// the sink. Warn and Error are ungated in BnShellLog by design, so the control
// line is never below Info.
//
// Asserted against the injected sink, as BnShellLogTest does, so the default
// `Log.println` is never called and the test needs no device.
// ─────────────────────────────────────────────────────────────────────────────

class BnStderrPumpTwinTest {

    companion object {
        private val productionSink = BnShellLog.sink
        private val productionThrowableSink = BnShellLog.throwableSink

        /** BnLogLevel ordinal 1..5 → the BnLog tag letter, in ordinal order. */
        private const val LETTERS_BY_ORDINAL = "EWIDV"
    }

    private val captured = mutableListOf<Triple<Int, String, String>>()

    @AfterEach
    fun restore() {
        BnShellLog.sink = productionSink
        BnShellLog.throwableSink = productionThrowableSink
        BnShellLog.reveal = false
        BnShellLog.setLevelFromOrdinal(BnLogLevel.UNSET)
    }

    private fun writeGated(level: Int) = when (level) {
        BnLogLevel.INFO -> BnShellLog.info("control", "gated")
        BnLogLevel.DEBUG -> BnShellLog.debug("control", "gated")
        else -> BnShellLog.verbose("control", "gated")
    }

    @Test
    fun `every_vector_is_forwarded_whatever_the_shell_level`() {
        assertTrue(BnStderrPumpVectors.all.isNotEmpty(), "no vectors ran: the generated table is empty")

        // Every vector runs and every failure is collected, so one red names every
        // vector that broke, not only the first. XCTest on iOS continues the same way.
        val failures = mutableListOf<String>()
        for (v in BnStderrPumpVectors.all) {
            captured.clear()
            BnShellLog.setLevelFromOrdinal(v.shellLevel)
            // The pump writes REDACTED, so the text is only visible when revealed.
            BnShellLog.reveal = true
            BnShellLog.sink = { priority, tag, message -> captured += Triple(priority, tag, message) }

            // Level controls. A pump that forwards everything also passes if the
            // shell level was never applied, so prove the level took, three ways.
            // 1. The setter took: the getter reads back the vector's level.
            if (BnShellLog.level() != v.shellLevel) {
                failures += "vector ${v.name}: shell level ${v.shellLevel} was not applied: " +
                    "the shell's level setter did not take"
            }
            // 2. Positive: a gated line AT the shell level reaches the sink. Warn and
            //    Error are ungated in BnShellLog, so this only exists from Info up.
            if (v.shellLevel >= BnLogLevel.INFO) {
                writeGated(v.shellLevel)
                if (captured.size != 1) {
                    failures += "vector ${v.name}: shell level ${v.shellLevel} was not applied: " +
                        "a gated line at it did not reach the sink"
                }
            }
            // 3. Negative: a gated line one below the threshold is dropped. It is
            //    never below Info for the same reason.
            captured.clear()
            if (v.shellLevel < BnLogLevel.VERBOSE) {
                writeGated(maxOf(v.shellLevel + 1, BnLogLevel.INFO))
                if (captured.isNotEmpty()) {
                    failures += "vector ${v.name}: shell level ${v.shellLevel} was not applied: " +
                        "a gated line below it reached the sink"
                }
            }
            // The collector must hold only the forwarded record from here on.
            captured.clear()

            BnStderrLogcatPump.forward(BnLogFormat.parse(v.line))

            if (captured.size != 1) {
                failures += "vector ${v.name}: the pump dropped or re-levelled a line .NET already gated " +
                    "(${captured.size} records)"
                continue
            }
            val (priority, tag, message) = captured[0]
            val expectedPriority = BnLogFormat.priorityForTag(LETTERS_BY_ORDINAL[v.level - 1])
            if (priority != expectedPriority) {
                failures += "vector ${v.name}: wrong android priority $priority, expected $expectedPriority"
            }
            val expectedTag = "${BnStderrLogcatPump.TAG_PREFIX}/${v.category}"
            if (tag != expectedTag) failures += "vector ${v.name}: wrong logcat tag $tag, expected $expectedTag"
            if (message != v.message) failures += "vector ${v.name}: wrong message"
        }
        assertTrue(failures.isEmpty(), failures.joinToString("\n"))
    }
}
