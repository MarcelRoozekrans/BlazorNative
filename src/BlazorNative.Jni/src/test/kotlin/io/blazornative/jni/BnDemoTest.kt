package io.blazornative.jni

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/**
 * Phase 3.4 Gate 3 — BnDemo driven through the published NativeAOT dll: the
 * Kotlin twin of tests/BlazorNative.Runtime.Tests/BnDemoTests.cs, proving the
 * bind loop (DoD #5) and the cascading theme toggle (DoD #6) at the patch
 * level every host decodes — through the C ABI instead of in-process.
 *
 * Shape: see src/BlazorNative.Components/BnDemo.razor's file header — this
 * header restates only the pins this file asserts; the full tree lives
 * there. Final child order under the form div: title span, input, echo
 * panel div, Clear button, Theme button, "Settings →" button (Phase 3.5 —
 * the navigation entry, DoD #7); the echo panel's create carries the
 * MID-LIST InsertIndex 2 (Blazor's FIFO render queue creates it AFTER the
 * buttons), everything else appends (-1).
 * Theme toggle: #FFEEAA ⇄ #334455 on BOTH themed divs.
 *
 * Node identification is ALWAYS structural / by text (the .NET twin's pins,
 * transliterated): node ids and handler ids are process-global monotonic
 * counters, so absolute values depend on which tests ran earlier in this JVM
 * — every id is harvested from this runtime's own mount frame.
 *
 * Safe alongside the other native tests in one JVM process: init is
 * idempotent, frame-callback registration is last-wins, and each start()
 * mounts a FRESH BnDemo instance (text = "", theme = default).
 */
class BnDemoTest {

    private companion object {
        const val DEFAULT_BACKGROUND = "#FFEEAA"
        const val ALT_BACKGROUND = "#334455"
    }

    /** Boots a fresh runtime + mounts BnDemo, capturing every frame
     * (the mount frame is frames[0] by the sync-mount contract). */
    private fun bootDemo(): Pair<BlazorNativeRuntime, MutableList<RenderFrame>> {
        val frames = mutableListOf<RenderFrame>()
        val runtime = BlazorNativeRuntime(onFrame = { frames.add(it) })
        runtime.start(componentName = "BnDemo", platformOs = "test-host")
        assertTrue(frames.isNotEmpty(), "mount must deliver the first frame synchronously")
        return runtime to frames
    }

    // ── The .NET twin's pin helpers, transliterated ──────────────────────────

    private fun createOf(frame: RenderFrame, nodeId: Int): RenderPatch.CreateNode =
        checkNotNull(
            frame.patches.filterIsInstance<RenderPatch.CreateNode>()
                .singleOrNull { it.nodeId == nodeId }
        ) { "expected exactly one CreateNode for node $nodeId; got ${frame.patches}" }

    /** NodeId of the element CONTAINING the given text (the text node's
     * create parent). Requires the text to be unique in the frame. */
    private fun containerOfText(frame: RenderFrame, text: String): Int {
        val t = checkNotNull(
            frame.patches.filterIsInstance<RenderPatch.ReplaceText>()
                .singleOrNull { it.text == text }
        ) { "expected exactly one ReplaceText '$text' in the frame; got ${frame.patches}" }
        return checkNotNull(createOf(frame, t.nodeId).parentId) { "text '$text' node must have a parent" }
    }

    private fun styleOn(frame: RenderFrame, nodeId: Int, property: String): RenderPatch.SetStyle =
        checkNotNull(
            frame.patches.filterIsInstance<RenderPatch.SetStyle>()
                .singleOrNull { it.nodeId == nodeId && it.property == property }
        ) { "expected exactly one SetStyle '$property' on node $nodeId; got ${frame.patches}" }

    private fun propOn(frame: RenderFrame, nodeId: Int, name: String): RenderPatch.UpdateProp =
        checkNotNull(
            frame.patches.filterIsInstance<RenderPatch.UpdateProp>()
                .singleOrNull { it.nodeId == nodeId && it.name == name }
        ) { "expected exactly one UpdateProp '$name' on node $nodeId; got ${frame.patches}" }

    /** HandlerId of the single click AttachEvent on [nodeId] in [frame]. */
    private fun clickHandlerOn(frame: RenderFrame, nodeId: Int): Int {
        val attach = checkNotNull(
            frame.patches.filterIsInstance<RenderPatch.AttachEvent>()
                .singleOrNull { it.nodeId == nodeId && it.eventName == "click" }
        ) { "expected exactly one click AttachEvent on node $nodeId; got ${frame.patches}" }
        assertTrue(attach.handlerId > 0, "runtime-assigned handlerId must be positive; got ${attach.handlerId}")
        return attach.handlerId
    }

    // Structural pins (stable across re-renders — the .NET twin's walk):
    // root = the single parentless create; form = the root's only child; echo
    // panel = the only "view" child of the FORM; echo TEXT NODE = grandchild via
    // the echo span.
    //
    // #204 put a BnScroll around the page, so the parentless create is now the
    // SCROLL VIEWPORT and the themed form div hangs beneath it. Kept as two
    // accessors rather than collapsing root() onto the form: the distinction is
    // what the wrapper introduced, and a helper that quietly skipped the viewport
    // would let the scroll node vanish from these pins unnoticed.

    private fun root(mount: RenderFrame): RenderPatch.CreateNode =
        checkNotNull(
            mount.patches.filterIsInstance<RenderPatch.CreateNode>()
                .singleOrNull { it.parentId == null }
        ) { "expected exactly one parentless create (the scroll viewport); got ${mount.patches}" }

    /** The themed form div — the scroll viewport's single child. */
    private fun form(mount: RenderFrame): RenderPatch.CreateNode {
        val viewportId = root(mount).nodeId
        return checkNotNull(
            mount.patches.filterIsInstance<RenderPatch.CreateNode>()
                .singleOrNull { it.parentId == viewportId }
        ) { "expected exactly one child of the scroll viewport (the form div); got ${mount.patches}" }
    }

    private fun echoPanel(mount: RenderFrame, formId: Int): RenderPatch.CreateNode =
        checkNotNull(
            mount.patches.filterIsInstance<RenderPatch.CreateNode>()
                .singleOrNull { it.parentId == formId && it.nodeType == "view" }
        ) { "expected exactly one 'view' child of the form (the echo panel); got ${mount.patches}" }

    private fun inputNode(mount: RenderFrame): RenderPatch.CreateNode =
        checkNotNull(
            mount.patches.filterIsInstance<RenderPatch.CreateNode>()
                .singleOrNull { it.nodeType == "input" }
        ) { "expected exactly one input create; got ${mount.patches}" }

    private fun echoTextNode(mount: RenderFrame): Int {
        val panel = echoPanel(mount, form(mount).nodeId)
        val span = checkNotNull(
            mount.patches.filterIsInstance<RenderPatch.CreateNode>()
                .singleOrNull { it.parentId == panel.nodeId }
        ) { "expected exactly one child of the echo panel (the echo span)" }
        return checkNotNull(
            mount.patches.filterIsInstance<RenderPatch.CreateNode>()
                .singleOrNull { it.parentId == span.nodeId }
        ) { "expected exactly one child of the echo span (the echo text node)" }.nodeId
    }

    private fun changeHandler(mount: RenderFrame): Int =
        checkNotNull(
            mount.patches.filterIsInstance<RenderPatch.AttachEvent>()
                .singleOrNull { it.eventName == "change" }
        ) { "expected exactly one change AttachEvent; got ${mount.patches}" }.handlerId

    // ── Mount shape (the canonical tree through the ABI) ─────────────────────

    @Test
    fun mount_shape_matches_canonical_tree() {
        val (_, frames) = bootDemo()
        val mount = frames.first()

        // Root: the SCROLL VIEWPORT (#204). A flex ITEM, not a container — Grow
        // and Basis live on it, while padding/background belong to the form div
        // inside. Pinning that split here is what catches someone moving the
        // container styles onto the scroll node, where both shells ignore them
        // silently.
        val root = root(mount)
        assertEquals("scroll", root.nodeType, "the page root must be the scroll viewport")
        assertEquals("1", styleOn(mount, root.nodeId, "flexGrow").value)
        assertEquals("0", styleOn(mount, root.nodeId, "flexBasis").value)

        // The themed form div: the viewport's single child, carrying the
        // container styling — default background + padding 16.
        val form = form(mount)
        assertEquals("view", form.nodeType, "the form must be a view/div")
        assertEquals(DEFAULT_BACKGROUND, styleOn(mount, form.nodeId, "backgroundColor").value)
        assertEquals("16", styleOn(mount, form.nodeId, "padding").value)

        // Title span with fontSize 24, under the form.
        val title = containerOfText(mount, "BnDemo")
        assertEquals("text", createOf(mount, title).nodeType, "title must be a text/span")
        assertEquals(form.nodeId, createOf(mount, title).parentId)
        assertEquals("24", styleOn(mount, title, "fontSize").value)

        // The bound input: value + placeholder props, change attach.
        val input = inputNode(mount)
        assertEquals(form.nodeId, input.parentId)
        assertEquals("", propOn(mount, input.nodeId, "value").value)
        assertEquals("Type here...", propOn(mount, input.nodeId, "placeholder").value)
        assertEquals(
            1,
            mount.patches.filterIsInstance<RenderPatch.AttachEvent>()
                .count { it.nodeId == input.nodeId && it.eventName == "change" },
            "the input must carry exactly one change attach"
        )

        // Echo panel: the second themed view — its BnView is a CHAINED child
        // component queued behind the buttons in Blazor's FIFO render queue,
        // so its create must carry the MID-LIST InsertIndex 2 across the ABI
        // (after title + input, before the buttons; the chain-fix pin) while
        // everything else appends with the explicit -1.
        val panel = echoPanel(mount, form.nodeId)
        assertEquals(2, panel.insertIndex, "echo panel create must carry the mid-list InsertIndex 2")
        assertEquals(DEFAULT_BACKGROUND, styleOn(mount, panel.nodeId, "backgroundColor").value)
        assertEquals("8", styleOn(mount, panel.nodeId, "padding").value)
        val echoText = checkNotNull(
            mount.patches.filterIsInstance<RenderPatch.ReplaceText>().singleOrNull { it.text == "" }
        ) { "expected exactly one empty ReplaceText (the echo); got ${mount.patches}" }
        assertEquals(echoTextNode(mount), echoText.nodeId, "the empty echo must sit on the pinned echo text node")
        mount.patches.filterIsInstance<RenderPatch.CreateNode>()
            .filter { it.parentId == form.nodeId && it.nodeId != panel.nodeId }
            .forEach { assertEquals(-1, it.insertIndex, "form child ${it.nodeId} must be an explicit -1 append") }

        // Buttons under the form, each with a click attach (Phase 3.5:
        // + "Settings →", the navigation entry — DoD #7).
        for (label in listOf("Clear", "Theme", "Settings →")) {
            val btn = containerOfText(mount, label)
            assertEquals("button", createOf(mount, btn).nodeType)
            assertEquals(form.nodeId, createOf(mount, btn).parentId)
            clickHandlerOn(mount, btn)
        }

        // Exactly 4 event attaches: change + Clear + Theme + Settings →.
        assertEquals(
            4,
            mount.patches.filterIsInstance<RenderPatch.AttachEvent>().size,
            "exactly change + 3 clicks; got ${mount.patches.filterIsInstance<RenderPatch.AttachEvent>()}"
        )
    }

    // ── The bind loop through the dll (DoD #5) ───────────────────────────────

    @Test
    fun change_dispatch_echoes_to_pinned_node() {
        val (runtime, frames) = bootDemo()
        val mount = frames.first()
        val input = inputNode(mount)
        val echoNode = echoTextNode(mount)
        val handler = changeHandler(mount)
        val framesBefore = frames.size

        // Non-ASCII payload crossing the ABI into the bind loop.
        val typed = "héllo→世界"
        assertEquals(0, runtime.dispatchEventBlocking(handler, "change", payload = typed))
        assertTrue(frames.size > framesBefore, "re-render frame must arrive synchronously inside dispatch")
        val frame = frames.last()

        // The echo BnText re-rendered the typed text on ITS mount-pinned text
        // node (the 3.2 Gate 3 lesson: text-only assertions can stay green
        // while Android silently drops a mistargeted patch)…
        val echoed = checkNotNull(
            frame.patches.filterIsInstance<RenderPatch.ReplaceText>().singleOrNull { it.text == typed }
        ) { "expected exactly one ReplaceText '$typed'; got ${frame.patches}" }
        assertEquals(echoNode, echoed.nodeId, "the echo must target the mount frame's echo text node")

        // …and the bound Value wrote back to the input's host prop.
        assertEquals(typed, propOn(frame, input.nodeId, "value").value)
    }

    @Test
    fun clear_click_resets_input_and_echo() {
        val (runtime, frames) = bootDemo()
        val mount = frames.first()
        val input = inputNode(mount)
        val echoNode = echoTextNode(mount)
        val clearHandler = clickHandlerOn(mount, containerOfText(mount, "Clear"))

        var framesBefore = frames.size
        assertEquals(0, runtime.dispatchEventBlocking(changeHandler(mount), "change", payload = "hello"))
        assertTrue(frames.size > framesBefore, "re-render frame must arrive synchronously inside the change dispatch")
        framesBefore = frames.size
        assertEquals(0, runtime.dispatchEventBlocking(clearHandler, "click"))
        assertTrue(frames.size > framesBefore, "re-render frame must arrive synchronously inside the Clear dispatch")
        val frame = frames.last()

        // Both halves reset: the input's value prop AND the echo text.
        assertEquals("", propOn(frame, input.nodeId, "value").value)
        val echoed = checkNotNull(
            frame.patches.filterIsInstance<RenderPatch.ReplaceText>().singleOrNull { it.text == "" }
        ) { "expected exactly one empty ReplaceText after Clear; got ${frame.patches}" }
        assertEquals(echoNode, echoed.nodeId, "the reset echo must target the mount frame's echo text node")
    }

    // ── The cascading theme toggle through the dll (DoD #6) ──────────────────

    @Test
    fun theme_click_flips_both_themed_backgrounds() {
        val (runtime, frames) = bootDemo()
        val mount = frames.first()
        val formId = form(mount).nodeId
        val panelId = echoPanel(mount, formId).nodeId
        val themeHandler = clickHandlerOn(mount, containerOfText(mount, "Theme"))
        val framesBefore = frames.size

        assertEquals(0, runtime.dispatchEventBlocking(themeHandler, "click"))
        assertTrue(frames.size > framesBefore, "re-render frame must arrive synchronously inside dispatch")

        // The cascaded BnTheme changed → BOTH consumers re-rendered with the
        // alt background, nodeId-pinned to the mount frame's themed divs
        // (DoD #6: parent change → children re-render).
        val flipped = frames.last().patches.filterIsInstance<RenderPatch.SetStyle>()
            .filter { it.property == "backgroundColor" && it.value == ALT_BACKGROUND }
            .map { it.nodeId }
            .toSet()
        assertTrue(formId in flipped, "the form div must flip to $ALT_BACKGROUND; got ${frames.last().patches}")
        assertTrue(panelId in flipped, "the echo panel must flip to $ALT_BACKGROUND; got ${frames.last().patches}")
        assertTrue(flipped.size >= 2, "expected >=2 themed children to flip; got $flipped")

        // Toggling again restores the default on both.
        val framesBeforeSecond = frames.size
        assertEquals(0, runtime.dispatchEventBlocking(themeHandler, "click"))
        assertTrue(frames.size > framesBeforeSecond, "re-render frame must arrive synchronously inside the second toggle")
        val back = frames.last().patches.filterIsInstance<RenderPatch.SetStyle>()
            .filter { it.property == "backgroundColor" && it.value == DEFAULT_BACKGROUND }
            .map { it.nodeId }
            .toSet()
        assertTrue(formId in back, "the form div must flip back to $DEFAULT_BACKGROUND")
        assertTrue(panelId in back, "the echo panel must flip back to $DEFAULT_BACKGROUND")
    }
}
