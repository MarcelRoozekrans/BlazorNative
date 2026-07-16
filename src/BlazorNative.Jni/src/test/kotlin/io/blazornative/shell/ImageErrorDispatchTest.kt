package io.blazornative.shell

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.DisplayName
import org.junit.jupiter.api.Test

/**
 * Phase 7.5 — **THE ERROR-DISPATCH DECISION, PINNED ON THE JVM LANE.**
 *
 * The two rules no Android device test can reliably stage (the function's KDoc has the
 * whole argument):
 *
 *  - **the liveness gate** composes [isLiveImageRequest] — the reset collision (same id,
 *    same generation, DIFFERENT view) cannot be staged by any single-mount instrumented
 *    test, which is the reason that function lives in the shared source set at all;
 *  - **the defer rule** — Coil has no deterministic synchronous-FAILURE path (its memory
 *    cache proves synchronous SUCCESS; a 404 is never cached), so "a dispatch may never run
 *    inside a batch" is assertable on Android ONLY as this table. iOS stages it live
 *    (`URL(string:) → nil` is synchronous by construction) — same table, two shells.
 *
 * Two distinct `Any()` instances stand in for the two `ImageView`s, exactly as in
 * [ImageRequestGuardTest] — the guard compares by reference, and taking `Any?` is what lets
 * this run without a device.
 */
@DisplayName("the image error dispatch: live + attached, deferred out of a batch, dropped never silently wrong")
class ImageErrorDispatchTest {

    private val liveView = Any()
    private val staleView = Any()

    @Test
    @DisplayName("live + attached + no batch: DISPATCH NOW — the ordinary asynchronous failure")
    fun live_attached_outside_a_batch_dispatches_now() {
        assertEquals(
            ImageErrorDispatchAction.DISPATCH_NOW,
            imageErrorDispatchAction(
                currentGeneration = 1, requestGeneration = 1,
                currentView = liveView, requestView = liveView,
                handlerAttached = true, applyingBatch = false,
            ),
            "a live request's failure, a bound handler, no batch in flight: the dispatch " +
                "goes out on this very main-thread turn — deferring it here would be latency " +
                "for nothing",
        )
    }

    @Test
    @DisplayName("live + attached + INSIDE A BATCH: DEFER — deferred, NEVER dropped")
    fun a_failure_inside_a_batch_is_DEFERRED_never_dropped() {
        assertEquals(
            ImageErrorDispatchAction.DEFER,
            imageErrorDispatchAction(
                currentGeneration = 1, requestGeneration = 1,
                currentView = liveView, requestView = liveView,
                handlerAttached = true, applyingBatch = true,
            ),
            "THE RULE THE PHASE'S RISK TABLE NAMES: a terminal callback CAN complete " +
                "synchronously inside UpdateProp(\"src\") (Dispatchers.Main.immediate — 6.3's " +
                "warm-cache finding), and a dispatch from inside applyBatch is re-entrant " +
                "dispatch under a non-re-entrant guard. It DEFERS to a fresh main-queue turn. " +
                "DISPATCH_NOW here is the re-entrancy bug; DROP here is a swallowed event that " +
                "never happened — unlike the re-solve, nothing downstream subsumes an event.",
        )
    }

    @Test
    @DisplayName("a SUPERSEDED generation never dispatches — a stale error is as stale as stale bytes")
    fun a_superseded_generation_never_dispatches() {
        assertEquals(
            ImageErrorDispatchAction.DROP,
            imageErrorDispatchAction(
                currentGeneration = 2, requestGeneration = 1,
                currentView = liveView, requestView = liveView,
                handlerAttached = true, applyingBatch = false,
            ),
            "the node's src was written again while this request was in flight: its error " +
                "describes a source the author has already replaced. It dispatches nothing, " +
                "exactly as it paints nothing — one guard, two consumers.",
        )
    }

    @Test
    @DisplayName("a PURGED node never dispatches — the handler it would enter belongs to nobody")
    fun a_purged_node_never_dispatches() {
        assertEquals(
            ImageErrorDispatchAction.DROP,
            imageErrorDispatchAction(
                currentGeneration = null, requestGeneration = 1,
                currentView = null, requestView = staleView,
                handlerAttached = true, applyingBatch = false,
            ),
            "node removal cancels, and a callback that outran the cancel finds no node: " +
                "nothing may dispatch on behalf of a node that no longer exists (its id is " +
                "one reset away from naming someone else).",
        )
    }

    @Test
    @DisplayName("THE RESET COLLISION never dispatches: same id, same generation 1, DIFFERENT view")
    fun the_reset_collision_never_dispatches() {
        assertEquals(
            ImageErrorDispatchAction.DROP,
            imageErrorDispatchAction(
                currentGeneration = 1, requestGeneration = 1,
                currentView = liveView, requestView = staleView,
                handlerAttached = true, applyingBatch = false,
            ),
            "/imagepolish → back → /imagepolish: node ids restart at 1, so the OLD node's " +
                "late failure meets a BRAND-NEW node on the same generation — and would " +
                "dispatch a stale error into the RECYCLED node's handler (the risk table's " +
                "third row). Only identity separates them; the dispatch asks it because " +
                "isLiveImageRequest is composed by name, not re-implemented.",
        )
    }

    @Test
    @DisplayName("an UNBOUND handler never dispatches — attach-iff-HasDelegate, the shell half")
    fun an_unbound_handler_never_dispatches() {
        val why = "no attach means no wire: an unbound OnError is ZERO wire presence (the " +
            "BnScroll shape, pinned .NET-side), so there is no handlerId to ride and the " +
            "failure stays what it always was — a logged, painted-nothing 404."
        assertEquals(
            ImageErrorDispatchAction.DROP,
            imageErrorDispatchAction(
                currentGeneration = 1, requestGeneration = 1,
                currentView = liveView, requestView = liveView,
                handlerAttached = false, applyingBatch = false,
            ),
            why,
        )
        assertEquals(
            ImageErrorDispatchAction.DROP,
            imageErrorDispatchAction(
                currentGeneration = 1, requestGeneration = 1,
                currentView = liveView, requestView = liveView,
                handlerAttached = false, applyingBatch = true,
            ),
            "$why (…and being inside a batch does not turn an unbound failure into a " +
                "deferred one: DEFER is for dispatches that WILL happen)",
        )
    }
}
