package io.blazornative.jni

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/**
 * Phase 5.1 Gate 2 — host-INITIATED events driven through the published
 * NativeAOT dll: the Kotlin twin of tests/BlazorNative.Runtime.Tests/
 * HostEventProbeTests.cs + the "back" host-event routing in NavigationTests.cs
 * (M5 DoD #5).
 *
 * Two proofs, both through blazornative_host_event (the 9th export):
 *   (1) a lifecycle event (dispatchHostEvent("onResume")) reaches the mounted
 *       HostEventProbe and re-renders its echo BnText, nodeId-pinned, counting;
 *   (2) the reserved "back" host event routes to NavigateBack — mount BnDemo,
 *       click "Settings →" (settings mounts), then dispatchHostEvent("back")
 *       returns BnDemo (rc 0 handled), and a further "back" at the root is
 *       rc 1 (not handled — the shell would finish).
 *
 * The back→NavigateBack mapping lives in .NET (Exports.DispatchHostEventCore
 * intercepts the reserved name), so this JVM path and Android's predictive-back
 * (Gate 3) drive the SAME ingress with identical semantics.
 *
 * Node identification is ALWAYS structural / by text (ids are process-global
 * monotonic counters — every id is harvested from this test's own mount frame),
 * the NavigationTest/BnDemoTest convention. Safe alongside the other native
 * tests in one JVM process (idempotent init, last-wins registration, fresh
 * mounts).
 */
class HostEventTest {

    /** Inert in-memory host: BnDemo's Settings→/Back buttons call navigate +
     * currentRoute; the probe touches nothing. Storage/fetch are unused. */
    private class InertHost : ShellBridgeHandlers {
        @Volatile private var route: String = "/"
        val navigations = mutableListOf<String>()
        override fun navigate(route: String) { navigations.add(route); this.route = route }
        override fun currentRoute(): String = route
        override fun storageRead(key: String): String? = null
        override fun storageWrite(key: String, value: String) {}
        override fun storageDelete(key: String) {}
        override fun fetchBegin(requestId: Long, request: BridgeFetchRequest) {
            BridgeFetchCompleter.completeFailure(requestId, "HostEventTest performs no fetch")
        }
    }

    private class Session(
        val runtime: BlazorNativeRuntime,
        val frames: MutableList<RenderFrame>,
        val host: InertHost,
    )

    private fun boot(componentName: String): Session {
        val frames = mutableListOf<RenderFrame>()
        val host = InertHost()
        val runtime = BlazorNativeRuntime(onFrame = { frames.add(it) })
        runtime.start(componentName = componentName, platformOs = "test-host", bridge = host)
        assertTrue(frames.isNotEmpty(), "mount must deliver the first frame synchronously")
        return Session(runtime, frames, host)
    }

    // ── Structural pin helpers (NavigationTest conventions) ──────────────────

    private fun root(mount: RenderFrame): RenderPatch.CreateNode =
        checkNotNull(
            mount.patches.filterIsInstance<RenderPatch.CreateNode>().singleOrNull { it.parentId == null }
        ) { "expected exactly one parentless create (the root); got ${mount.patches}" }

    private fun inputNode(mount: RenderFrame): RenderPatch.CreateNode =
        checkNotNull(
            mount.patches.filterIsInstance<RenderPatch.CreateNode>().singleOrNull { it.nodeType == "input" }
        ) { "expected exactly one input create; got ${mount.patches}" }

    private fun createOf(frame: RenderFrame, nodeId: Int): RenderPatch.CreateNode =
        checkNotNull(
            frame.patches.filterIsInstance<RenderPatch.CreateNode>().singleOrNull { it.nodeId == nodeId }
        ) { "expected exactly one CreateNode for node $nodeId; got ${frame.patches}" }

    private fun containerOfText(frame: RenderFrame, text: String): Int {
        val t = checkNotNull(
            frame.patches.filterIsInstance<RenderPatch.ReplaceText>().singleOrNull { it.text == text }
        ) { "expected exactly one ReplaceText '$text'; got ${frame.patches}" }
        return checkNotNull(createOf(frame, t.nodeId).parentId) { "text '$text' node must have a parent" }
    }

    private fun clickHandlerOn(frame: RenderFrame, nodeId: Int): Int =
        checkNotNull(
            frame.patches.filterIsInstance<RenderPatch.AttachEvent>()
                .singleOrNull { it.nodeId == nodeId && it.eventName == "click" }
        ) { "expected exactly one click AttachEvent on node $nodeId; got ${frame.patches}" }.handlerId

    private fun removedNodes(frame: RenderFrame): Set<Int> =
        frame.patches.filterIsInstance<RenderPatch.RemoveNode>().map { it.nodeId }.toSet()

    private fun hasText(frame: RenderFrame, text: String): Boolean =
        frame.patches.filterIsInstance<RenderPatch.ReplaceText>().any { it.text == text }

    /** The HostEventProbe echo BnText's TEXT node, pinned at mount: root div →
     * the span (text-type child of the root) → its single child text node (the
     * FocusProbe/HostEventProbeTests structural walk). */
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

    // ── (1) lifecycle host event reaches the probe, re-renders, counts ───────

    @Test
    fun host_event_reaches_probe_and_rerenders_nodeid_pinned() {
        val s = boot("HostEventProbe")
        val mount = s.frames.first()
        val echo = echoTextNode(mount)
        assertEquals("", replaceTextOn(listOf(mount), echo, "").text) // empty at mount

        // onResume → the echo BnText re-renders "onResume (1)" on ITS
        // mount-pinned text node (the host event reached the mounted component).
        var before = s.frames.size
        assertEquals(0, s.runtime.dispatchHostEventBlocking("onResume"))
        var window = s.frames.subList(before, s.frames.size).toList()
        replaceTextOn(window, echo, "onResume (1)")

        // A second event increments the count — same node.
        before = s.frames.size
        assertEquals(0, s.runtime.dispatchHostEventBlocking("onPause"))
        window = s.frames.subList(before, s.frames.size).toList()
        replaceTextOn(window, echo, "onPause (2)")

        s.runtime.retire()
    }

    // ── (2) the reserved "back" host event → NavigateBack through the dll ────

    @Test
    fun back_host_event_navigates_back_through_the_dll() {
        val s = boot("BnDemo")
        val mount = s.frames.first()
        val demoRoot = root(mount).nodeId

        // Forward to /settings via the button's own click dispatch.
        val settingsHandler = clickHandlerOn(mount, containerOfText(mount, "Settings →"))
        var before = s.frames.size
        assertEquals(0, s.runtime.dispatchEventBlocking(settingsHandler, "click"))
        val settingsMount = checkNotNull(
            s.frames.subList(before, s.frames.size).singleOrNull { hasText(it, "Settings") }
        ) { "expected exactly one settings mount frame inside the dispatch" }
        val settingsRoot = root(settingsMount).nodeId

        // "back" host event → NavigateBack: rc 0 (handled), BnDemo returns.
        before = s.frames.size
        assertEquals(0, s.runtime.dispatchHostEventBlocking("back"))
        val window = s.frames.subList(before, s.frames.size).toList()

        // BnDemo returned fresh (its bound input shape) and settings left screen.
        assertTrue(window.any { it.patches.filterIsInstance<RenderPatch.CreateNode>().any { c -> c.nodeType == "input" } },
            "BnDemo's input shape must return after back")
        assertTrue(window.flatMap { removedNodes(it) }.contains(settingsRoot),
            "the settings root was never removed during the back")
        assertEquals(listOf("/settings", "/"), s.host.navigations)

        // Slot consumed: a further "back" at the root is NOT handled (rc 1 —
        // the shell would fall through to default finish). Demo root untouched.
        assertEquals(1, s.runtime.dispatchHostEventBlocking("back"))
        assertFalse(demoRoot in s.frames.last().let { removedNodes(it) },
            "a not-handled back must not swap anything")

        s.runtime.retire()
    }
}
