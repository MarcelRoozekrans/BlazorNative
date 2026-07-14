// ─────────────────────────────────────────────────────────────────────────────
// BnImageTests — Phase 6.3 Gate 3 (Tasks 3.2 / 3.4): **THE PARITY CONTRACT, AT THE
// MAPPER LEVEL.** The iOS twin of Kotlin's `WidgetMapperImageTest`, row for row —
// and **the same numbers**, because that is the whole of the phase.
//
// The contract (design §"The parity contract") has eight rows. `/image` demonstrates
// three of them on a real page; the rest have no demo affordance and never will — no
// page flips a `Src` at run time (adding one would rewrite this phase's frame tables),
// and no page can hold a request open long enough for a test to cancel it. They are
// asserted here, against synthetic frames, which is exactly where this suite already
// asserts `UpdateProp` behaviour.
//
// | contract row | pinned by |
// |---|---|
// | THE UNIT — one file pixel is one point | every natural size below is asserted against the DECODED fixture's own PIXEL COUNT, and `bnAssertFixtureContract` pins `scale == 1` |
// | no `Width`/`Height` → 0×0, then the NATURAL size | `testAnIntrinsicImage…` (+ `/image`) |
// | `Width`/`Height` set → exactly those, always | `testADefiniteImage…` (+ `/image`) |
// | on failure → 0×0, reserves nothing, no retry | `testAFailedLoad…` (+ `/image`) |
// | on `Src` change → cancel; back to 0×0 | `testASrcChangeCancels…` |
// | on **`Src` → null** (and `""`) → cancel, CLEAR, collapse; **siblings move back UP** | `testSrcToNull…`, `testAnEmptyString…` |
// | on node removal → **cancel** | `testRemovingTheNode…` |
// | on load → markDirty + re-solve. **ONE reflow, never two** | `testAWarmCache…` (the layout-pass COUNT — no frame assertion can see this) |
// | content mode: aspect-fit, explicitly | `testADefiniteImage…` (frame-neutral: nothing else can) |
//
// **Cancellation is memory safety, not hygiene** (non-negotiable #4): on iOS a completion
// firing into a purged node marks a **freed `YGNodeRef`** dirty. It is pinned here in the
// only honest direction — the request is proven to have REACHED the fixture server
// (`awaitPath`) *before* it is cancelled, so "cancelled" cannot be the trivially-true
// answer about a request that had not started.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit
@testable import BnHost

final class BnImageTests: BnHostTestCase {

    private var server: BnImageFixtureServer!

    private let section: Int32 = 1
    private let image: Int32 = 2
    private let band: Int32 = 3
    private let sectionW: CGFloat = 300
    private let bandH: CGFloat = 20

    override func setUpWithError() throws {
        bnClearImageCaches()
        server = try BnImageFixtureServer()
    }

    override func tearDown() {
        // The server is closed even if setUp threw (a taken port) — and its own errors are
        // NOT asserted empty here, unlike the demo suites': the cancellation tests in this
        // class DO drop connections, which is cancellation seen from the other end of the
        // socket and exactly what they are asking for.
        server?.close()
        server = nil
        super.tearDown()
    }

    // ── The two measurement paths ────────────────────────────────────────────

    func testAnIntrinsicImageMeasuresZeroBeforeTheBytesAndItsNATURALSizeAfter() throws {
        // THE FIXTURE CONTRACT — on the DECODED bytes, BEFORE any frame is read.
        let fixture = try server.decoded(server.intrinsicPng)
        try bnAssertFixtureContract(intrinsic: fixture, fixed: server.decoded(server.fixedPng))
        let wi = fixture.size.width
        let hi = fixture.size.height

        let host = makeSection(src: BnImageFixtureServer.INTRINSIC_URL)

        // ── BEFORE THE BYTES ─────────────────────────────────────────────────
        XCTAssertEqual(host.mapper.imageTerminalCount, 0,
                       "nothing has terminated yet — the fixture server is HOLDING every response, "
                       + "which is the only thing that makes this 'before' honest")
        assertFrame("the intrinsic image, BEFORE: a measured leaf with no bytes measures 0 × 0 — "
                    + "not 'small', ZERO", try imageView(host), 0, 0, 0, 0)
        assertFrame("band I, BEFORE: y = 0", bandView(host), 0, 0, sectionW, bandH)
        assertFrame("the section HUGS 0 + 20", host.root.subviews[0], 0, 0, sectionW, bandH)

        // ── THE BYTES LAND ───────────────────────────────────────────────────
        server.release()
        bnAwaitImageResults(host.mapper, 1)

        XCTAssertEqual(host.mapper.imageResults,
                       [BnImageResult(nodeId: image, url: BnImageFixtureServer.INTRINSIC_URL,
                                      outcome: .success)],
                       "Kingfisher's own per-node TERMINAL callback said SUCCESS — the bytes really "
                       + "arrived, over real HTTP, from the in-process loopback fixture")

        // POSITIVE, and against the DECODED FIXTURE's own pixel count — never a constant this
        // file invents. A loader stubbed to a fixed size reddens here; so does any downsampling
        // that changed the reported size, and so does a `.scaleFactor`.
        let img = try imageView(host)
        assertFrame("the intrinsic image, AFTER: its NATURAL size — the DECODED FIXTURE's own "
                    + "\(Int(wi)) × \(Int(hi)) PIXELS, read as POINTS. ONE FILE PIXEL IS ONE dp/pt: "
                    + "that is what Android has to WORK for (bitmap.width) and what iOS must not "
                    + "give away, or the two shells cannot compute the same frame",
                    img, 0, 0, wi, hi)
        XCTAssertTrue(img.frame.width > 0 && img.frame.height > 0,
                      "POSITIVELY: Wi > 0 AND Hi > 0. Two of this phase's three demo cases assert "
                      + "'nothing moved', and a suite of negatives is a suite that a TOTAL FAILURE "
                      + "satisfies")
        assertFrame("THE REFLOW: band I moved from y = 0 to y = Hi. Only a genuine re-solve moves "
                    + "the node BELOW the image — markDirty (6.1) then calculateAndApply (6.2). The "
                    + "image's OWN frame could be faked by a shell that painted and never re-solved; "
                    + "the BAND's y could not",
                    bandView(host), 0, hi, sectionW, bandH)
        assertFrame("…and the section grew by exactly Hi",
                    host.root.subviews[0], 0, 0, sectionW, hi + bandH)
        XCTAssertNotNil(img.image, "the bytes were also PAINTED")
        XCTAssertEqual(host.mapper.inFlightImageCount, 0, "nothing is left in flight")
    }

    func testADefiniteImageIsNeverMeasuredSoTheBytesCannotMoveItsFrame() throws {
        let fixture = try server.decoded(server.fixedPng)
        XCTAssertFalse(fixture.size.width == 200 && fixture.size.height == 120,
                       "the FIXED case's fixture must NOT be 200 × 120 — otherwise 'it measures "
                       + "200 × 120' is a coincidence rather than a proof that a declared size "
                       + "short-circuits measurement. Got \(fixture.size)")

        let host = makeSection(src: BnImageFixtureServer.FIXED_URL,
                               imageStyles: [bnStyle(image, "width", "200"),
                                             bnStyle(image, "height", "120")])

        assertFrame("the fixed image, BEFORE the bytes", try imageView(host), 0, 0, 200, 120)
        assertFrame("band F, BEFORE: y = 120", bandView(host), 0, 120, sectionW, bandH)

        server.release()
        bnAwaitImageResults(host.mapper, 1)
        XCTAssertEqual(host.mapper.imageResults.first?.outcome, .success,
                       "the request DID terminate — otherwise 'the frame did not move' is a "
                       + "statement about a fetch that never happened")

        // THE IDENTITY. Both axes definite ⇒ Yoga never calls the measure func at all, so the
        // fixture's 64 × 48 is nowhere in this frame and could not be.
        let img = try imageView(host)
        assertFrame("the fixed image, AFTER: IDENTICAL. Width AND Height are definite, so Yoga "
                    + "never calls its measure func — the bytes cannot move this frame even in "
                    + "principle", img, 0, 0, 200, 120)
        assertFrame("band F, AFTER: y = 120, UNCHANGED — THE NO-REFLOW PROOF",
                    bandView(host), 0, 120, sectionW, bandH)
        XCTAssertNotNil(img.image, "…and the bytes were painted all the same")

        // THE CONTENT MODE — and THIS is the case where it bites: a 64 × 48 fixture inside a
        // DECLARED 200 × 120 frame. The two frameworks' defaults DISAGREE (UIImageView is
        // `.scaleToFill` — a STRETCH; Android's ImageView is FIT_CENTER), the divergence is
        // FRAME-NEUTRAL (every number above survives it), and so no frame table on either
        // platform can see it: what breaks is "renders identically", silently, on one platform.
        // Deferring the ContentMode API (decision 3) does not defer the DEFAULT.
        XCTAssertEqual(img.contentMode, .scaleAspectFit,
                       "the content mode is ASPECT-FIT, and it is set EXPLICITLY rather than "
                       + "inherited from the framework — UIImageView's own default would STRETCH "
                       + "this 64 × 48 fixture into a 200 × 120 box while Android letterboxed it, "
                       + "and the disagreement is invisible to every frame assertion in this file")
    }

    func testAFailedLoadStaysZeroAndReservesNothing() throws {
        let host = makeSection(src: BnImageFixtureServer.MISSING_URL)

        server.release()
        bnAwaitImageResults(host.mapper, 1)

        XCTAssertEqual(host.mapper.imageResults.first?.outcome, .error,
                       "a REAL 404 — from a REAL server, deterministic and offline. (A blocked "
                       + "cleartext fetch would look exactly like this, which is why "
                       + "BnImageDemoTests probes ATS separately.)")
        assertFrame("the failing image stays 0 × 0", try imageView(host), 0, 0, 0, 0)
        assertFrame("band X did not move: THE FAILURE RESERVED NOTHING",
                    bandView(host), 0, 0, sectionW, bandH)
        XCTAssertNil(try imageView(host).image, "nothing was painted")
        XCTAssertEqual(host.mapper.imageTerminalCount, 1, "…and it did NOT retry")
        XCTAssertEqual(host.mapper.inFlightImageCount, 0, "nothing is left in flight")
    }

    // ── The reflow in the OTHER direction (design §"On `Src` → `null`") ──────

    func testSrcToNullClearsTheImageAndTheSiblingMovesBackUP() throws {
        let fixture = try server.decoded(server.intrinsicPng)
        let hi = fixture.size.height
        let host = makeSection(src: BnImageFixtureServer.INTRINSIC_URL)
        server.release()
        bnAwaitImageResults(host.mapper, 1)
        assertFrame("the reflow DOWN happened first", bandView(host), 0, hi, sectionW, bandH)

        // THE PATCH THE RENDERER ALREADY EMITS: a RemoveAttribute on a non-style name
        // (BnButton.Enabled's precedent), pinned in .NET by
        // BnComponentTests.BnImage_SrcGoesNull_EmitsUpdatePropNullOnThePropWire. A shell that
        // CRASHED on it (`URL(string: nil)`) or that kept painting the old bytes is wrong in the
        // way two shells wrong DIFFERENTLY is worst: silently, on one platform.
        host.render([.updateProp(nodeId: image, name: "src", value: nil)])

        let img = try imageView(host)
        XCTAssertNil(img.image, "the image was CLEARED — the pixels go")
        XCTAssertNil(img.bnNaturalSize, "…and so does its recorded natural size, or the node would "
                     + "keep MEASURING bytes it no longer holds")
        assertFrame("the node collapsed back to 0 × 0", img, 0, 0, 0, 0)
        assertFrame("THE SECOND REFLOW DIRECTION: band I moved back UP, to y = 0",
                    bandView(host), 0, 0, sectionW, bandH)
        assertFrame("…and the section shrank back to hugging its band alone",
                    host.root.subviews[0], 0, 0, sectionW, bandH)
        XCTAssertEqual(host.mapper.inFlightImageCount, 0, "nothing was left in flight")
    }

    /// **AN EMPTY STRING TAKES THE CLEAR PATH, AND IS NEVER FETCHED.**
    ///
    /// A shell decision, written into the shared contract rather than left for the two shells
    /// to make differently — and on iOS it is not a nicety: **`URL(string: "")` is `nil`**, so
    /// a shell that force-unwrapped it would CRASH (an NPE by another name). Android's twin
    /// would merely have issued a pointless request that errored immediately. One rule, stated
    /// once, so it is one decision instead of two.
    func testAnEmptyStringTakesTheClearPathAndIsNeverFetched() throws {
        let fixture = try server.decoded(server.intrinsicPng)
        let host = makeSection(src: BnImageFixtureServer.INTRINSIC_URL)
        server.release()
        bnAwaitImageResults(host.mapper, 1)
        assertFrame("the bytes landed first", try imageView(host),
                    0, 0, fixture.size.width, fixture.size.height)

        host.render([.updateProp(nodeId: image, name: "src", value: "")])

        XCTAssertNil(try imageView(host).image, "an EMPTY src is the CLEAR contract")
        assertFrame("…so the node collapses to 0 × 0, exactly as a null does",
                    try imageView(host), 0, 0, 0, 0)
        assertFrame("…and the band moves back UP", bandView(host), 0, 0, sectionW, bandH)
        XCTAssertEqual(host.mapper.imageTerminalCount, 1,
                       "AND NOTHING WAS FETCHED: still exactly the ONE terminal result from the "
                       + "real load. An empty string is not a request that fails — it is not a "
                       + "request at all")
        XCTAssertEqual(host.mapper.inFlightImageCount, 0, "nothing is left in flight")
    }

    // ── Cancellation: MEMORY SAFETY, NOT HYGIENE (non-negotiable #4) ─────────

    func testASrcChangeCancelsTheRequestInFlight() throws {
        let fixture = try server.decoded(server.intrinsicPng)
        let host = makeSection(src: BnImageFixtureServer.SLOW_URL)
        server.release() // the ordinary paths answer; /slow.png is held on its own gate

        XCTAssertTrue(server.awaitPath("/slow.png"),
                      "the slow request never reached the fixture server — cancelling a request "
                      + "that had not started proves nothing")

        host.render([.updateProp(nodeId: image, name: "src",
                                 value: BnImageFixtureServer.INTRINSIC_URL)])

        bnAwaitImageResults(host.mapper, 2)
        server.releaseSlow() // and now the old bytes arrive at nobody

        // A SET, not a list. What this test claims is *what happened to each request* — the
        // in-flight one was cancelled, the new one succeeded — and that claim is
        // order-independent. The ORDER is a fact about the fixture server's gate and a cold
        // cache, not about the contract.
        let outcomes = Set(host.mapper.imageResults.map { BnUrlOutcome($0) })
        XCTAssertEqual(outcomes,
                       [BnUrlOutcome(url: BnImageFixtureServer.SLOW_URL, outcome: .cancelled),
                        BnUrlOutcome(url: BnImageFixtureServer.INTRINSIC_URL, outcome: .success)],
                       "the IN-FLIGHT request was CANCELLED, and the new one succeeded")
        assertFrame("the node measures the NEW bytes", try imageView(host),
                    0, 0, fixture.size.width, fixture.size.height)
        XCTAssertEqual(host.mapper.inFlightImageCount, 0, "nothing is left in flight")
    }

    /// **THE MUTATION THIS TEST EXISTS FOR** — and the reason its loader's timeout is 5s.
    ///
    /// Delete `cancelImageRequest(id)` from `BnWidgetMapper.handleRemove` and this test must go
    /// red **on the OUTCOME** (`expected .cancelled, got .error`), not on a gate timeout. That
    /// is what `BnImageLoader.downloadTimeout = 5` buys: `/slow.png` is held for the whole test,
    /// so an UNCANCELLED request does not hang — it sits on the socket until the download times
    /// out and Kingfisher reports a failure. Android gets the same shape for free from OkHttp's
    /// 10s read timeout; **`URLSession`'s default is 60 seconds and Kingfisher's own is 15**,
    /// either of which would blow past this suite's 30s synchronization gate and turn a named
    /// contract failure into an anonymous timeout. The Gate 2 conclusion says so in as many
    /// words, and this is the shell obeying it.
    func testRemovingTheNodeCancelsTheRequestInFlightAndTouchesNoPurgedNode() throws {
        let host = makeSection(src: BnImageFixtureServer.SLOW_URL)
        XCTAssertEqual(host.mapper.nodeCount, 3, "the section, the image and the band")
        XCTAssertTrue(server.awaitPath("/slow.png"),
                      "the request never reached the fixture server")
        let doomed = try imageView(host)

        // ONE RemoveNodePatch, naming the SECTION — the shape navigation actually emits (it
        // names the PAGE's root; it never names the image). The image's request must be
        // cancelled as part of the SUBTREE purge, or a completion fires into a removed node —
        // and on iOS that node's YGNodeRef has been FREED by `bn_yoga_node_free_subtree`, so the
        // completion's `markDirty` writes through a dangling pointer. 6.2's lesson, new costume.
        host.render([.removeNode(nodeId: section)])

        bnAwaitImageResults(host.mapper, 1)
        XCTAssertEqual(host.mapper.imageResults.first?.outcome, .cancelled,
                       "THE PIN: the in-flight request was CANCELLED by the purge")
        XCTAssertEqual(host.mapper.inFlightImageCount, 0, "nothing is left in flight")

        // …and NOW let the bytes arrive. A cancelled request must paint NOTHING — not into the
        // detached UIImageView, and not into a Yoga node that no longer exists.
        server.releaseSlow()
        bnSettle()

        XCTAssertEqual(host.mapper.imageResults.map { $0.outcome }, [.cancelled],
                       "A LATE COMPLETION PAINTED NOTHING: still exactly one terminal result, and "
                       + "it is still CANCELLED")
        XCTAssertNil(doomed.image, "…and nothing was set on the removed UIImageView")
        XCTAssertNil(doomed.bnNaturalSize, "…nor was a natural size recorded on it")
        XCTAssertEqual(host.mapper.nodeCount, 0,
                       "ONE RemoveNodePatch purged the whole subtree from BOTH trees…")
        XCTAssertEqual(host.mapper.yogaNodeCount, 0,
                       "…and the image node the completion would have marked dirty does not exist "
                       + "in either. Its YGNodeRef is FREED — which is why this is memory safety "
                       + "and not hygiene")
        XCTAssertEqual(host.mapper.yogaViewCount, 0, "…and the last view→node edge is gone")
        XCTAssertTrue(host.root.subviews.isEmpty, "the section is detached from the host root")
    }

    // ── The synchronous memory-cache hit (the un-numbered non-negotiable) ────

    /// **THE PATH EVERY OTHER TEST CLEARS AWAY — and the only assertion that can see it is a
    /// COUNT.**
    ///
    /// Kingfisher's callback queue is `.mainCurrentOrAsync`, and the shell issues its request
    /// from `UpdateProp("src", …)` — **on the main thread, inside `applyBatch`**. So on a
    /// memory-cache hit the whole completion (set-image, natural size, `markDirty`, re-solve)
    /// runs **to completion inside the `retrieveImage` call, before it returns**. That is the
    /// ordinary case on the SECOND mount of any page the process has already fetched, and it is
    /// exactly what `setUp`'s `bnClearImageCaches()` hides — every other test in this repo
    /// mounts against a cold cache, so this path was entirely unexercised. (Coil does the same
    /// on `Dispatchers.Main.immediate`; Android's twin found two bugs in it.)
    ///
    /// Two rules live in it, and **neither can be seen in a frame**:
    ///
    ///  1. **`resolveLayout` must NO-OP inside a batch.** The batch's own `CommitFrame` re-solves
    ///     the whole tree at the end; a re-solve from inside it runs Yoga against a HALF-APPLIED
    ///     tree (the band below the image has not been created yet — the patches are still
    ///     arriving) and then AGAIN at commit: **two reflows, where the contract says ONE.** The
    ///     final frames are IDENTICAL either way, because the commit fixes them up — which is
    ///     why the LAYOUT PASS COUNT is what this asserts. Make the re-solve re-enter and this
    ///     test reddens; nothing else in either suite would.
    ///  2. **The task is recorded only if it is STILL LIVE.** The completion already ran, so its
    ///     own `clearIfMine` found nothing to clear; storing the (spent) handle afterwards leaks
    ///     it forever — `inFlightImageCount` never returns to 0, and a later removal would
    ///     "cancel" a request that finished long ago.
    func testAWarmCacheCompletesInsideTheBatchAndCostsExactlyONEReflow() throws {
        let fixture = try server.decoded(server.intrinsicPng)
        let wi = fixture.size.width
        let hi = fixture.size.height

        server.release() // there is no BEFORE table to protect on either mount here

        // ── MOUNT 1: COLD (setUp cleared the caches). This is what WARMS them. ──
        let cold = makeSection(src: BnImageFixtureServer.INTRINSIC_URL)
        XCTAssertEqual(cold.mapper.layoutPassCount, 1,
                       "the mount frame's CommitFrame is ONE layout pass, and the request is still "
                       + "in flight — so nothing else has re-solved yet")
        bnAwaitImageResults(cold.mapper, 1)
        XCTAssertEqual(cold.mapper.layoutPassCount, 2,
                       "THE ASYNCHRONOUS COMPLETION RE-SOLVED EXACTLY ONCE — one reflow, never two. "
                       + "No patch is behind that frame (the wire carries no completion signal), so "
                       + "the re-solve is the shell's to trigger")
        assertFrame("…to the natural size", try imageView(cold), 0, 0, wi, hi)
        XCTAssertEqual(cold.mapper.inFlightImageCount, 0, "and mount 1 left nothing in flight")

        // ── MOUNT 2: WARM. NO bnClearImageCaches(). ─────────────────────────
        let warm = makeSection(src: BnImageFixtureServer.INTRINSIC_URL)

        XCTAssertEqual(warm.mapper.imageTerminalCount, 1,
                       "THE MEMORY HIT COMPLETED SYNCHRONOUSLY — inside `retrieveImage`, inside "
                       + "`UpdateProp(\"src\")`, inside `applyBatch`, before `render` returned. If "
                       + "this is 0 the whole premise below is untested")
        XCTAssertEqual(warm.mapper.layoutPassCount, 1,
                       "…AND IT COST EXACTLY ONE LAYOUT PASS. The completion set the image, recorded "
                       + "the natural size and marked the node dirty MID-BATCH, and the batch's own "
                       + "CommitFrame is the ONE re-solve that applied them. A completion that "
                       + "re-solved from inside the batch would make this 2 — against a HALF-APPLIED "
                       + "tree — and every frame below would still be correct")
        XCTAssertEqual(warm.mapper.inFlightImageCount, 0,
                       "NOTHING IS LEFT IN FLIGHT after a WARM-cache mount. A memory hit completes "
                       + "INSIDE the call, so its bookkeeping runs BEFORE the shell has anything to "
                       + "record — and an unconditional record leaks the handle for the life of the "
                       + "mapper. On iOS that stale entry is a request nothing can cancel, whose "
                       + "completion marks a freed YGNodeRef dirty")

        // …AND THE FRAMES ARE THE SAME ONES. A synchronous completion must produce the SAME
        // AFTER table as an asynchronous one — that is what "one reflow, never two" means when
        // the reflow happens to be synchronous.
        assertFrame("the intrinsic image, FROM CACHE: the same \(Int(wi)) × \(Int(hi)) it measured "
                    + "from the network", try imageView(warm), 0, 0, wi, hi)
        assertFrame("THE REFLOW STILL HAPPENED, from inside the batch: band I is at y = Hi",
                    bandView(warm), 0, hi, sectionW, bandH)
        assertFrame("…and the section grew by exactly Hi",
                    warm.root.subviews[0], 0, 0, sectionW, hi + bandH)
        XCTAssertNotNil(try imageView(warm).image, "…and the cached bytes were painted")
    }

    // ── Robustness ───────────────────────────────────────────────────────────

    /// **THE RESULT LOG IS A BOUNDED RING** — because it is appended by every terminal callback
    /// and it lives in PRODUCTION code. Unbounded, it grows one entry per image per navigation,
    /// for as long as the app runs. It is a diagnostic, not a ledger.
    ///
    /// `imageTerminalCount` is what makes the overflow observable at all: `imageResults` cannot
    /// count past the cap, so a test that waited for "more than the cap" on IT would wait forever.
    func testTheResultLogIsABOUNDEDRingItCannotGrowForever() {
        let host = BnSyntheticHost()
        let cap = host.mapper.imageResultCap
        let overflow = cap + 6

        // One image node per request — all 404s, all from the loopback fixture, all terminal.
        var patches: [BnPatch] = []
        for i in 1...overflow {
            patches.append(bnCreate(Int32(i), "image", nil))
            patches.append(.updateProp(nodeId: Int32(i), name: "src",
                                       value: BnImageFixtureServer.MISSING_URL))
        }
        host.render(patches)
        server.release()

        // Wait on the TOTAL, not on the log — the log is exactly the thing that cannot count
        // this high, which is the property under test.
        bnAwaitImageResults(host.mapper, overflow, timeout: 60)
        XCTAssertEqual(host.mapper.imageTerminalCount, overflow,
                       "all \(overflow) requests must terminate — otherwise 'the log is bounded' is "
                       + "a statement about a log nothing filled")
        XCTAssertEqual(host.mapper.imageResults.count, cap,
                       "THE BOUND: \(overflow) requests terminated and the log holds the last \(cap)")
        XCTAssertEqual(host.mapper.inFlightImageCount, 0, "…and nothing is left in flight")
    }

    func testSrcOnANonImageNodeIsLoggedAndIgnored() {
        let host = BnSyntheticHost()
        host.render([
            bnCreate(1, "button", nil),
            .updateProp(nodeId: 1, name: "src", value: BnImageFixtureServer.INTRINSIC_URL),
        ])
        XCTAssertEqual(host.mapper.inFlightImageCount, 0,
                       "no request is issued for a node that cannot hold an image")
        XCTAssertEqual(host.mapper.imageTerminalCount, 0, "…and none ever terminates")
        XCTAssertEqual(host.root.subviews.count, 1, "the UIButton is still there")
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// `BnImageDemo`'s case section, in one frame: a 300-wide column that HUGS its two
    /// children, holding an image and a 20pt band beneath it.
    ///
    /// **`alignItems: flex-start` is load-bearing** (as it is on the demo page): a section is a
    /// COLUMN, so its cross axis is WIDTH, and Yoga's default `alignItems` is **stretch** —
    /// under which an intrinsic image would be stretched to 300 and its measured width would
    /// never be seen at all.
    ///
    /// **The band's y is the proof.** An image's own frame is a bad witness: a shell could paint
    /// the bytes and never re-solve, and the image would still look right. Only a genuine
    /// re-solve moves the node BELOW it.
    private func makeSection(src: String, imageStyles: [BnPatch] = []) -> BnSyntheticHost {
        let host = BnSyntheticHost()
        host.render([
            bnCreate(section, "view", nil),
            bnStyle(section, "width", "300"),
            bnStyle(section, "alignItems", "flex-start"),
            bnCreate(image, "image", section),
        ] + imageStyles + [
            .updateProp(nodeId: image, name: "src", value: src),
            bnCreate(band, "view", section),
            bnStyle(band, "width", "300"),
            bnStyle(band, "height", "20"),
        ])
        return host
    }

    private func imageView(_ host: BnSyntheticHost,
                           file: StaticString = #filePath, line: UInt = #line) throws -> BnImageView {
        try bnImageIn(host.root.subviews[0], file: file, line: line)
    }

    private func bandView(_ host: BnSyntheticHost) -> UIView {
        host.root.subviews[0].subviews[1]
    }
}

/// `(url, outcome)` — the order-independent shape the cancellation assertions compare on.
struct BnUrlOutcome: Hashable {
    let url: String
    let outcome: BnImageOutcome

    init(url: String, outcome: BnImageOutcome) {
        self.url = url
        self.outcome = outcome
    }

    init(_ result: BnImageResult) {
        self.url = result.url
        self.outcome = result.outcome
    }
}
