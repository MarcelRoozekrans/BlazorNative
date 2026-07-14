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
// The content node is `height: auto`, `width: 100%`, `flexDirection: column`,
// and the scroll node is `overflow: scroll` so Yoga does not clamp the content
// to the viewport. THE CONTENT NODE'S COMPUTED HEIGHT *IS* THE CONTENT SIZE —
// read straight out of Yoga, never derived shell-side from a union of child
// frames (two shells deriving it independently is precisely where Android and
// iOS drift apart). A scroll node's wire child at index i is the CONTENT node's
// child at index i, in the Yoga tree AND the view tree, on both platforms.
//
// ── THE FRAME TABLE (dp, each frame RELATIVE TO ITS PARENT) ──────────────────
//
// root BnColumn                        fills the host; children stack vertically
//  ├─ [0] scroll  (0,   0, 300, 200)   BnScroll W=300 H=200 — the VIEWPORT
//  │    └─ content (0,  0, 300, 800)   SYNTHETIC. 10 × 80 = 800 → THE CONTENT SIZE
//  │         ├─ row 0  (0,   0, 300, 80)   H=80; no width → stretched to the
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
// At offset 0: rows 0 and 1 are fully inside the viewport, row 2 straddles its
// bottom edge (160..240 against a 200 cut), and rows 3-9 are entirely below it.
// At offset 600 (the maximum): rows 7, 8 and 9 fill the viewport and the content
// cannot scroll further. The load-bearing assertion is contentSize > viewport,
// FROM YOGA — that is the whole phase; the scroll-and-assert step proves the
// viewport actually moves over the content, not merely that the numbers add up.
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
    /// <summary>The ten row colours, in row order. Distinct per row: a scrolled
    /// row is identified in a screenshot by its colour, not by a nodeId.
    /// Mirrored by BnScrollDemoTests.RowColors — the golden asserts them.</summary>
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

    /// <summary>Ten rows × 80dp. Both numbers are load-bearing: their product
    /// (800) is the content size Gates 2/3 read out of Yoga, and 800 - 200 = 600
    /// is the scrollable range they drive.</summary>
    private const string RowHeight = "80";

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
    /// sizes itself to its content and never scrolls (the shells warn once when
    /// that happens; here it simply cannot).</summary>
    private static void BuildScrollSection(RenderTreeBuilder b, int seq)
    {
        b.OpenComponent<BnScroll>(seq);
        b.AddComponentParameter(seq + 1, nameof(BnScroll.Width), "300");
        b.AddComponentParameter(seq + 2, nameof(BnScroll.Height), "200");
        b.AddComponentParameter(seq + 3, nameof(BnScroll.ChildContent), (RenderFragment)(rb =>
        {
            for (var i = 0; i < RowColors.Length; i++)
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
                rb.CloseComponent();
            }
        }));
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
