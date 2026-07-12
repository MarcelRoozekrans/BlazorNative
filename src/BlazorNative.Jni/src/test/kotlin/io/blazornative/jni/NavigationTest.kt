package io.blazornative.jni

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/**
 * Phase 3.5 Gate 2 — two-page navigation driven through the published
 * NativeAOT dll: the Kotlin twin of tests/BlazorNative.Runtime.Tests/
 * NavigationTests.cs (DoD #7). The Settings button's own click dispatch
 * performs the root swap SYNCHRONOUSLY with respect to the export — when
 * blazornative_dispatch_event returns, the host has heard Navigate
 * ("/settings"), the old root's RemoveNodes have been delivered, and
 * BnSettingsPage's creates have arrived (removes strictly BEFORE creates;
 * the pin is ORDER, not frame count — Blazor completes the event's own
 * batch first, so the handler's no-op re-render may precede the swap
 * frames). Dispatching "← Back" remounts BnDemo FRESH (empty input value +
 * empty echo — state does not survive the swap), mirroring the .NET twin's
 * NavigateBack_RemountsFresh pins.
 *
 * Shapes: BnDemo per src/BlazorNative.Components/BnDemo.cs's canonical
 * header; BnSettingsPage per BnSettingsPage.cs's (themed view root, title
 * span "Settings", "← Back" button + click — and NO input, the shape pin
 * BnSettingsPage lacks by design).
 *
 * Node identification is ALWAYS structural / by text (ids are process-global
 * monotonic counters — every id is harvested from this test's own mount
 * frame). Safe alongside the other native tests in one JVM process: init is
 * idempotent, frame-callback + bridge registration are last-wins (superseded
 * bridge registrations are parked forever by BridgeRegistrar), each start()
 * mounts a FRESH BnDemo that becomes the session's tracked root, and a
 * direct named mount re-syncs the session's route state to "/" — so no
 * navigation this class performs can leak route state into another class's
 * mount.
 */
class NavigationTest {

    /** In-memory host: records every Navigate notification the runtime
     * sends (the observation surface for pin (a)) and answers CurrentRoute
     * with the last navigated route (initially "/"). Storage/fetch are
     * inert — navigation never touches them. */
    private class RecordingHandlers : ShellBridgeHandlers {
        val navigations = mutableListOf<String>()
        @Volatile private var route: String = "/"

        override fun navigate(route: String) {
            navigations.add(route)
            this.route = route
        }
        override fun currentRoute(): String = route
        override fun storageRead(key: String): String? = null
        override fun storageWrite(key: String, value: String) {}
        override fun storageDelete(key: String) {}
        override fun fetchBegin(requestId: Long, request: BridgeFetchRequest) {
            BridgeFetchCompleter.completeFailure(requestId, "NavigationTest performs no fetch")
        }
        override fun clipboardRead(): String = ""
        override fun clipboardWrite(text: String) {}
        override fun share(text: String) {}
    }

    private class Session(
        val runtime: BlazorNativeRuntime,
        val frames: MutableList<RenderFrame>,
        val host: RecordingHandlers,
    )

    /** Boots a fresh runtime with the recording host bridge and mounts the
     * routed app's default entry ("/" → BnDemo); the mount frame is
     * frames[0] by the sync-mount contract. */
    private fun bootDemo(): Session {
        val frames = mutableListOf<RenderFrame>()
        val host = RecordingHandlers()
        val runtime = BlazorNativeRuntime(onFrame = { frames.add(it) })
        runtime.start(componentName = "BnDemo", platformOs = "test-host", bridge = host)
        assertTrue(frames.isNotEmpty(), "mount must deliver the first frame synchronously")
        return Session(runtime, frames, host)
    }

    // ── Structural pin helpers (BnDemoTest conventions) ──────────────────────

    private fun createOf(frame: RenderFrame, nodeId: Int): RenderPatch.CreateNode =
        checkNotNull(
            frame.patches.filterIsInstance<RenderPatch.CreateNode>()
                .singleOrNull { it.nodeId == nodeId }
        ) { "expected exactly one CreateNode for node $nodeId; got ${frame.patches}" }

    /** The single parentless create — a mount frame's root view. */
    private fun root(mount: RenderFrame): RenderPatch.CreateNode =
        checkNotNull(
            mount.patches.filterIsInstance<RenderPatch.CreateNode>()
                .singleOrNull { it.parentId == null }
        ) { "expected exactly one parentless create (the page root); got ${mount.patches}" }

    /** BnDemo's bound input — the shape pin BnSettingsPage lacks. */
    private fun inputNode(mount: RenderFrame): RenderPatch.CreateNode =
        checkNotNull(
            mount.patches.filterIsInstance<RenderPatch.CreateNode>()
                .singleOrNull { it.nodeType == "input" }
        ) { "expected exactly one input create; got ${mount.patches}" }

    /** NodeId of the element CONTAINING the given text (unique per frame). */
    private fun containerOfText(frame: RenderFrame, text: String): Int {
        val t = checkNotNull(
            frame.patches.filterIsInstance<RenderPatch.ReplaceText>()
                .singleOrNull { it.text == text }
        ) { "expected exactly one ReplaceText '$text' in the frame; got ${frame.patches}" }
        return checkNotNull(createOf(frame, t.nodeId).parentId) { "text '$text' node must have a parent" }
    }

    private fun clickHandlerOn(frame: RenderFrame, nodeId: Int): Int =
        checkNotNull(
            frame.patches.filterIsInstance<RenderPatch.AttachEvent>()
                .singleOrNull { it.nodeId == nodeId && it.eventName == "click" }
        ) { "expected exactly one click AttachEvent on node $nodeId; got ${frame.patches}" }.handlerId

    private fun changeHandler(mount: RenderFrame): Int =
        checkNotNull(
            mount.patches.filterIsInstance<RenderPatch.AttachEvent>()
                .singleOrNull { it.eventName == "change" }
        ) { "expected exactly one change AttachEvent; got ${mount.patches}" }.handlerId

    private fun propOn(frame: RenderFrame, nodeId: Int, name: String): RenderPatch.UpdateProp =
        checkNotNull(
            frame.patches.filterIsInstance<RenderPatch.UpdateProp>()
                .singleOrNull { it.nodeId == nodeId && it.name == name }
        ) { "expected exactly one UpdateProp '$name' on node $nodeId; got ${frame.patches}" }

    private fun removedNodes(frame: RenderFrame): Set<Int> =
        frame.patches.filterIsInstance<RenderPatch.RemoveNode>()
            .map { it.nodeId }
            .toSet()

    private fun hasText(frame: RenderFrame, text: String): Boolean =
        frame.patches.filterIsInstance<RenderPatch.ReplaceText>().any { it.text == text }

    // ── Settings → : the swap observed in ONE synchronous dispatch window ────

    @Test
    fun settings_click_swaps_root_inside_one_dispatch() {
        val s = bootDemo()
        val mount = s.frames.first()
        val demoRoot = root(mount).nodeId
        val demoInput = inputNode(mount).nodeId
        val settingsHandler = clickHandlerOn(mount, containerOfText(mount, "Settings →"))
        val framesBefore = s.frames.size

        assertEquals(0, s.runtime.dispatchEventBlocking(settingsHandler, "click"))

        // EVERYTHING below arrived inside that one blocking dispatch call —
        // the window is the frames appended between framesBefore and now.
        val window = s.frames.subList(framesBefore, s.frames.size).toList()
        assertTrue(window.isNotEmpty(), "the swap's frames must arrive inside the dispatch")

        // (a) The host Navigate callback fired with the new route — once.
        assertEquals(listOf("/settings"), s.host.navigations)

        // (b) The old root's RemoveNodes: the form div AND the input's node
        // are gone, nodeId-pinned to this test's own mount frame.
        val removed = window.flatMap { removedNodes(it) }.toSet()
        assertTrue(demoRoot in removed, "BnDemo's root was never removed during the dispatch")
        assertTrue(demoInput in removed, "BnDemo's input was never removed during the dispatch")

        // (c) BnSettingsPage's creates, strictly AFTER the removes (the pin
        // is ORDER, not frame count — the handler's own batch may precede).
        val removeIdx = window.indexOfFirst { demoRoot in removedNodes(it) }
        val createIdx = window.indexOfFirst { hasText(it, "Settings") }
        assertTrue(createIdx >= 0, "settings title text missing from the dispatch window")
        assertTrue(
            createIdx > removeIdx,
            "BnSettingsPage's creates must follow the removes (remove@$removeIdx, create@$createIdx)"
        )

        // The settings mount frame: title span under a fresh themed view
        // root, a "← Back" button with a click attach — and no input
        // anywhere (the shape pin BnSettingsPage lacks by design).
        val settingsMount = window[createIdx]
        val settingsRoot = root(settingsMount)
        assertEquals("view", settingsRoot.nodeType, "the settings page root must be a view/div")
        val title = containerOfText(settingsMount, "Settings")
        assertEquals("text", createOf(settingsMount, title).nodeType, "title must be a text/span")
        assertEquals(settingsRoot.nodeId, createOf(settingsMount, title).parentId)
        clickHandlerOn(settingsMount, containerOfText(settingsMount, "← Back"))
        assertFalse(
            settingsMount.patches.filterIsInstance<RenderPatch.CreateNode>()
                .any { it.nodeType == "input" },
            "BnSettingsPage must not create an input"
        )
    }

    // ── ← Back: BnDemo remounts FRESH (state does not survive the swap) ─────

    @Test
    fun back_click_remounts_bndemo_fresh() {
        val s = bootDemo()
        val mount = s.frames.first()

        // Seed state through the bind loop: type "hello".
        assertEquals(0, s.runtime.dispatchEventBlocking(changeHandler(mount), "change", payload = "hello"))
        assertTrue(hasText(s.frames.last(), "hello"), "seed text never echoed")

        // Navigate to settings via the button, harvest "← Back" from the
        // settings mount frame that arrived inside the dispatch.
        val settingsHandler = clickHandlerOn(mount, containerOfText(mount, "Settings →"))
        var framesBefore = s.frames.size
        assertEquals(0, s.runtime.dispatchEventBlocking(settingsHandler, "click"))
        val settingsMount = checkNotNull(
            s.frames.subList(framesBefore, s.frames.size).singleOrNull { hasText(it, "Settings") }
        ) { "expected exactly one settings mount frame inside the dispatch" }
        val backHandler = clickHandlerOn(settingsMount, containerOfText(settingsMount, "← Back"))

        framesBefore = s.frames.size
        assertEquals(0, s.runtime.dispatchEventBlocking(backHandler, "click"))
        val window = s.frames.subList(framesBefore, s.frames.size).toList()

        // The host heard both navigations, in order.
        assertEquals(listOf("/settings", "/"), s.host.navigations)

        // The remount frame is a FRESH BnDemo (the .NET twin's
        // NavigateBack_RemountsFresh pins): empty value prop + empty echo,
        // and the seeded text is nowhere in the new tree.
        val remount = window.last()
        val input = inputNode(remount)
        assertEquals("", propOn(remount, input.nodeId, "value").value)
        assertTrue(hasText(remount, ""), "fresh echo text missing")
        assertFalse(hasText(remount, "hello"), "stale state survived the remount")

        // And the settings page actually left the screen: its root's
        // RemoveNode arrived in this window too.
        val settingsRoot = root(settingsMount).nodeId
        assertTrue(
            window.flatMap { removedNodes(it) }.contains(settingsRoot),
            "the settings root was never removed during the back dispatch"
        )
    }
}
