package io.blazornative.jni

import java.io.InputStream

// ─────────────────────────────────────────────────────────────────────────────
// BnLogFormat / BnStderrPump — Phase 11.4 Gate B: the ANDROID half of the stdio
// transport (M11 DoD #6, issues #155/#164), design §5.1 + §5.5.
//
// WHY THIS FILE EXISTS AT ALL. The .NET runtime writes its diagnostics to
// `Console.Error` — process fd 2 — and ANDROID SENDS fd 2 TO /dev/null. The
// repo has said so in four places for five milestones (ShellBridge.kt:280,
// BlazorNativeRuntime.kt:33, and eight NativeBindings KDoc rc contracts that
// point the reader at a stderr their platform destroys). Gate A gave every
// managed line a LEVEL; it did not give it a DESTINATION. This is the
// destination: `Os.pipe()` + `Os.dup2()` over fd 2, a daemon reader, and
// `android.util.Log`. Zero ABI cost — the runtime is not modified at all, the
// shell simply reads a file descriptor.
//
// WHY THE PURE HALF LIVES HERE, IN `src/main/kotlin`, AND NOT IN androidMain.
// Two reasons, and the second is the load-bearing one:
//   1. it imports nothing from `android.*`, so the AGP unit-test lane (where
//      every android.jar method throws "not mocked") can execute it;
//   2. design §8.1 pin 5 demands the pump be pinned AGAINST AN INJECTED SINK
//      rather than by scraping `logcat -d`. That is only honest if the
//      forwarding logic — line splitting, partial-line buffering, prefix
//      parsing, priority mapping — is a plain function over a plain stream.
//      `BnStderrLogcatPump` (androidMain) is then the ~40 lines that are
//      genuinely untestable off-device: the pipe, the dup2 and the Log call.
//
// ⚠ THE FORMAT IS A STRINGLY-TYPED CONTRACT ACROSS TWO LANGUAGES (design R1).
// `BlazorNative.Core.BnLog.FormatLine` writes `[BN|E|category] message`; the
// parser below reads it back. A ONE-CHARACTER DRIFT DOWNGRADES EVERY FRAMEWORK
// LINE TO THE UNPREFIXED `Log.w` FALLBACK AND NOTHING LOOKS BROKEN — the worst
// failure shape there is, because the pump keeps working, logcat keeps filling,
// and only the severity is quietly wrong. The two copies are therefore held
// equal by `BnLogFormatDriftTests` (tests/BlazorNative.Runtime.Tests), which
// READS THIS FILE'S SOURCE and compares the constants it declares against the
// live `BnLog` — so a drift reds in the commit that causes it.
// ─────────────────────────────────────────────────────────────────────────────

/** One parsed stderr line: the `android.util.Log` priority it should be emitted
 * at, the category (the logcat tag suffix) and the message. */
data class BnLogRecord(val priority: Int, val category: String, val message: String)

/**
 * The line format of design §5.5 and its parse-back.
 *
 * PURE BY CONSTRUCTION — no I/O, no `android.*`, no state. That is what lets the
 * round-trip be PINNED rather than asserted in prose.
 */
object BnLogFormat {

    /** The prefix marker. MUST equal `BlazorNative.Core.BnLog.LinePrefix`. */
    const val PREFIX: String = "[BN|"

    // The android.util.Log priority ordinals, MIRRORED rather than imported so
    // this object stays Android-free (see the file header). They are frozen
    // platform constants (android.util.Log.VERBOSE..ERROR), not our numbering.
    const val VERBOSE: Int = 2
    const val DEBUG: Int = 3
    const val INFO: Int = 4
    const val WARN: Int = 5
    const val ERROR: Int = 6

    /**
     * THE FALLBACK, AND IT IS A DECISION, NOT A DEFAULT (design §5.5).
     *
     * A line that does not carry the prefix is NOT DROPPED. It is BCL output, a
     * NativeAOT runtime dump, JNA's own swallow-to-stderr handler, or a
     * third-party native library — i.e. EXACTLY THE OUTPUT THIS TRANSPORT EXISTS
     * TO RESCUE, and the half no bridge-based option could ever have carried.
     *
     * `Log.w`, chosen over `Log.e` because unstructured runtime chatter is not
     * self-evidently a fault, and over `Log.i` because in Release the only thing
     * that reaches stderr unannounced is usually a problem.
     */
    const val FALLBACK_PRIORITY: Int = WARN

    /** The category unprefixed lines are filed under (logcat tag
     * `BlazorNative/native`). */
    const val FALLBACK_CATEGORY: String = "native"

    /**
     * The single-character level tag → `android.util.Log` priority. `null` for
     * any other character, which the caller treats as "not our line".
     *
     * The letters MIRROR `BnLog.Tag(BnLogLevel)`. Held equal by
     * `BnLogFormatDriftTests`.
     */
    fun priorityForTag(tag: Char): Int? = when (tag) {
        'E' -> ERROR
        'W' -> WARN
        'I' -> INFO
        'D' -> DEBUG
        'V' -> VERBOSE
        else -> null
    }

    /** The inverse of [priorityForTag]. An unknown priority maps to `W`,
     * defensively — the same non-lying rule `BnLog.Tag` applies to `Unset`. */
    fun tagForPriority(priority: Int): Char = when (priority) {
        ERROR -> 'E'
        WARN -> 'W'
        INFO -> 'I'
        DEBUG -> 'D'
        VERBOSE -> 'V'
        else -> 'W'
    }

    /**
     * The Kotlin twin of `BnLog.FormatLine`. NOT used in production — the
     * framework's lines are written by the .NET side — but it is what makes the
     * round-trip pin a ROUND TRIP instead of two hand-written literals: the JVM
     * test formats with this and parses with [parse], so any change to the shape
     * has to be made in one place and is then caught against C# by the drift pin.
     */
    fun format(priority: Int, category: String, message: String): String =
        "$PREFIX${tagForPriority(priority)}|$category] $message"

    /**
     * Recovers `(priority, category, message)` from one stderr line.
     *
     * Anything that is not a well-formed prefixed line comes back as
     * `(FALLBACK_PRIORITY, FALLBACK_CATEGORY, <the whole line verbatim>)` — see
     * [FALLBACK_PRIORITY]. The line is never dropped and never truncated by the
     * parser.
     */
    fun parse(line: String): BnLogRecord {
        if (!line.startsWith(PREFIX)) return fallback(line)

        val tagAt = PREFIX.length
        // Need at least "<tag>|" plus a closing ']'.
        if (line.length < tagAt + 3) return fallback(line)
        if (line[tagAt + 1] != '|') return fallback(line)

        val priority = priorityForTag(line[tagAt]) ?: return fallback(line)

        val close = line.indexOf(']', tagAt + 2)
        if (close < 0) return fallback(line)

        val category = line.substring(tagAt + 2, close)
        if (category.isEmpty()) return fallback(line)

        // FormatLine puts exactly one space after the ']'. A line that ends at
        // the bracket (an empty message) is still ours.
        val message = if (close + 2 <= line.length) line.substring(close + 2) else ""
        return BnLogRecord(priority, category, message)
    }

    private fun fallback(line: String) = BnLogRecord(FALLBACK_PRIORITY, FALLBACK_CATEGORY, line)
}

/**
 * The line pump: reads a stream of stderr bytes and hands each COMPLETE line to
 * a sink as a parsed [BnLogRecord].
 *
 * Everything hard about the transport that is not the `dup2` is here, and each
 * piece is a real hazard rather than defensive decoration:
 *
 *  · **Partial lines.** `Console.Error.WriteLine` auto-flushes, so lines arrive
 *    whole IN PRACTICE — but a read boundary can still land mid-line, and a
 *    naive `read → decode → split` would emit two half-lines with the second
 *    losing its prefix (and therefore its level). Bytes are buffered until `\n`.
 *  · **No trailing newline.** `Console.Error.Write` (no `Ln`) and a crash dump
 *    both leave a tail with no newline. On EOF the tail is flushed rather than
 *    swallowed — the last line before a fault is the one that matters most.
 *  · **A cap.** logcat truncates around 4 KB per entry regardless, and an
 *    unbounded buffer over a runaway writer is a heap grower. A line longer than
 *    [MAX_LINE] is emitted at the cap and the remainder is dropped up to the
 *    next newline.
 *  · **UTF-8 across the boundary.** Decoding happens per line, on whole lines,
 *    so a multi-byte sequence split across a read cannot become mojibake.
 *  · **A throwing sink cannot kill the pump.** The reader is a process-lifetime
 *    daemon thread; if it dies, every subsequent diagnostic is lost silently and
 *    the app looks fine. So sink throws are swallowed, exactly as `BnLog.Write`
 *    swallows its own.
 */
object BnStderrPump {

    /** Emitted-line cap. logcat truncates around 4 KB per entry anyway. */
    const val MAX_LINE: Int = 4000

    /** Read-buffer size. */
    private const val CHUNK = 4096

    /**
     * Drains [input] until EOF, calling [sink] once per line. Returns when the
     * stream ends; on Android that is never, which is why the caller runs this
     * on a daemon thread.
     *
     * @param sink receives every line, prefixed or not. Never called with a
     *   partial line except at EOF (see the tail-flush note above).
     */
    fun drain(input: InputStream, sink: (BnLogRecord) -> Unit) {
        val buffer = ByteArray(CHUNK)
        var pending = ByteArray(0)
        var overflowed = false

        while (true) {
            val read = try {
                input.read(buffer)
            } catch (t: Throwable) {
                break // the pipe closed under us; the process is going away
            }
            if (read < 0) break

            var start = 0
            for (i in 0 until read) {
                if (buffer[i] != '\n'.code.toByte()) continue

                // THE CAP APPLIES ON THIS PATH TOO, and that is not symmetry for
                // its own sake: a runaway line's newline eventually arrives, and
                // an append here that ignored MAX_LINE would hand the sink the
                // whole 20 KB the cap was supposed to have prevented.
                val grown = append(pending, buffer, start, i)
                overflowed = overflowed || grown.size < pending.size + (i - start)
                pending = grown

                start = i + 1
                emit(pending, overflowed, sink)
                pending = ByteArray(0)
                overflowed = false
            }

            if (start < read) {
                val grown = append(pending, buffer, start, read)
                overflowed = overflowed || grown.size < pending.size + (read - start)
                pending = grown
            }
        }

        // EOF: flush whatever came without a trailing newline.
        if (pending.isNotEmpty()) emit(pending, overflowed, sink)
    }

    /** Appends `buffer[from, to)` to `pending`, never past [MAX_LINE]. Keeping the
     * HEAD rather than the tail is deliberate: the `[BN|L|category]` prefix — the
     * level and the category — lives at the front, so a truncated line still
     * arrives at the right severity under the right tag. */
    private fun append(pending: ByteArray, buffer: ByteArray, from: Int, to: Int): ByteArray {
        val room = (MAX_LINE - pending.size).coerceAtLeast(0)
        val take = (to - from).coerceAtMost(room)
        return if (take <= 0) pending else pending + buffer.copyOfRange(from, from + take)
    }

    private fun emit(
        bytes: ByteArray,
        overflowed: Boolean,
        sink: (BnLogRecord) -> Unit,
    ) {
        var end = bytes.size
        if (end > 0 && bytes[end - 1] == '\r'.code.toByte()) end-- // tolerate CRLF
        if (end == 0 && !overflowed) return // a bare newline is not a log line

        var text = String(bytes, 0, end, Charsets.UTF_8)
        if (overflowed) text += " …[truncated]"

        val record = BnLogFormat.parse(text)
        try {
            sink(record)
        } catch (t: Throwable) {
            // A logger that faults its own reader thread would take the whole
            // transport down for the process lifetime. Swallow, as BnLog does.
        }
    }
}
