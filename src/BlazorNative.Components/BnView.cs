using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorNative.Components;

/// <summary>
/// A container: a box that holds other components and arranges them with
/// flexbox. Renders as a plain native view — a <c>FrameLayout</c> on Android,
/// a <c>UIView</c> on iOS — positioned by Yoga.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every parameter is optional, and an unset one is never sent to the
/// platform.</b> A <c>BnView</c> with no parameters is an unstyled container and
/// the layout engine's own defaults apply: children stack vertically, packed
/// toward the top and stretched across the width.
/// </para>
/// <para>
/// <b>Lengths.</b> Every length-valued parameter takes a bare number in
/// density-independent units — <c>"16"</c> means 16dp on Android and 16pt on
/// iOS — or a percentage such as <c>"50%"</c>, or <c>"auto"</c> where the layout
/// engine allows it. There is no unit suffix: <c>"16dp"</c>, <c>"16px"</c> and
/// <c>"16sp"</c> are not part of the grammar. <see cref="Grow"/> and
/// <see cref="Shrink"/> are the exceptions — they are unitless ratios rather
/// than lengths.
/// </para>
/// <para>
/// The parameters fall into three groups: how this view arranges its
/// <b>children</b> (<see cref="Direction"/>, <see cref="Justify"/>,
/// <see cref="Align"/>, <see cref="Wrap"/>, <see cref="Gap"/>); how this view
/// behaves as an <b>item</b> inside its own parent (<see cref="AlignSelf"/>,
/// <see cref="Grow"/>, <see cref="Shrink"/>, <see cref="Basis"/>); and its own
/// <b>box</b> (<see cref="Width"/>, <see cref="Height"/>, the min/max pair, and
/// the <see cref="Position"/> insets).
/// </para>
/// <example>
/// A header row pinned to the top of a filling column:
/// <code>
/// &lt;BnColumn Grow="1" Padding="16" Gap="8"&gt;
///     &lt;BnRow Justify="FlexJustify.SpaceBetween"&gt;
///         &lt;BnText Text="Inbox" /&gt;
///         &lt;BnText Text="3" /&gt;
///     &lt;/BnRow&gt;
///     &lt;BnView Grow="1" BackgroundColor="#EEEEEE" /&gt;
/// &lt;/BnColumn&gt;
/// </code>
/// </example>
/// </remarks>
public sealed class BnView : ComponentBase
{
    /// <summary>Background color as a hex string, e.g. <c>"#FFEEAA"</c>. Null
    /// leaves the view transparent.</summary>
    [Parameter] public string? BackgroundColor { get; set; }

    /// <summary>Inside spacing, in density-independent units — <c>16</c> is 16dp
    /// on Android and 16pt on iOS. Children are placed inside the padding box,
    /// so padding shrinks the space they get rather than moving this view. Null
    /// = none. Unlike the other lengths this one is a number, not a string:
    /// percentage and <c>"auto"</c> paddings are not expressible.</summary>
    [Parameter] public float? Padding { get; set; }

    /// <summary>Outside spacing, e.g. <c>"8"</c> — space between this view's box
    /// and its siblings. Null = none.</summary>
    [Parameter] public string? Margin { get; set; }

    // ── Container layout (how THIS view arranges its children) ────────────────

    /// <summary>Main axis (<c>flexDirection</c>). Null = Yoga's default (column).</summary>
    [Parameter] public FlexDirection? Direction { get; set; }

    /// <summary>Main-axis distribution (<c>justifyContent</c>). Null = flex-start.</summary>
    [Parameter] public FlexJustify? Justify { get; set; }

    /// <summary>Cross-axis alignment of the children (<c>alignItems</c>). Null = stretch.</summary>
    [Parameter] public FlexAlign? Align { get; set; }

    /// <summary>Line wrapping (<c>flexWrap</c>). Null = nowrap.</summary>
    [Parameter] public FlexWrap? Wrap { get; set; }

    /// <summary>Gap between children, e.g. <c>"8"</c> (<c>gap</c>). Null = 0.</summary>
    [Parameter] public string? Gap { get; set; }

    // ── Item layout (how this view behaves INSIDE its parent) ─────────────────

    /// <summary>Cross-axis override for this item (<c>alignSelf</c>). Null = auto
    /// (inherit the parent's <see cref="Align"/>).</summary>
    [Parameter] public FlexAlign? AlignSelf { get; set; }

    /// <summary>Share of the free main-axis space this item absorbs
    /// (<c>flexGrow</c>) — a UNITLESS number, not a length. Null = 0.</summary>
    [Parameter] public float? Grow { get; set; }

    /// <summary>Share of the main-axis overflow this item gives back
    /// (<c>flexShrink</c>) — a UNITLESS number. Null = Yoga's default.</summary>
    [Parameter] public float? Shrink { get; set; }

    /// <summary>Main-axis base size (<c>flexBasis</c>): <c>"auto"</c> | <c>"50%"</c>
    /// | <c>"120"</c>. Null = auto.</summary>
    [Parameter] public string? Basis { get; set; }

    // ── Box ───────────────────────────────────────────────────────────────────
    //
    // Every string-valued param below is a LAYOUT LENGTH and speaks the ONE
    // normative grammar — design §"Style value grammar (normative)": a bare
    // number (density-independent units: dp on Android, points on iOS), "N%",
    // or "auto" where Yoga allows it. NO unit suffix: "12dp"/"12px"/"12sp" are
    // not in the grammar and nothing here emits one.

    /// <summary>Width, e.g. <c>"300"</c> or <c>"50%"</c>. Null = auto.</summary>
    [Parameter] public string? Width { get; set; }

    /// <summary>Height, e.g. <c>"100"</c> or <c>"50%"</c>. Null = auto.</summary>
    [Parameter] public string? Height { get; set; }

    /// <summary>Minimum width. Null = unset.</summary>
    [Parameter] public string? MinWidth { get; set; }

    /// <summary>Maximum width. Null = unset.</summary>
    [Parameter] public string? MaxWidth { get; set; }

    /// <summary>Minimum height. Null = unset.</summary>
    [Parameter] public string? MinHeight { get; set; }

    /// <summary>Maximum height. Null = unset.</summary>
    [Parameter] public string? MaxHeight { get; set; }

    // ── Positioning ───────────────────────────────────────────────────────────

    /// <summary>Positioning mode (<c>position</c>). Null = relative (in flow).</summary>
    [Parameter] public FlexPosition? Position { get; set; }

    /// <summary>Top inset. Null = unset.</summary>
    [Parameter] public string? Top { get; set; }

    /// <summary>Right inset. Null = unset.</summary>
    [Parameter] public string? Right { get; set; }

    /// <summary>Bottom inset. Null = unset.</summary>
    [Parameter] public string? Bottom { get; set; }

    /// <summary>Left inset. Null = unset.</summary>
    [Parameter] public string? Left { get; set; }

    /// <summary>Nested content rendered inside this view.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "div");

        // Every AddAttribute below is a no-op when its value is null (an
        // ELEMENT attribute with a null value is not appended to the frame
        // array at all) — that is how "unset" reaches the wire as "absent".
        b.AddAttribute(1, "backgroundColor", BackgroundColor);
        b.AddAttribute(2, "padding", Padding.ToStyleValue());
        b.AddAttribute(3, "margin", Margin);

        b.AddAttribute(4, "flexDirection", Direction.ToStyleValue());
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
