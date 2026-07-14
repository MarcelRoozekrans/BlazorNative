package io.blazornative.shell

/**
 * Phase 6.3 Gate 2 review (I1) — **THE PURGED-NODE GUARD, AS A PURE DECISION.**
 *
 * `WidgetMapper` asks this before an image completion is allowed to paint: *may the request
 * that just terminated touch the node it was issued for?* The answer is the conjunction of
 * two facts, and the whole point of this file is that **neither one alone is sufficient** —
 * a claim the shell used to make in a KDoc and pin nowhere.
 *
 *  - **GENERATION** — has the node's `src` been written again since this request was issued?
 *    Every `src` write bumps the node's generation, so a superseded request carries an old
 *    one and must not paint stale bytes over fresh ones.
 *  - **IDENTITY** — is the view this callback captured still the view that node id names?
 *    **Node ids restart at 1 after a reset** (6.2's warn-once lesson, learned on the
 *    diagnostics keys). `/image` → back → `/image` re-uses the same mapper and hands out the
 *    same ids, so a stale callback carrying **generation 1** can meet a **brand-new node that
 *    is also on generation 1**. The generations MATCH. Only identity separates them, and the
 *    consequence of getting it wrong is not cosmetic: the stale callback evicts the live
 *    request's `Disposable`, the live request becomes un-cancellable, and on iOS its
 *    completion runs against a **freed `YGNodeRef`**.
 *
 * It is a top-level pure function, with no Android types in its signature, for exactly one
 * reason: **so it can be unit-tested off the device** (`ImageRequestGuardTest`, JVM lane) —
 * including the reset collision, which no single-mount instrumented test can stage. Deleting
 * the identity half used to leave the whole suite green; it does not any more.
 *
 * Gate 3 owes the same conjunction in Swift, and owes it for the same reason.
 *
 * @param currentGeneration the node's LIVE generation (`null` = the node has none — it was
 *   purged, or never carried a `src`).
 * @param requestGeneration the generation the terminating request was issued under.
 * @param currentView the view the node id names TODAY (`null` = the node is gone).
 * @param requestView the view the terminating request captured.
 */
internal fun isLiveImageRequest(
    currentGeneration: Int?,
    requestGeneration: Int,
    currentView: Any?,
    requestView: Any?,
): Boolean = currentGeneration == requestGeneration && currentView === requestView
