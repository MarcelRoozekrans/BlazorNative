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
        // Phase 6.1: the flexbox proof page. Its "← Back" navigates to "/", so
        // it rides the same nav path the other pages do. Nothing on BnDemo links
        // HERE — a "Layout →" button would churn BnDemo's four goldens for no
        // engine reason; the shells reach it by mount name (Intent extra) or by
        // deep link. The shells' own route mirrors (MainActivity's
        // DEEP_LINK_COMPONENTS) gain "/layout" in Gate 2.
        ["/layout"] = "BnLayoutDemo",
        // Phase 6.2: the scrolling proof page — same shape as "/layout". Its
        // "← Back" (outside the viewport, so scrolling cannot hide the exit)
        // navigates to "/"; nothing on BnDemo links here. The shells' route
        // mirrors (MainActivity's DEEP_LINK_COMPONENTS) gain "/scroll" in Gate 2;
        // iOS has no route mirror — it mounts by NAME (BnRuntime.start).
        ["/scroll"] = "BnScrollDemo",
        // Phase 6.3: the image proof page — same shape as "/layout" and "/scroll".
        // Its "← Back" navigates to "/"; nothing on BnDemo links here. THE SHELL
        // MIRRORS THIS TABLE OWES: Android's MainActivity.DEEP_LINK_COMPONENTS
        // gains "/image" → "BnImageDemo" in Gate 2 (a map that must not drift from
        // this one); iOS has no route mirror at all — it mounts by NAME
        // (BnRuntime.start), so Gate 3 touches no registry, only its test's mount
        // name.
        ["/image"] = "BnImageDemo",
        // Phase 7.2: the virtualized-list proof page — same shape as the three
        // above. Its "← Back" (outside the viewport) navigates to "/"; nothing
        // on BnDemo links here. Android's MainActivity.DEEP_LINK_COMPONENTS
        // gains "/list" → "BnListDemo" in Gate 2 (this table's mirror); iOS has
        // no route mirror — it mounts by NAME (BnRuntime.start).
        ["/list"] = "BnListDemo",
        // Phase 7.3: the form-controls proof page — same shape as the four
        // above. Its "← Back" navigates to "/"; nothing on BnDemo links here.
        // Android's MainActivity.DEEP_LINK_COMPONENTS gains "/form" →
        // "BnFormDemo" in Gate 2 (this table's mirror); iOS has no route
        // mirror — it mounts by NAME (BnRuntime.start).
        ["/form"] = "BnFormDemo",
        // Phase 7.4: the overlay proof page — same shape as the five above.
        // Its "← Back" navigates to "/"; nothing on BnDemo links here.
        // Android's MainActivity.DEEP_LINK_COMPONENTS gains "/modal" →
        // "BnModalDemo" in Gate 2 (this table's mirror); iOS has no route
        // mirror — it mounts by NAME (BnRuntime.start).
        ["/modal"] = "BnModalDemo",
    };

    /// <summary>The default route's component — the name a host mounts to get
    /// "the routed app" (MainActivity's no-extra default). HostSession's
    /// route-aware initial mount only ever overrides THIS name.</summary>
    internal static string DefaultComponent => s_routes[DefaultRoute];

    /// <summary>Test-only: the whole route table. There are TWO hand-maintained
    /// registries — this one and <c>HostSession</c>'s mount registry — and until
    /// Phase 6.3 nothing asserted that every route's VALUE is a name the mount
    /// registry actually knows. A route whose component is missing throws only when
    /// a user navigates to it (<c>SwapRoot</c>: "not in the mount registry"), which
    /// is a runtime crash on a device for a typo a set-equality test catches at
    /// build time. Five demo pages in, that is worth one line.</summary>
    internal static IReadOnlyDictionary<string, string> RoutesForTests => s_routes;

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
        if (!s_routes.TryGetValue(route, out string? component))
        {
            throw new ArgumentException(
                $"unknown route '{route}' — known routes: {string.Join(", ", s_routes.Keys)}",
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
