package io.blazornative.shell

import android.os.ParcelFileDescriptor
import android.system.Os
import androidx.test.ext.junit.runners.AndroidJUnit4
import io.blazornative.jni.BnLogFormat
import io.blazornative.jni.BnLogRecord
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.io.FileDescriptor
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit

// ─────────────────────────────────────────────────────────────────────────────
// BnStderrPumpAndroidTest — Phase 11.4 Gate B, design §8.2.
//
// ⚠ CI/EMULATOR-ONLY. Nothing in this file has ever run on a developer machine:
// there is no device in the dev loop, so it rides the nightly/dispatched
// instrumented lane like every other androidTest here. `compileDebugAndroidTest-
// Kotlin` is the only local proof, and it proves compilation, not behaviour.
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
// ── #191: WHY THERE ARE NOW TWO PROBES, AND WHAT EACH ONE DECIDES ────────────
// The first dispatch of this file FAILED with `expected:<…> but was:<null>` and
// nothing else — a message that cannot distinguish "the transport is broken"
// from "the probe never wrote to fd 2". Two hypotheses, OPPOSITE fixes:
//
//   H-TRANSPORT — `dup2` did not take, or fd 2 was re-pointed/closed after the
//     install and the reader hit EOF. The pump is wrong; #155's Android half is
//     undelivered.
//   H-PROBE — `FileDescriptor.err` in this process does NOT hold the literal
//     descriptor 2 (ART may hand out a dup taken at zygote time, whose open file
//     description is still the pre-install /dev/null). Then the probe's write
//     succeeds, goes nowhere near the pipe, and the pump is fine. A TEST bug.
//
// So the two probes below are deliberately asymmetric:
//   [stderrWrites_reachTheSink_throughTheRealDup2] writes to the descriptor the
//     pump captured — fd 2 itself — and is therefore the transport's real
//     contract. RED ⇒ H-TRANSPORT.
//   [javaFileDescriptorErr_isTheProcessStderr] writes the way the first version
//     did, via `FileDescriptor.err`, and additionally asserts that descriptor's
//     integer. RED while the other is GREEN ⇒ H-PROBE, and the printed integer
//     names the fd the write actually went to.
//
// THE FAILING RUN'S LOGCAT ALREADY LEANS ONE WAY, and the probes are here to
// settle it rather than to argue it. That artifact carries 71 lines tagged
// `W BlazorNative/native: s_glBindAttribLocation: …` — the pump's own
// unprefixed-line fallback (BnLogFormat.FALLBACK_PRIORITY/CATEGORY), i.e. the
// emulator's GL layer writing to fd 2 and coming out of the sink. They are all
// on ONE tid, which logs nothing else — the reader thread — and they span
// 14:37:16 to 14:40:27, straddling the 14:39:41 failure. There is also NO
// "pump could not be installed" warning. So fd 2 was captured, the reader was
// alive, and only the probe's own bytes went missing. That is H-PROBE, but it is
// inference from a side channel; the two probes below make the next dispatch
// state it outright.
//
// Every assertion message carries [diagnostics], so a failure prints the
// discriminator (dup2's result, the pipe's fds, the reader thread's liveness)
// instead of a bare `null`. THIS IS NOT A WEAKENING: both probes assert equality
// against the exact expected record, with the same 5-second budget as before.
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
     * THE TRANSPORT'S CONTRACT: bytes written to descriptor 2 — the one the pump
     * dup2'd over — come out of the sink, parsed.
     *
     * Failure here means H-TRANSPORT: the pump, not the probe. The message
     * carries the dup2 result and the reader thread's liveness, which separate
     * "the dup2 never took" from "the reader died / hit EOF afterwards".
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

            val record = seen.poll(5, TimeUnit.SECONDS)
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
     * `FileDescriptor.err` IS descriptor 2 in this process, and writing through
     * it therefore reaches the pump.
     *
     * This is the probe the first dispatch used. Failing here while the probe
     * above passes means H-PROBE — ART did not hand out the literal fd 2, the
     * write bypassed the pipe, and the ORIGINAL failure was a test bug rather
     * than a broken transport. The asserted integer names the fd it really used.
     */
    @Test
    fun javaFileDescriptorErr_isTheProcessStderr() {
        val seen = LinkedBlockingQueue<BnLogRecord>()
        val previous = BnStderrLogcatPump.sink
        BnStderrLogcatPump.sink = { record -> seen.put(record) }
        try {
            BnStderrLogcatPump.install()

            assertEquals(
                "java.io.FileDescriptor.err does not hold the literal descriptor 2, so a " +
                    "write through it never reaches the pump's pipe — ${diagnostics()}",
                "2", fdInt(FileDescriptor.err))

            write(FileDescriptor.err, "device pump probe via FileDescriptor.err")

            val record = seen.poll(5, TimeUnit.SECONDS)
            assertEquals(
                "nothing reached the sink through FileDescriptor.err — ${diagnostics()}",
                BnLogRecord(BnLogFormat.ERROR, "runtime", "device pump probe via FileDescriptor.err"),
                record)
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
