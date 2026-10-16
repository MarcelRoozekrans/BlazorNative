package io.blazornative.jni

import org.junit.jupiter.api.AfterEach
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.io.ByteArrayOutputStream
import java.io.PrintStream

// #426: src/main cannot reference android.util.Log, so its diagnostics go through
// one injectable sink. The default is System.err; the Android shell replaces it.
class BnDiagnosticsTest {

    companion object {
        private val productionSink = BnDiagnostics.sink
    }

    @AfterEach
    fun restore() {
        BnDiagnostics.sink = productionSink
    }

    @Test
    fun `the default sink writes to System err`() {
        val original = System.err
        val bytes = ByteArrayOutputStream()
        try {
            System.setErr(PrintStream(bytes, true))
            BnDiagnostics.sink(BnLogLevel.WARN, "[X] something happened", null)
            BnDiagnostics.sink(BnLogLevel.ERROR, "[X] failed", IllegalStateException("boom"))
        } finally {
            System.setErr(original)
        }
        val text = bytes.toString()
        assertTrue(text.contains("[X] something happened"), text)
        assertTrue(text.contains("[X] failed: java.lang.IllegalStateException: boom"), text)
        // The level does not change the JVM default's output: the two lines are
        // exactly what System.err.println wrote before the sink took a level.
        assertEquals(
            listOf("[X] something happened", "[X] failed: java.lang.IllegalStateException: boom"),
            text.lines().filter { it.isNotEmpty() })
    }

    @Test
    fun `the sink is replaceable`() {
        val seen = mutableListOf<Triple<Int, String, Throwable?>>()
        val t = RuntimeException("x")
        BnDiagnostics.sink = { level, m, th -> seen += Triple(level, m, th) }

        BnDiagnostics.sink(BnLogLevel.WARN, "hello", t)

        assertEquals(1, seen.size)
        assertEquals(BnLogLevel.WARN, seen[0].first)
        assertEquals("hello", seen[0].second)
        assertTrue(seen[0].third === t)
    }

    @Test
    fun `BnPrivacy has exactly the two members`() {
        assertEquals(listOf("REDACTED", "SAFE"), BnPrivacy.values().map { it.name })
    }
}
