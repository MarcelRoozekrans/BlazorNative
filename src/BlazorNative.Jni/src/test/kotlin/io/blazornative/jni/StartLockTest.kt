package io.blazornative.jni

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Assertions.fail
import org.junit.jupiter.api.Test
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference

/**
 * Phase 18.4 (#511), the start lock: two start() calls are never inside the .NET session at once.
 *
 * The native session is process-wide, and start() runs init, the frame-callback registration,
 * the bridge registration and the mount against it. A recreated Activity's boot can call start()
 * while an older boot is still inside it. [BlazorNativeRuntime.start] therefore runs its whole body
 * under one process-wide lock in the companion object.
 *
 * The pin holds runtime A's start() inside the body through `insideStartHookForTest`, which runs
 * after init and before the frame-callback registration, and selects A by identity. Runtime B's
 * start() then begins on another thread. The probe `enteredStartForTest` counts entries into the
 * body, so B entering while A is held shows as 2.
 *
 * Anchor: the probe reads 1 once A is held, so A really is inside the body and the probe counts.
 *
 * DOES NOT COVER: the ORDER between two Activities' boots. The lock serializes them but does not
 * say which goes first; that is MainActivity's boot record, pinned on a device. Nor the lane: a
 * dispatch on an old runtime's lane is serialized by retire(), not by this lock. iOS is not
 * affected: it never recreates its root view controller, and it boots one BnRuntime once.
 */
class StartLockTest {

    @Test
    fun a_second_start_waits_for_the_first_to_leave_the_lock() {
        val a = BlazorNativeRuntime(onFrame = {})
        val b = BlazorNativeRuntime(onFrame = {})
        val aInside = CountDownLatch(1)
        val releaseA = CountDownLatch(1)
        val aLines = AtomicReference<List<String>>()
        val bLines = AtomicReference<List<String>>()
        val aError = AtomicReference<Throwable>()
        val bError = AtomicReference<Throwable>()
        var ta: Thread? = null
        var tb: Thread? = null

        // The probe is process-wide and other classes' tests start runtimes too.
        BlazorNativeRuntime.enteredStartForTest.set(0)
        BlazorNativeRuntime.insideStartHookForTest = { runtime ->
            if (runtime === a) {
                aInside.countDown()
                if (!releaseA.await(30, TimeUnit.SECONDS)) {
                    throw AssertionError("releaseA was not counted down within 30 s: runtime A stayed held")
                }
            }
        }
        try {
            ta = Thread({
                try { aLines.set(a.start(platformOs = "test-host", apiLevel = 0)) } catch (t: Throwable) { aError.set(t) }
            }, "StartLockTest-A").apply { isDaemon = true; start() }
            if (!aInside.await(30, TimeUnit.SECONDS)) {
                fail<Unit>("aInside did not fire within 30 s: runtime A never reached the hook; error: ${aError.get()}")
            }
            assertEquals(1, BlazorNativeRuntime.enteredStartForTest.get(), "anchor: A is inside start()")

            tb = Thread({
                try { bLines.set(b.start(platformOs = "test-host", apiLevel = 0)) } catch (t: Throwable) { bError.set(t) }
            }, "StartLockTest-B").apply { isDaemon = true; start() }
            Thread.sleep(500)
            assertEquals(1, BlazorNativeRuntime.enteredStartForTest.get(),
                "a second start() entered while the first was inside it: two threads in the .NET session")

            releaseA.countDown()
            ta.join(30_000)
            assertTrue(!ta.isAlive, "runtime A's start() did not return within 30 s of its release")
            tb.join(30_000)
            assertTrue(!tb.isAlive, "runtime B's start() did not return within 30 s of A's release")
            assertTrue(aLines.get()?.lastOrNull()?.contains("[BOOT] mounted HelloComponent") == true,
                "runtime A's start() did not return its mounted line: ${aLines.get()}; error: ${aError.get()}")
            assertTrue(bLines.get()?.lastOrNull()?.contains("[BOOT] mounted HelloComponent") == true,
                "runtime B's start() did not return its mounted line: ${bLines.get()}; error: ${bError.get()}")
        } finally {
            releaseA.countDown()
            BlazorNativeRuntime.insideStartHookForTest = null
            // A failed assertion above leaves the threads running; let them finish before the next
            // test boots, so neither is still inside the session then.
            ta?.join(30_000)
            tb?.join(30_000)
        }
    }
}
