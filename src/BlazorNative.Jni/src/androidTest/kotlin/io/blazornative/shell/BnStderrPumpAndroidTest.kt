package io.blazornative.shell

import android.os.ParcelFileDescriptor
import android.system.Os
import androidx.test.ext.junit.runners.AndroidJUnit4
import io.blazornative.jni.BnLogFormat
import io.blazornative.jni.BnLogRecord
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.io.FileDescriptor
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit

// ─────────────────────────────────────────────────────────────────────────────
// BnStderrPumpAndroidTest — Phase 11.4 Gate B, design §8.2.
//
// ⚠ CI/EMULATOR-ONLY, AND IT HAS NOW RUN — TWICE. There is no device in this dev
// loop, so the file rides the nightly/dispatched instrumented lane like every
// other androidTest here; `compileDebugAndroidTestKotlin` is the only local
// proof, and it proves compilation, not behaviour. The two runs that matter,
// both API 34 google_apis x86_64 (Pixel 6 AVD):
//
//   1. `f53f74a` (2026-07-22) — 211/212. The single failure was this file's one
//      probe, reporting only `expected:<…> but was:<null>`. Not enough to say
//      whether the pump or the probe was wrong (#191).
//   2. `fix/191-android-pump-diagnostic` — 212/213, and the one failure was the
//      NEW discriminator, which carried the answer outright:
//        state=Installed isInstalled=true dup2Result=2 readFd=84 writeFd(closed)=-1
//        FileDescriptor.err=54 readerThread=alive=true
//      i.e. `dup2` returned 2, the reader was alive, THE CONTROL WRITE THROUGH AN
//      EXPLICIT fd 2 PASSED — and `FileDescriptor.err` was a dup sitting at fd 54.
//
// SO THE TRANSPORT WORKS ON A REAL ANDROID RUNTIME. That is now measured, not
// argued, and [stderrWrites_reachTheSink_throughTheRealDup2] is the proof.
// #191's root cause was test-side: the original probe wrote to the wrong
// descriptor. See [javaFileDescriptorErr_isNotTheProcessStderr] for the trap.
//
// WHAT ONLY A DEVICE CAN PROVE, and therefore the only reason this file exists:
// that `Os.pipe()` + `Os.dup2(writeFd, 2)` ACTUALLY CAPTURES fd 2 on a real
// Android runtime. The JVM lane (BnStderrPumpTest) pins everything downstream of
// that — line splitting, partial lines, the format round trip, the fallback —
// over an in-memory stream, because those are pure functions. It cannot pin the
// syscall: `android.system.Os` is one of the android.jar methods AGP's unit-test
// lane throws "not mocked" from.
//
// AND IT STILL ASSERTS AGAINST THE INJECTED SINK, NOT `logcat -d` (design §8.1
// pin 5). Scraping logcat is timing-sensitive, needs a settling delay, and fails
// for reasons that have nothing to do with the pump. Observing the line in logcat
// with human eyes belongs in Phase 11.2's device runbook, which is the repo's home
// for recorded-not-asserted device truth.
//
// ── #191: WHY THERE ARE TWO PROBES, AND WHY ONE OF THEM ASSERTS A TRAP ───────
// Run 1's message could not separate two hypotheses with OPPOSITE fixes:
//
//   H-TRANSPORT — `dup2` did not take, or fd 2 was re-pointed/closed after the
//     install and the reader hit EOF. The pump would be wrong and #155's Android
//     half undelivered. **DISPROVED** by run 2: dup2Result=2, reader alive, and
//     the explicit-fd-2 write round-tripped through the sink.
//   H-PROBE — `FileDescriptor.err` does NOT hold the literal descriptor 2 (ART
//     hands out a dup, whose open file description is the pre-install
//     /dev/null), so the write succeeds and goes nowhere near the pipe.
//     **CONFIRMED**: `FileDescriptor.err=54`.
//
// Hence the asymmetry that remains, deliberately:
//   [stderrWrites_reachTheSink_throughTheRealDup2] writes to the descriptor the
//     pump captured — fd 2 itself. THE LOAD-BEARING PROOF, an exact assertEquals
//     on the record; it can still fail and must be able to.
//   [javaFileDescriptorErr_isNotTheProcessStderr] pins the TRAP that cost this
//     issue a dispatch: on Android, `FileDescriptor.err` is not fd 2, so it is
//     the wrong instrument for probing a `dup2` over fd 2.
//
// The corroborating side channel from run 1, kept because it is independent
// evidence for the same conclusion: that logcat artifact carries 71 lines tagged
// `W BlazorNative/native: s_glBindAttribLocation: …` — the pump's own
// unprefixed-line fallback (BnLogFormat.FALLBACK_PRIORITY/CATEGORY), i.e. the
// emulator's GL layer writing to fd 2 and coming out of the sink. All on ONE
// tid, which logs nothing else (the reader thread), spanning 14:37:16 to
// 14:40:27 — straddling the 14:39:41 failure. The pump was working the whole
// time; only the probe's own bytes went missing.
//
// Every assertion message carries [diagnostics], so a failure prints the
// discriminator (dup2's result, the pipe's fds, the reader thread's liveness)
// instead of a bare `null`. That is how run 2 answered in one shot.
// ─────────────────────────────────────────────────────────────────────────────

@RunWith(AndroidJUnit4::class)
class BnStderrPumpAndroidTest {

    /**
     * Everything known about the transport at probe time, as one line.
     *
     * Read fresh on every assertion rather than captured once: the reader
     * thread's liveness is the interesting half, and it is only interesting as
     * of the moment the probe gave up.
     */
    private fun diagnostics(): String {
        val d = BnStderrLogcatPump.installDiagnostics
        val reader = Thread.getAllStackTraces().keys
            .firstOrNull { it.name == BnStderrLogcatPump.THREAD_NAME }
        return buildString {
            append("state=").append(BnStderrLogcatPump.installState())
            append(" isInstalled=").append(BnStderrLogcatPump.isInstalled())
            append(" dup2Result=").append(d?.let { fdInt(it.dup2Result) } ?: "n/a")
            append(" readFd=").append(d?.let { fdInt(it.readFd) } ?: "n/a")
            // -1 EXPECTED: install() closes the write end once fd 2 holds the
            // pipe, and Os.close blanks the FileDescriptor object.
            append(" writeFd(closed)=").append(d?.let { fdInt(it.writeFd) } ?: "n/a")
            append(" FileDescriptor.err=").append(fdInt(FileDescriptor.err))
            append(" readerThread=").append(
                if (reader == null) "ABSENT" else "alive=${reader.isAlive}"
            )
        }
    }

    /**
     * The integer inside a [FileDescriptor], or a reason it could not be read.
     *
     * `getInt$` is libcore-private, so this goes through reflection and may be
     * refused by the hidden-API policy on some images. A diagnostic that cannot
     * itself fail the test is the point — hence the string return.
     */
    private fun fdInt(fd: FileDescriptor): String =
        try {
            val m = FileDescriptor::class.java.getDeclaredMethod("getInt$")
            m.isAccessible = true
            (m.invoke(fd) as Int).toString()
        } catch (t: Throwable) {
            "unreadable(${t.javaClass.simpleName})"
        }

    /**
     * A [FileDescriptor] that names descriptor 2 EXPLICITLY, so the write cannot
     * inherit whatever `FileDescriptor.err` happens to point at.
     *
     * Preferred path is `setInt$(2)` — the literal descriptor, no duplication.
     * If the hidden-API policy refuses that, the fallback is
     * `ParcelFileDescriptor.fromFd(2)`, which DUPS fd 2 — and a dup shares the
     * OPEN FILE DESCRIPTION, so a write to it lands wherever fd 2 currently
     * points, which is exactly the question being asked. (`Os.dup(
     * FileDescriptor.err)` would NOT do: it would reproduce the very ambiguity
     * this probe exists to remove.) The returned handle is closed by the caller
     * only when it is the dup; descriptor 2 itself is never ours to close.
     */
    private fun descriptorTwo(): Pair<FileDescriptor, ParcelFileDescriptor?> =
        try {
            val fd = FileDescriptor()
            val m = FileDescriptor::class.java.getDeclaredMethod("setInt$", Int::class.javaPrimitiveType)
            m.isAccessible = true
            m.invoke(fd, 2)
            fd to null
        } catch (t: Throwable) {
            val dup = ParcelFileDescriptor.fromFd(2)
            dup.fileDescriptor to dup
        }

    private fun write(fd: FileDescriptor, message: String) {
        val bytes = (BnLogFormat.format(BnLogFormat.ERROR, "runtime", message) + "\n")
            .toByteArray(Charsets.UTF_8)
        Os.write(fd, bytes, 0, bytes.size)
    }

    /**
     * The record carrying [message], or null if it never arrived within
     * [timeoutMs].
     *
     * MATCHES ON THE MESSAGE rather than taking whatever the queue offers first:
     * fd 2 is PROCESS-WIDE, so the emulator's GL layer and any third-party native
     * library write there too (run 1's logcat shows 71 such lines). A bare
     * `poll()` would happily return someone else's line and turn either
     * assertion below into a coin flip.
     */
    private fun awaitRecord(
        seen: LinkedBlockingQueue<BnLogRecord>,
        message: String,
        timeoutMs: Long,
    ): BnLogRecord? {
        val deadline = System.nanoTime() + TimeUnit.MILLISECONDS.toNanos(timeoutMs)
        while (true) {
            val remaining = deadline - System.nanoTime()
            if (remaining <= 0) return null
            val record = seen.poll(remaining, TimeUnit.NANOSECONDS) ?: return null
            if (record.message == message) return record
        }
    }

    /**
     * THE TRANSPORT'S CONTRACT, AND THE ONE PROOF THE WHOLE FILE EXISTS FOR:
     * bytes written to descriptor 2 — the one the pump `dup2`'d over — come out
     * of the sink, parsed.
     *
     * **GREEN on run 2 (API 34 x86_64):** `dup2Result=2 readFd=84 readerThread=
     * alive=true`, and the `[BN|E|runtime]` line round-tripped. That is the
     * Android transport verified on a real runtime, which is what Gate B claimed
     * and could not previously show.
     *
     * It stays a strict `assertEquals` on the whole [BnLogRecord] — priority,
     * category and message — and it MUST remain able to fail: a regression in
     * `install()` is exactly what it is here to catch. A failure now means the
     * pump, not the probe; the message carries the dup2 result and the reader
     * thread's liveness, which separate "the dup2 never took" from "the reader
     * died / hit EOF afterwards".
     */
    @Test
    fun stderrWrites_reachTheSink_throughTheRealDup2() {
        val seen = LinkedBlockingQueue<BnLogRecord>()
        // Swap the sink BEFORE installing. The pump may already be installed —
        // MainActivity installs it in onCreate and the instrumented session is
        // process-shared — which is exactly why the seam is a settable sink rather
        // than an install parameter.
        val previous = BnStderrLogcatPump.sink
        BnStderrLogcatPump.sink = { record -> seen.put(record) }
        val (two, dupHandle) = descriptorTwo()
        try {
            BnStderrLogcatPump.install()
            // NOT VACUOUS ANY MORE (#191): before the tri-state fix this read
            // true even when the install threw, so it could never fail.
            assertTrue("the pump is not running after install() — ${diagnostics()}",
                BnStderrLogcatPump.isInstalled())

            // Write to fd 2 the way the NativeAOT runtime does — bytes, not a JVM
            // System.err.println (which ART routes through its own path).
            write(two, "device pump probe")

            val record = awaitRecord(seen, "device pump probe", 5_000)
            assertEquals(
                "nothing reached the sink from an explicit descriptor 2 — the dup2 " +
                    "over fd 2 did not take, or the reader is gone — ${diagnostics()}",
                BnLogRecord(BnLogFormat.ERROR, "runtime", "device pump probe"), record)
        } finally {
            BnStderrLogcatPump.sink = previous
            runCatching { dupHandle?.close() }
        }
    }

    /**
     * THE TRAP, PINNED: on Android, `java.io.FileDescriptor.err` is **not** the
     * process's stderr descriptor, so it is the wrong instrument for probing a
     * `dup2` over fd 2.
     *
     * THIS IS A PLATFORM FACT, NOT A DEFECT — which is why it is asserted as an
     * invariant instead of left as a permanent red. ART hands out a **dup** of
     * the descriptor as it stood at process init; that dup keeps the ORIGINAL
     * open file description (the `/dev/null` Android gives stderr), so a write
     * through it is a successful write to nowhere. It never touches the pipe the
     * pump installed over fd 2, and `Os.write` reports no error, which is
     * precisely what makes it a trap: the probe looks like it worked.
     *
     * **MEASURED (#191, run 2, API 34 google_apis x86_64):**
     * ```
     * state=Installed isInstalled=true dup2Result=2 readFd=84 writeFd(closed)=-1
     * FileDescriptor.err=54 readerThread=alive=true
     * ```
     * `FileDescriptor.err` was **fd 54** while the pump held fd 2 and its reader
     * was alive. That single line is what turned #191 from a suspected transport
     * bug into a confirmed test bug.
     *
     * IT DOCUMENTS RATHER THAN FORBIDS. The assertion is on the CONSEQUENCE, and
     * both worlds are legal:
     * - `err != 2` (today, fd 54) — the write must NOT reach the sink.
     * - `err == 2` (a future Android that stops duping, or a differently
     *   configured runtime) — then it is genuinely the process stderr and the
     *   write MUST reach the sink; the branch below asserts exactly that.
     *
     * A platform change therefore flips the branch, not the colour. If it ever
     * does flip, that is worth knowing — the trap would be gone and
     * the original probe would have been valid all along — but it is not a
     * regression in this framework, and this test will say so by staying green
     * down the other branch.
     */
    @Test
    fun javaFileDescriptorErr_isNotTheProcessStderr() {
        val seen = LinkedBlockingQueue<BnLogRecord>()
        val previous = BnStderrLogcatPump.sink
        BnStderrLogcatPump.sink = { record -> seen.put(record) }
        try {
            BnStderrLogcatPump.install()
            assertTrue("the pump is not running, so this probe cannot mean anything — " +
                diagnostics(), BnStderrLogcatPump.isInstalled())

            val probe = "device pump probe via FileDescriptor.err"
            val err = fdInt(FileDescriptor.err)
            write(FileDescriptor.err, probe)
            // 2s, not 5: the positive path delivers in microseconds (the reader
            // is blocked in read()), and the negative path pays this in full.
            val record = awaitRecord(seen, probe, 2_000)

            if (err == "2") {
                // The other world. `FileDescriptor.err` IS the descriptor the
                // pump captured, so the bytes must come back out of the sink.
                assertEquals(
                    "FileDescriptor.err holds the literal descriptor 2 on this runtime — the " +
                        "Android dup described in this test's KDoc is gone — yet a write " +
                        "through it did not reach the pump. Then the transport is broken: " +
                        diagnostics(),
                    BnLogRecord(BnLogFormat.ERROR, "runtime", probe), record)
            } else {
                // Today's world, and the reason the real probe uses an explicit
                // descriptor 2. Measured: err=54.
                assertNull(
                    "FileDescriptor.err is fd $err, NOT the process stderr, so its write " +
                        "cannot reach the pump's pipe — yet a record arrived. Either the " +
                        "descriptor was re-pointed at fd 2's description or the sink saw " +
                        "someone else's identical line: " + diagnostics(),
                    record)
            }
        } finally {
            BnStderrLogcatPump.sink = previous
        }
    }

    @Test
    fun install_isIdempotent() {
        // A second dup2 would re-point fd 2 at a NEW pipe and orphan the first
        // reader — every line written between the two installs would be lost, and
        // a second daemon thread would block on a descriptor nobody writes to.
        // Activity recreation re-enters onCreate, so this is a live path.
        BnStderrLogcatPump.install()
        assertFalse("a second install() must be a no-op, not a second dup2 — ${diagnostics()}",
            BnStderrLogcatPump.install())
    }
}
