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

    /** The control for the pin above. The exemption is for BACK's rc 1 only: another event's
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
