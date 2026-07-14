// ─────────────────────────────────────────────────────────────────────────────
// BnYogaLifecycleTests — Phase 6.1 Gate 3 review (CRITICAL 1).
//
// **ONE RemoveNodePatch STANDS FOR A WHOLE SUBTREE.**
//
// The renderer does NOT emit one `RemoveNode` per node: it emits one for the
// subtree's ROOT and purges the descendants in its own bookkeeping
// (`NativeRenderer.PurgeNodeSubtree`; the host contract on
// `ProcessDisposedComponent` says so in as many words). A host that drops only the
// named node's map entry therefore leaks EVERY DESCENDANT.
//
// And on iOS the leak is worse than on Android, which is why this file exists.
// Android's twin (`YogaNodeLifecycleAndroidTest`) leaks a GC-rooted `View` and a
// Java `YogaNode` whose native peer merely outstays its welcome. Here a missed
// descendant is a **malloc'd `YGNodeRef` that nothing will ever free** — the .mm
// says so in as many words ("raw native memory — nothing else will ever free it") —
// plus a UIView still on the far end of a live native node's measure-func edge.
// Every navigation replaces the tree, so it leaks per navigation, forever.
//
// **A leaked node lays out nothing and shows nothing, so NO FRAME ASSERTION CAN SEE
// THIS.** The only honest witness is the mapper's own bookkeeping, driven by the
// REAL renderer's patch stream — a synthetic RemoveNode patch would be assuming the
// very thing under test (that ONE patch arrives for a whole subtree). Before this
// file, deleting `BnWidgetMapper.handleRemove`'s purge loop left the entire XCTest
// suite green.
//
// So: mount `CompositionProbe`, whose `ItemComponent` is a two-node subtree (a div
// + its text child) with Add and Remove buttons, and cycle. Every add-then-remove
// returns the tree to the shape it had, so **the counts must return to their
// baseline**. With the purge missing they grow by one node per cycle, forever.
//
// The iOS twin of `YogaNodeLifecycleAndroidTest`, assertion for assertion.
//
// ── PHASE 6.2: AND THE SUBTREE NOW CONTAINS A NODE NO PATCH CAN NAME ─────────
//
// A `scroll` node's SYNTHETIC content node is the shell's, not the renderer's — it is on
// neither wire. Two more tests below (the twins of Android's two) prove the purge reaches
// it: once by a `RemoveNode` naming the scroll node, and once by one naming an ANCESTOR
// of it — **which is what navigation actually emits**, since the page's root column is
// what gets disposed. On iOS the stakes are a step higher than a leak: the content node
// is freed by `YGNodeFreeRecursive` along with its scroll node, so a handle the shell
// keeps afterwards is a **DANGLING `YGNodeRef`**, and the next `calculateAndApply` reads
// through it.
//
// ── MUTATION EVIDENCE (measured on CI, not asserted from an armchair) ────────
//
// Delete `handleRemove`'s purge loop and drop only the NAMED node, and this test
// fails exactly as predicted: nodes 18 → **21**, Yoga nodes 15 → **18** — +1 leaked
// text child per cycle, three cycles, one per navigation in the real app.
//
// And it fails LOUDER than a leak, which is the part worth writing down: the two
// navigation tests (`testSettingsNavigation…`, `testBackReturnsFresh…`) **CRASH the
// test host**. `bn_yoga_node_free_subtree` frees every descendant's `YGNodeRef` —
// so a descendant left behind in `yogaNodes` is not merely leaked, it is a DANGLING
// POINTER into freed native memory, and the next `calculateAndApply` dereferences it.
// The purge loop is load-bearing for MEMORY SAFETY, not just for bookkeeping hygiene.
// A crash in a nav test names the nav test; only this file names the cause.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit
@testable import BnHost

final class BnYogaLifecycleTests: XCTestCase {

    /// Hold the runtime for the test's lifetime so the @convention(c) callback
    /// trampoline is never released mid-render.
    private var runtime: BnRuntime?
    private var root: UIView!
    private var mapper: BnWidgetMapper!

    private struct Counts: Equatable {
        let nodes: Int
        let yogaNodes: Int
        let yogaViews: Int
    }

    func testRemovingASubtreePurgesEveryDescendantFromBothTrees() throws {
        root = UIView(frame: CGRect(x: 0, y: 0, width: 390, height: 844))
        let mapper = BnWidgetMapper(root: root)
        self.mapper = mapper
        let runtime = BnRuntime(mapper: mapper)
        self.runtime = runtime
        runtime.onError = { msg, err in NSLog("[BnYogaLifecycleTests] \(msg): \(err)") }

        // There is no iOS route registry — mount the probe by its registry NAME (the
        // pattern BnLayoutDemoTests uses; MainActivity's EXTRA_COMPONENT is Android's).
        try runtime.start(component: "CompositionProbe", os: "ios")

        XCTAssertTrue(pollUntil { self.probeRoot() != nil },
                      "CompositionProbe never rendered its 7-child composite")

        let baseline = counts()
        XCTAssertGreaterThan(baseline.nodes, 0, "the mount must have created nodes at all")
        XCTAssertEqual(baseline.yogaNodes, baseline.yogaViews,
                       "every node with a Yoga node must have a view mapping — the Yoga tree "
                       + "mirrors the VIEW tree")
        XCTAssertLessThan(baseline.yogaNodes, baseline.nodes,
                          "…and the COLLAPSED text children (the three buttons') own a view-map "
                          + "entry but NO Yoga node, so the Yoga tree is the smaller of the two. "
                          + "If these were equal the collapse would not be happening and this "
                          + "test's baseline would be measuring the wrong thing")

        // Three add→remove cycles. Add appends item-N (a div + its text child); Remove
        // drops the FIRST item (the same two nodes). Net zero, every time.
        for cycle in 0..<3 {
            try tapButton("Add")
            XCTAssertTrue(pollUntil { self.itemCount() == 3 },
                          "the list never grew to 3 items after Add (cycle \(cycle))")
            try tapButton("Remove")
            XCTAssertTrue(pollUntil { self.itemCount() == 2 },
                          "the list never shrank back to 2 items after Remove (cycle \(cycle))")
        }

        let after = counts()
        XCTAssertEqual(
            after.nodes, baseline.nodes,
            "THE PIN: an add→remove cycle is net-zero, so the mapper's node count must be back "
            + "at its baseline. ONE RemoveNodePatch arrives for the item's whole SUBTREE — purge "
            + "only the named node and its text child stays in the map forever, pinning a UIView, "
            + "once per cycle (3 cycles ⇒ +3 here, and one per navigation in the real app)")
        XCTAssertEqual(
            after.yogaNodes, baseline.yogaNodes,
            "…and the YOGA tree's nodes with it. This is the one that is a REAL LEAK on iOS: a "
            + "YGNodeRef is raw malloc'd memory and nothing but bn_yoga_node_free_subtree will "
            + "ever free it")
        XCTAssertEqual(
            after.yogaViews, baseline.yogaViews,
            "…and the view→node mappings, which are the last edge from a native node to a UIView "
            + "(a stale one keeps a measure func pointing at a released view)")
    }

    // ── Phase 6.2: …AND THE SUBTREE INCLUDES THE NODE NO PATCH EVER NAMES ────

    /// A `scroll` node's SYNTHETIC content node is created by the shell, lives in both
    /// trees, and is on **neither wire**: the renderer emits one `RemoveNodePatch` for the
    /// scroll node and knows nothing about the node underneath it. So the purge has to
    /// reach it — and the reason it does is structural rather than special-cased: the
    /// content node is a Yoga CHILD of its scroll node, so `bn_yoga_node_free_subtree`
    /// frees it for nothing.
    ///
    /// **Which is exactly why the shell's own handle to it must go in the same breath.**
    /// On Android a content node left in the map is a leak (it pins the content View, the
    /// rows under it, and the Activity Context). **Here it is a `YGNodeRef` into memory
    /// `YGNodeFreeRecursive` has already freed** — and the next `calculateAndApply` reads
    /// its computed frame. The 6.1 mutation run proved that shape of mistake CRASHES the
    /// test host rather than merely leaking; nothing about it is hypothetical.
    ///
    /// Synthetic (not the real renderer, unlike the test above) because a `RemoveNode` for
    /// a scroll node is exactly the patch under test, and there is no demo that mounts and
    /// unmounts one. The twin of `YogaNodeLifecycleAndroidTest
    /// .removing_a_scroll_node_frees_the_SYNTHETIC_content_node_too`.
    func testRemovingAScrollNodeFreesTheSyntheticContentNodeToo() {
        let host = BnSyntheticHost()

        host.render([
            bnCreate(1, "scroll", nil),
            bnStyle(1, "width", "300"), bnStyle(1, "height", "200"),
            bnCreate(10, "view", 1), bnStyle(10, "height", "80"),
            bnCreate(11, "view", 1), bnStyle(11, "height", "80"),
            bnCreate(12, "view", 11),   // a grandchild, INSIDE the content subtree
        ])

        XCTAssertEqual(host.mapper.nodeCount, 4, "four WIRE nodes: the scroll and its three descendants")
        XCTAssertEqual(host.mapper.yogaContentNodeCount, 1,
                       "ONE synthetic content node — the shell's, never the renderer's")
        XCTAssertEqual(host.mapper.scrollContentCount, 1,
                       "…and it is in the VIEW tree too, or it is in neither")
        XCTAssertEqual(host.mapper.yogaViewCount, 5,
                       "the Yoga tree holds the four wire nodes AND the synthetic one — five view "
                       + "mappings, four of which any patch can name")

        // ONE patch. It stands for the scroll node, its content node, and everything under
        // them.
        host.render([.removeNode(nodeId: 1)])

        XCTAssertEqual(host.mapper.nodeCount, 0, "the wire nodes are gone")
        XCTAssertEqual(host.mapper.yogaNodeCount, 0, "…and their Yoga nodes")
        XCTAssertEqual(host.mapper.yogaContentNodeCount, 0,
                       "THE PIN: the SYNTHETIC content node is gone with them. No patch named it; "
                       + "the free reached it because it is a Yoga CHILD of the scroll node. A "
                       + "handle left behind here is a DANGLING YGNodeRef — the memory is already "
                       + "freed — and the next calculateAndApply dereferences it. (Android's twin "
                       + "merely leaks.)")
        XCTAssertEqual(host.mapper.scrollContentCount, 0, "…and the view-tree half of the pair")
        XCTAssertEqual(host.mapper.yogaViewCount, 0, "…and its view mapping")
        XCTAssertTrue(host.root.subviews.isEmpty, "the UIScrollView itself is detached from the host")
    }

    /// **…AND THE PATCH THE APP ACTUALLY EMITS DOES NOT NAME THE SCROLL NODE AT ALL.**
    ///
    /// The test above removes the scroll node itself. **Navigation never does that.**
    /// `BnScrollDemo`'s scroll lives inside a root `BnColumn`, and navigating away disposes
    /// the **page** — so the real `RemoveNodePatch` names the **column**, and the scroll
    /// node (and, two levels down, the synthetic content node that no patch could name even
    /// in principle) has to be reached **transitively**.
    ///
    /// It works today: both subtree walks — the view hierarchy's and Yoga's — are structural
    /// and find it for free. But the one path the app actually takes was the one nothing
    /// asserted, and "it works transitively" is an assumption until a test says so. Same
    /// counts as above; the only difference is the patch, and the patch is the whole point.
    func testRemovingAWrapperFreesTheScrollNodeAndItsSyntheticContentNode() {
        let host = BnSyntheticHost()

        // The demo's shape: a root column, a scroll inside it, rows inside that.
        host.render([
            bnCreate(1, "view", nil),                                   // ← the root BnColumn
            bnCreate(2, "scroll", 1),
            bnStyle(2, "width", "300"), bnStyle(2, "height", "200"),
            bnCreate(10, "view", 2), bnStyle(10, "height", "80"),
            bnCreate(11, "view", 2), bnStyle(11, "height", "80"),
            bnCreate(12, "view", 11),   // a grandchild, INSIDE the content subtree
        ])

        XCTAssertEqual(host.mapper.nodeCount, 5,
                       "five WIRE nodes: the wrapper, the scroll and its three descendants")
        XCTAssertEqual(host.mapper.yogaContentNodeCount, 1,
                       "ONE synthetic content node, two levels below the node the patch will name")
        XCTAssertEqual(host.mapper.scrollContentCount, 1, "…and its view-tree half")
        XCTAssertEqual(host.mapper.yogaViewCount, 6,
                       "six Yoga view mappings — the five wire nodes plus the synthetic one")

        // THE PATCH NAVIGATION ACTUALLY EMITS: it names the WRAPPER. Nothing in it mentions
        // the scroll node, and nothing could ever mention the content node.
        host.render([.removeNode(nodeId: 1)])

        XCTAssertEqual(host.mapper.nodeCount, 0, "the wrapper and everything under it are gone")
        XCTAssertEqual(host.mapper.yogaNodeCount, 0, "…and their Yoga nodes")
        XCTAssertEqual(host.mapper.yogaContentNodeCount, 0,
                       "THE PIN: the SYNTHETIC content node is freed by a patch that names its "
                       + "GRANDPARENT. This is the path the app takes on EVERY navigation away from "
                       + "a page with a scroll on it — the other test's patch (RemoveNode on the "
                       + "scroll itself) is the one the app never emits.")
        XCTAssertEqual(host.mapper.scrollContentCount, 0, "…and the view-tree half")
        XCTAssertEqual(host.mapper.yogaViewCount, 0, "…and its Yoga view mapping — the last edge")
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private func counts() -> Counts {
        Counts(nodes: mapper.nodeCount,
               yogaNodes: mapper.yogaNodeCount,
               yogaViews: mapper.yogaViewCount)
    }

    /// The probe's root container: the host's single child, once it holds the full
    /// 7-child composite (header · badge · label · list · Add · Insert · Remove).
    private func probeRoot() -> UIView? {
        guard let container = root.subviews.first, container.subviews.count >= 7 else { return nil }
        return container
    }

    /// The list container (root child [3]) — its children ARE the items.
    private func itemCount() -> Int? {
        guard let container = probeRoot(), container.subviews.count > 3 else { return nil }
        return container.subviews[3].subviews.count
    }

    private func tapButton(_ title: String, file: StaticString = #filePath, line: UInt = #line) throws {
        let button = try XCTUnwrap(findButton(in: root, title: title),
                                   "button '\(title)' not on screen", file: file, line: line)
        button.sendActions(for: .touchUpInside)
    }

    private func findButton(in view: UIView, title: String) -> UIButton? {
        if let b = view as? UIButton, b.title(for: .normal) == title { return b }
        for sub in view.subviews {
            if let f = findButton(in: sub, title: title) { return f }
        }
        return nil
    }

    /// Pumps the MAIN runloop (draining the mapper's main-queue batch) until the
    /// condition holds or the deadline passes. Dispatch is async off the lane, so
    /// every post-tap assertion polls.
    private func pollUntil(deadline seconds: TimeInterval = 30, _ cond: () -> Bool) -> Bool {
        let end = Date().addingTimeInterval(seconds)
        while Date() < end {
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.05))
            if cond() { return true }
        }
        return cond()
    }
}
