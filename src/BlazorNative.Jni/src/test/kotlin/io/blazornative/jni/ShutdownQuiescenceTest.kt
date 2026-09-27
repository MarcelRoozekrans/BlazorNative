package io.blazornative.jni

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.util.Collections
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicInteger

/**
 * Phase 16.1 — [BlazorNativeRuntime.shutdown] is QUIESCENT, even against a late
 * continuation.
 *
 * Since 16.1 frames are not confined to host calls: a handler that awaited a host call
 * re-renders from a continuation on .NET's render thread when that call completes, with
 * no export in progress. `shutdown()` calls `blazornative_shutdown`, which closes the
 * session's frame gate, waits out callbacks in flight, clears the callback and joins the
 * render thread before it returns. This pins the shell-visible result through the
 * published dll: once `shutdown()` has returned, completing the held call delivers no
 * frame to the shut-down runtime, NOR to a replacement that registered its own callback
 * afterwards.
 *
 * The replacement matters. Without it, the cleared callback pointer alone would drop a
 * late frame, and the pin could not tell a quiescent session from an absent callback:
 * before 16.1's gate, `blazornative_shutdown` only cleared the pointer, and a late frame
 * would have gone to whichever callback registered next.
 *
 * Positive control: the same held call released WITHOUT shutdown does deliver the
 * continuation's frame, so the pin's "no frame" is shutdown's doing (the frame gate and
 * the render-thread join together), not a continuation that never runs or a demo that
 * never echoes.
 *
 * DOES NOT COVER:
 *  - Either mechanism ALONE. The gate and the join each stop this late frame on their
 *    own, so losing only the gate close (mutation N2) or only the join (N3) stays GREEN
 *    here: both are equivalent mutants for this pin, measured. Losing both, or the whole
 *    pre-16.1 shutdown, reds it. Each mechanism is pinned separately in
 *    tests/BlazorNative.Runtime.Tests/ShutdownQuiescenceTests.cs: the gate by
 *    Shutdown_ReturnsWithinBudget_WhenAHandlerNeverYields (a handler that never yields
 *    defeats the join, so only the gate can stop its frames) and
 *    Shutdown_WaitsForACallbackAlreadyInFlight (the gate's drain); the join by
 *    NoFrameReachesTheCallback_AfterShutdownReturns (the render thread must be gone
 *    after shutdown) and ResetForTests_JoinsTheRenderThread_AndLeaksNone.
 *  - A handler that never yields (the join times out and only the gate stands between its
 *    frames and the host); the sample components cannot reach it, the .NET test above does.
 *  - [BlazorNativeRuntime.retire], which is NOT quiescent (RetireLateContinuationTest).
 *
 * Shares the process-global session with every other JVM test class. `shutdown` detaches
 * it, so the next mount, the replacement's included, builds a fresh one; that is the
 * documented production behaviour after shutdown (TryMount_AfterShutdown_BuildsAFreshSession).
 */
class ShutdownQuiescenceTest {

    @Test
    fun shutdown_runtime_receives_no_frame_from_a_late_continuation() {
        val host = HeldCameraHost()
        val shutdownReturned = AtomicBoolean(false)
        val framesAfterShutdown = Collections.synchronizedList(mutableListOf<RenderFrame>())
        val framesA = Collections.synchronizedList(mutableListOf<RenderFrame>())
        val a = BlazorNativeRuntime(onFrame = { f ->
            framesA.add(f)
            if (shutdownReturned.get()) framesAfterShutdown.add(f)
        })
        a.start(componentName = "BnCameraDemo", platformOs = "test-host", bridge = host)
        val takePhoto = takePhotoHandler(framesA.first())

        val framesB = Collections.synchronizedList(mutableListOf<RenderFrame>())
        var b: BlazorNativeRuntime? = null
        var released = false
        try {
            // The export returns at the handler's first await (16.1); the call stays held.
            assertEquals(0, a.dispatchEventBlocking(takePhoto, "click"), "Take Photo must dispatch")
            awaitHeldCall(host)

            a.shutdown()
            shutdownReturned.set(true)

            // The replacement: a fresh callback registration after shutdown.
            b = BlazorNativeRuntime(onFrame = { framesB.add(it) })
            b.start(componentName = "HelloComponent", platformOs = "test-host")
            val replacementMountFrames = framesB.size
            assertTrue(replacementMountFrames > 0, "the replacement must mount (its callback is live)")

            val releaseRc = host.release()
            released = true
            println("[ShutdownQuiescenceTest] released the held call after shutdown → rc $releaseRc")
            Thread.sleep(1_000) // the positive control shows the continuation frames well inside this

            assertEquals(
                emptyList<RenderFrame>(), framesAfterShutdown.toList(),
                "a frame reached the SHUT-DOWN runtime after shutdown() returned: its late " +
                    "continuation got through. blazornative_shutdown must quiesce before it " +
                    "returns: the frame gate and the render-thread join together stop this frame."
            )
            val stray = framesB.drop(replacementMountFrames)
            assertTrue(
                stray.none { it.hasText(CANCELLED_ECHO) },
                "the shut-down session's late continuation framed into the REPLACEMENT's " +
                    "callback ('$CANCELLED_ECHO' arrived): shutdown did not quiesce the old " +
                    "session (neither the frame gate nor the render-thread join stopped it) " +
                    "render thread; stray frames: $stray"
            )
        } finally {
            if (!released) host.release()
            b?.retire()
        }
    }

    @Test
    fun shutdown_runtime_receives_no_frame_from_a_late_continuation_PositiveControl() {
        // The same held call, released WITHOUT shutdown: its continuation must frame, or the
        // pin above proves nothing about the gate.
        val host = HeldCameraHost()
        val echoed = CountDownLatch(1)
        val framesAfterRelease = AtomicInteger(0)
        val releasedFlag = AtomicBoolean(false)
        val frames = Collections.synchronizedList(mutableListOf<RenderFrame>())
        val runtime = BlazorNativeRuntime(onFrame = { f ->
            frames.add(f)
            if (releasedFlag.get()) framesAfterRelease.incrementAndGet()
            if (f.hasText(CANCELLED_ECHO)) echoed.countDown()
        })
        runtime.start(componentName = "BnCameraDemo", platformOs = "test-host", bridge = host)
        val takePhoto = takePhotoHandler(frames.first())

        var released = false
        try {
            assertEquals(0, runtime.dispatchEventBlocking(takePhoto, "click"), "Take Photo must dispatch")
            awaitHeldCall(host)
            releasedFlag.set(true)
            assertEquals(0, host.release(), "the held call must complete to a live .NET request")
            released = true
            assertTrue(
                echoed.await(5, TimeUnit.SECONDS),
                "the late continuation never framed '$CANCELLED_ECHO' without shutdown; the " +
                    "quiescence pin's 'no frame' could then be a continuation that never runs"
            )
            assertTrue(framesAfterRelease.get() > 0)
        } finally {
            if (!released) host.release()
            runtime.retire()
        }
    }
}
