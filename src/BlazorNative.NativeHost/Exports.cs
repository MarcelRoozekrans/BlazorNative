using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BlazorNative.NativeHost;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3.0b boot-only C-ABI surface.
//
// Three exports: init, shutdown, version. No renderer, no frame protocol —
// those land in Phase 3.0c. String ownership rule: input strings are caller-
// allocated UTF-8, callee-borrowed during the call; output strings are static
// native memory (never freed).
//
// See docs/plans/2026-05-31-phase-3.0b-design.md "C-ABI surface" for the
// long-form contract.
// ─────────────────────────────────────────────────────────────────────────────

[StructLayout(LayoutKind.Sequential)]
public struct BlazorNativeInitOptions
{
    public IntPtr PlatformInfoOs;        // const char* — host-allocated UTF-8
    public int    PlatformInfoApiLevel;
    public IntPtr PlatformInfoNote;      // const char* — optional
}

[StructLayout(LayoutKind.Sequential)]
public struct BlazorNativeInitResult
{
    public int    Status;                // 0 = success
    public IntPtr ErrorMessage;          // const char* — set on Status != 0
    public IntPtr VersionString;         // const char* — static, never freed
}

public static class Exports
{
    // Static UTF-8 cstrings — populated in the static ctor so the NativeAOT
    // RVA-fixup machinery doesn't need to allocate at first call.
    private static readonly IntPtr s_versionString;
    private static readonly IntPtr s_initOkErrorEmpty;

    static Exports()
    {
        s_versionString = Marshal.StringToHGlobalAnsi("BlazorNative.NativeHost 0.3.0-phase-3.0b");
        s_initOkErrorEmpty = Marshal.StringToHGlobalAnsi("");
    }

    [UnmanagedCallersOnly(EntryPoint = "blazornative_init")]
    public static unsafe BlazorNativeInitResult Init(BlazorNativeInitOptions* opts)
    {
        try
        {
            // Touch the BlazorInterop static ctor explicitly so VerifyAccessors
            // runs at init time (not lazily at first event dispatch).
            // Throws BlazorVersionMismatchException if Blazor's internal layout
            // drifted from what Phase 3.0a's annotations + Type.GetType refactor
            // expects.
            BlazorNative.Renderer.BlazorInterop.EnsureInitialized();

            // Phase 3.0b deliberately does NOT mount a renderer or build a full
            // DI graph here — that work belongs to Phase 3.0c via blazornative_mount.
            // Init is purely "the runtime loaded + Blazor accessors verify".

            return new BlazorNativeInitResult
            {
                Status = 0,
                ErrorMessage = s_initOkErrorEmpty,
                VersionString = s_versionString,
            };
        }
        catch (Exception ex)
        {
            // Allocate the error message; host borrows the pointer during this
            // call (caller must copy if retaining). Memory leaks per-failure;
            // acceptable since Init is one-shot.
            return new BlazorNativeInitResult
            {
                Status = 1,
                ErrorMessage = Marshal.StringToHGlobalAnsi($"{ex.GetType().Name}: {ex.Message}"),
                VersionString = s_versionString,
            };
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "blazornative_shutdown")]
    public static void Shutdown()
    {
        // Phase 3.0b no-op. Phase 3.0c+ may flush pending frames / dispose
        // renderer state. The static cstrings are intentionally leaked —
        // process-scoped lifetime.
    }

    [UnmanagedCallersOnly(EntryPoint = "blazornative_version")]
    public static IntPtr Version() => s_versionString;
}
