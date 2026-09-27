package io.blazornative.jni

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.util.Collections
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean

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
        override fun clipboardRead(): String = ""
        override fun clipboardWrite(text: String) {}
        override fun share(text: String) {}
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

    // ── nit: the onError message contract (frozen rc-2 wording) ──────────────

    @Test
    fun describeHostEventFailure_rc2_carries_frozen_wording() {
        // Parity with DispatchEventTest.describeDispatchFailure_rc2_… — the
        // KDoc claims "unit-tested", so pin the rc-2 wording + the desktop-JVM
        // reproduction hint (Android stderr is /dev/null).
        val runtime = BlazorNativeRuntime(onFrame = {})

        val msg = runtime.describeHostEventFailure(2, name = "onPause")

        assertTrue(
            msg.contains("faulted — a NativeEvents subscriber, its re-render, or the back swap threw"),
            "rc-2 message must carry the frozen wording; got: $msg"
        )
        // Phase 11.4 Gate B: see the twin note in DispatchEventTest — the
        // desktop-JVM workaround existed because Android discarded the detail, and
        // the stderr → logcat pump retired it.
        assertTrue(
            msg.contains("detail on the runtime's stderr — logcat `BlazorNative/…` on Android"),
            "rc-2 message must point at the destination the detail ACTUALLY reaches; got: $msg"
        )
        assertTrue(
            !msg.contains("reproduce on desktop JVM"),
            "the pre-11.4 desktop-JVM workaround must be gone; got: $msg"
        )
    }

    // ── nit: the async PRODUCTION path routes a non-zero rc to onError ───────

    @Test
    fun async_dispatchHostEvent_nonzero_rc_routes_to_onError() {
        // Closes the coverage asymmetry vs dispatchEvent: the async production
        // path (dispatchHostEvent → the BlazorNative-Dispatch lane) must route
        // a non-zero rc to onError. An empty name → rc 3 (malformed) is chosen
        // because it is DETERMINISTIC regardless of the process-global session's
        // nav state (a "back" rc would depend on whether a prior test left a
        // previous-route slot). The point is the routing, not the specific rc.
        val errors = Collections.synchronizedList(mutableListOf<String>())
        val latch = CountDownLatch(1)
        val host = InertHost()
        val runtime = BlazorNativeRuntime(
            onFrame = {},
            onError = { msg, _ -> errors.add(msg); latch.countDown() },
        )
        runtime.start(componentName = "BnDemo", platformOs = "test-host", bridge = host)

        runtime.dispatchHostEventUnchecked("") // empty name → rc 3 → onError, on the lane

        assertTrue(
            latch.await(5, TimeUnit.SECONDS),
            "onError must fire for a non-zero host_event rc; errors=$errors"
        )
        assertTrue(
            errors.any { it.contains("rc 3") },
            "onError message must describe the non-zero rc; got $errors"
        )
        assertTrue(runtime.retire(), "the lane must drain after the dispatch")
    }

    // ── Android measurement (phase 14.1, Step 3): does dispatchHostEventAndWait ──
    // ── deadlock behind a held dispatch lane, mirroring #339 on predictive-back? ─

    /** Host whose hostCallBegin (Camera op) records the request and NEVER completes
     * it — the JVM/Android twin of FakeShellHost.AutoCompleteHostCall = false in
     * tests/BlazorNative.Runtime.Tests/DispatchLaneBlockingTests.cs: the call stays
     * open exactly as it does while an OS permission sheet is up, unanswered. */
    private class StallingCameraHost : ShellBridgeHandlers {
        @Volatile private var route: String = "/"
        val callReceived = CountDownLatch(1)
        @Volatile var heldRequestId: Long = -1
        override fun navigate(route: String) { this.route = route }
        override fun currentRoute(): String = route
        override fun storageRead(key: String): String? = null
        override fun storageWrite(key: String, value: String) {}
        override fun storageDelete(key: String) {}
        override fun fetchBegin(requestId: Long, request: BridgeFetchRequest) {
            BridgeFetchCompleter.completeFailure(requestId, "HostEventTest performs no fetch")
        }
        override fun clipboardRead(): String = ""
        override fun clipboardWrite(text: String) {}
        override fun share(text: String) {}
        override fun hostCallBegin(requestId: Long, op: Int, argsJson: String) {
            heldRequestId = requestId
            callReceived.countDown() // NOT completed here — the call stays open for the
            // measurement window; the test completes it explicitly afterwards (see below).
        }
    }

    /**
     * #346, FIXED BY PHASE 16.1 and pinned the right way round.
     *
     * Until Phase 16.2, MainActivity's `handleBack` called
     * [BlazorNativeRuntime.dispatchHostEventAndWait] from the main thread inside the
     * predictive-back callback; since 16.2 it dispatches back fire-and-forget and this test
     * drives the blocking method directly. That method does an untimed `future.get()`
     * against the single `BlazorNative-Dispatch` lane. #339's
     * condition is a PRIOR async handler suspended on an open host call: the unanswered-
     * permission-sheet state. Until 16.1 that handler held the lane's one worker thread
     * inside `blazornative_dispatch_event`, so the back Callable queued behind it and
     * `future.get()` blocked indefinitely (measured in phase 14.1, see
     * docs/plans/2026-09-21-phase-14.1-conclusion.md). Since 16.1 the export returns once
     * the handler's synchronous part has run (#345), so the lane frees and back runs.
     *
     * The assertion is only that the call RETURNS within 2 s. It deliberately does NOT
     * assert the rc: the JVM session is never reset between test classes, so the
     * navigation history (`_previousRoute`) left by earlier classes decides whether back
     * is handled, and the rc would depend on test order. The back verdict itself is
     * pinned in phase 16.2.
     *
     * DOES NOT COVER: Android's OnBackInvokedCallback itself (the instrumented lane), or a
     * handler that holds the render thread synchronously without ever yielding.
     *
     * Bounded throughout: every wait carries an explicit timeout, and the held host call
     * is completed in a `finally`, so a failure here cannot leave the process-global
     * session wedged for the test classes that run after this one.
     */
    @Test
    fun dispatchHostEventAndWait_returns_while_a_handler_holds_a_host_call() {
        // Phase 16.1: flipped from "still deadlocks behind a held dispatch lane" (#346): the lane now frees when the handler yields.
        val host = StallingCameraHost()
        val frames = Collections.synchronizedList(mutableListOf<RenderFrame>())
        val runtime = BlazorNativeRuntime(onFrame = { frames.add(it) })
        runtime.start(componentName = "BnCameraDemo", platformOs = "test-host", bridge = host)
        assertTrue(frames.isNotEmpty(), "mount must deliver the first frame synchronously")

        val mount = frames.first()
        val takePhoto = clickHandlerOn(mount, containerOfText(mount, "Take Photo"))

        var backThread: Thread? = null
        var drained = false
        try {
            // Fire-and-forget dispatch of Take Photo. Its handler awaits the host call
            // StallingCameraHost never completes: the #339 state.
            runtime.dispatchEvent(takePhoto, "click")
            assertTrue(
                host.callReceived.await(10, TimeUnit.SECONDS),
                "the camera host call never arrived — the lane never even started the dispatch, " +
                    "so this run cannot measure anything"
            )

            // From another thread — standing in for Android's main thread inside
            // OnBackInvokedCallback — make the call MainActivity's handleBack made until Phase 16.2.
            val backReturned = AtomicBoolean(false)
            var backThrew: Throwable? = null
            backThread = Thread({
                try {
                    runtime.dispatchHostEventAndWait(BnHostEvent.Back)
                } catch (t: Throwable) {
                    backThrew = t
                } finally {
                    backReturned.set(true)
                }
            }, "back-probe").apply { isDaemon = true; start() }
            backThread.join(2_000) // BOUNDED — never Thread.join() with no timeout

            assertTrue(
                backReturned.get(),
                "dispatchHostEventAndWait did NOT return within 2 s while an async handler held a " +
                    "host call open. This is #346: the dispatch lane is still held by the export, " +
                    "so predictive back deadlocks behind it."
            )
            assertEquals(null, backThrew, "the back dispatch must return, not throw")
            // Anchor: the camera call was received, and StallingCameraHost never completes it,
            // so back was measured against a handler still suspended on it.
            assertTrue(host.heldRequestId >= 0, "the held camera request id was never recorded")
        } finally {
            // Release the held call whatever happened above: it sits on the ONE process-global
            // .NET session every JVM test class in this run shares.
            if (host.heldRequestId >= 0) {
                BridgeHostCallCompleter.complete(host.heldRequestId, CameraStatus.CANCELLED, null)
            }
            backThread?.join(10_000) // bounded — the lane frees promptly once released
            drained = runtime.retire()
        }
        assertTrue(drained, "the lane must drain once the held call is released")
    }
}
