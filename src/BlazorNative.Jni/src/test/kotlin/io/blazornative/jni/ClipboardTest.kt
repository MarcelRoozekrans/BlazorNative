package io.blazornative.jni

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/**
 * Phase 5.4 Gate 1 (JVM) — the clipboard write→read round-trip driven through
 * the published NativeAOT dll: the Kotlin twin of tests/BlazorNative.Runtime.
 * Tests/ClipboardProbeTests.cs (M5 DoD #6). Mounts ClipboardProbe with an
 * in-memory ShellBridgeHandlers, clicks "Copy" (writes the fixed literal to the
 * host clipboard through the size-negotiated ClipboardWrite slot), then "Paste"
 * (reads it back through ClipboardRead) — the echo BnText re-renders the value
 * on its mount-pinned text node inside the one dispatch window.
 *
 * The Android/iOS instrumented twins (Gates 2/3) drive the same probe against
 * the real ClipboardManager / UIPasteboard.
 *
 * Node identification is ALWAYS structural / by text (ids are process-global
 * monotonic counters — every id is harvested from this test's own mount frame),
 * the NavigationTest/HostEventTest convention. Safe alongside the other native
 * tests in one JVM process (idempotent init, last-wins registration, fresh
 * mounts).
 */
class ClipboardTest {

    private companion object {
        /** Mirror of ClipboardProbe.CopyPayload — the literal the Copy button
         * writes; a Copy→Paste round-trip must echo exactly this. */
        const val COPY_PAYLOAD = "clip!"
    }

    /** In-memory clipboard host: Copy/Paste round-trip through the map, share
     * recorded. Storage/fetch/navigation are inert — the probe never touches
     * them. */
    private class ClipboardHost : ShellBridgeHandlers {
        @Volatile var clipboard: String = ""
        @Volatile var lastShared: String? = null
        override fun navigate(route: String) {}
        override fun currentRoute(): String = "/"
        override fun storageRead(key: String): String? = null
        override fun storageWrite(key: String, value: String) {}
        override fun storageDelete(key: String) {}
        override fun fetchBegin(requestId: Long, request: BridgeFetchRequest) {
            BridgeFetchCompleter.completeFailure(requestId, "ClipboardTest performs no fetch")
        }
        override fun clipboardRead(): String = clipboard
        override fun clipboardWrite(text: String) { clipboard = text }
        override fun share(text: String) { lastShared = text }
    }

    private class Session(
        val runtime: BlazorNativeRuntime,
        val frames: MutableList<RenderFrame>,
        val host: ClipboardHost,
    )

    private fun boot(): Session {
        val frames = mutableListOf<RenderFrame>()
        val host = ClipboardHost()
        val runtime = BlazorNativeRuntime(onFrame = { frames.add(it) })
        runtime.start(componentName = "ClipboardProbe", platformOs = "test-host", bridge = host)
        assertTrue(frames.isNotEmpty(), "mount must deliver the first frame synchronously")
        return Session(runtime, frames, host)
    }

    // ── Structural pin helpers (HostEventTest conventions) ───────────────────

    private fun root(mount: RenderFrame): RenderPatch.CreateNode =
        checkNotNull(
            mount.patches.filterIsInstance<RenderPatch.CreateNode>().singleOrNull { it.parentId == null }
        ) { "expected exactly one parentless create (the root); got ${mount.patches}" }

    private fun createOf(frame: RenderFrame, nodeId: Int): RenderPatch.CreateNode =
        checkNotNull(
            frame.patches.filterIsInstance<RenderPatch.CreateNode>().singleOrNull { it.nodeId == nodeId }
        ) { "expected exactly one CreateNode for node $nodeId; got ${frame.patches}" }

    /** NodeId of the button element whose label text is [label]: the
     * ReplaceText's node → its button parent. */
    private fun buttonOfLabel(mount: RenderFrame, label: String): Int {
        val t = checkNotNull(
            mount.patches.filterIsInstance<RenderPatch.ReplaceText>().singleOrNull { it.text == label }
        ) { "expected exactly one ReplaceText '$label'; got ${mount.patches}" }
        return checkNotNull(createOf(mount, t.nodeId).parentId) { "label '$label' node must have a parent" }
    }

    private fun clickHandlerOn(frame: RenderFrame, nodeId: Int): Int =
        checkNotNull(
            frame.patches.filterIsInstance<RenderPatch.AttachEvent>()
                .singleOrNull { it.nodeId == nodeId && it.eventName == "click" }
        ) { "expected exactly one click AttachEvent on node $nodeId; got ${frame.patches}" }.handlerId

    /** The echo BnText's TEXT node, pinned at mount: root div → the single
     * text-type child (the BnText span; buttons are "button" nodes) → its child. */
    private fun echoTextNode(mount: RenderFrame): Int {
        val r = root(mount)
        val span = checkNotNull(
            mount.patches.filterIsInstance<RenderPatch.CreateNode>()
                .singleOrNull { it.parentId == r.nodeId && it.nodeType == "text" }
        ) { "expected exactly one text-type child of the root; got ${mount.patches}" }
        return checkNotNull(
            mount.patches.filterIsInstance<RenderPatch.CreateNode>().singleOrNull { it.parentId == span.nodeId }
        ) { "expected exactly one child of the echo span; got ${mount.patches}" }.nodeId
    }

    private fun replaceTextOn(frames: List<RenderFrame>, nodeId: Int, text: String) =
        checkNotNull(
            frames.flatMap { it.patches.filterIsInstance<RenderPatch.ReplaceText>() }
                .singleOrNull { it.nodeId == nodeId && it.text == text }
        ) { "expected a ReplaceText '$text' on node $nodeId; got ${frames.map { it.patches }}" }

    // ── Copy → Paste → echo shows the copied value (through the dll) ─────────

    @Test
    fun copy_then_paste_echoes_clipboard_through_the_dll() {
        val s = boot()
        val mount = s.frames.first()
        val echo = echoTextNode(mount)
        assertEquals("", replaceTextOn(listOf(mount), echo, "").text) // empty at mount

        val copyHandler = clickHandlerOn(mount, buttonOfLabel(mount, "Copy"))
        val pasteHandler = clickHandlerOn(mount, buttonOfLabel(mount, "Paste"))

        // Copy writes the fixed literal to the host clipboard (no echo change).
        assertEquals(0, s.runtime.dispatchEventBlocking(copyHandler, "click"))
        assertEquals(COPY_PAYLOAD, s.host.clipboard, "Copy did not reach the host clipboard through the dll")

        // Paste reads it back → the echo BnText re-renders the value on ITS
        // mount-pinned text node inside the dispatch window.
        val before = s.frames.size
        assertEquals(0, s.runtime.dispatchEventBlocking(pasteHandler, "click"))
        val window = s.frames.subList(before, s.frames.size).toList()
        replaceTextOn(window, echo, COPY_PAYLOAD)

        s.runtime.retire()
    }
}
