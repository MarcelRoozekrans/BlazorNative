using BlazorNative.Core;

namespace BlazorNative.Runtime;

// ─────────────────────────────────────────────────────────────────────────────
// NativeNavigationManager — Phase 3.5 (design §1, M3 DoD #7)
//
// The INavigationManager implementation for the native shell. NavigateToAsync
// runs the whole swap SYNCHRONOUSLY (inline dispatcher / sync bridge
// contract), in this order — the order the Gate 1-3 tests pin:
//   1. resolve the route (unknown → ArgumentException, strict conventions);
//   2. notify the host via the 3.1 Navigate bridge callback (the host updates
//      its @Volatile route + logs);
//   3. swap the root: HostSession.SwapRoot → NativeRenderer.Unmount (Blazor's
//      RemoveRootComponent — the 3.3 disposal machinery emits the RemoveNode
//      patches that clear the screen) → mount the new page fresh;
//   4. raise RouteChanged.
// Navigating from inside a click handler therefore delivers removes+creates
// before blazornative_dispatch_event returns (the dispatch-window pin).
//
// Route table: values are HostSession mount-registry keys — the registry
// stays the single component-name authority; this table only owns
// route → name.
//
// CurrentRoute: tracked .NET-side; lazily initialized by querying the host's
// CurrentRoute buffer callback — a host-restored route that maps to a known
// route starts the session there (HostSession's route-aware initial mount
// consumes this), anything else falls back to "/". Host-INITIATED navigation
// (back button, deep links) is explicitly M5.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class NativeNavigationManager : INavigationManager
{
    internal const string DefaultRoute = "/";

    /// <summary>route → mount-registry key. Static data, no per-session state.</summary>
    private static readonly Dictionary<string, string> s_routes = new(StringComparer.Ordinal)
    {
        [DefaultRoute] = "BnDemo",
        ["/settings"] = "BnSettingsPage",
    };

    /// <summary>The default route's component — the name a host mounts to get
    /// "the routed app" (MainActivity's no-extra default). HostSession's
    /// route-aware initial mount only ever overrides THIS name.</summary>
    internal static string DefaultComponent => s_routes[DefaultRoute];

    /// <summary>Reverse lookup for HostSession's mount tracking: true when
    /// <paramref name="component"/> is a routed page, with its route.</summary>
    internal static bool TryGetRouteForComponent(string component, out string route)
    {
        foreach ((string r, string name) in s_routes)
        {
            if (name == component)
            {
                route = r;
                return true;
            }
        }
        route = "";
        return false;
    }

    private readonly IMobileBridge _bridge;
    private string? _currentRoute; // null until first read — see QueryStartupRoute

    public NativeNavigationManager(IMobileBridge bridge) => _bridge = bridge;

    public event Action<string>? RouteChanged;

    public string CurrentRoute => _currentRoute ??= QueryStartupRoute();

    public ValueTask NavigateToAsync(string route)
    {
        if (!s_routes.TryGetValue(route, out string? component))
        {
            throw new ArgumentException(
                $"unknown route '{route}' — known routes: {string.Join(", ", s_routes.Keys)}",
                nameof(route));
        }

        // 1. Host notify FIRST (design order — the host's @Volatile route is
        //    current before any frame lands). Sync contract: the ValueTask
        //    completed inside the call; GetResult surfaces a host error rc.
        _bridge.NavigateAsync(route).GetAwaiter().GetResult();

        // 2. The swap: old root's RemoveNode disposal frame, then the new
        //    page's mount frame. Failures THROW — inside a click dispatch the
        //    3.2 capture window maps them to export rc 2.
        HostSession.SwapRoot(component);

        _currentRoute = route;
        RouteChanged?.Invoke(route);
        return ValueTask.CompletedTask;
    }

    /// <summary>Component for a KNOWN route (callers pass table keys:
    /// CurrentRoute is clamped to the table by QueryStartupRoute).</summary>
    internal string ResolveComponent(string route) => s_routes[route];

    /// <summary>HostSession calls this after mounting a ROUTED component so
    /// CurrentRoute agrees with the screen even for direct named mounts
    /// (e.g. an explicit "BnSettingsPage" Intent extra). Deliberately does
    /// NOT raise RouteChanged — mounting is not a navigation.</summary>
    internal void NotifyMounted(string route) => _currentRoute = route;

    /// <summary>The startup query (design §1): ask the host's CurrentRoute
    /// buffer callback once; a known route wins, anything else → "/".
    /// A missing bridge registration (host-CLR tests, pre-register mounts) or
    /// a host-side error falls back to "/" — no route to restore is a normal
    /// condition, not a strict violation (the bridge op itself logged/threw
    /// with detail where it matters).</summary>
    private string QueryStartupRoute()
    {
        try
        {
            string route = _bridge.GetCurrentRouteAsync().GetAwaiter().GetResult();
            return s_routes.ContainsKey(route) ? route : DefaultRoute;
        }
        catch (InvalidOperationException)
        {
            return DefaultRoute;
        }
    }
}
