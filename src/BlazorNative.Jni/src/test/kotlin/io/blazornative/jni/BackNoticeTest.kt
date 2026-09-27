package io.blazornative.jni

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.util.Collections
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicInteger

/**
 * Phase 16.2 (#346) — .NET's two back notices reach the listeners [BlazorNativeRuntime] was
 * given, through the published NativeAOT dll.
 *
 * Android no longer asks .NET "can you go back?" on the main thread. .NET pushes the answer as
 * a BackState notice, op 6, and a back that still reaches .NET with nothing to go back to sends
 * BackUnhandled, op 7, so the shell finishes. The shared [BridgeRegistrar] answers both, as it
 * answers FaultNotice: the host's own [ShellBridgeHandlers] never see them, so no host can drop
 * one, and each is completed OK.
 *
 * It also pins the other half of taking back off the main thread: back is now dispatched
 * fire-and-forget, so its rc 1 is not read by anyone. rc 1 from a back is a NORMAL outcome whose
 * channel is BackUnhandled, and it must not also raise onError. Every other event's rc 1 still
 * does, which the navigate control below proves.
 *
 * The session is the one process-global .NET session every JVM test shares, so the nav history
 * left by earlier classes is unknown. Each test first DRAINS it with one back: a back consumes
 * the only history slot and never re-arms it, so after one back the session is at its root
 * whatever came before.
 *
 * DOES NOT COVER: what the Android shell does with the notices, which is MainActivity's and
 * WidgetMapper's, pinned by the instrumented BackAndroidTest; the buffer that holds BackState
 * until its frame, pinned by BackStateBufferTest; the iOS arms; or the notices' exact args and
 * ORDER relative to frames, which BackStateNoticeTests.cs pins on the .NET side.
 */
class BackNoticeTest {

    /** An in-memory host that records every op its own hostCallBegin is handed. */
    private class RecordingHost : ShellBridgeHandlers {
        @Volatile private var route: String = "/"
        val ops: MutableList<Int> = Collections.synchronizedList(mutableListOf())
        override fun navigate(route: String) { this.route = route }
        override fun currentRoute(): String = route
        override fun storageRead(key: String): String? = null
        override fun storageWrite(key: String, value: String) {}
        override fun storageDelete(key: String) {}
        override fun fetchBegin(requestId: Long, request: BridgeFetchRequest) {
            BridgeFetchCompleter.completeFailure(requestId, "BackNoticeTest performs no fetch")
        }
        override fun clipboardRead(): String = ""
        override fun clipboardWrite(text: String) {}
        override fun share(text: String) {}
        override fun hostCallBegin(requestId: Long, op: Int, argsJson: String) {
            ops.add(op)
            BridgeHostCallCompleter.complete(requestId, HostCallStatus.ERROR, null)
        }
    }

    private class Session(
        val runtime: BlazorNativeRuntime,
        val host: RecordingHost,
        val frames: MutableList<RenderFrame>,
        val states: MutableList<Boolean>,
        val unhandled: AtomicInteger,
        val errors: MutableList<String>,
    )

    private fun boot(onUnhandled: () -> Unit = {}, onErrorSeen: (String) -> Unit = {}): Session {
        val host = RecordingHost()
        val frames = Collections.synchronizedList(mutableListOf<RenderFrame>())
        val states = Collections.synchronizedList(mutableListOf<Boolean>())
        val unhandled = AtomicInteger()
        val errors = Collections.synchronizedList(mutableListOf<String>())
        val runtime = BlazorNativeRuntime(
            onFrame = { frames.add(it) },
            onError = { msg, _ -> errors.add(msg); onErrorSeen(msg) },
            onBackState = { states.add(it) },
            onBackUnhandled = { unhandled.incrementAndGet(); onUnhandled() },
        )
        runtime.start(componentName = "BnDemo", platformOs = "test-host", bridge = host)
        assertTrue(frames.isNotEmpty(), "mount must deliver the first frame synchronously")
        return Session(runtime, host, frames, states, unhandled, errors)
    }

    /** One back, inline, so the session is at its root afterwards whatever history earlier
     * classes left. Its rc is 0 or 1 depending on that history, so it is not asserted. */
    private fun drainToRoot(s: Session) {
        val rc = s.runtime.dispatchHostEventBlocking(BnHostEvent.Back.wireName)
        assertTrue(rc == 0 || rc == 1, "the draining back faulted: rc $rc")
        s.states.clear()
        s.unhandled.set(0)
        s.errors.clear()
        BridgeRegistrar.noticeCompleteRcForTest.clear()
    }

    private fun waitFor(deadlineMs: Long, predicate: () -> Boolean): Boolean {
        val deadline = System.nanoTime() + TimeUnit.MILLISECONDS.toNanos(deadlineMs)
        while (System.nanoTime() < deadline) {
            if (predicate()) return true
            Thread.sleep(10)
        }
        return predicate()
    }

    @Test
    fun the_back_notices_are_ops_six_and_seven() {
        assertEquals(6, HostCallOp.BACK_STATE)
        assertEquals(7, HostCallOp.BACK_UNHANDLED)
    }

    @Test
    fun a_back_state_notice_reaches_the_back_state_listener_with_its_value_and_is_completed() {
        val s = boot()
        try {
            drainToRoot(s)

            // A forward navigation makes canGoBack true: exactly one BackState(true). Driven
            // through the navigate host event rather than a button, because the drain may
            // have left the session on any page, and a forward step from any page arms back.
            assertEquals(0, s.runtime.dispatchHostEventBlocking(BnHostEvent.Navigate.wireName, "/settings"),
                "the navigate to /settings must be handled")
            assertTrue(waitFor(10_000) { s.states.isNotEmpty() },
                "leaving the root sent no BackState to the back-state listener within 10s. " +
                    "Op 6 is routed somewhere else, or .NET stopped sending it.")
            assertEquals(listOf(true), s.states.toList(), "the back-state listener saw ${s.states}")

            // Going back makes it false again: the value is carried, not just the event.
            assertEquals(0, s.runtime.dispatchHostEventBlocking(BnHostEvent.Back.wireName),
                "the back from /settings must be handled")
            assertTrue(waitFor(10_000) { s.states.size >= 2 }, "going back sent no second BackState")
            assertEquals(listOf(true, false), s.states.toList(), "the back-state listener saw ${s.states}")

            // BackState is not BackUnhandled: the other listener stayed silent.
            assertEquals(0, s.unhandled.get(), "a BackState notice reached the unhandled listener")
            // Completed OK, and .NET found its pending entry: rc 0.
            assertTrue(waitFor(10_000) { BridgeRegistrar.noticeCompleteRcForTest[HostCallOp.BACK_STATE] == 0 },
                "the BackState notice was not completed OK: ${BridgeRegistrar.noticeCompleteRcForTest}")
            // The registrar answered it: the host's own handlers never saw op 6.
            assertFalse(s.host.ops.contains(HostCallOp.BACK_STATE), "the host's handlers saw ops ${s.host.ops}")
            assertTrue(s.errors.isEmpty(), "onError fired: ${s.errors}")
        } finally {
            s.runtime.retire()
        }
    }

    @Test
    fun a_back_at_the_root_routes_back_unhandled_and_raises_no_onError() {
        val unhandledSeen = CountDownLatch(1)
        val s = boot(onUnhandled = { unhandledSeen.countDown() })
        try {
            drainToRoot(s)

            // The production path: fire-and-forget, on the dispatch lane, at the root.
            s.runtime.dispatchHostEvent(BnHostEvent.Back)

            assertTrue(unhandledSeen.await(10, TimeUnit.SECONDS),
                "a back at the root sent no BackUnhandled to the unhandled listener within 10s. " +
                    "The shell would never finish, so the press is swallowed.")
            assertTrue(s.runtime.retire(), "the lane must drain after the back")
            assertEquals(1, s.unhandled.get(), "one back at the root, one BackUnhandled")
            assertTrue(s.states.isEmpty(), "a back at the root changed no state, yet BackState said ${s.states}")
            assertTrue(waitFor(10_000) { BridgeRegistrar.noticeCompleteRcForTest[HostCallOp.BACK_UNHANDLED] == 0 },
                "the BackUnhandled notice was not completed OK: ${BridgeRegistrar.noticeCompleteRcForTest}")
            assertFalse(s.host.ops.contains(HostCallOp.BACK_UNHANDLED), "the host's handlers saw ops ${s.host.ops}")

            // THE PIN: rc 1 from a back is the BackUnhandled outcome, not an error.
            assertTrue(s.errors.isEmpty(),
                "a back at the root raised onError: ${s.errors}. Its rc 1 is a normal outcome whose " +
                    "channel is BackUnhandled; reporting it as well logs a spurious error on every " +
                    "back that finishes the app.")
        } finally {
            s.runtime.retire()
        }
    }

    /**
     * The JVM twin of the instrumented "same runnable" pin, against the dll's REAL frames.
     * WidgetMapper offers each BackState to a [io.blazornative.shell.BackStateBuffer] and asks
     * it, at every CommitFrame, what the batch carries; this drives the same two calls from the
     * runtime's own listener and frame callback, in the order the shell receives them. A swap is
     * two frames, the old root's removal and the new page; the value must ride the page's.
     */
    @Test
    fun the_back_state_rides_the_frame_that_mounts_the_page_not_the_removal() {
        val buffer = io.blazornative.shell.BackStateBuffer()
        val carried = Collections.synchronizedList(mutableListOf<Pair<RenderFrame, Boolean?>>())
        val runtime = BlazorNativeRuntime(
            onFrame = { f -> carried.add(f to buffer.takeForBatch(f.patches)) },
            onBackState = { buffer.offer(it) },
        )
        runtime.start(componentName = "BnDemo", platformOs = "test-host", bridge = RecordingHost())
        try {
            // Drain to the root, then start from an empty log.
            runtime.dispatchHostEventBlocking(BnHostEvent.Back.wireName)
            carried.clear()

            assertEquals(0, runtime.dispatchHostEventBlocking(BnHostEvent.Navigate.wireName, "/settings"))
            val window = carried.toList()

            // Anchor, Rule 4: the swap is the two-frame shape this pin is about. If it ever
            // becomes one frame, re-point the pin deliberately rather than let it pass.
            val removal = window.indexOfFirst { (f, _) -> f.patches.any { it is RenderPatch.RemoveNode } }
            val page = window.indexOfFirst { (f, _) -> f.patches.any { it is RenderPatch.ReplaceText && it.text == "Settings" } }
            assertTrue(removal >= 0 && page > removal,
                "expected the old root's removal frame, then the settings page's frame; got " +
                    window.map { (f, c) -> "${f.patches.size} patches carried=$c" })

            assertEquals(true, window[page].second,
                "the settings page's frame did not carry BackState(true)")
            assertTrue(window.take(page).all { it.second == null },
                "a frame BEFORE the page carried the back state, so back would change while the old " +
                    "page or a blank screen is shown: ${window.map { it.second }}")
        } finally {
            runtime.retire()
        }
    }

    /**
     * Fix round 2, against the dll's REAL frames: a page whose FIRST render creates nothing
     * mounts with a frame that is a lone CommitFrame (measured: the swap into such a page is
     * `removal | CommitFrame only | the later content`, and a mount is `CommitFrame only |
     * the later content`). That frame is the one that puts the new page on screen, so the back
     * state .NET sent for the mount must ride it. A rule that waited for a created root node
     * left back disabled on the sub-page until content appeared, if it ever did.
     *
     * Driven as a MOUNT, not a navigation: EmptyFirstRenderProbe is a Named scaffolding page
     * with no route. A navigation's second frame is the same MountRoot, so the frame is the
     * same one.
     */
    @Test
    fun the_back_state_rides_the_mount_of_a_page_whose_first_render_is_empty() {
        // History first, so the mount below sends BackState(true).
        val warmup = BlazorNativeRuntime(onFrame = {})
        warmup.start(componentName = "BnDemo", platformOs = "test-host", bridge = RecordingHost())
        assertEquals(0, warmup.dispatchHostEventBlocking(BnHostEvent.Navigate.wireName, "/settings"))
        warmup.retire()

        val buffer = io.blazornative.shell.BackStateBuffer()
        val carried = Collections.synchronizedList(mutableListOf<Pair<RenderFrame, Boolean?>>())
        val runtime = BlazorNativeRuntime(
            onFrame = { f -> carried.add(f to buffer.takeForBatch(f.patches)) },
            onBackState = { buffer.offer(it) },
        )
        runtime.start(componentName = "EmptyFirstRenderProbe", platformOs = "test-host", bridge = RecordingHost())
        try {
            // Wait for the content, so a rule that carried the value late is observable too.
            assertTrue(waitFor(10_000) {
                carried.any { (f, _) -> f.patches.any { it is RenderPatch.ReplaceText && it.text == "loaded" } }
            }, "the probe's content never rendered")
            val window = carried.toList()

            // Anchor, Rule 4: the mount frame is the empty shape this pin is about.
            assertTrue(window[0].first.patches.all { it is RenderPatch.CommitFrame },
                "the probe's mount frame is no longer a lone CommitFrame, so this pin no longer " +
                    "exercises an empty first render; re-point it. Got ${window[0].first.patches}")

            assertEquals(true, window[0].second,
                "the empty page's mount frame did not carry BackState(true): back stays disabled " +
                    "on a sub-page, and a press exits the app. Carried per frame: ${window.map { it.second }}")
        } finally {
            runtime.retire()
        }
    }

    /** Holds the camera call open and records it; the test answers it. */
    private class HoldingCameraHost : ShellBridgeHandlers {
        @Volatile private var route: String = "/"
        val cameraCall = CountDownLatch(1)
        @Volatile var heldRequestId: Long = -1
        override fun navigate(route: String) { this.route = route }
        override fun currentRoute(): String = route
        override fun storageRead(key: String): String? = null
        override fun storageWrite(key: String, value: String) {}
        override fun storageDelete(key: String) {}
        override fun fetchBegin(requestId: Long, request: BridgeFetchRequest) {
            BridgeFetchCompleter.completeFailure(requestId, "BackNoticeTest performs no fetch")
        }
        override fun clipboardRead(): String = ""
        override fun clipboardWrite(text: String) {}
        override fun share(text: String) {}
        override fun hostCallBegin(requestId: Long, op: Int, argsJson: String) {
            if (op == HostCallOp.CAMERA) {
                heldRequestId = requestId
                cameraCall.countDown() // NOT completed: the test answers it
            } else {
                BridgeHostCallCompleter.complete(requestId, HostCallStatus.ERROR, null)
            }
        }
    }

    /**
     * The positive control for BackAndroidTest's #346 pin: BackHoldProbe really HOLDS the
     * dispatch lane. Its synchronous "Hold" handler blocks the render thread on a camera call,
     * so the dispatch export that runs it cannot return, and the pre-16.2 back path, the
     * blocking [BlazorNativeRuntime.dispatchHostEventAndWait], cannot either, until the call is
     * released. That is what makes the device pin able to tell the old back from the new one:
     * if this probe ever stopped holding, the old code would pass it too. It also proves the
     * probe does not deadlock: after the release, the held handler finishes and the back lands.
     *
     * DOES NOT COVER: the new, fire-and-forget back itself, which returns at once by
     * construction here; the device pin times it from Android's main thread.
     */
    @Test
    fun back_hold_probe_holds_the_lane_until_its_camera_call_is_released() {
        // History first: a forward step from BnDemo, so the probe's back is handled.
        val warmup = BlazorNativeRuntime(onFrame = {})
        warmup.start(componentName = "BnDemo", platformOs = "test-host", bridge = RecordingHost())
        assertEquals(0, warmup.dispatchHostEventBlocking(BnHostEvent.Navigate.wireName, "/settings"))
        warmup.retire()

        val host = HoldingCameraHost()
        val frames = Collections.synchronizedList(mutableListOf<RenderFrame>())
        val runtime = BlazorNativeRuntime(onFrame = { frames.add(it) })
        runtime.start(componentName = "BackHoldProbe", platformOs = "test-host", bridge = host)
        var releaser: Thread? = null
        try {
            val mount = frames.first()
            val hold = checkNotNull(
                mount.patches.filterIsInstance<RenderPatch.AttachEvent>().singleOrNull { it.eventName == "click" }
            ) { "expected exactly one click wire, the Hold button; got ${mount.patches}" }.handlerId

            runtime.dispatchEvent(hold, "click")
            assertTrue(host.cameraCall.await(10, TimeUnit.SECONDS), "Hold never began the camera call")

            // Release after 1.5 s, from a thread of its own, as the shell's completer would.
            val releasedAt = java.util.concurrent.atomic.AtomicLong(0)
            releaser = Thread({
                Thread.sleep(1_500)
                releasedAt.set(System.nanoTime())
                BridgeHostCallCompleter.complete(host.heldRequestId, CameraStatus.CANCELLED, null)
            }, "releaser").apply { isDaemon = true; start() }

            val rc = runtime.dispatchHostEventAndWait(BnHostEvent.Back)
            val returnedAt = System.nanoTime()

            assertTrue(releasedAt.get() != 0L && returnedAt >= releasedAt.get(),
                "the blocking back returned BEFORE the held camera call was released, so the probe " +
                    "does not hold the lane and cannot tell the old back from the new")
            assertEquals(0, rc, "after the release the back must be handled: the probe had history")
            assertTrue(frames.any { f -> f.patches.any { it is RenderPatch.ReplaceText && it.text == "released:Cancelled" } },
                "the held handler never finished after the release: ${frames.size} frames")
        } finally {
            releaser?.join(10_000)
            runtime.retire()
        }
    }

    /** The control for a_back_at_the_root_routes_back_unhandled_and_raises_no_onError. The exemption is for BACK's rc 1 only: another event's
     * rc 1 still reaches onError, on the same runtime and the same lane. Without this, a
     * runtime that routed nothing to onError at all would pass the pin above. */
    @Test
    fun a_navigate_that_is_not_handled_still_raises_onError() {
        val errorSeen = CountDownLatch(1)
        val s = boot(onErrorSeen = { errorSeen.countDown() })
        try {
            // An unknown route: DispatchHostNavigate's rc 1, not handled.
            s.runtime.dispatchHostEvent(BnHostEvent.Navigate, "/no-such-route-16-2")

            assertTrue(errorSeen.await(10, TimeUnit.SECONDS),
                "a navigate to an unknown route raised no onError within 10s. The rc-1 exemption " +
                    "has widened past back, or onError is no longer wired: ${s.errors}")
            assertTrue(s.errors.any { it.contains("rc 1") && it.contains("navigate") },
                "onError must describe the navigate's rc 1; got ${s.errors}")
            assertEquals(0, s.unhandled.get(), "a navigate is not a back, yet BackUnhandled fired")
        } finally {
            s.runtime.retire()
        }
    }
}
