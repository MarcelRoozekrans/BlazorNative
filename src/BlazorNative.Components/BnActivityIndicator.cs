using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorNative.Components;

// ─────────────────────────────────────────────────────────────────────────────
// BnActivityIndicator — Phase 7.4 (design decision 5: the RN parity survey's
// cheap win).
//
// Emits the `activityindicator` element → NodeType 12. Android: ProgressBar
// (indeterminate). iOS: UIActivityIndicatorView (.medium). Animating while
// mounted — presence is `@if` at the AUTHOR's level (the decision-2 posture
// BnModal set: hide is unmount, never a hidden style).
//
// NO PARAMETERS, and that is the design, not an omission: "no props, no new
// wire surface" (decision 5). A measured LEAF — its intrinsic size is the
// platform's own (a fresh ProgressBar / UIActivityIndicatorView measured the
// same way — the 6.3 oracle method), so there is nothing to declare; and it
// has no state, no events and no children. The wire shape is ONE
// CreateNodePatch and nothing else — pinned by BnActivityIndicatorTests (the
// measure-leaf wire shape), including the reflective no-parameters pin so a
// param growing here is a deliberate design change, not drift. The shells add
// `activityindicator` to MEASURED_NODE_TYPES in Gates 2/3.
//
// Hand-written C#, NOT .razor — and not by preference: a zero-attribute
// element is exactly the shape the Razor compiler's static-markup
// optimization collapses to AddMarkupContent("<activityindicator>…"), which
// the native renderer rejects by contract (non-whitespace markup is not
// representable on a native widget tree — the 7.1 footgun's cousin, observed
// red-first in this phase's Gate 1). The 7.1 recipe ("everything new is
// .razor") applies to components with a surface; the csproj header's "leaf
// primitives stay hand-written C#" rule is what a surface-less leaf falls
// under, for this structural reason.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// An indeterminate spinner — emits the <c>activityindicator</c> element
/// (host NodeType "activityindicator", wire id 12). Android:
/// <c>ProgressBar</c>; iOS: <c>UIActivityIndicatorView</c>. It animates while
/// mounted: show it with <c>@if</c>, hide it by unmounting — there is no
/// start/stop parameter, and no parameter at all (a measured leaf whose
/// intrinsic size is the platform's own).
/// </summary>
public sealed class BnActivityIndicator : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "activityindicator");
        b.CloseElement();
    }
}
