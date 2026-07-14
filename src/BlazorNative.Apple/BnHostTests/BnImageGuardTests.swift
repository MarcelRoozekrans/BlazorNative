// ─────────────────────────────────────────────────────────────────────────────
// BnImageGuardTests — Phase 6.3 Gate 3: **THE PURGED-NODE GUARD, PINNED.** The iOS
// twin of Kotlin's `ImageRequestGuardTest` (JVM lane), state for state.
//
// `bnIsLiveImageRequest`'s doc comment calls it *"not belt-and-braces theatre"*, and
// on Android nothing checked that: **delete the identity half and the whole
// instrumented suite stayed green.** No test anywhere forced an image completion to
// race a `cancel()`, and the one case the identity check exists for — **a node id
// RE-USED after a reset** — cannot be staged on a device by any of the demo pages
// (each mounts once, and each id is handed out once).
//
// So the decision is a pure function and it is pinned HERE, where the four states can
// simply be written down. The fourth is the one that matters:
//
//   **`/image` → back → `/image`.** One mapper; **node ids restart at 1**. The OLD
//   node 2's callback carries **generation 1** and meets a BRAND-NEW node 2 that is
//   ALSO on **generation 1**. THE GENERATIONS MATCH. If identity is not asked, the
//   stale callback is judged LIVE — it paints into the wrong node, and (in
//   `BnWidgetMapper.clearIfMine`'s case) it evicts the LIVE request's `DownloadTask`,
//   leaving a request nothing can cancel. `handleRemove` then frees that node's
//   `YGNodeRef`, the un-cancelled request completes, and its `markDirty` writes
//   through a **dangling pointer into freed native memory** — 6.3 non-negotiable #4,
//   arrived at through the guard that was supposed to prevent it.
//
// Two distinct `NSObject`s stand in for the two `BnImageView`s: the function compares
// by REFERENCE (`===`), which is the whole of the point, and it takes `AnyObject?`
// precisely so this test needs no UIKit tree, no mapper and no mount.
//
// **MUTATION EVIDENCE (measured on CI):** remove the identity half from
// `bnIsLiveImageRequest` AND from `BnWidgetMapper.clearIfMine` — i.e. ask the generation
// alone — and `testTheRESETCollisionIsNotLiveEvenThoughTheGenerationsMatch` is **the only
// test in the whole 70-case suite that goes red for it.** Not one device test, not the
// double mount, not the lifecycle tests. That is not an argument for the unit test's
// convenience; it is the argument for its EXISTENCE.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
@testable import BnHost

final class BnImageGuardTests: XCTestCase {

    private let liveView = NSObject()
    private let staleView = NSObject()

    func testSameGenerationAndSameViewIsLive() {
        XCTAssertTrue(
            bnIsLiveImageRequest(currentGeneration: 1, requestGeneration: 1,
                                 currentView: liveView, requestView: liveView),
            "THE ORDINARY COMPLETION: the node's src has not been written since this request was "
            + "issued, and the view the callback captured is still the view the node id names. It "
            + "may paint.")
    }

    func testASupersededGenerationIsNotLive() {
        XCTAssertFalse(
            bnIsLiveImageRequest(currentGeneration: 2, requestGeneration: 1,
                                 currentView: liveView, requestView: liveView),
            "A `Src` CHANGE HAPPENED: the node's src was written again (generation 1 → 2) while "
            + "this request was in flight. Its completion would paint STALE bytes over the fresh "
            + "ones — and it races its own cancel(), so `cancelImageRequest` alone cannot prevent "
            + "it.")
    }

    func testAPurgedNodeIsNotLive() {
        XCTAssertFalse(
            bnIsLiveImageRequest(currentGeneration: nil, requestGeneration: 1,
                                 currentView: nil, requestView: staleView),
            "THE PURGED NODE: it was removed (navigation purges the SUBTREE). A completion here "
            + "would paint into a detached UIImageView — harmless — and then markDirty a "
            + "YGNodeRef that `bn_yoga_node_free_subtree` has already FREED. That is why "
            + "cancellation is memory safety and not hygiene (non-negotiable #4).")
    }

    func testTheRESETCollisionIsNotLiveEvenThoughTheGenerationsMatch() {
        XCTAssertFalse(
            bnIsLiveImageRequest(currentGeneration: 1,   // the BRAND-NEW node 2, freshly mounted
                                 requestGeneration: 1,   // the OLD node 2's in-flight request
                                 currentView: liveView,  // …but the views are DIFFERENT instances
                                 requestView: staleView),
            "THE CASE NEITHER HALF COVERS ALONE, and the reason identity exists. /image → back → "
            + "/image re-uses ONE mapper and NODE IDS RESTART AT 1, so a stale callback carrying "
            + "generation 1 meets a brand-new node that is ALSO on generation 1. THE GENERATIONS "
            + "MATCH. Ask only the generation and this stale completion is judged LIVE: it paints "
            + "into the new node, and clearIfMine evicts the NEW request's DownloadTask — leaving "
            + "a live request that nothing can cancel, whose completion runs against a freed "
            + "YGNodeRef.")
    }
}
