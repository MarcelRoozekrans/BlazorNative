package io.blazornative.jni

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.util.Collections

/**
 * Phase 18.4 (#511), final review C1: a retired runtime drops a host event instead of throwing.
 *
 * Since 18.4 a new boot retires the previous runtime unconditionally, so an older Activity that is
 * still alive can call dispatchHostEvent on a retired lane, from onResume, onPause, onDestroy, the
 * safe-area report or the back press, all on the main thread. A RejectedExecutionException there is
 * uncaught and ends the process. [BlazorNativeRuntime.dispatchEvent] already dropped on a retired
 * lane; this pins the same for the fire-and-forget host-event entry.
 *
 * Anchor: `retire()` returned true, so the lane really is shut down and the dispatches below meet
 * a retired lane, not a live one.
 *
 * DOES NOT COVER: the blocking entries dispatchEventAndWait and dispatchHostEventAndWait, which
 * keep throwing RejectedExecutionException to their caller by design (no production shell code
 * calls them). Nor the device twin, two live Activities and a resume of the first. iOS is not
 * affected: it boots one BnRuntime and never retires it while its view controller lives.
 */
class RetiredLaneDispatchTest {

    @Test
    fun a_host_event_dispatched_on_a_retired_lane_is_dropped_without_throwing() {
        val errors = Collections.synchronizedList(mutableListOf<String>())
        val runtime = BlazorNativeRuntime(onFrame = {}, onError = { msg, _ -> errors.add(msg) })
        assertTrue(runtime.retire(), "anchor: retire() did not drain the lane, so the lane may still be live")

        for (event in listOf(BnHostEvent.OnResume, BnHostEvent.OnPause, BnHostEvent.OnDestroy, BnHostEvent.Back)) {
            try {
                runtime.dispatchHostEvent(event)
            } catch (e: java.util.concurrent.RejectedExecutionException) {
                throw AssertionError("dispatchHostEvent(${event.wireName}) on a retired runtime threw " +
                    "RejectedExecutionException: a live older Activity would crash the process", e)
            }
        }
        assertEquals(emptyList<String>(), errors.toList(),
            "a dropped host event on a retired lane must not be reported as an error")
    }
}
