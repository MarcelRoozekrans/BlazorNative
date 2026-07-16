// ─────────────────────────────────────────────────────────────────────────────
// BnScrollTests — Phase 6.2 Gate 3 — **THE SYNTHETIC CONTENT NODE, IN THE iOS
// SHELL.** The twin of `WidgetMapperScrollTest` (the instrumented suite), test for
// test and number for number.
//
// ```
//   WIRE                    VIEW / YOGA
//   scroll                  UIScrollView           ← the VIEWPORT (definite height)
//    ├─ row 0                └─ content view       ← SYNTHETIC. Never on the wire.
//    ├─ row 1                     ├─ row 0
//    └─ …                         ├─ row 1
//                                 └─ …
// ```
//
// That is the **second index-mapping rule** in this shell, after 6.1's text-collapse
// invariant, and it fails the same way: silently, as a skew. `insertIndex == -1` means
// "append to the CONTENT node's children" — never to the scroll node's, whose only
// child *is* the content node.
//
// ── WHY THE INDEX TESTS MATTER MORE HERE THAN ON ANDROID ─────────────────────
//
// Android's `ScrollView` **throws** on a second direct child and its Yoga twin
// **throws** on an out-of-range insert index. iOS does neither: `UIScrollView` accepts
// any number of subviews, and `bn_yoga_node_insert_child` **CLAMPS** (the recorded 6.1
// decision — it clamps BOTH trees identically, so they cannot skew against each other,
// and trapping inside a render callback aborts the app with no diagnostic at all). So
// **every mistake that fails LOUDLY on Android fails SILENTLY here**, and the four
// index tests are the only thing that would catch it.
//
// The content node gets `height: auto`, `width: 100%`, `flexDirection: column`, and
// **never `flexShrink`** — Yoga's default 0 is the entire mechanism by which it keeps
// its 800 against a 200-high viewport (non-negotiable #6).
//
// And the two scroll-node **diagnostics**, both warn-once, both asserted here rather
// than left to NSLog: container styles are ignored-and-logged, and an auto-height
// scroll node — which takes its height FROM its content and therefore cannot scroll —
// gets one warning, because "the page just doesn't move" is otherwise baffling.
//
// ── WHAT IS *NOT* HERE, DELIBERATELY (design §"ANDROID-SPECIFIC") ────────────
//
// No `onMeasure` UNSPECIFIED fallback, and no `isFillViewport`. `UIScrollView` neither
// re-measures its content view nor re-clamps `contentOffset` on layout, and there is no
// iOS knob for the second — both are Android mechanisms with no iOS equivalent, and
// going looking for one is a wasted gate. **What iOS owes INSTEAD** is
// [testShrinkingTheContentClampsAScrolledOffset]: `UIScrollView` does not move
// `contentOffset` when `contentSize` SHRINKS, so a page scrolled to 600 whose content
// later drops to 300 is left scrolled past its own end. Android's per-layout re-clamp
// handles that for free; iOS must handle it itself.
//
// …and the clamp iOS owes brings a HAZARD Android does not have, which is the other
// half of the same asymmetry: `UIScrollView.bounces` moves `contentOffset` OUT OF RANGE
// on purpose while the user's finger is down (Android's overscroll is a GLOW — the
// offset never moves), so an unconditional clamp fights the rubber band mid-drag. The
// clamp is therefore gated on `!scroll.isTracking`, and its ARITHMETIC is extracted and
// tabled ([testTheOffsetClampIsAClampAndItsFloorIsNotDecoration]) precisely because the
// gate itself cannot be unit-tested: an untestable guard over TESTED arithmetic.
//
// ── MUTATION EVIDENCE (measured on CI, not asserted from an armchair) ────────
//
// **`flexShrink: 1` on the content node** (the CSS instinct — the `.mm`'s one absent
// line): 8 tests red, 41 green, and **THE ROW FRAMES ARE NOT AMONG THEM.** The content
// node collapses 800 → 200, and every row keeps its 80 and its y = 80i — in this file
// AND in `BnScrollDemoTests`, whose entire frame table (rows, nested flex row, the
// Grow=1 box's 200, the back button, the root column hugging its sections) **stays
// green**. The ONLY corrupted numbers are `contentSize` (200, not 800), the scroll
// range it implies (0, not 600), and — the visible symptom — the rows no longer move
// when the offset is driven. Exactly what Android's mutation run found: a screenshot of
// the first 200pt looks perfect and 600pt of content can never be reached. (It also
// makes the definite-height diagnostic cry wolf on a healthy flex-sized viewport, which
// is a nice second signal and not one anybody predicted.)
//
// **Dropping the container-style ignore rule** (`handleSetStyle`): exactly
// [testContainerStylesOnAScrollNodeAreIgnoredAndLogged] goes red — the six diagnostics
// are gone, and `padding: 16` alone moves the content node to x = 16 and squeezes it to
// 268 wide, taking every row with it.
//
// **Dropping the synthetic content node's purge** (`handleRemove`): exactly the two
// `BnYogaLifecycleTests` scroll tests go red (47 green / 2 red), on the counts —
// `yogaContentNodeCount` and `scrollContentCount` stay at 1 after the node that owned
// them is freed. It does NOT crash, and the reason is worth writing down: the stale
// entry IS a handle into memory `YGNodeFreeRecursive` has already released, and the only
// thing standing between it and a dereference is `applyScrollFrames`' `guard let scroll
// = views[scrollId]` — the view is gone, so the loop skips it. A guard that reads like
// bookkeeping was holding up memory safety; it now `assertionFailure`s and SAYS SO, so
// the desync is loud at the moment it happens rather than two counts later.
//
// ── …AND THE GATE 3 REVIEW'S TWO (same lane, same shape) ─────────────────────
//
// **Dropping the clamp's `max(0, …)` floor** (`clampedOffset`): **48 green / 2 red** —
// [testShrinkingTheContentClampsAScrolledOffset] (act three) and
// [testTheOffsetClampIsAClampAndItsFloorIsNotDecoration]. And the simulator settles the
// argument the floor is *about*: with the maximum negative, the shell assigns −120 and
// `UIScrollView` **KEEPS IT** — `contentOffset.y` reads back **−120.0** at rest, a page
// scrolled 120pt above its own first row. Before this review the shrink test only went
// 800 → 240 against a 200 viewport (a POSITIVE maximum of 40), so **the floor could be
// deleted with the whole suite green.**
//
// **Deleting `contentInsetAdjustmentBehavior = .never`** (`makeView`): **49 green / 1
// red** — only [testAScrollNodeIsAUIScrollViewOverASyntheticContentView]'s new property
// assertion. That 49 IS the finding: every frame, every contentSize, every row, the whole
// demo table — all still green, because the safe-area inset a detached test host folds in
// is ZERO. The knob's own comment says the tests cannot see its effect, and until that
// assertion existed the line could be deleted with a green suite while real devices
// diverged from Android by a device constant. It is asserted on the PROPERTY because
// there is no number to assert it on.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit
@testable import BnHost

final class BnScrollTests: BnHostTestCase {

    private let rows = 10
    private let rowH: CGFloat = 80
    private let viewW: CGFloat = 300
    private let viewH: CGFloat = 200
    /// 10 × 80 = 800 — DERIVED, not transcribed (BnScrollDemo.ContentHeightDp is
    /// derived the same way, and a changed row height must move both sides at once).
    private var contentH: CGFloat { CGFloat(rows) * rowH }
    /// 800 − 200 = 600.
    private var scrollRange: CGFloat { contentH - viewH }

    // ── Trees ────────────────────────────────────────────────────────────────

    /// The demo's shape: a 300×200 viewport over ten 80-high rows. [scrollStyles] are
    /// extra SetStyle patches on the SCROLL node (the container-style test's hatch); a
    /// nil [viewportHeight] leaves the scroll node auto-height.
    private func scrollTree(viewportHeight: CGFloat? = 200,
                            scrollStyles: [BnPatch] = []) -> [BnPatch] {
        var patches: [BnPatch] = [
            bnCreate(1, "scroll", nil),
            bnStyle(1, "width", "\(Int(viewW))"),
        ]
        if let h = viewportHeight { patches.append(bnStyle(1, "height", "\(Int(h))")) }
        patches += scrollStyles
        for i in 0..<rows {
            patches.append(bnCreate(Int32(10 + i), "view", 1))
            patches.append(bnStyle(Int32(10 + i), "height", "\(Int(rowH))"))
        }
        return patches
    }

    /// A scroll inside a parent with a DEFINITE height, taking its own height **from
    /// flex rather than from a declared `height`** — so it sails past the
    /// definite-height diagnostic's first condition and is judged only on the second.
    ///
    /// [growOnly] picks between the two shapes, and the difference is the whole of
    /// [testAGrowOnlyScrollNodeIsNotBoundedAndIsWarnedAbout]:
    ///
    ///  - `growOnly: false` — `Grow="1"` **plus `Basis="0"`** (CSS's `flex: 1`). THE
    ///    SHAPE THAT WORKS: basis 0 → free space is `parentHeight − 0` (POSITIVE) →
    ///    grow gives the viewport exactly the parent's height.
    ///  - `growOnly: true` — `Grow="1"` alone, which is what every doc in this phase
    ///    recommended until the Gate 2 review and which **does not bound the viewport
    ///    at all** when the content is taller than the parent.
    private func grownScrollTree(parentHeight: CGFloat,
                                 contentHeight: CGFloat,
                                 growOnly: Bool = false) -> [BnPatch] {
        var patches: [BnPatch] = [
            bnCreate(1, "view", nil),
            bnStyle(1, "width", "\(Int(viewW))"),
            bnStyle(1, "height", "\(Int(parentHeight))"),
            bnCreate(2, "scroll", 1),
            bnStyle(2, "flexGrow", "1"),
        ]
        if !growOnly { patches.append(bnStyle(2, "flexBasis", "0")) }
        patches.append(bnCreate(10, "view", 2))
        patches.append(bnStyle(10, "height", "\(Int(contentHeight))"))
        return patches
    }

    // ── The model ────────────────────────────────────────────────────────────

    /// **THE PHASE, IN ONE ASSERTION.** The viewport is 300×200; the content view it
    /// wraps computes to 300×**800** — from Yoga, as the content node's frame, not from
    /// any shell-side union of child frames (non-negotiable #3). Ten rows, each 80 tall,
    /// at y = 80i, including the seven that sit entirely below the viewport's bottom
    /// edge. `contentSize > viewport` is the whole phase.
    func testAScrollNodeIsAUIScrollViewOverASyntheticContentView() throws {
        let host = bnRender(scrollTree())
        let scroll = try bnScrollView(host.root.subviews[0])
        let content = try bnContentView(of: scroll)

        assertFrame("the viewport", scroll, 0, 0, viewW, viewH)
        assertFrame("THE CONTENT SIZE — the synthetic content node's Yoga frame, 800 tall inside "
                    + "a 200-high viewport", content, 0, 0, viewW, contentH)

        XCTAssertEqual(scroll.contentSize.height, contentH, accuracy: 0.5,
                       "THE PHASE: contentSize comes FROM YOGA — the content node's computed "
                       + "height. Everything else here is bookkeeping")
        XCTAssertEqual(scroll.contentSize.width, viewW, accuracy: 0.5)
        XCTAssertGreaterThan(scroll.contentSize.height, scroll.bounds.height,
                             "…and it must EXCEED the viewport, or there is nothing to scroll")
        XCTAssertEqual(scroll.contentSize.height - scroll.bounds.height, scrollRange, accuracy: 0.5,
                       "…by exactly the scrollable range, 800 − 200")

        XCTAssertTrue(scroll.clipsToBounds,
                      "the VIEWPORT CLIPS — unlike every other container in this shell, which does "
                      + "NOT (Yoga's overflow default is `visible`, and Android's BnYogaFrameLayout "
                      + "sets clipChildren = false to match). A viewport that did not clip would "
                      + "draw all 800pt of content over the whole screen; this is the one place "
                      + "'our containers don't clip' is the WRONG rule to mirror")

        // **THE PARITY KNOB — ASSERTED ON THE PROPERTY, BECAUSE IT CANNOT BE ASSERTED ON A
        // NUMBER.** `.automatic` folds the safe-area inset into `adjustedContentInset`,
        // which shifts the resting `contentOffset` to −inset and moves the maximum offset
        // by a device constant Android's ScrollView has no notion of — so the two shells'
        // scroll ranges would differ, ON THE DEVICE ONLY. Its effect here is exactly ZERO
        // (a detached test host has no safe area), which is what makes this line the one
        // knob in the file whose own comment says "the tests cannot see this" — and, until
        // this assertion, the one knob you could DELETE with all 49 tests still green while
        // real devices diverged. There is no number to pin, so the PROPERTY is pinned.
        XCTAssertEqual(scroll.contentInsetAdjustmentBehavior, .never,
                       "contentInsetAdjustmentBehavior must be .never — a CROSS-SHELL PARITY knob. "
                       + "The default (.automatic) shifts the resting contentOffset to −safeArea and "
                       + "moves the maximum by the same amount; Android has no such notion, so the "
                       + "600pt range BnScrollDemoAndroidTest asserts would be a different number "
                       + "here. It is zero in this detached host — which is why nothing but this "
                       + "assertion can see it, and why the divergence would first show up on a "
                       + "device")

        XCTAssertEqual(content.subviews.count, rows, "all ten rows are children of the CONTENT view")
        for i in 0..<rows {
            assertFrame("row \(i) (no Width — stretched to the content node's 300 by Yoga's default "
                        + "alignItems:stretch, which is what proves the content node spans the "
                        + "viewport)",
                        content.subviews[i], 0, rowH * CGFloat(i), viewW, rowH)
        }
    }

    /// **NON-NEGOTIABLE #2, THE APPEND HALF.** `insertIndex == -1` means append to the
    /// **content** node's children. Add them to the `UIScrollView` instead and Android
    /// would throw on the second (a `ScrollView` holds exactly one child) — **iOS would
    /// not**: `UIScrollView` accepts any number of subviews, and the rows would simply be
    /// laid out by a Yoga node that has ten children where the content node has none. So
    /// the loud failure is Android's and the silent one is ours.
    func testWireChildrenParentIntoTheContentViewNeverIntoTheScrollView() throws {
        let host = bnRender(scrollTree())
        let scroll = try bnScrollView(host.root.subviews[0])
        let content = try bnContentView(of: scroll)

        XCTAssertEqual(content.subviews.count, rows)
        for i in 0..<rows {
            XCTAssertTrue(content.subviews[i].superview === content,
                          "row \(i)'s parent must be the CONTENT view")
        }
        // …and, from the other side: NONE of the rows is a direct subview of the
        // viewport. (Asserted as a set membership rather than as `subviews.count == 1`,
        // because UIScrollView keeps its own scroll-indicator subviews — see
        // [BnScrollContentView].)
        let contentRows = Set(content.subviews.map(ObjectIdentifier.init))
        for sub in scroll.subviews where sub !== content {
            XCTAssertFalse(contentRows.contains(ObjectIdentifier(sub)),
                           "a scroll node's wire children are NOT its view children")
        }
    }

    /// **NON-NEGOTIABLE #2, THE INDEXED HALF.** A scroll node's wire child at index *i*
    /// is the CONTENT node's child at index *i*. A shell that applied `insertIndex` to
    /// the scroll node's own children would be indexing a list whose only member is the
    /// content node — and on iOS an out-of-range index is CLAMPED, not thrown, so the
    /// row would silently land at the end. This asserts the FRAME: the late row must land
    /// FIRST, at y = 0, and push the two originals down.
    func testInsertIndexTargetsTheContentViewsChildren() throws {
        let host = BnSyntheticHost()
        host.render([
            bnCreate(1, "scroll", nil),
            bnStyle(1, "width", "300"), bnStyle(1, "height", "200"),
            bnCreate(10, "view", 1), bnStyle(10, "height", "80"),   // append
            bnCreate(11, "view", 1), bnStyle(11, "height", "80"),   // append
        ])
        // …and now a row at index 0 of the CONTENT node's children.
        host.render([bnCreate(12, "view", 1, insertIndex: 0), bnStyle(12, "height", "40")])

        let scroll = try bnScrollView(host.root.subviews[0])
        let content = try bnContentView(of: scroll)
        XCTAssertEqual(content.subviews.count, 3, "three rows, all under the content view")
        assertFrame("the row inserted at index 0 must be the CONTENT node's FIRST child",
                    content.subviews[0], 0, 0, 300, 40)
        assertFrame("…and the two originals must have moved down by its 40pt",
                    content.subviews[1], 0, 40, 300, 80)
        assertFrame("…both of them", content.subviews[2], 0, 120, 300, 80)
        XCTAssertEqual(scroll.contentSize.height, 200, accuracy: 0.5,
                       "the content node still computes to the sum: 40 + 80 + 80")
    }

    /// **NON-NEGOTIABLE #2, THE MID-LIST HALF — THE "SILENT SKEW" THE RULE IS NAMED
    /// AFTER.** Front (index 0) and back (append) are the two indices a wrong
    /// implementation is most likely to get right by accident. **Index 1 of 3 is the one
    /// that fails quietly**, and it is the one a keyed list re-order actually emits.
    ///
    /// Asserted as FRAMES, because that is the only place a skew is visible: the new
    /// 40-high row must land BETWEEN rows 10 and 11, and push the two below it down by
    /// exactly its 40.
    func testInsertIndexInTheMiddleOfAScrollNodesChildren() throws {
        let host = BnSyntheticHost()
        host.render([
            bnCreate(1, "scroll", nil),
            bnStyle(1, "width", "300"), bnStyle(1, "height", "200"),
            bnCreate(10, "view", 1), bnStyle(10, "height", "80"),
            bnCreate(11, "view", 1), bnStyle(11, "height", "80"),
            bnCreate(12, "view", 1), bnStyle(12, "height", "80"),
        ])
        host.render([bnCreate(13, "view", 1, insertIndex: 1), bnStyle(13, "height", "40")])

        let scroll = try bnScrollView(host.root.subviews[0])
        let content = try bnContentView(of: scroll)
        XCTAssertEqual(content.subviews.count, 4, "four rows, all under the content view")
        assertFrame("row 10 is untouched at index 0", content.subviews[0], 0, 0, 300, 80)
        assertFrame("THE NEW ROW is the CONTENT node's child at index 1, at y = 80",
                    content.subviews[1], 0, 80, 300, 40)
        assertFrame("…and the two below it moved down by exactly its 40pt",
                    content.subviews[2], 0, 120, 300, 80)
        assertFrame("…both of them", content.subviews[3], 0, 200, 300, 80)
        XCTAssertEqual(scroll.contentSize.height, 280, accuracy: 0.5,
                       "the content node computes to 80 + 40 + 80 + 80")
    }

    /// **NON-NEGOTIABLE #2, THE SYMMETRIC HALF.** The rule says the two trees mirror each
    /// other *"in BOTH trees, at the same index"* — and REMOVAL is the direction nothing
    /// asserted. A `RemoveNode` for a scroll node's CHILD must reach the child's Yoga node
    /// inside the CONTENT node, not just its view.
    ///
    /// The frame is what proves it: a Yoga node left behind keeps **reserving its 80pt**,
    /// so the surviving row below would stay at y = 160 instead of moving up into the
    /// hole. The view tree would look right and the layout would be silently wrong — the
    /// 6.1 "ghost node" failure, one level deeper.
    func testRemovingAScrollNodesChildRemovesItFromBothTrees() throws {
        let host = BnSyntheticHost()
        host.render([
            bnCreate(1, "scroll", nil),
            bnStyle(1, "width", "300"), bnStyle(1, "height", "200"),
            bnCreate(10, "view", 1), bnStyle(10, "height", "80"),
            bnCreate(11, "view", 1), bnStyle(11, "height", "80"),
            bnCreate(12, "view", 1), bnStyle(12, "height", "80"),
        ])
        host.render([.removeNode(nodeId: 11)])   // the MIDDLE one

        let scroll = try bnScrollView(host.root.subviews[0])
        let content = try bnContentView(of: scroll)
        XCTAssertEqual(content.subviews.count, 2, "the middle row is gone from the CONTENT view")
        assertFrame("row 10 keeps its place", content.subviews[0], 0, 0, 300, 80)
        assertFrame("row 12 MOVED UP into the hole — which is what proves the removed row left the "
                    + "YOGA tree too, not merely the view tree. A ghost node under the content node "
                    + "keeps reserving its 80pt and this row stays at y = 160.",
                    content.subviews[1], 0, 80, 300, 80)
        XCTAssertEqual(scroll.contentSize.height, 160, accuracy: 0.5,
                       "…and the content node SHRANK to 160: contentSize follows")
    }

    /// **THE TWO INDEX-MAPPING RULES, MEETING.** 6.1's text collapse says a `text` node
    /// whose parent is a text-bearing non-container gets **no view and no Yoga node**;
    /// 6.2's rule says a scroll node's wire child at index *i* is the CONTENT node's child
    /// at index *i*. Put a `button` (with its collapsed text child) directly inside a
    /// `scroll`, followed by a sibling at a KNOWN index, and the two rules have to hold at
    /// once.
    ///
    /// True by construction — the collapse returns before any container is touched — and
    /// that is precisely the kind of "true by construction" 6.1 learned to pin: the box at
    /// wire index 1 must be the content view's child at index 1, sitting directly under
    /// the button's measured height. A collapsed node that took a slot in either tree puts
    /// it at index 2 and every frame after it is wrong, silently.
    ///
    /// It is also **the only MEASURED leaf inside a scroll** anywhere in this suite or the
    /// demo — so the measure func is asserted against the [assertOracle], inside a
    /// `UIScrollView`, where a fabricated constant would otherwise never be caught.
    func testACollapsedTextChildInsideAScrollDoesNotSkewTheContentNodesIndices() throws {
        let host = BnSyntheticHost()
        host.render([
            bnCreate(1, "scroll", nil),
            bnStyle(1, "width", "300"), bnStyle(1, "height", "200"),
            // wire child 0 — a MEASURED leaf. alignSelf:flex-start so its width is its own
            // measured width (Yoga's default alignItems:stretch would stretch it to the
            // content node's 300 and the oracle would have nothing to say).
            bnCreate(20, "button", 1), bnStyle(20, "alignSelf", "flex-start"),
            bnCreate(21, "text", 20),          // COLLAPSED onto the UIButton: no view, no Yoga node
            bnText(21, "Scrolled button"),
            // wire child 1 — at an index that only holds if the collapse took no slot.
            bnCreate(30, "view", 1, insertIndex: 1), bnStyle(30, "height", "50"),
        ])

        let scroll = try bnScrollView(host.root.subviews[0])
        let content = try bnContentView(of: scroll)
        XCTAssertEqual(content.subviews.count, 2,
                       "the collapsed text node gets NO view: the content view's children are the "
                       + "button and the box — TWO, not three")

        let button = try XCTUnwrap(content.subviews[0] as? UIButton)
        let box = content.subviews[1]
        XCTAssertEqual(button.title(for: .normal), "Scrolled button")

        assertOracle("the button INSIDE the scroll", button, availableWidth: content.frame.width)

        XCTAssertEqual(box.frame.minY, button.frame.height, accuracy: 0.5,
                       "THE PIN: the box at wire index 1 is the CONTENT node's child at index 1, "
                       + "directly under the button's MEASURED height. A collapsed text node that "
                       + "took a slot in either tree would put it at index 2, and every frame below "
                       + "it would be silently skewed.")
        assertFrame("…and it is the 50-high box, stretched to the content node's width",
                    box, 0, button.frame.height, viewW, 50)
        XCTAssertEqual(scroll.contentSize.height, button.frame.height + box.frame.height,
                       accuracy: 0.5,
                       "the content node hugs the two of them — a measured leaf's height reaches "
                       + "contentSize like any other")
    }

    // ── WHAT iOS OWES INSTEAD OF ANDROID'S RE-CLAMP ──────────────────────────

    /// **`UIScrollView` DOES NOT MOVE `contentOffset` WHEN `contentSize` SHRINKS** — and
    /// this is the one scroll mechanism iOS has to implement that Android gets for free.
    ///
    /// Android's `ScrollView.onLayout` ends with `scrollTo(mScrollX, mScrollY)`, which
    /// re-clamps the offset against the content child's just-laid-out height on every
    /// layout it takes part in. `UIScrollView` does no such thing: assign a smaller
    /// `contentSize` under a live `contentOffset` and the page is simply left **scrolled
    /// past its own end** — a blank viewport, with the content above it, and nothing in
    /// any frame table wrong.
    ///
    /// So the shell clamps it itself, in `applyScrollFrames`. Remove that clamp and this
    /// test — and only this test — goes red.
    ///
    /// A CLAMP, not a reset: the second act asserts an offset still INSIDE the new range
    /// is the USER'S and is left alone. (A `contentOffset = .zero` "fix" would pass the
    /// first assertion and fail this one — and would snap a scrolled page to the top on
    /// every commit, which is exactly the Android bug the UNSPECIFIED fallback exists to
    /// prevent, re-introduced on the other platform.)
    ///
    /// **And a THIRD act, which is the one the `max(0, …)` FLOOR exists for.** Acts one and
    /// two shrink 800 → 240 against a 200 viewport, so the new maximum is a POSITIVE 40 and
    /// the floor never engages — **delete it and they both still pass.** Content that
    /// shrinks BELOW the viewport (a list that empties; a filter matching nothing; M7's
    /// first under-full frame) gives a NEGATIVE maximum: `min(max(0, 40), −120)` is −120,
    /// and the page would sit scrolled 120pt ABOVE its own content, permanently, because
    /// `UIScrollView` does not correct a programmatically-set out-of-range offset at rest.
    /// Act three shrinks to ONE row and pins the answer at 0.
    func testShrinkingTheContentClampsAScrolledOffset() throws {
        let host = BnSyntheticHost()
        host.render(scrollTree())
        let scroll = try bnScrollView(host.root.subviews[0])

        scroll.contentOffset = CGPoint(x: 0, y: scrollRange)   // the user is at the very end
        XCTAssertEqual(scroll.contentOffset.y, scrollRange, accuracy: 0.5)

        // A commit that SHRINKS the content: seven of the ten rows go. 3 × 80 = 240 of
        // content in a 200 viewport → the new maximum offset is 40.
        host.render((3..<rows).map { BnPatch.removeNode(nodeId: Int32(10 + $0)) })

        XCTAssertEqual(scroll.contentSize.height, 240, accuracy: 0.5,
                       "the content node shrank to three rows")
        XCTAssertEqual(scroll.contentOffset.y, 40, accuracy: 0.5,
                       "THE PIN: the offset is CLAMPED to the new maximum (240 − 200). UIScrollView "
                       + "does NOT do this — assign a smaller contentSize under a live contentOffset "
                       + "and the page is left scrolled 560pt past its own end, showing a blank "
                       + "viewport with the content above it. Android's ScrollView re-clamps on every "
                       + "layout and gets this for free; iOS owes it, and this is where it pays.")

        // …and now the other half: an offset INSIDE the range is the user's.
        scroll.contentOffset = CGPoint(x: 0, y: 20)
        host.render([bnCreate(99, "view", 1), bnStyle(99, "height", "80")])   // content GROWS
        XCTAssertEqual(scroll.contentOffset.y, 20, accuracy: 0.5,
                       "a clamp, NOT a reset: an offset still inside the range is the USER's and "
                       + "must survive the commit. Snapping it to 0 here would be the Android "
                       + "'page jumps to the top on every re-render' bug, re-introduced on iOS")

        // ── ACT THREE — the content shrinks BELOW the viewport, and the FLOOR is the only
        // thing standing between the user and a page scrolled above its own first row ────
        // Everything above leaves a POSITIVE maximum (240 − 200 = 40), so `max(0, …)` never
        // engages and DELETING IT CHANGES NOTHING. One row of 80 inside a 200-high viewport
        // makes the maximum −120: unfloored, `min(max(0, 20), −120)` is −120 and the shell
        // ASSIGNS it. UIScrollView keeps it (it does not correct an out-of-range offset set
        // programmatically at rest), so the page shows 120pt of nothing above its own
        // content, forever. This is M7's first under-full frame, and an emptied list.
        // rows 10/11/12 survived act one; 99 was added in act two. Leave ONE row.
        let doomed: [Int32] = [11, 12, 99]
        host.render(doomed.map { BnPatch.removeNode(nodeId: $0) })

        XCTAssertEqual(scroll.contentSize.height, rowH, accuracy: 0.5,
                       "one row left: the content is now SHORTER than the viewport")
        XCTAssertLessThan(scroll.contentSize.height, scroll.bounds.height,
                          "…which is the whole precondition — the maximum offset is NEGATIVE")
        XCTAssertEqual(scroll.contentOffset.y, 0, accuracy: 0.5,
                       "THE FLOOR: a maximum of 80 − 200 = −120 must be floored to 0. Without "
                       + "max(0, …) the clamp assigns −120 and the page is left scrolled 120pt "
                       + "ABOVE its own content — permanently, because UIScrollView does not "
                       + "correct a programmatically-set out-of-range offset at rest. Content "
                       + "shorter than its viewport has NO scrollable range, not a negative one")
    }

    /// **THE CLAMP'S ARITHMETIC, AS A TABLE** — `BnWidgetMapper.clampedOffset`, unit-tested.
    ///
    /// The clamp in `applyScrollFrames` is now gated on `!scroll.isTracking` (a commit that
    /// lands while the user's finger is down must NOT fight the rubber band: `bounces` is
    /// on by default, so a legitimate mid-drag `contentOffset.y` is NEGATIVE, and clamping
    /// it to 0 kills the bounce under the finger — M7's virtualized list commits
    /// continuously WHILE scrolling, which is when that stops being hypothetical).
    ///
    /// A gesture is awkward to synthesize in a unit test, so the gate is deliberately ONE
    /// commented line sitting over arithmetic that is tested HERE, exhaustively — the shape
    /// the review asked for: *an untestable guard over tested arithmetic, not an untested
    /// blob.*
    func testTheOffsetClampIsAClampAndItsFloorIsNotDecoration() {
        let viewport = CGSize(width: 300, height: 200)
        let tall = CGSize(width: 300, height: 800)     // range 0…600
        let short = CGSize(width: 300, height: 80)     // NO range at all
        let cases: [(String, CGPoint, CGSize, CGPoint)] = [
            ("IN RANGE — the offset is the USER's and must not move",
             CGPoint(x: 0, y: 250), tall, CGPoint(x: 0, y: 250)),
            ("at the very end — still in range, still untouched",
             CGPoint(x: 0, y: 600), tall, CGPoint(x: 0, y: 600)),
            ("PAST THE END — clamped back to the maximum, not reset to 0 (a reset would snap "
             + "a scrolled page to the top on every commit)",
             CGPoint(x: 0, y: 900), tall, CGPoint(x: 0, y: 600)),
            ("NEGATIVE input — floored to 0 (this is the state a rubber-band leaves behind, "
             + "which is why the caller does not run this while isTracking)",
             CGPoint(x: 0, y: -30), tall, CGPoint(x: 0, y: 0)),
            ("**CONTENT SHORTER THAN THE VIEWPORT — 0, NEVER NEGATIVE.** The maximum is "
             + "80 − 200 = −120, and un-floored the clamp would ASSIGN −120: a page scrolled "
             + "above its own content. This is the case the `max(0, …)` floor exists for and "
             + "the one the 800 → 240 shrink test cannot see",
             CGPoint(x: 0, y: 300), short, CGPoint(x: 0, y: 0)),
            ("…and it holds for an offset that was already 0",
             CGPoint(x: 0, y: 0), short, CGPoint(x: 0, y: 0)),
            ("the horizontal axis obeys the same two rules — no range, so 0",
             CGPoint(x: 120, y: 0), tall, CGPoint(x: 0, y: 0)),
        ]
        for (what, offset, contentSize, expected) in cases {
            let actual = BnWidgetMapper.clampedOffset(offset, contentSize: contentSize,
                                                      viewport: viewport)
            XCTAssertEqual(actual.x, expected.x, accuracy: 0.5, "\(what) — x")
            XCTAssertEqual(actual.y, expected.y, accuracy: 0.5, "\(what) — y")
        }
    }

    // ── The two diagnostics ──────────────────────────────────────────────────

    /// **CONTAINER STYLES ON A SCROLL NODE ARE IGNORED AND LOGGED** (non-negotiable #7 /
    /// design decision 6). `BnScroll`'s surface cannot produce them, but the raw element
    /// can — `OpenElement("scroll") + AddAttribute("gap", …)` reaches the wire, and a
    /// .NET test pins that it does, precisely so this rule is known to be live code. Each
    /// of the six would style the *scroll* node, whose only Yoga child is the content
    /// node, and each fails silently and bafflingly: `flexDirection: row` stretches the
    /// content to the viewport height and the page stops scrolling; `justifyContent:
    /// center` offsets it to y = −300 and the top of the content becomes permanently
    /// unreachable.
    ///
    /// So: the frames must be **exactly** the un-styled ones, and each drop must be named
    /// in a diagnostic. (The six names are pinned EQUAL to Kotlin's by
    /// `ShellStyleTableDriftTests.TheTwoShellsScrollIgnoreLists_AreIdenticalToEachOther`;
    /// this asserts the shell actually OBEYS them.)
    func testContainerStylesOnAScrollNodeAreIgnoredAndLogged() throws {
        let ignored = [
            ("flexDirection", "row"),
            ("justifyContent", "center"),
            ("alignItems", "center"),
            ("flexWrap", "wrap"),
            ("gap", "8"),
            ("padding", "16"),
        ]
        let host = bnRender(scrollTree(scrollStyles: ignored.map { bnStyle(1, $0.0, $0.1) }))
        let scroll = try bnScrollView(host.root.subviews[0])
        let content = try bnContentView(of: scroll)

        assertFrame("the viewport is untouched", scroll, 0, 0, viewW, viewH)
        assertFrame("the content node is EXACTLY where it would be with no styles at all — "
                    + "flexDirection:row would have stretched it to 200 and killed scrolling; "
                    + "padding:16 would have moved every row",
                    content, 0, 0, viewW, contentH)
        for i in 0..<rows {
            assertFrame("row \(i)", content.subviews[i], 0, rowH * CGFloat(i), viewW, rowH)
        }

        let diags = host.mapper.diagnostics
        for (property, _) in ignored {
            XCTAssertTrue(diags.contains { $0.contains("node 1") && $0.contains(property) },
                          "'\(property)' on a scroll node must be DROPPED WITH A WARNING naming the "
                          + "node and the style (got: \(diags))")
        }
    }

    /// The other half of the same rule: **item styles and `backgroundColor` apply
    /// NORMALLY.** A `BnScroll` *is* a flex item — how the viewport is placed in its
    /// parent is entirely the author's business; it is only the scroll node's CONTAINER
    /// layout that belongs to the shell. Over-broad filtering here would be as wrong as
    /// no filtering.
    func testItemStylesAndBackgroundColorApplyNormallyToAScrollNode() throws {
        let host = bnRender(scrollTree(scrollStyles: [
            bnStyle(1, "margin", "10"),
            bnStyle(1, "backgroundColor", "#FF0000"),
        ]))
        let scroll = try bnScrollView(host.root.subviews[0])
        let content = try bnContentView(of: scroll)

        assertFrame("margin is an ITEM style: it places the VIEWPORT in its parent and must still "
                    + "be honoured", scroll, 10, 10, viewW, viewH)
        XCTAssertEqual(scroll.backgroundColor, UIColor(red: 1, green: 0, blue: 0, alpha: 1),
                       "…and so is backgroundColor (it paints the viewport)")
        assertFrame("…while the content node is unmoved by either — margin insets the VIEWPORT, not "
                    + "the content", content, 0, 0, viewW, contentH)
        XCTAssertTrue(host.mapper.diagnostics.isEmpty,
                      "no diagnostic: an item style on a scroll node is not a mistake "
                      + "(got: \(host.mapper.diagnostics))")
    }

    /// **THE DEFINITE-HEIGHT WARNING.** An `auto`-height scroll node takes its height
    /// **from** its content, so the viewport IS the content and there is nothing to
    /// scroll. The symptom is a page that simply does not move: no exception, no dropped
    /// patch, no wrong frame. So the shell says so, ONCE (a layout pass runs per committed
    /// frame; a warning per frame is a flood, and a flood is a thing people mute).
    func testAnAutoHeightScrollNodeWarnsOnce() throws {
        let host = BnSyntheticHost()
        host.render(scrollTree(viewportHeight: nil))
        // A second frame: the layout pass runs again, and the warning must NOT.
        host.render([bnCreate(99, "view", nil), bnStyle(99, "height", "10")])

        let scroll = try bnScrollView(host.root.subviews[0])
        let content = try bnContentView(of: scroll)
        XCTAssertEqual(scroll.frame.height, content.frame.height, accuracy: 0.5,
                       "the auto-height viewport HUGS its content — 800 over 800, scroll range zero. "
                       + "Nothing errors; the page just never moves.")

        let warnings = host.mapper.diagnostics.filter { $0.contains("definite height") }
        XCTAssertEqual(warnings.count, 1,
                       "exactly ONE warning, across TWO layout passes "
                       + "(got: \(host.mapper.diagnostics))")
        XCTAssertTrue(warnings.first?.contains("node 1") == true,
                      "…and it must name the node (got: \(warnings))")
    }

    /// …and the negative: a scroll node with a definite height is the normal case and must
    /// say nothing at all. Without this, a warning that fired on EVERY scroll node would
    /// still pass the test above.
    ///
    /// It exits at the FIRST condition (the height is a POINT), so it says nothing about
    /// the second — that is what the three tests below are for.
    func testADefiniteHeightScrollNodeWarnsAboutNothing() {
        let host = bnRender(scrollTree())
        XCTAssertTrue(host.mapper.diagnostics.isEmpty,
                      "a 300×200 viewport over 800pt of content is the WORKING case — it must "
                      + "produce no diagnostic (got: \(host.mapper.diagnostics))")
    }

    /// **A FLEX-SIZED VIEWPORT THAT SCROLLS MUST NOT BE WARNED ABOUT** — the shape a
    /// full-screen scrolling page actually has. `Grow="1" Basis="0"` (CSS's `flex: 1`) in
    /// a bounded parent: the scroll node **declares no height at all**, so it sails past
    /// the diagnostic's first condition and is saved only by the second — flex DID give it
    /// a bounded height (200, from its parent), and 200 ≠ 800.
    func testAFlexSizedScrollNodeOverTallerContentWarnsAboutNothing() throws {
        let host = bnRender(grownScrollTree(parentHeight: viewH, contentHeight: contentH))
        let scroll = try bnScrollView(host.root.subviews[0].subviews[0])

        XCTAssertEqual(scroll.frame.height, viewH, accuracy: 0.5,
                       "the viewport took its 200 from its bounded parent (Grow + Basis=0)")
        XCTAssertEqual(scroll.contentSize.height, contentH, accuracy: 0.5,
                       "…over 800 of content: it SCROLLS")
        XCTAssertTrue(host.mapper.diagnostics.isEmpty,
                      "a flex-sized viewport that scrolls declares no height and is entirely correct "
                      + "— it must produce no diagnostic (got: \(host.mapper.diagnostics))")
    }

    /// **A VIEWPORT TALLER THAN ITS CONTENT MUST NOT BE WARNED ABOUT** — the test that
    /// caught the Gate 2 blocker, mirrored.
    ///
    /// The same flex-sized viewport, over content SHORTER than itself: a list still
    /// loading, a page with one item on it, M7's virtualized list on its first under-full
    /// frame. **This is not a mistake.** It is the ordinary case, and it starts scrolling
    /// the moment the content grows past the viewport.
    ///
    /// Gate 2 shipped the condition as `>=` ("at LEAST as tall as its content") where the
    /// design says **exactly**, so this ordinary shape got a warning that *stated a
    /// falsehood* and prescribed a fix the author had already applied. Weaken the
    /// comparison to `>=` and this test — and only this test — goes red.
    func testAViewportTallerThanItsContentWarnsAboutNothing() throws {
        let host = bnRender(grownScrollTree(parentHeight: viewH, contentHeight: 100))
        let scroll = try bnScrollView(host.root.subviews[0].subviews[0])

        XCTAssertEqual(scroll.frame.height, viewH, accuracy: 0.5, "the viewport is 200 tall")
        XCTAssertEqual(scroll.contentSize.height, 100, accuracy: 0.5,
                       "…and its content is only 100 — there is nothing to scroll YET")
        XCTAssertTrue(host.mapper.diagnostics.isEmpty,
                      "A VIEWPORT TALLER THAN ITS CONTENT IS NOT A MISTAKE. It is a viewport with "
                      + "nothing to scroll YET — the ordinary case for any list still loading, and "
                      + "for M7's virtualized list on its first under-full frame. A diagnostic that "
                      + "cries wolf on the shape the docs prescribe is worse than no diagnostic "
                      + "(got: \(host.mapper.diagnostics))")
    }

    /// **`Grow="1"` ALONE IS NOT A DEFINITE HEIGHT — AND EVERY DOC IN THIS PHASE SAID IT
    /// WAS** (found on the AVD at the Gate 2 review; mirrored here because the mechanism is
    /// Yoga's and is therefore identical on both shells).
    ///
    /// A `Grow="1"` scroll node leaves `flexBasis: auto`, so its flex BASIS is its
    /// CONTENT's height — 800. Against a 200-high parent the free space is `200 − 800 =
    /// −600`: **NEGATIVE**. `flexGrow` only ever distributes POSITIVE free space, so it
    /// never gets a say; negative free space goes to the **SHRINK** pass, in proportion to
    /// `flexShrink` — **which Yoga defaults to 0.** Nothing shrinks. The viewport keeps its
    /// 800, spills out of its 200-high parent, and viewport == content: nothing scrolls.
    ///
    /// That is the phase's own mechanism, one level up — the *exact* sentence the design
    /// writes about the CONTENT node, just as true of the VIEWPORT. So the diagnostic is
    /// **right** to fire here, and this test pins that it does.
    func testAGrowOnlyScrollNodeIsNotBoundedAndIsWarnedAbout() throws {
        let host = bnRender(grownScrollTree(parentHeight: viewH, contentHeight: contentH,
                                            growOnly: true))
        let scroll = try bnScrollView(host.root.subviews[0].subviews[0])

        XCTAssertEqual(scroll.frame.height, contentH, accuracy: 0.5,
                       "Grow=\"1\" with flexBasis:auto takes its BASIS from its content (800), and "
                       + "the free space against a 200-high parent is NEGATIVE (−600). flexGrow only "
                       + "distributes POSITIVE free space; the negative goes to the SHRINK pass, and "
                       + "Yoga's flexShrink default is 0. So NOTHING SHRINKS and the viewport keeps "
                       + "its 800 — spilling out of its 200-high parent.")
        XCTAssertEqual(scroll.contentSize.height, scroll.frame.height, accuracy: 0.5,
                       "…and it is exactly as tall as its content, so THERE IS NOTHING TO SCROLL")

        let warnings = host.mapper.diagnostics.filter { $0.contains("definite height") }
        XCTAssertEqual(warnings.count, 1,
                       "THE DIAGNOSTIC IS RIGHT TO FIRE HERE. `Grow=\"1\"` alone does not bound a "
                       + "viewport — use an explicit Height, or Grow + Basis=\"0\" (CSS's `flex: 1`), "
                       + "or Grow + Shrink=\"1\". (got: \(host.mapper.diagnostics))")
    }

    /// **AND THE FIRST CONDITION EARNS ITS KEEP TOO.** A scroll node with an EXPLICIT
    /// `Height="800"` over exactly 800 of content computes out equal to its content — so it
    /// passes the second condition — and it must still say nothing: the author gave it a
    /// definite height, which is what the warning would tell them to do.
    ///
    /// Delete the `bn_yoga_node_has_declared_height` check and this test — and only this
    /// test — goes red.
    func testADefiniteHeightThatHappensToEqualItsContentWarnsAboutNothing() throws {
        let host = bnRender(scrollTree(viewportHeight: contentH))  // Height="800" over 800
        let scroll = try bnScrollView(host.root.subviews[0])

        XCTAssertEqual(scroll.frame.height, scroll.contentSize.height, accuracy: 0.5,
                       "the viewport and its content are the same height, to the point")
        XCTAssertTrue(host.mapper.diagnostics.isEmpty,
                      "…and the author DECLARED that height, which is exactly what the warning would "
                      + "have told them to do. Both conditions are needed, and this is the one that "
                      + "pins the first (got: \(host.mapper.diagnostics))")
    }

    /// **THE DIAGNOSTICS BOOKKEEPING DIES WITH ITS NODE.** Node ids are **REUSED** —
    /// .NET's restart at 1 after a reset, so a retired id is handed straight back out on
    /// the next page. Keep a warn-once key past its node's death and the next scroll node
    /// to inherit that id is warned about **nothing at all**, silenced by a ghost — and
    /// the diagnostic is worth most on a freshly-written page, which is exactly when a
    /// stale key eats it. (And the message list would grow by one per navigation, forever.)
    ///
    /// Mount a broken (auto-height) scroll node, navigate away, mount another broken one
    /// with the same id. Twice broken, twice warned.
    func testAScrollNodeThatReusesARetiredIdGetsItsOwnWarning() {
        let host = BnSyntheticHost()
        host.render(scrollTree(viewportHeight: nil))
        XCTAssertEqual(host.mapper.diagnostics.count, 1,
                       "the first auto-height scroll node is warned about")

        host.render([.removeNode(nodeId: 1)])   // navigate away: ONE RemoveNodePatch
        XCTAssertTrue(host.mapper.diagnostics.isEmpty,
                      "the diagnostics go with the node they belong to — otherwise the list grows by "
                      + "one message per navigation, forever")

        host.render(scrollTree(viewportHeight: nil))   // …and the next page inherits id 1

        let warnings = host.mapper.diagnostics.filter { $0.contains("definite height") }
        XCTAssertEqual(warnings.count, 1,
                       "THE PIN: a scroll node that REUSES a retired id must get its OWN warning "
                       + "(got: \(host.mapper.diagnostics))")
    }
}
