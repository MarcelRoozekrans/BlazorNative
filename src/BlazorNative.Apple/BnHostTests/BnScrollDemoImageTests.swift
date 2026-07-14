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
// That class does not stand up the fixture server, so its row-0 image **fails to load**
// (connection refused, immediately, offline). It asserts the same table and still
// passes — which is not an accident to be tidied away but the *other half of the
// proof*: **a failed load moves nothing either**, because the image's size is definite
// and there is no measurement for a failure to change. Between the two classes, the 6.2
// table is pinned against an image that loaded and against one that did not.
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

    // BnScrollDemo.cs's constants and the two products it COMPUTES from them.
    private let rows = 10
    private let rowH: CGFloat = 80
    private let viewW: CGFloat = 300
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
        bnClearImageCaches()
        server = try BnImageFixtureServer()
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
        let errors = server?.errors ?? []
        server?.close()
        server = nil
        XCTAssertEqual(errors, [],
                       "the fixture server failed a write, and NOTHING ON THIS PAGE CANCELS "
                       + "ANYTHING — so it is a real server bug rather than a dropped client")
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
        assertFrame("the row image: (0, 0, 40, 40) in the row's coordinates. Both axes declared ⇒ "
                    + "Yoga never called its measure func, so the fixture's own "
                    + "\(Int(fixture.size.width)) × \(Int(fixture.size.height)) is nowhere in this "
                    + "frame", image, 0, 0, imageW, imageH)
        XCTAssertTrue(image.frame.width < row0.frame.width && image.frame.height < row0.frame.height,
                      "…and it is strictly SMALLER than its row in both axes, so it cannot overflow "
                      + "and raise a clipping question the two shells would answer differently")

        // ── AND NOW: EVERY NUMBER OF THE 6.2 TABLE, UNCHANGED ────────────────
        // Non-negotiable #2. If one of these moves, the change is wrong.
        assertFrame("the viewport", scroll, 0, 0, viewW, viewH)
        assertFrame("THE CONTENT SIZE: still 800 — ten 80-high rows in a height:auto column, "
                    + "computed by Yoga. A child that MEASURED could have grown row 0 and every "
                    + "number after it", content, 0, 0, viewW, contentH)
        XCTAssertEqual(scroll.contentSize.height, contentH, accuracy: 0.5,
                       "…and contentSize is still that frame")
        XCTAssertEqual(scroll.contentSize.height - scroll.bounds.height, scrollRange, accuracy: 0.5,
                       "…so the scrollable range is still 800 − 200")

        XCTAssertEqual(content.subviews.count, rows, "still ten rows")
        for i in 0..<rows {
            assertFrame("row \(i) is still at y = 80×\(i), 300 × 80",
                        content.subviews[i], 0, rowH * CGFloat(i), viewW, rowH)
        }

        let flexRow = content.subviews[1].subviews[0]
        assertFrame("the nested flex row", flexRow, 0, 0, viewW, rowH)
        assertFrame("box A", flexRow.subviews[0], 0, 0, 50, rowH)
        assertFrame("box B (Grow=1) — still absorbing 300 − 50 − 50",
                    flexRow.subviews[1], 50, 0, 200, rowH)
        assertFrame("box C", flexRow.subviews[2], 250, 0, 50, rowH)

        let backRow = root.subviews[1]
        XCTAssertEqual(backRow.frame.minY, viewH, accuracy: 0.5,
                       "the back row still starts where the viewport ends (y = 200)")
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
