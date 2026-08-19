using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorNative.Components;

/// <summary>
/// A <see cref="BnLayoutItem"/> that also arranges its <b>children</b> with
/// flexbox — padding inside its own box, and the main/cross-axis rules its
/// children obey.
/// </summary>
/// <remarks>
/// <para>
/// Only components that lay out children with flexbox derive from this type:
/// <c>BnView</c> and <c>BnFlexPreset</c>. <c>BnScroll</c> deliberately does not —
/// it has children but no flex parameters of its own, so it is a plain
/// <see cref="BnLayoutItem"/>.
/// </para>
/// <para>
/// <b><c>ChildContent</c> is not declared here.</b> Having children and arranging
/// them with flexbox are different capabilities, and conflating them would give
/// <c>BnScroll</c> a meaningless <c>Justify</c>. Each component that accepts
/// children declares <c>ChildContent</c> itself, at sequence 200.
/// </para>
/// <para>Sequence band: this type owns <b>50–54</b>.</para>
/// </remarks>
public abstract class BnLayoutContainer : BnLayoutItem
{
    /// <summary>Space inside the box, between its edge and its children. Null = none.</summary>
    /// <remarks>A bare number only — percentage and <c>auto</c> paddings are not expressible.</remarks>
    [Parameter] public float? Padding { get; set; }

    /// <summary>Main-axis distribution of children. Null = Yoga's default (flex-start).</summary>
    [Parameter] public FlexJustify? Justify { get; set; }

    /// <summary>Cross-axis alignment of children. Null = Yoga's default (stretch).</summary>
    [Parameter] public FlexAlign? Align { get; set; }

    /// <summary>Whether children wrap onto new lines. Null = Yoga's default (nowrap).</summary>
    [Parameter] public FlexWrap? Wrap { get; set; }

    /// <summary>Space between children. Null = none.</summary>
    [Parameter] public string? Gap { get; set; }

    /// <summary>Emits the container surface as ELEMENT attributes. Occupies 50–54.</summary>
    protected void EmitContainerAttributes(RenderTreeBuilder b)
    {
        b.AddAttribute(50, "padding",        Padding.ToStyleValue());
        b.AddAttribute(51, "justifyContent", Justify.ToStyleValue());
        b.AddAttribute(52, "alignItems",     Align.ToStyleValue());
        b.AddAttribute(53, "flexWrap",       Wrap.ToStyleValue());
        b.AddAttribute(54, "gap",            Gap);
    }

    /// <summary>Forwards the container surface as COMPONENT parameters. Occupies 50–54.</summary>
    protected void ForwardContainerParameters(RenderTreeBuilder b)
    {
        b.AddComponentParameter(50, nameof(Padding), Padding);
        b.AddComponentParameter(51, nameof(Justify), Justify);
        b.AddComponentParameter(52, nameof(Align),   Align);
        b.AddComponentParameter(53, nameof(Wrap),    Wrap);
        b.AddComponentParameter(54, nameof(Gap),     Gap);
    }
}
