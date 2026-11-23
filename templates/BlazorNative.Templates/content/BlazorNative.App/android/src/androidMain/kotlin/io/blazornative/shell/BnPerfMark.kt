package io.blazornative.shell

import android.app.Activity
import android.os.Process
import android.os.SystemClock
import io.blazornative.jni.BnLogLevel
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicLong

// ─────────────────────────────────────────────────────────────────────────────
// BnPerfMark — the Android shell's half of the phase 19.2 measurement harness.
//
// A mark is one line: `BNPERF <point> seq=<n> t=<ns>`, plus ` id=<handlerId>` on
// the dispatch points. `seq` is a per-point counter per process, starting at 1.
// scripts/perf/README.md on main holds the line contract; main's
// tools/BlazorNative.PerfReport parses it.
//
// The level gate is `BnShellLog.isEnabled(VERBOSE)`, and it is the first
// statement of [mark]. A mark below Verbose returns there, before the clock read
// and before the line is built. `reportFullyDrawn()` is not a mark and runs at
// every level.
//
// The line goes out through `BnShellLog.verbose`: one `Log.println`, the same cost
// as on main. The release this branch is cut from has no BnPrivacy and redacts no
// shell line, so main's `BnPrivacy.SAFE` argument has nothing to map to here. The
// line holds a point name, two counters and a handler id. Every call site passes
// one of the seven point names as a string literal.
//
// BACKPORT: this is main's file on the measurement backport branch, adapted only in
// the log call above. The marks, the gate and the clocks are main's.
//
// Clocks. A span that crosses the shell/.NET boundary reads `System.nanoTime()`,
// which is CLOCK_MONOTONIC on bionic, the clock .NET's Stopwatch reads. Boot
// reads `Process.getStartElapsedRealtime()` for the start and
// `SystemClock.elapsedRealtimeNanos()` for the content, the same clock family,
// so their difference is the cold boot.
// ─────────────────────────────────────────────────────────────────────────────

object BnPerfMark {

    private const val TAG = "BlazorNative"

    private val seqs = ConcurrentHashMap<String, AtomicLong>()

    private val bootContentFired = AtomicBoolean(false)

    /**
     * Replaces the nanosecond clock for [mark], [bootStart] and [bootContentOnce]. The JVM unit
     * lane cannot call `System.nanoTime()` and `SystemClock` deterministically, so
     * a test sets this and counts the reads. Null in production.
     */
    @Volatile
    internal var clockForTest: (() -> Long)? = null

    /** Writes one mark for [point] when the level gate is at Verbose or finer.
     * [id] is written as ` id=<id>` when it is not 0: handler ids start at 1, the same
     * convention as .NET's BnPerfMarks, and it keeps a boxed Long out of the path below Verbose. */
    @JvmStatic
    @JvmOverloads
    fun mark(point: String, id: Long = 0) {
        if (!BnShellLog.isEnabled(BnLogLevel.VERBOSE)) return
        val t = (clockForTest ?: System::nanoTime)()
        write(point, t, id)
    }

    /** `boot.start`: the process start, read from the OS, in nanoseconds. */
    @JvmStatic
    fun bootStart() {
        if (!BnShellLog.isEnabled(BnLogLevel.VERBOSE)) return
        write("boot.start", (clockForTest ?: { Process.getStartElapsedRealtime() * 1_000_000L })(), 0)
    }

    /**
     * `boot.content`, once per process, then [reportFullyDrawn]. The mark is
     * gated like every other; [reportFullyDrawn] runs at every level, because the
     * platform's `Fully drawn` ruler is read from the system log, not from this
     * file. [once] defaults to the process-wide flag; a test passes its own.
     */
    @JvmStatic
    fun bootContentOnce(
        once: AtomicBoolean = bootContentFired,
        reportFullyDrawn: () -> Unit,
    ) {
        if (!once.compareAndSet(false, true)) return
        if (BnShellLog.isEnabled(BnLogLevel.VERBOSE)) {
            val t = (clockForTest ?: SystemClock::elapsedRealtimeNanos)()
            write("boot.content", t, 0)
        }
        reportFullyDrawn()
    }

    /** True until [bootContentOnce] has run once on [once], which defaults to the process-wide
     * flag: a cheap read, so a caller can skip the work of deciding whether a batch is the first
     * with content. A test passes its own flag, as it does to [bootContentOnce]. */
    @JvmStatic
    fun bootContentPending(once: AtomicBoolean = bootContentFired): Boolean = !once.get()

    private fun write(point: String, t: Long, id: Long) {
        val seq = seqs.getOrPut(point) { AtomicLong() }.incrementAndGet()
        val line = if (id != 0L) "BNPERF $point seq=$seq t=$t id=$id" else "BNPERF $point seq=$seq t=$t"
        BnShellLog.verbose(TAG, line)
    }
}
