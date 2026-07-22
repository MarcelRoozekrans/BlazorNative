package io.blazornative.shell

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
// ⚠ CI/EMULATOR-ONLY. This test was NOT RUN when Gate B landed: no device was
// attached and booting one was out of scope. It rides the nightly instrumented
// lane like every other androidTest here.
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
// ─────────────────────────────────────────────────────────────────────────────

@RunWith(AndroidJUnit4::class)
class BnStderrPumpAndroidTest {

    @Test
    fun stderrWrites_reachTheSink_throughTheRealDup2() {
        val seen = LinkedBlockingQueue<BnLogRecord>()
        // Swap the sink BEFORE installing. The pump may already be installed —
        // MainActivity installs it in onCreate and the instrumented session is
        // process-shared — which is exactly why the seam is a settable sink rather
        // than an install parameter.
        val previous = BnStderrLogcatPump.sink
        BnStderrLogcatPump.sink = { record -> seen.put(record) }
        try {
            BnStderrLogcatPump.install()
            assertTrue("the pump reports itself uninstalled after install()",
                BnStderrLogcatPump.isInstalled())

            // Write to fd 2 the way the NativeAOT runtime does — bytes, not a JVM
            // System.err.println (which ART routes through its own path).
            val line = BnLogFormat.format(BnLogFormat.ERROR, "runtime", "device pump probe") + "\n"
            Os.write(FileDescriptor.err, line.toByteArray(Charsets.UTF_8), 0, line.toByteArray(Charsets.UTF_8).size)

            val record = seen.poll(5, TimeUnit.SECONDS)
            assertEquals("nothing reached the sink — the dup2 over fd 2 did not take",
                BnLogRecord(BnLogFormat.ERROR, "runtime", "device pump probe"), record)
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
        assertFalse("a second install() must be a no-op, not a second dup2",
            BnStderrLogcatPump.install())
    }
}
