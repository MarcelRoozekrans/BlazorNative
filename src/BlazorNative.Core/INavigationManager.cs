namespace BlazorNative.Core;

// ─────────────────────────────────────────────────────────────────────────────
// INavigationManager — Phase 3.5 (M3 DoD #7)
// The navigation contract, in Core beside IMobileBridge: components depend on
// THIS; each runtime provides the implementation (NativeNavigationManager in
// BlazorNative.Runtime does the root swap + host notification on Android).
// M6 lifts it into a dedicated BlazorNative.Navigation package.
//
// Phase 5.1 (M5 DoD #5) adds NavigateBackAsync — host-INITIATED back (the
// predictive-back gesture routes here through blazornative_host_event). Deep
// links still feed the existing startup-route channel (QueryStartupRoute).
// ─────────────────────────────────────────────────────────────────────────────

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
