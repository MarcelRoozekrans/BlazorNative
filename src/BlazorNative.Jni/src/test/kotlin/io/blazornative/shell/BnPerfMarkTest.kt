package io.blazornative.shell

import io.blazornative.jni.BnLogLevel
import java.util.concurrent.atomic.AtomicBoolean
import org.junit.jupiter.api.AfterEach
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

// ─────────────────────────────────────────────────────────────────────────────
// BnPerfMarkTest — phase 19.2 task 5. Asserted against BnShellLog's injected sink
// and BnPerfMark's clock seam, so no device and no android.jar call is needed.
//
// WHAT THIS CANNOT SEE: that MainActivity, WidgetMapper and BlazorNativeRuntime
// call the marks at the right places. That needs a device, and the measurement
// harness's click and boot runs read it back (19.3).
// ─────────────────────────────────────────────────────────────────────────────

class BnPerfMarkTest {

    companion object {
        private val productionSink = BnShellLog.sink
        private val productionReveal = BnShellLog.reveal
    }

    private val lines = mutableListOf<Pair<String, String>>()

    private fun collect() {
        lines.clear()
        BnShellLog.sink = { _, tag, message -> lines += tag to message }
    }

    @AfterEach
    fun restore() {
        BnShellLog.sink = productionSink
        BnShellLog.reveal = productionReveal
        BnShellLog.setLevelFromOrdinal(BnLogLevel.UNSET)
        BnPerfMark.clockForTest = null
    }

    @Test
    fun `mark at the default threshold reads no clock and writes nothing`() {
        collect()
        BnShellLog.setLevelFromOrdinal(BnLogLevel.UNSET)
        var reads = 0
        BnPerfMark.clockForTest = { reads++; 1L }

        BnPerfMark.mark("dispatch.post", 7)
        BnPerfMark.mark("frame.recv")

        assertEquals(0, reads, "a mark below Verbose must not read the clock")
        assertTrue(lines.isEmpty(), "a mark below Verbose must write nothing, got $lines")
    }

    @Test
    fun `mark at Verbose writes one SAFE BNPERF line`() {
        collect()
        BnShellLog.reveal = false
        BnShellLog.setLevelFromOrdinal(BnLogLevel.VERBOSE)
        BnPerfMark.clockForTest = { 123456789L }

        BnPerfMark.mark("perf.unit.a", 42)
        BnPerfMark.mark("perf.unit.a")
        BnPerfMark.mark("perf.unit.b")

        assertEquals(3, lines.size)
        // SAFE: with reveal off a REDACTED line would be "<private>".
        val first = Regex("""^BNPERF perf\.unit\.a seq=(\d+) t=123456789 id=42$""").find(lines[0].second)
        assertTrue(first != null, "first line was ${lines[0].second}")
        val second = Regex("""^BNPERF perf\.unit\.a seq=(\d+) t=123456789$""").find(lines[1].second)
        assertTrue(second != null, "second line was ${lines[1].second}")
        assertEquals(first!!.groupValues[1].toLong() + 1, second!!.groupValues[1].toLong())
        assertTrue(Regex("""^BNPERF perf\.unit\.b seq=1 t=123456789$""").matches(lines[2].second), lines[2].second)
        assertEquals("BlazorNative", lines[0].first)
    }

    @Test
    fun `bootContentOnce fires once`() {
        collect()
        BnShellLog.setLevelFromOrdinal(BnLogLevel.VERBOSE)
        BnPerfMark.clockForTest = { 5L }
        val once = AtomicBoolean(false)
        var reported = 0

        BnPerfMark.bootContentOnce(once) { reported++ }
        BnPerfMark.bootContentOnce(once) { reported++ }
        BnPerfMark.bootContentOnce(once) { reported++ }

        assertEquals(1, reported)
        assertEquals(1, lines.count { it.second.startsWith("BNPERF boot.content ") })
    }

    @Test
    fun `reportFullyDrawn is called at Warn too`() {
        collect()
        BnShellLog.setLevelFromOrdinal(BnLogLevel.WARN)
        var reads = 0
        BnPerfMark.clockForTest = { reads++; 1L }
        var reported = 0

        BnPerfMark.bootContentOnce(AtomicBoolean(false)) { reported++ }

        assertEquals(1, reported)
        assertEquals(0, reads, "boot.content below Verbose must not read the clock")
        assertTrue(lines.isEmpty(), "no mark at Warn, got $lines")
    }

    @Test
    fun `bootStart at the default threshold reads no clock and writes nothing`() {
        collect()
        BnShellLog.setLevelFromOrdinal(BnLogLevel.UNSET)
        var reads = 0
        BnPerfMark.clockForTest = { reads++; 1L }

        BnPerfMark.bootStart()

        assertEquals(0, reads, "boot.start below Verbose must not read the clock")
        assertTrue(lines.isEmpty(), "boot.start below Verbose must write nothing, got $lines")
    }

    @Test
    fun `bootContentPending is true until the process flag fires`() {
        collect()
        BnShellLog.setLevelFromOrdinal(BnLogLevel.WARN)
        // Only this test touches the process-wide flag; every other test passes its own.
        assertTrue(BnPerfMark.bootContentPending())
        var reported = 0

        BnPerfMark.bootContentOnce { reported++ }

        assertEquals(1, reported)
        assertTrue(!BnPerfMark.bootContentPending())
    }
}
