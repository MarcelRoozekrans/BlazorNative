using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorNative.Components;

// ─────────────────────────────────────────────────────────────────────────────
// BnFlexPreset — Phase 6.1 Task 1.3 (design decision 3).
//
// The shared body of BnRow and BnColumn: the ENTIRE BnView parameter surface
// except Direction, forwarded verbatim into a BnView whose direction the
// subclass fixes. A BnRow IS a row — it does not expose Direction at all, so
// the preset cannot be silently overridden and there is no "which one wins?"
// question to answer at runtime.
//
// One body, not two copy-pasted ones: a new BnView param that someone forgets
// to forward is a hole an author falls into. BnComponentTests pins the
// forwarding reflectively (Presets_ForwardEveryBnViewParameterExceptDirection).
//
// There is deliberately NO BnStack: it would be a synonym for BnColumn, and two
// names for one thing is a library smell on day one. (The M6 contract named it;
// the 6.1 design consciously drops it.)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Base for the direction presets (<see cref="BnRow"/> /
/// <see cref="BnColumn"/>): every <see cref="BnView"/> parameter except
/// <see cref="BnView.Direction"/>, forwarded to a <see cref="BnView"/> whose
/// direction is fixed by <see cref="PresetDirection"/>.</summary>
public abstract class BnFlexPreset : ComponentBase
{
    /// <summary>The direction this preset nails down. Not a
    /// <see cref="ParameterAttribute"/> — that is the whole point.</summary>
    protected abstract FlexDirection PresetDirection { get; }

    /// <inheritdoc cref="BnView.BackgroundColor"/>
    [Parameter] public string? BackgroundColor { get; set; }

    /// <inheritdoc cref="BnView.Padding"/>
    [Parameter] public string? Padding { get; set; }

    /// <inheritdoc cref="BnView.Margin"/>
    [Parameter] public string? Margin { get; set; }

    /// <inheritdoc cref="BnView.Justify"/>
    [Parameter] public Justify? Justify { get; set; }

    /// <inheritdoc cref="BnView.Align"/>
    [Parameter] public Align? Align { get; set; }

    /// <inheritdoc cref="BnView.Wrap"/>
    [Parameter] public Wrap? Wrap { get; set; }

    /// <inheritdoc cref="BnView.Gap"/>
    [Parameter] public string? Gap { get; set; }

    /// <inheritdoc cref="BnView.AlignSelf"/>
    [Parameter] public Align? AlignSelf { get; set; }

    /// <inheritdoc cref="BnView.Grow"/>
    [Parameter] public float? Grow { get; set; }

    /// <inheritdoc cref="BnView.Shrink"/>
    [Parameter] public float? Shrink { get; set; }

    /// <inheritdoc cref="BnView.Basis"/>
    [Parameter] public string? Basis { get; set; }

    /// <inheritdoc cref="BnView.Width"/>
    [Parameter] public string? Width { get; set; }

    /// <inheritdoc cref="BnView.Height"/>
    [Parameter] public string? Height { get; set; }

    /// <inheritdoc cref="BnView.MinWidth"/>
    [Parameter] public string? MinWidth { get; set; }

    /// <inheritdoc cref="BnView.MaxWidth"/>
    [Parameter] public string? MaxWidth { get; set; }

    /// <inheritdoc cref="BnView.MinHeight"/>
    [Parameter] public string? MinHeight { get; set; }

    /// <inheritdoc cref="BnView.MaxHeight"/>
    [Parameter] public string? MaxHeight { get; set; }

    /// <inheritdoc cref="BnView.Position"/>
    [Parameter] public Position? Position { get; set; }

    /// <inheritdoc cref="BnView.Top"/>
    [Parameter] public string? Top { get; set; }

    /// <inheritdoc cref="BnView.Right"/>
    [Parameter] public string? Right { get; set; }

    /// <inheritdoc cref="BnView.Bottom"/>
    [Parameter] public string? Bottom { get; set; }

    /// <inheritdoc cref="BnView.Left"/>
    [Parameter] public string? Left { get; set; }

    /// <inheritdoc cref="BnView.ChildContent"/>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenComponent<BnView>(0);
        b.AddComponentParameter(1, nameof(BnView.Direction), PresetDirection);

        b.AddComponentParameter(2, nameof(BnView.BackgroundColor), BackgroundColor);
        b.AddComponentParameter(3, nameof(BnView.Padding), Padding);
        b.AddComponentParameter(4, nameof(BnView.Margin), Margin);

        b.AddComponentParameter(5, nameof(BnView.Justify), Justify);
        b.AddComponentParameter(6, nameof(BnView.Align), Align);
        b.AddComponentParameter(7, nameof(BnView.Wrap), Wrap);
        b.AddComponentParameter(8, nameof(BnView.Gap), Gap);

        b.AddComponentParameter(9, nameof(BnView.AlignSelf), AlignSelf);
        b.AddComponentParameter(10, nameof(BnView.Grow), Grow);
        b.AddComponentParameter(11, nameof(BnView.Shrink), Shrink);
        b.AddComponentParameter(12, nameof(BnView.Basis), Basis);

        b.AddComponentParameter(13, nameof(BnView.Width), Width);
        b.AddComponentParameter(14, nameof(BnView.Height), Height);
        b.AddComponentParameter(15, nameof(BnView.MinWidth), MinWidth);
        b.AddComponentParameter(16, nameof(BnView.MaxWidth), MaxWidth);
        b.AddComponentParameter(17, nameof(BnView.MinHeight), MinHeight);
        b.AddComponentParameter(18, nameof(BnView.MaxHeight), MaxHeight);

        b.AddComponentParameter(19, nameof(BnView.Position), Position);
        b.AddComponentParameter(20, nameof(BnView.Top), Top);
        b.AddComponentParameter(21, nameof(BnView.Right), Right);
        b.AddComponentParameter(22, nameof(BnView.Bottom), Bottom);
        b.AddComponentParameter(23, nameof(BnView.Left), Left);

        b.AddComponentParameter(24, nameof(BnView.ChildContent), ChildContent);
        b.CloseComponent();
    }
}
