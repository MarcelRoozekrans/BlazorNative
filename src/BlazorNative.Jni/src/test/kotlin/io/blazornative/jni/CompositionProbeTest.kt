package io.blazornative.jni

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/**
 * Phase 3.3 Gate 2 — CompositionProbe driven through the published NativeAOT
 * dll: the Kotlin twin of tests/BlazorNative.Runtime.Tests/CompositionProbeTests.cs
 * (which pins the same shapes on the in-process .NET surface). Boots via
 * [BlazorNativeRuntime] mounting "CompositionProbe" from the mount registry and
 * dispatches through [BlazorNativeRuntime.dispatchEventBlocking] — the same
 * boot + frames-capture + blocking-dispatch pattern as DispatchEventTest.
 *
 * Probe shape (src/BlazorNative.Runtime/CompositionProbe.cs):
 *   root div
 *     ├─ [0] header div ("CompositionProbe")
 *     ├─ [1] badge ItemComponent div ("badge (taps: 0)")  ← INTERLEAVED child
 *     ├─ [2] label div ("list:")
 *     ├─ [3] list div: keyed ItemComponents "item-1 (taps: 0)", "item-2 (taps: 0)"
 *     └─ [4..6] buttons "Add" / "Insert" / "Remove"
 *
 * Node identification is ALWAYS by text (ContainerOfText-style, mirroring the
 * .NET twin): node ids and handler ids are process-global monotonic counters,
 * so absolute values depend on which tests ran earlier in this JVM — every id
 * is harvested from this runtime's own mount frame. Buttons are identified by
 * their text child ("Add"/"Insert"/"Remove"), never by patch order.
 *
 * Safe alongside the other native tests in one JVM process: init is idempotent,
 * frame-callback registration is last-wins, and each start() mounts a FRESH
 * CompositionProbe instance (list = [item-1, item-2], taps 0).
 */
class CompositionProbeTest {

    /** Boots a fresh runtime + mounts CompositionProbe, capturing every frame
     * (the mount frame is frames[0] by the sync-mount contract). */
    private fun bootProbe(): Pair<BlazorNativeRuntime, MutableList<RenderFrame>> {
        val frames = mutableListOf<RenderFrame>()
        val runtime = BlazorNativeRuntime(onFrame = { frames.add(it) })
        runtime.start(componentName = "CompositionProbe", platformOs = "test-host")
        assertTrue(frames.isNotEmpty(), "mount must deliver the first frame synchronously")
        return runtime to frames
    }

    // ── ContainerOfText-style helpers (the .NET twin's, transliterated) ──────

    /** NodeId of the element CONTAINING the given text (the text node's
     * create parent). Requires the text to be unique in the frame. */
    private fun containerOfText(frame: RenderFrame, text: String): Int {
        val t = frame.patches.filterIsInstance<RenderPatch.ReplaceText>()
            .single { it.text == text }
        val c = frame.patches.filterIsInstance<RenderPatch.CreateNode>()
            .single { it.nodeId == t.nodeId }
        return checkNotNull(c.parentId) { "text '$text' node must have a parent" }
    }

    private fun createOf(frame: RenderFrame, nodeId: Int): RenderPatch.CreateNode =
        frame.patches.filterIsInstance<RenderPatch.CreateNode>().single { it.nodeId == nodeId }

    /** HandlerId of the single click AttachEvent on [nodeId] in [frame]. */
    private fun handlerOn(frame: RenderFrame, nodeId: Int): Int {
        val attach = frame.patches.filterIsInstance<RenderPatch.AttachEvent>()
            .single { it.nodeId == nodeId && it.eventName == "click" }
        assertTrue(attach.handlerId > 0, "runtime-assigned handlerId must be positive; got ${attach.handlerId}")
        return attach.handlerId
    }

    // ── Mount shape: the interleaved badge's InsertIndex (DoD #8 at the wire) ─

    @Test
    fun mount_shape_interleaved_badge_has_insert_index_1() {
        val (_, frames) = bootProbe()
        val mount = frames.first()

        // Exactly one parentless create: the root container.
        val root = frames.first().patches.filterIsInstance<RenderPatch.CreateNode>()
            .single { it.parentId == null }

        // The badge ItemComponent is identified by its distinguishing text
        // child "badge (taps: 0)" (ItemComponent renders "<Label> (taps: N)").
        // Its own diff runs AFTER the parent finished walking (header, label,
        // list, buttons already created), so its create must carry the
        // mid-container host index 1 — right after the header, NOT an append.
        val badgeDiv = containerOfText(mount, "badge (taps: 0)")
        val badgeCreate = createOf(mount, badgeDiv)
        assertEquals(root.nodeId, badgeCreate.parentId, "badge must root under the root container")
        assertEquals(1, badgeCreate.insertIndex, "interleaved badge create must carry InsertIndex 1")

        // The keyed list items appended in order (nothing after them existed
        // at their render time) → explicit -1 append.
        val item1Div = containerOfText(mount, "item-1 (taps: 0)")
        val item2Div = containerOfText(mount, "item-2 (taps: 0)")
        assertEquals(-1, createOf(mount, item1Div).insertIndex, "item-1 must be an append")
        assertEquals(-1, createOf(mount, item2Div).insertIndex, "item-2 must be an append")

        // Both items live under the SAME list container, itself under root.
        val listDiv = createOf(mount, item1Div).parentId
        assertEquals(listDiv, createOf(mount, item2Div).parentId)
        assertEquals(root.nodeId, createOf(mount, listDiv!!).parentId)
    }

    // ── Insert-at-front: the DoD #10 payoff through the dll ──────────────────

    @Test
    fun insert_at_front_dispatch_emits_front_insert_index() {
        val (runtime, frames) = bootProbe()
        val mount = frames.first()

        // The Insert button is identified by its text child "Insert" (the
        // container of that text is the button view carrying the handler).
        val insertHandler = handlerOn(mount, containerOfText(mount, "Insert"))
        val listDiv = createOf(mount, containerOfText(mount, "item-1 (taps: 0)")).parentId
        val framesBefore = frames.size

        assertEquals(0, runtime.dispatchEventBlocking(insertHandler, "click"))
        assertTrue(frames.size > framesBefore, "re-render frame must arrive synchronously inside dispatch")
        val frame = frames.last()

        // Expected index arithmetic: the list container's children are
        // EXCLUSIVELY the keyed ItemComponent views — at mount, item-1 sits at
        // host child index 0 and item-2 at index 1. InsertAtFront() does
        // _items.Insert(0, …), so the new item's view must land at the front
        // of the list container: host child index 0 (NOT -1 — 0 is a valid
        // front index, explicitly encoded).
        val created = frame.patches.filterIsInstance<RenderPatch.CreateNode>()
            .single { it.parentId == listDiv }
        assertEquals(0, created.insertIndex, "front insert must carry InsertIndex 0")

        // And it is genuinely the new item: its text child says item-3.
        val newText = frame.patches.filterIsInstance<RenderPatch.ReplaceText>()
            .single { it.text == "item-3 (taps: 0)" }
        assertEquals(created.nodeId, createOf(frame, newText.nodeId).parentId)
    }

    // ── Remove-first: RemoveNode for the FIRST item's view ───────────────────

    @Test
    fun remove_first_dispatch_emits_remove_node() {
        val (runtime, frames) = bootProbe()
        val mount = frames.first()
        val item1Div = containerOfText(mount, "item-1 (taps: 0)")
        val removeHandler = handlerOn(mount, containerOfText(mount, "Remove"))

        assertEquals(0, runtime.dispatchEventBlocking(removeHandler, "click"))
        val frame = frames.last()

        // A pure removal: exactly one RemoveNode — the first item's root view
        // (nodeId pinned to the mount frame's create) — and NO creates.
        val removed = frame.patches.filterIsInstance<RenderPatch.RemoveNode>().single()
        assertEquals(item1Div, removed.nodeId, "remove-first must remove item-1's own view")
        assertTrue(
            frame.patches.filterIsInstance<RenderPatch.CreateNode>().isEmpty(),
            "remove-first must not create nodes; got ${frame.patches}"
        )
    }

    // ── Child ItemComponent click: its OWN re-render, its OWN text node ──────

    @Test
    fun child_item_click_rerenders_item_only() {
        val (runtime, frames) = bootProbe()
        val mount = frames.first()
        val badgeDiv = containerOfText(mount, "badge (taps: 0)")
        val badgeTextNodeId = mount.patches.filterIsInstance<RenderPatch.ReplaceText>()
            .single { it.text == "badge (taps: 0)" }.nodeId
        val badgeHandler = handlerOn(mount, badgeDiv)
        val framesBefore = frames.size

        assertEquals(0, runtime.dispatchEventBlocking(badgeHandler, "click"))
        assertTrue(frames.size > framesBefore, "re-render frame must arrive synchronously inside dispatch")

        // The child's OWN state mutated and its OWN text node updated — and
        // (the 3.2 Gate 3 lesson) the ReplaceText must target the SAME nodeId
        // the mount frame used, or Android would silently drop it.
        val updated = frames.last().patches.filterIsInstance<RenderPatch.ReplaceText>()
            .single { it.text == "badge (taps: 1)" }
        assertEquals(badgeTextNodeId, updated.nodeId, "re-render must target the badge's own mount text node")
    }
}
