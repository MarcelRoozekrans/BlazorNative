// ─────────────────────────────────────────────────────────────────────────────
// BnImagePolishMapperTests — Phase 7.5 Gate 3: **THE THREE POLISH FEATURES, AT
// THE MAPPER LEVEL.** The iOS twin of Kotlin's `WidgetMapperImagePolishTest`, row
// for row — and then the tests only THIS shell can stage at all.
//
// The design's three normative tables — the placeholder STATE table (decision 1),
// the dispatch DISCIPLINE (decision 2), the mode TABLE (decision 3) — asserted
// against synthetic frames, exactly where this suite already asserts `UpdateProp`
// behaviour (`BnImageTests` is the template; `/imagepolish` demonstrates the same
// rows on a real page). The rows with no demo affordance live ONLY here: src →
// null with a placeholder present, an unknown mode word (unrepresentable from the
// component), a detached error wire, a cancelled request's non-dispatch staged
// two ways — and **the synchronous in-batch failure**, which is iOS's own
// (`URL(string:)` → nil terminates inside `UpdateProp("src")`, inside the batch,
// BY CONSTRUCTION — Android has no deterministic path there and pins the DEFER
// row on the JVM decision table instead; the design's risk-table row names this
// split).
//
// The phase's one sentence, restated as this file's throughline: **every
// assertion below re-reads a 6.3 frame and finds it unchanged.** A placeholder
// never measures (it is a bounds-tracking paint, not the measure func's input —
// `BnImageView.bnShowPlaceholder`'s header), an error never re-measures, a mode
// never consults measure. The frames are the 6.3 numbers, verbatim.
//
// ── THE THREE END-TO-END DEFER PINS (the tests only iOS can stage) ────────────
// The nil-URL failure terminates synchronously with the batch OPEN, so ordinary
// two-patch frames become adversarial orderings no other suite can produce:
//
//  1. failure mid-batch, nothing after → the dispatch arrives on a FRESH main
//     turn (never inside the batch — the event sink reads `isApplyingBatch` at
//     delivery), exactly once. DEFERRED, NEVER DROPPED.
//  2. failure mid-batch, the NODE IS REMOVED later in the same batch → ZERO
//     dispatches: the deferred turn RE-ASKS the liveness decision at fire time
//     and finds the node purged.
//  3. failure mid-batch, the SRC CHANGES later in the same batch → ZERO
//     dispatches for the superseded source (its generation is stale at fire
//     time), and the new source's own lifecycle is untouched.
//
// A deferred dispatch that replayed a decision-time capture passes 1 and fails
// 2 and 3 — which is exactly why 2 and 3 exist.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit
@testable import BnHost

final class BnImagePolishMapperTests: BnHostTestCase {

    private var server: BnImageFixtureServer!

    private let section: Int32 = 1
    private let image: Int32 = 2
    private let band: Int32 = 3
    private let sectionW: CGFloat = 300
    private let bandH: CGFloat = 20
    private let errorHandler: Int32 = 77

    /// BnImagePolishDemo.razor's `PlaceholderHex` — transcribed the way sectionW is
    /// (a device test cannot read a .razor; the DEMO test asserts the same value off
    /// the page the wire actually built, which is the drift pin).
    private let placeholderHex = "#FFCA28"

    /// A `src` that `URL(string:)` REJECTS — the synchronous-failure path's key, and
    /// it has to be a STRUCTURAL violation: Foundation's lenient parser percent-encodes
    /// mere garbage ("not a url at all" parses — the recorded 6.3 finding), but a
    /// non-numeric PORT cannot be encoded into legality. Every test that uses it
    /// asserts the nil-parse as its own precondition, so a Foundation that starts
    /// accepting it fails BY NAME here instead of silently un-staging the defer path.
    private let unparseableSrc = "http://127.0.0.1:notaport/never.png"

    private var placeholderColor: UIColor { BnColor.parse(placeholderHex)! }

    /// Every recorded `error` delivery: the wire triple plus WHERE it was delivered
    /// (`isApplyingBatch` at delivery time — the defer rule's honest witness; see
    /// `BnWidgetMapper.isApplyingBatch` for why an ordering assertion is not one:
    /// the main queue's drain can run a deferred block before the test regains
    /// control).
    private struct Delivery: Equatable {
        let handlerId: Int32
        let eventName: String
        let payload: String?
        let insideBatch: Bool
    }

    override func setUpWithError() throws {
        try super.setUpWithError()
        bnClearImageCaches()
        // The close is STRUCTURAL — `started(for:)` registers a teardown block. This
        // class's server errors are deliberately NOT asserted empty: the cancellation
        // tests DO drop connections, which is cancellation seen from the other end of
        // the socket (the BnImageTests posture).
        server = try BnImageFixtureServer.started(for: self)
    }

    // ── Decision 1: the placeholder state table, row by row ──────────────────

    func testThePlaceholderPaintsWhileInFlightAndSuccessCLEARSIt() throws {
        let host = makeSection(src: BnImageFixtureServer.SLOW_URL, declared: true)

        XCTAssertTrue(server.awaitPath("/slow.png"),
                      "the request must be genuinely IN FLIGHT — a placeholder asserted before "
                      + "the request started proves nothing about the IN-FLIGHT row")
        // THE IN-FLIGHT ROW: the placeholder color fills the box — as PAINT inside the
        // box Yoga already gave the node, never as size.
        try assertPlaceholderPainted("in flight", imageView(host))
        assertFrame("the declared box, while in flight — the placeholder never bought a pt",
                    try imageView(host), 0, 0, 200, 120)
        assertFrame("band under it: y = 120, the declared height", bandView(host),
                    0, 120, sectionW, bandH)

        server.releaseSlow()
        bnAwaitImageResults(host.mapper, 1)
        XCTAssertEqual(host.mapper.imageResults.map { $0.outcome }, [.success],
                       "the held response terminated as SUCCESS")

        // THE SUCCESS ROW: the placeholder is CLEARED — the bytes are the LAST write.
        // Letterbox bars (Contain, 64 × 48 in a 200 × 120 box) show the view
        // BACKGROUND, never the placeholder: the paint is gone.
        let img = try imageView(host)
        XCTAssertNil(img.bnPlaceholderColor,
                     "the placeholder must be CLEARED by SUCCESS — a surviving paint here is "
                     + "the state table's SUCCESS row broken")
        XCTAssertNotNil(img.image, "…because the BYTES replaced it")
        // …and the which-bytes pin: /slow.png serves the 64 × 48 FIXED fixture's bytes
        // (the razor's stated Gates 2/3 contract). The box is DECLARED, so only the
        // recorded natural size can see which bytes the held response carried.
        XCTAssertEqual(img.bnNaturalSize, CGSize(width: BnImageFixtureServer.FIXED_W,
                                                 height: BnImageFixtureServer.FIXED_H),
                       "the held response served the 64 × 48 fixed bytes — OURS")
        assertFrame("…and the frame did not move by a hair: paint, never size",
                    img, 0, 0, 200, 120)
        assertFrame("…nor the band's y", bandView(host), 0, 120, sectionW, bandH)
    }

    func testThePlaceholderSTAYSOnErrorAndTheDeclaredBoxHolds() throws {
        let host = makeSection(src: BnImageFixtureServer.MISSING_URL, declared: true)
        server.release()
        bnAwaitImageResults(host.mapper, 1)
        XCTAssertEqual(host.mapper.imageResults.map { $0.outcome }, [.error], "a REAL 404")

        // THE ERROR ROW: the placeholder STAYS — it is the error state's visual.
        try assertPlaceholderPainted("after the 404", imageView(host))
        assertFrame("the declared box HOLDS — because it was DECLARED, not because it "
                    + "failed: Yoga never called its measure func, so the failure cannot "
                    + "move this frame even in principle",
                    try imageView(host), 0, 0, 200, 120)
        assertFrame("band y = 120, identical before and after the failure",
                    bandView(host), 0, 120, sectionW, bandH)
        XCTAssertEqual(host.mapper.imageTerminalCount, 1, "…and it did not retry")
    }

    func testSrcToNullClearsThePlaceholderWithTheImageAndDispatchesNothing() throws {
        let host = makeSection(src: BnImageFixtureServer.SLOW_URL, declared: true,
                               attachError: errorHandler)
        XCTAssertTrue(server.awaitPath("/slow.png"), "the request never reached the server")
        try assertPlaceholderPainted("in flight, before the clear", imageView(host))

        // THE `src → null` ROW: no source names no pending image — the placeholder is
        // cleared WITH the image (the 6.3 clear), not left behind as a ghost of a load
        // that no longer exists.
        host.render([.updateProp(nodeId: image, name: "src", value: nil)])
        XCTAssertNil(try imageView(host).bnPlaceholderColor,
                     "the placeholder went with the src it was waiting for")

        // …and the cancel the clear caused dispatches NOTHING, even with the error
        // wire attached: CANCELLED is not an error (decision 2).
        server.releaseSlow()
        bnAwaitImageResults(host.mapper, 1)
        bnSettle()
        XCTAssertEqual(host.mapper.imageResults.map { $0.outcome }, [.cancelled])
        XCTAssertEqual(host.mapper.errorDispatchesSent, 0,
                       "a clear NEVER dispatches `error` — a cancel is the author's own act, "
                       + "not a failure to report back")
    }

    func testAnIntrinsicPlaceholderNeverMeasuresTheFailingSide() throws {
        let host = makeSection(src: BnImageFixtureServer.MISSING_URL, declared: false)

        // BEFORE: 0 × 0 — the placeholder is a 0 × 0 paint, invisible, CORRECT, and
        // not diagnosed (a zero-sized paint is a no-op, not an error). If a
        // placeholder measured as ANYTHING, this band moves: decision 1's red line.
        assertFrame("intrinsic + placeholder, BEFORE: still ZERO — the placeholder does "
                    + "not measure", try imageView(host), 0, 0, 0, 0)
        assertFrame("band at y = 0", bandView(host), 0, 0, sectionW, bandH)

        server.release()
        bnAwaitImageResults(host.mapper, 1)
        XCTAssertEqual(host.mapper.imageResults.map { $0.outcome }, [.error])
        assertFrame("AFTER the 404: STILL zero — the failure reserved nothing, with a "
                    + "placeholder present exactly as without one (6.3's failure row, "
                    + "re-run against the feature it was afraid of)",
                    try imageView(host), 0, 0, 0, 0)
        assertFrame("band at y = 0, forever", bandView(host), 0, 0, sectionW, bandH)
    }

    func testAnIntrinsicPlaceholderStillReflowsExactlyONCETheLoadingSide() throws {
        let fixture = try server.decoded(server.intrinsicPng)
        let wi = fixture.size.width
        let hi = fixture.size.height
        XCTAssertTrue(hi > 0, "Hi > 0 — Hi IS the reflow")

        let host = makeSection(src: BnImageFixtureServer.INTRINSIC_URL, declared: false)
        assertFrame("BEFORE: 0 × 0 with a placeholder present", try imageView(host), 0, 0, 0, 0)
        assertFrame("band at y = 0", bandView(host), 0, 0, sectionW, bandH)

        server.release()
        bnAwaitImageResults(host.mapper, 1)
        assertFrame("AFTER: the NATURAL size — the placeholder changed nothing about the "
                    + "6.3 measurement contract", try imageView(host), 0, 0, wi, hi)
        assertFrame("THE REFLOW, exactly once: band y 0 → Hi", bandView(host),
                    0, hi, sectionW, bandH)
        XCTAssertNil(try imageView(host).bnPlaceholderColor,
                     "…and the bytes replaced the placeholder")
        XCTAssertNotNil(try imageView(host).image)
    }

    // ── Decision 3: the mode table, on the widget ─────────────────────────────

    func testContentModeMapsTheFourStrictWordsToTheFourContentModes() throws {
        // Four images, one per wire word, same declared 120 × 60 box, stacked in one
        // 300-wide section — the quartet's mapper-level half. The DEMO asserts the four
        // identical frames through the real wire; here the per-word `UIView.ContentMode`
        // spelling is pinned (the design's mutation: swap the mode map → these red).
        let modes: [(String, UIView.ContentMode)] = [
            ("contain", .scaleAspectFit),
            ("cover", .scaleAspectFill),
            ("stretch", .scaleToFill),
            ("center", .center),
        ]
        let host = BnSyntheticHost()
        var patches: [BnPatch] = [
            bnCreate(section, "view", nil),
            bnStyle(section, "width", "300"),
            bnStyle(section, "alignItems", "flex-start"),
        ]
        for (i, (word, _)) in modes.enumerated() {
            let id = Int32(10 + i)
            patches.append(bnCreate(id, "image", section))
            patches.append(bnStyle(id, "width", "120"))
            patches.append(bnStyle(id, "height", "60"))
            patches.append(.updateProp(nodeId: id, name: "src",
                                       value: BnImageFixtureServer.FIXED_URL))
            patches.append(.updateProp(nodeId: id, name: "contentMode", value: word))
        }
        host.render(patches)
        server.release()
        bnAwaitImageResults(host.mapper, 4)

        let sectionView = host.root.subviews[0]
        for (i, (word, mode)) in modes.enumerated() {
            let img = try XCTUnwrap(sectionView.subviews[i] as? BnImageView,
                                    "quartet slot \(i) must be a BnImageView")
            XCTAssertEqual(img.contentMode, mode,
                           "mode '\(word)' → \(mode) — the table's iOS spelling, per word "
                           + "(a collapsed arm reddens all four)")
            // THE PARITY RULE: four identical layout frames under four modes — the Yoga
            // box never changes with mode. Mode is PAINT-ONLY.
            assertFrame("mode '\(word)': the frame is the declared 120 × 60 at y = "
                        + "\(i * 60) — an IDENTICAL box under a different mode",
                        img, 0, CGFloat(i) * 60, 120, 60)
            // …and the corollary that makes Cover/Center honest paint: the overdraw is
            // CLIPPED to the box (the design's named rule — Android clips by
            // construction; iOS only because [makeView] says so).
            XCTAssertTrue(img.clipsToBounds,
                          "mode '\(word)': clipsToBounds must be TRUE — Cover/Center paint "
                          + "past the Yoga box otherwise, over the very bands the table pins")
            XCTAssertNotNil(img.image, "…with the bytes painted")
        }
    }

    func testAnUnknownContentModeIsDiagnosedAndNOTApplied() throws {
        let host = makeSection(src: BnImageFixtureServer.FIXED_URL, declared: true)
        host.render([.updateProp(nodeId: image, name: "contentMode", value: "cover")])
        XCTAssertEqual(try imageView(host).contentMode, .scaleAspectFill)

        // Reachable by hand-rolled wire only (the .NET enum cannot write it) — and the
        // node KEEPS its current mode: a guessed fallback is how two shells guess
        // differently.
        host.render([.updateProp(nodeId: image, name: "contentMode", value: "fill")])
        XCTAssertEqual(try imageView(host).contentMode, .scaleAspectFill,
                       "the unknown word applied NOTHING — the node keeps cover")
        XCTAssertTrue(host.mapper.scrollDiagnostics.contains { $0.contains("contentMode 'fill'") },
                      "…and the ignore is DIAGNOSED where a test can read it (the modal "
                      + "style-ignore precedent): NSLog is not an assertion surface, and this "
                      + "failure is invisible on every frame table by the mode-invariance rule "
                      + "itself. Got: \(host.mapper.scrollDiagnostics)")

        server.release()
        bnAwaitImageResults(host.mapper, 1) // hygiene: let the fixture request terminate
    }

    func testContentModeNullRestoresTheDefaultScaleAspectFit() throws {
        let host = makeSection(src: BnImageFixtureServer.FIXED_URL, declared: true)
        host.render([.updateProp(nodeId: image, name: "contentMode", value: "stretch")])
        XCTAssertEqual(try imageView(host).contentMode, .scaleToFill)

        // The Enabled-null precedent: null on the prop wire means "the author took the
        // parameter away", and what it restores is the DEFAULT — contain, the 6.3
        // row's value, now named.
        host.render([.updateProp(nodeId: image, name: "contentMode", value: nil)])
        XCTAssertEqual(try imageView(host).contentMode, .scaleAspectFit,
                       "contentMode → null restores the default: .scaleAspectFit (contain)")

        server.release()
        bnAwaitImageResults(host.mapper, 1)
    }

    /// **THE CREATION-PROPERTY PIN** — the design's named iOS mutation ("drop
    /// `clipsToBounds` → the creation-property pin red — the achievable discriminator:
    /// hosted XCTest cannot synthesize paint, 7.4 finding 4"). Asserted at CREATION,
    /// before any prop has touched the node, because the rule is "always, at creation"
    /// — not "once a mode arrives".
    func testAnImageClipsItsPaintToTheYogaBoxFromCreation() throws {
        let host = bnRender([bnCreate(1, "image", nil)])
        let img = try XCTUnwrap(host.root.subviews[0] as? BnImageView)
        XCTAssertTrue(img.clipsToBounds,
                      "clipsToBounds must be TRUE from [makeView] — `.scaleAspectFill` and "
                      + "`.center` paint BIGGER than the box, iOS does not clip on its own "
                      + "(Android's ImageView does, by construction), and the bleed is over "
                      + "SIBLINGS — invisible to every frame assertion, which is why it is "
                      + "pinned as a property")
        XCTAssertEqual(img.contentMode, .scaleAspectFit,
                       "…and the default mode is aspect-fit — the 6.3 row, which 7.5's "
                       + "`contain` default now NAMES (deliberately not RN's `cover`)")
    }

    // ── Decision 2: the error wire ────────────────────────────────────────────

    func testAFailureDispatchesTheWIRESrcExactlyOnceIntoTheAttachedHandler() throws {
        var deliveries: [Delivery] = []
        let host = makeSection(src: BnImageFixtureServer.MISSING_URL, declared: true,
                               attachError: errorHandler)
        host.mapper.onUiEvent = { [weak mapper = host.mapper] handlerId, name, payload in
            deliveries.append(Delivery(handlerId: handlerId, eventName: name, payload: payload,
                                       insideBatch: mapper?.isApplyingBatch ?? false))
        }

        server.release()
        bnAwaitImageResults(host.mapper, 1)
        bnSettle() // a dispatch that must arrive exactly ONCE gets every chance to double

        XCTAssertEqual(deliveries,
                       [Delivery(handlerId: errorHandler, eventName: "error",
                                 payload: BnImageFixtureServer.MISSING_URL, insideBatch: false)],
                       "EXACTLY ONE dispatch: the event name is `error`, the payload is the "
                       + "WIRE's src, VERBATIM — the URL is the only fact two loaders share "
                       + "about the same failure, so it is the only payload two shells can "
                       + "dispatch identically")
        XCTAssertEqual(host.mapper.errorDispatchesSent, 1,
                       "…and the counter agrees (the device page's only honest observation "
                       + "point — an /imagepolish dispatch that doubled would move no frame)")
    }

    func testAnUnboundFailureDispatchesNOTHING() throws {
        let host = makeSection(src: BnImageFixtureServer.MISSING_URL, declared: true)
        server.release()
        bnAwaitImageResults(host.mapper, 1)
        bnSettle()
        XCTAssertEqual(host.mapper.errorDispatchesSent, 0,
                       "no attach means no wire — attach-iff-HasDelegate's shell half: the "
                       + "failure stays what it always was, a logged, painted-nothing 404")
    }

    func testADetachedErrorWireDispatchesNOTHING() throws {
        let host = makeSection(src: BnImageFixtureServer.MISSING_URL, declared: true,
                               attachError: errorHandler)
        host.render([.detachEvent(nodeId: image, handlerId: errorHandler, eventName: "error")])
        server.release()
        bnAwaitImageResults(host.mapper, 1)
        bnSettle()
        XCTAssertEqual(host.mapper.errorDispatchesSent, 0,
                       "the detach killed the wire before the failure terminated — the attach "
                       + "arm's mirror (the 3.3 symmetric-arms rule)")
    }

    func testASrcChangeCancelsAndTheCancellationDispatchesNOTHING() throws {
        let host = makeSection(src: BnImageFixtureServer.SLOW_URL, declared: true,
                               attachError: errorHandler)
        server.release() // ordinary paths answer; /slow.png held on its own gate
        XCTAssertTrue(server.awaitPath("/slow.png"), "the request never reached the server")

        host.render([.updateProp(nodeId: image, name: "src",
                                 value: BnImageFixtureServer.INTRINSIC_URL)])
        bnAwaitImageResults(host.mapper, 2)
        server.releaseSlow()
        bnSettle()

        XCTAssertEqual(Set(host.mapper.imageResults.map { BnUrlOutcome($0) }),
                       [BnUrlOutcome(url: BnImageFixtureServer.SLOW_URL, outcome: .cancelled),
                        BnUrlOutcome(url: BnImageFixtureServer.INTRINSIC_URL, outcome: .success)],
                       "the in-flight request was CANCELLED and the new one succeeded")
        XCTAssertEqual(host.mapper.errorDispatchesSent, 0,
                       "CANCELLED IS NOT AN ERROR: a Src change dispatches nothing, with the "
                       + "wire attached and live the whole time")
    }

    func testNodeRemovalCancelsAndTheCancellationDispatchesNOTHING() throws {
        let host = makeSection(src: BnImageFixtureServer.SLOW_URL, declared: true,
                               attachError: errorHandler)
        XCTAssertTrue(server.awaitPath("/slow.png"), "the request never reached the server")

        host.render([.removeNode(nodeId: section)])
        bnAwaitImageResults(host.mapper, 1)
        server.releaseSlow()
        bnSettle()

        XCTAssertEqual(host.mapper.imageResults.map { $0.outcome }, [.cancelled])
        XCTAssertEqual(host.mapper.errorDispatchesSent, 0,
                       "node removal dispatches nothing — and could not even if it tried: the "
                       + "purge took the error wire with the node (ids restart; a wire that "
                       + "outlived its node would answer for the next node to inherit the id)")
    }

    // ── iOS's own: the SYNCHRONOUS in-batch failure, end to end ───────────────

    /// **THE DEFER ROW, LIVE** — the test the design names for this shell ("the
    /// nil-`URL` synchronous failure reaching the same deferred dispatch site"), and
    /// the required mutation's red surface ("dispatch the nil-URL failure synchronously
    /// inside the batch → the re-entrancy/defer test red").
    func testTheNilURLFailureInsideABatchDefersTheDispatchToAFreshTurnExactlyOnce() throws {
        XCTAssertNil(URL(string: unparseableSrc),
                     "PRECONDITION: '\(unparseableSrc)' must be a URL Foundation REJECTS "
                     + "(a structural violation — a non-numeric port — that lenient "
                     + "percent-encoding cannot repair). If this ever parses, the synchronous "
                     + "failure path is un-staged and this suite must find a new key, loudly, "
                     + "here — not by quietly proving nothing.")

        var deliveries: [Delivery] = []
        // The wire is attached in an EARLIER batch, deliberately: this test stages the
        // PURE defer row — a failure while a wire is LIVE and a batch is OPEN, nothing
        // else in question. The mount-order shape (src seq 24 BEFORE the attach seq 27
        // in ONE batch) has its own end-to-end pin below
        // (testAMountBatchAttachingTheWireAFTERTheBadSrc…): mid-batch the handler
        // question is unsettled, so the decision DEFERS and the fire-time re-ask finds
        // the attach landed — dispatching post-batch, matching Android, whose
        // mount-time failure lands after the whole batch and always dispatched.
        // (Gate 3 review I-1: the old table DROPped there — a parity break this
        // comment used to mis-record as "matching Android".)
        let host = makeSection(src: BnImageFixtureServer.SLOW_URL, declared: true,
                               attachError: errorHandler)
        host.mapper.onUiEvent = { [weak mapper = host.mapper] handlerId, name, payload in
            deliveries.append(Delivery(handlerId: handlerId, eventName: name, payload: payload,
                                       insideBatch: mapper?.isApplyingBatch ?? false))
        }

        // THE FAILURE, SYNCHRONOUS AND IN-BATCH BY CONSTRUCTION: `URL(string:)` → nil
        // terminates inside `UpdateProp("src")`, inside applyBatch — the old /slow.png
        // request is cancelled by the same write.
        host.render([.updateProp(nodeId: image, name: "src", value: unparseableSrc)])
        bnSettle()

        XCTAssertEqual(deliveries,
                       [Delivery(handlerId: errorHandler, eventName: "error",
                                 payload: unparseableSrc, insideBatch: false)],
                       "DEFERRED, NEVER DROPPED — and never inside the batch: the one dispatch "
                       + "carries the wire's src VERBATIM (the unparseable string itself; "
                       + "nothing normalizes what never became a URL) and was delivered on a "
                       + "fresh main-queue turn (insideBatch: false). A dispatch from inside "
                       + "applyBatch is re-entrant dispatch under a non-re-entrant guard")
        XCTAssertEqual(host.mapper.errorDispatchesSent, 1, "…exactly once — deferring is not doubling")
        XCTAssertEqual(host.mapper.imageResults.map { BnUrlOutcome($0) }.filter { $0.outcome == .error },
                       [BnUrlOutcome(url: unparseableSrc, outcome: .error)],
                       "the synchronous failure is a TERMINAL result (Kingfisher never saw it; "
                       + "the shell owed it a verdict anyway)")

        // …and the ERROR row held through it all: the placeholder painted by the
        // in-flight write STAYS, the declared box holds.
        try assertPlaceholderPainted("after the synchronous failure", imageView(host))
        assertFrame("the declared box holds through a failure that never left the batch",
                    try imageView(host), 0, 0, 200, 120)

        // Hygiene: the superseded /slow.png request terminates before teardown.
        server.releaseSlow()
        bnAwaitImageResults(host.mapper, 2)
    }

    /// **ADVERSARIAL ORDERING 1 — the node is REMOVED later in the same batch.** A
    /// deferred dispatch that replayed a decision-time capture would fire for a PURGED
    /// node; the fire-time re-decision finds the generation evicted and DROPs.
    func testAFailureMidBatchThenRemovalLaterInTheSameBatchDispatchesNOTHING() throws {
        XCTAssertNil(URL(string: unparseableSrc), "PRECONDITION — see the defer test")

        var deliveries: [Delivery] = []
        let host = makeSection(src: BnImageFixtureServer.SLOW_URL, declared: true,
                               attachError: errorHandler)
        host.mapper.onUiEvent = { [weak mapper = host.mapper] handlerId, name, payload in
            deliveries.append(Delivery(handlerId: handlerId, eventName: name, payload: payload,
                                       insideBatch: mapper?.isApplyingBatch ?? false))
        }

        // ONE batch: the synchronous failure, then the whole section is removed —
        // navigation's own shape (it names an ancestor, never the image). The DEFER
        // decision is made mid-batch; by the time the fresh turn runs, the node is
        // gone from every map.
        host.render([
            .updateProp(nodeId: image, name: "src", value: unparseableSrc),
            .removeNode(nodeId: section),
        ])
        server.releaseSlow() // the cancelled /slow.png bytes arrive at nobody
        bnSettle()

        XCTAssertEqual(deliveries, [],
                       "ZERO dispatches: the deferred turn RE-ASKED the liveness decision at "
                       + "fire time and found the node PURGED — a captured verdict replayed "
                       + "here would dispatch into a handler whose node no longer exists")
        XCTAssertEqual(host.mapper.errorDispatchesSent, 0)
        XCTAssertEqual(host.mapper.nodeCount, 0, "…and the purge itself was total")
    }

    /// **ADVERSARIAL ORDERING 2 — the src CHANGES later in the same batch.** The
    /// superseded source's deferred error must NOT be delivered into live user code
    /// (the stale-callback rule: it dispatches nothing, exactly as it paints nothing),
    /// and the NEW source's own lifecycle is untouched.
    func testAFailureMidBatchThenASrcChangeLaterInTheSameBatchDropsTheSupersededError() throws {
        XCTAssertNil(URL(string: unparseableSrc), "PRECONDITION — see the defer test")
        let fixture = try server.decoded(server.intrinsicPng)

        var deliveries: [Delivery] = []
        let host = makeSection(src: BnImageFixtureServer.SLOW_URL, declared: false,
                               attachError: errorHandler)
        host.mapper.onUiEvent = { [weak mapper = host.mapper] handlerId, name, payload in
            deliveries.append(Delivery(handlerId: handlerId, eventName: name, payload: payload,
                                       insideBatch: mapper?.isApplyingBatch ?? false))
        }

        // ONE batch: the synchronous failure (generation N), then a real source
        // (generation N + 1). At the deferred turn's fire time the failure's
        // generation is STALE — DROP, by the same one guard that stops stale paint.
        host.render([
            .updateProp(nodeId: image, name: "src", value: unparseableSrc),
            .updateProp(nodeId: image, name: "src", value: BnImageFixtureServer.INTRINSIC_URL),
        ])
        server.release()
        server.releaseSlow()
        bnAwaitImageResults(host.mapper, 3) // slow CANCELLED + bad ERROR + intrinsic SUCCESS
        bnSettle()

        XCTAssertEqual(deliveries, [],
                       "ZERO dispatches: the superseded source's error is a STALE CALLBACK "
                       + "by the time the deferred turn fires — delivering it would hand live "
                       + "user code a failure about a src the author already replaced")
        XCTAssertEqual(host.mapper.errorDispatchesSent, 0)
        XCTAssertEqual(Set(host.mapper.imageResults.map { BnUrlOutcome($0) }),
                       [BnUrlOutcome(url: BnImageFixtureServer.SLOW_URL, outcome: .cancelled),
                        BnUrlOutcome(url: unparseableSrc, outcome: .error),
                        BnUrlOutcome(url: BnImageFixtureServer.INTRINSIC_URL, outcome: .success)],
                       "…while every request still got its own honest verdict")
        // …and the NEW source's lifecycle was untouched by the dropped error: the bytes
        // painted, the placeholder cleared, the node measures the NATURAL size.
        assertFrame("the new source landed and measured", try imageView(host),
                    0, 0, fixture.size.width, fixture.size.height)
        XCTAssertNil(try imageView(host).bnPlaceholderColor)
        XCTAssertEqual(host.mapper.inFlightImageCount, 0, "nothing left in flight")
    }

    /// **THE MOUNT-ORDER PIN (Gate 3 review, I-1) — the attach lands AFTER the bad
    /// src, in the SAME batch, and the dispatch still happens.** This is the frame the
    /// renderer actually emits for `<BnImage Src="<unparseable>" OnError="...">`: at
    /// mount, `src` (seq 24) precedes `attachEvent "error"` (seq 27) in one batch, so
    /// the synchronous nil-URL failure asks "handler attached?" three patches early.
    /// The old table consulted `handlerAttached` BEFORE `applyingBatch` and answered
    /// DROP — permanently, against mid-batch state — so `OnError` never fired here
    /// while Android (whose failure lands post-batch) dispatched: a parity break, and
    /// design decision 2's "an unparseable non-empty URL is a failure and DISPATCHES"
    /// broken on this shell. Now the mid-batch decision DEFERS and the fire-time
    /// re-ask (`decideAndDispatchError` posts ITSELF) reads the SETTLED handler state:
    /// exactly one dispatch, on a fresh main-queue turn, never inside the batch.
    func testAMountBatchAttachingTheWireAFTERTheBadSrcStillDispatchesExactlyOnce() throws {
        XCTAssertNil(URL(string: unparseableSrc), "PRECONDITION — see the defer test")

        var deliveries: [Delivery] = []
        let host = BnSyntheticHost()
        host.mapper.onUiEvent = { [weak mapper = host.mapper] handlerId, name, payload in
            deliveries.append(Delivery(handlerId: handlerId, eventName: name, payload: payload,
                                       insideBatch: mapper?.isApplyingBatch ?? false))
        }

        // ONE batch, the renderer's own mount ordering: the failure terminates at
        // `src` with the attach still THREE patches away — more patches land behind
        // it (the placeholder prop, a whole sibling subtree), and the attach rides
        // LAST (the renderer's event-attribute position, makeSection's own order).
        host.render([
            bnCreate(section, "view", nil),
            bnStyle(section, "width", "300"),
            bnStyle(section, "alignItems", "flex-start"),
            bnCreate(image, "image", section),
            bnStyle(image, "width", "200"),
            bnStyle(image, "height", "120"),
            .updateProp(nodeId: image, name: "src", value: unparseableSrc),
            .updateProp(nodeId: image, name: "placeholderColor", value: placeholderHex),
            bnCreate(band, "view", section),
            bnStyle(band, "width", "300"),
            bnStyle(band, "height", "20"),
            .attachEvent(nodeId: image, eventName: "error", handlerId: errorHandler),
        ])
        bnSettle()

        XCTAssertEqual(deliveries,
                       [Delivery(handlerId: errorHandler, eventName: "error",
                                 payload: unparseableSrc, insideBatch: false)],
                       "EXACTLY ONE dispatch for the mount-time bad URL, delivered on a fresh "
                       + "main-queue turn (insideBatch: false): mid-batch the handler question "
                       + "is a RACE with the rest of the batch, so the decision DEFERS and the "
                       + "fire-time re-ask finds the attach the same batch carried. DROP here "
                       + "is the I-1 parity break: Android dispatches for this exact markup")
        XCTAssertEqual(host.mapper.errorDispatchesSent, 1,
                       "…exactly once — the defer is not a double, and the mount batch's other "
                       + "patches moved nothing")
        XCTAssertEqual(host.mapper.imageResults.map { BnUrlOutcome($0) },
                       [BnUrlOutcome(url: unparseableSrc, outcome: .error)],
                       "the synchronous failure is this mount's ONLY terminal result")
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// The 6.3 case section, with the 7.5 props riding in WIRE ORDER: `src` (seq 24)
    /// lands BEFORE `placeholderColor` (seq 25) — the order `BnImageWireState`'s
    /// header calls load-bearing, and the one the renderer actually emits. The attach,
    /// when asked for, rides last (the renderer's event-attribute position).
    private func makeSection(src: String, declared: Bool,
                             attachError: Int32? = nil) -> BnSyntheticHost {
        let host = BnSyntheticHost()
        var patches: [BnPatch] = [
            bnCreate(section, "view", nil),
            bnStyle(section, "width", "300"),
            bnStyle(section, "alignItems", "flex-start"),
            bnCreate(image, "image", section),
        ]
        if declared {
            patches.append(bnStyle(image, "width", "200"))
            patches.append(bnStyle(image, "height", "120"))
        }
        patches.append(.updateProp(nodeId: image, name: "src", value: src))
        patches.append(.updateProp(nodeId: image, name: "placeholderColor", value: placeholderHex))
        if let handler = attachError {
            patches.append(.attachEvent(nodeId: image, eventName: "error", handlerId: handler))
        }
        patches.append(bnCreate(band, "view", section))
        patches.append(bnStyle(band, "width", "300"))
        patches.append(bnStyle(band, "height", "20"))
        host.render(patches)
        return host
    }

    /// The view-state pin: the paint IS the placeholder, in exactly the prop's color
    /// (the twin of Android's ColorDrawable assertion).
    private func assertPlaceholderPainted(_ whenWhat: String, _ image: BnImageView,
                                          file: StaticString = #filePath, line: UInt = #line) {
        XCTAssertEqual(image.bnPlaceholderColor, placeholderColor,
                       "the placeholder must be PAINTED \(whenWhat) — the paint is the "
                       + "placeholder (inside the box Yoga gave the node), in exactly the "
                       + "prop's color; got \(String(describing: image.bnPlaceholderColor))",
                       file: file, line: line)
    }

    private func imageView(_ host: BnSyntheticHost,
                           file: StaticString = #filePath, line: UInt = #line) throws -> BnImageView {
        try bnImageIn(host.root.subviews[0], file: file, line: line)
    }

    private func bandView(_ host: BnSyntheticHost) -> UIView {
        host.root.subviews[0].subviews[1]
    }
}
