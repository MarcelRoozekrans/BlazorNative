package io.blazornative.jni

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/**
 * Phase 4.2 Gate 2 — focus/blur driven through the published NativeAOT dll:
 * the Kotlin twin of tests/BlazorNative.Runtime.Tests/FocusProbeTests.cs,
 * proving the focus/blur carriers (M4 DoD #4) at the patch level every host
 * decodes — through the C ABI instead of in-process.
 *
 * Shape: see src/BlazorNative.Runtime/FocusProbe.cs's file header — root div
 * containing a BnInput (focus + blur + the always-on change attach) and the
 * echo BnText ("" → "focused" / "blurred" on a mount-pinned text node).
 *
 * Node identification is ALWAYS structural (the .NET twin's pins,
 * transliterated): node ids and handler ids are process-global monotonic
 * counters, so absolute values depend on which tests ran earlier in this JVM
 * — every id is harvested from this runtime's own mount frame.
 *
 * Safe alongside the other native tests in one JVM process: init is
 * idempotent, frame-callback registration is last-wins, and each start()
 * mounts a FRESH FocusProbe instance (state = "").
 */
class FocusBlurTest {

    /** Boots a fresh runtime + mounts FocusProbe, capturing every frame
     * (the mount frame is frames[0] by the sync-mount contract). */
    private fun bootProbe(): Pair<BlazorNativeRuntime, MutableList<RenderFrame>> {
        val frames = mutableListOf<RenderFrame>()
        val runtime = BlazorNativeRuntime(onFrame = { frames.add(it) })
        runtime.start(componentName = "FocusProbe", platformOs = "test-host")
        assertTrue(frames.isNotEmpty(), "mount must deliver the first frame synchronously")
        return runtime to frames
    }

    // ── The .NET twin's pin helpers, transliterated ──────────────────────────

    private fun inputNode(mount: RenderFrame): RenderPatch.CreateNode =
        checkNotNull(
            mount.patches.filterIsInstance<RenderPatch.CreateNode>()
                .singleOrNull { it.nodeType == "input" }
        ) { "expected exactly one input create; got ${mount.patches}" }

    /** HandlerId of the single [eventName] AttachEvent on [nodeId]. */
    private fun handlerOn(frame: RenderFrame, nodeId: Int, eventName: String): Int {
        val attach = checkNotNull(
            frame.patches.filterIsInstance<RenderPatch.AttachEvent>()
                .singleOrNull { it.nodeId == nodeId && it.eventName == eventName }
        ) { "expected exactly one $eventName AttachEvent on node $nodeId; got ${frame.patches}" }
        assertTrue(attach.handlerId > 0, "runtime-assigned handlerId must be positive; got ${attach.handlerId}")
        return attach.handlerId
    }

    /** The echo BnText's TEXT node, pinned at mount: root div → the span
     * (text-type child of the root) → its single child text node (the .NET
     * twin's structural walk). */
    private fun echoTextNode(mount: RenderFrame): Int {
        val root = checkNotNull(
            mount.patches.filterIsInstance<RenderPatch.CreateNode>()
                .singleOrNull { it.parentId == null }
        ) { "expected exactly one parentless create (the probe's root div); got ${mount.patches}" }
        val span = checkNotNull(
            mount.patches.filterIsInstance<RenderPatch.CreateNode>()
                .singleOrNull { it.parentId == root.nodeId && it.nodeType == "text" }
        ) { "expected exactly one text-type child of the root (the echo span); got ${mount.patches}" }
        return checkNotNull(
            mount.patches.filterIsInstance<RenderPatch.CreateNode>()
                .singleOrNull { it.parentId == span.nodeId }
        ) { "expected exactly one child of the echo span (the echo text node)" }.nodeId
    }

    // ── Mount shape (the probe's tree through the ABI) ───────────────────────

    @Test
    fun mount_shape_input_carries_focus_blur_and_change_attaches() {
        val (_, frames) = bootProbe()
        val mount = frames.first()

        // The input carries focus + blur attaches (change too — BnInput
        // always wires its bind half): exactly 3, nothing else.
        val input = inputNode(mount)
        handlerOn(mount, input.nodeId, "focus")
        handlerOn(mount, input.nodeId, "blur")
        handlerOn(mount, input.nodeId, "change")
        assertEquals(
            3,
            mount.patches.filterIsInstance<RenderPatch.AttachEvent>().size,
            "exactly focus + blur + change; got ${mount.patches.filterIsInstance<RenderPatch.AttachEvent>()}"
        )

        // The echo text node exists from mount, empty until an event lands.
        val echoNode = echoTextNode(mount)
        val initial = checkNotNull(
            mount.patches.filterIsInstance<RenderPatch.ReplaceText>().singleOrNull()
        ) { "expected exactly one ReplaceText in the mount frame (the empty echo); got ${mount.patches}" }
        assertEquals(echoNode, initial.nodeId, "the initial echo must sit on the pinned echo text node")
        assertEquals("", initial.text, "the echo must be empty until a focus/blur lands")
    }

    // ── The round trip through the dll: focus → blur, echo pinned ───────────

    @Test
    fun focus_then_blur_dispatch_echo_transitions_on_pinned_node() {
        val (runtime, frames) = bootProbe()
        val mount = frames.first()
        val input = inputNode(mount)
        val echoNode = echoTextNode(mount)
        val focusHandler = handlerOn(mount, input.nodeId, "focus")
        val blurHandler = handlerOn(mount, input.nodeId, "blur")

        // focus → the echo BnText re-renders "focused" on ITS mount-pinned
        // text node (BuildEventArgs maps "focus" → FocusEventArgs).
        var framesBefore = frames.size
        assertEquals(0, runtime.dispatchEventBlocking(focusHandler, "focus"))
        assertTrue(frames.size > framesBefore, "re-render frame must arrive synchronously inside the focus dispatch")
        val focused = checkNotNull(
            frames.last().patches.filterIsInstance<RenderPatch.ReplaceText>()
                .singleOrNull { it.text == "focused" }
        ) { "expected exactly one ReplaceText 'focused'; got ${frames.last().patches}" }
        assertEquals(echoNode, focused.nodeId, "the focus echo must target the mount frame's echo text node")

        // blur → "blurred", same node.
        framesBefore = frames.size
        assertEquals(0, runtime.dispatchEventBlocking(blurHandler, "blur"))
        assertTrue(frames.size > framesBefore, "re-render frame must arrive synchronously inside the blur dispatch")
        val blurred = checkNotNull(
            frames.last().patches.filterIsInstance<RenderPatch.ReplaceText>()
                .singleOrNull { it.text == "blurred" }
        ) { "expected exactly one ReplaceText 'blurred'; got ${frames.last().patches}" }
        assertEquals(echoNode, blurred.nodeId, "the blur echo must target the mount frame's echo text node")
    }
}
