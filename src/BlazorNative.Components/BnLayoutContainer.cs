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
/// <para>
/// <b>There is deliberately no <c>ContainerAttributes</c> splat</b> — the twin of
/// <see cref="BnLayoutItem"/>'s <c>ItemAttributes</c>, which exists for components
/// written as markup, whose generated render tree cannot call an emit helper. No
/// container in this package is written that way, so the splat would be public
/// surface with no caller, frozen for good at the first stable release. Add it
/// with the first markup-authored container, not before.
/// </para>
/// <para>
/// <b>You do not derive from this yourself</b>, for the same reason given on
/// <see cref="BnLayoutItem"/>: it is public and abstract to serve the components in
/// this package, not as an extension point.
/// </para>
/// </remarks>
public abstract class BnLayoutContainer : BnLayoutItem
{
    /// <summary>Space inside the box, between its edge and its children. Null = none.</summary>
    /// <remarks>Percentages are legal; <c>auto</c> is not, which is why this is
    /// <see cref="BnLength"/> and not <see cref="BnAutoLength"/>.</remarks>
    [Parameter] public BnLength? Padding { get; set; }

    /// <summary>Main-axis distribution of children. Null = Yoga's default (flex-start).</summary>
    [Parameter] public FlexJustify? Justify { get; set; }

    /// <summary>Cross-axis alignment of children. Null = Yoga's default (stretch).</summary>
    [Parameter] public FlexAlign? Align { get; set; }

    /// <summary>Whether children wrap onto new lines. Null = Yoga's default (nowrap).</summary>
    [Parameter] public FlexWrap? Wrap { get; set; }

    /// <summary>Space between children. Null = none.</summary>
    [Parameter] public BnLength? Gap { get; set; }

    /// <summary>Emits the container surface as ELEMENT attributes. Occupies 50–54.</summary>
    protected void EmitContainerAttributes(RenderTreeBuilder b)
    {
        b.AddAttribute(50, "padding",        Padding.ToStyleValue());
        b.AddAttribute(51, "justifyContent", Justify.ToStyleValue());
        b.AddAttribute(52, "alignItems",     Align.ToStyleValue());
        b.AddAttribute(53, "flexWrap",       Wrap.ToStyleValue());
        b.AddAttribute(54, "gap",            Gap.ToStyleValue());
    }

    /// <summary>Forwards the container surface as COMPONENT parameters. Occupies 50–54.</summary>
    protected void ForwardContainerParameters(RenderTreeBuilder b)
    {
        // Not formatted, deliberately: these are COMPONENT parameters, so the value stays
        // typed all the way to the base that emits it. Only the element paths format (R1).
        b.AddComponentParameter(50, nameof(Padding), Padding);
        b.AddComponentParameter(51, nameof(Justify), Justify);
        b.AddComponentParameter(52, nameof(Align),   Align);
        b.AddComponentParameter(53, nameof(Wrap),    Wrap);
        b.AddComponentParameter(54, nameof(Gap),     Gap);
    }
}
