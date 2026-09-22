using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BlazorNative.Core;
using BlazorNative.Renderer;

namespace BlazorNative.Runtime;

// M11 #173 — CS1591 off for this file. Every public type here is [EditorBrowsable(Never)]
// NOT-API: public only because the C ABI / AOT exports require it, documented by the
// C-ABI contract comments below rather than XML docs. BnEnforceDocCoverage makes CS1591
// an error package-wide (the STABLE surface — BlazorNativeApp, BlazorNativePage — is
// fully ///-documented); these interop types opt out here, and the reference generator
// (scripts/generate-reference.ps1) drops their pages via the same [EditorBrowsable(Never)]
// mark, so they are neither a build break nor a blank stub.
#pragma warning disable 1591

// ─────────────────────────────────────────────────────────────────────────────
// BlazorNative.Runtime C-ABI surface: Phase 3.0b boot + Phase 3.0d wire
// protocol + Phase 3.1 shell bridge (Phase 3.0e gave the library its final
// name — the version string below is the JNA-visible one).
//
// Ten exports (Phase 9.0 grew the surface 9→10 — the first export event since
// Phase 3.1's fetch_complete):
//   init                    — load runtime, verify Blazor accessors, store
//                             platform-info options for the shell bridge
//   shutdown                — clears the frame callback (frame flush lands later)
//   version                 — static version cstring
//   register_frame_callback — Phase 3.0d: store the host's cdecl frame callback
//   mount                   — Phase 3.0d: mount a registered component by name
//   dispatch_event          — Phase 3.2: host→renderer event ingress
//                             (handlerId + flat-JSON args; synchronous
//                             handler → re-render → frame callback; 0/1/2/3)
//   register_bridge         — Phase 3.1 / 5.4 / 9.0: size-negotiated copy of the
//                             host's callback struct (leading structSize;
//                             min-copy + zero-fill — 10 slots since 9.0)
//   fetch_complete          — Phase 3.1: deliver an async fetch response
//   host_event              — Phase 5.1 (M5 DoD #5): host-INITIATED lifecycle
//                             ingress (pause/resume, back, deep links) — fires
//                             the real NativeShellBridge.NativeEvents to mounted
//                             components; name + optional payload; 0/2/3
//   host_call_complete      — Phase 9.0 (M9 DoD #1): deliver the tri-state result
//                             of a GENERIC permission-gated async call (the
//                             fetch_complete twin) — requestId + status + optional
//                             flat-JSON payload; 0/1/2. Denial is DATA, carried in
//                             the status; wired for geolocation in 9.0, generic so
//                             9.1/9.2/9.3 add an op with ZERO further ABI churn.
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

/// <summary>The init-INPUT struct a shell passes to <c>blazornative_init</c>.</summary>
/// <remarks>Not part of the supported public API: public because it is a parameter type of an
/// <c>[UnmanagedCallersOnly]</c> export, and its contract is the sequential C layout mirrored in
/// the C header and the Kotlin JNA structure — not this managed declaration. Its audience is the
/// two shells in this repo. Tier NOT-API.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
[StructLayout(LayoutKind.Sequential)]
public struct BlazorNativeInitOptions
{
    public IntPtr PlatformInfoOs;        // const char* — host-allocated UTF-8
    public int    PlatformInfoApiLevel;
    public IntPtr PlatformInfoNote;      // const char* — optional
    // Phase 10.0 (#121): the shell's real PlatformKind ordinal (DevHost=0,
    // Android=1, iOS=2, Windows=3, Mac=4 — mirrors BlazorNative.Core.PlatformKind).
    // Appended AFTER the three original fields so the LayoutKind.Sequential offsets
    // 0/8/16 are unchanged — an old shell that predates this field leaves it 0
    // (DevHost, a safe non-lying default), a new shell passes its own ordinal so an
    // iOS app stops reporting Android. Both shells ship in this repo and update in
    // lockstep with the C header / Kotlin JNA mirror; this is the init-INPUT struct,
    // NOT the frozen 80-byte callbacks bridge.
    public int    PlatformInfoKind;
    // Phase 11.4 Gate A (#155): the shell's declared BnLogLevel ordinal
    // (Unset=0, Error=1, Warn=2, Info=3, Debug=4, Verbose=5 — mirrors
    // BlazorNative.Core.BnLogLevel).
    //
    // THIS FIELD COSTS ZERO BYTES, which is the whole reason it is allowed to
    // exist inside a frozen-ABI milestone. The struct's alignment is 8 (it holds
    // IntPtrs), so PlatformInfoKind at offset 24 was already followed by 4 bytes
    // of TAIL PADDING. LogLevel lands in that padding at offset 28 and
    // Marshal.SizeOf stays 32 — measured, not assumed, and pinned BOTH ways by
    // NativeShellBridgeTests.InitOptionsStruct_Is32Bytes_… (SizeOf == 32 AND
    // OffsetOf(LogLevel) == 28; either assertion alone would let the field be
    // smuggled in at a cost).
    //
    // Strictly safer than the M10 growth that established the precedent, which
    // DID change the size 24 → 32: a shell that predates this field allocates 32
    // bytes anyway and leaves offset 28 zero, and ordinal 0 = unset = Warn, the
    // quiet Release default. Back-compatible by construction.
    public int    LogLevel;
}

/// <summary>The init-OUTPUT struct <c>blazornative_init</c> writes back to the shell.</summary>
/// <remarks>Not part of the supported public API — a C-ABI export struct, see
/// <see cref="BlazorNativeInitOptions"/>. Tier NOT-API.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
[StructLayout(LayoutKind.Sequential)]
public struct BlazorNativeInitResult
{
    public int    Status;                // 0 = success
    public IntPtr ErrorMessage;          // const char* — set on Status != 0
    public IntPtr VersionString;         // const char* — static, never freed
}

/// <summary>The ten <c>[UnmanagedCallersOnly]</c> C entry points the native shells call.</summary>
/// <remarks>Not part of the supported public API: <c>[UnmanagedCallersOnly]</c> requires the
/// exported methods to be public static, so the containing class is public purely as a
/// consequence of the export mechanism. No app author calls <c>Exports.Init</c> — the callers are
/// the Kotlin/JNA and Swift/ObjC++ shells in this repo, and the exported set is separately gated
/// by a symbol-count check on every published binary. Tier NOT-API.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
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
    internal const string VersionNumber = "0.13.1"; // x-release-please-version

    static Exports()
    {
        s_versionString = Marshal.StringToCoTaskMemUTF8($"BlazorNative.Runtime {VersionNumber}");
        s_initOkErrorEmpty = Marshal.StringToCoTaskMemUTF8("");
    }

    [UnmanagedCallersOnly(EntryPoint = "blazornative_init", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static unsafe BlazorNativeInitResult Init(int structSize, BlazorNativeInitOptions* opts)
    {
        try
        {
            // #213 item 3 — SIZE NEGOTIATION, mirroring blazornative_register_bridge.
            //
            // This export used to take a bare pointer and read FIXED OFFSETS (24, 28)
            // directly out of the caller's buffer. Those offsets are M10/M11 additions —
            // the struct grew 24 → 32 bytes — so a shell compiled against a pre-M10 header
            // allocates only 24 bytes, and reading Kind/LogLevel there was an OUT-OF-BOUNDS
            // read, not merely an uninitialised one. The old comment's "an old shell leaves
            // it 0" was true of a pre-11.4 shell (32 bytes already, tail padding) and FALSE
            // of a pre-10.0 one; nothing drew the line.
            //
            // The register_bridge boundary already solved exactly this and init did not,
            // which was the whole defect: a safety resting on "the shells ship in lockstep"
            // where the codebase had the mechanism to not need the assumption. The copy is
            // NegotiateInitOptions (internal, so the too-short/oversize/null cases are unit-
            // tested the way NativeShellBridge.Register's are) — a field the shell did not
            // supply reads as zero: DevHost for Kind, Unset→Warn for LogLevel.
            BlazorNativeInitOptions o = NegotiateInitOptions(structSize, opts);

            // Phase 11.4 (#155): the log threshold FIRST — before anything else in
            // this method can write a line. Init's own failure path is the highest
            // -value diagnostic the framework ever emits, so it must be governed by
            // the level the shell actually asked for, not by whatever the default
            // was when the module loaded. Ordinal 0 (a shell predating the field,
            // OR one whose buffer was too short to carry it) resolves to the quiet
            // Warn default.
            BnLog.SetLevelFromOrdinal(o.LogLevel);

            // Touch the BlazorInterop static ctor explicitly so VerifyAccessors
            // runs at init time (not lazily at first event dispatch).
            // Throws BlazorVersionMismatchException if Blazor's internal layout
            // drifted from what Phase 3.0a's annotations + Type.GetType refactor
            // expects.
            BlazorNative.Renderer.BlazorInterop.EnsureInitialized();

            // Phase 3.1: STORE the platform-info options so IMobileBridge
            // .PlatformInfo / GetPlatformInfoAsync can serve them (the host
            // owns the strings only during this call — copy now). The string
            // POINTERS are read from the negotiated copy `o`, but the pointees
            // still live in the caller's buffer for the duration of this call —
            // the min-copy duplicates the 8-byte pointer fields (offsets 0/16,
            // always within even the smallest historical struct), not the UTF-8
            // bytes they address. Marshal.PtrToStringUTF8 copies those now.
            if (opts != null)
            {
                NativeShellBridge.SetPlatformInfo(
                    os: o.PlatformInfoOs == IntPtr.Zero
                        ? "" : Marshal.PtrToStringUTF8(o.PlatformInfoOs) ?? "",
                    apiLevel: o.PlatformInfoApiLevel,
                    note: o.PlatformInfoNote == IntPtr.Zero
                        ? null : Marshal.PtrToStringUTF8(o.PlatformInfoNote),
                    // Phase 10.0 (#121): map the shell-supplied ordinal to PlatformKind.
                    // An unset (0), unknown, or out-of-range value falls back to DevHost
                    // — a safe non-lying default, never Android.
                    kind: ToPlatformKind(o.PlatformInfoKind));
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
            //
            // THE ONE DOCUMENTED EXEMPTION FROM §7's RELEASE REDACTION (Phase
            // 11.4). Everywhere else the seam reduces an exception to type +
            // message + top frame below Debug verbosity (BnLog.FormatException),
            // precisely so internal detail does not leak at default Release
            // verbosity. This site keeps the full ToString() ON PURPOSE, and the
            // trade is stated rather than assumed: it is a ONE-SHOT boot failure
            // on a binary that is already not going to run, the diagnostic value
            // of the offending type name outweighs the disclosure, and it does
            // not go to a log at all — it is handed back to the SHELL, in-process,
            // through the init result. Not an oversight; a decision.
            return new BlazorNativeInitResult
            {
                Status = 1,
                ErrorMessage = Marshal.StringToCoTaskMemUTF8(ex.ToString()),
                VersionString = s_versionString,
            };
        }
    }

    /// <summary>Phase 10.0 (#121): maps the shell-supplied init-options ordinal to
    /// <see cref="PlatformKind"/>. The int mirrors the enum ordinal by contract
    /// (DevHost=0, Android=1, iOS=2, Windows=3, Mac=4 — pinned by
    /// NativeShellBridgeTests.ToPlatformKind_MapsTheInitOptionsOrdinal_ByEnumValue).
    /// An unset (0) value is DevHost, and any
    /// unknown/out-of-range ordinal ALSO falls back to DevHost — a safe non-lying
    /// default for a shell that predates or mis-sets the field, never Android.</summary>
    internal static PlatformKind ToPlatformKind(int ordinal)
        => Enum.IsDefined(typeof(PlatformKind), ordinal)
            ? (PlatformKind)ordinal
            : PlatformKind.DevHost;

    /// <summary>
    /// #213 item 3 — copies the caller's init options into a fresh, ZERO-INITIALISED
    /// struct, reading at most <paramref name="structSize"/> bytes and never more than
    /// this runtime's own <c>sizeof</c>. The twin of
    /// <c>NativeShellBridge.Register(structSize, …)</c>, and it exists for the identical
    /// reason: the struct grew <b>24 → 32 bytes</b> over M10/M11, so a shell compiled
    /// against an older header allocates a shorter buffer, and reading the newer fields
    /// (<c>PlatformInfoKind</c> at 24, <c>LogLevel</c> at 28) at fixed offsets was an
    /// OUT-OF-BOUNDS read — not the merely-uninitialised value the old comment assumed.
    ///
    /// <para>The clamp makes the copy length self-contained: the upper bound truncates a
    /// NEWER shell's extra tail; the lower bound turns a stray negative/zero size into a
    /// safe no-copy (everything defaults) rather than an overflow. A field the shell did
    /// not supply reads as zero — <see cref="PlatformKind.DevHost"/> for Kind,
    /// <c>Unset</c>→<c>Warn</c> for LogLevel — which are exactly the safe non-lying
    /// defaults the fixed-offset path intended but could not guarantee. The runtime never
    /// over-reads the caller's buffer.</para>
    ///
    /// <para>A null pointer (a shell that passed none) yields the all-default struct.</para>
    /// </summary>
    internal static unsafe BlazorNativeInitOptions NegotiateInitOptions(
        int structSize, BlazorNativeInitOptions* opts)
    {
        BlazorNativeInitOptions dest = default; // zero-fills the whole struct incl. any un-copied tail
        if (opts != null)
        {
            int known = sizeof(BlazorNativeInitOptions);
            int toCopy = Math.Clamp(structSize, 0, known);
            Buffer.MemoryCopy(opts, &dest, known, toCopy);
        }
        return dest;
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
            BnLog.Error("Exports", "shutdown failed", ex);
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
            BnLog.Error("Exports", "register_frame_callback failed", ex);
            return 2;
        }
    }

    /// <summary>
    /// Phase 3.0d: mounts a registered component by NUL-terminated UTF-8 name.
    /// Builds the DI session lazily on first call. The first render completes
    /// synchronously, so the registered frame callback has already fired when
    /// this returns. Status: 0 ok / 1 unknown component / 2 mount threw
    /// (detail on stderr — logcat / the unified log since Phase 11.4 Gates B
    /// and C) / 3 name pointer null.
    /// <para>
    /// PHASE 11.4 GATE D (#164) — WHAT rc 2 NOW ALSO COVERS. A PARAMETER-BINDING
    /// fault (a component handed a parameter name it does not declare —
    /// <c>@bind-Value</c> where the property is <c>Checked</c>) aborts the mount
    /// and lands here. It used to log and continue, leaving rc 0 and a
    /// half-rendered screen. It is an author bug, never transient, so the host
    /// gets a signal it can act on. EVERY OTHER render fault keeps the
    /// log-and-continue posture and still returns 0 — see
    /// <c>NativeRenderer.HandleException</c> and
    /// <c>BlazorInterop.ParameterBindingFrames</c> for the exact boundary.
    /// </para>
    /// <para>
    /// NO NEW rc, DELIBERATELY: <c>BlazorNativeRuntime.kt</c>'s mount <c>when</c>
    /// ends in <c>else -&gt; throw</c>, so an unknown rc is fatal — a "non-fatal
    /// rc 4" would hard-crash every consumer still on an older COPIED shell.
    /// Design §6.2 also DEFERS option (b) (a host error callback in a new
    /// bridge slot 11, or a frame flag), with this trigger: revisit it when a
    /// genuinely RECOVERABLE fault class appears that the host must react to
    /// PROGRAMMATICALLY — retry, fall back, report — rather than merely observe
    /// in a log. None of the five shipped capabilities has produced one.
    /// </para>
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
            BnLog.Error("Exports", "mount failed", ex);
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
            BnLog.Error("Exports", $"dispatch_event handler {handlerId} failed", ex);
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
            // Warn, not Error: malformed args are the HOST bending the contract, and
            // rc 3 already carries the fact to the caller. The line adds the detail.
            BnLog.Warn("Exports", $"dispatch_event handler {handlerId}: bad args — {ex.Message}");
            return 3;
        }

        // Guard BEFORE the (int) narrowing below: silent truncation of an
        // out-of-range id could alias onto a LIVE handler and dispatch the
        // wrong event — reject as malformed input instead.
        if (handlerId > int.MaxValue)
        {
            BnLog.Warn("Exports",
                $"dispatch_event: handlerId {handlerId} exceeds the handler table's int range — rejected as malformed");
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
            // #201 developer trace — the "I pressed a button and saw it" line. Debug, so
            // the default (Warn) stays quiet; IsEnabled-guarded so the interpolation never
            // runs on the hot dispatch path when off. Name only, never `payload` (it can
            // carry the user's text-field input).
            if (BnLog.IsEnabled(BnLogLevel.Debug))
                BnLog.Debug("Exports", $"dispatch_event handler {handlerId} '{name}' → rc 0");
            return 0;
        }
        catch (Exception ex)
        {
            // Dispatch fault (DoD #9 partial): the handler, the resulting
            // re-render, or frame delivery threw — visible via rc 2 + full
            // detail on stderr so a device-side crash is diagnosable from
            // logcat.
            BnLog.Error("Exports", $"dispatch_event handler {handlerId} faulted", ex);
            return 2;
        }
    }

    /// <summary>
    /// Phase 3.1 / Phase 5.4: COPIES the host's callback struct into
    /// NativeShellBridge (the host may free its struct memory after this returns;
    /// the function pointers themselves must stay alive — JNA STRONG-ref rule,
    /// same as the frame callback). Re-registration is allowed (last wins).
    /// Returns 0 on success, 2 on null pointer / bad size / failure (detail on
    /// stderr). Call BEFORE blazornative_mount so components resolving
    /// IMobileBridge find a live host.
    ///
    /// SIZE NEGOTIATION (Phase 5.4, DoD #6): <paramref name="structSize"/> is the
    /// byte size of the caller's <c>BlazorNativeBridgeCallbacks</c>. The runtime
    /// copies <c>min(structSize, sizeof(its own struct))</c> bytes and zero-fills
    /// the tail — an OLD shell (fewer slots) leaves the new slots null
    /// (capability unsupported), and a NEWER shell's extra tail is ignored. The
    /// export never over-reads the caller's buffer. Full ABI contract:
    /// BridgeProtocolNative.cs.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "blazornative_register_bridge", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static unsafe int RegisterBridge(int structSize, BlazorNativeBridgeCallbacks* callbacks)
    {
        try
        {
            if (callbacks == null || structSize <= 0)
                return 2;
            NativeShellBridge.Register(structSize, callbacks);
            return 0;
        }
        catch (Exception ex)
        {
            BnLog.Error("Exports", "register_bridge failed", ex);
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
                // Warn: a null pointer is the host bending the contract; rc 2 carries it.
                BnLog.Warn("Exports", $"fetch_complete id {requestId}: null response pointer");
                return 2;
            }
            return NativeShellBridge.CompleteFetch(requestId, in *response);
        }
        catch (Exception ex)
        {
            BnLog.Error("Exports", $"fetch_complete id {requestId} failed", ex);
            return 2;
        }
    }

    /// <summary>
    /// Phase 5.1 (M5 DoD #5): host-INITIATED event ingress. Thin ABI wrapper
    /// over <see cref="DispatchHostEventCore"/> (same wrapper/core split as
    /// dispatch_event → DispatchEventCore) — marshals the two caller-allocated
    /// UTF-8 pointers (name required, payload optional/NULL) and guarantees no
    /// exception crosses the ABI. Unlike dispatch_event this carries NO
    /// handlerId: it fires the real <see cref="NativeShellBridge.NativeEvents"/>
    /// multicast, so a mounted component's subscriber (and the re-render its
    /// StateHasChanged drives) runs synchronously before this returns.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "blazornative_host_event", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int HostEvent(IntPtr nameUtf8, IntPtr payloadUtf8)
    {
        try
        {
            string? name = nameUtf8 == IntPtr.Zero
                ? null
                : Marshal.PtrToStringUTF8(nameUtf8);
            string? payload = payloadUtf8 == IntPtr.Zero
                ? null
                : Marshal.PtrToStringUTF8(payloadUtf8);
            return DispatchHostEventCore(name, payload);
        }
        catch (Exception ex)
        {
            BnLog.Error("Exports", "host_event failed", ex);
            return 2;
        }
    }

    /// <summary>
    /// Phase 9.0 (M9 DoD #1): delivers the tri-state result of a GENERIC
    /// permission-gated async call (a HostCallBegin request id). The fetch_complete
    /// twin, generalized: capability-agnostic (requestId + int status + optional
    /// flat-JSON payload), so 9.1/9.2/9.3 reuse it with ZERO further ABI change.
    /// Denial is DATA — it arrives as a status value here, never as a throw. The
    /// payload string (when present) is host-owned and valid ONLY during this call
    /// (copied before return). Return codes:
    ///   0 = delivered
    ///   1 = unknown/already-completed id — benign cancellation race, ignore it
    ///   2 = internal bridge failure — the host should log LOUDLY; detail on stderr
    /// Never throws across the ABI.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "blazornative_host_call_complete", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int HostCallComplete(long requestId, int status, IntPtr payloadJsonUtf8)
    {
        try
        {
            string? payloadJson = payloadJsonUtf8 == IntPtr.Zero
                ? null
                : Marshal.PtrToStringUTF8(payloadJsonUtf8);
            return NativeShellBridge.CompleteHostCall(requestId, status, payloadJson);
        }
        catch (Exception ex)
        {
            BnLog.Error("Exports", $"host_call_complete id {requestId} failed", ex);
            return 2;
        }
    }

    // The reserved host-event name (Phase 5.1) that routes to navigation-back
    // instead of the NativeShellBridge.NativeEvents multicast. The SAME ingress
    // Android's predictive-back (OnBackInvokedCallback, Gate 3) and the JVM test
    // both drive: the back→NavigateBack mapping lives HERE, in .NET, so every
    // shell gets identical back semantics (a Kotlin-side mapping would fork
    // Android from the headless/JVM path). Kotlin's dispatchHostEvent("back")
    // must use this exact literal. As of Phase 14.0 this is BnHostEvents.Back
    // (BlazorNative.Core) — the app-facing copy of the same manifest entry —
    // rather than a private constant, so the shells and the app share one name.

    // The reserved host-event name (Phase 9.1) that routes to forward-navigation
    // instead of the NativeShellBridge.NativeEvents multicast — the WARM half of
    // notification tap-through. When a notification is tapped over a LIVE app,
    // the shell delivers the tap to Activity.onNewIntent (Android) / the UNUC
    // delegate (iOS), parses the blazornative://<route>, and — instead of a cold
    // mount — dispatches host_event("navigate", route). Like "back", the
    // name→verb mapping lives HERE, in .NET, so every shell gets identical
    // semantics: the payload is the bare route string, mapped to
    // NativeNavigationManager.NavigateToAsync. This is wire vocabulary + a .NET
    // branch over the EXISTING blazornative_host_event export — NOT an ABI
    // change (the exact shape 5.1 used to add "back"). The Kotlin/Swift shells
    // (Gates 2/3) must use this exact literal. As of Phase 14.0 this is
    // BnHostEvents.Navigate (BlazorNative.Core), for the same reason as above.

    // The reserved host-event name (Phase 14.2) that routes to
    // BnSafeAreaInsets.Report instead of the NativeShellBridge.NativeEvents
    // multicast. Both shells send this whenever the obscured-area geometry
    // changes (rotation, a keyboard, a call banner) — content sitting under a
    // notch or a home indicator is the motivating bug (design §14.2). Like
    // "back"/"navigate", the name→verb mapping lives HERE so every shell gets
    // identical semantics. Wire vocabulary + a .NET branch over the EXISTING
    // blazornative_host_event export — NOT an ABI change. This is
    // BnHostEvents.SafeAreaChanged (BlazorNative.Core).

    /// <summary>
    /// Managed core of blazornative_host_event (testable without the ABI
    /// crossing). Three routes on ONE ingress:
    ///   • the reserved name "back" (<see cref="BlazorNative.Core.BnHostEvents.Back"/>) → the nav
    ///     manager's NavigateBackAsync (the predictive-back production path);
    ///   • the reserved name "safeAreaChanged" (<see cref="BlazorNative.Core.BnHostEvents.SafeAreaChanged"/>)
    ///     → <see cref="DispatchHostSafeArea"/>, which stores the reported insets
    ///     and re-renders a live session;
    ///   • anything else → the real <see cref="NativeShellBridge.RaiseNativeEvent"/>
    ///     lifecycle multicast (the 3.2 no-op is gone). Reserved names are
    ///     INTERCEPTED before the multicast, so they never reach NativeEvents
    ///     subscribers — each is a command/state report, not a lifecycle
    ///     notification.
    ///
    /// Return codes:
    ///   0 = delivered / handled — a lifecycle event reached every subscriber
    ///       (or none: an unheard signal is not an error, so there is no
    ///       "no session" rc for the multicast path, unlike dispatch_event); OR
    ///       "back" navigated to the previous route; OR "safeAreaChanged"
    ///       stored a valid payload AND a live session was told to re-render;
    ///   1 = NOT handled — for "back", at the origin (no previous route, or no
    ///       session): the shell falls through to its default back (Android
    ///       finishes the Activity). For "safeAreaChanged", a valid payload
    ///       with no session mounted (stored anyway — see
    ///       <see cref="DispatchHostSafeArea"/>), or no payload at all. rc 1
    ///       never occurs on the multicast path — a "nothing to act on"
    ///       semantic parallel to dispatch_event's rc 1;
    ///   2 = a subscriber (or the re-render its StateHasChanged drove, when
    ///       strict rethrows it) faulted — CONTAINED (isolation: every other
    ///       subscriber still ran) but surfaced so the host logs loudly; OR
    ///       the back swap faulted; detail ex.ToString() on stderr;
    ///   3 = malformed input: a NULL or empty event name (an unnamed event is
    ///       undispatchable — mirrors dispatch_event's rc 3); OR, for
    ///       "safeAreaChanged" only, a non-empty payload that fails to parse —
    ///       bad JSON, a missing edge key, or a non-numeric edge value. A NULL
    ///       payload is otherwise LEGAL (most lifecycle events, and "back",
    ///       carry none).
    ///
    /// SYNCHRONOUS by contract, like dispatch_event: raised on the calling
    /// (dispatch-lane) thread, so a subscriber's StateHasChanged re-render — or
    /// the back swap's remove+create frames — have all been delivered when this
    /// returns. Not called from inside a dispatch window (host-initiated,
    /// between clicks), so both StateHasChanged's batch and the swap's
    /// RunAfterDispatch drain cleanly at depth 0 — see the off-lane pin.
    /// </summary>
    internal static int DispatchHostEventCore(string? name, string? payload)
    {
        if (string.IsNullOrEmpty(name))
            return 3; // an unnamed host event is not dispatchable

        if (name == BnHostEvents.Back)
            return DispatchHostBack();

        if (name == BnHostEvents.Navigate)
            return DispatchHostNavigate(payload);

        if (name == BnHostEvents.SafeAreaChanged)
            return DispatchHostSafeArea(payload);

        try
        {
            bool faulted = NativeShellBridge.RaiseNativeEvent(new NativeEvent(name, payload));
            return faulted ? 2 : 0;
        }
        catch (Exception ex)
        {
            // Defensive: RaiseNativeEvent isolates per-subscriber, so this only
            // trips on an unexpected fault OUTSIDE a subscriber body.
            BnLog.Error("Exports", $"host_event '{name}' faulted", ex);
            return 2;
        }
    }

    /// <summary>Routes the reserved "back" host event to the session's nav
    /// manager (Phase 5.1). rc 0 = handled (navigated to the previous route) /
    /// 1 = not handled (at the origin, or no session — the shell finishes) /
    /// 2 = the back swap faulted. Sync: the swap's frames are delivered before
    /// this returns (off-lane RunAfterDispatch drains immediately).</summary>
    private static int DispatchHostBack()
    {
        NativeNavigationManager? nav = HostSession.CurrentNavigationManager;
        if (nav is null)
            return 1; // nothing mounted → nothing to go back from (not handled)

        try
        {
            bool handled = nav.NavigateBackAsync().GetAwaiter().GetResult();
            return handled ? 0 : 1;
        }
        catch (Exception ex)
        {
            BnLog.Error("Exports", "host_event 'back' faulted", ex);
            return 2;
        }
    }

    /// <summary>Routes the reserved "navigate" host event to the session's nav
    /// manager (Phase 9.1) — the WARM half of notification tap-through. The payload
    /// is the target route. rc 0 = navigated / 1 = not handled (no session, or an
    /// unknown/empty route — the shell had a route the app does not know, benign) /
    /// 2 = the navigation swap faulted. Sync: NavigateToAsync runs the swap inline,
    /// so the target page's frames are delivered before this returns. An unknown
    /// route surfaces as ArgumentException from NavigateToAsync and is mapped to
    /// rc 1 (not handled) rather than rc 2 (fault): a stale deep link is not a
    /// renderer fault, and a live session that cannot honour the route simply stays
    /// put — the caller (the shell) already foregrounded the app.</summary>
    private static int DispatchHostNavigate(string? route)
    {
        if (string.IsNullOrEmpty(route))
            return 1; // a navigate with no route is nothing to act on (not handled)

        NativeNavigationManager? nav = HostSession.CurrentNavigationManager;
        if (nav is null)
            return 1; // nothing mounted → nowhere to navigate from (not handled)

        try
        {
            nav.NavigateToAsync(route).GetAwaiter().GetResult();
            return 0;
        }
        catch (ArgumentException)
        {
            // An unknown route (a stale/foreign deep link): the live session cannot
            // honour it, but the app is already foregrounded — not handled, not a fault.
            return 1;
        }
        catch (Exception ex)
        {
            BnLog.Error("Exports", "host_event 'navigate' faulted", ex);
            return 2;
        }
    }

    /// <summary>Routes the reserved "safeAreaChanged" host event (Phase 14.2) to
    /// <see cref="BnSafeAreaInsets.Report"/>. The rc contract, spelled out because
    /// 14.0's <c>EveryReservedHostEvent_IsRoutedRatherThanFallingThrough</c> pin
    /// depends on it (rc 1 with no session is the pin's signature for "routed,
    /// idle"):
    ///   0 = the payload parsed AND a live session was told to re-render
    ///       (<see cref="HostSession.CurrentRenderer"/> is non-null);
    ///   1 = no payload at all (NULL/empty — "nothing sent", the same convention
    ///       <see cref="DispatchHostNavigate"/> uses for a missing route; this is
    ///       also exactly what the 14.0 pin drives every reserved arm with), OR
    ///       the payload parsed but no session is mounted — stored anyway, so
    ///       the NEXT mount starts with the right values instead of rendering at
    ///       zero a second time;
    ///   3 = a non-empty payload that fails to parse: bad JSON, a missing edge
    ///       key, or a non-numeric edge value. NEVER defaulted to zero — a
    ///       missing/bad edge is the shell disagreeing with the wire contract,
    ///       and defaulting it would hide that disagreement, exactly the
    ///       silent-divergence class this milestone exists to close. The insets
    ///       are left untouched on rc 3 (nothing is stored).
    ///
    /// The payload is flat JSON with STRING-valued numbers — the same shape
    /// NativeShellBridge already hand-parses for HTTP headers, reused here rather
    /// than duplicated:
    /// <c>{"top":"47","right":"0","bottom":"34","left":"0"}</c>. All four edge
    /// keys are required; extra keys are ignored.</summary>
    private static int DispatchHostSafeArea(string? payload)
    {
        if (string.IsNullOrEmpty(payload))
            return 1; // nothing sent — nothing to act on (not malformed)

        Dictionary<string, string> fields;
        try
        {
            fields = NativeShellBridge.ParseFlatJsonObject(payload);
        }
        catch (FormatException)
        {
            return 3; // bad JSON — a shell bug, must never fault the dispatch lane
        }

        if (!TryParseEdge(fields, "top", out double top)
            || !TryParseEdge(fields, "right", out double right)
            || !TryParseEdge(fields, "bottom", out double bottom)
            || !TryParseEdge(fields, "left", out double left))
        {
            return 3; // a missing/non-numeric edge — the shell disagreeing with the contract
        }

        // Store unconditionally, even when nothing is mounted to re-render, so a
        // later mount starts with the right values instead of rendering at zero
        // a second time.
        BnSafeAreaInsets.Report(new BnSafeAreaInsets(top, right, bottom, left));

        return HostSession.CurrentRenderer is null ? 1 : 0;
    }

    /// <summary>Parses one edge's value out of the flat-JSON payload. Rejects — as a
    /// PARSE failure (rc 3 in <see cref="DispatchHostSafeArea"/>), same as a missing
    /// key — three shapes <see cref="double.TryParse(string?, System.Globalization.NumberStyles, IFormatProvider?, out double)"/>
    /// with <see cref="System.Globalization.NumberStyles.Float"/> would otherwise
    /// accept without complaint:
    ///   • <c>NaN</c> — a legal .NET float LITERAL, but not a legal inset;
    ///   • <c>Infinity</c>/<c>-Infinity</c> — likewise legal float text, nonsensical
    ///     as an obscured-area measurement;
    ///   • a NEGATIVE value — an inset is, physically, an area obscured from an
    ///     edge; there is no such thing as a negative one, and Yoga's own padding
    ///     setter already rejects negative padding (<c>BnYogaStyleParserTests</c>'
    ///     "negatives accepted ONLY for margin and the offsets" rule) — a negative
    ///     inset stored here would either surface as a silently-dropped Yoga style
    ///     downstream or, combined additively with author padding, produce a
    ///     smaller-than-intended pad that LOOKS like a rounding error rather than a
    ///     malformed wire payload.
    /// All three are numeric-but-nonsensical, on a wire contract the rc-3 doc
    /// comment already calls a shell disagreeing with the contract — this closes
    /// that door for the safe-area edges specifically (whole-branch review MINOR 2).
    /// </summary>
    private static bool TryParseEdge(Dictionary<string, string> fields, string key, out double value)
    {
        value = 0;
        if (!fields.TryGetValue(key, out string? raw)
            || !double.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double parsed))
        {
            return false;
        }

        if (double.IsNaN(parsed) || double.IsInfinity(parsed) || parsed < 0)
            return false;

        value = parsed;
        return true;
    }
}
