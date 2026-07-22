namespace BlazorNative.Core;

// ─────────────────────────────────────────────────────────────────────────────
// INavigationManager — Phase 3.5 (M3 DoD #7)
// The navigation contract, in Core beside IMobileBridge: components depend on
// THIS; each runtime provides the implementation (NativeNavigationManager in
// BlazorNative.Runtime does the root swap + host notification on Android).
// This header once read "M6 lifts it into a dedicated BlazorNative.Navigation
// package." It did not: M6 closed without the lift and the lift is now issue #23,
// explicitly out of M11's scope (docs/planning/MILESTONE.md). The contract's home
// is BlazorNative.Core and stays there for the foreseeable future — see the
// <remarks> on the interface and docs/plans/2026-07-21-phase-11.3-api-tiers.md.
//
// Phase 5.1 (M5 DoD #5) adds NavigateBackAsync — host-INITIATED back (the
// predictive-back gesture routes here through blazornative_host_event). Deep
// links still feed the existing startup-route channel (QueryStartupRoute).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The navigation contract. Resolve it from DI and call it; the runtime
/// supplies the implementation (<c>NativeNavigationManager</c> in
/// <c>BlazorNative.Runtime</c>).</summary>
/// <remarks>
/// <para><b>API-additions policy (consume-only contract).</b> Same policy as
/// <see cref="IMobileBridge"/>: this interface is a contract you <i>call</i>.
/// Adding a member is declared <b>non-breaking</b>, because every supported
/// consumer is a caller. <b>Implementing it outside BlazorNative is
/// unsupported</b> — an addition will break an external implementer at compile
/// time, in a minor version. Default (<c>virtual</c>) interface members were
/// considered and rejected: the contracts package holds no bodies.</para>
/// <para><b>Location.</b> This type lives in <c>BlazorNative.Core</c>. Moving it to
/// a dedicated <c>BlazorNative.Navigation</c> package is tracked as issue #23 and
/// is <i>not</i> planned for 1.0; if it ever happens, it ships with a
/// <c>[TypeForwardedTo]</c> from <c>BlazorNative.Core</c>, which makes the move
/// non-breaking for both source and binary consumers.</para>
/// <para>See <c>docs/plans/2026-07-21-phase-11.3-api-tiers.md</c> (tier: STABLE).</para>
/// </remarks>
public interface INavigationManager
{
    /// <summary>The current route. Initialized at session start from the
    /// host's restored route when it maps to a known route; <c>"/"</c>
    /// otherwise.</summary>
    string CurrentRoute { get; }

    /// <summary>Navigates to a registered route: notifies the host, swaps the
    /// root component (the old page's views are removed, the new page mounts
    /// fresh), then raises <see cref="RouteChanged"/>. Completes synchronously
    /// on the inline-dispatcher runtimes. Throws <see cref="ArgumentException"/>
    /// for an unknown route (surfaced per the strict/error conventions).</summary>
    ValueTask NavigateToAsync(string route);

    /// <summary>Host-initiated back (Phase 5.1, M5 DoD #5): swaps to the
    /// previous route recorded by the last <see cref="NavigateToAsync"/> and
    /// returns <c>true</c> (handled). At the origin — no previous route (a fresh
    /// session, or the slot was consumed by an immediately preceding back) —
    /// returns <c>false</c> so the shell falls through to its default back
    /// behavior (Android finishes the Activity). A single previous-route slot:
    /// a back consumes it, so a second consecutive back returns <c>false</c>; a
    /// fresh forward navigation re-arms it.</summary>
    ValueTask<bool> NavigateBackAsync();

    /// <summary>Raised after a completed navigation with the new route. NOT
    /// raised for the initial mount (mounting is not a navigation).</summary>
    event Action<string> RouteChanged;
}
