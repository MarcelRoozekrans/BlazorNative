using BlazorNative.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorNative.Components;

// ─────────────────────────────────────────────────────────────────────────────
// BnScrollDemo — Phase 6.2 Task 1.2 (design §"The proof surface"), route
// "/scroll", mount-registry key "BnScrollDemo".
//
// The scroll demo is a FOURTH page (design decision 3): BnDemo, BnSettingsPage
// and BnLayoutDemo keep their goldens on all four surfaces, so scroll-engine
// bugs never arrive mixed with golden-rewrite noise. BnLayoutDemo's 22-number
// frame table is THE cross-platform parity contract — wrapping it in a scroll
// view would rewrite that table in the same phase that introduces the scroll
// engine.
//
// Its whole job is to have COMPUTED FRAMES AND A CONTENT SIZE THAT ARE FULLY
// PREDICTABLE, because three surfaces assert against them: the .NET patch golden
// (BnScrollDemoTests), the Android instrumented frames (Gate 2) and the iOS
// XCTest frames (Gate 3) — and the last two assert THE SAME NUMBERS. Yoga
// computes in density-independent units on both platforms (iOS points 1:1;
// Android multiplies by density at frame-apply time), so the table below is in
// dp/pt.
//
// ── THE SYNTHETIC CONTENT NODE: THE WIRE TREE IS NOT THE VIEW TREE ───────────
//
//   WIRE (what BnScrollDemoTests pins)      VIEW / YOGA (what the shells build)
//   ───────────────────────────────────     ───────────────────────────────────
//   scroll                                  scroll        ← the VIEWPORT
//    ├─ row 0                                └─ content   ← SYNTHETIC: created by
//    ├─ row 1                                     ├─ row 0   the shell, NEVER on
//    └─ …                                         ├─ row 1   the wire
//                                                 └─ …
//
// The content node is `height: auto`, `width: 100%`, `flexDirection: column`.
// THE CONTENT NODE'S COMPUTED HEIGHT *IS* THE CONTENT SIZE — read straight out
// of Yoga, never derived shell-side from a union of child frames (two shells
// deriving it independently is precisely where Android and iOS drift apart). A
// scroll node's wire child at index i is the CONTENT node's child at index i, in
// the Yoga tree AND the view tree, on both platforms.
//
// ── WHY THE CONTENT NODE COMPUTES TO 800 AND NOT 200 ─────────────────────────
// Because YOGA'S `flexShrink` DEFAULT IS 0 (CSS's is 1). The content node is a
// 200-high viewport's only child with 800 of children: free space is 200 − 800 =
// −600, and the shrink pass distributes negative free space across flexShrink —
// which is 0, so nothing shrinks and the node keeps its 800. It is NOT
// `overflow: scroll` that produces the 800 (that flag is about how a node sizes
// ITSELF under fit-content/auto-height, not about clamping an overflowing child);
// the same 800 comes out with `overflow: visible`.
//
// THE ONE THING A SHELL MUST NOT DO: set `flexShrink` on the content node. A
// Gate 2/3 implementer reaching for CSS instincts and writing `flexShrink: 1`
// collapses 800 → 200 and the page silently stops scrolling. Yoga's default 0 is
// LOAD-BEARING; leave it alone. (Gate 2's Yoga-only unit test asserts BOTH
// mechanisms explicitly, and settles empirically whether `overflow: scroll` needs
// setting at all — we keep it because it is semantically right and is what RN
// does, not because it is what makes 800.)
//
// ── THE FRAME TABLE (dp, each frame RELATIVE TO ITS PARENT) ──────────────────
//
// root BnColumn                        HUGS its two sections; children stack down
//                                      (it does NOT fill the host — the Android test
//                                      asserts backRow.bottom == root.height, which is
//                                      the 6.1 pin that catches the host root re-laying
//                                      out top-level nodes behind Yoga's back)
//  ├─ [0] scroll  (0,   0, 300, 200)   BnScroll W=300 H=200 — the VIEWPORT
//  │    └─ content (0,  0, 300, 800)   SYNTHETIC. 10 × 80 = 800 → THE CONTENT SIZE
//  │         ├─ row 0  (0,   0, 300, 80)   H=80; no width → stretched to the
//  │         │    └─ image (0, 0, 40, 40)  6.3 — FIXED, and SMALLER than the row
//  │         ├─ row 1  (0,  80, 300, 80)   content node's 300 (alignItems:stretch)
//  │         │    └─ flex row (0, 0, 300, 80)   Grow=1 in an 80-high column
//  │         │         ├─ box A  (0,   0,  50, 80)   W=50   ← cross-stretch → h=80
//  │         │         ├─ box B  (50,  0, 200, 80)   Grow=1 ← absorbs 300-50-50
//  │         │         └─ box C  (250, 0,  50, 80)   W=50
//  │         ├─ row 2  (0, 160, 300, 80)
//  │         ├─ row 3  (0, 240, 300, 80)
//  │         ├─ row 4  (0, 320, 300, 80)
//  │         ├─ row 5  (0, 400, 300, 80)
//  │         ├─ row 6  (0, 480, 300, 80)
//  │         ├─ row 7  (0, 560, 300, 80)
//  │         ├─ row 8  (0, 640, 300, 80)
//  │         └─ row 9  (0, 720, 300, 80)
//  └─ [1] back section (0, 200, 300, Hb)   BnRow W=300, NO height
//       └─ "← Back"    (0,   0,  Wb, Hb)   Hb/Wb = the button's MEASURED size
//
//   contentSize      300 × 800   (from YOGA — the content node's computed frame)
//   viewport         300 × 200
//   scrollable range 800 - 200 = 600      initial offset 0
//
// At offset 0 the visible window is content y 0..200: rows 0 and 1 are fully
// inside it, row 2 straddles its bottom edge (160..240 against a 200 cut), and
// rows 3-9 are entirely below it.
// At offset 600 (the maximum) the visible window is content y 600..800: row 7
// (560..640) is PARTIALLY visible — only its bottom 40dp — and rows 8 (640..720)
// and 9 (720..800) are fully visible. The content cannot scroll further. Write
// the shell assertions against those numbers, not against "rows 7, 8 and 9 fill
// the viewport": row 7 does not.
//
// The load-bearing assertion is contentSize > viewport, FROM YOGA — that is the
// whole phase; the scroll-and-assert step proves the viewport actually moves over
// the content, not merely that the numbers add up.
//
// ── PHASE 6.3: AN IMAGE IN ROW 0, AND EVERY NUMBER ABOVE IS UNCHANGED ────────
// Images inside a scroll viewport is the most common real usage of both features,
// and leaving it unexercised until someone hits it is how you find out the hard
// way. But the table above is the 6.2 CROSS-PLATFORM PARITY CONTRACT, and 6.3
// non-negotiable #2 is blunt: IF A NUMBER IN IT MOVES, THE CHANGE IS WRONG.
//
// It cannot move, and there are TWO independent reasons — either one alone would
// be enough, which is the point of stating both (BnScrollDemoTests
// .TheImageCannotMoveTheFrameTable pins them):
//
//   • THE ROW'S HEIGHT IS DEFINITE (80). A child cannot grow a definite-height
//     parent, so row 0 is 80 high whatever the image does. Every row stays at
//     y = 80·i, the content node stays 10 × 80 = 800, the range stays 600.
//   • THE IMAGE'S SIZE IS DEFINITE (40 × 40). Both axes declared → YOGA NEVER
//     CALLS ITS MEASURE FUNC. The bytes cannot move a frame even in principle,
//     and a FAILED load moves nothing either: there is no measurement to change.
//     (That is precisely the parity contract's second row — "Width/Height set →
//     exactly those, always; the bytes never move the frame" — and here it is
//     doing structural work, not just being asserted.)
//
// It is also strictly smaller than the row in BOTH axes, so it cannot overflow and
// raise a clipping question two shells would answer differently.
//
// Row 0, not a deeper one: like the flex row, it is fully inside the viewport at
// offset 0 (y 0..80 of a 200-high viewport), so the shells see the image load in
// the FIRST screenshot they take rather than behind a scroll — while rows 7-9
// remain what scrolling has to reveal. And NOT row 1: two features in one row would
// make a failure ambiguous.
//
// What this page therefore proves for 6.3 is not "an image lays out" (that is
// /image's job, with its two frame tables and its measured sibling) but the thing
// only a scroll can prove: an image that lives inside a scrolled, re-parented,
// synthetic-content-node subtree still fetches, still paints, and — when the page
// is navigated away from — has its in-flight request CANCELLED as part of the
// subtree purge. A completion firing into a removed row is 6.2's dangling-pointer
// lesson in a new costume (on iOS, a freed YGNodeRef).
//
// ── THE TWO NESTINGS (design §Verification #4) ───────────────────────────────
// The scroll sits INSIDE a flex column (the root), and a flex ROW sits inside a
// scrolled row (row 1). Row 1 on purpose: at offset 0 it is fully visible
// (y 80..160 of a 200-high viewport), so the nesting proof is in the FIRST
// screenshot each shell takes — while rows 7-9 are what scrolling has to reveal.
//
// ── WHY THE ROWS CARRY NO TEXT ───────────────────────────────────────────────
// A label's height is a font's business, not a constant we get to invent
// (BnLayoutDemo puts its measured leaves LAST for exactly this reason). Every
// row here is a fixed-height coloured band, so every number above is a fixed
// number on both platforms; the only measured frame on the page is the back
// button's, and it is the last child of the root. The colours are the rows'
// identity — the AVD/simulator screenshots are read by humans too, and a wrong
// frame is easier to SEE than to diff.
//
// Nav parity with the other pages: "← Back" → INavigationManager → "/". It sits
// OUTSIDE the viewport — a page whose only exit can scroll off the screen is not
// a page with an exit. Reachable by mount-name ("BnScrollDemo") and by route
// ("/scroll"); no button on BnDemo points here — adding one would churn BnDemo's
// four goldens for no engine reason (non-negotiable #4).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The scrolling proof page — see the file header for the pinned frame
/// table and content size. A fresh ROOT component (like BnLayoutDemo): no
/// cascaded theme crosses a root swap.</summary>
public sealed class BnScrollDemo : ComponentBase
{
    /// <summary>The row colours, in row order — exactly <see cref="RowCount"/> of
    /// them (the golden pins the two counts equal; the render loop runs to
    /// RowCount, so a colour added here without bumping RowCount would be dead).
    /// Distinct per row: a scrolled row is identified in a screenshot by its
    /// colour, not by a nodeId. Mirrored by BnScrollDemoTests.RowColors — the
    /// golden asserts them.</summary>
    private static readonly string[] RowColors =
    [
        "#E57373", // 0 red
        "#90A4AE", // 1 blue-grey — HOSTS THE NESTED FLEX ROW
        "#81C784", // 2 green
        "#FFB74D", // 3 orange
        "#BA68C8", // 4 purple
        "#4DB6AC", // 5 teal
        "#F06292", // 6 pink
        "#7986CB", // 7 indigo   ─┐ the three rows the scroll has to reveal
        "#A1887F", // 8 brown     │ (offset 600 — the end of the range)
        "#DCE775", // 9 lime     ─┘
    ];

    // The nested flex row's boxes: 600-weight colours, so they read against
    // row 1's 300-weight blue-grey.
    private const string BoxA = "#E53935"; // red
    private const string BoxB = "#1E88E5"; // blue  — the Grow=1 box
    private const string BoxC = "#43A047"; // green

    /// <summary>The row that hosts the nested flex row — see the header.</summary>
    private const int FlexRowIndex = 1;

    /// <summary>The row that hosts the 6.3 image — see the header. Row 0: fully
    /// inside the viewport at offset 0, so the load is in the FIRST screenshot the
    /// shells take. NOT row 1 (two features in one row make a failure
    /// ambiguous).</summary>
    internal const int ImageRowIndex = 0;

    /// <summary>The row image's DECLARED size — 40 × 40. Both axes, deliberately:
    /// a definite size means Yoga never calls the measure func, so the bytes cannot
    /// move a frame in the 6.2 parity table even in principle (and neither can a
    /// failed load). Strictly smaller than the row in both axes, so it cannot
    /// overflow it. Pinned in BnScrollDemoTests.TheImageCannotMoveTheFrameTable.
    /// </summary>
    internal const int RowImageWidthDp = 40;

    /// <inheritdoc cref="RowImageWidthDp"/>
    internal const int RowImageHeightDp = 40;

    /// <summary>The row image's source — THE SAME fixture BnImageDemo's fixed case
    /// loads, from the same loopback origin. One fixture, one server, both demos:
    /// the shells stand up one in-process fixture server and CI never touches the
    /// public internet (6.3 non-negotiable #5).</summary>
    internal const string RowImageSrc = BnImageDemo.FixedSrc;

    private static readonly string RowImageWidth =
        RowImageWidthDp.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static readonly string RowImageHeight =
        RowImageHeightDp.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // ── THE PAGE'S ARITHMETIC, AS CONSTANTS ───────────────────────────────────
    //
    // The content size is COMPUTED BY THE CONTRACT, not restated by a human in
    // three file headers. BnScrollDemoTests asserts the products
    // (RowCount × RowHeightDp == 800; 800 − ViewportHeightDp == 600), so a
    // changed row height reddens the golden instead of quietly desynchronising
    // the prose from the wire — and Gates 2/3 read ContentHeightDp/ScrollRangeDp
    // rather than transcribing 800 and 600 by hand.

    /// <summary>Ten rows — <see cref="RowColors"/> has one colour each (the
    /// golden pins the two counts equal).</summary>
    internal const int RowCount = 10;

    /// <summary>Each row is 80dp tall. Load-bearing: RowCount × RowHeightDp is
    /// the content size Gates 2/3 read out of Yoga.</summary>
    internal const int RowHeightDp = 80;

    /// <summary>The viewport: 300 × 200. Load-bearing the same way — the
    /// scrollable range is the content height minus this.</summary>
    internal const int ViewportWidthDp = 300;

    /// <inheritdoc cref="ViewportWidthDp"/>
    internal const int ViewportHeightDp = 200;

    /// <summary>What the SYNTHETIC content node must compute to in Yoga — 800.
    /// Not a number the shells invent: it is what ten 80-high rows in a
    /// height:auto column add up to, and the shells READ it (contentSize) rather
    /// than deriving it.</summary>
    internal const int ContentHeightDp = RowCount * RowHeightDp;

    /// <summary>The scrollable range Gates 2/3 drive: 800 − 200 = 600.</summary>
    internal const int ScrollRangeDp = ContentHeightDp - ViewportHeightDp;

    private static readonly string RowHeight =
        RowHeightDp.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static readonly string ViewportWidth =
        ViewportWidthDp.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static readonly string ViewportHeight =
        ViewportHeightDp.ToString(System.Globalization.CultureInfo.InvariantCulture);

    [Inject] public INavigationManager Navigation { get; set; } = default!;

    private Task GoBack() => Navigation.NavigateToAsync("/").AsTask();

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        // The scroll lives inside a flex COLUMN (design §Verification #4, half
        // one): its 300×200 frame is placed by flex like any other item's.
        b.OpenComponent<BnColumn>(0);
        b.AddComponentParameter(1, nameof(BnColumn.ChildContent), (RenderFragment)BuildSections);
        b.CloseComponent();
    }

    private void BuildSections(RenderTreeBuilder b)
    {
        BuildScrollSection(b, 0);
        BuildBackSection(b, 100);
    }

    /// <summary>[0] the viewport: 300×200 over 800dp of content. A DEFINITE
    /// height is what makes it a viewport at all — an auto-height scroll node
    /// takes its height FROM its content, so viewport == content and there is
    /// nothing to scroll (the shells warn once when that happens; here it simply
    /// cannot). An explicit Height, deliberately: the flex-sized alternative is
    /// <c>Grow="1" Basis="0"</c> (CSS's <c>flex: 1</c>) and NOT <c>Grow="1"</c>
    /// alone — see BnScroll's header, which used to say otherwise. Note what is
    /// NOT here: no Gap, no Padding, no Justify — BnScroll is a flex ITEM (see
    /// BnScroll's header); the rows' layout is the synthetic content node's
    /// business, and an author who wants to shape it puts a BnColumn inside.</summary>
    private static void BuildScrollSection(RenderTreeBuilder b, int seq)
    {
        b.OpenComponent<BnScroll>(seq);
        b.AddComponentParameter(seq + 1, nameof(BnScroll.Width), ViewportWidth);
        b.AddComponentParameter(seq + 2, nameof(BnScroll.Height), ViewportHeight);
        b.AddComponentParameter(seq + 3, nameof(BnScroll.ChildContent), (RenderFragment)(rb =>
        {
            for (var i = 0; i < RowCount; i++)
            {
                // Sequence numbers: i * 10, stable and unique across renders (the
                // loop's shape is constant — ten rows, always, with the flex row
                // always at index 1). Blazor's diff keys on these.
                rb.OpenComponent<BnView>(i * 10);
                rb.AddComponentParameter((i * 10) + 1, nameof(BnView.Height), RowHeight);
                rb.AddComponentParameter((i * 10) + 2, nameof(BnView.BackgroundColor), RowColors[i]);
                // No Width: the synthetic content node is width:100% of the
                // viewport and Yoga's default alignItems is stretch, so each row
                // computes to 300 wide. Setting it here would hide whether the
                // content node actually spans the viewport.
                if (i == FlexRowIndex)
                    rb.AddComponentParameter((i * 10) + 3, nameof(BnView.ChildContent), (RenderFragment)BuildFlexRow);
                else if (i == ImageRowIndex)
                    rb.AddComponentParameter((i * 10) + 3, nameof(BnView.ChildContent), (RenderFragment)BuildRowImage);
                rb.CloseComponent();
            }
        }));
        b.CloseComponent();
    }

    /// <summary>The 6.3 image, nested INSIDE the scroll (design §"The proof
    /// surface"): images-in-a-viewport proven, with the 6.2 frame table untouched.
    /// <para>40 × 40 inside an 80-high, 300-wide row. FIXED in both axes, which is
    /// what makes it safe: Yoga never calls its measure func, so the bytes cannot
    /// move a frame — and neither can a failure. Its frame is (0, 0, 40, 40) in the
    /// row's coordinates (the row is a column whose default alignItems is stretch,
    /// but a definite width does not stretch). See the file header for the two
    /// independent reasons this page's parity table cannot move.</para>
    /// <para>What the shells owe here that /image cannot ask of them: this image
    /// lives inside a re-parented subtree under a SYNTHETIC content node, and when
    /// the page is navigated away its in-flight request must be CANCELLED as part of
    /// the subtree purge — a completion painting into a removed row is 6.2's
    /// dangling-pointer lesson in a new costume.</para></summary>
    private static void BuildRowImage(RenderTreeBuilder b)
    {
        b.OpenComponent<BnImage>(0);
        b.AddComponentParameter(1, nameof(BnImage.Src), RowImageSrc);
        b.AddComponentParameter(2, nameof(BnImage.Width), RowImageWidth);
        b.AddComponentParameter(3, nameof(BnImage.Height), RowImageHeight);
        b.CloseComponent();
    }

    /// <summary>Flex nested INSIDE the scroll (design §Verification #4, half two):
    /// a BnRow with Grow=1 inside row 1's definite 80dp column → it fills the row
    /// (0,0,300,80); inside it, BnLayoutDemo's idiom — fixed 50 · Grow=1 · fixed
    /// 50 — so the grow box absorbs exactly the remainder (200) and the boxes,
    /// having no height, take the row's full 80 by cross-axis stretch.</summary>
    private static void BuildFlexRow(RenderTreeBuilder b)
    {
        b.OpenComponent<BnRow>(0);
        b.AddComponentParameter(1, nameof(BnRow.Grow), 1f);
        b.AddComponentParameter(2, nameof(BnRow.ChildContent), (RenderFragment)(fb =>
        {
            fb.OpenComponent<BnView>(0);
            fb.AddComponentParameter(1, nameof(BnView.Width), "50");
            fb.AddComponentParameter(2, nameof(BnView.BackgroundColor), BoxA);
            fb.CloseComponent();

            fb.OpenComponent<BnView>(10);
            fb.AddComponentParameter(11, nameof(BnView.Grow), 1f);
            fb.AddComponentParameter(12, nameof(BnView.BackgroundColor), BoxB);
            fb.CloseComponent();

            fb.OpenComponent<BnView>(20);
            fb.AddComponentParameter(21, nameof(BnView.Width), "50");
            fb.AddComponentParameter(22, nameof(BnView.BackgroundColor), BoxC);
            fb.CloseComponent();
        }));
        b.CloseComponent();
    }

    /// <summary>[1] nav parity with the other pages — OUTSIDE the viewport, so
    /// scrolling can never take the exit off the screen. Also the page's only
    /// measured leaf (a button sizes itself), and it is deliberately LAST: a
    /// font-dependent height must not shift the frames the parity assertion is
    /// built on.</summary>
    private void BuildBackSection(RenderTreeBuilder b, int seq)
    {
        b.OpenComponent<BnRow>(seq);
        b.AddComponentParameter(seq + 1, nameof(BnRow.Width), "300");
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
