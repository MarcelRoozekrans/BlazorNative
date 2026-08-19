using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorNative.Components;

// ─────────────────────────────────────────────────────────────────────────────
// BnActivityIndicator — the RN parity survey's cheap win (design decision 5).
//
// Emits the `activityindicator` element → NodeType 12. Android: ProgressBar
// (indeterminate). iOS: UIActivityIndicatorView (.medium). Animating while
// mounted — presence is `@if` at the AUTHOR's level (the BnModal posture:
// hide is unmount, never a hidden style).
//
// NO CONTROL PARAMETERS, and that is the design, not an omission: "no props,
// no new wire surface" (decision 5) — it has no start/stop, no state, no
// events and no children. What it DOES take, since 13.0 Task 8, is the SAME
// item surface every other layout participant takes: it is a measured LEAF,
// exactly like `image`/`checkbox`/`switch`/`slider`/`picker` — a real Yoga
// node with a measure function attached BY NODETYPE (both shells' widget
// mappers put `activityindicator` in the SAME unified node-creation path as
// every other widget, with no special-cased fixed size outside Yoga). Its
// INTRINSIC size (Width/Height left null) is the platform's own, exactly as
// before; setting Width and Height makes the box definite, the same
// declared-vs-intrinsic choice BnImage documents. The wire shape gains
// sequence 1-17 from EmitItemAttributes and is otherwise unchanged — pinned
// by BnActivityIndicatorTests (the measure-leaf wire shape), including the
// reflective no-control-parameters pin so a control param growing here is a
// deliberate design change, not drift.
//
// Hand-written C#, NOT .razor — and not by preference: a zero-own-attribute
// element is exactly the shape the Razor compiler's static-markup
// optimization collapses to AddMarkupContent("<activityindicator>…"), which
// the native renderer rejects by contract (non-whitespace markup is not
// representable on a native widget tree). The "everything new is .razor"
// recipe applies to components with their OWN surface; the csproj header's
// "leaf primitives stay hand-written C#" rule is what a control-surface-less
// leaf falls under, for this structural reason — EmitItemAttributes(b) is
// reachable from hand-written C# the way a splat is reachable from .razor.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// An indeterminate spinner. Renders as an indeterminate <c>ProgressBar</c> on
/// Android and a <c>UIActivityIndicatorView</c> on iOS.
/// </summary>
/// <remarks>
/// <para>
/// <b>It has no control parameters, including no start/stop.</b> It animates for
/// as long as it is mounted, so you control it with <c>@if</c> — render it while
/// you are loading and unmount it when you are done. There is no hidden state to
/// get wrong.
/// </para>
/// <para>
/// <b>Its size is the platform's own by default, but you can override it.</b> Leave
/// <see cref="BnLayoutItem.Width"/> and <see cref="BnLayoutItem.Height"/> unset and
/// it measures itself, the same as always. Set both and the box is exactly those
/// numbers instead. The rest of the shared item surface (<see cref="BnLayoutItem.Margin"/>,
/// <see cref="BnLayoutItem.AlignSelf"/>, <see cref="BnLayoutItem.Grow"/>,
/// <see cref="BnLayoutItem.Shrink"/>, <see cref="BnLayoutItem.Basis"/>,
/// <see cref="BnLayoutItem.Position"/>) works exactly as it does on any other
/// component. Put it in a <see cref="BnView"/> if you need to place or pad it
/// some other way.
/// </para>
/// <example>
/// <code>
/// @if (_loading)
/// {
///     &lt;BnActivityIndicator /&gt;
/// }
/// </code>
/// </example>
/// </remarks>
public sealed class BnActivityIndicator : BnLayoutItem
{
    // The item surface (BackgroundColor, Margin, AlignSelf, Grow/Shrink/Basis,
    // the box, Position and its insets) is inherited from BnLayoutItem and
    // emitted at sequence 1-17 by EmitItemAttributes — see that type for the
    // parameters. This component has no surface of its own.

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "activityindicator");

        // Sequence 1-17: the shared item surface — see BnLayoutItem.EmitItemAttributes.
        EmitItemAttributes(b);

        b.CloseElement();
    }
}
