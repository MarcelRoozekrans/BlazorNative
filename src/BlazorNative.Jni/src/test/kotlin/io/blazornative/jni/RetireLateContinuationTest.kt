package io.blazornative.jni

import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.util.Collections
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean

/**
 * Phase 16.1 — what [BlazorNativeRuntime.retire] actually does, pinned as it stands.
 *
 * A KNOWN HAZARD, PINNED ON PURPOSE. `retire()` drains only the Kotlin dispatch lane: it
 * never calls into .NET. Since 16.1 a handler that awaited a host call re-renders from a
 * continuation on .NET's render thread when that call completes, with no export in
 * progress, so a retired runtime still receives that frame. On Activity recreation
 * (retire the old runtime, start a replacement) the frame reaches the retired runtime's
 * onFrame until the replacement re-registers the callback, and the replacement's
 * afterwards. Before 16.1 the export held the lane until the handler finished, so
 * `retire()` waited for it; the old "a retired runtime is fully quiescent" claim was true
 * then and is not now. [BlazorNativeRuntime.shutdown] IS quiescent (ShutdownQuiescenceTest).
 *
 * This test PASSES on today's behaviour and FAILS the day `retire()` is made to quiesce
 * .NET's frames for its runtime. That is intentional: when that lands, invert the
 * assertion, do not delete the test, and correct the recreation contract on
 * [BlazorNativeRuntime.start].
 *
 * Anchors: `retire()` returned true (the lane drained, so the export returned while the
 * call was still held), and no frame had arrived between retire and release, so the
 * frame this test observes is the continuation's, caused by the release.
 *
 * DOES NOT COVER: the replacement-runtime half of the hazard (the frame reaching a NEW
 * runtime's onFrame after its start() re-registers), or Android's MainActivity, which does
 * not call retire() today.
 */
class RetireLateContinuationTest {

    @Test
    fun retired_runtime_still_receives_a_frame_from_a_late_continuation() {
        val host = HeldCameraHost()
        val retired = AtomicBoolean(false)
        val framesAfterRetire = Collections.synchronizedList(mutableListOf<RenderFrame>())
        val echoedAfterRetire = CountDownLatch(1)
        val frames = Collections.synchronizedList(mutableListOf<RenderFrame>())
        val runtime = BlazorNativeRuntime(onFrame = { f ->
            frames.add(f)
            if (retired.get()) {
                framesAfterRetire.add(f)
                if (f.hasText(CANCELLED_ECHO)) echoedAfterRetire.countDown()
            }
        })
        runtime.start(componentName = "BnCameraDemo", platformOs = "test-host", bridge = host)
        val takePhoto = takePhotoHandler(frames.first())

        var released = false
        try {
            // The production path: fire-and-forget on the lane. The export returns at the
            // handler's first await, so the lane drains while the call is still held.
            runtime.dispatchEvent(takePhoto, "click")
            awaitHeldCall(host)
            assertTrue(runtime.retire(), "the lane must drain while the handler is suspended on the held call")
            retired.set(true)
            assertTrue(
                framesAfterRetire.isEmpty(),
                "a frame arrived between retire() and the release, so the frame below could not be " +
                    "attributed to the late continuation; got $framesAfterRetire"
            )

            host.release()
            released = true

            // KNOWN HAZARD, PINNED ON PURPOSE: a retired runtime still receives the frame.
            assertTrue(
                echoedAfterRetire.await(5, TimeUnit.SECONDS),
                "the retired runtime received NO frame from the late continuation within 5 s. " +
                    "This test pins the known hazard that retire() drains only the Kotlin lane " +
                    "and does not quiesce .NET. If retire() now quiesces, invert this assertion " +
                    "instead of deleting the test, and correct the recreation contract on start(). " +
                    "Frames after retire: $framesAfterRetire"
            )
        } finally {
            if (!released) host.release()
        }
    }
}
