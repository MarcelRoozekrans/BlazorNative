using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorNative.Components;

// ─────────────────────────────────────────────────────────────────────────────
// BnScroll — Phase 6.2 Task 1.1 (design §"The component").
//
// A VIEWPORT whose content may exceed it. It emits the `scroll` element, which
// MapElementToNodeType has mapped to NodeType 6 ("scroll") since Phase 2.5 —
// so this component is a pure Components-side addition: NO new export, NO new
// NodeType, NO new patch kind, NO ABI change (9 exports + the 72-byte bridge).
// The stub the shells have carried since 2.5 becomes real in Gates 2/3.
//
// ── THE SYNTHETIC CONTENT NODE (what the shells do with this node) ───────────
// A scroll node's wire children are NOT parented into the scroll node itself.
// The shells interpose a SYNTHETIC content node — created shell-side, never on
// the wire:
//
//     wire tree:            view/Yoga trees:
//     scroll                scroll                 overflow: scroll  (viewport)
//      ├─ child 0            └─ content            height:auto, width:100%,
//      ├─ child 1                 ├─ child 0       flexDirection:column
//      └─ …                       ├─ child 1       ← its COMPUTED HEIGHT
//                                 └─ …               *is* the content size
//
// Yoga owns every frame, including the off-screen ones, and `contentSize` is
// read straight out of the content node — never derived by a shell from a union
// of child frames (two shells deriving it independently is exactly where Android
// and iOS drift apart).
//
// ── DIRECTION IS ABSENT BY CONSTRUCTION ──────────────────────────────────────
// The surface is BnView's MINUS Direction, the way BnRow's is: scrolling is
// VERTICAL-ONLY (6.2 design decision 2 — Android's ScrollView is a vertical-only
// widget class, and horizontal is ledgered). A `flexDirection` on the scroll node
// governs the placement of its only child — the synthetic content node — so a
// row direction would lay that node out across the cross axis, stretch it to the
// viewport height and silently kill scrolling. The symptom ("this page just
// doesn't scroll") is baffling; a parameter that cannot be set is a bug that
// cannot be written.
//
// ── A SCROLL NODE NEEDS A DEFINITE HEIGHT ────────────────────────────────────
// An auto-height scroll node sizes itself TO its content and therefore never
// scrolls. Give it an explicit Height, or Grow="1" inside a bounded parent (the
// common case: a scroll filling the screen under the host root). Nothing is
// defaulted here — the wire says exactly what the author said (the un-styled
// invariant); the SHELLS warn once at runtime when a scroll node ends up
// auto-height (Gates 2/3).
//
// Sequence numbers mirror BnView's exactly, gap at 4 (`flexDirection`) included:
// the two BuildRenderTrees are meant to be read side by side, and the gap is the
// decision, visible.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A scrolling viewport — emits the <c>scroll</c> element (host NodeType
/// "scroll"). It <em>is</em> a flex item (<see cref="BnView"/>'s whole
/// parameter surface except <c>Direction</c>); it just scrolls its content.
/// </summary>
/// <remarks>
/// <para>
/// <b>Vertical only</b> (6.2 design decision 2) — hence no <c>Direction</c>
/// parameter. Horizontal scrolling is a different widget class on Android and
/// is ledgered, not stubbed.
/// </para>
/// <para>
/// <b>Give it a definite height.</b> A <c>BnScroll</c> with no
/// <see cref="Height"/> (and no <see cref="Grow"/> inside a bounded parent)
/// sizes itself to its content and never scrolls. The shells log one warning
/// when that happens.
/// </para>
/// <para>
/// Children ride the wire as children of the scroll node; the shells re-parent
/// them into a synthetic content node whose Yoga-computed height is the content
/// size (see the file header).
/// </para>
/// </remarks>
public sealed class BnScroll : ComponentBase
{
    /// <inheritdoc cref="BnView.BackgroundColor"/>
    [Parameter] public string? BackgroundColor { get; set; }

    /// <inheritdoc cref="BnView.Padding"/>
    [Parameter] public string? Padding { get; set; }

    /// <inheritdoc cref="BnView.Margin"/>
    [Parameter] public string? Margin { get; set; }

    // ── Container layout (applies to the scroll node's content) ───────────────
    //
    // NO Direction: vertical-only (see the file header).

    /// <inheritdoc cref="BnView.Justify"/>
    [Parameter] public FlexJustify? Justify { get; set; }

    /// <inheritdoc cref="BnView.Align"/>
    [Parameter] public FlexAlign? Align { get; set; }

    /// <inheritdoc cref="BnView.Wrap"/>
    [Parameter] public FlexWrap? Wrap { get; set; }

    /// <inheritdoc cref="BnView.Gap"/>
    [Parameter] public string? Gap { get; set; }

    // ── Item layout (how the viewport behaves INSIDE its parent) ──────────────

    /// <inheritdoc cref="BnView.AlignSelf"/>
    [Parameter] public FlexAlign? AlignSelf { get; set; }

    /// <inheritdoc cref="BnView.Grow"/>
    [Parameter] public float? Grow { get; set; }

    /// <inheritdoc cref="BnView.Shrink"/>
    [Parameter] public float? Shrink { get; set; }

    /// <inheritdoc cref="BnView.Basis"/>
    [Parameter] public string? Basis { get; set; }

    // ── Box ───────────────────────────────────────────────────────────────────

    /// <inheritdoc cref="BnView.Width"/>
    [Parameter] public string? Width { get; set; }

    /// <summary>Height of the VIEWPORT, e.g. <c>"200"</c>. Null = auto — and an
    /// auto-height scroll sizes to its content and never scrolls (see the class
    /// remarks). Either this or <see cref="Grow"/> in a bounded parent.</summary>
    [Parameter] public string? Height { get; set; }

    /// <inheritdoc cref="BnView.MinWidth"/>
    [Parameter] public string? MinWidth { get; set; }

    /// <inheritdoc cref="BnView.MaxWidth"/>
    [Parameter] public string? MaxWidth { get; set; }

    /// <inheritdoc cref="BnView.MinHeight"/>
    [Parameter] public string? MinHeight { get; set; }

    /// <inheritdoc cref="BnView.MaxHeight"/>
    [Parameter] public string? MaxHeight { get; set; }

    // ── Positioning ───────────────────────────────────────────────────────────

    /// <inheritdoc cref="BnView.Position"/>
    [Parameter] public FlexPosition? Position { get; set; }

    /// <inheritdoc cref="BnView.Top"/>
    [Parameter] public string? Top { get; set; }

    /// <inheritdoc cref="BnView.Right"/>
    [Parameter] public string? Right { get; set; }

    /// <inheritdoc cref="BnView.Bottom"/>
    [Parameter] public string? Bottom { get; set; }

    /// <inheritdoc cref="BnView.Left"/>
    [Parameter] public string? Left { get; set; }

    /// <summary>The scrolled content. Rides the wire as children of the scroll
    /// node; the shells parent it into the synthetic content node.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "scroll");

        // Null attributes are not appended to the frame array at all — that is
        // how "unset" reaches the wire as "absent" (BnView's un-styled invariant,
        // and the reason this component needs no null guards).
        b.AddAttribute(1, "backgroundColor", BackgroundColor);
        b.AddAttribute(2, "padding", Padding);
        b.AddAttribute(3, "margin", Margin);

        // 4 ("flexDirection") is deliberately UNUSED — see the file header.
        b.AddAttribute(5, "justifyContent", Justify.ToStyleValue());
        b.AddAttribute(6, "alignItems", Align.ToStyleValue());
        b.AddAttribute(7, "flexWrap", Wrap.ToStyleValue());
        b.AddAttribute(8, "gap", Gap);

        b.AddAttribute(9, "alignSelf", AlignSelf.ToStyleValue());
        b.AddAttribute(10, "flexGrow", Grow.ToStyleValue());
        b.AddAttribute(11, "flexShrink", Shrink.ToStyleValue());
        b.AddAttribute(12, "flexBasis", Basis);

        b.AddAttribute(13, "width", Width);
        b.AddAttribute(14, "height", Height);
        b.AddAttribute(15, "minWidth", MinWidth);
        b.AddAttribute(16, "maxWidth", MaxWidth);
        b.AddAttribute(17, "minHeight", MinHeight);
        b.AddAttribute(18, "maxHeight", MaxHeight);

        b.AddAttribute(19, "position", Position.ToStyleValue());
        b.AddAttribute(20, "top", Top);
        b.AddAttribute(21, "right", Right);
        b.AddAttribute(22, "bottom", Bottom);
        b.AddAttribute(23, "left", Left);

        b.AddContent(100, ChildContent);

        b.CloseElement();
    }
}
