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
/// <c>"16sp"</c> are not part of the grammar. <see cref="BnLayoutItem.Grow"/> and
/// <see cref="BnLayoutItem.Shrink"/> are the exceptions — they are unitless
/// ratios rather than lengths.
/// </para>
/// <para>
/// The parameters fall into three groups: how this view arranges its
/// <b>children</b> (<see cref="Direction"/>, <see cref="BnLayoutContainer.Justify"/>,
/// <see cref="BnLayoutContainer.Align"/>, <see cref="BnLayoutContainer.Wrap"/>,
/// <see cref="BnLayoutContainer.Gap"/>); how this view behaves as an
/// <b>item</b> inside its own parent (<see cref="BnLayoutItem.AlignSelf"/>,
/// <see cref="BnLayoutItem.Grow"/>, <see cref="BnLayoutItem.Shrink"/>,
/// <see cref="BnLayoutItem.Basis"/>); and its own <b>box</b>
/// (<see cref="BnLayoutItem.Width"/>, <see cref="BnLayoutItem.Height"/>, the
/// min/max pair, and the <see cref="BnLayoutItem.Position"/> insets).
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
public sealed class BnView : BnLayoutContainer
{
    /// <summary>Main axis (<c>flexDirection</c>). Null = Yoga's default (column).</summary>
    [Parameter] public FlexDirection? Direction { get; set; }

    /// <summary>Nested content rendered inside this view.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "div");

        // Sequence 1-17: the shared item surface — see
        // BnLayoutItem.EmitItemAttributes. Every AddAttribute call below (and
        // inside it) is a no-op when its value is null (an ELEMENT attribute
        // with a null value is not appended to the frame array at all) — that
        // is how "unset" reaches the wire as "absent".
        EmitItemAttributes(b);

        // Sequence 50-54: the shared container surface (Padding, Justify,
        // Align, Wrap, Gap) — see BnLayoutContainer.EmitContainerAttributes.
        EmitContainerAttributes(b);

        // Sequence 100+: this component's own surface, clear of both base
        // bands so a collision (which would produce a wrong diff, silently)
        // cannot happen.
        b.AddAttribute(100, "flexDirection", Direction.ToStyleValue());

        b.AddContent(200, ChildContent);

        b.CloseElement();
    }
}
