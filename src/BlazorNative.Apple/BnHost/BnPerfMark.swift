// ─────────────────────────────────────────────────────────────────────────────
// BnPerfMark — the iOS shell's half of the phase 19.2 measurement harness, the
// twin of the Android shell's BnPerfMark.kt.
//
// A mark is one line: `BNPERF <point> seq=<n> t=<ns>`, plus ` id=<handlerId>` on
// `dispatch.post`. `seq` is a per-point counter per process, starting at 1.
// scripts/perf/README.md holds the line contract; tools/BlazorNative.PerfReport
// parses it.
//
// The level gate is `BnLog.isEnabled(BnLogLevel.verbose)`, and it is the first
// statement of [mark]. A mark below Verbose returns there, before the clock read
// and before the line is built. BnPerfMarkTests pins that with a counting clock.
//
// The line goes out through `BnLog.verbose`, category `Perf`, with `.safe`
// privacy, because a REDACTED line would be written as `<private>` and hide the
// numbers. It holds a point name, two counters and a handler id. Every call site
// passes one of the seven point names as a string literal, and
// PerfMarkSiteDriftTests pins that for the Swift sources.
//
// Threads. Marks come from the main, lane and render threads. `seq` is assigned
// under `lock` and the line is handed to the sink and to BnLog after the lock is
// released, so two threads marking the same point get distinct seq values but their
// lines may reach the log in either order.
//
// Clocks. A span that crosses the shell/.NET boundary reads
// `clock_gettime_nsec_np(CLOCK_UPTIME_RAW)`. Boot is a wall-clock pair:
// `boot.start` is the process start the kernel recorded, `kp_proc.p_starttime`
// from sysctl KERN_PROC_PID, and `boot.content` is `CLOCK_REALTIME`, both in
// nanoseconds since the epoch, so the parser can subtract them. Every read goes
// through [clockForTest] when a test has set it.
// ─────────────────────────────────────────────────────────────────────────────

import Foundation
import Darwin

/// A once-flag a test can own. [BnPerfMark.bootContentOnce] takes one so a test does
/// not depend on the process-wide flag another test could have set first.
final class BnOnceFlag {
    private let lock = NSLock()
    private var fired = false

    /// True for exactly the first caller.
    func fire() -> Bool {
        lock.lock()
        defer { lock.unlock() }
        if fired { return false }
        fired = true
        return true
    }

    var hasFired: Bool {
        lock.lock()
        defer { lock.unlock() }
        return fired
    }
}

enum BnPerfMark {

    static let category = "Perf"

    private static let lock = NSLock()
    private static var seqs: [String: UInt64] = [:]
    private static var clockStorage: (() -> UInt64)?
    private static var sinkStorage: ((String) -> Void)?

    /// The process-wide flag for `boot.content`.
    static let bootContentFlag = BnOnceFlag()

    /// Replaces the nanosecond clock for [mark], [bootStart] and [bootContentOnce], so a
    /// test can count the reads. Nil in production. Guarded by a lock: marks come from
    /// the main, lane and render threads.
    static var clockForTest: (() -> UInt64)? {
        get { lock.lock(); defer { lock.unlock() }; return clockStorage }
        set { lock.lock(); defer { lock.unlock() }; clockStorage = newValue }
    }

    /// Receives every line [mark] and its boot siblings write, in addition to `BnLog`.
    /// Nil in production.
    static var sinkForTest: ((String) -> Void)? {
        get { lock.lock(); defer { lock.unlock() }; return sinkStorage }
        set { lock.lock(); defer { lock.unlock() }; sinkStorage = newValue }
    }

    /// Writes one mark for [point] when the level gate is at Verbose or finer.
    /// [id] is written as ` id=<id>` when it is not 0: handler ids start at 1, the same
    /// convention as .NET's BnPerfMarks.
    static func mark(_ point: String, id: UInt64 = 0) {
        guard BnLog.isEnabled(BnLogLevel.verbose) else { return }
        let t = (clockForTest ?? { clock_gettime_nsec_np(CLOCK_UPTIME_RAW) })()
        write(point, t, id)
    }

    /// `boot.start`: the process start the kernel recorded, in wall-clock nanoseconds.
    static func bootStart() {
        guard BnLog.isEnabled(BnLogLevel.verbose) else { return }
        let t = (clockForTest ?? processStartWallNs)()
        write("boot.start", t, 0)
    }

    /// `boot.content`, once per process. The mark is gated like every other, and the
    /// once-flag is consumed whether or not it writes, as on Android. [once] defaults to
    /// the process-wide flag; a test passes its own.
    static func bootContentOnce(once: BnOnceFlag = bootContentFlag) {
        guard once.fire() else { return }
        guard BnLog.isEnabled(BnLogLevel.verbose) else { return }
        let t = (clockForTest ?? { clock_gettime_nsec_np(CLOCK_REALTIME) })()
        write("boot.content", t, 0)
    }

    /// True until [bootContentOnce] has run once on [once]: one flag read, so a caller can
    /// skip the work of deciding whether a batch is the first with content.
    static func bootContentPending(once: BnOnceFlag = bootContentFlag) -> Bool {
        !once.hasFired
    }

    /// `kp_proc.p_starttime` of this process, as nanoseconds since the epoch. Falls back
    /// to the wall clock now when sysctl fails, so a mark is never silently missing.
    private static func processStartWallNs() -> UInt64 {
        var info = kinfo_proc()
        var size = MemoryLayout<kinfo_proc>.stride
        var mib: [Int32] = [CTL_KERN, KERN_PROC, KERN_PROC_PID, getpid()]
        let rc = sysctl(&mib, UInt32(mib.count), &info, &size, nil, 0)
        guard rc == 0 else { return clock_gettime_nsec_np(CLOCK_REALTIME) }
        let tv = info.kp_proc.p_un.__p_starttime
        return UInt64(tv.tv_sec) * 1_000_000_000 + UInt64(tv.tv_usec) * 1_000
    }

    private static func write(_ point: String, _ t: UInt64, _ id: UInt64) {
        lock.lock()
        let seq = (seqs[point] ?? 0) + 1
        seqs[point] = seq
        let sink = sinkStorage
        lock.unlock()
        let line = id != 0 ? "BNPERF \(point) seq=\(seq) t=\(t) id=\(id)" : "BNPERF \(point) seq=\(seq) t=\(t)"
        sink?(line)
        BnLog.verbose(category, line, privacy: .safe)
    }
}
