// ─────────────────────────────────────────────────────────────────────────────
// BnModalMapperTests — Phase 7.4 Gate 3: **the anchor + overlay model, at the
// mechanism** (design decision 1: the 6.2 synthetic-node machinery, pointed at
// the root). The iOS twin of Android's WidgetMapperModalTest, plus the two
// node-type twins of WidgetMapperNodeTypesTest's 7.4 rows and the two arms that
// are iOS's ALONE: the tap-recognizer `click` for plain views with its
// touch-view filter (design decision 4 — Android's setOnClickListener is
// already generic), and the NESTED-modal fixpoint made EXPLICIT (the Gate 2
// review's finding: the fixpoint exists for exactly that shape and no test on
// either shell constructed it — and iOS is where the miss is a dangling
// `YGNodeRef` crash, not a leak).
//
// Synthetic frames against a [BnSyntheticHost] (the detached-root discipline:
// these pin STRUCTURE and CREATION SHAPE — the design says full-root numbers
// are a DEVICE assertion, and `BnModalDemoTests` is where the real host view's
// own bounds are read at assert time; the host here has definite 400 × 800
// bounds, so the centering arithmetic is still a number).
//
// The model under test, stated once (the design's words):
//  - the ANCHOR — a 0-sized `position:absolute` view at the modal's WIRE slot,
//    out of the flex flow entirely. THE THIRD INDEX-MAPPING RULE (normative):
//    it occupies the modal's slot in its wire parent, in the view tree AND the
//    Yoga tree, so sibling insert indices never skew; the modal's wire child
//    at index i is the OVERLAY's child at index i.
//  - the OVERLAY — full-root, attached LAST at the host root, never
//    re-ordered; children redirect into it; the scrim paints on it; the
//    dismissal-request `click` listens on it.
//  - `RemoveNode` purges **BOTH subtrees** (NOT the scroll shape: the overlay
//    is not a descendant of the anchor — the `modalOverlays` entry names it),
//    as a FIXPOINT, freeing every Yoga node EXACTLY ONCE.
//
// The RemoveNode shapes here are the PURE one-remove shape, deliberately:
// these trees have inline-only content, and the unit-pinned .NET shape for
// that is exactly one RemoveNodePatch. The COMPOSITE shape — /modal's FIVE
// removes (the four nested components' disposal removes riding ahead of the
// modal's own, the 7.2 removes-first order) — is the real renderer's, and
// `BnModalDemoTests` asserts the purge under it.
//
// ── THE HAND-ROLLED DISPATCH, SAID ONCE FOR THE WHOLE SUITE ──────────────────
// A hosted XCTest cannot synthesize a `UITouch` (the 7.3 finding, recorded in
// BnFormDemoTests' header), and a `UITapGestureRecognizer` only transitions
// state through real touch delivery — so the recognizer tests fire the
// recognized tap's EFFECT by hand (`BnClickTapRecognizer.bnFire()`, the
// `sendActions` analog) and pin the FILTER — the one thing a hand-rolled fire
// bypasses — as the pure decision it is ([bnClickTouchIsOwn]'s truth table),
// exactly the `bnIsLiveImageRequest` shape: the delegate callback is one line
// that asks a unit-tested function.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit
@testable import BnHost

// The prop/attach/detach builders the shared file does not have yet — local,
// the bnCreate/bnStyle shape.
private func bnProp(_ nodeId: Int32, _ name: String, _ value: String?) -> BnPatch {
    .updateProp(nodeId: nodeId, name: name, value: value)
}

private func bnAttach(_ nodeId: Int32, _ eventName: String, _ handlerId: Int32) -> BnPatch {
    .attachEvent(nodeId: nodeId, eventName: eventName, handlerId: handlerId)
}

private func bnDetach(_ nodeId: Int32, _ eventName: String, _ handlerId: Int32) -> BnPatch {
    .detachEvent(nodeId: nodeId, handlerId: handlerId, eventName: eventName)
}

/// The click recognizer installed on [view], or nil — found by TYPE, the
/// BnScrollContentView discipline (a view may carry framework recognizers).
private func bnClickRecognizer(on view: UIView) -> BnClickTapRecognizer? {
    view.gestureRecognizers?.compactMap { $0 as? BnClickTapRecognizer }.first
}

final class BnModalMapperTests: BnHostTestCase {

    // The lifecycle pins' vocabulary (the WidgetMapperModalTest `Counts` twin).
    private struct Counts {
        let nodes: Int
        let yogaNodes: Int
        let yogaViews: Int
        let overlays: Int
        let yogaOverlays: Int
    }

    private func counts(_ host: BnSyntheticHost) -> Counts {
        Counts(nodes: host.mapper.nodeCount,
               yogaNodes: host.mapper.yogaNodeCount,
               yogaViews: host.mapper.yogaViewCount,
               overlays: host.mapper.modalOverlayCount,
               yogaOverlays: host.mapper.yogaOverlayNodeCount)
    }

    // ── The anchor: the third index-mapping rule ─────────────────────────────

    func testTheAnchorIsZeroFootprintAndTheSiblingsHoldTheirExactFrames() {
        let host = BnSyntheticHost()
        // The demo's shape in miniature: a column with two declared-size boxes.
        host.render([
            bnCreate(1, "view", nil),
            bnCreate(10, "view", 1), bnStyle(10, "width", "220"), bnStyle(10, "height", "48"),
            bnCreate(11, "view", 1), bnStyle(11, "width", "220"), bnStyle(11, "height", "48"),
        ])
        let page = host.root.subviews[0]
        assertFrame("box A before the show", page.subviews[0], 0, 0, 220, 48)
        assertFrame("box B before the show", page.subviews[1], 0, 48, 220, 48)

        // THE SHOW: the modal lands BETWEEN the boxes (insertIndex 1 — the
        // anchor's slot), with its declared-size content box inside.
        host.render([
            bnCreate(2, "modal", 1, insertIndex: 1),
            bnCreate(20, "view", 2),
            bnStyle(20, "width", "280"), bnStyle(20, "height", "180"),
        ])

        XCTAssertEqual(page.subviews.count, 3, "the anchor OCCUPIES the wire slot — three children now")
        let anchor = page.subviews[1]
        XCTAssertTrue(anchor is BnModalAnchorView,
                      "the wire slot holds the ANCHOR, found by TYPE (got \(type(of: anchor)))")
        XCTAssertEqual(anchor.frame.width, 0, "the anchor is 0 wide")
        XCTAssertEqual(anchor.frame.height, 0, "the anchor is 0 tall")
        XCTAssertTrue(anchor.subviews.isEmpty,
                      "the anchor hosts NOTHING — the modal's wire children redirect to the overlay")
        // THE PIN: the siblings hold the EXACT frames they held with the modal
        // closed — the anchor is absolute and 0-sized, so it contributes
        // nothing to any sibling's frame. Break the anchor's fixed styles and
        // box B moves down by the anchor's height.
        assertFrame("box A with the modal open", page.subviews[0], 0, 0, 220, 48)
        assertFrame("box B with the modal open — the zero-footprint rule",
                    page.subviews[2], 0, 48, 220, 48)
    }

    // ── The overlay: last at the root, full-root, children redirected ────────

    func testModalChildrenRedirectIntoTheOverlayAndTheContentBoxCenters() {
        let host = BnSyntheticHost() // 400 × 800 pt
        host.render([
            bnCreate(1, "view", nil),
            bnCreate(2, "modal", 1),
            bnCreate(20, "view", 2), bnStyle(20, "width", "280"), bnStyle(20, "height", "180"),
        ])
        XCTAssertEqual(host.root.subviews.count, 2, "the host root holds the page AND the overlay")
        let overlay = host.root.subviews[1]
        XCTAssertTrue(overlay is BnModalOverlayView,
                      "the root's LAST subview is the OVERLAY, found by TYPE (got \(type(of: overlay)))")
        assertFrame("the overlay is the ROOT's own bounds (100%/100% against the host — "
                    + "the scrim IS the root)", overlay, 0, 0, 400, 800)
        XCTAssertEqual(overlay.subviews.count, 1,
                       "the modal's wire child parents into the OVERLAY (the third "
                       + "index-mapping rule's second half)")
        // The design's frame arithmetic: (W − 280)/2 × (H − 180)/2 — the
        // centering is the SHELL's (the wire carries no layout for it).
        assertFrame("the content box centers against the root",
                    overlay.subviews[0], (400 - 280) / 2, (800 - 180) / 2, 280, 180)
    }

    func testATopLevelWireAppendLandsBeforeTheLiveOverlays() {
        let host = BnSyntheticHost()
        host.render([
            bnCreate(1, "view", nil), bnStyle(1, "height", "40"),
            bnCreate(2, "modal", nil), // a TOP-LEVEL modal: anchor at root, overlay after it
        ])
        // A top-level wire APPEND while the overlay is live: it must slot in
        // AHEAD of the overlay — "the overlay is LAST, always" — or the new
        // node draws over the scrim and the root's 1:1 index arithmetic skews.
        host.render([
            bnCreate(3, "view", nil), bnStyle(3, "height", "80"),
        ])
        XCTAssertEqual(host.root.subviews.count, 4)
        // [0] view 1, [1] anchor 2, [2] view 3 — the append, BEFORE the overlay —
        // [3] the overlay, still last.
        assertFrame("the appended top-level node flows after view 1 in BOTH trees",
                    host.root.subviews[2], 0, 40, 400, 80)
        XCTAssertTrue(host.root.subviews[3] is BnModalOverlayView)
        assertFrame("the overlay is STILL the last child, still full-root",
                    host.root.subviews[3], 0, 0, 400, 800)
    }

    func testAReshownModalIsARecreateAndLandsOnTop() {
        let host = BnSyntheticHost()
        host.render([
            bnCreate(1, "view", nil),
            bnCreate(2, "modal", 1),                    // modal A
            bnCreate(20, "view", 2), bnStyle(20, "height", "100"),
            bnCreate(3, "modal", 1, insertIndex: 1),    // modal B — created after A: on top
            bnCreate(30, "view", 3), bnStyle(30, "height", "50"),
        ])
        XCTAssertEqual(host.root.subviews.count, 3, "page + two overlays, creation order")
        // B's overlay is last (its content is the 50-tall box).
        let overlayOfB = host.root.subviews[2]
        XCTAssertEqual(overlayOfB.subviews[0].frame.height, 50, accuracy: 0.5)

        // Hide A (unmount — one RemoveNodePatch, the inline-content shape) and
        // RE-SHOW it: a re-created modal (fresh wire id — hide is unmount, the
        // decision-2 posture) lands ON TOP of B. Stacking is creation order;
        // the shell never re-orders.
        host.render([.removeNode(nodeId: 2)])
        host.render([
            bnCreate(4, "modal", 1, insertIndex: 1),
            bnCreate(40, "view", 4), bnStyle(40, "height", "100"),
        ])
        XCTAssertEqual(host.root.subviews.count, 3)
        XCTAssertTrue(host.root.subviews[1] === overlayOfB, "B's overlay kept its place")
        let top = host.root.subviews[2]
        XCTAssertEqual(top.subviews[0].frame.height, 100, accuracy: 0.5,
                       "the RE-SHOWN modal's overlay is on top (re-show = re-create = attached last)")
    }

    // ── The scrim: paint by PROP, dismissal by click-on-scrim ────────────────

    func testScrimColorIsAPropAndPaintsTheOverlay() {
        let host = BnSyntheticHost()
        host.render([
            bnCreate(1, "view", nil),
            bnCreate(2, "modal", 1),
            bnProp(2, "scrimColor", "#80000000"), // the component default, off the wire
        ])
        let overlay = host.root.subviews[1]
        XCTAssertEqual(overlay.backgroundColor, BnColor.parse("#80000000"),
                       "the scrim paints the OVERLAY — the one paintable thing the author owns "
                       + "on a modal node, and it rides the PROP wire by design")
        // Garbage is logged and ignored (the backgroundColor arm's posture) —
        // the scrim keeps its paint.
        host.render([bnProp(2, "scrimColor", "not-a-color")])
        XCTAssertEqual(overlay.backgroundColor, BnColor.parse("#80000000"))
    }

    func testTheDismissalClickListensOnTheScrimNeverTheAnchor() throws {
        var dispatches: [String] = []
        let host = BnSyntheticHost()
        host.mapper.onUiEvent = { handlerId, event, _ in dispatches.append("\(handlerId):\(event)") }
        host.render([
            bnCreate(1, "view", nil),
            bnCreate(2, "modal", 1),
            bnCreate(20, "view", 2), bnStyle(20, "width", "280"), bnStyle(20, "height", "180"),
            bnAttach(2, "click", 77),   // the modal's dismissal wire
            bnAttach(20, "click", 88),  // the content-box swallow (a PLAIN view — the new arm)
        ])
        let page = host.root.subviews[0]
        let overlay = host.root.subviews[1]
        let anchor = page.subviews[0]
        let box = overlay.subviews[0]

        // A scrim tap dispatches the modal's click — the dismissal REQUEST.
        // (bnFire is the hand-rolled dispatch — see the file header.)
        let scrimRecognizer = try XCTUnwrap(bnClickRecognizer(on: overlay),
                                            "the OVERLAY holds the click recognizer")
        scrimRecognizer.bnFire()
        XCTAssertEqual(dispatches, ["77:click"])

        // A content-box tap dispatches the BOX's own attach (the swallow — a
        // real dispatch the counters account for), never the modal's: the
        // touch-view filter keeps a descendant's touch from ever reaching the
        // scrim's recognizer (decision 4's iOS half — [bnClickTouchIsOwn]).
        let boxRecognizer = try XCTUnwrap(bnClickRecognizer(on: box),
                                          "the content box holds its own (swallow) recognizer")
        boxRecognizer.bnFire()
        XCTAssertEqual(dispatches, ["77:click", "88:click"])

        // The anchor holds NO recognizer — the wire's view is the scrim.
        XCTAssertNil(bnClickRecognizer(on: anchor), "the anchor must not be clickable")
        XCTAssertEqual(host.mapper.clickDispatchesSent, 2,
                       "every click above rode the counted dispatch path")

        // Detach kills the recognizer with the wire (the symmetric-arms rule).
        host.render([bnDetach(2, "click", 77)])
        XCTAssertNil(bnClickRecognizer(on: overlay), "DetachEvent removed the scrim's recognizer")
    }

    /// **THE TOUCH-VIEW FILTER, pinned as the pure decision it is** (design
    /// decision 4's normative rule — the file header says why the truth table
    /// stands in for a `UITouch` no hosted test can construct). The wiring half
    /// is asserted alongside: the recognizer IS its own delegate on the live
    /// scrim, so the filter is in the touch path, not just in a function.
    func testTheTouchViewFilterIsTheScrimTapRule() throws {
        // The truth table: ONLY the attached view's own touch passes.
        let overlay = UIView()
        let descendant = UIView()
        overlay.addSubview(descendant)
        XCTAssertTrue(bnClickTouchIsOwn(touchView: overlay, attachedView: overlay),
                      "a touch on the scrim itself dismiss-requests")
        XCTAssertFalse(bnClickTouchIsOwn(touchView: descendant, attachedView: overlay),
                       "a touch on a DESCENDANT (the content box; a control inside it) "
                       + "never fires the scrim's recognizer — the normative rule")
        XCTAssertFalse(bnClickTouchIsOwn(touchView: nil, attachedView: overlay),
                       "a view-less touch is nobody's")
        XCTAssertFalse(bnClickTouchIsOwn(touchView: overlay, attachedView: nil),
                       "a detached recognizer (UIKit clears .view at removal) declines everything")

        // The wiring: the live scrim's recognizer routes shouldReceive through
        // exactly this function (it is its own delegate — drop the delegate and
        // the recognizer fires for EVERY descendant touch, including the
        // in-modal switch's).
        let host = BnSyntheticHost()
        host.render([
            bnCreate(1, "view", nil),
            bnCreate(2, "modal", 1),
            bnAttach(2, "click", 77),
        ])
        let scrim = host.root.subviews[1]
        let recognizer = try XCTUnwrap(bnClickRecognizer(on: scrim))
        XCTAssertTrue(recognizer.delegate === recognizer,
                      "the recognizer is its own delegate — the filter is IN the touch path")
        XCTAssertTrue(recognizer.view === scrim, "…attached to the scrim itself")
    }

    /// The generic plain-view arm (decision 4's `Pressable` down payment):
    /// attach dispatches, re-attach is last-wins (ONE recognizer, the new
    /// handler), detach removes it — and the UIButton arm still rides
    /// target-action, now counted.
    func testClickOnAPlainViewAttachesLastWinsAndDetaches() throws {
        var dispatches: [String] = []
        let host = BnSyntheticHost()
        host.mapper.onUiEvent = { handlerId, event, _ in dispatches.append("\(handlerId):\(event)") }
        host.render([
            bnCreate(1, "view", nil),
            bnCreate(10, "view", 1), bnStyle(10, "width", "100"), bnStyle(10, "height", "40"),
            bnCreate(11, "button", 1),
            bnAttach(10, "click", 55),
            bnAttach(11, "click", 66),
        ])
        let page = host.root.subviews[0]
        let plain = page.subviews[0]
        let button = try XCTUnwrap(page.subviews[1] as? UIButton)

        try XCTUnwrap(bnClickRecognizer(on: plain)).bnFire()
        XCTAssertEqual(dispatches, ["55:click"], "a plain view's click rides the recognizer arm")

        // Last-wins re-attach (same node, new handlerId, no preceding detach):
        // ONE recognizer, the new handler — no stacked dispatches.
        host.render([bnAttach(10, "click", 57)])
        XCTAssertEqual(plain.gestureRecognizers?.compactMap { $0 as? BnClickTapRecognizer }.count, 1,
                       "re-attach REPLACES the recognizer, never stacks a second")
        try XCTUnwrap(bnClickRecognizer(on: plain)).bnFire()
        XCTAssertEqual(dispatches, ["55:click", "57:click"])

        // The UIButton arm is unchanged — target-action, no recognizer — and it
        // rides the SAME counted dispatch path.
        XCTAssertNil(bnClickRecognizer(on: button), "a UIButton's click is target-action, not a recognizer")
        button.sendActions(for: .touchUpInside)
        XCTAssertEqual(dispatches, ["55:click", "57:click", "66:click"])
        XCTAssertEqual(host.mapper.clickDispatchesSent, 3)

        // Detach removes the recognizer from the view.
        host.render([bnDetach(10, "click", 57)])
        XCTAssertNil(bnClickRecognizer(on: plain))
    }

    // ── The style-ignore rule (decision 1, live code) ────────────────────────

    func testSetStyleOnAModalNodeIsDiagnosedAndIgnored() {
        let host = BnSyntheticHost()
        host.render([
            bnCreate(1, "view", nil),
            bnCreate(2, "modal", 1),
            bnProp(2, "scrimColor", "#80000000"),
            bnCreate(20, "view", 2), bnStyle(20, "width", "280"), bnStyle(20, "height", "180"),
        ])
        // The hand-rolled-wire hatch (the .NET side pins it open): a LAYOUT name
        // and a VISUAL name, both against the modal node. Every style would land
        // on the anchor or the overlay, neither of which the author owns.
        host.render([
            bnStyle(2, "width", "100"),
            bnStyle(2, "backgroundColor", "#FF0000"),
        ])
        let page = host.root.subviews[0]
        let overlay = host.root.subviews[1]
        XCTAssertEqual(page.subviews[0].frame.width, 0,
                       "width did NOT size the anchor back into the flex flow")
        assertFrame("the overlay kept its shell-fixed full-root frame", overlay, 0, 0, 400, 800)
        XCTAssertEqual(overlay.backgroundColor, BnColor.parse("#80000000"),
                       "backgroundColor did NOT repaint the scrim (the scrim's paint is the "
                       + "scrimColor PROP)")
        let diags = host.mapper.scrollDiagnostics.filter { $0.contains("`modal` node") }
        XCTAssertEqual(diags.count, 2,
                       "both drops are RECORDED — NSLog is not an assertion surface, and the "
                       + "failure this rule prevents is silent on every frame table")
        XCTAssertTrue(diags[0].contains("width") && diags[1].contains("backgroundColor"))
    }

    // ── The two-subtree purge (the overlay-count lifecycle pin) ──────────────

    func testRemoveNodeOnTheModalPurgesBothSubtrees() {
        let host = BnSyntheticHost()
        host.render([bnCreate(1, "view", nil)])
        let baseline = counts(host)

        host.render([
            bnCreate(2, "modal", 1),
            bnProp(2, "scrimColor", "#80000000"),
            bnAttach(2, "click", 77),
            bnCreate(20, "view", 2), bnStyle(20, "width", "280"), bnStyle(20, "height", "180"),
            bnCreate(21, "text", 20), bnText(21, "inside the overlay"),
        ])
        let mounted = counts(host)
        XCTAssertEqual(mounted.nodes, baseline.nodes + 3,
                       "the modal mounts THREE wire nodes (anchor id + box + text)")
        XCTAssertEqual(mounted.overlays, 1, "ONE live overlay")
        XCTAssertEqual(mounted.yogaOverlays, 1, "…in the Yoga tree too — both trees or neither")

        // ONE RemoveNodePatch (inline-only content — the unit-pinned pure shape;
        // /modal's composite five-remove shape is BnModalDemoTests'). It stands
        // for the ANCHOR's subtree AND the OVERLAY's — the overlay is not a
        // descendant of the anchor, and the design names the difference.
        host.render([.removeNode(nodeId: 2)])

        let after = counts(host)
        XCTAssertEqual(after.nodes, baseline.nodes, "the wire nodes are gone")
        XCTAssertEqual(after.overlays, 0,
                       "THE PIN: the overlay count is back to 0. Purge only the anchor's subtree "
                       + "and the overlay, the content box and everything under it leak once per "
                       + "dismissal — and HERE the leaked handle is a dangling YGNodeRef the next "
                       + "calculateAndApply dereferences.")
        XCTAssertEqual(after.yogaOverlays, 0, "…and the Yoga overlay node with it")
        XCTAssertEqual(after.yogaViews, baseline.yogaViews,
                       "…and its view mappings (what pins the detached views)")
        XCTAssertEqual(after.yogaNodes, baseline.yogaNodes, "…and the Yoga wire nodes")
        XCTAssertEqual(host.root.subviews.count, 1, "the overlay view is DETACHED from the host root")
    }

    func testRemovingAWrapperPurgesTheModalAndItsOverlayTransitively() {
        let host = BnSyntheticHost()
        let baseline = counts(host)

        // THE PATCH NAVIGATION ACTUALLY EMITS: away from /modal with the modal
        // OPEN, the RemoveNode names the PAGE root — nothing names the modal,
        // and nothing could ever name the overlay. The anchor is found
        // transitively; the fixpoint is what takes the overlay with it.
        host.render([
            bnCreate(1, "view", nil),
            bnCreate(10, "view", 1), bnStyle(10, "height", "48"),
            bnCreate(2, "modal", 1, insertIndex: 1),
            bnCreate(20, "view", 2), bnStyle(20, "width", "280"), bnStyle(20, "height", "180"),
        ])
        XCTAssertEqual(counts(host).overlays, 1)

        host.render([.removeNode(nodeId: 1)])

        let after = counts(host)
        XCTAssertEqual(after.nodes, baseline.nodes)
        XCTAssertEqual(after.overlays, 0,
                       "THE PIN: the overlay is freed by a patch that names the modal's "
                       + "GRANDPARENT — the path every navigation-away takes")
        XCTAssertEqual(after.yogaOverlays, 0)
        XCTAssertEqual(after.yogaViews, baseline.yogaViews)
        XCTAssertEqual(after.yogaNodes, baseline.yogaNodes)
        XCTAssertEqual(host.root.subviews.count, 0, "nothing is left under the host root")
    }

    /// **THE NESTED MODAL, EXPLICIT** (the Gate 2 review's finding: the fixpoint
    /// exists for exactly this shape — a modal whose wire parent lives inside
    /// another modal's overlay — and no test on either shell constructed it;
    /// this platform is where the miss is a crash). RemoveNode(A) dooms A's
    /// anchor → the fixpoint dooms A's overlay subtree → which contains B's
    /// ANCHOR → the fixpoint's second pass dooms B's overlay. Every Yoga node
    /// freed EXACTLY ONCE: B's anchor node is freed by A's overlay-subtree free
    /// (it is a Yoga child of A's content box) and only EVICTED thereafter;
    /// B's overlay node hangs directly off the host root and gets its own free.
    /// The follow-up render proves no dangling ref survives — the next
    /// calculateAndApply walks the overlay map and dereferences every handle
    /// still in it.
    func testANestedModalIsPurgedByTheFixpointExactlyOnce() {
        let host = BnSyntheticHost()
        host.render([bnCreate(1, "view", nil)])
        let baseline = counts(host)

        host.render([
            bnCreate(2, "modal", 1),                                          // modal A
            bnProp(2, "scrimColor", "#80000000"),
            bnCreate(20, "view", 2), bnStyle(20, "width", "280"), bnStyle(20, "height", "180"),
            bnCreate(3, "modal", 20),                                         // modal B — INSIDE A's overlay
            bnProp(3, "scrimColor", "#40FF0000"),
            bnCreate(30, "view", 3), bnStyle(30, "width", "100"), bnStyle(30, "height", "80"),
        ])
        let mounted = counts(host)
        XCTAssertEqual(mounted.overlays, 2, "two live overlays — the nested shape is real")
        XCTAssertEqual(mounted.yogaOverlays, 2)
        XCTAssertEqual(host.root.subviews.count, 3, "page + overlay A + overlay B (creation order)")
        // B's ANCHOR sits inside A's overlay subtree (in A's content box) —
        // the precondition the fixpoint exists for.
        let overlayA = host.root.subviews[1]
        XCTAssertTrue(overlayA.subviews[0].subviews.contains { $0 is BnModalAnchorView },
                      "modal B's anchor lives INSIDE modal A's overlay subtree")

        // ONE remove, naming modal A only. Nothing names B; nothing could ever
        // name either overlay.
        host.render([.removeNode(nodeId: 2)])

        let after = counts(host)
        XCTAssertEqual(after.overlays, 0,
                       "THE PIN: BOTH overlays purged by the FIXPOINT — one pass finds A's, "
                       + "the second finds B's through A's doomed subtree")
        XCTAssertEqual(after.yogaOverlays, 0, "…in the Yoga interop too")
        XCTAssertEqual(after.nodes, baseline.nodes)
        XCTAssertEqual(after.yogaNodes, baseline.yogaNodes)
        XCTAssertEqual(after.yogaViews, baseline.yogaViews)
        XCTAssertEqual(host.root.subviews.count, 1, "only the page column remains at the root")

        // The no-dangling-ref proof: another frame's layout pass walks every
        // surviving handle. A half-purged overlay map dereferences freed memory
        // HERE — loudly under ASan, as a corrupt frame otherwise. (The page
        // column is EMPTY after the purge — the anchor was its only child — so
        // the new box lands at index 0.)
        host.render([bnCreate(4, "view", 1), bnStyle(4, "height", "20")])
        assertFrame("the tree still lays out after the nested purge",
                    host.root.subviews[0].subviews[0], 0, 0, 400, 20)
    }

    // ── The node-type twins (WidgetMapperNodeTypesTest's 7.4 rows) ───────────

    func testActivityIndicatorIsASpinningMeasuredLeafByOracle() throws {
        let host = BnSyntheticHost()
        host.render([
            bnCreate(1, "view", nil),
            bnCreate(10, "activityindicator", 1),
        ])
        let live = try XCTUnwrap(host.root.subviews[0].subviews[0] as? UIActivityIndicatorView,
                                 "`activityindicator` must decode and instantiate the platform spinner")
        XCTAssertTrue(live.isAnimating,
                      "SPINNING is the contract — a stopped UIActivityIndicatorView hides itself")

        // The measured-leaf law (6.1) by ORACLE (the 6.3 method; 7.3's lesson —
        // per-platform intrinsics, never cross-platform pixel claims): the live
        // height equals what a fresh spinner MEASURES, and the LAYOUT box
        // stretches to the column (alignItems: stretch) — asserted on the Yoga
        // frame, the 7.3 UISwitch discipline, because whether the widget accepts
        // the imposed width is the platform's own business.
        let layout = try XCTUnwrap(host.mapper.bnYogaFrame(of: live),
                                   "the indicator must have a Yoga node")
        XCTAssertEqual(layout.width, 400, accuracy: 0.5,
                       "the un-styled leaf's LAYOUT box stretches to the column width")
        let oracle = UIActivityIndicatorView(style: .medium)
        let fit = oracle.sizeThatFits(CGSize(width: layout.width, height: .greatestFiniteMagnitude))
        XCTAssertGreaterThan(fit.height, 0)
        XCTAssertEqual(layout.height, fit.height, accuracy: 1,
                       "the indicator's height is what the platform's OWN spinner measures — "
                       + "a fabricated measure func passes every relational assertion and fails this")
    }

    func testModalCreatesTheAnchorPlusOverlayPairByType() {
        let host = BnSyntheticHost()
        host.render([
            bnCreate(1, "view", nil),
            bnCreate(2, "modal", 1),
        ])
        // The pair, found by TYPE in both places it lives: the wire slot holds
        // the anchor; the root's tail holds the overlay; the bookkeeping sees
        // exactly one of each, in BOTH trees (the counts are the lifecycle
        // pins' vocabulary — created in the same breath here, evicted in the
        // same breath by the purge tests above).
        XCTAssertTrue(host.root.subviews[0].subviews[0] is BnModalAnchorView,
                      "the wire slot holds the ANCHOR")
        XCTAssertTrue(host.root.subviews[1] is BnModalOverlayView,
                      "the host root's last child is the OVERLAY")
        XCTAssertEqual(host.mapper.modalOverlayCount, 1)
        XCTAssertEqual(host.mapper.yogaOverlayNodeCount, 1)
        // The anchor is 0×0 even though the modal has no children yet — the
        // shell-fixed styles are the CREATE's, not the first child's.
        XCTAssertEqual(host.root.subviews[0].subviews[0].frame, .zero)
    }
}
