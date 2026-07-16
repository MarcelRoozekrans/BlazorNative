using BlazorNative.Renderer;

namespace BlazorNative.Runtime;

// ─────────────────────────────────────────────────────────────────────────────
// PageManifest — Phase 7.6 (design decision 1, M7 DoD #8: route-registry
// unification).
//
// THE NORMATIVE RULE: a page is declared ONCE — one row in this manifest.
// Every other surface is either a DERIVED VIEW (same assembly, same object
// graph — cannot drift by construction) or a PINNED MIRROR (a hand-written
// copy a required-lane drift test compares pair-for-pair). Today:
//
//   - `HostSession.s_components` (the mount registry, name → Mount) is DERIVED
//     from ALL rows;
//   - `NativeNavigationManager.s_routes` (route → name) is DERIVED from the
//     routed rows (Route != null);
//   - Android's `MainActivity.DEEP_LINK_COMPONENTS` is the one surviving
//     PINNED MIRROR — it must be a copy, structurally: the shell resolves the
//     deep-link component at Intent-parse time, BEFORE the .so is loaded, so
//     there is no runtime to ask (the 5.1 record). `RouteTableDriftTests`
//     pins it pair-for-pair in build-test, the one required lane where every
//     file is checkout-visible (ShellStyleTableDriftTests' own rationale);
//   - iOS has NO route surface at all — `BnRuntime.start(component:)` mounts
//     by NAME (verified 7.5 Gate 3). The day it grows one, it becomes a
//     mirror AND joins the pin — a decision, not an accident.
//
// TRIM LAW (why every row's lambda looks the way it does): mount-by-name
// keeps reflection out of the C ABI — each `r => r.Mount<T>()` is a
// statically-rooted generic instantiation, so NativeAOT trims nothing it
// needs. The lambdas moved here VERBATIM from HostSession (Phase 7.6);
// nothing goes reflective, and Gate 1's publish asserts the 4 accepted
// IL2072s and 9 exports as always.
//
// This is deliberately its OWN class: neither HostSession nor
// NativeNavigationManager feeds the other's static initializer — both read
// one static array, a fan-out, not a cycle.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One page declaration: its route (null for the unrouted test
/// probes, which shells mount by NAME only), its mount-registry name, and its
/// statically-rooted mount thunk (the trim-law shape — see the file header).</summary>
internal readonly record struct Page(string? Route, string Name, Func<NativeRenderer, int> Mount);

internal static class PageManifest
{
    internal const string DefaultRoute = "/";

    /// <summary>THE manifest: 9 routed pages + 5 unrouted test probes.
    /// Sync Mount&lt;T&gt; (inline dispatcher, Phase 2.4) — the first render
    /// completes before TryMount returns, so the frame callback has already
    /// fired. Mount values return the root componentId (Phase 3.5: the
    /// navigation swap needs it — see HostSession.s_currentRootComponentId).</summary>
    internal static readonly Page[] Pages =
    [
        new(Route: null, "HelloComponent", r => r.Mount<HelloComponent>()),
        // Phase 3.3: the composition proof app (nested components, keyed
        // list mutation, detach — design §6).
        new(Route: null, "CompositionProbe", r => r.Mount<CompositionProbe>()),
        // Phase 3.4: the Bn* demo form (bind loop + cascading theme —
        // DoD #5/#6); MainActivity's default since Gate 4. Phase 3.5 made it
        // the DEFAULT ROUTE's page (see DefaultComponent below).
        new(DefaultRoute, "BnDemo", r => r.Mount<BlazorNative.Components.BnDemo>()),
        // Phase 3.5: the demo's second page (route "/settings" — DoD #7).
        new("/settings", "BnSettingsPage", r => r.Mount<BlazorNative.Components.BnSettingsPage>()),
        // Phase 6.1: the flexbox proof page (route "/layout" — M6 DoD #2/#3).
        // A THIRD page on purpose: BnDemo/BnSettingsPage keep their goldens, so
        // layout bugs never arrive mixed with golden-rewrite noise. Its computed
        // frames are asserted identically on the AVD and the iOS simulator —
        // see BnLayoutDemo.razor's frame table. Its "← Back" navigates to "/";
        // nothing on BnDemo links here (a "Layout →" button would churn
        // BnDemo's goldens for no engine reason) — the shells reach it by mount
        // name (Intent extra) or by deep link.
        new("/layout", "BnLayoutDemo", r => r.Mount<BlazorNative.Components.BnLayoutDemo>()),
        // Phase 6.2: the scrolling proof page (route "/scroll" — M6 DoD #4). A
        // FOURTH page, same rationale: BnLayoutDemo's 22-number frame table IS
        // the parity contract, and wrapping it in a scroll view would rewrite it
        // in the same phase that introduces the scroll engine. Its content size
        // (800) and row frames are asserted identically on both devices — see
        // BnScrollDemo.razor's frame table.
        new("/scroll", "BnScrollDemo", r => r.Mount<BlazorNative.Components.BnScrollDemo>()),
        // Phase 6.3: the image proof page (route "/image" — M6 DoD #5). A FIFTH
        // page, same rationale a fourth time: the existing frame tables ARE the
        // parity contract, and a new capability does not get to rewrite them.
        // Its THREE measurement paths — fixed (never measured), intrinsic
        // (0×0 → the natural size, and the sibling below it MOVES) and failure
        // (0×0 forever, reserving nothing) — are asserted identically on the
        // AVD (Coil) and the iOS simulator (Kingfisher). See BnImageDemo.razor's
        // TWO frame tables: before the bytes land, and after.
        new("/image", "BnImageDemo", r => r.Mount<BlazorNative.Components.BnImageDemo>()),
        // Phase 7.2: the virtualized-list proof page (route "/list" — M7
        // DoD #3). A SIXTH page, same rationale a fifth time: BnScrollDemo's
        // frame table IS the 6.2 parity contract and the phase that introduces
        // virtualization does not get to rewrite it. Its liveness counts
        // (2 spacers + 11/15/11 window rows), spacer heights and content size
        // (32,000) are asserted identically on both devices — see
        // BnListDemo.razor's header.
        new("/list", "BnListDemo", r => r.Mount<BlazorNative.Components.BnListDemo>()),
        // Phase 7.3: the form-controls proof page (route "/form" — M7 DoD #4).
        // A SEVENTH page, same rationale a sixth time. Each control appears
        // TWICE (bound + disabled) with a live echo; the bind round-trips, the
        // loop guards, the picker clamp rule and the declared widths are
        // asserted identically on both devices — see BnFormDemo.razor.
        new("/form", "BnFormDemo", r => r.Mount<BlazorNative.Components.BnFormDemo>()),
        // Phase 7.4: the overlay proof page (route "/modal" — M7 DoD #5).
        // An EIGHTH page, same rationale a seventh time. The modal sits BETWEEN
        // two declared-size siblings (the anchor's zero-footprint rule as a
        // frame assertion), the switch + echo prove the wire INSIDE the
        // overlay, and the indicator appears in both hosting contexts — see
        // BnModalDemo.razor's header.
        new("/modal", "BnModalDemo", r => r.Mount<BlazorNative.Components.BnModalDemo>()),
        // Phase 7.5: the image-polish proof page (route "/imagepolish" — M7
        // DoD #6). A NINTH page, same rationale an eighth time — and here at
        // its sharpest: BnImageDemo's two frame tables ARE the 6.3 parity
        // contract and its section arithmetic is golden-pinned, so the phase
        // that adds placeholder/error/mode does NOT extend "/image". The new
        // page re-runs 6.3's measurement proofs WITH the new features present —
        // placeholder never measures (both sides), failure keeps a declared
        // box, the mode quartet's four identical frames — plus the counted
        // OnError round trip. See BnImagePolishDemo.razor's header.
        new("/imagepolish", "BnImagePolishDemo", r => r.Mount<BlazorNative.Components.BnImagePolishDemo>()),
        // Phase 4.2: the focus/blur proof app (BnInput OnFocus/OnBlur →
        // echo BnText — M4 DoD #4). Scaffolding, like CompositionProbe.
        new(Route: null, "FocusProbe", r => r.Mount<FocusProbe>()),
        // Phase 5.1: the host-event proof app (IMobileBridge.NativeEvents →
        // echo BnText — M5 DoD #5). Scaffolding, like FocusProbe.
        new(Route: null, "HostEventProbe", r => r.Mount<HostEventProbe>()),
        // Phase 5.4: the clipboard/share proof app (IMobileBridge clipboard
        // read/write + share → echo BnText — M5 DoD #6). Scaffolding, like
        // HostEventProbe.
        new(Route: null, "ClipboardProbe", r => r.Mount<ClipboardProbe>()),
    ];

    /// <summary>The default route's component — the name a host mounts to get
    /// "the routed app" (MainActivity's no-extra default; the Kotlin
    /// <c>?: "BnDemo"</c> fallback literal is pinned to THIS by
    /// RouteTableDriftTests). HostSession's route-aware initial mount only
    /// ever overrides this name.</summary>
    internal static readonly string DefaultComponent =
        Pages.Single(p => p.Route == DefaultRoute).Name;
}
