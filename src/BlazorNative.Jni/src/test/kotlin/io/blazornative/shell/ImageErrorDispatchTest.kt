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
    @DisplayName("THE DEFERRED TURN RE-ASKS THE DECISION: decision-time DEFER, generation bumped behind it, fire-time DROP")
    fun the_deferred_turn_re_asks_the_decision_with_fire_time_facts() {
        // Decision time — the failure terminates INSIDE the batch, live and attached: DEFER.
        assertEquals(
            ImageErrorDispatchAction.DEFER,
            imageErrorDispatchAction(
                currentGeneration = 1, requestGeneration = 1,
                currentView = liveView, requestView = liveView,
                handlerAttached = true, applyingBatch = true,
            ),
            "mid-batch, live, attached: the decision is DEFER — but a verdict is not a " +
                "dispatch ticket. What the posted turn may carry is the REQUEST's facts " +
                "(nodeId, generation, view, url), never the verdict itself.",
        )
        // Fire time — a later patch in the SAME batch wrote src again (generation 1 → 2);
        // the posted turn runs after the batch closed and re-asks with LIVE facts: DROP.
        assertEquals(
            ImageErrorDispatchAction.DROP,
            imageErrorDispatchAction(
                currentGeneration = 2, requestGeneration = 1,
                currentView = liveView, requestView = liveView,
                handlerAttached = true, applyingBatch = false,
            ),
            "GATE 2 REVIEW I-1, the row: a DEFER arm that replays a decision-time capture " +
                "would deliver the OLD src's error into live user code here (and a " +
                "RemoveNode behind the failure would dispatch for a PURGED node). The " +
                "deferred turn must RE-ENTER imageErrorDispatchAction with fire-time state " +
                "(WidgetMapper.decideAndDispatchError posts ITSELF — the iOS shape, where " +
                "URL(string:) → nil proves this frame live). Latent on Android only " +
                "because Coil cannot fail synchronously mid-batch; normative regardless.",
        )
    }

    @Test
    @DisplayName("an UNBOUND handler never dispatches — attach-iff-HasDelegate, the shell half")
    fun an_unbound_handler_never_dispatches() {
        assertEquals(
            ImageErrorDispatchAction.DROP,
            imageErrorDispatchAction(
                currentGeneration = 1, requestGeneration = 1,
                currentView = liveView, requestView = liveView,
                handlerAttached = false, applyingBatch = false,
            ),
            "no attach means no wire: an unbound OnError is ZERO wire presence (the " +
                "BnScroll shape, pinned .NET-side), so there is no handlerId to ride and the " +
                "failure stays what it always was — a logged, painted-nothing 404. Asked " +
                "OUTSIDE a batch, where 'unbound' is a settled fact — the mid-batch shape is " +
                "the mount-order test's, and it DEFERS.",
        )
    }

    @Test
    @DisplayName("GATE 3 REVIEW I-1: mid-batch the handler question is NOT SETTLED — DEFER, and let fire time answer it")
    fun a_mid_batch_failure_with_no_handler_yet_DEFERS_and_fire_time_decides() {
        // Decision time — the mount frame's own ordering: `src` (seq 24) precedes
        // `attachEvent "error"` (seq 27) in ONE batch, so iOS's synchronous nil-URL
        // failure asks "handler attached?" three patches early. Mid-batch that answer
        // is a race, not a fact: DEFER. The old table asked handlerAttached FIRST and
        // answered DROP — permanently — so OnError never fired for
        // <BnImage Src="<unparseable>" OnError="..."> at mount on iOS, while this
        // shell (whose failure lands post-batch) dispatched: a parity break the
        // design's "an unparseable non-empty URL is a failure and DISPATCHES" forbids.
        assertEquals(
            ImageErrorDispatchAction.DEFER,
            imageErrorDispatchAction(
                currentGeneration = 1, requestGeneration = 1,
                currentView = liveView, requestView = liveView,
                handlerAttached = false, applyingBatch = true,
            ),
            "mid-batch + no handler YET is DEFER, not DROP: the attach may be three " +
                "patches behind the failure in the SAME batch (the mount order), and a " +
                "verdict reached against mid-batch state is a verdict reached against a " +
                "race. Only the liveness DROP may be permanent inside a batch — identity " +
                "and generation never un-supersede.",
        )
        // Fire time — the batch closed and the attach landed (the mount case): the
        // re-entered decision reads the SETTLED handler state and dispatches, once.
        assertEquals(
            ImageErrorDispatchAction.DISPATCH_NOW,
            imageErrorDispatchAction(
                currentGeneration = 1, requestGeneration = 1,
                currentView = liveView, requestView = liveView,
                handlerAttached = true, applyingBatch = false,
            ),
            "fire time, attach landed by batch end: DISPATCH_NOW — the deferred turn " +
                "re-enters imageErrorDispatchAction (decideAndDispatchError posts ITSELF) " +
                "and the settled state says the wire exists. This is the mount-time " +
                "bad-URL dispatch, exactly once, post-batch — both shells.",
        )
        // Fire time — no attach ever landed: the unbound rule holds where it is true.
        assertEquals(
            ImageErrorDispatchAction.DROP,
            imageErrorDispatchAction(
                currentGeneration = 1, requestGeneration = 1,
                currentView = liveView, requestView = liveView,
                handlerAttached = false, applyingBatch = false,
            ),
            "fire time, still unbound: DROP — deferring an unbound mid-batch failure " +
                "does not manufacture a dispatch; it only moves the handler question to " +
                "the one moment it can be answered truthfully.",
        )
    }
}
