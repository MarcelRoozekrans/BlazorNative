using BlazorNative.Core;
using BlazorNative.Http;
using BlazorNative.Renderer;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorNative.Runtime;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3.0d host session — the lazy singleton behind blazornative_mount /
// blazornative_register_frame_callback.
//
// EnsureSession() builds the same DI surface as TrimProbeRunner (Core +
// Renderer + Http services), resolves the NativeRenderer singleton, and
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
// NativeAOT trims nothing it needs (same idiom as TrimProbeRunner).
// ─────────────────────────────────────────────────────────────────────────────

internal static unsafe class HostSession
{
    private static readonly object s_lock = new();
    private static NativeRenderer? s_renderer;
    private static IntPtr s_frameCallback; // delegate* unmanaged[Cdecl]<BlazorNativeFrame*, void>

    // Sync Mount<T> (inline dispatcher, Phase 2.4) — the first render completes
    // before TryMount returns, so the frame callback has already fired.
    private static readonly Dictionary<string, Action<NativeRenderer>> s_components = new()
    {
        ["HelloComponent"] = r => r.Mount<HelloComponent>(),
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
    /// module initializer; JVM/instrumented suites can gain an export-level
    /// hook later if Gate 2/3 need it (none wired — the C ABI is unchanged).</summary>
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
            Volatile.Write(ref s_frameCallback, IntPtr.Zero);
        }
    }

    /// <summary>Mounts a registered component by name.
    /// Returns 0 = ok, 1 = unknown component, 2 = mount threw.</summary>
    public static int TryMount(string name)
    {
        if (!s_components.TryGetValue(name, out Action<NativeRenderer>? mount))
            return 1;

        try
        {
            mount(EnsureSession());
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

            // Same registrations as TrimProbeRunner — the production DI surface.
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
            renderer = services.BuildServiceProvider().GetRequiredService<NativeRenderer>();
            renderer.StrictErrors = Volatile.Read(ref s_strictErrors);

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
