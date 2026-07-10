namespace BlazorNative.Core;

// ─────────────────────────────────────────────────────────────────────────────
// INavigationManager — Phase 3.5 (M3 DoD #7)
// The navigation contract, in Core beside IMobileBridge: components depend on
// THIS; each runtime provides the implementation (NativeNavigationManager in
// BlazorNative.Runtime does the root swap + host notification on Android).
// M6 lifts it into a dedicated BlazorNative.Navigation package.
//
// Host-INITIATED navigation (back button, deep links) is explicitly M5 —
// this contract only covers app-initiated route changes.
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

    /// <summary>Raised after a completed navigation with the new route. NOT
    /// raised for the initial mount (mounting is not a navigation).</summary>
    event Action<string> RouteChanged;
}
