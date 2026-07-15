// ─────────────────────────────────────────────────────────────────────────────
// BnScrollDemoImageTests — Phase 6.3 Gate 3 Task 3.4 — **AN IMAGE INSIDE A SCROLL,
// AND THE 6.2 FRAME TABLE DOES NOT MOVE** (6.3 non-negotiable #2, stated bluntly:
// *if a number in it moves, the change is wrong*). The iOS twin of
// `BnScrollDemoImageAndroidTest`.
//
// Images inside a scroll viewport is the most common real usage of both features, and
// leaving it unexercised until someone hits it is how you find out the hard way. But
// `BnScrollDemo`'s frame table **is the 6.2 cross-platform parity contract**, so this
// test asserts the image AND re-asserts every number of that table — *with the bytes
// loaded*, which is the state `BnScrollDemoTests` never sees.
//
// ── WHY `BnScrollDemoTests` IS NOT THIS TEST, AND MUST NOT BECOME IT ─────────────
// That class stands up NO fixture server and **clears Kingfisher's caches**, so its row-0
// image **fails to load** (connection refused, immediately, offline) — and it now AWAITS
// that failure and ASSERTS it (`ERROR`, on the URL the wire carried, nothing painted).
// It asserts the same table and still passes, which is not an accident to be tidied away
// but the *other half of the proof*: **a failed load moves nothing either**, because the
// image's size is definite and there is no measurement for a failure to change. Between
// the two classes, the 6.2 table is pinned against an image that loaded and against one
// that did not.
//
// **The claim used to be FALSE, and order-dependent** — the Gate 3 review caught it. That
// class cleared no caches and asserted nothing about the outcome, and XCTest runs classes
// ALPHABETICALLY: `BnScrollDemoImage…` sorts BEFORE `BnScrollDemoTests`, so `fixed.png` sat
// in Kingfisher's memory *and disk* cache and row 0 was a cache HIT — **the image
// SUCCEEDED**, and the class quietly proved the same thing as this one while its header
// said the opposite. (Android hit exactly this, fixed it, and recorded the fix; Gate 3
// copied the claim and not the fix.)
//
// ── THE TWO INDEPENDENT REASONS THE TABLE CANNOT MOVE ───────────────────────────
//  - **the row's height is DEFINITE (80)** — a child cannot grow a definite-height parent;
//  - **the image's size is DEFINITE (40 × 40)** — both axes declared, so **Yoga never
//    calls its measure func**. The bytes cannot move a frame even in principle, and the
//    fixture's natural size (64 × 48, asserted) is nowhere in the answer.
//
// ── AND WHAT ONLY A SCROLL CAN PROVE ────────────────────────────────────────────
// This image lives inside a **re-parented subtree under a SYNTHETIC content node** — a
// node no patch ever names. It still fetches, still paints, and its in-flight request
// is still cancelled when the page goes away (the subtree purge; pinned in
// `BnImageTests` and `BnYogaLifecycleTests`).
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit
@testable import BnHost

final class BnScrollDemoImageTests: BnHostTestCase {

    // BnScrollDemo.razor's constants and the two products it COMPUTES from them.
    private let rows = 10
    private let rowH: CGFloat = 80
    private let viewH: CGFloat = 200
    private var contentH: CGFloat { CGFloat(rows) * rowH }   // 800
    private var scrollRange: CGFloat { contentH - viewH }    // 600
    private let imageRow = 0
    private let imageW: CGFloat = 40
    private let imageH: CGFloat = 40

    private var server: BnImageFixtureServer!
    private var runtime: BnRuntime?
    private var host: UIView!
    private var mapper: BnWidgetMapper!

    override func setUpWithError() throws {
        try super.setUpWithError()
        bnClearImageCaches()
        // The close is STRUCTURAL (a teardown block registered by `started(for:)`).
        server = try BnImageFixtureServer.started(for: self)
        server.release() // this page has no BEFORE table to protect — 6.2's table is the contract

        host = UIView(frame: CGRect(x: 0, y: 0, width: 390, height: 844))
        let mapper = bnMapper(root: host)
        self.mapper = mapper
        let runtime = BnRuntime(mapper: mapper)
        self.runtime = runtime
        runtime.onError = { msg, err in NSLog("[BnScrollDemoImageTests] \(msg): \(err)") }
        try runtime.start(component: "BnScrollDemo", os: "ios")
    }

    /// **THE SERVER'S OWN ERRORS ARE ASSERTED EMPTY**: this class, like `BnImageDemoTests`,
    /// **cancels nothing** — no client here drops a connection — so a failed write on a
    /// fixture-server worker thread is a real server bug, and the swallow that (correctly)
    /// keeps a broken pipe from killing the test host would otherwise make it silent.
    override func tearDown() {
        XCTAssertEqual(server?.errors ?? [], [],
                       "the fixture server failed a write, and NOTHING ON THIS PAGE CANCELS "
                       + "ANYTHING — so it is a real server bug rather than a dropped client")
        server = nil // …and the CLOSE is the teardown block `started(for:)` registered
        super.tearDown()
    }

    func testTheRowImageLoadsAndThe62FrameTableIsUNCHANGED() throws {
        let fixture = try server.decoded(server.fixedPng)
        XCTAssertFalse(fixture.size.width == imageW && fixture.size.height == imageH,
                       "the row image's fixture must NOT be 40 × 40 — otherwise 'it measures "
                       + "40 × 40' is a coincidence rather than a proof that the declared size "
                       + "short-circuits measurement. Got \(fixture.size)")

        let root = try pollForDemo()

        // THE SYNCHRONIZATION GATE, in its one-request form: the table below is only a statement
        // about an image that LOADED once Kingfisher's own terminal callback says so.
        bnAwaitImageResults(mapper, 1)
        XCTAssertEqual(mapper.imageResults.map { BnUrlOutcome($0) },
                       [BnUrlOutcome(url: BnImageFixtureServer.FIXED_URL, outcome: .success)],
                       "the row image's request succeeded — from the loopback fixture, over real "
                       + "HTTP, from inside a SCROLLED subtree")

        let scroll = try bnScrollView(root.subviews[0])
        let content = try bnContentView(of: scroll)

        // ── THE IMAGE ────────────────────────────────────────────────────────
        let row0 = content.subviews[imageRow]
        XCTAssertEqual(row0.subviews.count, 1, "row 0 has exactly one child: the image")
        let image = try bnImageIn(row0)
        XCTAssertNotNil(image.image,
                        "THE BYTES LANDED, inside a scrolled, re-parented subtree under the "
                        + "SYNTHETIC content node")
        assertFrame(bnScrollDemoImageFrames, "row image", image,
                    "in the row's coordinates. Both axes declared ⇒ Yoga never called its measure "
                    + "func, so the fixture's own \(Int(fixture.size.width)) × "
                    + "\(Int(fixture.size.height)) is nowhere in this frame")
        XCTAssertTrue(image.frame.width < row0.frame.width && image.frame.height < row0.frame.height,
                      "…and it is strictly SMALLER than its row in both axes, so it cannot overflow "
                      + "and raise a clipping question the two shells would answer differently")

        // ── AND NOW: EVERY NUMBER OF THE 6.2 TABLE, UNCHANGED ────────────────
        // Non-negotiable #2. If one of these moves, the change is wrong. It is the SAME
        // DECLARATION BnScrollDemoTests consumes (BnDemoFrameTables.swift) — so "unchanged"
        // is now a fact about one table read twice, not two transcriptions that agree.
        let f = bnScrollDemoFrames
        assertFrame(f, "viewport", scroll)
        assertFrame(f, "content", content,
                    "THE CONTENT SIZE: still 800 — ten 80-high rows in a height:auto column, "
                    + "computed by Yoga. A child that MEASURED could have grown row 0 and every "
                    + "number after it")
        XCTAssertEqual(scroll.contentSize.height, contentH, accuracy: 0.5,
                       "…and contentSize is still that frame")
        XCTAssertEqual(scroll.contentSize.height - scroll.bounds.height, scrollRange, accuracy: 0.5,
                       "…so the scrollable range is still 800 − 200")

        XCTAssertEqual(content.subviews.count, rows, "still ten rows")
        for i in 0..<rows {
            assertFrame(f, "row \(i)", content.subviews[i], "still at y = 80×\(i), 300 × 80")
        }

        let flexRow = content.subviews[1].subviews[0]
        assertFrame(f, "nested flex row", flexRow)
        assertFrame(f, "nested box A", flexRow.subviews[0])
        assertFrame(f, "nested box B", flexRow.subviews[1], "Grow=1 — still absorbing 300 − 50 − 50")
        assertFrame(f, "nested box C", flexRow.subviews[2])

        let backRow = root.subviews[1]
        assertFrame(f, "back row", backRow, "still starts where the viewport ends (y = 200)")
        XCTAssertEqual(root.frame.height, backRow.frame.maxY, accuracy: 0.5,
                       "the root column still HUGS its two sections")
    }

    private func pollForDemo(deadline seconds: TimeInterval = 30) throws -> UIView {
        let end = Date().addingTimeInterval(seconds)
        while Date() < end {
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.05))
            if let root = host.subviews.first, root.subviews.count == 2, root.frame.height > 0,
               let scroll = root.subviews[0] as? UIScrollView,
               let content = scroll.subviews.compactMap({ $0 as? BnScrollContentView }).first,
               content.subviews.count == rows, content.frame.height > 0 {
                return root
            }
        }
        XCTFail("BnScrollDemo never rendered a laid-out tree within \(Int(seconds))s")
        throw BnRuntimeError.mountFailed(rc: -1, component: "BnScrollDemo")
    }
}
