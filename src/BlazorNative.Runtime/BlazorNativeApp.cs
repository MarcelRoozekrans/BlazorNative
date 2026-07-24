using System.Diagnostics.CodeAnalysis;
using BlazorNative.Renderer;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

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
// of the C ABI. The factories are the only way to construct a VALID row's
// thunk: T carries DynamicallyAccessedMembers(All) (flowing
// NativeRenderer.Mount<T>'s own requirement to the app's call site, where a
// CONCRETE type satisfies it at compile time), and the lambda is
// `static r => r.Mount<T>()` — verbatim the row shape PageManifest carried
// since 7.6. But BlazorNativePage is a readonly struct, so C# ALSO grants a
// public parameterless constructor: `default(BlazorNativePage)` and
// `new BlazorNativePage()` compile and produce a row with a null Name and a
// null Mount thunk — the private constructor cannot suppress them (issue #181).
// That is caller error, not a shape the framework hands out, and it is caught
// LOUDLY at the boundary: PageManifest.Validate rejects any row with a null
// Name or Mount, naming the offending index (see RegisterPages). Publish gates
// hold the trim proof: exactly 4 IL2072 (all in Renderer internals, none here)
// and 10 exports.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One page declaration: route (null = mount-by-name only), mount-
/// registry name, and the statically-rooted mount thunk. Build rows through the
/// two factories (<see cref="Routed{T}"/> / <see cref="Named{T}"/>) — the
/// supported and only VALID construction path. Because this is a
/// <c>readonly struct</c>, C# also grants a public parameterless constructor, so
/// <c>default(BlazorNativePage)</c> and <c>new BlazorNativePage()</c> compile and
/// yield an INVALID row (null <see cref="Name"/> and null mount thunk); the
/// private constructor cannot prevent that. Such a row is rejected loudly at
/// registration (<see cref="BlazorNativeApp.RegisterPages"/> names the offending
/// index) rather than failing silently at first mount — see issue #181.</summary>
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

    /// <summary>Registers app-authored services into the framework's DI
    /// container. Captured ONCE, at app startup, alongside
    /// <see cref="RegisterPages"/> (in a NativeAOT app: from a
    /// <c>[ModuleInitializer]</c> — see the file header), and consumed ONCE by
    /// the host session's composition root: <paramref name="configure"/> runs
    /// on the same <see cref="IServiceCollection"/> the framework registered
    /// its own services into, AFTER those registrations and immediately BEFORE
    /// the single provider is built — so an app <c>[Inject]</c> reaches app
    /// services exactly as it reaches framework ones. Additive and last-wins;
    /// there is exactly ONE provider.
    /// <para>
    /// <b>RESERVED CONTRACTS — the one limit on "last-wins" (#210).</b> This
    /// summary used to say a re-registration of a framework contract was "a
    /// conscious last-write". That was not true of every contract, and the
    /// exception was the dangerous kind: the framework re-resolves some of its
    /// own registrations by concrete type after this delegate runs, so replacing
    /// one did not override behaviour — it broke composition.
    /// <see cref="BlazorNative.Core.INavigationManager"/> is the live case:
    /// re-registering it used to throw <c>InvalidCastException</c> inside the
    /// host session and fail EVERY mount with rc 2, naming nothing. It now fails
    /// fast with a message that names the cause.
    /// </para>
    /// <para>
    /// The rule, stated so it can be relied on: last-wins holds for <b>your</b>
    /// services and for framework services the framework only ever resolves
    /// through the same abstraction you replaced. It does <b>not</b> hold for
    /// contracts documented CONSUME-ONLY — <see cref="BlazorNative.Core.INavigationManager"/>
    /// and <see cref="BlazorNative.Core.IMobileBridge"/> — which the framework
    /// implements and consumes itself. Adding services is always safe; replacing
    /// a consume-only contract is not, and is rejected rather than half-honoured.
    /// </para>
    /// Never calling it is the baseline — the field stays null and the host
    /// session skips the invocation.</summary>
    public static void ConfigureServices(Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        HostSession.SetConfigureServices(configure);
    }

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
