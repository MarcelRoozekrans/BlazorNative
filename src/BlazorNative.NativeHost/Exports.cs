using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BlazorNative.NativeHost;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3.0b boot C-ABI surface + Phase 3.0c Gate 4 diagnostic + Phase 3.0d
// wire protocol.
//
// Seven exports:
//   init                    — load runtime, verify Blazor accessors
//   shutdown                — clears the frame callback (frame flush lands later)
//   version                 — static version cstring
//   run_trim_probes         — Phase 3.0c Gate 4 diagnostic (delete-vs-keep TBD)
//   register_frame_callback — Phase 3.0d: store the host's cdecl frame callback
//   mount                   — Phase 3.0d: mount a registered component by name
//   dispatch_event          — ABI-reserved stub; Phase 3.2 wires event ingress
//
// String ownership rule: input strings are caller-allocated UTF-8,
// callee-borrowed during the call; output strings are static native memory
// (never freed). Documented exception: failure-detail strings (Init's error
// path, RunTrimProbes' non-zero-status path) are allocated fresh per failing
// call and leak — acceptable for one-shot / diagnostic paths. Frame payloads
// (register_frame_callback → callback) live in a FrameArena and are valid
// only during the callback — see PatchProtocolNative.cs.
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
        s_versionString = Marshal.StringToCoTaskMemUTF8("BlazorNative.NativeHost 0.4.0-phase-3.0d");
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
            // DI graph here — that work belongs to Phase 3.0d via blazornative_mount.
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
        // Phase 3.0d: clear the frame callback so a post-shutdown re-render
        // (possible once Phase 3.2 wires event-driven re-renders) can never
        // dispatch into a freed JNA trampoline after the host releases its
        // callback object. Renderer/session state is NOT disposed (frame
        // flush / teardown is later-phase work); the static cstrings are
        // intentionally leaked — process-scoped lifetime.
        HostSession.SetFrameCallback(IntPtr.Zero);
    }

    [UnmanagedCallersOnly(EntryPoint = "blazornative_version")]
    public static IntPtr Version() => s_versionString;

    /// <summary>
    /// Phase 3.0d: stores the host's frame callback — a cdecl
    /// <c>void (*)(BlazorNativeFrame*)</c> function pointer. Returns 0 on
    /// success. Re-registration is allowed (last wins); passing NULL disables
    /// frame delivery. The frame pointer handed to the callback (and every
    /// string it references) is valid ONLY for the duration of the callback —
    /// the host must copy synchronously (PatchProtocolNative.cs contract).
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "blazornative_register_frame_callback")]
    public static int RegisterFrameCallback(IntPtr fnPtr)
    {
        try
        {
            HostSession.SetFrameCallback(fnPtr);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Exports] register_frame_callback failed: {ex}");
            return 2;
        }
    }

    /// <summary>
    /// Phase 3.0d: mounts a registered component by NUL-terminated UTF-8 name.
    /// Builds the DI session lazily on first call. The first render completes
    /// synchronously, so the registered frame callback has already fired when
    /// this returns. Status: 0 ok / 1 unknown component / 2 mount threw
    /// (detail on stderr) / 3 name pointer null.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "blazornative_mount")]
    public static int Mount(IntPtr componentNameUtf8)
    {
        try
        {
            if (componentNameUtf8 == IntPtr.Zero)
                return 3;
            string? name = Marshal.PtrToStringUTF8(componentNameUtf8);
            if (name is null)
                return 3;
            return HostSession.TryMount(name);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Exports] mount failed: {ex}");
            return 2;
        }
    }

    /// <summary>
    /// ABI-reserved, deliberately dormant: Phase 3.2 wires host→renderer event
    /// ingress through this entry point (handlerId + JSON args → NativeUiEvent).
    /// Declared now so the C ABI is complete for M3 DoD and the Kotlin binding
    /// surface doesn't churn. Always returns -1 (not implemented).
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "blazornative_dispatch_event")]
    public static int DispatchEvent(ulong handlerId, IntPtr argsJsonUtf8)
    {
        _ = handlerId;
        _ = argsJsonUtf8;
        return -1;
    }

    /// <summary>
    /// Phase 3.0c Gate 4 diagnostic export. Status = number of failed probes
    /// (0 = all pass, -1 = runner crashed). ErrorMessage carries per-probe
    /// failure detail. Reuses the InitResult struct so the Kotlin side needs
    /// no new mirror. The failure-path ErrorMessage is allocated per call and
    /// never freed — acceptable leak for a diagnostic invoked once per test
    /// run. Fate (delete vs. keep) is a Phase 3.0d decision.
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
