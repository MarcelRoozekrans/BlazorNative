using BlazorNative.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorNative.Components;

/// <summary>How one edge of a <see cref="BnSafeArea"/> reacts to the inset the host
/// shell reports for it.</summary>
public enum BnSafeAreaEdge
{
    /// <summary>The reported inset is ignored on this edge — the edge-to-edge case, e.g.
    /// a full-bleed hero image drawn under the status bar. The author's own padding for
    /// that edge, if any, still applies.</summary>
    Off,

    /// <summary>The reported inset is added to the author's own padding for that edge.
    /// The default.</summary>
    Additive,

    /// <summary>Whichever is larger: the reported inset, or the author's own padding for
    /// that edge — React Native's rule, Math.max(insets.bottom, 16) written once here
    /// instead of by every author.</summary>
    Maximum,
}

// ─────────────────────────────────────────────────────────────────────────────
// BnSafeArea — Phase 14.2 Task 5 (design "The component"; decisions 3-4).
//
// Keeps content clear of notches, status bars, camera cutouts and home
// indicators by padding the reported BnSafeAreaInsets.Current into a BnView.
// Opt-in, like every other component (decision 1) — the app author wraps
// what should be inset.
//
// ── WHY THIS DERIVES FROM BnLayoutContainer, NOT BnLayoutItem ────────────────
// The design's own prose (docs/superpowers/specs/2026-09-21-phase-14.2-design.md)
// says "BnSafeArea : BnLayoutItem", written before Phase 14.2 Task 1 added
// PaddingTop/Right/Bottom/Left to BnLayoutContainer. Those four parameters ARE
// "the author's own padding" this component compares against in Maximum mode,
// and ForwardContainerParameters/Padding/Justify/Align/Wrap/Gap are the
// container surface a safe-area box legitimately wants too (an author may
// still want to justify/align its children). BnLayoutItem alone cannot express
// either. This is the SAME base BnFlexPreset and BnView already use.
//
// ── WHY THE EDGE-MODE PARAMETERS ARE NAMED TopEdge/RightEdge/BottomEdge/
//    LeftEdge, NOT Top/Right/Bottom/Left ───────────────────────────────────
// BnLayoutItem already declares public BnLength? Top/Right/Bottom/Left — the
// CSS-style POSITION insets (sequence 14-17). A `new`-shadowed [Parameter] of
// a different type on a derived type is not expressible in Blazor: parameter
// binding walks the whole hierarchy and throws InvalidOperationException,
// "declares more than one parameter matching the name", on first render — the
// exact failure LayoutSurfacePinTests.BnList_CannotTakeTheItemBase_… already
// proves for BnList's Height/float vs BnLayoutItem's Height/BnAutoLength?
// collision. NoComponent_RedeclaresAnInheritedLayoutParameter (the same file)
// would also red the instant a BnLayoutItem descendant re-declared any of the
// 26 shared names. Renaming — not shadowing — is this codebase's own settled
// answer to a same-name, different-type collision (see BnList's doc comment:
// "this is not a migration that was skipped — it is one that does not exist
// until either the narrowing goes away or the name does"). The rename also
// leaves the genuine position Top/Right/Bottom/Left available on BnSafeArea,
// for absolute-positioning the safe-area box itself.
//
// ── NESTING: A CASCADING VALUE, NOT A STATIC ─────────────────────────────────
// Flutter's rule: SafeArea rewrites the inset data its children see, so only
// the outermost instance pads. This component publishes "insets already
// consumed" as a named CascadingValue&lt;bool&gt; around its OWN ChildContent —
// scoped to that subtree — rather than a static flag. A static would wrongly
// suppress a SECOND, unrelated BnSafeArea in a different branch of the tree
// (e.g. two independent screens each wrapped at their own root): both are
// "outermost" in their own branch, and only a value scoped to the render tree
// (not to the process) gets that right.
//
// ── Maximum's DIRECTION ───────────────────────────────────────────────────
// "My padding, or the inset, whichever is larger" — not "the inset overrides
// my padding" and not "my padding overrides the inset". Both directions are
// tested (MaximumMode_UsesTheAuthorsPadding…, MaximumMode_UsesTheInset…); a
// naive MathF.Max argument swap or a Math.Min typo passes one and fails the
// other, which is why both exist rather than one.
//
// ── Sequence numbers (M13's bands, F1's extension) ───────────────────────────
// 1-17 item (ForwardItemParameters, unmodified); 50-54 the non-edge container
// params (Padding/Justify/Align/Wrap/Gap, forwarded raw); 55-58 the four
// per-edge paddings — still the CONTAINER band, because
// LayoutSurfaceSequenceBandTests classifies an attribute by NAME
// (PaddingTop/Right/Bottom/Left are container names, so 50-99 is required),
// even though the VALUE at those four sequences is computed rather than
// forwarded verbatim. TopEdge/RightEdge/BottomEdge/LeftEdge are never put on
// the wire at all — BnView has no such parameters — so they need no sequence
// number here; they are pure inputs to Resolve() below. 200 is ChildContent.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Keeps its content clear of notches, status bars, camera cutouts and home
/// indicators, by padding a <see cref="BnView"/> with the safe-area insets the
/// host shell reports.
/// </summary>
/// <remarks>
/// <para>
/// <b>Opt-in.</b> Wrap the page content (or just the part of it) that needs to
/// stay clear of system UI: <c>&lt;BnSafeArea&gt;…&lt;/BnSafeArea&gt;</c>.
/// </para>
/// <para>
/// <b>Per edge, independently.</b> <see cref="TopEdge"/>, <see cref="RightEdge"/>,
/// <see cref="BottomEdge"/> and <see cref="LeftEdge"/> each take a
/// <see cref="BnSafeAreaEdge"/> — <c>Off</c> ignores the inset on that edge
/// (the edge-to-edge case, e.g. a full-bleed hero image under the status
/// bar), <c>Additive</c> (the default) adds the inset to whatever padding you
/// set for that edge, and <c>Maximum</c> takes whichever of the two is
/// larger.
/// </para>
/// <para>
/// <b>Nesting is safe.</b> Wrapping a page in <see cref="BnSafeArea"/> and
/// then wrapping a section inside it in another <see cref="BnSafeArea"/> does
/// not pad twice — only the outermost instance applies the inset; a nested
/// one sees it as already consumed.
/// </para>
/// <para>
/// <b>Live.</b> Rotation, a keyboard appearing, a call banner — the insets can
/// change while the app is running, and this component re-lays-out when they
/// do.
/// </para>
/// </remarks>
public sealed class BnSafeArea : BnLayoutContainer, IDisposable
{
    private const string InsetsConsumedCascadingName =
        "BlazorNative.Components.BnSafeArea.InsetsAlreadyConsumed";

    /// <summary>How the top edge reacts to the reported inset. Default <c>Additive</c>.</summary>
    [Parameter] public BnSafeAreaEdge TopEdge { get; set; } = BnSafeAreaEdge.Additive;

    /// <summary>How the right edge reacts to the reported inset. Default <c>Additive</c>.</summary>
    [Parameter] public BnSafeAreaEdge RightEdge { get; set; } = BnSafeAreaEdge.Additive;

    /// <summary>How the bottom edge reacts to the reported inset. Default <c>Additive</c>.</summary>
    [Parameter] public BnSafeAreaEdge BottomEdge { get; set; } = BnSafeAreaEdge.Additive;

    /// <summary>How the left edge reacts to the reported inset. Default <c>Additive</c>.</summary>
    [Parameter] public BnSafeAreaEdge LeftEdge { get; set; } = BnSafeAreaEdge.Additive;

    /// <inheritdoc cref="BnView.ChildContent"/>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>
    /// Whether an ANCESTOR <see cref="BnSafeArea"/> already consumed the insets for this
    /// subtree. Published by every instance's own <c>BuildRenderTree</c> as <c>true</c>
    /// around its <see cref="ChildContent"/> — see the file header for why a cascading
    /// value, scoped to that subtree, is used instead of a static flag.
    /// </summary>
    [CascadingParameter(Name = InsetsConsumedCascadingName)]
    private bool InsetsAlreadyConsumed { get; set; }

    /// <inheritdoc />
    protected override void OnInitialized() => BnSafeAreaInsets.Changed += OnInsetsChanged;

    private void OnInsetsChanged(BnSafeAreaInsets _) => InvokeAsync(StateHasChanged);

    /// <summary>Unsubscribes from <see cref="BnSafeAreaInsets.Changed"/> so an unmounted
    /// page does not keep re-rendering itself forever.</summary>
    void IDisposable.Dispose() => BnSafeAreaInsets.Changed -= OnInsetsChanged;

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        // An ancestor already consumed the insets for this subtree (decision 3 / the
        // nesting rule) — this instance pads nothing, on every edge, regardless of mode.
        BnSafeAreaInsets insets = InsetsAlreadyConsumed ? BnSafeAreaInsets.Zero : BnSafeAreaInsets.Current;

        b.OpenComponent<BnView>(0);

        // Sequence 1-17: the shared item surface, forwarded exactly like BnFlexPreset —
        // nulls included (ForwardItemParameters' own contract).
        ForwardItemParameters(b);

        // Sequence 50-54: the container surface EXCEPT the four per-edge paddings, which
        // this component computes itself below instead of forwarding raw.
        b.AddComponentParameter(50, nameof(BnView.Padding), Padding);
        b.AddComponentParameter(51, nameof(BnView.Justify), Justify);
        b.AddComponentParameter(52, nameof(BnView.Align),   Align);
        b.AddComponentParameter(53, nameof(BnView.Wrap),    Wrap);
        b.AddComponentParameter(54, nameof(BnView.Gap),     Gap);

        // Sequence 55-58: still the container band by NAME (LayoutSurfaceSequenceBandTests
        // classifies PaddingTop/Right/Bottom/Left as container regardless of who computed
        // the value) — this is the component's whole reason to exist.
        //
        // `?? Padding`: the per-edge parameter falls back to the shorthand `Padding` when
        // the author did not set that edge explicitly — the SAME "more specific edge wins"
        // rule BnLayoutContainer's own doc comments state for PaddingTop/Right/Bottom/Left
        // vs Padding. Without this fallback, `<BnSafeArea Padding="16">` silently rendered
        // ZERO padding on every edge: this method ALWAYS emits a concrete BnLength for all
        // four edges (never null, even when the resolved value is exactly 0), and Yoga
        // resolves an edge-specific value over YGEdgeAll — so the always-present per-edge 0
        // clobbered `Padding`'s YGEdgeAll=16 on every edge, every time, regardless of mode
        // (a whole-branch review finding, IMPORTANT 1; pinned by
        // BnSafeArea_PaddingShorthand_IsNotSilentlyDropped in BnSafeAreaTests.cs).
        b.AddComponentParameter(55, nameof(BnView.PaddingTop),    Resolve(TopEdge,    PaddingTop    ?? Padding, insets.Top));
        b.AddComponentParameter(56, nameof(BnView.PaddingRight),  Resolve(RightEdge,  PaddingRight  ?? Padding, insets.Right));
        b.AddComponentParameter(57, nameof(BnView.PaddingBottom), Resolve(BottomEdge, PaddingBottom ?? Padding, insets.Bottom));
        b.AddComponentParameter(58, nameof(BnView.PaddingLeft),   Resolve(LeftEdge,   PaddingLeft   ?? Padding, insets.Left));

        // Sequence 200: ChildContent, wrapped in the cascading value that tells any
        // nested BnSafeArea the insets were already consumed here.
        b.AddComponentParameter(200, nameof(BnView.ChildContent), (RenderFragment)(cb =>
        {
            cb.OpenComponent<CascadingValue<bool>>(0);
            cb.AddComponentParameter(1, nameof(CascadingValue<bool>.Name), InsetsConsumedCascadingName);
            cb.AddComponentParameter(2, nameof(CascadingValue<bool>.Value), true);
            cb.AddComponentParameter(3, nameof(CascadingValue<bool>.ChildContent), ChildContent);
            cb.CloseComponent();
        }));

        b.CloseComponent();
    }

    /// <summary>
    /// The combination rule for one edge. <c>Off</c> uses only the
    /// author's own padding for that edge (zero if unset); <c>Additive</c>
    /// (the default) sums it with the reported inset; <c>Maximum</c>
    /// takes whichever of the two is larger — React Native's rule.
    /// </summary>
    private static BnLength Resolve(BnSafeAreaEdge mode, BnLength? authorPadding, double inset)
    {
        float author = authorPadding?.Value ?? 0f;
        float reported = (float)inset;
        return mode switch
        {
            BnSafeAreaEdge.Off => author,
            BnSafeAreaEdge.Maximum => MathF.Max(author, reported),
            BnSafeAreaEdge.Additive => author + reported,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "unrecognised BnSafeAreaEdge"),
        };
    }
}
