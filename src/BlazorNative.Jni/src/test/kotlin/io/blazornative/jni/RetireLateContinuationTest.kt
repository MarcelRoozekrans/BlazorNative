package io.blazornative.jni

import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.util.Collections
import java.util.concurrent.atomic.AtomicBoolean

/**
 * Phase 16.1, flipped in 17.3 (#425) — a retired runtime drops a frame from a late continuation.
 *
 * Until 17.3 this pinned a KNOWN HAZARD: `retire()` drained only the Kotlin dispatch lane, so a
 * handler that awaited a host call re-rendered from a continuation on .NET's render thread after
 * retire(), and the retired runtime's onFrame received that frame. On Activity recreation it
 * reached a destroyed Activity's views. Since 17.3 `retire()` marks the runtime retired, and its
 * frame callback drops every frame that still reaches it. [BlazorNativeRuntime.shutdown] is
 * quiescent too (ShutdownQuiescenceTest), by closing .NET's frame gate.
 *
 * Anchors: `retire()` returned true, so the lane drained while the call was still held; and the
 * runtime COUNTED a dropped frame after the release, beyond those it counted before it, so the
 * late continuation really rendered and the empty onFrame is the drop, not silence.
 *
 * DOES NOT COVER: the replacement runtime. A replacing mount disposes the old root, so the late
 * continuation renders nothing there; RecreationTests pins that on .NET, and
 * RecreationAndroidTest on a device. MainActivity calls retire() in onDestroy since 17.3.
 */
class RetireLateContinuationTest {

    @Test
    fun a_retired_runtime_drops_a_frame_from_a_late_continuation() {
        val host = HeldCameraHost()
        val retired = AtomicBoolean(false)
        val framesAfterRetire = Collections.synchronizedList(mutableListOf<RenderFrame>())
        val frames = Collections.synchronizedList(mutableListOf<RenderFrame>())
        val runtime = BlazorNativeRuntime(onFrame = { f ->
            frames.add(f)
            if (retired.get()) framesAfterRetire.add(f)
        })
        runtime.start(componentName = "BnCameraDemo", platformOs = "test-host", bridge = host)
        val takePhoto = takePhotoHandler(frames.first { it.patches.any { p -> p is RenderPatch.CreateNode } })

        var released = false
        try {
            // The production path: fire-and-forget on the lane. The export returns at the
            // handler's first await, so the lane drains while the call is still held.
            runtime.dispatchEvent(takePhoto, "click")
            awaitHeldCall(host)
            assertTrue(runtime.retire(), "the lane must drain while the handler is suspended on the held call")
            retired.set(true)
            // retire() marks the runtime retired BEFORE it drains the lane, so a frame that reaches
            // the callback during the drain is dropped and counted too. Counting from zero let the
            // anchor pass with no late continuation at all: mutation C1 in the 17.7 record loses
            // every host call completion and passed it. Premise: the dispatch export delivers the
            // synchronous part's frames before it returns, per the rc contract in Exports.cs. So
            // once retire() has returned, the lane is drained and no callback is in flight, and
            // only a frame after the release, which only the late continuation can render, counts.
            val droppedBeforeRelease = runtime.framesDroppedAfterRetire.get()

            host.release()
            released = true

            // Anchor: the late continuation really rendered, and the runtime dropped that frame.
            val deadline = System.currentTimeMillis() + 5_000
            while (runtime.framesDroppedAfterRetire.get() == droppedBeforeRelease && System.currentTimeMillis() < deadline) Thread.sleep(10)
            assertTrue(runtime.framesDroppedAfterRetire.get() > droppedBeforeRelease,
                "anchor: no frame reached the retired runtime within 5 s of the release, so its drop was never " +
                    "exercised; $droppedBeforeRelease were dropped before the release")
            // The callback counts a frame BEFORE it would deliver it, so a broken drop delivers it
            // just after the anchor passes. Asserting at once raced that delivery and passed with
            // the drop removed, mutation J1 in the 17.3 record; give the callback time to finish.
            Thread.sleep(500)
            assertTrue(framesAfterRetire.isEmpty(),
                "a retired runtime's onFrame received a frame from a late continuation, which on Activity " +
                    "recreation renders into a destroyed Activity's views (#425): $framesAfterRetire")
        } finally {
            if (!released) host.release()
        }
    }

    /**
     * 17.3 review fix: a frame callback can read `retired` just BEFORE retire() sets it, and then
     * deliver to onFrame after retire() returned. So retire() waits out a callback in flight.
     * Built with no dispatch on the lane, which retire() would wait for anyway and so mask the
     * fix: the runtime's own mount frame is held inside onFrame on the starting thread, and
     * retire() runs on another. Mutation J2's first version of this pin used a late
     * continuation, whose synchronous part could still hold the lane, and passed with the wait
     * removed. Anchor: the frame really is inside onFrame when retire() is called. DOES NOT
     * COVER: a callback held past retire()'s 5 s budget, after which it returns anyway.
     */
    @Test
    fun retire_waits_out_a_frame_callback_already_in_flight() {
        val inOnFrame = java.util.concurrent.CountDownLatch(1)
        val releaseOnFrame = java.util.concurrent.CountDownLatch(1)
        val runtime = BlazorNativeRuntime(onFrame = {
            inOnFrame.countDown()
            releaseOnFrame.await(10, java.util.concurrent.TimeUnit.SECONDS)
        })
        val starter = Thread({
            runtime.start(componentName = "BnDemo", platformOs = "test-host", bridge = HeldCameraHost())
        }, "starter").apply { isDaemon = true; start() }
        var retirer: Thread? = null
        try {
            assertTrue(inOnFrame.await(10, java.util.concurrent.TimeUnit.SECONDS),
                "anchor: the mount frame never reached onFrame, so nothing was in flight")

            val returned = java.util.concurrent.CountDownLatch(1)
            retirer = Thread({ runtime.retire(); returned.countDown() }, "retirer").apply { isDaemon = true; start() }
            assertTrue(!returned.await(300, java.util.concurrent.TimeUnit.MILLISECONDS),
                "retire() returned while a frame callback was still inside onFrame, so that frame is " +
                    "delivered to a retired runtime's shell after retire() (#425)")
            releaseOnFrame.countDown()
            assertTrue(returned.await(5, java.util.concurrent.TimeUnit.SECONDS),
                "retire() never returned after the callback left onFrame")
        } finally {
            releaseOnFrame.countDown()
            starter.join(10_000)
            retirer?.join(10_000)
        }
    }
}
