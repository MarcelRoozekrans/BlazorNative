package io.blazornative.jni

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.util.Collections
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference

/**
 * Phase 3.2 Gate 2 — blazornative_dispatch_event proven on the desktop JVM
 * against the win-x64 NativeAOT dll, through [BlazorNativeRuntime]'s dispatch
 * seam ([BlazorNativeRuntime.dispatchEventBlocking] — the tests' calling
 * thread IS the dispatch-discipline thread, so the inline variant is the
 * honest one for the rc-contract tests). The async PRODUCTION path
 * ([BlazorNativeRuntime.dispatchEvent] → the BlazorNative-Dispatch lane) has
 * its own latch-based test pinning the threading contract.
 *
 * Return-code contract under test (frozen at Gate 1 close — Exports.cs):
 *   0 = dispatched (incl. stale-handler at-most-once)
 *   1 = no session/nothing mounted
 *   2 = dispatch faulted — the handler, the resulting re-render, or frame
 *       delivery threw (detail on native stderr)
 *   3 = malformed/NULL args OR handlerId > int.MaxValue
 *
 * Synchronous contract: the handler, the re-render, AND the frame callback
 * all complete before the export returns — the tests assert the re-render
 * frame is ALREADY captured when dispatchEventBlocking returns, no waiting.
 *
 * Safe alongside the other native tests in one JVM process: init is
 * idempotent, frame-callback registration is last-wins (each test's runtime
 * takes over the slot), and every mount adds a FRESH HelloComponent instance
 * (taps start at 0) on the process-global session.
 */
class DispatchEventTest {

    /** Boots a fresh runtime + mounts HelloComponent, capturing every frame
     * (the mount frame is frames[0] by the sync-mount contract). */
    private fun bootHello(): Pair<BlazorNativeRuntime, MutableList<RenderFrame>> {
        val frames = mutableListOf<RenderFrame>()
        val runtime = BlazorNativeRuntime(onFrame = { frames.add(it) })
        runtime.start(platformOs = "test-host")
        assertTrue(frames.isNotEmpty(), "mount must deliver the first frame synchronously")
        return runtime to frames
    }

    /** HandlerId of the LATEST AttachEvent for [eventName] across all captured
     * frames — Blazor may re-issue handler ids on a re-render, so the freshest
     * attach wins (the same rule WidgetMapper's re-attach follows on Android).
     * NOTE: ignores DetachEvent — sufficient for Hello (never detaches); do
     * not copy unaudited into tests for components that detach handlers. */
    private fun latestHandlerId(frames: List<RenderFrame>, eventName: String): Int =
        frames.asReversed().firstNotNullOfOrNull { frame ->
            frame.patches.filterIsInstance<RenderPatch.AttachEvent>()
                .lastOrNull { it.eventName == eventName }?.handlerId
        } ?: throw AssertionError("no AttachEvent('$eventName') patch in any captured frame")

    // ── rc 0: the round-trip ─────────────────────────────────────────────────

    @Test
    fun click_dispatch_rerenders_with_incremented_counter() {
        val (runtime, frames) = bootHello()
        val handlerId = latestHandlerId(frames, "click")
        assertTrue(handlerId > 0, "runtime-assigned handlerId must be positive; got $handlerId")
        val framesBefore = frames.size

        // Tap 1: handler runs + re-render + frame delivery all complete
        // before this returns (synchronous dispatch contract).
        assertEquals(0, runtime.dispatchEventBlocking(handlerId, "click"))
        assertTrue(frames.size > framesBefore, "re-render frame must arrive synchronously inside dispatch")
        println(
            "[DispatchEventTest] tap 1 (handlerId=$handlerId) re-render texts: " +
                frames.last().patches.filterIsInstance<RenderPatch.ReplaceText>().map { it.text }
        )
        assertTrue(
            frames.last().patches.filterIsInstance<RenderPatch.ReplaceText>()
                .any { it.text.contains("taps: 1") },
            "re-render frame must carry the incremented counter; got ${frames.last().patches}"
        )

        // Tap 2: harvest the handlerId from the LATEST frame — Blazor may
        // have re-issued it during the re-render.
        val freshHandlerId = latestHandlerId(frames, "click")
        assertEquals(0, runtime.dispatchEventBlocking(freshHandlerId, "click"))
        println(
            "[DispatchEventTest] tap 2 (handlerId=$freshHandlerId) re-render texts: " +
                frames.last().patches.filterIsInstance<RenderPatch.ReplaceText>().map { it.text }
        )
        assertTrue(
            frames.last().patches.filterIsInstance<RenderPatch.ReplaceText>()
                .any { it.text.contains("taps: 2") },
            "second dispatch must increment again; got ${frames.last().patches}"
        )
    }

    // ── payload marshaling across the ABI ────────────────────────────────────

    @Test
    fun payload_carrying_dispatch_crosses_abi() {
        // HONESTY NOTE: Hello has no change handler, so no JVM test here can
        // prove end-to-end ChangeEventArgs delivery — the .NET-side Gate 1
        // test (Dispatch_Change_BuildsChangeEventArgs) already proved that
        // in-process. What THIS test proves, DETERMINISTICALLY, is that
        // payload-carrying args cross the ABI intact and parse: a "click"
        // dispatch WITH a payload against the real click handler must behave
        // exactly like a payload-less click — rc 0 and the counter
        // increments (the payload is parsed by the export, then unused by
        // Hello's no-arg handler). A corrupted/unparsed payload would
        // surface as rc 3 instead.
        val (runtime, frames) = bootHello()
        val handlerId = latestHandlerId(frames, "click")

        val rc = runtime.dispatchEventBlocking(handlerId, "click", payload = "héllo → 世界")

        println("[DispatchEventTest] click-with-payload → rc $rc")
        assertEquals(0, rc, "payload-carrying click args must parse and dispatch")
        assertTrue(
            frames.last().patches.filterIsInstance<RenderPatch.ReplaceText>()
                .any { it.text.contains("taps: 1") },
            "the handler must have run despite the (ignored) payload; got ${frames.last().patches}"
        )
    }

    // ── the PRODUCTION path: async lane (THREADING CONTRACT) ─────────────────

    @Test
    fun async_dispatchEvent_rerenders_on_the_dispatch_lane() {
        // dispatchEvent (not the blocking test seam): the event is queued on
        // the BlazorNative-Dispatch lane, so the re-render frame must arrive
        // asynchronously ON that lane thread — pinning the threading contract
        // production shells rely on (UI listeners never enter the ABI
        // directly).
        val latch = CountDownLatch(1)
        val rerenderThread = AtomicReference<String>()
        val frames = Collections.synchronizedList(mutableListOf<RenderFrame>())
        val errors = Collections.synchronizedList(mutableListOf<String>())
        val runtime = BlazorNativeRuntime(
            onFrame = { f ->
                frames.add(f)
                if (f.patches.filterIsInstance<RenderPatch.ReplaceText>()
                        .any { it.text.contains("taps: 1") }
                ) {
                    rerenderThread.set(Thread.currentThread().name)
                    latch.countDown()
                }
            },
            onError = { msg, t -> errors.add("$msg: $t") },
        )
        runtime.start(platformOs = "test-host")
        val handlerId = latestHandlerId(frames.toList(), "click")

        runtime.dispatchEvent(handlerId, "click")

        assertTrue(
            latch.await(5, TimeUnit.SECONDS),
            "re-render frame did not arrive within 5 s via the async lane; errors=$errors"
        )
        println("[DispatchEventTest] async re-render frame arrived on thread '${rerenderThread.get()}'")
        assertEquals(
            "BlazorNative-Dispatch", rerenderThread.get(),
            "the re-render frame must be delivered on the dispatch lane (THREADING CONTRACT)"
        )
        assertTrue(errors.isEmpty(), "async dispatch must not route to onError; got $errors")
        assertTrue(runtime.retire(), "the lane must drain after the dispatch completed")
    }

    // ── onError message contract (frozen rc-2 wording) ───────────────────────

    @Test
    fun describeDispatchFailure_rc2_carries_frozen_wording() {
        // The rc-2 message contract from the plan: the frozen Gate 1 wording
        // + the desktop-JVM reproduction hint (Android stderr is /dev/null).
        val runtime = BlazorNativeRuntime(onFrame = {})

        val msg = runtime.describeDispatchFailure(2, handlerId = 42, eventName = "click")

        assertTrue(
            msg.contains("dispatch faulted — the handler, the resulting re-render, or frame delivery threw"),
            "rc-2 message must carry the frozen Gate 1 wording; got: $msg"
        )
        assertTrue(
            msg.contains("detail on native stderr — reproduce on desktop JVM to see it"),
            "rc-2 message must carry the desktop-JVM reproduction hint; got: $msg"
        )
    }

    // ── rc 0: stale handler (at-most-once contract) ──────────────────────────

    @Test
    fun bogus_handler_returns_0_stale_contract() {
        // A handlerId that was never issued is indistinguishable from one that
        // died in a re-render: the renderer catches Blazor's ArgumentException
        // and logs — a stale tap is NOT an error (at-most-once delivery).
        val (runtime, _) = bootHello()

        assertEquals(0, runtime.dispatchEventBlocking(999_999, "click"))
    }

    // ── rc 3: handlerId beyond the handler table's int range ─────────────────

    @Test
    fun huge_handler_returns_3() {
        // Unreachable through BlazorNativeRuntime's Int-typed API (deliberate
        // narrowing) — call the binding directly to pin the ABI contract:
        // silent (int) truncation could alias onto a LIVE handler, so the
        // export rejects handlerId > int.MaxValue as malformed input.
        bootHello() // session mounted, so rc 3 is unambiguously "bad input", not rc 1

        val args = "{\"name\":\"click\"}".toByteArray(Charsets.UTF_8) + 0
        val rc = NativeBindings.INSTANCE.blazornative_dispatch_event(Int.MAX_VALUE.toLong() + 1, args)

        assertEquals(3, rc)
    }
}
