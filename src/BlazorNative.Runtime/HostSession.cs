using BlazorNative.Core;
using BlazorNative.Device;
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
// NativeAOT trims nothing it needs. Phase 7.6: the entries themselves live in
// PageManifest (one row per page — the single declaration); s_components is a
// DERIVED VIEW of that manifest and cannot drift from the route table by
// construction.
// ─────────────────────────────────────────────────────────────────────────────

internal static unsafe class HostSession
{
    private static readonly object s_lock = new();
    private static NativeRenderer? s_renderer;
    private static NativeNavigationManager? s_navigation; // born with the session (Phase 3.5)
    private static IntPtr s_frameCallback; // delegate* unmanaged[Cdecl]<BlazorNativeFrame*, void>
    private static FrameGate? s_frameGate; // the live session's gate; born and detached with it

    /// <summary>How long shutdown waits for callbacks already in flight, and, separately,
    /// for the render thread to join (Phase 16.1).</summary>
    private static readonly TimeSpan QuiesceBudget = TimeSpan.FromSeconds(5);

    /// <summary>Serializes <see cref="SetFrameCallback"/>'s swap-and-wait, so each wait
    /// covers exactly the entries that may hold the pointer it replaced.</summary>
    private static readonly object s_registrationLock = new();

    /// <summary>Phase 16.1: a counted region around every frame-callback invocation.
    /// The sink enters it before it reads the callback pointer, INVOKES the callback
    /// inside it, and leaves only after the callback returns. What makes waiting on the
    /// gate safe is that invocation: a callback that is running is always counted.
    /// <list type="bullet">
    /// <item><see cref="CloseAndDrain"/> refuses new entries and waits for the ones in
    /// flight. Once shutdown has closed and drained the gate, no frame of that session
    /// reaches the host, including one produced later by a handler that outlived the
    /// render thread's join, and none is inside the host's trampoline.</item>
    /// <item><see cref="WaitOutPreviousEpoch"/> leaves the gate open and waits only for
    /// the entries that may hold a pointer that has just been replaced. Entries are
    /// counted in one of two epoch slots, and the swap flips the epoch, so entries that
    /// arrive after it land in the other slot and a steady stream of new frames cannot
    /// starve the wait.</item>
    /// </list></summary>
    internal sealed class FrameGate
    {
        /// <summary>How many gate regions the CURRENT thread is inside, across all gates.
        /// A registration from inside a frame callback must not wait for itself.</summary>
        [ThreadStatic] private static int t_depth;

        private readonly int[] _inFlight = new int[2];
        private int _epoch;
        private volatile bool _closed;

        /// <summary>True when the calling thread is inside a frame callback.</summary>
        public static bool CallerIsInside => t_depth > 0;

        /// <summary>Test-only: runs inside <see cref="TryEnter"/> between the epoch read and
        /// the slot increment, the window a registration's flip can land in. Null in
        /// production and never set in <c>src</c>; tests reset it in a <c>finally</c>.</summary>
        internal static Action? AfterEpochReadForTests;

        /// <summary>Enters the region, in the current epoch's slot. False once the gate
        /// is closed: the caller drops the frame.</summary>
        public bool TryEnter(out int slot)
        {
            // Increment BEFORE reading _closed or the pointer. CloseAndDrain writes _closed,
            // and SetFrameCallback writes the pointer, BEFORE reading the counts, with a
            // full fence on each side: either the waiter sees this entry and waits for it,
            // or this entry sees the gate closed or the new pointer.
            //
            // The slot must be the epoch that is current AFTER the increment, so the epoch
            // is re-read once the increment's full fence has passed, and a mismatch backs
            // out and retries. Without it, an entry that read the epoch, then lost the CPU
            // across a registration's flip, would be counted in the old slot while it went
            // on to read and call the NEW pointer. The next registration flips back and
            // waits only on the other slot, so it would return while that callback ran.
            // Once the epoch is confirmed, any registration that flips after it waits on
            // this slot, and any that flipped before it wrote its pointer first, which is
            // the pointer this entry then reads.
            while (true)
            {
                slot = Volatile.Read(ref _epoch);
                AfterEpochReadForTests?.Invoke();
                Interlocked.Increment(ref _inFlight[slot]);
                if (Volatile.Read(ref _epoch) == slot)
                    break;
                Interlocked.Decrement(ref _inFlight[slot]);
            }
            if (_closed)
            {
                Interlocked.Decrement(ref _inFlight[slot]);
                return false;
            }
            t_depth++;
            return true;
        }

        /// <summary>Leaves the region entered by a successful <see cref="TryEnter"/>.</summary>
        public void Exit(int slot)
        {
            t_depth--;
            Interlocked.Decrement(ref _inFlight[slot]);
        }

        /// <summary>Closes the gate, then waits until no callback is in flight, bounded by
        /// <paramref name="budget"/>. Returns whether it drained.</summary>
        public bool CloseAndDrain(TimeSpan budget)
        {
            _closed = true;
            Interlocked.MemoryBarrier();
            return SpinWait.SpinUntil(
                () => Volatile.Read(ref _inFlight[0]) == 0 && Volatile.Read(ref _inFlight[1]) == 0,
                budget);
        }

        /// <summary>Called after the callback pointer was replaced: flips the epoch and
        /// waits, bounded, for the previous epoch's entries, which are the only ones that
        /// can hold the replaced pointer. Returns whether they drained.</summary>
        public bool WaitOutPreviousEpoch(TimeSpan budget)
        {
            int previous = Volatile.Read(ref _epoch);
            // Exchange is a full fence: the pointer write before it is visible to any
            // entry that reads the new epoch, so such an entry reads the new pointer.
            Interlocked.Exchange(ref _epoch, 1 - previous);
            return SpinWait.SpinUntil(() => Volatile.Read(ref _inFlight[previous]) == 0, budget);
        }
    }

    // Phase 0.4.0-prep Gate A (design §1): the app's captured service-
    // registration delegate — the ConfigureServices seam. Written ONCE via
    // BlazorNativeApp.ConfigureServices (at the app's [ModuleInitializer],
    // beside RegisterPages); read ONCE by EnsureSession under s_lock and
    // invoked on the ServiceCollection after the framework's registrations and
    // before BuildServiceProvider. Null = never set = no-op (the baseline app).
    // Same Volatile idiom as s_renderer/s_navigation above.
    private static Action<IServiceCollection>? s_configureServices;

    /// <summary>Root componentId of the CURRENT page (Phase 3.5): tracked at
    /// every mount so the navigation swap knows what to unmount. -1 = none.
    /// Single-threaded post-boot contract (same as the renderer's dispatch
    /// fields) — mounts and navigations both run on the host's dispatch lane.</summary>
    private static int s_currentRootComponentId = -1;

    // Phase 7.6 (design decision 1): a DERIVED VIEW of PageManifest.Pages —
    // ALL rows (routed pages AND the unrouted probes), name → mount thunk.
    // Phase 8.0: LAZY-AFTER-FREEZE instead of static-readonly. Under NativeAOT,
    // ILC may pre-initialize static ctors at compile time — a static-readonly
    // projection would snapshot an EMPTY registry before the app's module
    // initializer registered anything, the exact silent failure the inversion
    // cannot afford (PageManifest.cs's header). First materialization IS the
    // freeze point (reading PageManifest.Pages flips its frozen bit). A
    // mutable Dictionary on purpose: ReplaceRegistryEntryForTests swaps
    // entries in THIS copy — the manifest itself is never touched.
    private static Dictionary<string, Func<NativeRenderer, int>>? s_components;

    private static Dictionary<string, Func<NativeRenderer, int>> Components
    {
        get
        {
            Dictionary<string, Func<NativeRenderer, int>>? view = Volatile.Read(ref s_components);
            if (view is not null)
                return view;

            lock (s_lock)
            {
                if (s_components is null)
                {
                    Volatile.Write(ref s_components, PageManifest.Pages
                        .ToDictionary(p => p.Name, p => p.Mount, StringComparer.Ordinal));
                }
                return s_components!;
            }
        }
    }

    /// <summary>Test-only (BlazorNativeApp.ResetRegistrationForTests): drops
    /// the materialized view so a re-registered manifest projects fresh.</summary>
    internal static void ResetComponentsViewForTests()
    {
        lock (s_lock)
        {
            Volatile.Write(ref s_components, null);
        }
    }

    /// <summary>Stores the host's frame callback. IntPtr.Zero disables
    /// delivery; re-registration is allowed (last wins). Phase 16.1: once this
    /// returns, no callback holding the PREVIOUS pointer is still in flight, so the
    /// host may release its old callback object. Android Activity recreation does
    /// exactly that without calling shutdown. The gate stays open: frames keep
    /// flowing to the new pointer while the old ones drain. The wait is bounded at
    /// 5 s with a warning, and skipped when called from inside a frame callback,
    /// which would otherwise wait for itself.</summary>
    public static void SetFrameCallback(IntPtr fnPtr)
    {
        if (FrameGate.CallerIsInside)
        {
            // Registering from a callback: that callback is itself an old-pointer entry,
            // so waiting would only time out. Swap, and leave the wait to the host.
            Volatile.Write(ref s_frameCallback, fnPtr);
            return;
        }

        lock (s_registrationLock)
        {
            Volatile.Write(ref s_frameCallback, fnPtr);
            FrameGate? gate = Volatile.Read(ref s_frameGate);
            if (gate is not null && !gate.WaitOutPreviousEpoch(QuiesceBudget))
            {
                BnLog.Warn("HostSession",
                    $"a callback holding the previous frame callback was still in flight "
                    + $"{QuiesceBudget.TotalSeconds:0} s after re-registration; returning without it");
            }
        }
    }

    /// <summary>Stores the app's ConfigureServices delegate (design §1) —
    /// backing BlazorNativeApp.ConfigureServices. Consumed once by
    /// EnsureSession when it builds the session's provider. Same Volatile
    /// write idiom as SetFrameCallback; last write wins.</summary>
    internal static void SetConfigureServices(Action<IServiceCollection>? configure)
        => Volatile.Write(ref s_configureServices, configure);

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

    /// <summary>Detaches the live session under <c>s_lock</c> and returns its renderer
    /// and frame gate, so the caller can quiesce them OUTSIDE the lock. Nothing here
    /// waits. The next EnsureSession builds a fresh session.</summary>
    private static (NativeRenderer? Renderer, FrameGate? Gate) Detach()
    {
        lock (s_lock)
        {
            NativeRenderer? renderer = Volatile.Read(ref s_renderer);
            FrameGate? gate = Volatile.Read(ref s_frameGate);
            Volatile.Write(ref s_renderer, null);
            Volatile.Write(ref s_frameGate, null);
            Volatile.Write(ref s_navigation, null);
            Volatile.Write(ref s_currentRootComponentId, -1);
            return (renderer, gate);
        }
    }

    /// <summary>Quiescence steps 1 and 2: close the session's frame gate and drain the
    /// callbacks in flight, THEN clear the callback pointer. Never under s_lock.</summary>
    private static void CloseGateAndClearCallback(FrameGate? gate)
    {
        if (gate is not null && !gate.CloseAndDrain(QuiesceBudget))
        {
            BnLog.Warn("HostSession",
                $"a frame callback was still in flight {QuiesceBudget.TotalSeconds:0} s after shutdown "
                + "closed the frame gate; shutdown continues without it");
        }
        Volatile.Write(ref s_frameCallback, IntPtr.Zero);
    }

    /// <summary>Quiescence step 3: shut the render thread's queue and join it, bounded.
    /// Logs a warning when it did not join. Never under s_lock.</summary>
    private static void JoinRenderThread(NativeRenderer renderer)
    {
        if (renderer.Dispatcher is RenderThreadDispatcher dispatcher
            && !dispatcher.Shutdown(QuiesceBudget))
        {
            BnLog.Warn("HostSession",
                $"the render thread did not join within {QuiesceBudget.TotalSeconds:0} s because a "
                + "handler has not yielded; its later frames are dropped at the closed frame gate");
        }
    }

    /// <summary>Phase 16.1 — behind blazornative_shutdown. Once this returns, no frame
    /// of the session can reach the host's callback and none is in flight, even when a
    /// handler never yields. In order:
    /// <list type="number">
    /// <item>detach the session under s_lock, so the next EnsureSession builds a fresh
    /// one instead of handing out a renderer whose thread is gone;</item>
    /// <item>close the frame gate and drain the callbacks already in flight;</item>
    /// <item>clear the callback pointer;</item>
    /// <item>shut the render thread's queue and join it, bounded at 5 s, with a warning
    /// if it did not join. Work posted after this completes as cancelled.</item>
    /// </list>
    /// Steps 2 to 4 run outside s_lock. The renderer is not disposed: its components'
    /// Dispose needs the render thread, which a handler may still hold.</summary>
    internal static void Shutdown()
    {
        (NativeRenderer? renderer, FrameGate? gate) = Detach();
        CloseGateAndClearCallback(gate);
        if (renderer is not null)
            JoinRenderThread(renderer);
    }

    /// <summary>Test-only: tears down the session singleton so "no session"
    /// paths are testable and each test gets a fresh renderer. Tests touching
    /// HostSession serialize via the "host-session" xUnit collection — the
    /// production ABI never calls this. Phase 16.1: the statics are swapped out
    /// under s_lock, and everything that waits (the gate's drain, the renderer's
    /// dispose on its render thread, and the join) runs after the lock is
    /// released. The session's render thread is joined, so no thread leaks.</summary>
    internal static void ResetForTests()
    {
        (NativeRenderer? renderer, FrameGate? gate) = Detach();
        // Gate A: clear the app's captured ConfigureServices delegate too,
        // so a session-composition capture never leaks across tests (the
        // ConfigureServices seam's isolation, alongside the renderer's).
        Volatile.Write(ref s_configureServices, null);

        CloseGateAndClearCallback(gate);
        if (renderer is null)
            return;

        // BL0006: Dispose comes from Blazor's internal Renderer base —
        // same deliberate-access rationale as CurrentRenderer above. The
        // components are disposed ON the render thread, which waits for any
        // handler holding it: the reason none of this runs under s_lock.
#pragma warning disable BL0006
        renderer.Dispose();
#pragma warning restore BL0006
        // Dispose joins on its way out; this call reports a join that timed out.
        JoinRenderThread(renderer);
    }

    /// <summary>Test-only: the mount registry's KEYS — every name
    /// <see cref="TryMount"/> and <see cref="SwapRoot"/> accept. Born in Phase
    /// 6.3 so the then-hand-maintained route table could be checked against
    /// this registry; since Phase 7.6 both are derived views of
    /// <see cref="PageManifest.Pages"/> (routes ⊆ registry holds by
    /// construction) and this surface remains for tests that enumerate the
    /// mountable names.</summary>
    internal static IReadOnlyCollection<string> RegisteredComponentsForTests
        => Components.Keys;

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
        Func<NativeRenderer, int> original = Components[name];
        Components[name] = mount;
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
        // Phase 8.0: the distinguished empty-registry diagnostic. Rides the
        // EXISTING rc 1 (no new return code, no ABI change) but names the fix:
        // an app that never called RegisterPages cannot mount anything, and
        // "unknown component" would send the integrator hunting a typo.
        Dictionary<string, Func<NativeRenderer, int>> components = Components;
        if (components.Count == 0)
        {
            BnLog.Error("HostSession",
                "no pages are registered — the app assembly must call "
                + "BlazorNativeApp.RegisterPages at startup (a [ModuleInitializer]; "
                + "see samples/BlazorNative.SampleApp).");
            return 1;
        }
        if (!components.ContainsKey(name))
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
            // #201 developer trace (Debug, IsEnabled-guarded). `effective` is the
            // resolved registry name — a route-aware initial mount may differ from `name`.
            if (BnLog.IsEnabled(BnLogLevel.Debug))
                BnLog.Debug("HostSession", $"mounted '{effective}' → rc 0");
            return 0;
        }
        catch (Exception ex)
        {
            // ex.ToString() so the InnerException chain + stack survive the
            // C-ABI crossing (same rationale as Exports.cs Init's catch).
            BnLog.Error("HostSession", $"mount '{name}' failed", ex);
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
    /// map to export rc 2 (strict conventions). Phase 16.1: the whole swap
    /// decision runs on the render thread, and a call from any other thread
    /// waits for ALL of it — off the render thread no dispatch scope can be
    /// open, so the swap runs at once and its frames are delivered before
    /// this returns.</summary>
    /// <param name="name">The mount-registry key to swap to.</param>
    /// <param name="afterSwap">Runs INSIDE the swap unit, after the new root
    /// mounted — the nav manager finalizes route state + RouteChanged here so
    /// neither happens when a (possibly deferred) swap fails.</param>
    internal static void SwapRoot(string name, Action? afterSwap = null)
    {
        if (!Components.ContainsKey(name))
        {
            throw new InvalidOperationException(
                $"navigation swap target '{name}' is not in the mount registry");
        }

        NativeRenderer renderer = EnsureSession();
        // Inline on the render thread, where a handler's navigation must still see
        // its own dispatch scope; a full wait from anywhere else.
        renderer.Dispatcher.InvokeAsync(() => renderer.RunAfterDispatch(() =>
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
        })).GetAwaiter().GetResult();
    }

    /// <summary>Mounts a registry component (callers verified the key) and
    /// tracks it as the session's current root; a ROUTED component also syncs
    /// the nav manager's CurrentRoute so route state agrees with the screen
    /// even for direct named mounts. Throws on mount failure.</summary>
    private static void MountRoot(string name, NativeRenderer renderer)
    {
        int rootId = Components[name](renderer);
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
            // Phase 9.0: the device facades (IGeolocation) — a thin delegate over
            // the IMobileBridge above, so a component [Inject]s the ergonomic facade
            // and the permission-gated host-call bridge stays out of component code.
            services.AddBlazorNativeDevice();
            // Phase 3.5: the navigation service (DoD #7). Registered as the
            // Core contract so components [Inject] INavigationManager; the
            // session also keeps the concrete instance for the route-aware
            // initial mount + swap plumbing (TryMount/SwapRoot).
            // #210: registered as the CONCRETE type first, with the Core contract
            // resolving THROUGH it. The old single line
            // (`AddSingleton<INavigationManager, NativeNavigationManager>()`) made
            // the concrete instance reachable ONLY via the interface, so the session
            // had to downcast what the interface resolved to — and an app that
            // re-registered the interface made that cast throw. Two registrations
            // pointing at ONE instance keep the framework's own plumbing resolvable
            // by a type no app registration can displace, while `[Inject]
            // INavigationManager` still reaches the same object.
            services.AddSingleton<NativeNavigationManager>();
            services.AddSingleton<INavigationManager>(
                sp => sp.GetRequiredService<NativeNavigationManager>());
            // Phase 0.4.0-prep Gate A (design §1): the app-service seam. The
            // app's captured ConfigureServices delegate runs LAST — after every
            // framework registration above, immediately before the build — so an
            // app registration is purely additive, or a conscious last-write over
            // a framework contract. Null (never set) = no-op. Read once here,
            // under s_lock; still exactly ONE ServiceProvider.
            Action<IServiceCollection>? configure = Volatile.Read(ref s_configureServices);
            configure?.Invoke(services);
            ServiceProvider provider = services.BuildServiceProvider();
            renderer = provider.GetRequiredService<NativeRenderer>();

            // #210 — RESOLVE THE CONCRETE TYPE, THEN CHECK THE CONTRACT STILL POINTS AT IT.
            //
            // This used to be a downcast of what `INavigationManager` resolved to. An app
            // doing the thing ConfigureServices documents — `services.AddSingleton<
            // INavigationManager, MyNav>()` — made that cast throw InvalidCastException
            // *inside* EnsureSession, which TryMount maps to rc 2. Every mount failed, and
            // the mount-failure log never named DI as the cause. A landmine disguised as a
            // supported override.
            //
            // The concrete resolve cannot throw: nothing an app registers displaces the
            // NativeNavigationManager registration above.
            NativeNavigationManager navigation = provider.GetRequiredService<NativeNavigationManager>();

            // …and the interface MUST still resolve to that same instance. This is not
            // defensive noise — it is the difference between two failure modes, and the
            // loud one is far better. If an app replaced the contract, components would
            // `[Inject]` THEIR manager while the shell's route-aware mount and root swap
            // drove THIS one: navigation would half-work, silently, in a way no stack
            // trace explains. INavigationManager is consume-only (see its xmldoc and the
            // 1.0 criteria's A4), so this configuration is unsupported rather than
            // merely awkward, and saying so at composition time is the honest response.
            if (!ReferenceEquals(provider.GetRequiredService<INavigationManager>(), navigation))
            {
                throw new InvalidOperationException(
                    "ConfigureServices re-registered INavigationManager. That contract is "
                    + "CONSUME-ONLY: the framework's route-aware mount and root swap resolve "
                    + "the concrete NativeNavigationManager, so a replacement would leave "
                    + "components injecting one navigator while the shell drove another. "
                    + "Remove the registration — ConfigureServices is last-wins for YOUR "
                    + "services, not for the framework contracts it re-resolves internally.");
            }

            Volatile.Write(ref s_navigation, navigation);
            // .NET test hook OR the instrumented-harness env toggle (see
            // StrictErrorsForTests doc) — production default remains false.
            renderer.StrictErrors = Volatile.Read(ref s_strictErrors)
                || Environment.GetEnvironmentVariable("BLAZORNATIVE_STRICT") == "1";

            // Phase 16.1: every callback is INVOKED inside this session's frame gate,
            // and the gate is left only after the callback returns. That is what makes
            // both waits safe: a callback that is running is always counted, so
            // Shutdown's drain and SetFrameCallback's epoch wait each cover every call
            // into the pointer they replace. The pointer is read after entering, so an
            // entry the waiter misses reads the new value. A closed gate drops the
            // frame: the session has been shut down or reset.
            var gate = new FrameGate();
            renderer.FrameSink = frame =>
            {
                if (!gate.TryEnter(out int slot))
                    return;
                try
                {
                    var cb = (delegate* unmanaged[Cdecl]<BlazorNativeFrame*, void>)
                        Volatile.Read(ref s_frameCallback);
                    if (cb == null)
                        return; // no host callback registered — drop the frame

                    using var arena = FrameArena.Rent();
                    BlazorNativeFrame native = FrameEncoder.Encode(frame, arena);
                    cb(&native); // synchronous: arena memory dies when this returns
                }
                finally
                {
                    gate.Exit(slot);
                }
            };

            Volatile.Write(ref s_frameGate, gate);
            Volatile.Write(ref s_renderer, renderer);
            return renderer;
        }
    }
}
