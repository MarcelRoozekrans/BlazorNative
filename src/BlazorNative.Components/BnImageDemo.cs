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
//   1. FIXED (200 × 120)   → the band's y is 120 BEFORE the bytes and 120 AFTER.
//                            Identical. THE NO-REFLOW PROOF.
//   2. INTRINSIC (no size) → the band's y is 0 BEFORE the bytes and Hi AFTER.
//                            It MOVED. THE REFLOW PROOF.
//   3. FAILING (404 URL)   → the band's y is 0 BEFORE and 0 AFTER — in its parent.
//                            The failure RESERVED NOTHING.
//
// ── AND CASE 1 IS FIRST, ON PURPOSE ──────────────────────────────────────────
// A no-reflow assertion is only honest if nothing ABOVE the node could have moved
// it for an unrelated reason. The fixed case is the root column's FIRST child, so
// its y is 0 by construction and no reflow anywhere on this page can touch it.
// Case 2's reflow propagates DOWNWARD (into case 3 and the back row) and nowhere
// else — which is exactly why case 3's "reserves nothing" assertion is stated
// PARENT-RELATIVE: its section moves down by Hi (because case 2 grew above it),
// while the band INSIDE it stays at y = 0 (because its own image reserved
// nothing). Two different facts; the frames are parent-relative, so both are
// visible at once.
//
// ── THE FRAME TABLE, BEFORE THE BYTES LAND (dp, RELATIVE TO THE PARENT) ──────
//
// This is the MOUNT state — the first frame each shell applies, with both measured
// images reporting 0 × 0 (they are Yoga leaves with a measure func — 6.1 attaches
// it BY NODETYPE, and `image` is already in the set — and an image with no bytes
// measures to zero, by construction rather than by accident).
//
// root BnColumn                          HUGS its four sections; children stack down
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
// root BnColumn
//  ├─ [0] fixed section  (0,      0, 300, 140)   UNCHANGED ─┐
//  │    ├─ image (fixed) (0,      0, 200, 120)   UNCHANGED  │ THE NO-REFLOW PROOF:
//  │    └─ band F        (0,    120, 300,  20)   UNCHANGED ─┘ every number identical
//  ├─ [1] intrinsic sect.(0,    140, 300, Hi+20)  grew by Hi
//  │    ├─ image (intr.) (0,      0,  Wi,  Hi)    ← 0×0 → the NATURAL size
//  │    └─ band I        (0,     Hi, 300,  20)    ← y: 0 → Hi. THE REFLOW PROOF.
//  ├─ [2] failing sect.  (0, 160+Hi, 300,  20)    moved down by case 2's reflow…
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
// ── THE URLS ARE LOOPBACK. CI NEVER TOUCHES THE PUBLIC INTERNET ─────────────
// 6.3 non-negotiable #5, and it is not negotiable because a suite whose green
// depends on a remote host is not a suite. The three sources point at a fixture
// server the SHELLS run IN-PROCESS (127.0.0.1 is the device's own loopback on the
// AVD and on the simulator alike — the host machine's network is never involved),
// and the failing case is a path that server 404s. So:
//   • the fetch is a REAL network fetch, through Coil and Kingfisher, over HTTP —
//     which is the capability this phase is actually shipping;
//   • the failure is a REAL failure, deterministic, and offline;
//   • no test anywhere depends on a host that can go down.
// A shell that would rather serve the same bytes from a bundled asset may — THE
// FRAMES ARE THE CONTRACT, not the transport (design §"The parity contract":
// parity is asserted on frames, never on cache internals). But it must then answer
// for what a 404 means, and the loopback server already does.
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
    // Loopback, on the DEVICE (127.0.0.1 is the AVD's own loopback and the
    // simulator's own loopback — never the host machine's network). The shells
    // serve the fixture in-process; CI never reaches the public internet
    // (non-negotiable #5). Gates 2/3 read these constants rather than transcribing
    // the strings — a URL retyped in three places is a URL that drifts in two.

    /// <summary>The in-process fixture server the shells stand up. The port is
    /// arbitrary but FIXED: the demo page is a page, not a test, so it cannot be
    /// handed a port at run time.</summary>
    internal const string FixtureOrigin = "http://127.0.0.1:8099";

    /// <summary>Case 1's source. Its natural size must NOT be 200 × 120 — see the
    /// fixture contract in the file header.</summary>
    internal const string FixedSrc = FixtureOrigin + "/fixed.png";

    /// <summary>Case 2's source. ITS natural size (Wi × Hi) is the reflow.</summary>
    internal const string IntrinsicSrc = FixtureOrigin + "/intrinsic.png";

    /// <summary>Case 3's source: a path the fixture server 404s. A REAL failure,
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

    /// <summary>Case 1's declared size. Both set → Yoga never calls the measure
    /// func, so the bytes can never move this frame.</summary>
    internal const int FixedWidthDp = 200;

    /// <inheritdoc cref="FixedWidthDp"/>
    internal const int FixedHeightDp = 120;

    /// <summary>Every section is 300 wide (the width the measure func's AT_MOST
    /// constraint is derived from — see the fixture contract: Wi ≤ 300).</summary>
    internal const int SectionWidthDp = 300;

    /// <summary>Each case's sibling band: 300 × 20. Its <b>y</b> is the whole proof
    /// surface of this page — see the file header.</summary>
    internal const int SiblingHeightDp = 20;

    /// <summary>Case 1's section HUGS its children: 120 + 20 = 140. Nothing
    /// measured is inside it, so this is a fixed number on both platforms.</summary>
    internal const int FixedSectionHeightDp = FixedHeightDp + SiblingHeightDp;

    /// <summary>Case 2 starts where case 1 ends. Fixed, in BOTH states — case 1
    /// cannot reflow.</summary>
    internal const int IntrinsicSectionYDp = FixedSectionHeightDp;

    /// <summary>Case 3's y BEFORE the bytes (case 2's section is then 0 + 20 high).
    /// AFTER, it is this + Hi — the reflow, propagating downward.</summary>
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
        // The order is load-bearing — see the file header. FIXED FIRST, so that
        // nothing above it can ever move it and its "the frame did not change" is a
        // fact about the image rather than a fact about the page.
        BuildFixedSection(b, 0);
        BuildIntrinsicSection(b, 100);
        BuildFailingSection(b, 200);
        BuildBackSection(b, 300);
    }

    /// <summary>[0] FIXED — sized immediately, never measured, never reflowed.
    /// Width AND Height are both definite, so Yoga does not call the image's
    /// measure func at all: the frame is (0,0,200,120) before the bytes and
    /// (0,0,200,120) after. Its band sits at y = 120 in both states, and BECAUSE
    /// THIS SECTION IS FIRST nothing on the page can move it for an unrelated
    /// reason. That identity is the "no reflow" half of the parity contract.</summary>
    private static void BuildFixedSection(RenderTreeBuilder b, int seq)
        => BuildCaseSection(b, seq, BandUnderFixed, (ib, s) =>
        {
            ib.OpenComponent<BnImage>(s);
            ib.AddComponentParameter(s + 1, nameof(BnImage.Src), FixedSrc);
            ib.AddComponentParameter(s + 2, nameof(BnImage.Width), FixedWidth);
            ib.AddComponentParameter(s + 3, nameof(BnImage.Height), FixedHeight);
            ib.CloseComponent();
        });

    /// <summary>[1] INTRINSIC — THE REFLOW. No Width, no Height, so the image is a
    /// Yoga leaf with a measure func: 0 × 0 until the bytes land, its NATURAL size
    /// (Wi × Hi) after. The shell then marks the node dirty and re-solves, and the
    /// band below it moves from y = 0 to y = Hi. THE BAND'S y IS THE PROOF — the
    /// image's own frame is not, because a shell could paint the bytes and never
    /// re-solve, and the image would still look right.</summary>
    private static void BuildIntrinsicSection(RenderTreeBuilder b, int seq)
        => BuildCaseSection(b, seq, BandUnderIntrinsic, (ib, s) =>
        {
            ib.OpenComponent<BnImage>(s);
            ib.AddComponentParameter(s + 1, nameof(BnImage.Src), IntrinsicSrc);
            // NO Width, NO Height. That absence IS the case.
            ib.CloseComponent();
        });

    /// <summary>[2] FAILING — reserves nothing. Structurally IDENTICAL to case 1's
    /// intrinsic image (same empty style table, same measured leaf): ONLY THE URL
    /// DIFFERS, which is what makes the difference the shells observe (0 × 0
    /// forever, versus 0 × 0 → Wi × Hi) attributable to the LOAD and to nothing
    /// .NET said. Its band stays at y = 0 IN ITS PARENT even though the section
    /// itself slides down by Hi when case 1 above it reflows.</summary>
    private static void BuildFailingSection(RenderTreeBuilder b, int seq)
        => BuildCaseSection(b, seq, BandUnderFailing, (ib, s) =>
        {
            ib.OpenComponent<BnImage>(s);
            ib.AddComponentParameter(s + 1, nameof(BnImage.Src), FailingSrc);
            ib.CloseComponent();
        });

    /// <summary>The shape all three cases share: a 300-wide column with NO height
    /// (it HUGS its two children — which is how the intrinsic image's Hi propagates
    /// down the page), holding an image and a 20dp band beneath it.
    /// <para><b><c>Align=FlexStart</c> is load-bearing.</b> A section is a COLUMN,
    /// so its cross axis is WIDTH — and Yoga's default <c>alignItems</c> is
    /// <b>stretch</b>. Under the default, an intrinsic image (no width) would be
    /// STRETCHED to the section's 300 and its measured width would never be seen:
    /// the "natural size" half of the parity contract would be untestable, and this
    /// page would silently prove half of what it claims.</para></summary>
    private static void BuildCaseSection(
        RenderTreeBuilder b, int seq, string bandColor, Action<RenderTreeBuilder, int> buildImage)
    {
        b.OpenComponent<BnColumn>(seq);
        b.AddComponentParameter(seq + 1, nameof(BnColumn.Width), SectionWidth);
        b.AddComponentParameter(seq + 2, nameof(BnColumn.Align), FlexAlign.FlexStart);
        b.AddComponentParameter(seq + 3, nameof(BnColumn.ChildContent), (RenderFragment)(cb =>
        {
            buildImage(cb, 0);

            // The band. Its y is the page's whole proof surface, so it is explicit
            // in both axes — a measured band would be a witness that could itself
            // move for reasons unrelated to the image above it.
            cb.OpenComponent<BnView>(10);
            cb.AddComponentParameter(11, nameof(BnView.Width), SectionWidth);
            cb.AddComponentParameter(12, nameof(BnView.Height), BandHeight);
            cb.AddComponentParameter(13, nameof(BnView.BackgroundColor), bandColor);
            cb.CloseComponent();
        }));
        b.CloseComponent();
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
