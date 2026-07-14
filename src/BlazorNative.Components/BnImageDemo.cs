using BlazorNative.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorNative.Components;

// ─────────────────────────────────────────────────────────────────────────────
// BnImageDemo — Phase 6.3 Task 1.2 (design §"The proof surface"), route
// "/image", mount-registry key "BnImageDemo".
//
// The image demo is a FIFTH page, for the reason the third and fourth exist:
// BnDemo, BnSettingsPage, BnLayoutDemo and BnScrollDemo keep their goldens on
// every surface, so image bugs never arrive mixed with golden-rewrite noise.
// BnLayoutDemo's 22-number table and BnScrollDemo's ten-row table ARE the
// cross-platform parity contract; a new capability does not get to rewrite them.
//
// Its whole job is to make the THREE MEASUREMENT PATHS OF AN IMAGE OBSERVABLE,
// because three surfaces assert against them: the .NET patch golden
// (BnImageDemoTests), the Android instrumented frames (Gate 2, Coil) and the iOS
// XCTest frames (Gate 3, Kingfisher) — and the last two assert THE SAME NUMBERS.
// Yoga computes in density-independent units on both platforms (iOS points 1:1;
// Android multiplies by density at frame-apply time), so the tables below are in
// dp/pt.
//
// ── EVERY CASE HAS A SIBLING UNDER IT, AND THE SIBLING IS THE PROOF ──────────
// An image's OWN frame is a bad witness. A shell could paint the bytes into the
// view and never re-solve the tree, and the image would still LOOK right on a
// screenshot while the layout underneath it was a lie. What cannot be faked is
// WHAT MOVED: only a genuine re-solve moves the node BELOW the image. So each of
// the three cases is an image with a plain 20dp coloured band beneath it, and the
// assertion Gates 2/3 write is about the BAND's y, not the image's.
//
// The cases are numbered THE WAY THE GOLDEN INDEXES THEM — `sections[i]`, 0-based,
// in wire order. `[0]`, `[1]`, `[2]`, `[3]` mean the same thing in this header, in
// BnImageDemoTests and in the shells' `root.getChildAt(i)`. There is no other
// numbering scheme on this page.
//
//   [0] FIXED (200 × 120)   → band F's y is 120 BEFORE the bytes and 120 AFTER.
//                             Identical. THE NO-REFLOW PROOF.
//   [1] INTRINSIC (no size) → band I's y is 0 BEFORE the bytes and Hi AFTER.
//                             It MOVED. THE REFLOW PROOF.
//   [2] FAILING (404 URL)   → band X's y is 0 BEFORE and 0 AFTER — in its parent.
//                             The failure RESERVED NOTHING.
//   [3] the back row.
//
// ── AND [0] IS FIRST, ON PURPOSE ─────────────────────────────────────────────
// A no-reflow assertion is only honest if nothing ABOVE the node could have moved
// it for an unrelated reason. The fixed case is the root column's FIRST child, so
// its y is 0 by construction and no reflow anywhere on this page can touch it.
// [1]'s reflow propagates DOWNWARD (into [2] and the back row) and nowhere else —
// which is exactly why [2]'s "reserves nothing" assertion is stated
// PARENT-RELATIVE: its section moves down by Hi (because [1] grew above it), while
// the band INSIDE it stays at y = 0 (because its own image reserved nothing). Two
// different facts; the frames are parent-relative, so both are visible at once.
//
// ── THE FRAME TABLE, BEFORE THE BYTES LAND (dp, RELATIVE TO THE PARENT) ──────
//
// This is the MOUNT state — the first frame each shell applies, with both measured
// images reporting 0 × 0 (they are Yoga leaves with a measure func — 6.1 attaches
// it BY NODETYPE, and `image` is already in the set — and an image with no bytes
// measures to zero, by construction rather than by accident).
//
// root BnColumn        (0, 0, Whost, 180+Hb)  ← SEE "THE ROOT'S OWN FRAME" BELOW.
//  │                                            Whost = the HOST view's width in dp
//  │                                            (NOT 300); the height HUGS.
//  ├─ [0] fixed section  (0,   0, 300, 140)   BnColumn W=300, NO height → hugs 120+20
//  │    ├─ image (fixed) (0,   0, 200, 120)   Width=200 Height=120 → NEVER measured
//  │    └─ band F        (0, 120, 300,  20)   ← y = 120
//  ├─ [1] intrinsic sect.(0, 140, 300,  20)   hugs 0 + 20
//  │    ├─ image (intr.) (0,   0,   0,   0)   NO Width/Height → measures 0 × 0
//  │    └─ band I        (0,   0, 300,  20)   ← y = 0
//  ├─ [2] failing sect.  (0, 160, 300,  20)   hugs 0 + 20
//  │    ├─ image (fail)  (0,   0,   0,   0)
//  │    └─ band X        (0,   0, 300,  20)   ← y = 0
//  └─ [3] back section   (0, 180, 300,  Hb)   BnRow W=300, NO height
//       └─ "← Back"      (0,   0,  Wb,  Hb)   Hb/Wb = the button's MEASURED size
//
// ── THE FRAME TABLE, AFTER THE BYTES LAND ───────────────────────────────────
//
// The intrinsic image's bytes arrive; the shell sets the image ON THE MAIN THREAD,
// marks the Yoga node dirty (the 6.1 path) and re-solves (the 6.2 path). ONE
// reflow, never two. `Wi × Hi` is THE FIXTURE'S NATURAL PIXEL SIZE IN dp/pt —
// SYMBOLIC, deliberately: Gates 2/3 supply the fixture and assert against its real
// size. It is NOT a constant this file gets to invent.
//
// THIS TABLE MAY ONLY BE ASSERTED ONCE ALL THREE REQUESTS HAVE TERMINATED — see
// "THE SYNCHRONIZATION GATE" below. It is the single most dangerous thing on this
// page: two of the three rows are "nothing moved", and a suite that asserts them
// before a byte has been fetched passes them both.
//
// root BnColumn        (0, 0, Whost, 180+Hi+Hb)  ← the height GREW by Hi; the width
//  │                                               did NOT change (it is the host's)
//  ├─ [0] fixed section  (0,      0, 300, 140)   UNCHANGED ─┐
//  │    ├─ image (fixed) (0,      0, 200, 120)   UNCHANGED  │ THE NO-REFLOW PROOF:
//  │    └─ band F        (0,    120, 300,  20)   UNCHANGED ─┘ every number identical
//  ├─ [1] intrinsic sect.(0,    140, 300, Hi+20)  grew by Hi
//  │    ├─ image (intr.) (0,      0,  Wi,  Hi)    ← 0×0 → the NATURAL size
//  │    └─ band I        (0,     Hi, 300,  20)    ← y: 0 → Hi. THE REFLOW PROOF.
//  ├─ [2] failing sect.  (0, 160+Hi, 300,  20)    moved down by [1]'s reflow…
//  │    ├─ image (fail)  (0,      0,   0,   0)    …but ITS image stayed 0 × 0…
//  │    └─ band X        (0,      0, 300,  20)    …so y = 0 IN ITS PARENT, still:
//  │                                               THE FAILURE RESERVED NOTHING.
//  └─ [3] back section   (0, 180+Hi, 300,  Hb)
//
//   The fixed image's bytes also land, and change NOTHING: Width AND Height are
//   both definite, so Yoga never calls its measure func at all. That is the
//   contract's second row ("the bytes never move the frame"), and it is asserted
//   as an IDENTITY between two frames of the same node, not as a number.
//
// ── THE ROOT'S OWN FRAME (do not guess it — it is asymmetric) ───────────────
// The wire root (this page's BnColumn) is NOT the shells' Yoga root: each shell
// owns a SYNTHETIC host root sized to the host view, and the wire root is its only
// child. That host root is a column with Yoga's default `alignItems: stretch`, so:
//
//   • WIDTH  — the root FILLS THE HOST'S WIDTH. It declares none, and stretch gives
//     it the host's. It is NOT 300 (300 is each SECTION's width), and it is NOT the
//     sections' union. Assert it against the host view's width, in dp.
//   • HEIGHT — the root HUGS its four sections: 140 + 20 + 20 + Hb before the bytes,
//     and Hi more after. It does NOT fill the host, and asserting that it does not
//     (root.height != host.height, root.height == backSection.bottom) is the 6.1
//     host-root pin: it catches a host that re-lays out top-level nodes behind
//     Yoga's back. BnScrollDemo's table pins exactly this pair, and its Android test
//     is the template (`BnScrollDemoAndroidTest`: "the root column must fill the
//     host's width" + "the root column HUGS its two sections").
//
// The prose used to say the root "hugs its four sections" full stop. It hugs in ONE
// axis. Gates 2/3 assert both, so both are stated.
//
// ── THE SYNCHRONIZATION GATE: WHAT A SHELL MUST AWAIT ───────────────────────
//
// NORMATIVE, AND THE MOST IMPORTANT PARAGRAPH IN THIS FILE.
//
//   THE "AFTER" TABLE MAY ONLY BE ASSERTED ONCE ALL THREE REQUESTS — [0]'s, [1]'s
//   AND [2]'s — HAVE TERMINATED (succeeded or failed). Not one of them. All three.
//
// Why this has to be said: THE WIRE CARRIES NO COMPLETION SIGNAL, by design. There
// is no `OnError`, no `OnLoad` (design decision 3 — each changes measurement), so
// .NET never learns that a fetch finished and no patch marks the "after" state. The
// only thing that changes is a FRAME, in the shell, and only for ONE of the three
// cases:
//
//   [0] FIXED     — the AFTER frame is IDENTICAL to the BEFORE frame.
//   [1] INTRINSIC — the AFTER frame differs (band I: y 0 → Hi). SELF-SIGNALLING.
//   [2] FAILING   — the AFTER frame is IDENTICAL to the BEFORE frame.
//
// So an implementer who asserts the AFTER table immediately after mount — before a
// single byte has been fetched — PASSES [0] AND PASSES [2], having proven nothing
// about either. Only [1] would go red. And that is the good case; the bad one is
// worse, because it composes with the cleartext trap below: if HTTP is blocked, all
// three fetches fail, [0] still passes (its frame is definite), [2] still passes (a
// BLOCKED LOAD IS INDISTINGUISHABLE FROM A 404 — that is the whole point of a
// negative assertion), and only [1] goes red. Two of three assertions would certify
// a contract on a device that never loaded a byte.
//
// Therefore:
//
//   1. EACH SHELL AWAITS ITS OWN LOADER'S PER-NODE TERMINAL CALLBACK, for all three
//      image nodes:
//        • Android — Coil's `ImageRequest.Listener`: `onSuccess` OR `onError`
//          (also `onCancel`, which on this page is a failure of the test setup).
//        • iOS — Kingfisher's `completionHandler`: `.success` OR `.failure`.
//      Three terminations, counted; THEN assert. A `CountDownLatch(3)` /
//      `XCTestExpectation` with `expectedFulfillmentCount = 3`, with a timeout that
//      FAILS the test rather than proceeding.
//   2. DO NOT GATE ON BAND I's MOVEMENT. Polling until `band I.y == Hi` is the
//      obvious shortcut and it is wrong: it observes ONLY case [1]. It cannot tell
//      you that [0]'s request finished (so [0]'s "unchanged" is unproven) and it
//      cannot tell you that [2]'s request finished (so [2]'s "reserved nothing" is
//      unproven — you may simply be looking at a request still in flight).
//   3. THE RE-SOLVE THE TERMINAL CALLBACK TRIGGERS IS ASYNCHRONOUS TO IT. The
//      callback fires on the main thread, then sets the image / marks dirty /
//      re-solves. Read the frames AFTER that unit of work, not inside the callback.
//
// ── AND A POSITIVE ASSERTION, NOT ONLY NEGATIVE ONES ────────────────────────
// Two of the three cases assert "did not move". A suite of negatives is a suite
// that a total failure satisfies. So Gates 2/3 MUST ALSO assert, POSITIVELY:
//
//   • `Wi > 0` and `Hi > 0` on the intrinsic image node's computed frame — the
//     bytes REALLY arrived and REALLY measured. This is what makes "band F did not
//     move" mean "the bytes landed and did not move it" instead of "no bytes
//     landed".
//   • …and before ANY frame assertion, that the DECODED FIXTURE satisfies the
//     contract below — `0 < Wi ≤ 300`, `Hi > 0`, `(Wfixed, Hfixed) ≠ (200, 120)`.
//     An unasserted fixture constraint is a coincidence waiting to happen.
//
// ── THE FIXTURE'S CONTRACT (what Gates 2/3 must pick, and why) ──────────────
//   • Wi ≤ 300. A section is 300 wide, so the measure func is called with
//     AT_MOST(300); a wider fixture would raise "does the shell clamp, and does it
//     clamp the same way on both platforms?", which is a question this phase
//     deliberately does not answer (no ContentMode — design decision 3).
//   • Hi > 0, and comfortably so: Hi IS the reflow. A 0-high fixture would make
//     the reflow assertion vacuously true.
//   • The FIXED image's fixture must have a natural size that is NOT 200 × 120.
//     Otherwise "it measures 200 × 120" is a coincidence, not a proof that the
//     declared size short-circuits measurement. (It may be the same file as the
//     intrinsic fixture — Wi × Hi ≠ 200 × 120 is the only requirement.)
//   • No downsampling that changes the reported size. The measured size is the
//     image's NATURAL size, not a decoder's chosen sample size (the parity
//     contract's last row) — configure Coil and Kingfisher to it, and assert the
//     number against a fixture whose pixel size you know.
//
// EVERY ONE OF THOSE FOUR IS AN ASSERTION IN THE TEST, evaluated on the DECODED
// fixture before any frame is looked at — not a comment, and not a property of a
// file someone once checked in. They are also the probe that the bytes came from
// OUR fixture server (see the port note below).
//
// ── CLEARTEXT HTTP IS BLOCKED BY DEFAULT. ON BOTH PLATFORMS. ────────────────
//
// NORMATIVE. This is not an environment quirk to be discovered at Gate 2/3; it is a
// precondition of the capability, and its failure mode is SILENT (see the
// synchronization gate above: a blocked load looks exactly like the 404 that case
// [2] expects, so two of three assertions stay green).
//
//   ANDROID — `targetSdk ≥ 28` blocks cleartext HTTP outright. This repo already ate
//     this once, in Phase 3.1. It is ALREADY HANDLED, BY INHERITANCE, FOR THE
//     INSTRUMENTED TESTS: `src/BlazorNative.Jni/src/debug/res/xml/
//     network_security_config.xml` permits cleartext to `127.0.0.1` / `localhost`
//     (and `10.0.2.2`), and instrumented tests run the DEBUG build. Gate 2 needs no
//     new manifest work — but it must KNOW that, because:
//     THE RELEASE COROLLARY: `src/androidMain/res/xml/network_security_config.xml`
//     is main's secure default and permits NO cleartext. A RELEASE build of the demo
//     app therefore shows THREE FAILED IMAGES on "/image". That is correct, expected
//     behaviour for a demo whose fixtures are loopback HTTP — not a bug to be
//     "fixed" by weakening the release config. If a release demo must ever show the
//     images, the answer is to bundle the fixture as an asset, not to permit
//     cleartext in release.
//   iOS — App Transport Security blocks arbitrary HTTP, and KINGFISHER FETCHES
//     THROUGH `URLSession`, SO ATS APPLIES TO IT. `src/BlazorNative.Apple/BnHost/
//     Info.plist` today has NO `NSAppTransportSecurity` key at all. GATE 3 MUST ADD
//     ONE — `NSAppTransportSecurity` → `NSAllowsLocalNetworking = true` (the narrow,
//     loopback-only exemption; NOT `NSAllowsArbitraryLoads`). Without it, Gate 3
//     rediscovers this as a mysterious fetch failure whose only symptom is that
//     case [1] is red and the other two are green.
//
// ── THE URLS ARE LOOPBACK. CI NEVER TOUCHES THE PUBLIC INTERNET ─────────────
// 6.3 non-negotiable #5, and it is not negotiable because a suite whose green
// depends on a remote host is not a suite. The three sources point at a fixture
// server the SHELLS run IN-PROCESS, and the failing case is a path that server
// 404s. So:
//   • the fetch is a REAL network fetch, through Coil and Kingfisher, over HTTP —
//     which is the capability this phase is actually shipping;
//   • the failure is a REAL failure, deterministic, and offline;
//   • no test anywhere depends on a host that can go down.
//
// WHAT "LOOPBACK" ACTUALLY MEANS, PER PLATFORM — because the two are NOT the same,
// and the difference is exactly the sentence Gate 3 would otherwise trust:
//   • ANDROID (AVD) — `127.0.0.1` IS the emulated device's own loopback. A separate
//     network stack; the host machine's is not involved. (The host is reachable, but
//     only through the emulator's `10.0.2.2` alias — a DIFFERENT address.)
//   • iOS (SIMULATOR) — the simulator is a process on macOS and SHARES THE HOST'S
//     NETWORK STACK. `127.0.0.1` inside the simulator IS the host Mac's loopback.
//     The offline guarantee still holds (nothing leaves the machine), but the
//     REASON is different, and two consequences follow that "the device's own
//     loopback" would hide:
//       1. PORT 8099 IS HOST-GLOBAL on the macOS CI runner. Any other process on
//          8099 — a leftover server from a previous run, another job on a shared
//          runner — would serve the fixture instead of ours. So Gate 3's fixture
//          server must BIND EXCLUSIVELY and FAIL LOUDLY if the port is taken (never
//          fall back to "someone is already listening, good enough"), and must PROVE
//          THE BYTES ARE OURS before asserting a frame. The fixture-contract
//          assertions above ARE that probe: a foreign server cannot serve an image
//          whose natural size is the one we assert, and a 200-response on
//          "/missing.png" fails case [2] loudly rather than silently.
//       2. ATS applies (see above).
//
// A shell that would rather serve the same bytes from a bundled asset may — THE
// FRAMES ARE THE CONTRACT, not the transport (design §"The parity contract":
// parity is asserted on frames, never on cache internals). But it must then answer
// for what a 404 means, and the loopback server already does — and it would no
// longer be exercising the HTTP path this phase ships.
//
// ── WHY THE BANDS CARRY NO TEXT ─────────────────────────────────────────────
// A label's height is a font's business, not a constant we get to invent
// (BnLayoutDemo puts its measured leaves LAST for exactly this reason). Every band
// is a fixed-height coloured strip, so every number above is fixed on both
// platforms except the two that are SUPPOSED to be measured — the intrinsic image
// (Wi × Hi) and the back button (Wb × Hb), and the button is LAST.
//
// Nav parity with the other pages: "← Back" → INavigationManager → "/". Reachable
// by mount-name ("BnImageDemo") and by route ("/image"); no button on BnDemo points
// here — adding one would churn BnDemo's four goldens for no engine reason.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The image proof page — see the file header for the pinned frame
/// tables (there are TWO: before the bytes land and after, and that difference is
/// the phase). A fresh ROOT component (like BnScrollDemo): no cascaded theme
/// crosses a root swap.</summary>
public sealed class BnImageDemo : ComponentBase
{
    // ── THE FIXTURE ORIGIN ────────────────────────────────────────────────────
    //
    // Loopback. The shells serve the fixture in-process and CI never reaches the
    // public internet (non-negotiable #5). Gates 2/3 read these constants rather
    // than transcribing the strings — a URL retyped in three places is a URL that
    // drifts in two.
    //
    // WHOSE loopback it is DIFFERS BY PLATFORM, and the file header says so at
    // length because Gate 3 depends on it: on the AVD, 127.0.0.1 is the emulated
    // device's own; in the iOS SIMULATOR it is the HOST MAC's, because the
    // simulator shares the host's network stack. The offline guarantee holds either
    // way; the port does not.

    /// <summary>The in-process fixture server the shells stand up. The port is
    /// arbitrary but FIXED: the demo page is a page, not a test, so it cannot be
    /// handed a port at run time.
    /// <para><b>On iOS it is therefore HOST-GLOBAL</b> — the simulator shares
    /// macOS's network stack, so this is the CI runner's own :8099. The fixture
    /// server must bind exclusively and FAIL LOUDLY on a taken port, and the
    /// fixture-contract assertions (the header's "positive assertion" rule) are what
    /// prove the bytes came from OURS. If 8099 ever genuinely collides, move this
    /// one constant — all three URLs follow it.</para></summary>
    internal const string FixtureOrigin = "http://127.0.0.1:8099";

    /// <summary><b>[0]</b>'s source — the FIXED case. Its natural size must NOT be
    /// 200 × 120 — see the fixture contract in the file header.</summary>
    internal const string FixedSrc = FixtureOrigin + "/fixed.png";

    /// <summary><b>[1]</b>'s source — the INTRINSIC case. ITS natural size (Wi × Hi)
    /// IS the reflow.</summary>
    internal const string IntrinsicSrc = FixtureOrigin + "/intrinsic.png";

    /// <summary><b>[2]</b>'s source — the FAILING case: a path the fixture server
    /// 404s. A REAL failure,
    /// deterministic and offline — the node must stay 0 × 0, log, reserve nothing,
    /// and NOT retry.</summary>
    internal const string FailingSrc = FixtureOrigin + "/missing.png";

    // ── THE PAGE'S ARITHMETIC, AS CONSTANTS ───────────────────────────────────
    //
    // The section offsets are COMPUTED BY THE CONTRACT, not restated by a human in
    // three file headers. BnImageDemoTests asserts the sums (120 + 20 = 140; the
    // sections at 140 / 160 / 180), so a changed band height reddens the golden
    // instead of quietly desynchronising the prose from the wire — and Gates 2/3
    // read these rather than transcribing the numbers by hand.

    /// <summary><b>[0]</b>'s declared WIDTH. Both axes are set, so Yoga never calls the
    /// measure func and the bytes can never move this frame — the wire pin is the
    /// image node's style table in BnImageDemoTests, not this constant.</summary>
    internal const int FixedWidthDp = 200;

    /// <summary><b>[0]</b>'s declared HEIGHT. Same rule; see
    /// <see cref="FixedWidthDp"/>.</summary>
    internal const int FixedHeightDp = 120;

    /// <summary>Every section is 300 wide (the width the measure func's AT_MOST
    /// constraint is derived from — see the fixture contract: Wi ≤ 300).</summary>
    internal const int SectionWidthDp = 300;

    /// <summary>Each case's sibling band: 300 × 20. Its <b>y</b> is the whole proof
    /// surface of this page — see the file header.</summary>
    internal const int SiblingHeightDp = 20;

    /// <summary><b>[0]</b>'s section HUGS its children: 120 + 20 = 140. Nothing
    /// measured is inside it, so this is a fixed number on both platforms.</summary>
    internal const int FixedSectionHeightDp = FixedHeightDp + SiblingHeightDp;

    /// <summary><b>[1]</b> starts where <b>[0]</b> ends. Fixed, in BOTH states — [0] is
    /// the FIXED image and cannot reflow.</summary>
    internal const int IntrinsicSectionYDp = FixedSectionHeightDp;

    /// <summary><b>[2]</b>'s y BEFORE the bytes ([1]'s section is then 0 + 20 high).
    /// AFTER, it is this + Hi — [1]'s reflow, propagating downward.</summary>
    internal const int FailingSectionYDp = IntrinsicSectionYDp + SiblingHeightDp;

    /// <summary>The back row's y BEFORE the bytes; this + Hi after. Same
    /// reason.</summary>
    internal const int BackSectionYDp = FailingSectionYDp + SiblingHeightDp;

    // The band colours: distinct per case, because Gates 2/3 read the reflow off a
    // SCREENSHOT as well as off a number, and a band that moved is easier to SEE
    // than to diff. Mirrored by BnImageDemoTests — the golden asserts them.
    private const string BandUnderFixed = "#42A5F5";     // blue
    private const string BandUnderIntrinsic = "#66BB6A"; // green
    private const string BandUnderFailing = "#EF5350";   // red

    private static readonly string SectionWidth =
        SectionWidthDp.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static readonly string BandHeight =
        SiblingHeightDp.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static readonly string FixedWidth =
        FixedWidthDp.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static readonly string FixedHeight =
        FixedHeightDp.ToString(System.Globalization.CultureInfo.InvariantCulture);

    [Inject] public INavigationManager Navigation { get; set; } = default!;

    private Task GoBack() => Navigation.NavigateToAsync("/").AsTask();

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenComponent<BnColumn>(0);
        b.AddComponentParameter(1, nameof(BnColumn.ChildContent), (RenderFragment)BuildSections);
        b.CloseComponent();
    }

    private void BuildSections(RenderTreeBuilder b)
    {
        // The order is load-bearing — see the file header. [0] FIXED FIRST, so that
        // nothing above it can ever move it and its "the frame did not change" is a
        // fact about the image rather than a fact about the page.
        BuildFixedSection(b, 0);
        BuildIntrinsicSection(b, 100);
        BuildFailingSection(b, 200);
        BuildBackSection(b, 300);
    }

    // The three sections' content, as CACHED RenderFragment method groups — one
    // static delegate each, allocated once, exactly as BnScrollDemo caches
    // BuildFlexRow/BuildRowImage. A lambda closing over the band colour would
    // allocate a fresh RenderFragment on every render of every section, which is
    // both a divergence from the established pattern and a new Blazor diff identity
    // each frame. The band's colour therefore travels the only other way it can:
    // each case builds its OWN band, and the shared shape is the SECTION.

    /// <summary>[0] FIXED — sized immediately, never measured, never reflowed.
    /// Width AND Height are both definite, so Yoga does not call the image's
    /// measure func at all: the frame is (0,0,200,120) before the bytes and
    /// (0,0,200,120) after. Band F sits at y = 120 in both states, and BECAUSE
    /// THIS SECTION IS FIRST nothing on the page can move it for an unrelated
    /// reason. That identity is the "no reflow" half of the parity contract.</summary>
    private static void BuildFixedSection(RenderTreeBuilder b, int seq)
        => BuildCaseSection(b, seq, BuildFixedCase);

    private static void BuildFixedCase(RenderTreeBuilder cb)
    {
        cb.OpenComponent<BnImage>(0);
        cb.AddComponentParameter(1, nameof(BnImage.Src), FixedSrc);
        cb.AddComponentParameter(2, nameof(BnImage.Width), FixedWidth);
        cb.AddComponentParameter(3, nameof(BnImage.Height), FixedHeight);
        cb.CloseComponent();

        BuildBand(cb, BandUnderFixed);
    }

    /// <summary>[1] INTRINSIC — THE REFLOW. No Width, no Height, so the image is a
    /// Yoga leaf with a measure func: 0 × 0 until the bytes land, its NATURAL size
    /// (Wi × Hi) after. The shell then marks the node dirty and re-solves, and band
    /// I below it moves from y = 0 to y = Hi. THE BAND'S y IS THE PROOF — the
    /// image's own frame is not, because a shell could paint the bytes and never
    /// re-solve, and the image would still look right.</summary>
    private static void BuildIntrinsicSection(RenderTreeBuilder b, int seq)
        => BuildCaseSection(b, seq, BuildIntrinsicCase);

    private static void BuildIntrinsicCase(RenderTreeBuilder cb)
    {
        cb.OpenComponent<BnImage>(0);
        cb.AddComponentParameter(1, nameof(BnImage.Src), IntrinsicSrc);
        // NO Width, NO Height. That absence IS the case.
        cb.CloseComponent();

        BuildBand(cb, BandUnderIntrinsic);
    }

    /// <summary>[2] FAILING — reserves nothing. Structurally IDENTICAL to <b>[1]</b>'s
    /// intrinsic image (same empty style table, same measured leaf): ONLY THE URL
    /// DIFFERS, which is what makes the difference the shells observe (0 × 0
    /// forever, versus 0 × 0 → Wi × Hi) attributable to the LOAD and to nothing
    /// .NET said. Band X stays at y = 0 IN ITS PARENT even though the section itself
    /// slides down by Hi when <b>[1]</b> above it reflows. (NOT [0]: [0] is the
    /// FIXED image, and it provably cannot reflow — that is its whole job.)</summary>
    private static void BuildFailingSection(RenderTreeBuilder b, int seq)
        => BuildCaseSection(b, seq, BuildFailingCase);

    private static void BuildFailingCase(RenderTreeBuilder cb)
    {
        cb.OpenComponent<BnImage>(0);
        cb.AddComponentParameter(1, nameof(BnImage.Src), FailingSrc);
        cb.CloseComponent();

        BuildBand(cb, BandUnderFailing);
    }

    /// <summary>The shape all three cases share: a 300-wide column with NO height
    /// (it HUGS its two children — which is how the intrinsic image's Hi propagates
    /// down the page), holding an image and a 20dp band beneath it.
    /// <para><b><c>Align=FlexStart</c> is load-bearing.</b> A section is a COLUMN,
    /// so its cross axis is WIDTH — and Yoga's default <c>alignItems</c> is
    /// <b>stretch</b>. Under the default, an intrinsic image (no width) would be
    /// STRETCHED to the section's 300 and its measured width would never be seen:
    /// the "natural size" half of the parity contract would be untestable, and this
    /// page would silently prove half of what it claims.</para></summary>
    private static void BuildCaseSection(RenderTreeBuilder b, int seq, RenderFragment content)
    {
        b.OpenComponent<BnColumn>(seq);
        b.AddComponentParameter(seq + 1, nameof(BnColumn.Width), SectionWidth);
        b.AddComponentParameter(seq + 2, nameof(BnColumn.Align), FlexAlign.FlexStart);
        b.AddComponentParameter(seq + 3, nameof(BnColumn.ChildContent), content);
        b.CloseComponent();
    }

    /// <summary>The band beneath a case's image. Its y is the page's whole proof
    /// surface, so it is explicit in BOTH axes — a measured band would be a witness
    /// that could itself move for reasons unrelated to the image above it.</summary>
    private static void BuildBand(RenderTreeBuilder cb, string bandColor)
    {
        cb.OpenComponent<BnView>(10);
        cb.AddComponentParameter(11, nameof(BnView.Width), SectionWidth);
        cb.AddComponentParameter(12, nameof(BnView.Height), BandHeight);
        cb.AddComponentParameter(13, nameof(BnView.BackgroundColor), bandColor);
        cb.CloseComponent();
    }

    /// <summary>[3] nav parity with the other pages — and the page's only other
    /// measured leaf (a button sizes itself). Deliberately LAST: a font-dependent
    /// height must not shift the frames the parity assertion is built on.</summary>
    private void BuildBackSection(RenderTreeBuilder b, int seq)
    {
        b.OpenComponent<BnRow>(seq);
        b.AddComponentParameter(seq + 1, nameof(BnRow.Width), SectionWidth);
        b.AddComponentParameter(seq + 2, nameof(BnRow.ChildContent), (RenderFragment)(bb =>
        {
            bb.OpenComponent<BnButton>(0);
            bb.AddComponentParameter(1, nameof(BnButton.Label), "← Back");
            bb.AddComponentParameter(2, nameof(BnButton.OnClick),
                EventCallback.Factory.Create<MouseEventArgs>(this, GoBack));
            bb.CloseComponent();
        }));
        b.CloseComponent();
    }
}
