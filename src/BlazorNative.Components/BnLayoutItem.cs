using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorNative.Components;

// ─────────────────────────────────────────────────────────────────────────────
// ⚠ THE PROTECTED HELPERS ARE NAMED IN <c>, NOT <see cref>, AND THAT IS LOAD-
// BEARING. EmitItemAttributes / ItemAttributes / ForwardItemParameters are
// protected: they are the component-AUTHOR's surface, not the consumer's. The
// docs reference runs xmldoc2md with --member-accessibility-level public (a
// deliberate choice — see scripts/generate-reference.ps1, which explains that
// the `protected` default would document ComponentBase's web-hosting members on
// every page of a framework that renders to native widgets). So these members
// get no heading, and a <see cref> to one from a doc comment that IS emitted —
// this class-level <remarks> — renders as a link to an anchor no heading has.
// `onBrokenAnchors: 'throw'` then fails `npm run build`. Naming them in <c>
// reads identically in an IDE tooltip and cannot rot. Restoring the crefs reds
// the site build, not the compiler, so nothing local will tell you.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The parameters every component can carry as an <b>item inside its parent's
/// layout</b> — how it aligns itself, how it grows and shrinks, its own box, and
/// its position insets.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the single declaration of the item surface.</b> Previously
/// these seventeen parameters were copy-pasted across eight components and absent
/// from four more, so a <c>BnText</c> could not take a margin and a <c>BnButton</c>
/// could take nothing at all. Any component that participates in layout derives
/// from this type; <c>LayoutSurfacePinTests</c> reds if one does not, and
/// <c>LayoutSurfaceSequenceBandTests</c> reds if one derives it and then fails to
/// EMIT it — declaring a parameter and dropping it every frame is the defect this
/// type exists to make impossible, not a lesser version of it.
/// </para>
/// <para>
/// <b>You do not derive from this yourself.</b> It is public and abstract because
/// the components in this package derive from it across their own file boundaries,
/// not as an extension point: the shells map a fixed vocabulary of element names,
/// so a component of your own opening an element they do not know would produce a
/// node neither shell can create — and the pins that keep this surface honest scan
/// only this assembly, so nothing would tell you. Compose the components here
/// instead.
/// </para>
/// <para>
/// <b>Sequence-number bands are normative, and there are two ways to take this
/// surface.</b> A component that writes its own render tree calls
/// <c>EmitItemAttributes</c>, which occupies <b>1–17</b>;
/// <see cref="BnLayoutContainer"/> owns 50–99; the component's own attributes
/// start at 100 and <c>ChildContent</c> is 200. Keeping to those bands matters
/// because a collision does not throw — it produces a wrong diff, silently.
/// <c>LayoutSurfaceSequenceBandTests</c> enforces both halves for every
/// component that writes its own render tree: no two attributes in one region
/// share a sequence, and each attribute sits in the band its name belongs to.
/// The splat form described next is exempt from the band half, and necessarily
/// so — the Razor compiler assigns those numbers, not the author.
/// </para>
/// <para>
/// A component written as markup cannot do that: its render tree is generated
/// for it and every sequence number is assigned by the compiler, so there is no
/// point at which <c>EmitItemAttributes</c> could run. Those components
/// splat <c>ItemAttributes</c> instead, and all seventeen then share a
/// <em>single</em> sequence number rather than spanning 1–17. That is still
/// correct, and not by luck: repeated sequence numbers inside a run of
/// attributes make the diff match them <b>by name</b> instead of by position, so
/// a value that changes updates, and a value that goes null still emits its
/// reset. It costs a dictionary per render, which is why the numbered form
/// remains the default wherever it is reachable.
/// </para>
/// <para>
/// <b>Lengths are unchanged here.</b> Every length is still a string in
/// the shells' grammar (a bare number, <c>N%</c>, or <c>auto</c> where allowed).
/// Giving them a stricter type is a separate, later change.
/// </para>
/// </remarks>
public abstract class BnLayoutItem : ComponentBase
{
    /// <summary>Fill colour behind the component. Null leaves it transparent.</summary>
    [Parameter] public string? BackgroundColor { get; set; }

    /// <summary>Space outside the component, between it and its siblings. Null = none.</summary>
    [Parameter] public string? Margin { get; set; }

    /// <summary>Cross-axis alignment for this item alone, overriding the parent's. Null = inherit.</summary>
    [Parameter] public FlexAlign? AlignSelf { get; set; }

    /// <summary>Share of leftover space this item takes (unitless ratio). Null = Yoga's default (0).</summary>
    [Parameter] public float? Grow { get; set; }

    /// <summary>Share of overflow this item gives up (unitless ratio). Null = Yoga's default (1).</summary>
    [Parameter] public float? Shrink { get; set; }

    /// <summary>Starting main-axis size before grow/shrink. Null = <c>auto</c>.</summary>
    [Parameter] public string? Basis { get; set; }

    /// <summary>Box width. Null = <c>auto</c>.</summary>
    [Parameter] public string? Width { get; set; }

    /// <summary>Box height. Null = <c>auto</c>.</summary>
    [Parameter] public string? Height { get; set; }

    /// <summary>Lower bound on width. Null = unset.</summary>
    [Parameter] public string? MinWidth { get; set; }

    /// <summary>Upper bound on width. Null = unset.</summary>
    [Parameter] public string? MaxWidth { get; set; }

    /// <summary>Lower bound on height. Null = unset.</summary>
    [Parameter] public string? MinHeight { get; set; }

    /// <summary>Upper bound on height. Null = unset.</summary>
    [Parameter] public string? MaxHeight { get; set; }

    /// <summary>Positioning scheme. Null = Yoga's default (relative).</summary>
    [Parameter] public FlexPosition? Position { get; set; }

    /// <summary>Top inset. Null = unset.</summary>
    [Parameter] public string? Top { get; set; }

    /// <summary>Right inset. Null = unset.</summary>
    [Parameter] public string? Right { get; set; }

    /// <summary>Bottom inset. Null = unset.</summary>
    [Parameter] public string? Bottom { get; set; }

    /// <summary>Left inset. Null = unset.</summary>
    [Parameter] public string? Left { get; set; }

    /// <summary>
    /// Emits the item surface as ELEMENT attributes, for components that open
    /// their own element. Occupies sequence numbers 1–17.
    /// </summary>
    /// <remarks>
    /// A null value is a no-op: an element attribute with a null value is not
    /// appended to the frame array at all. That is how "unset" reaches the wire
    /// as "absent", and it is why granting this surface to a component that
    /// never had it cannot move any frame table.
    /// </remarks>
    protected void EmitItemAttributes(RenderTreeBuilder b)
    {
        b.AddAttribute(1,  "backgroundColor", BackgroundColor);
        b.AddAttribute(2,  "margin",          Margin);
        b.AddAttribute(3,  "alignSelf",       AlignSelf.ToStyleValue());
        b.AddAttribute(4,  "flexGrow",        Grow.ToStyleValue());
        b.AddAttribute(5,  "flexShrink",      Shrink.ToStyleValue());
        b.AddAttribute(6,  "flexBasis",       Basis);
        b.AddAttribute(7,  "width",           Width);
        b.AddAttribute(8,  "height",          Height);
        b.AddAttribute(9,  "minWidth",        MinWidth);
        b.AddAttribute(10, "maxWidth",        MaxWidth);
        b.AddAttribute(11, "minHeight",       MinHeight);
        b.AddAttribute(12, "maxHeight",       MaxHeight);
        b.AddAttribute(13, "position",        Position.ToStyleValue());
        b.AddAttribute(14, "top",             Top);
        b.AddAttribute(15, "right",           Right);
        b.AddAttribute(16, "bottom",          Bottom);
        b.AddAttribute(17, "left",            Left);
    }

    /// <summary>
    /// The item surface as a splat, for components whose render tree the Razor
    /// compiler generates. Element emitters use <see cref="EmitItemAttributes"/>
    /// instead — it gives exact sequence numbers, which a splat cannot.
    /// </summary>
    protected IReadOnlyDictionary<string, object?> ItemAttributes
    {
        get
        {
            var d = new Dictionary<string, object?>(17);

            // The null filter is DEFENSIVE, not load-bearing, and the difference
            // was measured rather than assumed: AddMultipleAttributes funnels
            // every pair through AddAttribute(int, string, object?), which
            // already refuses to append a null-valued attribute to an ELEMENT.
            // Deleting this guard changes no frame today (mutation-tested — the
            // whole suite stays green). It is kept because that is an element-
            // only rule: splatted onto a COMPONENT, a null would be appended as
            // a real parameter and would overwrite a default. Filtering here
            // makes the dictionary mean "the attributes the author set" at both
            // destinations instead of only one.
            void Add(string k, object? v) { if (v is not null) d[k] = v; }
            Add("backgroundColor", BackgroundColor);
            Add("margin",          Margin);
            Add("alignSelf",       AlignSelf.ToStyleValue());
            Add("flexGrow",        Grow.ToStyleValue());
            Add("flexShrink",      Shrink.ToStyleValue());
            Add("flexBasis",       Basis);
            Add("width",           Width);
            Add("height",          Height);
            Add("minWidth",        MinWidth);
            Add("maxWidth",        MaxWidth);
            Add("minHeight",       MinHeight);
            Add("maxHeight",       MaxHeight);
            Add("position",        Position.ToStyleValue());
            Add("top",             Top);
            Add("right",           Right);
            Add("bottom",          Bottom);
            Add("left",            Left);
            return d;
        }
    }

    /// <summary>
    /// Forwards the item surface as COMPONENT parameters, for wrappers that render
    /// another <see cref="BnLayoutItem"/> rather than an element. Occupies 1–17.
    /// </summary>
    /// <remarks>
    /// <b>These names are matched as strings at runtime</b> by
    /// <see cref="RenderTreeBuilder.AddComponentParameter"/>, so a rename that
    /// updated the declaration and missed a forward would fail at runtime rather
    /// than at compile time. <c>nameof</c> keeps them honest, and
    /// <c>ForwardedParameterNameTests</c> pins that every forwarded name really is
    /// a parameter on the target.
    /// </remarks>
    protected void ForwardItemParameters(RenderTreeBuilder b)
    {
        b.AddComponentParameter(1,  nameof(BackgroundColor), BackgroundColor);
        b.AddComponentParameter(2,  nameof(Margin),          Margin);
        b.AddComponentParameter(3,  nameof(AlignSelf),       AlignSelf);
        b.AddComponentParameter(4,  nameof(Grow),            Grow);
        b.AddComponentParameter(5,  nameof(Shrink),          Shrink);
        b.AddComponentParameter(6,  nameof(Basis),           Basis);
        b.AddComponentParameter(7,  nameof(Width),           Width);
        b.AddComponentParameter(8,  nameof(Height),          Height);
        b.AddComponentParameter(9,  nameof(MinWidth),        MinWidth);
        b.AddComponentParameter(10, nameof(MaxWidth),        MaxWidth);
        b.AddComponentParameter(11, nameof(MinHeight),       MinHeight);
        b.AddComponentParameter(12, nameof(MaxHeight),       MaxHeight);
        b.AddComponentParameter(13, nameof(Position),        Position);
        b.AddComponentParameter(14, nameof(Top),             Top);
        b.AddComponentParameter(15, nameof(Right),           Right);
        b.AddComponentParameter(16, nameof(Bottom),          Bottom);
        b.AddComponentParameter(17, nameof(Left),            Left);
    }
}
