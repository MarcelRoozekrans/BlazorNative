package io.blazornative.shell

import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.DisplayName
import org.junit.jupiter.api.Test

/**
 * Phase 6.3 Gate 2 review (I1) — **THE PURGED-NODE GUARD, PINNED.**
 *
 * [isLiveImageRequest]'s KDoc called it *"not belt-and-braces theatre"*, and nothing checked
 * that. **Delete the identity half and the whole instrumented suite stayed 107/0**: no test
 * anywhere forced an image completion to race a `dispose()`, and the one case the identity
 * check exists for — a node id RE-USED after a reset — cannot be staged on a device by any of
 * the demo pages (each mounts once).
 *
 * So the decision was extracted into a pure function and it is pinned HERE, on the JVM lane,
 * where the four states can simply be written down. The fourth is the one that matters, and it
 * is the same reasoning `WidgetMapper.clearIfMine` (Gate 2 review, C1) depends on:
 *
 *   **`/image` → back → `/image`.** One mapper; node ids restart at 1. The OLD node 2's
 *   callback carries **generation 1** and meets a BRAND-NEW node 2 that is ALSO on
 *   **generation 1**. The generations MATCH. If identity is not asked, the stale callback is
 *   judged LIVE — it paints into the wrong node, and (in `clearIfMine`'s case) it evicts the
 *   live request's `Disposable`, leaving a request nothing can cancel. On iOS that
 *   un-cancelled completion runs against a **freed `YGNodeRef`**.
 *
 * Two distinct `Any()` instances stand in for the two `ImageView`s: the function compares by
 * REFERENCE (`===`), which is the whole of the point, and it takes `Any?` precisely so this
 * test needs no device.
 */
@DisplayName("the image request guard: generation AND identity, and neither alone")
class ImageRequestGuardTest {

    private val liveView = Any()
    private val staleView = Any()

    @Test
    @DisplayName("same generation + same view: LIVE — this is the ordinary completion")
    fun same_generation_and_same_view_is_live() {
        assertTrue(
            isLiveImageRequest(
                currentGeneration = 1,
                requestGeneration = 1,
                currentView = liveView,
                requestView = liveView,
            ),
            "the node's src has not been written since this request was issued, and the view " +
                "the callback captured is still the view the node id names. It may paint.",
        )
    }

    @Test
    @DisplayName("SUPERSEDED generation: a `Src` change happened — the old bytes must NOT paint")
    fun a_superseded_generation_is_not_live() {
        assertFalse(
            isLiveImageRequest(
                currentGeneration = 2,
                requestGeneration = 1,
                currentView = liveView,
                requestView = liveView,
            ),
            "the node's src was written again (generation 1 → 2) while this request was in " +
                "flight. Its completion would paint STALE bytes over the fresh ones — and it " +
                "races its own dispose(), so `cancelImageRequest` alone cannot prevent it.",
        )
    }

    @Test
    @DisplayName("PURGED node: no generation, no view — the completion has nothing to paint into")
    fun a_purged_node_is_not_live() {
        assertFalse(
            isLiveImageRequest(
                currentGeneration = null,
                requestGeneration = 1,
                currentView = null,
                requestView = staleView,
            ),
            "the node was removed (navigation purges the SUBTREE). A completion here would paint " +
                "into a detached ImageView on Android — and touch a FREED YGNodeRef on iOS, which " +
                "is why cancellation is memory safety rather than hygiene (non-negotiable #4).",
        )
    }

    @Test
    @DisplayName("THE RESET COLLISION: same id, same generation 1, DIFFERENT view — NOT live")
    fun the_reset_collision_is_not_live_even_though_the_generations_match() {
        assertFalse(
            isLiveImageRequest(
                currentGeneration = 1,   // the BRAND-NEW node 2, freshly mounted
                requestGeneration = 1,   // the OLD node 2's in-flight request
                currentView = liveView,  // …but the views are DIFFERENT instances
                requestView = staleView,
            ),
            "THE CASE NEITHER HALF COVERS ALONE, and the reason identity exists. /image → back → " +
                "/image re-uses one mapper and NODE IDS RESTART AT 1 (6.2's warn-once lesson), so " +
                "a stale callback carrying generation 1 meets a brand-new node that is also on " +
                "generation 1. THE GENERATIONS MATCH. Ask only the generation and this stale " +
                "completion is judged LIVE: it paints into the new node, and WidgetMapper." +
                "clearIfMine would evict the NEW request's Disposable — leaving a live request " +
                "that nothing can cancel, whose completion (on iOS) runs against a freed YGNodeRef.",
        )
    }
}
