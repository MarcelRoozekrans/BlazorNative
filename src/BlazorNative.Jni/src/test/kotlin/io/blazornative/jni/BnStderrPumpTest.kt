package io.blazornative.jni

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import org.junit.jupiter.params.ParameterizedTest
import org.junit.jupiter.params.provider.ValueSource
import java.io.ByteArrayInputStream
import java.io.InputStream

// ─────────────────────────────────────────────────────────────────────────────
// BnStderrPumpTest — Phase 11.4 Gate B, design §8.1 pins 4 and 5.
//
// THIS IS THE HONEST PIN, AND THE DESIGN SAYS SO IN AS MANY WORDS: the pump is
// asserted AGAINST AN INJECTED SINK over an in-memory stream, not by shelling
// out to `logcat -d` and grepping. A logcat scrape is timing-sensitive, needs a
// device, and is the classic green-test-unread-output trap; the sink assertion
// is deterministic and runs in the JVM lane on every PR.
//
// WHAT IS *NOT* PINNED HERE, stated plainly: the `Os.pipe()`/`Os.dup2()` install
// itself. It needs a real Android runtime. That half lives in
// `BnStderrPumpAndroidTest` (androidTest), is CI/emulator-only, and was NOT run
// locally for this change — no device attached.
//
// THE ROUND-TRIP HALF (pin 4) IS THE ONE THAT MATTERS MOST — design risk R1. A
// one-character drift between C#'s `BnLog.FormatLine` and this parser silently
// downgrades every framework line to the unprefixed Log.w fallback and NOTHING
// LOOKS BROKEN. This file pins the round trip WITHIN Kotlin (format → parse,
// through the declared constants, so the shape can only be changed in one
// place). The CROSS-LANGUAGE half — that C#'s constants equal Kotlin's — is
// `BnLogFormatDriftTests` in the .NET suite, which reads BOTH sources.
// ─────────────────────────────────────────────────────────────────────────────

class BnStderrPumpTest {

    // ── pin 4: the format round-trips, for EVERY level ───────────────────────

    @ParameterizedTest
    @ValueSource(ints = [BnLogFormat.ERROR, BnLogFormat.WARN, BnLogFormat.INFO,
        BnLogFormat.DEBUG, BnLogFormat.VERBOSE])
    fun `every level round-trips through format and parse`(priority: Int) {
        val line = BnLogFormat.format(priority, "renderer", "mount faulted: boom")
        val parsed = BnLogFormat.parse(line)

        assertEquals(priority, parsed.priority, "level lost in the round trip for line: $line")
        assertEquals("renderer", parsed.category)
        assertEquals("mount faulted: boom", parsed.message)
    }

    @Test
    fun `the tag mapping is a bijection over the five levels`() {
        val levels = listOf(
            BnLogFormat.ERROR, BnLogFormat.WARN, BnLogFormat.INFO,
            BnLogFormat.DEBUG, BnLogFormat.VERBOSE,
        )
        val tags = levels.map { BnLogFormat.tagForPriority(it) }
        assertEquals(levels.size, tags.toSet().size, "two levels share a tag character: $tags")
        levels.forEach { assertEquals(it, BnLogFormat.priorityForTag(BnLogFormat.tagForPriority(it))) }
    }

    @Test
    fun `the shipped prefix is exactly what the parser looks for`() {
        // Non-vacuity for the round trip above: it would pass just as happily if
        // BOTH sides used "[XX|". This anchors the Kotlin side to the literal the
        // .NET drift pin compares against BnLog.LinePrefix.
        assertEquals("[BN|", BnLogFormat.PREFIX)
        assertTrue(BnLogFormat.format(BnLogFormat.ERROR, "c", "m").startsWith("[BN|E|c] "))
    }

    // ── the fallback: unprefixed lines are KEPT, at Warn ──────────────────────

    @ParameterizedTest
    @ValueSource(strings = [
        "Unhandled Exception: System.TypeLoadException: …",
        "[BN|",                        // the prefix and nothing else
        "[BN|X|cat] unknown tag",      // a tag we do not know
        "[BN|E cat] no pipe",          // the '|' separator is gone
        "[BN|E|cat no bracket",        // the ']' is gone
        "[BN|E|] empty category",
        "  [BN|E|cat] not at column 0",
    ])
    fun `an unprefixed or malformed line is kept verbatim at Warn`(line: String) {
        val parsed = BnLogFormat.parse(line)

        assertEquals(BnLogFormat.WARN, parsed.priority,
            "design §5.5: a line that does not match the prefix is emitted at Log.w")
        assertEquals("native", parsed.category)
        assertEquals(line, parsed.message, "the line must be kept VERBATIM, never dropped or trimmed")
    }

    @Test
    fun `an empty message is still our line`() {
        val parsed = BnLogFormat.parse("[BN|I|boot] ")
        assertEquals(BnLogFormat.INFO, parsed.priority)
        assertEquals("boot", parsed.category)
        assertEquals("", parsed.message)
    }

    // ── pin 5: the pump forwards to the sink, line by line ───────────────────

    @Test
    fun `each line reaches the sink once, with its own level`() {
        val seen = drain(
            BnLogFormat.format(BnLogFormat.ERROR, "runtime", "one") + "\n" +
            BnLogFormat.format(BnLogFormat.INFO, "boot", "two") + "\n" +
            "a bare BCL line\n"
        )

        assertEquals(3, seen.size)
        assertEquals(BnLogRecord(BnLogFormat.ERROR, "runtime", "one"), seen[0])
        assertEquals(BnLogRecord(BnLogFormat.INFO, "boot", "two"), seen[1])
        assertEquals(BnLogRecord(BnLogFormat.WARN, "native", "a bare BCL line"), seen[2])
    }

    @Test
    fun `a line split across two reads is not emitted until its newline`() {
        // The hazard: a naive read-decode-split emits two half-lines and the
        // SECOND one loses the prefix — i.e. loses its level, silently.
        val whole = BnLogFormat.format(BnLogFormat.ERROR, "renderer", "a long faulting message")
        val bytes = (whole + "\n").toByteArray(Charsets.UTF_8)

        val seen = mutableListOf<BnLogRecord>()
        BnStderrPump.drain(chunked(bytes, at = 9)) { seen += it }

        assertEquals(1, seen.size, "the line was split into fragments: $seen")
        assertEquals(BnLogRecord(BnLogFormat.ERROR, "renderer", "a long faulting message"), seen[0])
    }

    @Test
    fun `a tail with no trailing newline is flushed at EOF`() {
        // Console.Error.Write (no Ln) and a crash dump both end this way, and the
        // last line before a fault is the one that matters most.
        val seen = drain("[BN|E|runtime] the last thing before the crash")

        assertEquals(1, seen.size)
        assertEquals(BnLogRecord(BnLogFormat.ERROR, "runtime", "the last thing before the crash"), seen[0])
    }

    @Test
    fun `a multi-byte character split across a read boundary is not mangled`() {
        val bytes = "[BN|W|mapper] ✓ değer — ok\n".toByteArray(Charsets.UTF_8)
        val seen = mutableListOf<BnLogRecord>()
        // 15 lands inside the UTF-8 sequence for '✓'.
        BnStderrPump.drain(chunked(bytes, at = 15)) { seen += it }

        assertEquals(1, seen.size)
        assertEquals("✓ değer — ok", seen[0].message)
    }

    @Test
    fun `CRLF is tolerated`() {
        val seen = drain("[BN|D|x] windows\r\n")
        assertEquals(listOf(BnLogRecord(BnLogFormat.DEBUG, "x", "windows")), seen)
    }

    @Test
    fun `a bare newline emits nothing`() {
        assertEquals(emptyList<BnLogRecord>(), drain("\n\n\n"))
    }

    @Test
    fun `a runaway line is capped rather than growing the heap`() {
        val seen = drain("[BN|E|runtime] " + "x".repeat(20_000) + "\n")

        assertEquals(1, seen.size)
        assertTrue(seen[0].message.length < BnStderrPump.MAX_LINE + 64,
            "the emitted line was ${seen[0].message.length} chars — the cap did not apply")
        assertEquals(BnLogFormat.ERROR, seen[0].priority, "the cap must not cost the level")
        assertTrue(seen[0].message.endsWith("…[truncated]"), "truncation must be visible, not silent")
    }

    @Test
    fun `a throwing sink cannot kill the pump`() {
        // The reader is a process-lifetime daemon thread: if it dies, every
        // subsequent diagnostic is lost silently and the app looks fine.
        val seen = mutableListOf<BnLogRecord>()
        var calls = 0
        BnStderrPump.drain(stream("[BN|E|a] one\n[BN|E|b] two\n[BN|E|c] three\n")) {
            calls++
            if (calls == 2) throw IllegalStateException("sink is broken")
            seen += it
        }

        assertEquals(3, calls, "the pump stopped reading after the sink threw")
        assertEquals(listOf("a", "c"), seen.map { it.category })
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private fun drain(text: String): List<BnLogRecord> {
        val seen = mutableListOf<BnLogRecord>()
        BnStderrPump.drain(stream(text)) { seen += it }
        return seen
    }

    private fun stream(text: String): InputStream =
        ByteArrayInputStream(text.toByteArray(Charsets.UTF_8))

    /** A stream that hands out `[0, at)` and then the rest — the read boundary a
     * real pipe puts wherever it likes. */
    private fun chunked(bytes: ByteArray, at: Int): InputStream = object : InputStream() {
        private var pos = 0
        override fun read(): Int = if (pos >= bytes.size) -1 else bytes[pos++].toInt() and 0xFF
        override fun read(b: ByteArray, off: Int, len: Int): Int {
            if (pos >= bytes.size) return -1
            val limit = if (pos < at) at else bytes.size
            val n = minOf(len, limit - pos)
            System.arraycopy(bytes, pos, b, off, n)
            pos += n
            return n
        }
    }
}
