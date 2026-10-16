package io.blazornative.jni

import org.junit.jupiter.api.AfterEach
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNotSame
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
            BnDiagnostics.sink("[X] something happened", null)
            BnDiagnostics.sink("[X] failed", IllegalStateException("boom"))
        } finally {
            System.setErr(original)
        }
        val text = bytes.toString()
        assertTrue(text.contains("[X] something happened"), text)
        assertTrue(text.contains("[X] failed: java.lang.IllegalStateException: boom"), text)
    }

    @Test
    fun `the sink is replaceable`() {
        val seen = mutableListOf<Pair<String, Throwable?>>()
        val t = RuntimeException("x")
        BnDiagnostics.sink = { m, th -> seen += m to th }

        BnDiagnostics.sink("hello", t)

        assertEquals(1, seen.size)
        assertEquals("hello", seen[0].first)
        assertTrue(seen[0].second === t)
        assertNotSame(productionSink, BnDiagnostics.sink)
    }

    @Test
    fun `BnPrivacy has exactly the two members`() {
        assertEquals(listOf("REDACTED", "SAFE"), BnPrivacy.values().map { it.name })
    }
}
