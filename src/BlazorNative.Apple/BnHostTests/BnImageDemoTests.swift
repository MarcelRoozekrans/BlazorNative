// ─────────────────────────────────────────────────────────────────────────────
// BnImageDemoTests — Phase 6.3 Gate 3 Task 3.4 — **THE IMAGE DEMO, ON THE
// SIMULATOR** (M6 DoD #5).
//
// Mounts `BnImageDemo` (the `/image` page) through the real NativeAOT boot and
// asserts **both** canonical tables from `src/BlazorNative.Components/BnImageDemo.razor`'s
// file header — *before* the bytes land and *after* — because the difference between
// them **is the phase**. Same discipline and same pairing as `BnLayoutDemoTests` and
// `BnScrollDemoTests`: **`BnImageDemoAndroidTest` asserts THE SAME NUMBERS on the
// AVD**, line for line, with the same tolerance. That PAIRING — not either test alone
// — is what "identically on both platforms" means. It works because Yoga computes in
// density-independent units on both: iOS points map 1:1, Android multiplies by
// `density` at frame-apply time.
//
// Both tables are declared in `bnImageDemoBeforeFrames` / `bnImageDemoAfterFrames`
// (BnDemoFrameTables.swift) — never inline here — and `ShellFrameTableDriftTests` demands
// the Android shell's twin declaration be equal to them, in the REQUIRED lane (M6 audit,
// F2). The AFTER table is parameterised by the DECODED fixture's own `wi`/`hi`, which the
// drift test compares AS SYMBOLS: it can check that both shells say `hi + 20` without
// knowing what `hi` is, which is exactly the point — neither shell is allowed to write that
// number down.
//
// ── THE TRAP THIS FILE EXISTS TO NOT FALL INTO ──────────────────────────────────
//
// **Two of the three cases assert that NOTHING MOVED** — [0]'s frame is definite and
// [2]'s failure reserves nothing — and the wire carries **no completion signal** by
// design (no `OnLoad`, no `OnError`: each would change measurement). So:
//
//  - assert the AFTER table straight after mount and **[0] and [2] pass having proven
//    nothing**; only [1] reddens.
//  - and if ATS blocked cleartext HTTP, all three fetches fail — **a blocked load is
//    INDISTINGUISHABLE from the 404 that [2] expects** — so [0] still passes, [2]
//    still passes, and only [1] reddens, reading as a reflow bug. **A green suite is
//    achievable on a simulator that loaded nothing.**
//
// Four things defend against that, and all four are load-bearing:
//
//  1. **THE SYNCHRONIZATION GATE** ([bnAwaitImageResults]) — the AFTER table is read
//     only once **all three** requests have TERMINATED, awaited on Kingfisher's own
//     per-node `completionHandler`, counted to three, with a timeout that **FAILS**.
//     Never on band I's movement: that witnesses only case [1].
//  2. **THE OUTCOMES ARE ASSERTED, NOT JUST THE COUNT** — two `SUCCESS` and one
//     `ERROR`, against the URLs the WIRE carried. A blocked-ATS simulator produces
//     three `ERROR`s and reddens *here*, by name.
//  3. **THE CLEARTEXT PROBE** ([testCleartextLoopbackIsPermittedByATS]) — the ATS
//     verdict read directly, through `URLSession`, so a block names itself.
//  4. **POSITIVE ASSERTIONS** — `Wi > 0`, `Hi > 0`, and the intrinsic image's frame
//     equal to the **decoded fixture's own pixel count**, so "band F did not move"
//     means "the bytes landed and did not move it" rather than "no bytes landed".
//
// And the BEFORE table is only observable at all because the fixture server **holds
// every response** until the test has read it ([BnImageFixtureServer]) — otherwise the
// loopback fetch wins the race against the test's first look and the "before" table is
// asserted on a page that has already reflowed.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit
@testable import BnHost

final class BnImageDemoTests: BnHostTestCase {

    /// BnImageDemo.razor's `SectionWidthDp`. Every OTHER number this page's frames are made of
    /// now lives in the canonical tables (`bnImageDemoBeforeFrames` / `bnImageDemoAfterFrames`,
    /// BnDemoFrameTables.swift), which the Android shell declares too and
    /// `ShellFrameTableDriftTests` pins the two against each other in the REQUIRED lane (M6
    /// audit, F2). This one survives because it is not a frame: it bounds the fixture
    /// (`0 < Wi ≤ 300` — a section is 300 wide, so the measure func is asked at most 300).
    private let sectionW: CGFloat = 300

    private var server: BnImageFixtureServer!

    /// Hold the runtime for the test's lifetime so the @convention(c) callback trampoline
    /// is never released mid-render.
    private var runtime: BnRuntime?
    private var host: UIView!
    private var mapper: BnWidgetMapper!

    override func setUpWithError() throws {
        try super.setUpWithError()
        bnClearImageCaches()
        // The close is STRUCTURAL (a teardown block registered by `started(for:)`) — a server
        // nobody closed keeps :8099 bound forever, and the class that pays is a LATER one.
        server = try BnImageFixtureServer.started(for: self)
    }

    /// **THE SERVER'S OWN ERRORS ARE ASSERTED EMPTY.** The fixture server swallows a failed
    /// write, and the scoping is right — a broken pipe IS cancellation, seen from the other
    /// end of the socket. But **NOTHING ON THIS PAGE CANCELS ANYTHING**: there is no client
    /// here that could drop a connection, so a write failure in this class is a REAL SERVER
    /// BUG. Unrecorded and unasserted, its only symptom would be the synchronization gate
    /// timing out 30 seconds later and taking the blame for it.
    override func tearDown() {
        XCTAssertEqual(server?.errors ?? [], [],
                       "the fixture server failed a write, and NOTHING ON THIS PAGE CANCELS "
                       + "ANYTHING — so this is a real server bug, not a dropped client")
        server = nil // …and the CLOSE is the teardown block `started(for:)` registered
        super.tearDown()
    }

    // ── The mount ────────────────────────────────────────────────────────────

    /// Boots a FRESH mapper + runtime and mounts `/image`. Called once by most tests, and
    /// TWICE by the warm-cache test — which is the whole of that test's premise.
    ///
    /// There is no iOS route registry (`HostViewController` hardcodes `BnDemo`), so the demo
    /// is mounted by its registry NAME — the pattern `BnLayoutDemoTests` uses.
    @discardableResult
    private func mount() throws -> UIView {
        host = UIView(frame: CGRect(x: 0, y: 0, width: 390, height: 844))
        let mapper = bnMapper(root: host)
        self.mapper = mapper
        let runtime = BnRuntime(mapper: mapper)
        self.runtime = runtime
        runtime.onError = { msg, err in NSLog("[BnImageDemoTests] \(msg): \(err)") }
        try runtime.start(component: "BnImageDemo", os: "ios")
        return try pollForDemo()
    }

    // ── [1] CLEARTEXT, VERIFIED RATHER THAN ASSUMED ──────────────────────────

    /// **App Transport Security blocks arbitrary HTTP, and KINGFISHER FETCHES THROUGH
    /// `URLSession`, SO ATS APPLIES TO IT.** `BnHost/Info.plist` carries
    /// `NSAppTransportSecurity → NSAllowsLocalNetworking` (the narrow loopback exemption —
    /// **not** `NSAllowsArbitraryLoads`) for exactly this reason.
    ///
    /// This test is what turns that sentence into a **checked fact**. It pulls a fixture over
    /// the very loopback Kingfisher will use, through `URLSession`, so a block surfaces here
    /// as the named error it is — instead of as three "failed" images that TWO OF THE DEMO'S
    /// THREE ASSERTIONS WOULD HAPPILY CERTIFY. (Android's twin is
    /// `cleartext_loopback_is_permitted_in_the_debug_build`; the mechanism differs — a
    /// network-security-config — and the failure mode is identical.)
    ///
    /// It also proves the bytes are **OURS**: port 8099 is HOST-GLOBAL on the macOS runner
    /// (the simulator shares the host's network stack), so a foreign listener would serve
    /// foreign bytes. A foreign server cannot serve an image whose natural size is the one we
    /// assert — and a 200 on `/missing.png` fails case [2] loudly here rather than silently
    /// there.
    func testCleartextLoopbackIsPermittedByATS() throws {
        server.release()

        let ok = server.fetch(BnImageFixtureServer.FIXED_URL)
        XCTAssertNil(ok.error,
                     "cleartext HTTP to 127.0.0.1 must be PERMITTED (Info.plist → "
                     + "NSAllowsLocalNetworking). If this errors, EVERY image on /image fails — "
                     + "and TWO OF THE THREE DEMO ASSERTIONS STILL PASS, because a blocked load is "
                     + "indistinguishable from the 404 that case [2] expects")
        XCTAssertEqual(ok.status, 200, "…and the fixture is served")

        let decoded = try server.decoded(ok.body)
        XCTAssertEqual(decoded.cgImage?.width, BnImageFixtureServer.FIXED_W,
                       "…and the bytes are OURS: the fixture's own pixel width")
        XCTAssertEqual(decoded.cgImage?.height, BnImageFixtureServer.FIXED_H,
                       "…and its own pixel height")

        let missing = server.fetch(BnImageFixtureServer.MISSING_URL)
        XCTAssertNil(missing.error, "the failing case must FAIL AT THE HTTP LAYER, not at ATS")
        XCTAssertEqual(missing.status, 404,
                       "…it is a REAL 404 from a REAL server — not a dropped connection, and (the "
                       + "point) not a blocked request wearing a 404's clothes")
    }

    // ── [2] THE TWO FRAME TABLES, and the difference between them is the phase ──

    /// BEFORE (`BnImageDemo.razor` §"THE FRAME TABLE, BEFORE THE BYTES LAND"):
    /// ```
    /// root BnColumn        (0, 0, Whost, 180+Hb)   width FILLS the host; height HUGS
    ///  ├─ [0] fixed sect.  (0,   0, 300, 140)
    ///  │    ├─ image       (0,   0, 200, 120)      declared → NEVER measured
    ///  │    └─ band F      (0, 120, 300,  20)
    ///  ├─ [1] intr. sect.  (0, 140, 300,  20)
    ///  │    ├─ image       (0,   0,   0,   0)      no bytes → 0 × 0
    ///  │    └─ band I      (0,   0, 300,  20)      ← y = 0
    ///  ├─ [2] fail. sect.  (0, 160, 300,  20)
    ///  │    ├─ image       (0,   0,   0,   0)
    ///  │    └─ band X      (0,   0, 300,  20)
    ///  └─ [3] back sect.   (0, 180, 300,  Hb)
    /// ```
    /// AFTER — the intrinsic image's bytes arrive, the shell records their natural size, marks
    /// its Yoga node dirty and re-solves; ONE reflow, never two:
    /// ```
    /// root BnColumn        (0, 0, Whost, 180+Hi+Hb)  the height GREW by Hi
    ///  ├─ [0] fixed sect.  (0,      0, 300, 140)     UNCHANGED ─┐ THE NO-REFLOW PROOF:
    ///  │    ├─ image       (0,      0, 200, 120)      UNCHANGED │ asserted as an IDENTITY
    ///  │    └─ band F      (0,    120, 300,  20)      UNCHANGED ┘ between two frames of
    ///  ├─ [1] intr. sect.  (0,    140, 300, Hi+20)                the SAME node
    ///  │    ├─ image       (0,      0,  Wi,  Hi)     ← the NATURAL size
    ///  │    └─ band I      (0,     Hi, 300,  20)     ← y: 0 → Hi. THE REFLOW PROOF
    ///  ├─ [2] fail. sect.  (0, 160+Hi, 300,  20)     moved down by [1]'s reflow…
    ///  │    ├─ image       (0,      0,   0,   0)     …but ITS image stayed 0 × 0…
    ///  │    └─ band X      (0,      0, 300,  20)     …so y = 0 IN ITS PARENT: THE FAILURE
    ///  └─ [3] back sect.   (0, 180+Hi, 300,  Hb)        RESERVED NOTHING
    /// ```
    /// `Wi × Hi` is the fixture's natural PIXEL size, read off the **decoded fixture** — never
    /// a constant this file invents.
    func testTheFrameTablesBEFOREAndAFTERTheBytes() throws {
        // ── THE FIXTURE CONTRACT, on the DECODED fixtures, BEFORE ANY FRAME ──
        let intrinsic = try server.decoded(server.intrinsicPng)
        let fixed = try server.decoded(server.fixedPng)
        bnAssertFixtureContract(intrinsic: intrinsic, fixed: fixed)
        let wi = intrinsic.size.width
        let hi = intrinsic.size.height

        let root = try mount()

        // THE CANONICAL BEFORE TABLE — BnDemoFrameTables.swift, pinned against the Android
        // shell's twin in the REQUIRED lane (M6 audit, F2).
        let b = bnImageDemoBeforeFrames

        // ══ BEFORE THE BYTES ═════════════════════════════════════════════════
        XCTAssertEqual(mapper.imageTerminalCount, 0,
                       "no request has terminated — the fixture server is HOLDING every response, "
                       + "which is the only thing that makes this 'before' honest. (Kingfisher's "
                       + "caches were cleared in setUp, so all three go to the wire.)")
        XCTAssertEqual(root.subviews.count, 4, "four sections: fixed, intrinsic, failing, back")

        // [0] FIXED — first on purpose: nothing above it can ever move it, so its "did not
        // move" is a fact about the IMAGE and not about the page.
        let fixedSection = root.subviews[0]
        assertFrame(b, "[0] fixed section", fixedSection, "HUGS 120 + 20")
        assertFrame(b, "[0] fixed image", try bnImageIn(fixedSection), "Width AND Height declared")
        assertFrame(b, "[0] band F", fixedSection.subviews[1], "BEFORE: y = 120")
        let fixedImageFrameBefore = try bnImageIn(fixedSection).frame

        // [1] INTRINSIC — 0 × 0. Not "small": ZERO.
        let intrinsicSection = root.subviews[1]
        assertFrame(b, "[1] intrinsic section", intrinsicSection, "HUGS 0 + 20")
        assertFrame(b, "[1] intrinsic image", try bnImageIn(intrinsicSection),
                    "BEFORE: a measured leaf with no bytes measures 0 × 0")
        assertFrame(b, "[1] band I", intrinsicSection.subviews[1],
                    "BEFORE: y = 0 — THE REFLOW HAS NOT HAPPENED")
        XCTAssertNil(try bnImageIn(intrinsicSection).image, "[1] nothing painted yet")

        // [2] FAILING — structurally identical to [1]; only the URL differs.
        let failingSection = root.subviews[2]
        assertFrame(b, "[2] failing section", failingSection, "HUGS 0 + 20")
        assertFrame(b, "[2] failing image", try bnImageIn(failingSection), "BEFORE: 0 × 0")
        assertFrame(b, "[2] band X", failingSection.subviews[1], "BEFORE: y = 0 in its parent")

        // [3] the back row — the page's only measured leaf, deliberately LAST. Its HEIGHT is
        // MEASURED in the table (a font metric is nobody's to invent); its y = 180 is not.
        assertFrame(b, "[3] back section", root.subviews[3])
        assertRootFrame(root: root, backSection: root.subviews[3])

        // ══ THE GATE OPENS ═══════════════════════════════════════════════════
        server.release()
        bnAwaitImageResults(mapper, 3)

        // THE OUTCOMES, against the URLs the WIRE carried — which is also the drift pin on
        // BnImageDemo.razor's three `internal const` sources (a device-side test cannot read a
        // .razor file; it CAN read what the renderer put on the wire). A blocked-ATS simulator produces
        // three ERRORs and reddens HERE, by name, instead of quietly passing two of three frame
        // assertions.
        XCTAssertEqual(Set(mapper.imageResults.map { BnUrlOutcome($0) }),
                       [BnUrlOutcome(url: BnImageFixtureServer.FIXED_URL, outcome: .success),
                        BnUrlOutcome(url: BnImageFixtureServer.INTRINSIC_URL, outcome: .success),
                        BnUrlOutcome(url: BnImageFixtureServer.MISSING_URL, outcome: .error)],
                       "[0] and [1] SUCCEEDED and [2] genuinely 404'd — all three from OUR loopback "
                       + "fixture server, and not one of them CANCELLED (nothing on this page "
                       + "cancels anything: a cancel here is a setup failure)")

        // ══ AFTER THE BYTES ══════════════════════════════════════════════════
        // THE CANONICAL AFTER TABLE, parameterised by the DECODED fixture's own pixel size —
        // never a constant this file invents. Its Android twin declares the same rows with the
        // same `wi`/`hi` symbols, and the drift test compares them AS SYMBOLS: `hi + 20` on
        // both shells is an equality it can check without knowing the number.
        let a = bnImageDemoAfterFrames(wi: wi, hi: hi)

        // [0] THE NO-REFLOW PROOF — asserted as an IDENTITY between two frames of the same
        // node, not as a number. Both axes are definite, so Yoga never called its measure func
        // at all and the fixture's own size is nowhere in this frame.
        assertFrame(a, "[0] fixed section", fixedSection, "AFTER: UNCHANGED")
        XCTAssertEqual(try bnImageIn(fixedSection).frame, fixedImageFrameBefore,
                       "[0] THE NO-REFLOW PROOF: the fixed image's frame is IDENTICAL, number for "
                       + "number, to the one it had before its bytes landed")
        assertFrame(a, "[0] band F", fixedSection.subviews[1], "AFTER: y = 120. IT DID NOT MOVE")
        XCTAssertNotNil(try bnImageIn(fixedSection).image,
                        "[0] …and its bytes DID land — which is what makes 'it did not move' mean "
                        + "something")

        // [1] THE REFLOW.
        let intrinsicImage = try bnImageIn(intrinsicSection)
        assertFrame(a, "[1] intrinsic image", intrinsicImage,
                    "AFTER: its NATURAL size — the DECODED FIXTURE's own \(Int(wi)) × \(Int(hi)) "
                    + "PIXELS, read as POINTS. One file pixel is one dp/pt, which is the only "
                    + "reading under which iOS and Android compute the same frame")
        XCTAssertTrue(intrinsicImage.frame.width > 0 && intrinsicImage.frame.height > 0,
                      "[1] POSITIVELY: Wi > 0 AND Hi > 0. Two of this page's three cases assert "
                      + "'nothing moved', and a suite of negatives is one that a TOTAL FAILURE "
                      + "satisfies")
        assertFrame(a, "[1] band I", intrinsicSection.subviews[1],
                    "THE REFLOW PROOF: band I moved from y = 0 to y = Hi. The image's own frame "
                    + "could be faked by a shell that painted and never re-solved; the BAND's y "
                    + "could not")
        assertFrame(a, "[1] intrinsic section", intrinsicSection,
                    "…and the section grew by exactly Hi")

        // [2] THE FAILURE RESERVED NOTHING — two facts at once, and the frames are
        // parent-relative so both are visible: the SECTION slid down by Hi (because [1] grew
        // above it) while the band INSIDE it stayed at y = 0.
        assertFrame(a, "[2] failing section", failingSection,
                    "moved down by Hi — [1]'s reflow, propagating downward")
        assertFrame(a, "[2] failing image", try bnImageIn(failingSection),
                    "…but ITS image stayed 0 × 0")
        assertFrame(a, "[2] band X", failingSection.subviews[1],
                    "…so band X is still at y = 0 IN ITS PARENT: THE FAILURE RESERVED NOTHING")
        XCTAssertNil(try bnImageIn(failingSection).image, "[2] and nothing was painted")

        // [3]
        assertFrame(a, "[3] back section", root.subviews[3], "moved down by Hi too")

        assertRootFrame(root: root, backSection: root.subviews[3])
        XCTAssertEqual(mapper.inFlightImageCount, 0, "nothing is left in flight")
    }

    // ── [3] THE SECOND MOUNT, WITH A WARM CACHE ──────────────────────────────

    /// **THE PATH EVERY OTHER TEST CLEARS AWAY** (design §"A memory-cache hit completes
    /// SYNCHRONOUSLY").
    ///
    /// Kingfisher's callback queue is `.mainCurrentOrAsync`, and the shell issues its request
    /// from `UpdateProp("src", …)` — **on the main thread, inside `applyBatch`**. So on a
    /// memory-cache hit the whole request — set-image, natural size, `markDirty`, re-solve —
    /// runs **to completion inside the call, before it returns**. That is the ordinary case on
    /// the SECOND mount of any page the process has already fetched, and it is exactly what
    /// `setUp`'s `bnClearImageCaches()` hides.
    ///
    /// **This is NOT the reset collision's habitat, and it is worth saying so**: [mount] builds
    /// a FRESH `BnWidgetMapper` each time, so the second mount's node ids are handed out into
    /// empty maps and no stale entry from the first can survive to meet them. The collision
    /// needs ONE mapper outliving a navigation, which no test here can stage and which is
    /// exactly why `BnImageGuardTests` writes the four states down as a pure function. What THIS
    /// test proves is the synchronous-completion path: that a warm double mount is clean.
    ///
    /// The mapper-level twin (`BnImageTests.testAWarmCacheCompletesInsideTheBatch…`) is where
    /// the LAYOUT PASS COUNT is pinned — the one assertion that can see "one reflow, never two"
    /// when the reflow is synchronous. This one asserts the FRAMES and the in-flight invariant:
    /// the AFTER table must be identical whether the bytes came from the network or the cache.
    func testTheSecondMountWithAWarmCacheProducesTheSameFrames() throws {
        let intrinsic = try server.decoded(server.intrinsicPng)
        let wi = intrinsic.size.width
        let hi = intrinsic.size.height

        server.release() // both mounts fetch freely; there is no BEFORE table to protect here

        // ── MOUNT 1: cold cache (setUp cleared it). This is what WARMS it. ──
        _ = try mount()
        bnAwaitImageResults(mapper, 3)
        XCTAssertEqual(mapper.imageTerminalCount, 3, "mount 1 fetched all three over real HTTP")
        XCTAssertEqual(mapper.inFlightImageCount, 0, "…and left nothing in flight")

        // ── MOUNT 2: NO bnClearImageCaches(). fixed.png and intrinsic.png are MEMORY-CACHE
        //    HITS, so their completions run INSIDE applyBatch, synchronously, before the
        //    request even returns. missing.png still goes to the wire (a 404 is not cached),
        //    which is why the synchronization gate below still has work to do.
        let root = try mount()
        bnAwaitImageResults(mapper, 3)

        // THE INVARIANT THAT BREAKS. A synchronously-completed request that was recorded anyway
        // sits in the in-flight map forever: the completion's clear ran BEFORE the entry
        // existed. Two cached images ⇒ this would be 2.
        XCTAssertEqual(mapper.inFlightImageCount, 0,
                       "NOTHING IS LEFT IN FLIGHT after a WARM-cache mount. A memory hit completes "
                       + "INSIDE the call, so its bookkeeping runs BEFORE the shell has anything to "
                       + "record — and an unconditional record leaks the handle for the life of the "
                       + "mapper. On iOS that stale entry is a request nothing can cancel, whose "
                       + "completion marks a freed YGNodeRef dirty.")

        // …AND THE FRAMES ARE THE SAME ONES — literally: the SAME AFTER table the network mount
        // asserts, read a second time rather than transcribed a second time.
        let a = bnImageDemoAfterFrames(wi: wi, hi: hi)

        let fixedSection = root.subviews[0]
        assertFrame(a, "[0] fixed section", fixedSection, "from cache: UNCHANGED")
        assertFrame(a, "[0] fixed image", try bnImageIn(fixedSection),
                    "from cache: its DECLARED size, still")
        XCTAssertNotNil(try bnImageIn(fixedSection).image, "[0] …and the cached bytes were painted")

        let intrinsicSection = root.subviews[1]
        assertFrame(a, "[1] intrinsic image", try bnImageIn(intrinsicSection),
                    "from cache: its NATURAL size — the same \(Int(wi)) × \(Int(hi)) it measured "
                    + "from the network. One reflow, never two, whether the bytes arrive "
                    + "synchronously or not")
        assertFrame(a, "[1] band I", intrinsicSection.subviews[1],
                    "THE REFLOW STILL HAPPENED, from inside the batch: band I is at y = Hi")
        assertFrame(a, "[1] intrinsic section", intrinsicSection,
                    "…and the section grew by exactly Hi")

        // [2] still fails — a 404 is not cached, so this one really did go to the wire, which is
        // what keeps the synchronization gate above honest on this mount.
        let failingSection = root.subviews[2]
        assertFrame(a, "[2] failing image", try bnImageIn(failingSection),
                    "the failure still reserves nothing")
        assertFrame(a, "[2] band X", failingSection.subviews[1],
                    "…and its band is still at y = 0 in its parent")

        assertFrame(a, "[3] back section", root.subviews[3],
                    "the back row is where the reflow put it")
        assertRootFrame(root: root, backSection: root.subviews[3])
    }

    // ── [4] the back row ─────────────────────────────────────────────────────

    /// The page's only measured leaf, deliberately LAST so a font-dependent height cannot
    /// shift the frames the parity assertion is built on. Asserted by ORACLE — no font
    /// constant is anyone's to invent (`BnLayoutDemoTests`' rule).
    func testTheBackRowIsThePagesOnlyMeasuredLeaf() throws {
        server.release()
        let root = try mount()
        bnAwaitImageResults(mapper, 3)

        let backSection = root.subviews[3]
        let back = try XCTUnwrap(backSection.subviews[0] as? UIButton, "the back leaf must be a UIButton")
        XCTAssertEqual(back.title(for: .normal), "← Back")
        XCTAssertEqual(backSection.frame.width, sectionW, accuracy: 0.5, "the back row is 300pt wide")
        XCTAssertEqual(backSection.frame.height, back.frame.height, accuracy: 0.5,
                       "it declares no height and HUGS the button's MEASURED height")
        assertOracle("the measured back button", back, availableWidth: backSection.frame.width)
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// **THE ROOT'S OWN FRAME — it is ASYMMETRIC, and both halves are asserted.**
    ///
    /// The wire root (`BnImageDemo`'s `BnColumn`) is NOT the shell's Yoga root: the shell owns
    /// a SYNTHETIC host root sized to the host view, and the wire root is its only child. That
    /// host root is a column with Yoga's default `alignItems: stretch`, so the wire root
    /// **FILLS the host's WIDTH** (it is NOT 300 — that is each SECTION's width) and **HUGS in
    /// HEIGHT**. Asserting that it does *not* fill the height is the 6.1 host-root pin: it
    /// catches a host that re-lays out top-level nodes behind Yoga's back. `BnScrollDemoTests`'
    /// two root assertions are the template.
    private func assertRootFrame(root: UIView, backSection: UIView,
                                 file: StaticString = #filePath, line: UInt = #line) {
        XCTAssertEqual(root.frame.width, host.bounds.width, accuracy: 0.5,
                       "the root column FILLS THE HOST'S WIDTH (default alignItems: stretch on the "
                       + "synthetic host root) — it is not 300, and it is not the sections' union",
                       file: file, line: line)
        XCTAssertEqual(root.frame.height, backSection.frame.maxY, accuracy: 0.5,
                       "…and HUGS in HEIGHT: it ends where the back row ends. The pin that catches "
                       + "a host root re-laying out top-level nodes behind Yoga's back",
                       file: file, line: line)
        XCTAssertLessThan(root.frame.height, host.bounds.height,
                          "…so it does NOT fill the host's height, and the assertion above is not a "
                          + "coincidence", file: file, line: line)
    }

    /// Pumps the MAIN runloop (draining the mapper's `DispatchQueue.main.async` batch) until
    /// the mount frame has been applied AND laid out: four sections under the root, and a root
    /// with a computed height.
    private func pollForDemo(deadline seconds: TimeInterval = 30) throws -> UIView {
        let end = Date().addingTimeInterval(seconds)
        while Date() < end {
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.05))
            if let root = host.subviews.first, root.subviews.count == 4, root.frame.height > 0 {
                return root
            }
        }
        XCTFail("BnImageDemo never rendered a laid-out tree within \(Int(seconds))s")
        throw BnRuntimeError.mountFailed(rc: -1, component: "BnImageDemo")
    }
}
