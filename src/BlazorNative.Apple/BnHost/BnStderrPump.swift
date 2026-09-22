// ─────────────────────────────────────────────────────────────────────────────
// BnStderrPump — Phase 11.4 Gate C: the iOS half of the stdio transport (design
// §5.1 Option 1, ADOPTED, and §5.5). The Swift twin of Android's
// `BnStderrLogcatPump`.
//
// THE PROBLEM, RESTATED. The shared .NET runtime writes its diagnostics to
// `Console.Error` — process fd 2. Android sends fd 2 to /dev/null; iOS sends it
// nowhere USEFUL, because an unattached Release build has no terminal on the
// other end and the unified log does not capture stdio. Either way the runtime's
// 31 managed diagnostic sites, the BCL's own output and NativeAOT's
// TypeLoadException detail have never been visible on the target platform. Gate A
// gave every managed line a LEVEL; this gives it a DESTINATION.
//
// ⚠ ZERO ABI COST, WHICH IS THE WHOLE REASON THIS SHAPE WAS CHOSEN. The runtime
// is not modified at all — the shell reads a file descriptor. The alternatives
// (a `HostCallOp = 5` on the bridge, or an 11th export) were both rejected in
// design §5.2/§5.3, and the deciding argument was coverage, not cost: neither can
// carry the output of the BCL, of the NativeAOT runtime itself, or of anything
// written before `register_bridge` — including `blazornative_init`'s failure
// path, the one place the framework deliberately emits a full `ex.ToString()`.
//
// ⚠ INSTALL ORDER IS LOAD-BEARING. This must run before `blazornative_init`.
// HostViewController installs it as the first statement after the XCTest guard,
// which is well before the boot thread calls `runtime.start()`.
//
// ⚠ NOT INSTALLED UNDER XCTEST, AND THAT IS DELIBERATE, NOT AN OVERSIGHT. The
// XCTest bundle is HOSTED — it runs inside this app's process, and `xcodebuild`
// reads the test runner's stdio. A pump that captured fd 2 in the test host would
// redirect XCTest's own output into the unified log and out of xcodebuild's
// hands. The install site is therefore inside HostViewController's existing
// "do not boot under tests" guard, which is also the only path that boots the
// runtime at all — so the transport covers exactly the process it exists for.
//
// ⚠ NOT INSTALLED WHEN `OS_ACTIVITY_DT_MODE` IS SET, AND THAT ABSENCE IS THE
// TRANSPORT (#17). A developer who sets that variable is asking the OS to MIRROR
// `os_log` output to fd 2 so a device console can show it — which is the ONLY
// remaining way to see [BnLog] `Debug` and `Verbose` on a real iPhone. Both
// levels map onto `OSLogType.debug`, which the unified log drops unless the
// subsystem is enabled, and `log config` has no `--device` flag; on macOS 26
// `log stream` lost device support, and `devicectl --console` carries fd 1 and
// fd 2 only. Installing the pump points fd 2 at ourselves, so the mirror lands
// in our own pipe and that last route closes — BY US. Measured on an iPhone 17
// Pro Max: exactly one UIKit line arrives before install, then nothing, even at
// Verbose.
//
// WHAT TO DO, THEN. To see `Debug`/`Verbose` on a device, set
// `OS_ACTIVITY_DT_MODE=YES` in the scheme's environment and read the console
// with `devicectl device process launch --console`. The pump stands aside for
// that launch, and the price is the pump's own value: the runtime's fd 2 output
// arrives as plain console text rather than tagged unified-log entries. WITHOUT
// the variable the pump IS installed and those two levels are not visible on a
// device at all. Recorded on the website's iOS shell page.
//
// ⚠ IRREVERSIBLE AND PROCESS-GLOBAL (design R2). `dup2` over fd 2 cannot be
// meaningfully undone and captures the descriptor for the WHOLE process,
// including any third-party native library. That is mostly the point. It is a
// COLLISION if the consumer's app also redirects fd 2 (Crashlytics/Sentry
// handlers do): last writer wins. This cannot be tested; it is written down here
// and in the shell docs. Hence [install] is idempotent and guarded.
//
// ⚠ CI-VERIFIED ONLY. Like every Swift file in this shell, this was not built or
// run on the authoring machine (no Mac, no Xcode) — the iOS lane is
// .github/workflows/ios.yml.
//
// See docs/plans/2026-07-21-phase-11.4-design.md §5.1, §5.5 and R2/R8.
// ─────────────────────────────────────────────────────────────────────────────

import Foundation

/// Redirects process stderr (fd 2) into the unified log via [BnLog].
///
/// Install once, as early as possible. See the file header for why the ordering
/// matters and why it can never be undone.
enum BnStderrPump {

    /// Emitted-line cap. The unified log truncates long entries anyway, and an
    /// unbounded buffer over a runaway writer is a heap grower. Mirrors Kotlin's
    /// `BnStderrPump.MAX_LINE`.
    static let maxLine = 4000

    /// THE SINK SEAM (design §8.1 pin 5). Production forwards to [BnLog]; a test
    /// swaps in a collector and asserts the level and category it observed —
    /// which is the honest pin, because scraping the unified log for a line is
    /// timing-sensitive and slow.
    static var sink: (BnLogRecord) -> Void = { record in
        BnLog.write(record.level, record.category, record.message)
    }

    private static let lock = NSLock()
    private static var installedFlag = false
    private static var source: DispatchSourceRead?

    /// Has a pump been installed in this process?
    static var isInstalled: Bool {
        lock.lock(); defer { lock.unlock() }
        return installedFlag
    }

    /// Creates the pipe, points fd 2 at it, and starts the reader.
    ///
    /// - Returns: true if THIS call installed the pump; false for any of THREE
    ///   distinct reasons — `OS_ACTIVITY_DT_MODE` is set, so the pump stands
    ///   aside and leaves the `os_log`-to-fd-2 mirror intact; the pump was
    ///   already installed (idempotent — the second caller is a no-op, not a
    ///   second `dup2`); or the install failed. A caller that needs to tell them
    ///   apart reads [isInstalled], which is false for the first and the third.
    ///
    /// NEVER THROWS AND NEVER TRAPS. A shell whose logging transport could abort
    /// launch would be strictly worse than a shell with no transport: the failure
    /// mode of a missing pump is "diagnostics go where they already went", and the
    /// failure mode of a trap is a blank app. A failed install is reported through
    /// [BnLog] at warn and swallowed.
    @discardableResult
    static func install() -> Bool {
        // OS_ACTIVITY_DT_MODE (#17). A developer who sets this is asking the OS to
        // MIRROR os_log to fd 2 so a device console can show it. Installing the pump
        // then points fd 2 at ourselves and the mirror lands in our own pipe -- so the
        // one documented route to seeing Debug and Verbose on a device is closed BY
        // US. Measured on an iPhone 17 Pro Max: exactly one UIKit line arrives before
        // install, then nothing, even at Verbose.
        //
        // Every other route is already shut: BnLog maps both levels onto
        // OSLogType.debug, which the unified log drops unless the subsystem is
        // enabled, and `log config` has no --device flag. On macOS 26 `log stream`
        // lost device support and `devicectl --console` carries only fd 1 and fd 2.
        //
        // So when the mirror is on, standing aside IS the transport. It also avoids a
        // cycle nobody should have to reason about: mirror writes fd 2, pump reads
        // fd 2, BnLog writes os_log, mirror writes fd 2.
        //
        // BEFORE the lock and BEFORE the idempotence guard, deliberately: checking
        // after `installedFlag = true` would leave the flag — and the public
        // [isInstalled] — asserting a pump that has no pipe and no dup2 behind it.
        //
        // `getenv` and not `ProcessInfo.processInfo.environment`: this reads live
        // `environ`, which is what ProcessInfo reads underneath anyway, and it is the
        // honest shape for a variable that can change after launch. ProcessInfo is
        // documented as the environment the process was LAUNCHED from, and if that
        // dictionary is ever snapshotted then a `setenv` after start is invisible to
        // it -- which would red this file's XCTest in the iOS lane, the one place
        // nobody can check locally.
        if getenv("OS_ACTIVITY_DT_MODE") != nil {
            // NOT VISIBLE AT THE DEFAULT LEVEL, and that is recorded rather than
            // fixed. [install] runs before `BnRuntime.resolveLogLevel`, so the
            // threshold here is always [BnLog.defaultLevel] -- `warn` -- and even
            // `BN_LOG_LEVEL=Debug` does not rescue this line, because the level that
            // env var names has not been applied yet.
            //
            // It stays at `info` ON PURPOSE. Standing aside is REQUESTED behaviour,
            // not a fault, and borrowing `warn` to make a line discoverable is crying
            // wolf in a repo that spent this whole milestone making levels mean what
            // they say. It stays at all because it becomes correct the moment anything
            // reorders boot. What a developer actually needs is in this file's header
            // and on the website's iOS shell page.
            BnLog.info("native", "stderr pump: standing aside — OS_ACTIVITY_DT_MODE is "
                + "set, so os_log is mirrored to fd 2 and the pump would capture it",
                privacy: .safe)
            return false
        }

        lock.lock()
        if installedFlag { lock.unlock(); return false }
        // Set BEFORE the syscalls: a partial install must not leave the guard open
        // for a second attempt to re-dup fd 2.
        installedFlag = true
        lock.unlock()

        var fds: [Int32] = [0, 0]
        guard pipe(&fds) == 0 else {
            BnLog.warn("native", "stderr pump: pipe() failed (errno \(errno)) — the runtime's "
                + "diagnostics stay on a stderr iOS does not surface", privacy: .safe)
            return false
        }
        let readFd = fds[0]
        let writeFd = fds[1]

        // fd 2 now refers to the pipe's write end.
        guard dup2(writeFd, STDERR_FILENO) >= 0 else {
            close(readFd)
            close(writeFd)
            BnLog.warn("native", "stderr pump: dup2() failed (errno \(errno)) — the runtime's "
                + "diagnostics stay on a stderr iOS does not surface", privacy: .safe)
            return false
        }
        // The duplicate in `writeFd` is redundant now; fd 2 holds the pipe open.
        // Closing it avoids leaking a descriptor for the process lifetime and keeps
        // the writer count at exactly one.
        close(writeFd)

        // A DISPATCH SOURCE, NOT A BLOCKED THREAD — the one place this file
        // deliberately diverges from the Kotlin twin. Android's pump owns a daemon
        // thread parked in `read()` forever (design R8 accepts it); GCD gives the
        // same "wake when readable" for free on a shared queue, so iOS pays a queue
        // and no permanent thread of its own. The reading is otherwise identical:
        // the same line splitting, the same partial-line buffering, the same cap.
        let queue = DispatchQueue(label: "io.blazornative.stderr-pump", qos: .utility)
        let readSource = DispatchSource.makeReadSource(fileDescriptor: readFd, queue: queue)

        var pending = Data()
        var overflowed = false

        readSource.setEventHandler {
            var buffer = [UInt8](repeating: 0, count: 4096)
            let count = buffer.withUnsafeMutableBytes { raw -> Int in
                read(readFd, raw.baseAddress, raw.count)
            }
            // #213 item 4 — HANDLE EOF, don't just skip it. `count == 0` is EOF (every
            // writer closed fd 2); `count < 0` is an error (EINTR aside). The old code
            // was `guard count > 0 else { return }` on both, which had two faults the
            // Kotlin twin does not: the buffered PARTIAL LINE was never flushed (the
            // shared drain flushes its tail on EOF — BnLogFormat.drain's
            // `if (pending.isNotEmpty()) emit(...)`), and the source was left resumed on
            // a fd that is now permanently readable-but-empty, so it re-fires in a tight
            // loop reading zero forever. Flush the tail, then CANCEL: the cancel handler
            // closes readFd, which is the only correct way to stop a dispatch read source.
            guard count > 0 else {
                if count == 0 { flushPending(pending: &pending, overflowed: &overflowed) }
                readSource.cancel()
                return
            }
            drain(Data(buffer[0..<count]), pending: &pending, overflowed: &overflowed)
        }
        readSource.setCancelHandler {
            close(readFd)
        }
        readSource.resume()
        source = readSource
        return true
    }

    // ── the line machinery — the pure half, mirroring Kotlin's BnStderrPump ───
    //
    // Each piece is a real hazard rather than defensive decoration:
    //
    //  · PARTIAL LINES. `Console.Error.WriteLine` auto-flushes, so lines arrive
    //    whole IN PRACTICE — but a read boundary can still land mid-line, and a
    //    naive read → decode → split emits two half-lines with the second losing
    //    its prefix, and therefore its LEVEL. Bytes are buffered until `\n`.
    //  · A CAP. A line longer than [maxLine] is emitted at the cap and the
    //    remainder dropped up to the next newline. Keeping the HEAD rather than
    //    the tail is deliberate: the `[BN|L|category]` prefix lives at the front,
    //    so a truncated line still arrives at the right level under the right
    //    category.
    //  · UTF-8 ACROSS THE BOUNDARY. Decoding happens per line, on whole lines, so
    //    a multi-byte sequence split across a read cannot become mojibake.
    //  · A THROWING SINK CANNOT KILL THE PUMP — Swift has no checked exceptions
    //    here, but the sink is called on the source's queue and must not be given
    //    a chance to reenter; it is called last, after all state is updated.

    /// Feeds one chunk through the line splitter, emitting every COMPLETE line.
    /// Split out from the event handler so the .NET-side and any future XCTest can
    /// drive it without a pipe.
    static func drain(_ chunk: Data, pending: inout Data, overflowed: inout Bool) {
        var start = chunk.startIndex
        for i in chunk.indices where chunk[i] == UInt8(ascii: "\n") {
            append(chunk[start..<i], to: &pending, overflowed: &overflowed)
            start = chunk.index(after: i)
            emit(pending, overflowed)
            pending = Data()
            overflowed = false
        }
        if start < chunk.endIndex {
            append(chunk[start..<chunk.endIndex], to: &pending, overflowed: &overflowed)
        }
    }

    /// #213 item 4 — EMIT THE BUFFERED TAIL AT EOF. `drain` only emits on `\n`, so a
    /// final line with no trailing newline sits in `pending` forever. On EOF the writer
    /// is gone and none is coming, so the tail is a complete line — flush it, exactly as
    /// the Kotlin twin's shared drain does (`if (pending.isNotEmpty()) emit(...)`).
    /// Idempotent: an empty `pending` emits nothing, so a double EOF (or a cancel that
    /// races a final read) cannot double-log. Split out so an XCTest can drive it through
    /// the injected [sink] rather than a real pipe closing.
    static func flushPending(pending: inout Data, overflowed: inout Bool) {
        guard !pending.isEmpty else { return }
        emit(pending, overflowed)
        pending = Data()
        overflowed = false
    }

    /// Appends, never past [maxLine]; records whether anything was dropped.
    private static func append(_ slice: Data, to pending: inout Data, overflowed: inout Bool) {
        let room = max(0, maxLine - pending.count)
        if slice.count > room { overflowed = true }
        guard room > 0 else { return }
        pending.append(contentsOf: slice.prefix(room))
    }

    private static func emit(_ bytes: Data, _ overflowed: Bool) {
        var body = bytes
        if body.last == UInt8(ascii: "\r") { body = body.dropLast() } // tolerate CRLF
        if body.isEmpty && !overflowed { return } // a bare newline is not a log line

        var text = String(decoding: body, as: UTF8.self)
        if overflowed { text += " …[truncated]" }
        sink(BnLogFormat.parse(text))
    }
}
