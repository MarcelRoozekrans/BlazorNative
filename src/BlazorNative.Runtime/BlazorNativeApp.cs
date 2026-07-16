using System.Diagnostics.CodeAnalysis;
using BlazorNative.Renderer;
using Microsoft.AspNetCore.Components;

namespace BlazorNative.Runtime;

// ─────────────────────────────────────────────────────────────────────────────
// BlazorNativeApp / BlazorNativePage — Phase 8.0 (design decision 1, M8
// DoD #1: the registration inversion).
//
// THE INVERSION: before 8.0 the library named the app — PageManifest.cs
// hardcoded 14 demo/probe types and Runtime referenced Components to reach
// them. Now the APP names itself: it builds its page rows through the two
// factories below and pushes them, ONCE, at startup, through RegisterPages.
// PageManifest survives as the internal STORE behind this API; HostSession's
// mount registry and NativeNavigationManager's route table remain derived
// views of that one array (the 7.6 normative rule, owner swapped).
//
// WHO CALLS IT: there is no managed Main in a NativeLib — the app's one hook
// where its code runs without being named is [ModuleInitializer], which
// NativeAOT compiles into the startup path and runs eagerly inside the first
// exported call (blazornative_init), before it returns. CoreCLR test hosts
// run module initializers LAZILY (on first touch of the assembly), so tests
// call the app's idempotent EnsureRegistered() from their own module
// initializer instead — see samples/BlazorNative.SampleApp/SampleAppPages.cs.
//
// TRIM LAW (why the factories exist): the mount thunk must be a statically
// rooted generic Mount<T> instantiation — the shape that keeps reflection out
// of the C ABI. The factories are the ONLY way to construct a row's thunk, so
// the trim-law shape is not constructible any other way: T carries
// DynamicallyAccessedMembers(All) (flowing NativeRenderer.Mount<T>'s own
// requirement to the app's call site, where a CONCRETE type satisfies it at
// compile time), and the lambda is `static r => r.Mount<T>()` — verbatim the
// row shape PageManifest carried since 7.6. Publish gates hold the proof:
// exactly 4 IL2072 (all in Renderer internals, none here) and 9 exports.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One page declaration: route (null = mount-by-name only), mount-
/// registry name, and the statically-rooted mount thunk (created ONLY via the
/// two factories — the trim-law shape is not constructible any other way).</summary>
public readonly struct BlazorNativePage
{
    public string? Route { get; }
    public string  Name  { get; }
    internal Func<NativeRenderer, int> Mount { get; }

    private BlazorNativePage(string? route, string name, Func<NativeRenderer, int> mount)
    {
        Route = route;
        Name = name;
        Mount = mount;
    }

    /// <summary>A routed page: reachable by route (navigation / deep links)
    /// AND by mount-registry name.</summary>
    public static BlazorNativePage Routed<[DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.All)] T>(string route, string name)
        where T : IComponent
    {
        if (string.IsNullOrEmpty(route))
            throw new ArgumentException($"page '{name}': a routed page needs a non-empty route", nameof(route));
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException($"route '{route}': a page needs a non-empty mount-registry name", nameof(name));
        return new(route, name, static r => r.Mount<T>());
    }

    /// <summary>An unrouted page: reachable by mount-registry name only (the
    /// probe shape — shells mount it explicitly, navigation never targets it).</summary>
    public static BlazorNativePage Named<[DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.All)] T>(string name)
        where T : IComponent
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("a page needs a non-empty mount-registry name", nameof(name));
        return new(null, name, static r => r.Mount<T>());
    }
}

/// <summary>The app-facing registration surface — the consumer API the 8.3
/// template calls. Lives in Runtime (not Core) because the mount thunk's type
/// is <c>Func&lt;NativeRenderer,int&gt;</c> and its consumer is HostSession —
/// putting it in Core would force a Core→Renderer dependency that inverts the
/// layering for nothing. Core stays pure contracts.</summary>
public static class BlazorNativeApp
{
    public const string DefaultRoute = "/";

    /// <summary>Called ONCE, at app startup, before the first mount (in a
    /// NativeAOT app: from a <c>[ModuleInitializer]</c> — see the file header).
    /// Validates loudly: non-empty array; factory-built rows only; unique
    /// names; unique routes; and if any routed row exists, a
    /// <see cref="DefaultRoute"/> row is required (Android's no-extra default
    /// mount and the route-aware initial mount both resolve through it).
    /// A second call, or a call after the derived views froze, throws
    /// <see cref="InvalidOperationException"/>.</summary>
    public static void RegisterPages(params BlazorNativePage[] pages)
        => PageManifest.Register(pages);

    /// <summary>Test-only: tears down the registration store AND the two
    /// derived views so the empty-registry rc-1 diagnostic and the validation
    /// paths are testable. Tests using it serialize via the "host-session"
    /// collection (they mutate process-wide singletons) and MUST restore the
    /// app manifest in a finally. The production ABI never calls this.</summary>
    internal static void ResetRegistrationForTests()
    {
        PageManifest.ResetForTests();
        HostSession.ResetComponentsViewForTests();
        NativeNavigationManager.ResetRoutesViewForTests();
    }
}
