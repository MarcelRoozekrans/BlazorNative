// ─────────────────────────────────────────────────────────────────────────────
// BnImagePolishDemoTests — Phase 7.5 Gate 3: **THE IMAGE-POLISH DEMO, ON THE
// SIMULATOR** (M7 DoD #6). The iOS twin of `BnImagePolishDemoAndroidTest`, state
// for state and number for number.
//
// Mounts `BnImagePolishDemo` (`/imagepolish`) through the real NativeAOT boot —
// by its registry NAME, the BnModalDemoTests pattern (iOS mounts by name; the
// route table is .NET's and Android's DEEP_LINK line is Android's) — and walks
// the page through its THREE states: everything held / everything but `/slow.png`
// terminated / everything terminated. The difference between the states is the
// phase: **the frames barely differ** (one band moves, once), and that
// near-identity is the proof that a placeholder never measures, an error never
// re-measures, and a mode never consults measure.
//
// ── THE SYNCHRONIZATION GATE COUNTS **EIGHT** ────────────────────────────────
// The page carries EIGHT src-bearing image nodes: [0] slow ×1, [1] missing ×1,
// [2] fixed ×4, [3] missing ×1, [4] intrinsic ×1 — count them off the razor's
// own markup (slow 1 + error 1 + fixed 4 + intrinsic 2 = 8; the header's
// original "seven" was the Gate 1 review's recorded miscount, fixed since). A
// gate that awaited seven would let the AFTER assertions race the eighth
// request. `OnError` joins the evidence — the counted dispatch is case [1]'s —
// but does not replace the gate: success still has no wire signal.
//
// ── WHY SO MANY ASSERTIONS RIDE ONE TEST ─────────────────────────────────────
// The three states are one mount's LIFECYCLE: "the placeholder was painted, then
// the same node's placeholder survived the 404, then the still-held node
// released and cleared" is a statement about ONE page instance, and three mounts
// would assert three different pages. `BnImageDemoTests
// .testTheFrameTablesBEFOREAndAFTERTheBytes` is the shape.
//
// No BnDemoFrameTables entry, deliberately: the drift parser compares tables
// BOTH shells declare, and Android's Gate 2 kept these numbers local for the
// same reason (the modal precedent) — the .NET golden
// (BnImagePolishDemoTests.cs) is the shared source of truth for this page.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit
@testable import BnHost

final class BnImagePolishDemoTests: BnHostTestCase {

    // BnImagePolishDemo.razor's constants (the transcription posture: a device test
    // cannot read a .razor; the outcome assertions against the WIRE's own URLs are
    // the drift pin, and the golden .NET test asserts the same numbers off the wire).
    // The section offsets are the razor's COMPUTED consts, recomputed here from the
    // same parts so a changed band height is one edit on each side.
    private let sectionW: CGFloat = 300
    private let bandH: CGFloat = 20
    private let declaredW: CGFloat = 200
    private let declaredH: CGFloat = 120
    private let echoRowH: CGFloat = 24
    private let modeW: CGFloat = 120
    private let modeH: CGFloat = 60
    private let placeholderHex = "#FFCA28"

    private var loadingH: CGFloat { declaredH + bandH }                    // [0] hugs 140
    private var errorY: CGFloat { loadingH }                               // [1] at 140
    private var errorH: CGFloat { declaredH + bandH + echoRowH }           // [1] hugs 164
    private var quartetY: CGFloat { errorY + errorH }                      // [2] at 304
    private var modeStep: CGFloat { modeH + bandH }                        // 80
    private var quartetH: CGFloat { 4 * modeStep }                         // 320
    private var intrinsicFailingY: CGFloat { quartetY + quartetH }         // [3] at 624
    private var intrinsicLoadingY: CGFloat { intrinsicFailingY + bandH }   // [4] at 644
    private var backYBefore: CGFloat { intrinsicLoadingY + bandH }         // 664; after: +Hi

    /// The eight terminal callbacks the gate awaits — DERIVED from the page's eight
    /// src-bearing nodes (see the file header), never transcribed from prose.
    private let allRequests = 8
    private var allButSlow: Int { allRequests - 1 }

    private let echoInitial = "err:0"
    private let echoAfterOne = "err:1 \(BnImageFixtureServer.MISSING_URL)"

    private var placeholderColor: UIColor { BnColor.parse(placeholderHex)! }

    private var server: BnImageFixtureServer!

    /// Hold the runtime for the test's lifetime so the @convention(c) callback
    /// trampoline is never released mid-render.
    private var runtime: BnRuntime?
    private var host: UIView!
    private var mapper: BnWidgetMapper!

    override func setUpWithError() throws {
        try super.setUpWithError()
        bnClearImageCaches()
        // The close is STRUCTURAL — `started(for:)` registers a teardown block. No
        // blanket errors-empty assert in tearDown (the BnImagePolishDemoAndroidTest
        // posture): the back-nav test navigates while nothing is in flight, but a
        // future edit that navigates over a held response would CANCEL it, and a
        // broken pipe is that cancellation seen from the other end of the socket.
        // The full-table test asserts the list empty itself — nothing there cancels.
        server = try BnImageFixtureServer.started(for: self)
    }

    // ── The mount ─────────────────────────────────────────────────────────────

    @discardableResult
    private func mount() throws -> UIView {
        host = UIView(frame: CGRect(x: 0, y: 0, width: 390, height: 844))
        let mapper = bnMapper(root: host)
        self.mapper = mapper
        let runtime = BnRuntime(mapper: mapper)
        self.runtime = runtime
        runtime.onError = { msg, err in NSLog("[BnImagePolishDemoTests] \(msg): \(err)") }
        try runtime.start(component: "BnImagePolishDemo", os: "ios")
        return try pollForDemo()
    }

    /// Pumps the MAIN runloop until the mount frame has been applied AND laid out:
    /// six sections under the root, and a root with a computed height.
    private func pollForDemo(deadline seconds: TimeInterval = 30) throws -> UIView {
        let end = Date().addingTimeInterval(seconds)
        while Date() < end {
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.05))
            if let root = host.subviews.first, root.subviews.count == 6, root.frame.height > 0 {
                return root
            }
        }
        XCTFail("BnImagePolishDemo never rendered a laid-out tree within \(Int(seconds))s")
        throw BnRuntimeError.mountFailed(rc: -1, component: "BnImagePolishDemo")
    }

    // ── [1] THE FULL TABLE, THROUGH THE THREE STATES ──────────────────────────

    /// The razor header's six cases, each band's y asserted in every state it is
    /// observable in — held / released / slow-released.
    func testTheFrameTableThroughHeldThenReleasedThenSlowReleased() throws {
        // ── THE FIXTURE CONTRACT, on the DECODED bytes, BEFORE ANY FRAME ──────
        let fixed = try server.decoded(server.fixedPng)
        let intrinsic = try server.decoded(server.intrinsicPng)
        let wi = intrinsic.size.width
        let hi = intrinsic.size.height
        XCTAssertTrue(hi > 0, "Hi > 0 — case [4]'s band moves by exactly Hi")
        XCTAssertTrue(wi > 0 && wi <= sectionW, "0 < Wi ≤ 300")
        XCTAssertFalse(fixed.size.width * modeH == fixed.size.height * modeW,
                       "the mode fixture's aspect (\(fixed.size.width):\(fixed.size.height)) must "
                       + "DISAGREE with the 120 × 60 box (2:1) — otherwise the four modes paint "
                       + "identically and the quartet proves nothing about paint")

        let root = try mount()

        // ══ STATE 1: EVERYTHING HELD ══════════════════════════════════════════
        XCTAssertEqual(mapper.imageTerminalCount, 0,
                       "no request has terminated — the fixture server is HOLDING every response "
                       + "(Kingfisher's caches were cleared), which is what makes the held state "
                       + "an assertable STATE rather than a race")
        XCTAssertEqual(root.subviews.count, 6,
                       "six sections: loading, error, quartet, intrinsic-failing, "
                       + "intrinsic-loading, back")

        // [0] PLACEHOLDER-WHILE-LOADING — held, so "while loading" is NOW.
        let s0 = root.subviews[0]
        assertFrame("[0] section HUGS 120 + 20", s0, 0, 0, sectionW, loadingH)
        assertFrame("[0] image: the DECLARED box, while in flight", try bnImageIn(s0),
                    0, 0, declaredW, declaredH)
        try assertPlaceholder("[0] in flight", bnImageIn(s0))
        assertFrame("[0] band L at y = 120", s0.subviews[1], 0, declaredH, sectionW, bandH)

        // [1] ERROR, SPACE KEPT — before the 404 lands, it is just a declared box.
        let s1 = root.subviews[1]
        assertFrame("[1] section at y = 140, HUGS 120 + 20 + 24", s1,
                    0, errorY, sectionW, errorH)
        assertFrame("[1] image: the declared box", try bnImageIn(s1),
                    0, 0, declaredW, declaredH)
        try assertPlaceholder("[1] in flight", bnImageIn(s1))
        assertFrame("[1] band E at y = 120", s1.subviews[1], 0, declaredH, sectionW, bandH)
        assertFrame("[1] the echo row: FIXED height, so the round-trip re-renders text "
                    + "inside a box that cannot move", s1.subviews[2],
                    0, declaredH + bandH, sectionW, echoRowH)
        XCTAssertEqual(echoText(), echoInitial, "[1] the echo's mount state")

        // [2] THE FOUR MODES — the modes are already APPLIED (props ride the mount
        // batch); only the bytes are missing.
        try assertQuartet(root, bytesLanded: false)

        // [3] / [4] INTRINSIC — 0 × 0 with placeholders present: the placeholder does
        // not measure, from both sides.
        let s3 = root.subviews[3]
        assertFrame("[3] section at y = 624: band-only, 20 high", s3,
                    0, intrinsicFailingY, sectionW, bandH)
        assertFrame("[3] image: ZERO — a placeholder never measures", try bnImageIn(s3),
                    0, 0, 0, 0)
        assertFrame("[3] band X at y = 0 in its parent", s3.subviews[1],
                    0, 0, sectionW, bandH)

        let s4 = root.subviews[4]
        assertFrame("[4] section at y = 644: band-only before the bytes", s4,
                    0, intrinsicLoadingY, sectionW, bandH)
        assertFrame("[4] image: ZERO before the bytes", try bnImageIn(s4), 0, 0, 0, 0)
        assertFrame("[4] band I at y = 0 — THE REFLOW HAS NOT HAPPENED", s4.subviews[1],
                    0, 0, sectionW, bandH)

        // [5] the back row, LAST (the only font-measured leaf).
        let back = root.subviews[5]
        XCTAssertEqual(back.frame.minY, backYBefore, accuracy: 0.5,
                       "[5] back row at y = 664 before the reflow")
        XCTAssertEqual((back.subviews.first as? UIButton)?.title(for: .normal), "← Back")

        XCTAssertEqual(mapper.errorDispatchesSent, 0,
                       "nothing dispatched while everything is held")

        // ══ STATE 2: THE GATE OPENS — everything but /slow.png terminates ══════
        //
        // The held window is kept DELIBERATELY SHORT here: `BnImageLoader
        // .downloadTimeout` is 5s (a 6.3 decision this page inherits — an
        // uncancelled held request must die on the OUTCOME, not on the gate), and
        // /slow.png's idle clock has been running since the mount batch. So state 2
        // asserts ONLY the fact that needs /slow.png still open — case [0] held,
        // placeholder painting, frame identical — and everything slow-independent
        // ([1]'s error row, the quartet, [3]/[4], the echo round-trip, the back row)
        // is asserted after releaseSlow, where it is byte-identical anyway: those
        // seven requests are TERMINAL, and the eighth's success touches only [0].
        server.release()
        bnAwaitImageResults(mapper, allButSlow)

        XCTAssertFalse(mapper.imageResults.contains { $0.url == BnImageFixtureServer.SLOW_URL },
                       "[0] is STILL HELD — seven terminals and not one of them the slow "
                       + "path's (a slow entry here means the 5s download timeout beat "
                       + "releaseSlow: the held window grew too long)")
        assertFrame("[0] section: UNCHANGED while still in flight", s0,
                    0, 0, sectionW, loadingH)
        try assertPlaceholder("[0] still in flight, seven terminals later", bnImageIn(s0))
        let s0FrameWhileHeld = try bnImageIn(s0).frame

        // ══ STATE 3: /slow.png RELEASES — and the full AFTER table is read ═════
        server.releaseSlow()
        bnAwaitImageResults(mapper, allRequests)
        XCTAssertTrue(pollUntil(15) { self.echoText() == self.echoAfterOne },
                      "the error round-trip never re-rendered the echo — the dispatch crossed "
                      + "the wire, .NET counted it, and the re-render must come back "
                      + "(got '\(echoText() ?? "nil")')")

        // THE OUTCOMES, against the URLs the WIRE carried — the drift pin on the
        // razor's sources (they alias BnImageDemo's constants + the new /slow.png).
        var outcomes: [BnUrlOutcome: Int] = [:]
        for result in mapper.imageResults {
            outcomes[BnUrlOutcome(result), default: 0] += 1
        }
        XCTAssertEqual(outcomes,
                       [BnUrlOutcome(url: BnImageFixtureServer.FIXED_URL, outcome: .success): 4,
                        BnUrlOutcome(url: BnImageFixtureServer.MISSING_URL, outcome: .error): 2,
                        BnUrlOutcome(url: BnImageFixtureServer.INTRINSIC_URL, outcome: .success): 1,
                        BnUrlOutcome(url: BnImageFixtureServer.SLOW_URL, outcome: .success): 1],
                       "4× fixed SUCCESS + 2× missing ERROR + 1× intrinsic SUCCESS + the "
                       + "released slow SUCCESS — and not one CANCELLED (nothing on this page "
                       + "cancels anything)")

        // THE COUNTED DISPATCH: two failures, ONE attach ([3] is deliberately unbound —
        // attach-iff-HasDelegate as a page-level fact), ONE dispatch.
        XCTAssertEqual(mapper.errorDispatchesSent, 1,
                       "`error` dispatched EXACTLY ONCE: case [1]'s failure, counted on the "
                       + "wire — the unbound failure [3] moved no counter, and the successes "
                       + "moved nothing either")
        XCTAssertEqual(echoText(), echoAfterOne,
                       "…and the echo carries the count AND the payload: the wire's src, verbatim")

        // [1] ERROR, SPACE KEPT — the section's whole frame is IDENTICAL through the
        // failure AND the echo re-render.
        assertFrame("[1] section after the 404 + the echo re-render: IDENTICAL — the box "
                    + "held because it was DECLARED, and the echo row is fixed",
                    s1, 0, errorY, sectionW, errorH)
        assertFrame("[1] image: the declared box, held", try bnImageIn(s1),
                    0, 0, declaredW, declaredH)
        try assertPlaceholder("[1] after the 404 — the ERROR row KEEPS the placeholder",
                              bnImageIn(s1))
        assertFrame("[1] band E: y = 120, identical before/after the failure",
                    s1.subviews[1], 0, declaredH, sectionW, bandH)

        // [2] the quartet, bytes landed: FOUR IDENTICAL FRAMES, four modes.
        try assertQuartet(root, bytesLanded: true)

        // [3] the failing intrinsic: 0 × 0 forever, band never moved.
        assertFrame("[3] section: STILL band-only at y = 624", s3,
                    0, intrinsicFailingY, sectionW, bandH)
        assertFrame("[3] image: STILL zero — a placeholder measured NOTHING through a "
                    + "failure", try bnImageIn(s3), 0, 0, 0, 0)
        assertFrame("[3] band X: y = 0, forever", s3.subviews[1], 0, 0, sectionW, bandH)

        // [4] THE REFLOW — once, by exactly Hi, with a placeholder present.
        assertFrame("[4] section grew by exactly Hi", s4,
                    0, intrinsicLoadingY, sectionW, hi + bandH)
        assertFrame("[4] image: the NATURAL size — the decoded fixture's own "
                    + "\(Int(wi)) × \(Int(hi)) pixels read as points", try bnImageIn(s4),
                    0, 0, wi, hi)
        XCTAssertNil(try bnImageIn(s4).bnPlaceholderColor,
                     "[4] …and the bytes replaced the placeholder")
        XCTAssertNotNil(try bnImageIn(s4).image)
        assertFrame("[4] THE REFLOW PROOF: band I moved 0 → Hi, once", s4.subviews[1],
                    0, hi, sectionW, bandH)

        // [5] the back row: the page's ONLY moving number, moved by Hi.
        XCTAssertEqual(root.subviews[5].frame.minY, backYBefore + hi, accuracy: 0.5,
                       "[5] back row at 664 + Hi")

        // [0] AFTER releaseSlow — the SUCCESS row clears the placeholder, and the
        // frame is byte-for-byte what it was while held.
        let s0Image = try bnImageIn(s0)
        XCTAssertNil(s0Image.bnPlaceholderColor,
                     "[0] SUCCESS CLEARED the placeholder: the bytes are the paint now "
                     + "(letterbox bars show BackgroundColor, never the placeholder)")
        XCTAssertNotNil(s0Image.image)
        XCTAssertEqual(s0Image.bnNaturalSize,
                       CGSize(width: BnImageFixtureServer.FIXED_W,
                              height: BnImageFixtureServer.FIXED_H),
                       "[0] …and the held response was OURS: the 64 × 48 fixed bytes (the "
                       + "razor's stated Gates 2/3 contract — the box is declared, so only "
                       + "this pin can see which bytes arrived)")
        XCTAssertEqual(s0Image.frame, s0FrameWhileHeld,
                       "[0] the image's frame is IDENTICAL, number for number, to the one it "
                       + "held while in flight — the placeholder never bought or cost a pt")
        assertFrame("[0] section: unchanged through all three states", s0,
                    0, 0, sectionW, loadingH)
        assertFrame("[0] image: still the declared box", s0Image, 0, 0, declaredW, declaredH)
        assertFrame("[0] band L: still y = 120", s0.subviews[1],
                    0, declaredH, sectionW, bandH)

        XCTAssertEqual(mapper.errorDispatchesSent, 1,
                       "STILL exactly one dispatch — a success dispatches nothing")
        XCTAssertEqual(echoText(), echoAfterOne, "…and the echo never moved again")
        XCTAssertEqual(mapper.inFlightImageCount, 0, "nothing is left in flight")

        XCTAssertEqual(server.errors, [],
                       "NOTHING ON THIS TEST CANCELS ANYTHING — a failed server write here is "
                       + "a real server bug, not a dropped client")
    }

    // ── [2] the back row, by ORACLE ───────────────────────────────────────────

    /// The page's only measured leaf, deliberately LAST — by oracle, the
    /// BnImageDemoTests rule (no font constant is anyone's to invent).
    func testTheBackRowIsThePagesOnlyMeasuredLeaf() throws {
        server.release()
        server.releaseSlow()
        let root = try mount()
        bnAwaitImageResults(mapper, allRequests)

        let backSection = root.subviews[5]
        let back = try XCTUnwrap(backSection.subviews.first as? UIButton,
                                 "the back leaf must be a UIButton")
        XCTAssertEqual(back.title(for: .normal), "← Back")
        XCTAssertEqual(backSection.frame.width, sectionW, accuracy: 0.5,
                       "the back row is 300pt wide")
        XCTAssertEqual(backSection.frame.height, back.frame.height, accuracy: 0.5,
                       "it declares no height and HUGS the button's MEASURED height")
        assertOracle("the measured back button", back, availableWidth: backSection.frame.width)
    }

    // ── [3] back-nav: "← Back" → "/" over the REAL wire ───────────────────────

    /// Nav parity (the razor's case [5]): the button's click rides dispatch_event →
    /// NativeAOT → INavigationManager → "/" → BnDemo remounts — the same lane every
    /// other page's back row uses, proven on THIS page because navigation is what
    /// purges a page full of image nodes (all terminated first, so the purge cancels
    /// nothing and the swap is pure).
    func testBackNavigationReturnsToBnDemoOverTheRealWire() throws {
        server.release()
        server.releaseSlow()
        let root = try mount()
        bnAwaitImageResults(mapper, allRequests) // navigate over a QUIET page

        let back = try XCTUnwrap(findButton(in: root, title: "← Back"))
        back.sendActions(for: .touchUpInside)

        // BnDemo's shape: root's single child, a plain container with ≥ 6 children
        // whose second is the bound input (the BnInteractionTests accessors).
        XCTAssertTrue(pollUntil(15) {
            guard let page = self.host.subviews.first,
                  type(of: page) == UIView.self,
                  page.subviews.count >= 6 else { return false }
            return page.subviews[1] is UITextField
        }, "BnDemo never remounted after ← Back")
        XCTAssertFalse(containsImageView(host),
                       "…and the /imagepolish tree left the screen whole — navigation names "
                       + "the page, and the subtree purge takes every image node with it")
    }

    // ── Tree accessors + helpers ──────────────────────────────────────────────

    /// [1]'s echo: section 1, child 2 (the fixed-height BnView row), child 0 (BnText's
    /// UILabel — a `view` parent is a container, so the text node materializes its own).
    private func echoText() -> String? {
        guard let root = host.subviews.first, root.subviews.count == 6 else { return nil }
        return root.subviews[1].subviews[2].subviews.first.flatMap { $0 as? UILabel }?.text
    }

    /// The view-state pin: the paint IS the placeholder, in exactly the razor's color.
    private func assertPlaceholder(_ whenWhat: String, _ image: BnImageView,
                                   file: StaticString = #filePath, line: UInt = #line) {
        XCTAssertEqual(image.bnPlaceholderColor, placeholderColor,
                       "the placeholder must be painted \(whenWhat) — in the razor's "
                       + "PlaceholderHex; got \(String(describing: image.bnPlaceholderColor))",
                       file: file, line: line)
    }

    /// **[2] THE FOUR MODES** — THE assertion: four IDENTICAL layout frames (same
    /// x/w/h, y's in fixed 60+20 steps) under four DIFFERENT modes, bands pinned; the
    /// per-node `contentMode` + `clipsToBounds` pins carry the paint half (the 7.4
    /// finding-4 lesson: assert the wiring where the paint cannot be synthesized).
    /// Identical whether the bytes have landed or not — the frames belong to Yoga,
    /// and Yoga never heard about the mode.
    private func assertQuartet(_ root: UIView, bytesLanded: Bool,
                               file: StaticString = #filePath, line: UInt = #line) throws {
        let s2 = root.subviews[2]
        assertFrame("[2] quartet section at y = 304, hugging 4 × 80", s2,
                    0, quartetY, sectionW, quartetH, file: file, line: line)
        XCTAssertEqual(s2.subviews.count, 8, "four images and four bands, alternating",
                       file: file, line: line)
        let expected: [UIView.ContentMode] = [
            .scaleAspectFit,   // contain — the default, spelled out
            .scaleAspectFill,  // cover
            .scaleToFill,      // stretch
            .center,           // center
        ]
        for i in 0..<4 {
            let img = try XCTUnwrap(s2.subviews[2 * i] as? BnImageView,
                                    "[2] slot \(i) must be an image", file: file, line: line)
            assertFrame("[2] mode \(expected[i]): frame #\(i) is the SAME 120 × 60 box at "
                        + "y = \(Int(CGFloat(i) * modeStep)) — the Yoga box never changes "
                        + "with mode", img, 0, CGFloat(i) * modeStep, modeW, modeH,
                        file: file, line: line)
            XCTAssertEqual(img.contentMode, expected[i],
                           "[2] …under contentMode \(expected[i])", file: file, line: line)
            XCTAssertTrue(img.clipsToBounds,
                          "[2] …with the paint clipped to the box (Cover/Center bleed over "
                          + "the bands otherwise)", file: file, line: line)
            if bytesLanded {
                XCTAssertNotNil(img.image, "[2] …with the bytes painted", file: file, line: line)
            }
            assertFrame("[2] band #\(i) at y = \(Int(modeH + CGFloat(i) * modeStep))",
                        s2.subviews[2 * i + 1],
                        0, modeH + CGFloat(i) * modeStep, sectionW, bandH,
                        file: file, line: line)
        }
    }

    private func pollUntil(_ seconds: TimeInterval, _ cond: () -> Bool) -> Bool {
        let end = Date().addingTimeInterval(seconds)
        while Date() < end {
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.05))
            if cond() { return true }
        }
        return cond()
    }

    private func findButton(in view: UIView, title: String) -> UIButton? {
        if let b = view as? UIButton, b.title(for: .normal) == title { return b }
        for sub in view.subviews {
            if let f = findButton(in: sub, title: title) { return f }
        }
        return nil
    }

    private func containsImageView(_ view: UIView) -> Bool {
        if view is BnImageView { return true }
        return view.subviews.contains { containsImageView($0) }
    }
}
