// ─────────────────────────────────────────────────────────────────────────────
// BnImageRequestGuard — Phase 6.3 Gate 3: **THE PURGED-NODE GUARD, AS A PURE
// DECISION.** The Swift twin of Kotlin's `ImageRequestGuard.kt`, function for
// function, and it exists for the same reason: so it can be tested WITHOUT A
// DEVICE and without a mount.
//
// `BnWidgetMapper` asks this at **BOTH** of the places the decision is made — before
// an image completion is allowed to PAINT ([BnWidgetMapper.isLive]) and before any
// terminal callback is allowed to EVICT an in-flight entry
// ([BnWidgetMapper.clearIfMine]): *may the request that just terminated touch the node
// it was issued for?* The answer is the CONJUNCTION of two facts, and the whole point
// of this file is that **neither one alone is sufficient**:
//
// **ONE decision, TWO call sites, ONE unit test.** `clearIfMine` used to re-implement
// the conjunction inline, so this file — and `BnImageGuardTests` — defended only the
// painting path: dropping `&& entry.view === view` from `clearIfMine` ALONE left all 70
// tests green. A mutation that must be applied in two places to redden one test is a
// mutation whose second site is unpinned. Both sites now route through here.
//
//   - **GENERATION** — has the node's `src` been written again since this request
//     was issued? Every `src` write bumps the node's generation, so a superseded
//     request carries an old one and must not paint stale bytes over fresh ones.
//   - **IDENTITY** — is the view this callback captured still the view that node
//     id names? **Node ids restart at 1 after a reset** (the renderer's, and 6.2's
//     warn-once lesson learned on the diagnostics keys). `/image` → back → `/image`
//     re-uses the same mapper and hands out the same ids, so a stale callback
//     carrying **generation 1** can meet a **brand-new node that is also on
//     generation 1**. THE GENERATIONS MATCH. Only identity separates them.
//
// And on iOS the consequence of getting it wrong is not cosmetic, it is MEMORY
// SAFETY (6.3 non-negotiable #4). A generation-only guard lets the stale callback
// evict the LIVE request's `DownloadTask` from the in-flight map — leaving a
// request that nothing can cancel. `handleRemove` then frees the node's
// `YGNodeRef` (raw native memory: `bn_yoga_node_free_subtree`), the un-cancelled
// request completes, and its completion marks a **freed `YGNodeRef`** dirty. That
// is 6.2's dangling-pointer lesson wearing the fix's clothes.
//
// **THE RESET COLLISION CANNOT BE STAGED ON A DEVICE** by any demo page (each
// mounts once), which is precisely why this is a top-level function taking
// `AnyObject?` rather than a method taking `BnImageView` — `BnImageGuardTests`
// writes the four states down with two bare `NSObject`s and no UIKit tree at all.
// Deleting the identity half used to leave the whole XCTest suite green; it does
// not any more.
// ─────────────────────────────────────────────────────────────────────────────

import Foundation

/// - Parameters:
///   - currentGeneration: the node's LIVE generation (`nil` = the node has none —
///     it was purged, or never carried a `src`).
///   - requestGeneration: the generation the terminating request was issued under.
///   - currentView: the view the node id names TODAY (`nil` = the node is gone).
///   - requestView: the view the terminating request captured.
func bnIsLiveImageRequest(currentGeneration: Int?,
                          requestGeneration: Int,
                          currentView: AnyObject?,
                          requestView: AnyObject?) -> Bool {
    // `===` and not `==`: this is a question about OBJECT IDENTITY. Two distinct
    // UIImageViews for the same node id are exactly the case this guard exists for.
    return currentGeneration == requestGeneration && currentView === requestView
}
