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
    // Static UTF-8 cstrings — allocated once on first type touch via the static
    // ctor; pointers stay valid for the entire process lifetime so the JNA-side
    // caller can hold them across many Init/Version calls. StringToCoTaskMemUTF8
    // (not StringToHGlobalAnsi) gives us actual UTF-8 bytes; ANSI uses the OS
    // codepage (Windows-1252 / locale default on Linux) which would mojibake on
    // any non-ASCII content the Kotlin side decodes with Charsets.UTF_8.
    private static readonly IntPtr s_versionString;
    private static readonly IntPtr s_initOkErrorEmpty;
    private static readonly IntPtr s_probesLabel;

    static Exports()
    {
        s_versionString = Marshal.StringToCoTaskMemUTF8("BlazorNative.NativeHost 0.3.0-phase-3.0b");
        s_initOkErrorEmpty = Marshal.StringToCoTaskMemUTF8("");
        s_probesLabel = Marshal.StringToCoTaskMemUTF8("probes:parameter,cascading,inject");
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
            // acceptable since Init is one-shot. Use ex.ToString() so the
            // InnerException chain + stack come along — for the actual R1
            // failure modes (TypeLoadException, MissingMethodException from
            // NativeAOT trim), Message alone hides the offending type/member.
            return new BlazorNativeInitResult
            {
                Status = 1,
                ErrorMessage = Marshal.StringToCoTaskMemUTF8(ex.ToString()),
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

    /// <summary>
    /// Phase 3.0c Gate 4 diagnostic export. Status = number of failed probes
    /// (0 = all pass, -1 = runner crashed). ErrorMessage carries per-probe
    /// failure detail. Reuses the InitResult struct so the Kotlin side needs
    /// no new mirror. Fate (delete vs. keep) is a Phase 3.0d decision.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "blazornative_run_trim_probes")]
    public static BlazorNativeInitResult RunTrimProbes()
    {
        try
        {
            var (failed, detail) = TrimProbeRunner.RunAll();
            return new BlazorNativeInitResult
            {
                Status = failed,
                ErrorMessage = failed == 0 ? s_initOkErrorEmpty : Marshal.StringToCoTaskMemUTF8(detail),
                VersionString = s_probesLabel,
            };
        }
        catch (Exception ex)
        {
            return new BlazorNativeInitResult
            {
                Status = -1,
                ErrorMessage = Marshal.StringToCoTaskMemUTF8(ex.ToString()),
                VersionString = s_probesLabel,
            };
        }
    }
}
