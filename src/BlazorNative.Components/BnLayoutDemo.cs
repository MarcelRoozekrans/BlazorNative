using BlazorNative.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorNative.Components;

// ─────────────────────────────────────────────────────────────────────────────
// BnLayoutDemo — Phase 6.1 Task 1.4 (design §"The proof surface"), route
// "/layout", mount-registry key "BnLayoutDemo".
//
// The flex demo is a THIRD page (design decision 2): BnDemo and BnSettingsPage
// keep their goldens on all four surfaces, so layout-engine bugs never arrive
// mixed with golden-rewrite noise.
//
// Its whole job is to have COMPUTED FRAMES THAT ARE FULLY PREDICTABLE, because
// three surfaces assert against them: the .NET patch golden (BnLayoutDemoTests),
// the Android instrumented frames (Gate 2) and the iOS XCTest frames (Gate 3) —
// and the last two assert THE SAME NUMBERS. That pairing is M6 DoD #2 ("lays
// out identically on both platforms"), so every size here is explicit and every
// position derives from Yoga's documented defaults. Yoga computes in
// density-independent units on both platforms (iOS points 1:1; Android
// multiplies by density at frame-apply time), so the table below is in dp/pt.
//
// ── THE FRAME TABLE (dp, each frame RELATIVE TO ITS PARENT) ──────────────────
//
// root BnColumn                       fills the host; children stack vertically
//  ├─ [0] row section   (0,   0, 300, 100)   BnRow    W=300 H=100
//  │    ├─ box A        (0,   0,  50, 100)   W=50           ← cross-stretch → h=100
//  │    ├─ box B        (50,  0, 200, 100)   Grow=1         ← absorbs 300-50-50
//  │    └─ box C        (250, 0,  50, 100)   W=50
//  ├─ [1] column sect.  (0, 100, 300, 200)   BnColumn W=300 H=200 Justify=SpaceBetween
//  │    ├─ item 0       (0,   0, 100,  40)                  free = 200-3*40 = 80,
//  │    ├─ item 1       (100, 80, 100,  40)   AlignSelf=Center   split into 2 gaps
//  │    └─ item 2       (0, 160, 100,  40)                       of 40 → y 0/80/160
//  ├─ [2] wrap section  (0, 300, 300, 100)   BnRow    W=300 H=100 Wrap=Wrap
//  │    ├─ wrap 0       (0,   0,  90,  40)   line 1 (3 × 90 = 270 of 300 — 30dp
//  │    ├─ wrap 1       (90,  0,  90,  40)   line 1   of slack, see below)
//  │    ├─ wrap 2       (180, 0,  90,  40)   line 1
//  │    └─ wrap 3       (0,  40,  90,  40)   line 2 — alignContent defaults to
//  │                                          flex-start in Yoga, so line 2 sits
//  │                                          at the line-1 cross size (40)
//  ├─ [3] text section  (0, 400, 150,  H)    BnRow W=150, NO height
//  │    └─ text leaf    (0,   0, w,    H)    w ≤ 150; H = the NATIVELY MEASURED
//  │                                          height of the wrapped label
//  └─ [4] back section  (0, 400+H, 300, Hb)  BnRow W=300, NO height
//       └─ "← Back"     (0,   0, Wb,   Hb)   Hb/Wb = the button's measured size
//
// The wrap row's boxes are 90dp, not 100. Four 100s in a 300 row would put three
// on line 1 ONLY because Yoga's break test is `consumed + item > available` and
// 300 > 300 is false — i.e. the demo would sit EXACTLY on the wrap boundary, and
// half a dp of rounding drift on either shell would drop box 3 to line 2 and make
// the two platforms "disagree" for a reason that has nothing to do with the
// engine. 90 × 3 = 270 leaves 30dp of slack: the break is unambiguous.
//
// The last two rows are the DoD #3 proof and are the ONLY frames without fixed
// numbers — deliberately: a font's metrics are not a constant we get to invent.
// The shells assert them RELATIONALLY (H > 0; the text row's height EQUALS the
// leaf's measured height; the back row starts at y = 400 + H). Everything above
// them is a fixed number on both platforms, which is why the measured leaves
// sit at the END of the column: a font-dependent height must not shift the
// frames the parity assertion is built on.
//
// Nav parity with the other pages: "← Back" → INavigationManager → "/".
// Reachable by mount-name ("BnLayoutDemo") and by route ("/layout"); no button
// on BnDemo points here — adding one would churn BnDemo's four goldens for no
// engine reason (non-negotiable #4).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The flexbox proof page — see the file header for the pinned frame
/// table. A fresh ROOT component (like BnSettingsPage): no cascaded theme
/// crosses a root swap.</summary>
public sealed class BnLayoutDemo : ComponentBase
{
    // Distinct per-box colours: the AVD/simulator screenshots are read by
    // humans too, and a wrong frame is easier to SEE than to diff.
    private const string BoxA = "#E57373"; // red
    private const string BoxB = "#64B5F6"; // blue  — the Grow=1 box
    private const string BoxC = "#81C784"; // green
    private const string Item0 = "#FFB74D"; // orange
    private const string Item1 = "#BA68C8"; // purple — the AlignSelf=Center child
    private const string Item2 = "#4DB6AC"; // teal
    private const string WrapBox = "#90A4AE"; // grey

    /// <summary>The long label whose NATIVELY MEASURED size drives its frame
    /// (DoD #3): 150dp of row is far too narrow for it, so it must wrap and
    /// report a multi-line height — and its row must grow to that height.</summary>
    private const string MeasuredText =
        "This label is measured natively: it wraps inside 150dp and its measured height drives the row.";

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
        BuildRowSection(b, 0);
        BuildColumnSection(b, 100);
        BuildWrapSection(b, 200);
        BuildTextSection(b, 300);
        BuildBackSection(b, 400);
    }

    /// <summary>[0] fixed 50 · Grow=1 · fixed 50, inside a 300×100 row. The grow
    /// box absorbs exactly the remainder (200); the boxes have no height, so the
    /// row's default cross-axis stretch gives them its full 100.</summary>
    private static void BuildRowSection(RenderTreeBuilder b, int seq)
    {
        b.OpenComponent<BnRow>(seq);
        b.AddComponentParameter(seq + 1, nameof(BnRow.Width), "300");
        b.AddComponentParameter(seq + 2, nameof(BnRow.Height), "100");
        b.AddComponentParameter(seq + 3, nameof(BnRow.ChildContent), (RenderFragment)(rb =>
        {
            rb.OpenComponent<BnView>(0);
            rb.AddComponentParameter(1, nameof(BnView.Width), "50");
            rb.AddComponentParameter(2, nameof(BnView.BackgroundColor), BoxA);
            rb.CloseComponent();

            rb.OpenComponent<BnView>(10);
            rb.AddComponentParameter(11, nameof(BnView.Grow), 1f);
            rb.AddComponentParameter(12, nameof(BnView.BackgroundColor), BoxB);
            rb.CloseComponent();

            rb.OpenComponent<BnView>(20);
            rb.AddComponentParameter(21, nameof(BnView.Width), "50");
            rb.AddComponentParameter(22, nameof(BnView.BackgroundColor), BoxC);
            rb.CloseComponent();
        }));
        b.CloseComponent();
    }

    /// <summary>[1] space-between over a 200-high column: three 40-high items →
    /// 80dp of free space split into two 40dp gaps (y = 0 / 80 / 160). The middle
    /// item overrides the cross axis with AlignSelf=Center → x = (300-100)/2.</summary>
    private static void BuildColumnSection(RenderTreeBuilder b, int seq)
    {
        b.OpenComponent<BnColumn>(seq);
        b.AddComponentParameter(seq + 1, nameof(BnColumn.Width), "300");
        b.AddComponentParameter(seq + 2, nameof(BnColumn.Height), "200");
        b.AddComponentParameter(seq + 3, nameof(BnColumn.Justify), FlexJustify.SpaceBetween);
        b.AddComponentParameter(seq + 4, nameof(BnColumn.ChildContent), (RenderFragment)(cb =>
        {
            cb.OpenComponent<BnView>(0);
            cb.AddComponentParameter(1, nameof(BnView.Width), "100");
            cb.AddComponentParameter(2, nameof(BnView.Height), "40");
            cb.AddComponentParameter(3, nameof(BnView.BackgroundColor), Item0);
            cb.CloseComponent();

            cb.OpenComponent<BnView>(10);
            cb.AddComponentParameter(11, nameof(BnView.Width), "100");
            cb.AddComponentParameter(12, nameof(BnView.Height), "40");
            cb.AddComponentParameter(13, nameof(BnView.AlignSelf), FlexAlign.Center);
            cb.AddComponentParameter(14, nameof(BnView.BackgroundColor), Item1);
            cb.CloseComponent();

            cb.OpenComponent<BnView>(20);
            cb.AddComponentParameter(21, nameof(BnView.Width), "100");
            cb.AddComponentParameter(22, nameof(BnView.Height), "40");
            cb.AddComponentParameter(23, nameof(BnView.BackgroundColor), Item2);
            cb.CloseComponent();
        }));
        b.CloseComponent();
    }

    /// <summary>[2] four 90-wide boxes in a 300-wide wrapping row: three fit on
    /// line 1 (270 of 300), the fourth overflows onto line 2 at y = 40 (the line-1
    /// cross size — Yoga's alignContent defaults to flex-start).
    /// <para>90, not 100, ON PURPOSE: four 100s would leave the row sitting exactly
    /// on Yoga's break boundary (<c>consumed + item &gt; available</c>; 300 &gt; 300
    /// is false), where a half-dp of rounding on either shell flips box 3 onto
    /// line 2. 30dp of slack makes the break a fact, not a coin toss.</para></summary>
    private static void BuildWrapSection(RenderTreeBuilder b, int seq)
    {
        b.OpenComponent<BnRow>(seq);
        b.AddComponentParameter(seq + 1, nameof(BnRow.Width), "300");
        b.AddComponentParameter(seq + 2, nameof(BnRow.Height), "100");
        b.AddComponentParameter(seq + 3, nameof(BnRow.Wrap), FlexWrap.Wrap);
        b.AddComponentParameter(seq + 4, nameof(BnRow.ChildContent), (RenderFragment)(wb =>
        {
            for (var i = 0; i < 4; i++)
            {
                wb.OpenComponent<BnView>(i * 10);
                wb.AddComponentParameter((i * 10) + 1, nameof(BnView.Width), "90");
                wb.AddComponentParameter((i * 10) + 2, nameof(BnView.Height), "40");
                wb.AddComponentParameter((i * 10) + 3, nameof(BnView.BackgroundColor), WrapBox);
                wb.CloseComponent();
            }
        }));
        b.CloseComponent();
    }

    /// <summary>[3] the DoD #3 proof: a 150-wide row with NO height. The label
    /// cannot fit on one line, so the shell's Yoga measure function reports its
    /// WRAPPED height — and the row hugs it. Nothing above this section depends
    /// on that height (it is the second-to-last child), so the fixed frames stay
    /// fixed on both platforms.</summary>
    private static void BuildTextSection(RenderTreeBuilder b, int seq)
    {
        b.OpenComponent<BnRow>(seq);
        b.AddComponentParameter(seq + 1, nameof(BnRow.Width), "150");
        b.AddComponentParameter(seq + 2, nameof(BnRow.ChildContent), (RenderFragment)(tb =>
        {
            tb.OpenComponent<BnText>(0);
            tb.AddComponentParameter(1, nameof(BnText.Text), MeasuredText);
            tb.CloseComponent();
        }));
        b.CloseComponent();
    }

    /// <summary>[4] nav parity with BnDemo/BnSettingsPage — and a second measured
    /// leaf (a button sizes itself).</summary>
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
