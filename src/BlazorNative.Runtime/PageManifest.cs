namespace BlazorNative.Runtime;

// ─────────────────────────────────────────────────────────────────────────────
// PageManifest — Phase 7.6 (route-registry unification) reshaped by Phase 8.0
// (the registration inversion, M8 DoD #1).
//
// THE NORMATIVE RULE survives verbatim, one owner swapped: a page is declared
// ONCE — one row in the APP's manifest (samples/BlazorNative.SampleApp's
// `SampleAppPages.All`), registered through `BlazorNativeApp.RegisterPages`.
// This class is the internal STORE behind that API; its 7.6 rows (14 demo/
// probe types the library used to name) moved to the app, provenance comments
// and all. Every other surface is either a DERIVED VIEW (same assembly, same
// object graph — cannot drift by construction) or a PINNED MIRROR:
//
//   - `HostSession.Components` (the mount registry, name → Mount) is DERIVED
//     from ALL rows;
//   - `NativeNavigationManager.Routes` (route → name) is DERIVED from the
//     routed rows (Route != null);
//   - Android's `MainActivity.DEEP_LINK_COMPONENTS` is the one surviving
//     PINNED MIRROR (Intent-parse time, before the .so loads — the 5.1
//     record), drift-tested pair-for-pair by RouteTableDriftTests against
//     the app's manifest in the required build-test lane;
//   - iOS has NO route surface at all — mounts by NAME (the 7.5 record).
//
// LAZY-AFTER-FREEZE (Phase 8.0, the static-ctor preinit hazard): the derived
// views used to be static-readonly projections. Under NativeAOT, ILC may
// pre-initialize static ctors at COMPILE time — and a pre-baked EMPTY
// dictionary (snapshotted before the app's module initializer registered
// anything) is precisely the silent failure this phase cannot afford. So the
// views materialize on FIRST USE, and that first materialization is the
// FREEZE POINT: `Pages` flips s_frozen, and any registration after it (or a
// second registration ever) throws InvalidOperationException. If the app
// registers NOTHING, blazornative_mount returns the existing rc 1 with a
// distinguished stderr diagnostic (HostSession.TryMount) — no new return
// code, no ABI change.
//
// TRIM LAW: the row thunks keep the exact 7.6 shape (`static r =>
// r.Mount<T>()`), now written in APP code through BlazorNativePage's
// DAM(All)-annotated factories — see BlazorNativeApp.cs's header.
// ─────────────────────────────────────────────────────────────────────────────

internal static class PageManifest
{
    internal const string DefaultRoute = BlazorNativeApp.DefaultRoute;

    private static readonly object s_lock = new();
    private static BlazorNativePage[] s_pages = [];
    private static bool s_registered;
    private static bool s_frozen;

    /// <summary>The store's write side (BlazorNativeApp.RegisterPages).
    /// Validates loudly — ArgumentException naming the offending row — then
    /// stores a defensive copy. Once only, and never after the freeze.</summary>
    internal static void Register(BlazorNativePage[] pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        lock (s_lock)
        {
            if (s_frozen)
            {
                throw new InvalidOperationException(
                    "BlazorNativeApp.RegisterPages was called after the page registry was already "
                    + "materialized (the first mount/navigation froze it). Pages are registered once, "
                    + "at app startup, before the first mount — a [ModuleInitializer] is the hook; "
                    + "see samples/BlazorNative.SampleApp.");
            }
            if (s_registered)
            {
                throw new InvalidOperationException(
                    "BlazorNativeApp.RegisterPages was called twice — pages are registered once, at startup.");
            }

            Validate(pages);

            s_pages = (BlazorNativePage[])pages.Clone();
            s_registered = true;
        }
    }

    private static void Validate(BlazorNativePage[] pages)
    {
        if (pages.Length == 0)
        {
            throw new ArgumentException(
                "RegisterPages needs at least one page — an app with no pages has nothing to mount.",
                nameof(pages));
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var routes = new HashSet<string>(StringComparer.Ordinal);
        bool anyRouted = false;
        bool hasDefault = false;

        for (int i = 0; i < pages.Length; i++)
        {
            BlazorNativePage page = pages[i];

            // A default(BlazorNativePage) — e.g. an under-filled `new
            // BlazorNativePage[n]` — has no name and no thunk. The factories
            // are the only legitimate constructors.
            if (string.IsNullOrEmpty(page.Name) || page.Mount is null)
            {
                throw new ArgumentException(
                    $"row {i} is not a factory-built page — create rows via "
                    + "BlazorNativePage.Routed<T>(route, name) or BlazorNativePage.Named<T>(name).",
                    nameof(pages));
            }
            if (!names.Add(page.Name))
            {
                throw new ArgumentException(
                    $"row {i} ('{page.Name}'): duplicate page name — a page is declared ONCE, "
                    + "and mount-registry names are unique.",
                    nameof(pages));
            }
            if (page.Route is not null)
            {
                anyRouted = true;
                if (!routes.Add(page.Route))
                {
                    throw new ArgumentException(
                        $"row {i} ('{page.Name}'): duplicate route '{page.Route}' — routes are unique.",
                        nameof(pages));
                }
                if (page.Route == DefaultRoute)
                    hasDefault = true;
            }
        }

        if (anyRouted && !hasDefault)
        {
            throw new ArgumentException(
                $"a routed app needs a \"{DefaultRoute}\" row — DefaultComponent (the page a host "
                + "mounts with no route) and the route-aware initial mount both resolve through it.",
                nameof(pages));
        }
    }

    /// <summary>THE manifest, and the FREEZE POINT: the first read pins the
    /// registration window shut (see the file header). Both derived views —
    /// and every test-side projection — read this.</summary>
    internal static BlazorNativePage[] Pages
    {
        get
        {
            lock (s_lock)
            {
                s_frozen = true;
                return s_pages;
            }
        }
    }

    /// <summary>The default route's component — the name a host mounts to get
    /// "the routed app" (MainActivity's no-extra default; the Kotlin
    /// <c>?: "BnDemo"</c> fallback literal is pinned to THIS by
    /// RouteTableDriftTests). Null when the registry has no routed rows (an
    /// all-probes registry is legal — validation only demands "/" when a
    /// routed row exists). Derived exactly as before 8.0: the "/" row's name.</summary>
    internal static string? DefaultComponent
    {
        get
        {
            foreach (BlazorNativePage page in Pages)
            {
                if (page.Route == DefaultRoute)
                    return page.Name;
            }
            return null;
        }
    }

    /// <summary>Test-only (BlazorNativeApp.ResetRegistrationForTests): reopens
    /// the store so the empty-registry diagnostic and validation paths are
    /// testable. Callers restore the app manifest in a finally.</summary>
    internal static void ResetForTests()
    {
        lock (s_lock)
        {
            s_pages = [];
            s_registered = false;
            s_frozen = false;
        }
    }
}
