using System.ComponentModel;
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
// Route table: a DERIVED VIEW of PageManifest's routed rows (Phase 7.6,
// design decision 1) — the manifest is the single page authority; this table
// and HostSession's mount registry are projections of the same object graph,
// so a route's value is a mount-registry key BY CONSTRUCTION. Android's
// deep-link map (res/raw/blazornative_routes.json, read at Intent-parse time —
// before the .so loads) is, since Phase 11.0, GENERATED from these rows at build
// time by BlazorNative.RouteGen rather than hand-written; RouteTableDriftTests
// guards the generator's output pair-for-pair in the required build-test lane.
//
// CurrentRoute: tracked .NET-side; lazily initialized by querying the host's
// CurrentRoute buffer callback — a host-restored route that maps to a known
// route starts the session there (HostSession's route-aware initial mount
// consumes this), anything else falls back to "/". Host-INITIATED navigation
// (back button, deep links) is explicitly M5.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The on-device <see cref="INavigationManager"/> implementation, driving the root swap
/// through the host session.</summary>
/// <remarks>Not part of the supported public API: public only so <c>internal static unsafe class
/// HostSession</c> (<c>HostSession.cs:31</c>) can compose it across the Runtime→Core boundary. A
/// consumer injects <see cref="INavigationManager"/> (tier STABLE) and never names this class.
/// Tier NOT-API.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class NativeNavigationManager : INavigationManager
{
    internal const string DefaultRoute = PageManifest.DefaultRoute;

    /// <summary>route → mount-registry key. Static data, no per-session state.
    /// Phase 7.6: derived from <see cref="PageManifest.Pages"/>' routed rows —
    /// a value here is a HostSession mount-registry key by construction (both
    /// are views of the one array). Phase 8.0: LAZY-AFTER-FREEZE instead of
    /// static-readonly — ILC may pre-initialize static ctors at compile time,
    /// and a projection snapshotted before the app's module initializer
    /// registered anything would be silently EMPTY (PageManifest.cs's header).
    /// First materialization is the freeze point.</summary>
    private static Dictionary<string, string>? s_routes;
    private static readonly object s_routesLock = new();

    private static Dictionary<string, string> Routes
    {
        get
        {
            Dictionary<string, string>? view = Volatile.Read(ref s_routes);
            if (view is not null)
                return view;

            lock (s_routesLock)
            {
                if (s_routes is null)
                {
                    Volatile.Write(ref s_routes, PageManifest.Pages
                        .Where(p => p.Route is not null)
                        .ToDictionary(p => p.Route!, p => p.Name, StringComparer.Ordinal));
                }
                return s_routes!;
            }
        }
    }

    /// <summary>Test-only (BlazorNativeApp.ResetRegistrationForTests): drops
    /// the materialized view so a re-registered manifest projects fresh.</summary>
    internal static void ResetRoutesViewForTests()
    {
        lock (s_routesLock)
        {
            Volatile.Write(ref s_routes, null);
        }
    }

    /// <summary>The default route's component — the name a host mounts to get
    /// "the routed app" (MainActivity's no-extra default). HostSession's
    /// route-aware initial mount only ever overrides THIS name. Phase 7.6:
    /// forwards to the manifest, where the row itself lives (null when the
    /// registered manifest has no routed rows — Phase 8.0).</summary>
    internal static string? DefaultComponent => PageManifest.DefaultComponent;

    /// <summary>Test-only: the whole route table. Born in Phase 6.3, when this
    /// table and <c>HostSession</c>'s mount registry were two hand-maintained
    /// mirrors and a set test had to assert routes ⊆ registry; since Phase 7.6
    /// both derive from <see cref="PageManifest.Pages"/> (that test retired as
    /// a by-construction tautology) and this surface remains for the tests
    /// that drive navigation by route.</summary>
    internal static IReadOnlyDictionary<string, string> RoutesForTests => Routes;

    /// <summary>Reverse lookup for HostSession's mount tracking: true when
    /// <paramref name="component"/> is a routed page, with its route.</summary>
    internal static bool TryGetRouteForComponent(string component, out string route)
    {
        foreach ((string r, string name) in Routes)
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

    /// <summary>The previous-route slot (Phase 5.1, design §2): the route the
    /// LAST successful <see cref="NavigateToAsync"/> left, or null at the origin.
    /// <see cref="NavigateBackAsync"/> swaps to it and consumes it (null), so a
    /// back is not itself a re-armable forward step — a second consecutive back
    /// finds no prior and returns false. A single slot; a stack is later work.</summary>
    private string? _previousRoute;

    public NativeNavigationManager(IMobileBridge bridge) => _bridge = bridge;

    public event Action<string>? RouteChanged;

    public string CurrentRoute => _currentRoute ??= QueryStartupRoute();

    public ValueTask NavigateToAsync(string route)
    {
        if (!Routes.TryGetValue(route, out string? component))
        {
            throw new ArgumentException(
                $"unknown route '{route}' — known routes: {string.Join(", ", Routes.Keys)}",
                nameof(route));
        }

        // Capture the `from` BEFORE the swap (design §2): CurrentRoute resolves
        // the lazy startup query if it hasn't run, so the slot is never seeded
        // from a null field. Recorded inside afterSwap so a FAILED swap leaves
        // the slot (and CurrentRoute) untouched — the previous-route trail
        // tracks the SCREEN, exactly like _currentRoute/RouteChanged do.
        string from = CurrentRoute;

        // 1. Host notify FIRST (design order — the host's @Volatile route is
        //    current before any frame lands). Sync contract: the ValueTask
        //    completed inside the call; GetResult surfaces a host error rc.
        _bridge.NavigateAsync(route).GetAwaiter().GetResult();

        // 2. The swap: old root's RemoveNode disposal frame, then the new
        //    page's mount frame. Failures THROW — inside a click dispatch the
        //    3.2 capture window maps them to export rc 2. Route state + the
        //    RouteChanged event ride the swap unit (afterSwap) so they track
        //    the SCREEN, not the intent: a mid-dispatch navigation defers the
        //    swap to the dispatch unwind, and a failed swap must not leave
        //    CurrentRoute pointing at a page that never mounted.
        //    RouteChanged subscribers are ISOLATED (Phase 4.2, DoD #4) — see
        //    RaiseRouteChanged.
        HostSession.SwapRoot(component, afterSwap: () =>
        {
            _previousRoute = from;
            _currentRoute = route;
            RaiseRouteChanged(route);
        });
        return ValueTask.CompletedTask;
    }

    /// <summary>Host-initiated back (Phase 5.1, design §2): swaps to the
    /// <see cref="_previousRoute"/> slot and returns true; at the origin (no
    /// prior) returns false so the Android shell finishes. The slot is CONSUMED
    /// by the back — cleared AFTER the swap so a second consecutive back has no
    /// prior (returns false) rather than ping-ponging forever between two pages.
    /// The nested <see cref="NavigateToAsync"/> re-records the slot (with the
    /// page we're leaving) as part of its normal contract; the clear then wipes
    /// that so back leaves no re-back trail — a fresh FORWARD navigation is what
    /// re-arms it. Runs off the dispatch lane (host-initiated): the swap's
    /// RunAfterDispatch drains immediately (no open batch — the pinned
    /// no-open-batch path). On a failed swap the exception propagates and the
    /// slot survives (the clear is skipped), so back can be retried.</summary>
    public async ValueTask<bool> NavigateBackAsync()
    {
        if (_previousRoute is not { } target)
            return false; // at the origin — the shell falls through to finish

        await NavigateToAsync(target);
        _previousRoute = null; // a back consumes the slot — no auto re-back
        return true;
    }

    /// <summary>Raises <see cref="RouteChanged"/> with per-subscriber
    /// isolation (Phase 4.2, DoD #4 — the DevHostBridge.RaiseNativeEvent
    /// pattern): a throwing subscriber is stderr-logged and the remaining
    /// subscribers still run. The fault is CONTAINED in strict mode too —
    /// deliberate posture: the navigation already succeeded (the screen
    /// swapped, CurrentRoute is consistent) when this event fires, so a
    /// listener's bug must not convert success into export rc 2.
    /// StrictErrors surfaces RENDERER contract violations, not app-listener
    /// bugs — the same line the dispatch capture window draws by treating
    /// handler exceptions as rc 2 only when they fault the dispatch
    /// itself.</summary>
    private void RaiseRouteChanged(string route)
    {
        if (RouteChanged is not { } subscribers)
            return;
        foreach (Delegate subscriber in subscribers.GetInvocationList())
        {
            try
            {
                ((Action<string>)subscriber)(route);
            }
            catch (Exception ex)
            {
                // ex.ToString(): the subscriber is app code — keep its stack.
                Console.Error.WriteLine(
                    $"[NativeNavigationManager] RouteChanged subscriber threw: {ex}");
            }
        }
    }

    /// <summary>Component for a KNOWN route (callers pass table keys:
    /// CurrentRoute is clamped to the table by QueryStartupRoute).</summary>
    internal string ResolveComponent(string route) => Routes[route];

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
            return Routes.ContainsKey(route) ? route : DefaultRoute;
        }
        catch (InvalidOperationException)
        {
            return DefaultRoute;
        }
    }
}
