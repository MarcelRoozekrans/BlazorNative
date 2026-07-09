using BlazorNative.Core;
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

    private static NativeRenderer EnsureSession()
    {
        NativeRenderer? renderer = Volatile.Read(ref s_renderer);
        if (renderer is not null)
            return renderer;

        lock (s_lock)
        {
            if (s_renderer is not null)
                return s_renderer;

            // Same registrations as TrimProbeRunner — the production DI surface.
            var services = new ServiceCollection();
            services.AddBlazorNativeCoreServices();
            services.AddBlazorNativeRendererServices();
            services.AddBlazorNativeHttpServices();
            renderer = services.BuildServiceProvider().GetRequiredService<NativeRenderer>();

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
