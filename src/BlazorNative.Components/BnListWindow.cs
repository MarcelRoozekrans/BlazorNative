namespace BlazorNative.Components;

// ─────────────────────────────────────────────────────────────────────────────
// BnListWindow — Phase 7.2 Task 1.2 (design §"BnList": "The window calc is a
// PURE STATIC FUNCTION (offset, viewport, itemHeight, count, overscan) →
// (start, end) — unit-tested exhaustively; the component consumes it").
//
// EXACT ARITHMETIC, BY DESIGN (decision 3: fixed ItemHeight required in 7.2).
// Every frame the shells assert on a device is derived from this function's
// answer, so it must be deterministic and boring: no floats leak out, no
// culture, no rounding surprises. BnListWindowTests owns the edge-case table;
// BnListDemo's numbers (500 × 64dp in a 400dp viewport, overscan 4 → 11 rows
// at the top, 15 mid-scroll, 11 at the bottom) are its products.
//
// THE WINDOW IS HALF-OPEN: [Start, End) over the item indices. End − Start is
// the live row count; Start × itemHeight is the lead spacer's height;
// (count − End) × itemHeight is the trail spacer's — the spacer arithmetic in
// BnList is a direct read of this tuple.
//
// OFFSET IS CLAMPED HERE, NOT TRUSTED: iOS's rubber-band sends NEGATIVE
// offsets while over-scrolling at the top, and an offset PAST the end arrives
// when content shrinks under a scrolled viewport (the 6.2 shrink case) or
// while rubber-banding at the bottom. Both clamp to the legal scroll range —
// the window a shell can actually show is the window this returns.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The pure window calculation behind <see cref="BnList{TItem}"/>:
/// which item indices are live (visible + overscan) at a scroll offset.</summary>
internal static class BnListWindow
{
    /// <summary>Computes the half-open live window <c>[Start, End)</c>.</summary>
    /// <param name="offset">Content offset in dp/pt (the 6.3 unit rule). Clamped
    /// to the legal range <c>[0, max(0, count·itemHeight − viewport)]</c> —
    /// negative (iOS rubber-band) and past-the-end (6.2 shrink) offsets are
    /// runtime DATA, not misuse.</param>
    /// <param name="viewport">Viewport height in dp/pt. Must be positive — a
    /// list without a definite viewport has no window (component misuse).</param>
    /// <param name="itemHeight">Fixed row height in dp/pt (design decision 3).
    /// Must be positive (misuse).</param>
    /// <param name="count">Item count. Must be non-negative (misuse).</param>
    /// <param name="overscan">Extra rows rendered on EACH side of the visible
    /// span, clamped at both list ends. Must be non-negative (misuse).</param>
    internal static (int Start, int End) Compute(
        float offset, float viewport, float itemHeight, int count, int overscan)
    {
        // Misuse guards — these are AUTHOR bugs, not runtime data, so they
        // throw (the strict posture) rather than clamp into a silent wrong
        // window nobody can debug from a device screenshot.
        if (!(viewport > 0f))
            throw new ArgumentOutOfRangeException(nameof(viewport), viewport,
                "a BnList viewport must have a positive definite height — the window is undefined otherwise");
        if (!(itemHeight > 0f))
            throw new ArgumentOutOfRangeException(nameof(itemHeight), itemHeight,
                "ItemHeight must be positive (fixed row heights are 7.2's design decision 3)");
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "item count cannot be negative");
        if (overscan < 0)
            throw new ArgumentOutOfRangeException(nameof(overscan), overscan, "overscan cannot be negative");
        // A NaN offset is a shell contract violation, not a legal sample: it
        // would clamp to NaN and floor-cast to int.MinValue — a silently
        // insane window. Throw instead; the dispatch window surfaces it as a
        // loud rc-2 fault. (±Infinity needs no guard: Clamp resolves it to a
        // range end.)
        if (float.IsNaN(offset))
            throw new ArgumentOutOfRangeException(nameof(offset), offset,
                "a NaN scroll offset is not a legal sample — the shell's conflation slot is corrupt");

        if (count == 0)
            return (0, 0); // an empty list has an empty window at any offset

        // Clamp the offset to the legal scroll range. Content shorter than the
        // viewport has range 0 — every offset resolves to 0 and the window is
        // the whole list (via the End clamp below).
        //
        // EXACT ARITHMETIC IN DOUBLE (#124): float is exact for integers only
        // to 2²⁴ (16,777,216), but a real list scrolls well past that
        // (millions of rows × tens of dp: count·itemHeight ≈ 3.5e7 here). In
        // float, maxOffset and the floor/ceil divisions round across a ROW
        // boundary and the window drifts by a row. double is exact to 2⁵³ —
        // beyond any list a device can hold — so every intermediate below runs
        // in double. The public inputs stay float (the shell's unit is float);
        // only the internal arithmetic widens.
        var maxOffset = Math.Max(0.0, (double)count * itemHeight - viewport);
        var clamped = Math.Clamp((double)offset, 0.0, maxOffset);

        // First visible row: the row containing the viewport's top edge.
        // Last visible row boundary (EXCLUSIVE): ceil of the bottom edge — a
        // bottom edge landing exactly on a row boundary does NOT make the row
        // below it visible (half-open all the way down).
        var firstVisible = (int)Math.Floor(clamped / itemHeight);
        var lastVisibleExclusive = (int)Math.Ceiling((clamped + viewport) / itemHeight);

        var start = Math.Max(firstVisible - overscan, 0);
        var end = Math.Min(lastVisibleExclusive + overscan, count);
        return (start, end);
    }
}
