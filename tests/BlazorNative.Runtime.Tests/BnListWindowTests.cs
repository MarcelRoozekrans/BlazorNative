using BlazorNative.Components;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// BnListWindowTests — Phase 7.2 Task 1.2: the pure window function, exhausted.
//
// BnListWindow.Compute is the arithmetic every device frame in Gates 2/3 is
// derived from, so this table is deliberately wider than "looks right": both
// ends, both clamps, empty/single, a viewport taller than the content,
// fractional and exact-boundary offsets, and the two ILLEGAL offsets that are
// runtime DATA rather than misuse — negative (iOS rubber-band) and past the
// end (the 6.2 shrink case) — which must CLAMP, not throw.
//
// The demo-shaped rows (500 × 64dp in a 400dp viewport, overscan 4) double as
// the numbers BnListDemo's header promises Gates 2/3: 11 live rows at the
// top, 15 mid-scroll, 11 at the bottom.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class BnListWindowTests
{
    // The BnListDemo shape — one source for every demo-shaped case below.
    private const float Viewport = 400f;
    private const float ItemHeight = 64f;
    private const int Count = 500;
    private const int Overscan = 4;

    private static (int Start, int End) Demo(float offset)
        => BnListWindow.Compute(offset, Viewport, ItemHeight, Count, Overscan);

    // ── The demo table (the numbers Gates 2/3 assert on devices) ──────────────

    /// <summary>Top of the list: 400/64 spans rows 0..6 (row 6 partially), +4
    /// trailing overscan, leading overscan clamped at 0 → [0, 11) = 11 rows.</summary>
    [Fact]
    public void AtTheTop_WindowIsElevenRows_LeadingOverscanClamped()
        => Assert.Equal((0, 11), Demo(0f));

    /// <summary>Mid-scroll (offset 640 = exactly 10 rows): visible span is rows
    /// 10..16 (1040/64 = 16.25 → row 16 partial), ±4 overscan → [6, 21) = 15
    /// rows — the design's ceil(400/64) + 2×4 liveness count.</summary>
    [Fact]
    public void MidScroll_WindowIsFifteenRows_OverscanOnBothSides()
        => Assert.Equal((6, 21), Demo(640f));

    /// <summary>Bottom (offset = the 31,600 scroll range): visible span is rows
    /// 493..499, +4 leading overscan, trailing clamped at 500 → [489, 500) =
    /// 11 rows.</summary>
    [Fact]
    public void AtTheBottom_WindowIsElevenRows_TrailingOverscanClamped()
        => Assert.Equal((489, 500), Demo(Count * ItemHeight - Viewport));

    // ── Overscan clamping near (not at) the ends ──────────────────────────────

    /// <summary>One row down (offset 64): first visible is row 1, overscan
    /// wants row −3 — clamped to 0; bottom edge 464/64 = 7.25 → row 7 partial,
    /// +4 → [0, 12).</summary>
    [Fact]
    public void NearTheTop_LeadingOverscanClampsAtZero()
        => Assert.Equal((0, 12), Demo(64f));

    /// <summary>One row up from the bottom (offset 31,536): bottom edge
    /// 31,936/64 = 499 exactly — row 499 NOT visible (half-open), overscan
    /// wants 503 — clamped to 500; top edge 492.75 → row 492, −4 →
    /// [488, 500).</summary>
    [Fact]
    public void NearTheBottom_TrailingOverscanClampsAtCount()
        => Assert.Equal((488, 500), Demo(Count * ItemHeight - Viewport - 64f));

    /// <summary>Zero overscan is legal: the window is exactly the visible
    /// span.</summary>
    [Fact]
    public void ZeroOverscan_WindowIsTheVisibleSpanOnly()
        => Assert.Equal((10, 17), BnListWindow.Compute(640f, Viewport, ItemHeight, Count, 0));

    // ── The illegal offsets that are DATA, not misuse: they CLAMP ─────────────

    /// <summary>iOS rubber-band over-scrolls the top with NEGATIVE offsets —
    /// they clamp to 0 and the window is the top window.</summary>
    [Theory]
    [InlineData(-0.5f)]
    [InlineData(-40f)]
    [InlineData(float.NegativeInfinity)]
    public void NegativeOffset_RubberBand_ClampsToTheTopWindow(float offset)
        => Assert.Equal(Demo(0f), Demo(offset));

    /// <summary>An offset past the end (content shrank under a scrolled
    /// viewport — the 6.2 shrink case — or bottom rubber-band) clamps to the
    /// scroll range: the bottom window, never an out-of-range index.</summary>
    [Theory]
    [InlineData(31_601f)]
    [InlineData(999_999f)]
    [InlineData(float.PositiveInfinity)]
    public void OffsetPastTheEnd_ShrinkCase_ClampsToTheBottomWindow(float offset)
        => Assert.Equal(Demo(Count * ItemHeight - Viewport), Demo(offset));

    // ── Degenerate lists ──────────────────────────────────────────────────────

    /// <summary>An empty list has the empty window at ANY offset — and never
    /// throws for one.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(-40f)]
    [InlineData(12_345f)]
    public void EmptyList_WindowIsEmpty(float offset)
        => Assert.Equal((0, 0), BnListWindow.Compute(offset, Viewport, ItemHeight, 0, Overscan));

    /// <summary>A single item in a viewport taller than it: the whole list,
    /// [0, 1).</summary>
    [Fact]
    public void SingleItem_ViewportTaller_WindowIsTheWholeList()
        => Assert.Equal((0, 1), BnListWindow.Compute(0f, Viewport, ItemHeight, 1, Overscan));

    /// <summary>Content shorter than the viewport: the scroll range is 0, every
    /// offset resolves to 0, and the window is always the whole list.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(-10f)]
    [InlineData(500f)]
    public void ViewportTallerThanContent_WindowIsTheWholeList_AtAnyOffset(float offset)
        => Assert.Equal((0, 3), BnListWindow.Compute(offset, Viewport, ItemHeight, 3, Overscan));

    // ── Fractional and boundary offsets ───────────────────────────────────────

    /// <summary>A fractional offset (100.5): the top edge is inside row 1
    /// (100.5/64 ≈ 1.57 → floor 1), the bottom edge inside row 7 (500.5/64 ≈
    /// 7.82 → ceil 8). Overscan 0 isolates the visible-span arithmetic.</summary>
    [Fact]
    public void FractionalOffset_FloorsTheTopEdge_CeilsTheBottomEdge()
        => Assert.Equal((1, 8), BnListWindow.Compute(100.5f, Viewport, ItemHeight, Count, 0));

    /// <summary>A bottom edge landing EXACTLY on a row boundary does not make
    /// the row below it visible: offset 128 + viewport 384 = 512 = 8×64 →
    /// rows [2, 8), not [2, 9). Half-open all the way down.</summary>
    [Fact]
    public void ExactRowBoundaryAtTheBottomEdge_ExcludesTheNextRow()
        => Assert.Equal((2, 8), BnListWindow.Compute(128f, 384f, ItemHeight, Count, 0));

    /// <summary>A top edge exactly on a row boundary starts AT that row —
    /// offset 128 is row 2's first pixel, so row 1 is not visible.</summary>
    [Fact]
    public void ExactRowBoundaryAtTheTopEdge_StartsAtThatRow()
        => Assert.Equal((2, 10), BnListWindow.Compute(128f, 512f, ItemHeight, Count, 0));

    // ── Misuse guards (author bugs THROW; they are not runtime data) ──────────

    [Theory]
    [InlineData(0f)]
    [InlineData(-400f)]
    [InlineData(float.NaN)]
    public void NonPositiveViewport_IsMisuse_Throws(float viewport)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => BnListWindow.Compute(0f, viewport, ItemHeight, Count, Overscan));

    [Theory]
    [InlineData(0f)]
    [InlineData(-64f)]
    [InlineData(float.NaN)]
    public void NonPositiveItemHeight_IsMisuse_Throws(float itemHeight)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => BnListWindow.Compute(0f, Viewport, itemHeight, Count, Overscan));

    [Fact]
    public void NegativeCount_IsMisuse_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => BnListWindow.Compute(0f, Viewport, ItemHeight, -1, Overscan));

    [Fact]
    public void NegativeOverscan_IsMisuse_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => BnListWindow.Compute(0f, Viewport, ItemHeight, Count, -1));

    /// <summary>A NaN OFFSET is a corrupt shell sample, not a clampable value —
    /// it would floor-cast to int.MinValue and produce an insane window. Loud,
    /// per the misuse posture (the dispatch window turns it into an rc-2
    /// fault a shell log can see).</summary>
    [Fact]
    public void NaNOffset_IsACorruptSample_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => BnListWindow.Compute(float.NaN, Viewport, ItemHeight, Count, Overscan));

    // ── The spacer identity BnList builds on: the window tiles the content ────

    /// <summary>For any window, lead spacer + live rows + trail spacer must
    /// tile the content EXACTLY: Start·h + (End−Start)·h + (Count−End)·h =
    /// Count·h. Trivially true of a half-open window — pinned so a future
    /// "inclusive End" refactor cannot silently double-count a row.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(100.5f)]
    [InlineData(640f)]
    [InlineData(31_600f)]
    public void TheWindowTilesTheContent_SpacersPlusRowsEqualCount(float offset)
    {
        var (start, end) = Demo(offset);
        Assert.InRange(start, 0, Count);
        Assert.InRange(end, start, Count);
        Assert.Equal(Count, start + (end - start) + (Count - end));
    }
}
