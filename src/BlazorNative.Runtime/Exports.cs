using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BlazorNative.Renderer;

namespace BlazorNative.Runtime;

// ─────────────────────────────────────────────────────────────────────────────
// BlazorNative.Runtime C-ABI surface: Phase 3.0b boot + Phase 3.0d wire
// protocol + Phase 3.1 shell bridge (Phase 3.0e gave the library its final
// name — the version string below is the JNA-visible one).
//
// Eight exports:
//   init                    — load runtime, verify Blazor accessors, store
//                             platform-info options for the shell bridge
//   shutdown                — clears the frame callback (frame flush lands later)
//   version                 — static version cstring
//   register_frame_callback — Phase 3.0d: store the host's cdecl frame callback
//   mount                   — Phase 3.0d: mount a registered component by name
//   dispatch_event          — Phase 3.2: host→renderer event ingress
//                             (handlerId + flat-JSON args; synchronous
//                             handler → re-render → frame callback; 0/1/2/3)
//   register_bridge         — Phase 3.1: copy the host's 6-callback struct
//   fetch_complete          — Phase 3.1: deliver an async fetch response
//
// Phase 3.5 (M3 close): the two diagnostic probe exports —
// blazornative_run_trim_probes (3.0c Gate 4) + blazornative_run_bridge_probes
// (3.1) — are DELETED, together with their runners (TrimProbeRunner,
// BridgeProbeRunner). Their validation job (IL2072 trim paths, bridge ops
// inside the trimmed binary) is superseded by real components running under
// strict mode + production bridge use (BnDemo navigation/storage/fetch on all
// three surfaces).
//
// String ownership rule: input strings are caller-allocated UTF-8,
// callee-borrowed during the call; output strings are static native memory
// (never freed). Documented exception: failure-detail strings (Init's error
// path) are allocated fresh per failing call and leak — acceptable for a
// one-shot boot path. Frame payloads
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

    /// <summary>Single source of truth for the runtime version — the
    /// JNA-visible version cstring and NativeShellBridge.PlatformInfo both
    /// derive from it.</summary>
    internal const string VersionNumber = "1.2.0-phase-4.5";

    static Exports()
    {
        s_versionString = Marshal.StringToCoTaskMemUTF8($"BlazorNative.Runtime {VersionNumber}");
        s_initOkErrorEmpty = Marshal.StringToCoTaskMemUTF8("");
    }

    [UnmanagedCallersOnly(EntryPoint = "blazornative_init", CallConvs = new[] { typeof(CallConvCdecl) })]
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

            // Phase 3.1: STORE the platform-info options so IMobileBridge
            // .PlatformInfo / GetPlatformInfoAsync can serve them (the host
            // owns the strings only during this call — copy now).
            if (opts != null)
            {
                NativeShellBridge.SetPlatformInfo(
                    os: opts->PlatformInfoOs == IntPtr.Zero
                        ? "" : Marshal.PtrToStringUTF8(opts->PlatformInfoOs) ?? "",
                    apiLevel: opts->PlatformInfoApiLevel,
                    note: opts->PlatformInfoNote == IntPtr.Zero
                        ? null : Marshal.PtrToStringUTF8(opts->PlatformInfoNote));
            }

            // Phase 3.0b deliberately does NOT mount a renderer or build a full
            // DI graph here — that work belongs to Phase 3.0d via blazornative_mount.
            // Init is purely "the runtime loaded + Blazor accessors verify" (plus
            // the option store above).

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

    [UnmanagedCallersOnly(EntryPoint = "blazornative_shutdown", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static void Shutdown()
    {
        try
        {
            // Phase 3.0d: clear the frame callback so a post-shutdown re-render
            // (possible once Phase 3.2 wires event-driven re-renders) can never
            // dispatch into a freed JNA trampoline after the host releases its
            // callback object. Renderer/session state is NOT disposed (frame
            // flush / teardown is later-phase work); the static cstrings are
            // intentionally leaked — process-scoped lifetime.
            HostSession.SetFrameCallback(IntPtr.Zero);
        }
        catch (Exception ex)
        {
            // void export — no rc channel; detail on stderr. The wrap exists for
            // the BN0020 boundary shape: nothing may throw across the C-ABI.
            Console.Error.WriteLine($"[Exports] shutdown failed: {ex}");
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "blazornative_version", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static IntPtr Version()
    {
        try
        {
            return s_versionString;
        }
        catch (Exception)
        {
            // Trivially safe (static-field read) — wrapped for the BN0020
            // boundary shape. IntPtr.Zero on fault matches the rc-contract
            // spirit: the host null-checks before decoding the cstring.
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Phase 3.0d: stores the host's frame callback — a cdecl
    /// <c>void (*)(BlazorNativeFrame*)</c> function pointer. Returns 0 on
    /// success. Re-registration is allowed (last wins); passing NULL disables
    /// frame delivery. The frame pointer handed to the callback (and every
    /// string it references) is valid ONLY for the duration of the callback —
    /// the host must copy synchronously (PatchProtocolNative.cs contract).
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "blazornative_register_frame_callback", CallConvs = new[] { typeof(CallConvCdecl) })]
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
    [UnmanagedCallersOnly(EntryPoint = "blazornative_mount", CallConvs = new[] { typeof(CallConvCdecl) })]
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
    /// Phase 3.2: host→renderer event ingress. Thin ABI wrapper over
    /// <see cref="DispatchEventCore"/> (same wrapper/core split as
    /// fetch_complete → CompleteFetch) — the wrapper only marshals the args
    /// pointer and guarantees no exception crosses the ABI.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "blazornative_dispatch_event", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int DispatchEvent(ulong handlerId, IntPtr argsJsonUtf8)
    {
        try
        {
            string? argsJson = argsJsonUtf8 == IntPtr.Zero
                ? null
                : Marshal.PtrToStringUTF8(argsJsonUtf8);
            return DispatchEventCore(handlerId, argsJson);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Exports] dispatch_event handler {handlerId} failed: {ex}");
            return 2;
        }
    }

    /// <summary>
    /// Managed core of blazornative_dispatch_event (testable without the ABI
    /// crossing). Args are flat JSON via the 3.1 FlatJson parser
    /// (NativeShellBridge internals — same hand-rolled pair the Kotlin side
    /// mirrors): <c>{"name":"click"}</c> / <c>{"name":"change","payload":"…"}</c>.
    ///
    /// Return codes:
    ///   0 = dispatched — INCLUDING a stale handlerId: delivery is
    ///       at-most-once, the renderer catches Blazor's ArgumentException for
    ///       a handler that died in a re-render and logs it (a stale tap is
    ///       not an error);
    ///   1 = no session / nothing mounted;
    ///   2 = dispatch faulted — the handler, the resulting re-render, or
    ///       frame delivery threw (anything routed to HandleException inside
    ///       the dispatch window; detail ex.ToString() on stderr — Kotlin
    ///       logs loudly);
    ///   3 = malformed or NULL args, including a handlerId outside the int
    ///       range of the renderer's handler table.
    ///
    /// SYNCHRONOUS by contract: the renderer's InlineDispatcher runs the
    /// handler, the re-render, and the FrameSink callback on the calling
    /// thread, so everything — including frame delivery to the host — has
    /// completed when this returns. Frames therefore still fire only inside
    /// host calls (mount OR dispatch), containing the 3.0d trampoline hazard.
    /// The host-side threading contract (single BlazorNative-Dispatch lane,
    /// never the UI thread) lives in BlazorNativeRuntime.kt.
    /// </summary>
    internal static int DispatchEventCore(ulong handlerId, string? argsJson)
    {
        // Args validate first (rc 3) — a malformed dispatch is diagnosable
        // regardless of session state.
        if (argsJson is null)
            return 3;

        string? name;
        string? payload;
        try
        {
            Dictionary<string, string> args = NativeShellBridge.ParseFlatJsonObject(argsJson);
            if (!args.TryGetValue("name", out name) || string.IsNullOrEmpty(name))
                return 3; // parsed but no event name — not a dispatchable event
            args.TryGetValue("payload", out payload);
        }
        catch (FormatException ex)
        {
            Console.Error.WriteLine($"[Exports] dispatch_event handler {handlerId}: bad args — {ex.Message}");
            return 3;
        }

        // Guard BEFORE the (int) narrowing below: silent truncation of an
        // out-of-range id could alias onto a LIVE handler and dispatch the
        // wrong event — reject as malformed input instead.
        if (handlerId > int.MaxValue)
        {
            Console.Error.WriteLine(
                $"[Exports] dispatch_event: handlerId {handlerId} exceeds the handler table's int range — rejected as malformed");
            return 3;
        }

        var renderer = HostSession.CurrentRenderer;
        if (renderer is null)
            return 1;

        try
        {
            // GetAwaiter().GetResult() is the sync contract, not a blocking
            // wait: the InlineDispatcher completed the work before the Task
            // was handed back (Phase 2.4 decision).
            renderer.DispatchUiEventAsync(new NativeUiEvent(0, (int)handlerId, name, payload))
                .GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception ex)
        {
            // Dispatch fault (DoD #9 partial): the handler, the resulting
            // re-render, or frame delivery threw — visible via rc 2 + full
            // detail on stderr so a device-side crash is diagnosable from
            // logcat.
            Console.Error.WriteLine($"[Exports] dispatch_event handler {handlerId} faulted: {ex}");
            return 2;
        }
    }

    /// <summary>
    /// Phase 3.1: COPIES the host's 6-callback struct into NativeShellBridge
    /// (the host may free its struct memory after this returns; the function
    /// pointers themselves must stay alive — JNA STRONG-ref rule, same as the
    /// frame callback). Re-registration is allowed (last wins). Returns 0 on
    /// success, 2 on null pointer / failure (detail on stderr). Call BEFORE
    /// blazornative_mount so components resolving IMobileBridge find a live
    /// host. Full ABI contract: BridgeProtocolNative.cs.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "blazornative_register_bridge", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static unsafe int RegisterBridge(BlazorNativeBridgeCallbacks* callbacks)
    {
        try
        {
            if (callbacks == null)
                return 2;
            NativeShellBridge.Register(in *callbacks);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Exports] register_bridge failed: {ex}");
            return 2;
        }
    }

    /// <summary>
    /// Phase 3.1: delivers an async fetch response for a FetchBegin request
    /// id. The response struct + every string it references are host-owned
    /// and valid ONLY during this call (copied before return). Return codes:
    ///   0 = delivered
    ///   1 = unknown/already-completed id — benign cancellation race, the
    ///       host should ignore it
    ///   2 = invalid call (null response pointer) or internal bridge failure
    ///       — the host should log LOUDLY; detail lands on stderr
    /// Never throws across the ABI.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "blazornative_fetch_complete", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static unsafe int FetchComplete(long requestId, BlazorNativeFetchResponse* response)
    {
        try
        {
            if (response == null)
            {
                Console.Error.WriteLine($"[Exports] fetch_complete id {requestId}: null response pointer");
                return 2;
            }
            return NativeShellBridge.CompleteFetch(requestId, in *response);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Exports] fetch_complete id {requestId} failed: {ex}");
            return 2;
        }
    }
}
