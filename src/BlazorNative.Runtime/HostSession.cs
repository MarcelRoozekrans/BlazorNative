using BlazorNative.Core;
using BlazorNative.Http;
using BlazorNative.Renderer;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorNative.Runtime;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3.0d host session — the lazy singleton behind blazornative_mount /
// blazornative_register_frame_callback.
//
// EnsureSession() builds the production DI surface (Renderer + Http
// services), resolves the NativeRenderer singleton, and
// installs the FrameSink marshaller: RenderFrame → FrameEncoder → one
// synchronous cdecl callback into the host (JNA on the Kotlin side).
//
// Callback lifetime: s_frameCallback is a raw function pointer the HOST owns.
// Re-registration is allowed (last wins). The pointed-at frame + strings live
// in a FrameArena — valid ONLY during the callback; the host copies
// synchronously before returning (PatchProtocolNative.cs contract).
//
// Component registry: mount-by-name keeps reflection out of the C ABI —
// each entry is a statically-rooted generic Mount<T> instantiation, so
// NativeAOT trims nothing it needs.
// ─────────────────────────────────────────────────────────────────────────────

internal static unsafe class HostSession
{
    private static readonly object s_lock = new();
    private static NativeRenderer? s_renderer;
    private static NativeNavigationManager? s_navigation; // born with the session (Phase 3.5)
    private static IntPtr s_frameCallback; // delegate* unmanaged[Cdecl]<BlazorNativeFrame*, void>

    /// <summary>Root componentId of the CURRENT page (Phase 3.5): tracked at
    /// every mount so the navigation swap knows what to unmount. -1 = none.
    /// Single-threaded post-boot contract (same as the renderer's dispatch
    /// fields) — mounts and navigations both run on the host's dispatch lane.</summary>
    private static int s_currentRootComponentId = -1;

    // Sync Mount<T> (inline dispatcher, Phase 2.4) — the first render completes
    // before TryMount returns, so the frame callback has already fired.
    // Values return the root componentId (Phase 3.5: the navigation swap needs
    // it — see s_currentRootComponentId).
    private static readonly Dictionary<string, Func<NativeRenderer, int>> s_components = new()
    {
        ["HelloComponent"] = r => r.Mount<HelloComponent>(),
        // Phase 3.3: the composition proof app (nested components, keyed
        // list mutation, detach — design §6).
        ["CompositionProbe"] = r => r.Mount<CompositionProbe>(),
        // Phase 3.4: the Bn* demo form (bind loop + cascading theme —
        // DoD #5/#6); MainActivity's default since Gate 4.
        ["BnDemo"] = r => r.Mount<BlazorNative.Components.BnDemo>(),
        // Phase 3.5: the demo's second page (route "/settings" — DoD #7).
        ["BnSettingsPage"] = r => r.Mount<BlazorNative.Components.BnSettingsPage>(),
    };

    /// <summary>Stores the host's frame callback. IntPtr.Zero disables
    /// delivery; re-registration is allowed (last wins).</summary>
    public static void SetFrameCallback(IntPtr fnPtr)
        => Volatile.Write(ref s_frameCallback, fnPtr);

    /// <summary>Test-only strict-mode toggle (Phase 3.3 Task 6, DoD #9):
    /// applied to the session renderer at EnsureSession AND to a live session
    /// immediately. The PRODUCTION default stays false — renderer errors log
    /// to stderr rather than crash the host process (deliberate POC posture;
    /// a diagnostics surface is M4+). .NET host-session tests flip this via a
    /// module initializer. The instrumented path has no managed hook, so
    /// EnsureSession ALSO ORs in the <c>BLAZORNATIVE_STRICT=1</c> process
    /// environment variable — set by BlazorNativeTestRunner (Phase 3.5
    /// Gate 0) before any instrumented test class loads, so the ONE-SHOT
    /// read here at first-session creation always sees strict on-device
    /// (no ABI change; the per-class setenv/ordering pattern is gone).
    /// Absent/other values leave the production default.</summary>
    internal static bool StrictErrorsForTests
    {
        get => Volatile.Read(ref s_strictErrors);
        set
        {
            Volatile.Write(ref s_strictErrors, value);
            var renderer = Volatile.Read(ref s_renderer);
            if (renderer is not null)
                renderer.StrictErrors = value;
        }
    }
    private static bool s_strictErrors;

    /// <summary>The live session renderer, or null before the first
    /// EnsureSession/TryMount. Phase 3.2: blazornative_dispatch_event resolves
    /// its target through this — null maps to return code 1 (no session).</summary>
    // BL0006 (NativeRenderer derives from Blazor's internal Renderer): the
    // Renderer project suppresses it project-wide for the same reason — all
    // internal-type access is deliberate and drift-guarded by VerifyAccessors.
#pragma warning disable BL0006
    internal static NativeRenderer? CurrentRenderer => Volatile.Read(ref s_renderer);
#pragma warning restore BL0006

    /// <summary>The session's navigation manager, or null before the first
    /// EnsureSession (Phase 3.5). Tests reach the INavigationManager surface
    /// through this; components resolve it via DI ([Inject]).</summary>
    internal static NativeNavigationManager? CurrentNavigationManager
        => Volatile.Read(ref s_navigation);

    /// <summary>Test-only: tears down the session singleton so "no session"
    /// paths are testable and each test gets a fresh renderer. Tests touching
    /// HostSession serialize via the "host-session" xUnit collection — the
    /// production ABI never calls this.</summary>
    internal static void ResetForTests()
    {
        lock (s_lock)
        {
            // BL0006: Dispose comes from Blazor's internal Renderer base —
            // same deliberate-access rationale as CurrentRenderer above.
#pragma warning disable BL0006
            Volatile.Read(ref s_renderer)?.Dispose();
#pragma warning restore BL0006
            Volatile.Write(ref s_renderer, null);
            Volatile.Write(ref s_navigation, null);
            Volatile.Write(ref s_currentRootComponentId, -1);
            Volatile.Write(ref s_frameCallback, IntPtr.Zero);
        }
    }

    /// <summary>Test-only (same posture as StrictErrorsForTests): swaps a
    /// mount-registry entry so failure paths — a navigation swap whose
    /// target mount THROWS — are testable without a throwing production
    /// component. Returns the original entry; callers restore it in a
    /// finally. The production ABI never calls this; tests using it
    /// serialize via the "host-session" collection like every other
    /// registry consumer.</summary>
    internal static Func<NativeRenderer, int> ReplaceRegistryEntryForTests(
        string name, Func<NativeRenderer, int> mount)
    {
        Func<NativeRenderer, int> original = s_components[name];
        s_components[name] = mount;
        return original;
    }

    /// <summary>Mounts a registered component by name.
    /// Returns 0 = ok, 1 = unknown component, 2 = mount threw.</summary>
    /// <remarks>Phase 3.5 route-aware initial mount (design §1): the mount
    /// ABI stays NAME-based, but when the FIRST mount of a session requests
    /// the routed app's DEFAULT entry ("BnDemo" — MainActivity's no-extra
    /// default), the host's restored route wins: the nav manager initializes
    /// CurrentRoute from the host's CurrentRoute buffer callback, and a
    /// known non-default route mounts ITS page instead (unknown/empty →
    /// "/" → the requested default). Explicit mounts of any OTHER name
    /// (test Intent extras, probes) are never overridden.
    /// One mount per session: a second TryMount over a live root is ADDITIVE
    /// (the new root renders alongside the old one) and orphans the old
    /// root's tracking — host contract: use navigation to change pages.</remarks>
    public static int TryMount(string name)
    {
        if (!s_components.ContainsKey(name))
            return 1;

        try
        {
            NativeRenderer renderer = EnsureSession();

            string effective = name;
            if (Volatile.Read(ref s_currentRootComponentId) < 0
                && name == NativeNavigationManager.DefaultComponent
                && Volatile.Read(ref s_navigation) is { } nav)
            {
                // CurrentRoute lazily queries the host here (startup query);
                // the table clamps it, so ResolveComponent cannot miss.
                effective = nav.ResolveComponent(nav.CurrentRoute);
            }

            MountRoot(effective, renderer);
            return 0;
        }
        catch (Exception ex)
        {
            // ex.ToString() so the InnerException chain + stack survive the
            // C-ABI crossing (same rationale as Exports.cs Init's catch).
            Console.Error.WriteLine($"[HostSession] mount '{name}' failed: {ex}");
            return 2;
        }
    }

    /// <summary>Phase 3.5: the navigation swap (NativeNavigationManager's
    /// step 2). Unmounts the tracked current root — Blazor's disposal
    /// machinery emits the RemoveNode patches that clear the screen — then
    /// mounts the target registry component fresh. Navigations triggered
    /// INSIDE a click handler defer through RunAfterDispatch (Blazor keeps
    /// the event's batch open across the handler, so RemoveRootComponent
    /// cannot start its disposal batch there); the deferred swap still runs
    /// before blazornative_dispatch_event returns. Failures THROW — direct
    /// callers see them; deferred ones join the 3.2 dispatch capture and
    /// map to export rc 2 (strict conventions).</summary>
    /// <param name="name">The mount-registry key to swap to.</param>
    /// <param name="afterSwap">Runs INSIDE the swap unit, after the new root
    /// mounted — the nav manager finalizes route state + RouteChanged here so
    /// neither happens when a (possibly deferred) swap fails.</param>
    internal static void SwapRoot(string name, Action? afterSwap = null)
    {
        if (!s_components.ContainsKey(name))
        {
            throw new InvalidOperationException(
                $"navigation swap target '{name}' is not in the mount registry");
        }

        NativeRenderer renderer = EnsureSession();
        renderer.RunAfterDispatch(() =>
        {
            int current = Volatile.Read(ref s_currentRootComponentId);
            if (current >= 0)
            {
                // Tracking clears BEFORE Unmount (unmount-as-best-effort): a
                // strict-mode disposal fault leaves the old root in an
                // undefined half-disposed state, and keeping its dead id
                // would make every LATER swap re-call Unmount on it — each
                // raising Blazor's "not a live root component"
                // ArgumentException and masking the original fault forever.
                // The fault itself still surfaces (thrown here → rc 2 on the
                // deferred path), and the next swap can proceed to a mount.
                Volatile.Write(ref s_currentRootComponentId, -1);
                renderer.Unmount(current);
            }
            MountRoot(name, renderer);
            afterSwap?.Invoke();
        });
    }

    /// <summary>Mounts a registry component (callers verified the key) and
    /// tracks it as the session's current root; a ROUTED component also syncs
    /// the nav manager's CurrentRoute so route state agrees with the screen
    /// even for direct named mounts. Throws on mount failure.</summary>
    private static void MountRoot(string name, NativeRenderer renderer)
    {
        int rootId = s_components[name](renderer);
        Volatile.Write(ref s_currentRootComponentId, rootId);
        if (Volatile.Read(ref s_navigation) is { } nav
            && NativeNavigationManager.TryGetRouteForComponent(name, out string route))
        {
            nav.NotifyMounted(route);
        }
    }

    // internal (not private): DispatchEventTests needs the renderer BEFORE the
    // first mount so it can subscribe to Frames and harvest the first frame's
    // AttachEventPatch handlerId (the renderer is otherwise only born inside
    // TryMount, after which the first frame is gone).
    internal static NativeRenderer EnsureSession()
    {
        NativeRenderer? renderer = Volatile.Read(ref s_renderer);
        if (renderer is not null)
            return renderer;

        lock (s_lock)
        {
            if (s_renderer is not null)
                return s_renderer;

            // The production DI surface.
            // (No AddBlazorNativeCoreServices call: Phase 3.2 deleted WasiBridge,
            // Core's last [Singleton] type, so the ZeroAlloc.Inject generator no
            // longer emits the Core extension method at all.)
            var services = new ServiceCollection();
            services.AddBlazorNativeRendererServices();
            // Full HttpClient plumbing, not just the generated handler
            // registration: AddBlazorNativeHttp() layers the IHttpClientFactory
            // + default-client configuration over AddBlazorNativeHttpServices()
            // so a component doing [Inject] HttpClient resolves through
            // BridgeHttpHandler (3.3+ components rely on this).
            services.AddBlazorNativeHttp();
            // Phase 3.1: the shell bridge is THE IMobileBridge on-device.
            // Sole runtime registration since Phase 3.2 deleted the WASM-era
            // WasiBridge (Core no longer registers any IMobileBridge).
            // BridgeHttpHandler therefore resolves against the host callbacks
            // on Android.
            services.AddSingleton<IMobileBridge, NativeShellBridge>();
            // Phase 3.5: the navigation service (DoD #7). Registered as the
            // Core contract so components [Inject] INavigationManager; the
            // session also keeps the concrete instance for the route-aware
            // initial mount + swap plumbing (TryMount/SwapRoot).
            services.AddSingleton<INavigationManager, NativeNavigationManager>();
            ServiceProvider provider = services.BuildServiceProvider();
            renderer = provider.GetRequiredService<NativeRenderer>();
            Volatile.Write(ref s_navigation,
                (NativeNavigationManager)provider.GetRequiredService<INavigationManager>());
            // .NET test hook OR the instrumented-harness env toggle (see
            // StrictErrorsForTests doc) — production default remains false.
            renderer.StrictErrors = Volatile.Read(ref s_strictErrors)
                || Environment.GetEnvironmentVariable("BLAZORNATIVE_STRICT") == "1";

            renderer.FrameSink = frame =>
            {
                var cb = (delegate* unmanaged[Cdecl]<BlazorNativeFrame*, void>)
                    Volatile.Read(ref s_frameCallback);
                if (cb == null)
                    return; // no host callback registered — drop the frame

                using var arena = FrameArena.Rent();
                BlazorNativeFrame native = FrameEncoder.Encode(frame, arena);
                cb(&native); // synchronous: arena memory dies when this returns
            };

            Volatile.Write(ref s_renderer, renderer);
            return renderer;
        }
    }
}
