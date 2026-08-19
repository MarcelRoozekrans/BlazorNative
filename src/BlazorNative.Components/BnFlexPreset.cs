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
// ── WHERE THE SURFACE COMES FROM (Phase 13.0) ────────────────────────────────
// This type used to REDECLARE all 22 of those parameters and then forward them
// one AddComponentParameter at a time — a second copy of BnView's surface
// wearing different clothes, and the copy an author fell into whenever someone
// added a BnView parameter and forgot the twin here.
//
// Both halves now come from the same place BnView's do: BnFlexPreset derives
// from BnLayoutContainer, so the 17 item parameters and the 5 container
// parameters are DECLARED once (in BnLayoutItem / BnLayoutContainer) and
// FORWARDED once (ForwardItemParameters / ForwardContainerParameters). There is
// no longer a list on this side that can fall out of step with BnView's,
// because there is no longer a list on this side at all.
//
// BnComponentTests still pins BOTH halves, and both still matter:
//   • Presets_ForwardEveryBnViewParameterExceptDirection — DECLARATION (the
//     preset exposes exactly BnView's params minus Direction), reflectively,
//     comparing NAME AND TYPE. It cannot see BuildRenderTree at all.
//   • BnRow_/BnColumn_ForwardsTheWholeFlexSurface_* — FORWARDING (mounted with
//     the full param dictionary, the preset emits BnView's whole SetStyle table
//     plus its own flexDirection). Delete a line from either Forward* helper
//     and THAT test goes red; the reflective one stays green while
//     <BnRow Grow="1"> silently does nothing.
//
// ── DO NOT GUARD THE FORWARDING ON null ──────────────────────────────────────
// Every AddComponentParameter in the Forward* helpers fires UNCONDITIONALLY,
// nulls included, and that is load-bearing. Blazor hands a component a
// ParameterView containing only the parameters that were SUPPLIED; anything
// absent is simply not written, so the target keeps whatever it held from the
// previous render. An `if (Grow is not null)` "optimisation" (saving 22 boxed
// nulls per render) would therefore leave BnView holding a STALE Grow the
// moment an author writes `Grow = cond ? 1 : null` — resurrecting, one level
// up, the exact null-reset bug Task 1.2 fixed in the renderer. Null must
// travel. (Pinned: BnRow_GrowGoesNull_ForwardsTheNullThroughToTheStyleWire.)
//
// This is the one place the COMPONENT forward and the ELEMENT emit differ, and
// they differ on purpose: a null ELEMENT attribute is never appended to the
// frame array at all (absent means unset on the wire), while a null COMPONENT
// parameter must be appended, because appending it is what resets the target
// to its default. Two spellings of "unset", implemented oppositely.
//
// ── Sequence numbers (Phase 13.0) ────────────────────────────────────────────
// The bands are the same ones every other component in this library uses:
// 1-17 the item surface, 50-54 the container surface, 100+ this component's
// own vocabulary, 200 ChildContent. Keeping to them matters because a
// collision does not throw — it produces a wrong diff, silently.
//
// There is deliberately NO BnStack: it would be a synonym for BnColumn, and two
// names for one thing is a library smell on day one. (The M6 contract named it;
// the 6.1 design consciously drops it.)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The shared base of the direction presets <see cref="BnRow"/> and
/// <see cref="BnColumn"/>.</summary>
/// <remarks>
/// It carries every <see cref="BnView"/> parameter except
/// <see cref="BnView.Direction"/> — the item surface from
/// <see cref="BnLayoutItem"/> and the container surface from
/// <see cref="BnLayoutContainer"/> — and forwards them to a
/// <see cref="BnView"/> whose direction the subclass fixes. That omission is
/// deliberate: a <see cref="BnRow"/> is a row, so there is no parameter that
/// could contradict it. Use <see cref="BnView"/> directly when the direction has
/// to be decided at runtime. You do not derive from this yourself — use the two
/// presets.
/// </remarks>
public abstract class BnFlexPreset : BnLayoutContainer
{
    /// <summary>The direction the concrete preset nails down. Deliberately not
    /// a parameter — that is what makes the preset a preset.</summary>
    protected abstract FlexDirection PresetDirection { get; }

    /// <inheritdoc cref="BnView.ChildContent"/>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenComponent<BnView>(0);

        // Sequence 1-17 and 50-54: the shared surfaces, forwarded by the same
        // two helpers BnView itself emits from. Nulls included — see the file
        // header for why that is load-bearing rather than wasteful.
        ForwardItemParameters(b);
        ForwardContainerParameters(b);

        // Sequence 100+: this component's own contribution, clear of both base
        // bands. The preset's whole reason to exist is this one parameter.
        b.AddComponentParameter(100, nameof(BnView.Direction), PresetDirection);

        b.AddComponentParameter(200, nameof(BnView.ChildContent), ChildContent);

        b.CloseComponent();
    }
}
