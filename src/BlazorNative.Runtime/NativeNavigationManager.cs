using System.ComponentModel;
using BlazorNative.Core;

namespace BlazorNative.Runtime;

// M11 #173 — CS1591 off for this file. Every public type here is [EditorBrowsable(Never)]
// NOT-API: public only because the C ABI / AOT exports require it, documented by the
// C-ABI contract comments below rather than XML docs. BnEnforceDocCoverage makes CS1591
// an error package-wide (the STABLE surface — BlazorNativeApp, BlazorNativePage — is
// fully ///-documented); these interop types opt out here, and the reference generator
// (scripts/generate-reference.ps1) drops their pages via the same [EditorBrowsable(Never)]
// mark, so they are neither a build break nor a blank stub.
#pragma warning disable 1591

// ─────────────────────────────────────────────────────────────────────────────
// NativeNavigationManager — Phase 3.5 (design §1, M3 DoD #7)
//
// The INavigationManager implementation for the native shell. NavigateToAsync
// runs the whole swap SYNCHRONOUSLY (the renderer's entry points post to its
// render thread and wait; sync bridge contract), in this order — the order
// the Gate 1-3 tests pin:
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
        Navigate(route, isBack: false);
        return ValueTask.CompletedTask;
    }

    /// <summary>The navigation, forward or back. A forward step records the page it
    /// leaves in <see cref="_previousRoute"/>; a back CONSUMES the slot instead. Both
    /// happen inside afterSwap, so a failed swap leaves the slot untouched.</summary>
    private void Navigate(string route, bool isBack)
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
        //
        //    Phase 16.2 (#346, spec decision 2): the BackState notice goes out in
        //    beforeSwap, NOT in afterSwap. The swap's frames are emitted between
        //    the two, so a notice sent where the route state is recorded would
        //    reach the shell after the page it describes. It therefore carries the
        //    value this navigation is ABOUT to produce: a forward step can always
        //    go back, and a back consumes the only slot. A swap that throws resends
        //    the value the route state still holds.
        HostSession.SwapRoot(
            component,
            beforeSwap: () => PublishBackState(canGoBack: !isBack),
            afterSwap: () =>
            {
                _previousRoute = isBack ? null : from;
                _currentRoute = route;
                RaiseRouteChanged(route);
                // #201 developer trace (Debug, IsEnabled-guarded). Inside afterSwap, so it
                // fires only when the swap SUCCEEDED — it tracks the screen, like the route
                // state above. Route strings are app-authored, not user data.
                if (BnLog.IsEnabled(BnLogLevel.Debug))
                    BnLog.Debug("NativeNavigationManager", $"navigated '{from}' → '{route}' (component '{component}')");
            },
            swapFailed: () => PublishBackState(CanGoBack));
    }

    /// <summary>Host-initiated back (Phase 5.1, design §2): swaps to the
    /// <see cref="_previousRoute"/> slot and returns true; at the origin (no
    /// prior) returns false, and the shell hands the press to the platform's default
    /// back. The slot is CONSUMED by the back — cleared inside the swap unit, so a
    /// second consecutive back has no prior (returns false) rather than ping-ponging
    /// forever between two pages.
    /// A fresh FORWARD navigation is what re-arms it. Runs off the dispatch lane
    /// (host-initiated): the swap's RunAfterDispatch drains immediately (no open
    /// batch — the pinned no-open-batch path). On a failed swap the exception
    /// propagates and the slot survives, so back can be retried.
    /// Phase 16.2: the clear moved from after the swap into afterSwap. The two
    /// are the same off the dispatch lane; inside a click handler, where the
    /// swap is deferred, the old order cleared the slot BEFORE the deferred swap
    /// re-recorded it, so a back from a handler left a re-back trail.</summary>
    public ValueTask<bool> NavigateBackAsync()
    {
        if (_previousRoute is not { } target)
            return ValueTask.FromResult(false); // at the origin — the shell falls through to finish

        Navigate(target, isBack: true);
        return ValueTask.FromResult(true);
    }

    // ── The back state (Phase 16.2, #346) ────────────────────────────────────
    //
    // The shell no longer asks whether .NET can go back; this pushes the answer as a
    // BackState notice. _lastSentCanGoBack is what the shell was last told, null until
    // the session's first mount. It needs no reset of its own: this manager is born
    // with its session, and a new session builds a new one.

    private readonly object _backStateLock = new();
    private bool? _lastSentCanGoBack;

    /// <summary>Whether a back would be handled now: the previous-route slot is set.</summary>
    internal bool CanGoBack => _previousRoute is not null;

    /// <summary>Tells the shell <paramref name="canGoBack"/> when it differs from what
    /// the shell was last told. Always sends the first time, which is the session's first
    /// mount. Never throws: the send is fire-and-forget.
    ///
    /// The send happens INSIDE the lock, so the order the shell receives notices in is
    /// the order the recorded value changed in. Callers are not all on one thread:
    /// TryMount publishes from the mount export's thread, and navigation from the render
    /// thread. Holding the lock across the send cannot deadlock: under it runs only
    /// SendBackState, whose hostCallBegin is a begin that must return at once by the
    /// host-call contract, and a shell that answers inline re-enters .NET only through
    /// CompleteHostCall, which takes no lock of this manager and runs no continuation
    /// inline. The lock is private and taken nowhere else.</summary>
    internal void PublishBackState(bool canGoBack) => PublishBackState(canGoBack, force: false);

    private void PublishBackState(bool canGoBack, bool force)
    {
        lock (_backStateLock)
        {
            if (!force && _lastSentCanGoBack == canGoBack)
                return;
            _lastSentCanGoBack = canGoBack;
            NativeShellBridge.SendBackState(canGoBack);
        }
    }

    /// <summary>HostSession calls this before EVERY mount, so the shell's back state is set
    /// before the mount's first frame arrives. It sends even when the value is unchanged:
    /// a mount after the first is a NEW shell on this process-global session, such as an
    /// Android Activity recreated by a rotation, and a new shell starts with back disabled.
    /// Deduplicating here would leave it disabled while .NET can go back, and a later forward
    /// step that keeps the value true would not correct it, so back would finish the app from
    /// a sub-page.</summary>
    internal void PublishBackState() => PublishBackState(CanGoBack, force: true);

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
                BnLog.Error("NativeNavigationManager", "RouteChanged subscriber threw", ex);
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
