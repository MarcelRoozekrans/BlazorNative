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
 * runtime COUNTED a dropped frame after the release, so the late continuation really rendered
 * and the empty onFrame is the drop, not silence.
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

            host.release()
            released = true

            // Anchor: the late continuation really rendered, and the runtime dropped that frame.
            val deadline = System.currentTimeMillis() + 5_000
            while (runtime.framesDroppedAfterRetire.get() == 0 && System.currentTimeMillis() < deadline) Thread.sleep(10)
            assertTrue(runtime.framesDroppedAfterRetire.get() > 0,
                "anchor: no frame reached the retired runtime within 5 s, so its drop was never exercised")
            assertTrue(framesAfterRetire.isEmpty(),
                "a retired runtime's onFrame received a frame from a late continuation, which on Activity " +
                    "recreation renders into a destroyed Activity's views (#425): $framesAfterRetire")
        } finally {
            if (!released) host.release()
        }
    }
}
