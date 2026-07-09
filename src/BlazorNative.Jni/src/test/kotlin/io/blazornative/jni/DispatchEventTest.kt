package io.blazornative.jni

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/**
 * Phase 3.2 Gate 2 — blazornative_dispatch_event proven on the desktop JVM
 * against the win-x64 NativeAOT dll, through [BlazorNativeRuntime]'s dispatch
 * seam ([BlazorNativeRuntime.dispatchEventBlocking] — the tests' calling
 * thread IS the dispatch-discipline thread, so the inline variant is the
 * honest one to exercise; the async lane adds only an executor hop).
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
     * attach wins (the same rule WidgetMapper's re-attach follows on Android). */
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
    fun change_dispatch_payload_crosses_abi() {
        // HONESTY NOTE: Hello has no change handler, so this test cannot
        // prove end-to-end ChangeEventArgs delivery — the .NET-side Gate 1
        // test (Dispatch_Change_BuildsChangeEventArgs) already proved payload
        // marshaling in-process. What THIS test proves is that a payload-
        // carrying args object crosses the ABI intact: dispatching "change"
        // against the CLICK handler's id either faults on the handler-args
        // type mismatch (rc 2) or dispatches benignly (rc 0) — either way the
        // args JSON PARSED, i.e. anything but rc 3.
        val (runtime, frames) = bootHello()
        val handlerId = latestHandlerId(frames, "click")

        val rc = runtime.dispatchEventBlocking(handlerId, "change", payload = "héllo → 世界")

        println("[DispatchEventTest] change-with-payload against the click handler → rc $rc")
        assertTrue(
            rc == 0 || rc == 2,
            "payload-carrying change args must parse across the ABI (rc 0 or 2, never 3); got rc $rc"
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
