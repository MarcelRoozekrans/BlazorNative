using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using BlazorNative.Core;

namespace BlazorNative.Runtime;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3.1 — NativeShellBridge: IMobileBridge over the six host-registered
// cdecl callbacks (BridgeProtocolNative.cs holds the ABI contract).
//
// Registration: blazornative_register_bridge COPIES the callbacks struct into
// s_callbacks — the host may free its struct memory after the call (the
// function pointers must stay alive host-side: JNA STRONG-ref rule).
//
// Sync ops: caller-allocated buffers, 4 KB first try, ONE retry at the exact
// -needed size, second failure throws. StorageRead -2 → null.
//
// Fetch: async completion pattern. FetchAsync assigns an Interlocked id,
// parks a TaskCompletionSource (RunContinuationsAsynchronously — completion
// arrives on a host/Kotlin thread and must not run continuations inline on
// it), and calls FetchBegin; the host answers later via
// blazornative_fetch_complete → CompleteFetch. Cancellation removes the id
// from the table and cancels the task; a late/unknown completion hits the
// unknown-id path (return 1, logged, never a throw).
//
// Headers cross as a flat JSON object string ({"k":"v",...}) hand-written /
// hand-parsed below — no JsonSerializerContext (trim-cost decision, see the
// Phase 3.1 design doc).
//
// State is static (process-wide, like HostSession): the C ABI has one bridge
// per process. The instance class exists so DI can hand it out as
// IMobileBridge.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class NativeShellBridge : IMobileBridge
{
    internal const string NotRegisteredMessage =
        "shell bridge not registered — call blazornative_register_bridge before mount";

    private const int DefaultBufferSize = 4096;

    /// <summary>Sanity ceiling for the -needed retry rent: a host demanding
    /// more than this is malfunctioning, not returning a big route/value.</summary>
    private const int MaxRetryBytes = 4 * 1024 * 1024;

    /// <summary>Immutable snapshot of the registered callbacks. The holder
    /// REFERENCE is swapped atomically via Volatile.Write/Read, so a reader
    /// can never observe a torn 72-byte struct during re-registration — an
    /// in-flight op sees either the old snapshot or the new one, whole.
    /// (The host-side liveness obligation for the OLD pointers is documented
    /// in BridgeProtocolNative.cs.)</summary>
    private sealed class CallbackHolder
    {
        public readonly BlazorNativeBridgeCallbacks Callbacks;
        public CallbackHolder(in BlazorNativeBridgeCallbacks callbacks) => Callbacks = callbacks;
    }

    private static CallbackHolder? s_callbacks; // copied at registration; null = unregistered

    private static long s_nextFetchId;
    private static readonly ConcurrentDictionary<long, TaskCompletionSource<BridgeHttpResponse>>
        s_pendingFetches = new();

    // Phase 9.0 — the permission-gated async pending registry (the s_pendingFetches
    // twin). Keyed by an Interlocked id; RunContinuationsAsynchronously because the
    // completion lands on a host/Kotlin thread. Process-scoped (a static field, one
    // bridge per process) — so it SURVIVES an Android Activity recreation: only the
    // host-side requestCode→requestId map is app-scoped; this .NET state never noticed.
    private static long s_nextHostCallId;
    private static readonly ConcurrentDictionary<long, TaskCompletionSource<HostCallResult>>
        s_pendingHostCalls = new();

    /// <summary>The generic permission-gated capabilities carried on the ONE
    /// HostCallBegin slot. 9.0 wired exactly one (Geolocation); Phase 9.1 added the
    /// SECOND (Notifications); Phase 9.2 added TWO at once (Biometrics = 2,
    /// SecureStorage = 3); Phase 9.3 adds the FIFTH (Camera = 4) — op-enum values and
    /// nothing else on the ABI: NO struct grow (still 80 bytes / 10 slots), NO new
    /// export (still 10), NO drift-pin move. This is the "pay once (9.0), reuse
    /// thrice" bet paying its FOURTH draw — and Camera does it while returning an
    /// IMAGE, because the image crosses as a PATH in the completion payload, not bytes
    /// (no binary/buffer export). The integer is the wire contract — the host switches
    /// on it, and the shells' mirror enums (Kotlin HostCallOp / Swift BnHostCallOp)
    /// gain the same values at Gates 2/3.</summary>
    internal enum HostCallOp { Geolocation = 0, Notifications = 1, Biometrics = 2, SecureStorage = 3, Camera = 4 }

    /// <summary>A completion carried back through blazornative_host_call_complete:
    /// the wire status integer (mapped to <see cref="GeolocationStatus"/> etc. by the
    /// caller) + an optional flat-JSON payload (the fix, when Granted).</summary>
    internal readonly record struct HostCallResult(int Status, string? PayloadJson);

    // The args each geolocation op sends — a flat JSON object, so a later phase can
    // extend it without an ABI change. `mode` lets the host distinguish the
    // request-then-fetch call (may prompt) from the read-only permission check (never
    // prompts) on the SAME op — no second op, no second slot.
    private const string GeolocationRequestArgs = "{\"mode\":\"request\"}";
    private const string GeolocationCheckArgs = "{\"mode\":\"check\"}";

    /// <summary>Real host→.NET event multicast (Phase 5.1, replacing the 3.2
    /// no-op). Static, like every other bridge field: the C ABI has one bridge
    /// per process, so components resolving the DI singleton and the
    /// blazornative_host_event export both address THIS invocation list.
    /// Single-threaded post-boot (the dispatch lane), but a bare field is fine —
    /// += / -= produce a new immutable delegate the raiser snapshots.</summary>
    private static Action<NativeEvent>? s_nativeEvents;

    // Init-time platform options (Exports.Init stores them; PlatformInfo serves them).
    // Phase 10.0 (#121): Kind is host-supplied like Os — the shell passes its real
    // PlatformKind through the init options, so an iOS app no longer reports Android.
    private sealed record PlatformOptions(string Os, int ApiLevel, string? Note, PlatformKind Kind);
    private static PlatformOptions? s_platformOptions;

    // ── Registration (blazornative_register_bridge / Exports.Init plumbing) ──

    /// <summary>Size-negotiated registration (Phase 5.4, DoD #6). Copies
    /// <c>min(structSize, sizeof(BlazorNativeBridgeCallbacks))</c> bytes from the
    /// host's (possibly shorter) struct into a fresh, ZERO-INITIALISED holder and
    /// swaps the reference atomically. The min-copy never over-reads the caller's
    /// buffer; the zero-fill leaves any slot the host didn't supply as
    /// <c>IntPtr.Zero</c> — the "capability unsupported" signal the invokers
    /// guard on. A newer host's extra tail (structSize &gt; ours) is ignored.
    /// Re-registration is allowed (last wins) — same posture as the frame
    /// callback.</summary>
    internal static unsafe void Register(int structSize, BlazorNativeBridgeCallbacks* source)
    {
        int known = sizeof(BlazorNativeBridgeCallbacks);
        // Clamp both bounds so the copy length is a self-contained invariant: the
        // upper bound truncates a newer host's extra tail; the lower bound makes a
        // stray negative size a safe no-copy (all-zero → everything unsupported)
        // rather than an OverflowException, independent of the export's structSize
        // guard (defense in depth).
        int toCopy = Math.Clamp(structSize, 0, known);
        BlazorNativeBridgeCallbacks dest = default; // zero-fills the whole struct, incl. any un-copied tail
        Buffer.MemoryCopy(source, &dest, known, toCopy);
        Volatile.Write(ref s_callbacks, new CallbackHolder(in dest));
    }

    /// <summary>Full-struct registration convenience (in-process managed callers
    /// + tests): forwards to the size-negotiated core with
    /// <c>structSize == sizeof</c>, so all slots are copied. A test may instead
    /// call the (structSize, in) overload with a smaller size to exercise the
    /// forward-compat truncation.</summary>
    internal static unsafe void Register(in BlazorNativeBridgeCallbacks callbacks)
    {
        fixed (BlazorNativeBridgeCallbacks* p = &callbacks)
            Register(sizeof(BlazorNativeBridgeCallbacks), p);
    }

    /// <summary>Test seam: register a managed struct as if the host had declared
    /// only <paramref name="structSize"/> bytes — the min-copy truncates and the
    /// tail zero-fills, exactly like a shorter native buffer (drives the 48-byte
    /// old-shell → unsupported-clipboard forward-compat path).</summary>
    internal static unsafe void Register(int structSize, in BlazorNativeBridgeCallbacks callbacks)
    {
        fixed (BlazorNativeBridgeCallbacks* p = &callbacks)
            Register(structSize, p);
    }

    /// <summary>Stores Init's platform options; <see cref="PlatformInfo"/> and
    /// <see cref="GetPlatformInfoAsync"/> serve them. Phase 10.0 (#121): the
    /// host-supplied <paramref name="kind"/> is served verbatim, so the reported
    /// PlatformKind is the shell's real one (iOS on iOS, Android on Android) rather
    /// than a hardcoded constant.</summary>
    internal static void SetPlatformInfo(string os, int apiLevel, string? note, PlatformKind kind)
        => Volatile.Write(ref s_platformOptions, new PlatformOptions(os, apiLevel, note, kind));

    /// <summary>Test-only: unregister + drain pending fetches so state can't
    /// leak across tests (the "host-session" xUnit collection serializes
    /// callers — Phase 3.5 merged the former "native-shell-bridge"
    /// collection into it).</summary>
    internal static void ResetForTests()
    {
        Volatile.Write(ref s_callbacks, null);
        foreach (long id in s_pendingFetches.Keys)
        {
            if (s_pendingFetches.TryRemove(id, out var tcs))
                tcs.TrySetCanceled();
        }
        // Phase 9.0: drain pending host calls too, so a call left in flight by a
        // test (a killed-process analogue) cannot leak across tests.
        foreach (long id in s_pendingHostCalls.Keys)
        {
            if (s_pendingHostCalls.TryRemove(id, out var tcs))
                tcs.TrySetCanceled();
        }
        Volatile.Write(ref s_platformOptions, null);
        s_nativeEvents = null; // drop subscribers so they can't leak across tests
    }

    private static BlazorNativeBridgeCallbacks GetCallbacks()
    {
        CallbackHolder? holder = Volatile.Read(ref s_callbacks);
        if (holder is null)
            throw new InvalidOperationException(NotRegisteredMessage);
        return holder.Callbacks; // immutable snapshot — safe to copy out
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    public ValueTask NavigateAsync(string route, CancellationToken ct = default)
    {
        var cb = GetCallbacks();
        int rc = InvokeWithOneString(cb.Navigate, route);
        if (rc < 0)
            throw HostError("navigate", rc);
        return ValueTask.CompletedTask;
    }

    public ValueTask<string> GetCurrentRouteAsync(CancellationToken ct = default)
    {
        var cb = GetCallbacks();
        // CurrentRoute never legitimately answers -2/absent, so nullOnAbsent
        // is false and the null-forgiving result is safe.
        string route = InvokeBufferProtocol("current-route", cb.CurrentRoute, key: null, nullOnAbsent: false)!;
        return ValueTask.FromResult(route);
    }

    // ── Storage ───────────────────────────────────────────────────────────────

    public ValueTask<string?> ReadStorageAsync(string key, CancellationToken ct = default)
    {
        var cb = GetCallbacks();
        string? value = InvokeBufferProtocol("storage-read", cb.StorageRead, key, nullOnAbsent: true);
        return ValueTask.FromResult(value);
    }

    public ValueTask WriteStorageAsync(string key, string value, CancellationToken ct = default)
    {
        var cb = GetCallbacks();
        int rc = InvokeWithTwoStrings(cb.StorageWrite, key, value);
        if (rc < 0)
            throw HostError("storage-write", rc);
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteStorageAsync(string key, CancellationToken ct = default)
    {
        var cb = GetCallbacks();
        int rc = InvokeWithOneString(cb.StorageDelete, key);
        if (rc < 0)
            throw HostError("storage-delete", rc);
        return ValueTask.CompletedTask;
    }

    // ── Clipboard + Share (Phase 5.4 — size-negotiated slots) ─────────────────
    //
    // Each is guarded by RequireSlot: a null callback pointer (an old host that
    // predates the slot, left zero by the register min-copy + zero-fill) surfaces
    // as NotSupportedException, never a null-pointer call. ClipboardRead reuses
    // the -needed buffer protocol (CurrentRoute's twin); ClipboardWrite + Share
    // reuse the one-string invoker (Navigate's twin).

    public ValueTask<string> ClipboardReadAsync(CancellationToken ct = default)
    {
        var cb = GetCallbacks();
        RequireSlot(cb.ClipboardRead, "clipboard-read");
        // Clipboard never legitimately answers -2/absent — an empty clipboard is
        // the empty string (rc <= 1), so nullOnAbsent is false and the
        // null-forgiving result is safe.
        string text = InvokeBufferProtocol("clipboard-read", cb.ClipboardRead, key: null, nullOnAbsent: false)!;
        return ValueTask.FromResult(text);
    }

    public ValueTask ClipboardWriteAsync(string text, CancellationToken ct = default)
    {
        var cb = GetCallbacks();
        RequireSlot(cb.ClipboardWrite, "clipboard-write");
        int rc = InvokeWithOneString(cb.ClipboardWrite, text);
        if (rc < 0)
            throw HostError("clipboard-write", rc);
        return ValueTask.CompletedTask;
    }

    public ValueTask ShareAsync(string text, CancellationToken ct = default)
    {
        var cb = GetCallbacks();
        RequireSlot(cb.Share, "share");
        int rc = InvokeWithOneString(cb.Share, text);
        if (rc < 0)
            throw HostError("share", rc);
        return ValueTask.CompletedTask;
    }

    /// <summary>The null-slot guard that makes the size negotiation real: a
    /// callback the host never supplied (Zero after the register zero-fill) means
    /// the capability is unsupported by THIS host — a graceful
    /// <see cref="NotSupportedException"/>, never a crash into a null pointer.</summary>
    private static void RequireSlot(IntPtr fnPtr, string op)
    {
        if (fnPtr == IntPtr.Zero)
            throw new NotSupportedException(
                $"shell bridge '{op}' is not supported by this host (callback slot not registered)");
    }

    // ── Network (async completion pattern) ────────────────────────────────────

    public async ValueTask<BridgeHttpResponse> FetchAsync(BridgeHttpRequest request, CancellationToken ct = default)
    {
        _ = GetCallbacks(); // fail fast before allocating an id
        ct.ThrowIfCancellationRequested();

        long id = Interlocked.Increment(ref s_nextFetchId);
        var tcs = new TaskCompletionSource<BridgeHttpResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        s_pendingFetches[id] = tcs;

        try
        {
            BeginFetch(id, in request);
        }
        catch
        {
            s_pendingFetches.TryRemove(id, out _);
            throw;
        }

        // Registered AFTER FetchBegin: if the host completed synchronously the
        // id is already out of the table and cancellation is a no-op. On
        // cancel, whoever removes the id from the table wins — a completion
        // arriving after cancellation finds nothing and takes the unknown-id
        // path (CompleteFetch returns 1).
        using CancellationTokenRegistration ctr = ct.Register(static state =>
        {
            var (rid, pending, token) = ((long, TaskCompletionSource<BridgeHttpResponse>, CancellationToken))state!;
            if (s_pendingFetches.TryRemove(rid, out _))
                pending.TrySetCanceled(token);
        }, (id, tcs, ct));

        return await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>Marshals the request and calls the host's FetchBegin. The
    /// native struct + strings are freed when this returns — valid ONLY
    /// during the call, per the ABI lifetime rules. Separate non-async method
    /// because pointers can't live in an async body.</summary>
    private static unsafe void BeginFetch(long requestId, in BridgeHttpRequest request)
    {
        var cb = GetCallbacks();
        IntPtr url = Marshal.StringToCoTaskMemUTF8(request.Url);
        IntPtr method = Marshal.StringToCoTaskMemUTF8(request.Method);
        IntPtr body = request.Body is null ? IntPtr.Zero : Marshal.StringToCoTaskMemUTF8(request.Body);
        IntPtr headers = request.Headers is { Count: > 0 } h
            ? Marshal.StringToCoTaskMemUTF8(WriteFlatJsonObject(h))
            : IntPtr.Zero;
        try
        {
            var native = new BlazorNativeFetchRequest
            {
                Url = url,
                Method = method,
                Body = body,
                HeadersJson = headers,
            };
            int rc = ((delegate* unmanaged[Cdecl]<long, BlazorNativeFetchRequest*, int>)cb.FetchBegin)(requestId, &native);
            if (rc < 0)
                throw HostError("fetch-begin", rc);
        }
        finally
        {
            Marshal.FreeCoTaskMem(url);
            Marshal.FreeCoTaskMem(method);
            if (body != IntPtr.Zero) Marshal.FreeCoTaskMem(body);
            if (headers != IntPtr.Zero) Marshal.FreeCoTaskMem(headers);
        }
    }

    /// <summary>Managed core of blazornative_fetch_complete (same split as the
    /// DispatchEventNative/Core precedent). Copies everything out of the
    /// host-owned response before returning. Returns 0 = delivered,
    /// 1 = unknown/already-completed id (logged, ignored — cancellation race).
    /// Never throws.</summary>
    internal static int CompleteFetch(long requestId, in BlazorNativeFetchResponse response)
    {
        if (!s_pendingFetches.TryRemove(requestId, out var tcs))
        {
            Console.Error.WriteLine(
                $"[NativeShellBridge] fetch_complete for unknown/completed id {requestId} — ignored (cancellation race)");
            return 1;
        }

        try
        {
            if (response.Ok == 0)
            {
                string error = PtrToStringOrNull(response.ErrorMessage) ?? "shell fetch transport error";
                tcs.TrySetException(new HttpRequestException(error));
            }
            else
            {
                string bodyText = PtrToStringOrNull(response.BodyUtf8) ?? "";
                IReadOnlyDictionary<string, string> headers =
                    ParseFlatJsonObject(PtrToStringOrNull(response.HeadersJson));
                tcs.TrySetResult(new BridgeHttpResponse(response.StatusCode, bodyText, headers));
            }
        }
        catch (Exception ex)
        {
            // Malformed headers JSON etc. — fail the awaiting fetch, not the host.
            tcs.TrySetException(ex);
        }
        return 0;
    }

    // ── Geolocation (Phase 9.0 — the permission-gated async pattern) ──────────
    //
    // A permission-gated call is an async-begin (HostCallBegin) + a deferred
    // push-completion (host_call_complete), keyed by requestId, resolved off-lane —
    // structurally identical to fetch, with a permission prompt in the middle and a
    // tri-state instead of an HTTP response. Denial is DATA: every terminal outcome
    // (grant / denial / restriction / no-fix / error) is a completion with a status,
    // and the awaiting ValueTask ALWAYS resolves (or is cancelled by the caller's
    // token) — never an exception, never a hang.

    public async ValueTask<GeolocationResult> GetCurrentPositionAsync(CancellationToken ct = default)
    {
        HostCallResult result = await InvokeHostCallAsync(
            HostCallOp.Geolocation, GeolocationRequestArgs, ct).ConfigureAwait(false);
        return ParseGeolocationResult(in result);
    }

    public async ValueTask<GeolocationStatus> CheckGeolocationPermissionAsync(CancellationToken ct = default)
    {
        HostCallResult result = await InvokeHostCallAsync(
            HostCallOp.Geolocation, GeolocationCheckArgs, ct).ConfigureAwait(false);
        return ToGeolocationStatus(result.Status);
    }

    /// <summary>The generic permission-gated call: assign an id, park a TCS, call
    /// HostCallBegin, await the host's push-completion. The RequireSlot guard makes
    /// an old shell that predates the slot surface NotSupportedException rather than
    /// call a null pointer. Cancellation removes the id and cancels the task (a
    /// process killed during the prompt is the caller's token to abandon, never a
    /// leaked entry); a late/unknown completion takes the unknown-id path in
    /// CompleteHostCall (return 1, never a throw). The exact FetchAsync posture.</summary>
    private async ValueTask<HostCallResult> InvokeHostCallAsync(
        HostCallOp op, string argsJson, CancellationToken ct)
    {
        var cb = GetCallbacks();
        RequireSlot(cb.HostCallBegin, "host-call-begin");
        ct.ThrowIfCancellationRequested();

        long id = Interlocked.Increment(ref s_nextHostCallId);
        var tcs = new TaskCompletionSource<HostCallResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        s_pendingHostCalls[id] = tcs;

        try
        {
            BeginHostCall(id, (int)op, argsJson);
        }
        catch
        {
            s_pendingHostCalls.TryRemove(id, out _);
            throw;
        }

        // Registered AFTER BeginHostCall (the FetchAsync ordering): a synchronous
        // completion has already removed the id, so cancel is a no-op; on cancel
        // whoever removes the id wins, and a completion arriving after finds nothing
        // and takes the unknown-id path (CompleteHostCall returns 1).
        using CancellationTokenRegistration ctr = ct.Register(static state =>
        {
            var (rid, pending, token) = ((long, TaskCompletionSource<HostCallResult>, CancellationToken))state!;
            if (s_pendingHostCalls.TryRemove(rid, out _))
                pending.TrySetCanceled(token);
        }, (id, tcs, ct));

        return await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>Marshals the flat-JSON args and calls the host's HostCallBegin. The
    /// native string is freed when this returns — valid ONLY during the call, per the
    /// ABI lifetime rules. Separate non-async method because pointers can't live in an
    /// async body (the BeginFetch split).</summary>
    private static unsafe void BeginHostCall(long requestId, int op, string argsJson)
    {
        var cb = GetCallbacks();
        IntPtr args = Marshal.StringToCoTaskMemUTF8(argsJson);
        try
        {
            int rc = ((delegate* unmanaged[Cdecl]<long, int, IntPtr, int>)cb.HostCallBegin)(
                requestId, op, args);
            if (rc < 0)
                throw HostError("host-call-begin", rc);
        }
        finally
        {
            Marshal.FreeCoTaskMem(args);
        }
    }

    /// <summary>Managed core of blazornative_host_call_complete (the CompleteFetch
    /// twin). Removes the id and resolves the TCS with the wire status + payload —
    /// the status mapping to a typed enum happens on the AWAITING side, so this stays
    /// capability-agnostic. Returns 0 = delivered, 1 = unknown/already-completed id
    /// (logged, ignored — cancellation race). Never throws.</summary>
    internal static int CompleteHostCall(long requestId, int status, string? payloadJson)
    {
        if (!s_pendingHostCalls.TryRemove(requestId, out var tcs))
        {
            Console.Error.WriteLine(
                $"[NativeShellBridge] host_call_complete for unknown/completed id {requestId} — ignored (cancellation race)");
            return 1;
        }

        // A completion is NEVER a throw and NEVER a hang: even an out-of-range status
        // is delivered as data (the awaiting side maps it to Error). TrySetResult so a
        // duplicate completion after a cancellation race cannot fault the host.
        tcs.TrySetResult(new HostCallResult(status, payloadJson));
        return 0;
    }

    /// <summary>Maps the wire status integer to the typed tri-state; an out-of-range
    /// value (a host bug) becomes <see cref="GeolocationStatus.Error"/> — still data,
    /// never a throw.</summary>
    private static GeolocationStatus ToGeolocationStatus(int status)
        => status is >= (int)GeolocationStatus.Granted and <= (int)GeolocationStatus.Error
            ? (GeolocationStatus)status
            : GeolocationStatus.Error;

    /// <summary>Parses a completion into a typed result. Only a Granted status carries
    /// a fix (the flat-JSON payload, numbers string-encoded — the fetch-headers wire
    /// form); every other status is the status alone with a null position.</summary>
    private static GeolocationResult ParseGeolocationResult(in HostCallResult result)
    {
        GeolocationStatus status = ToGeolocationStatus(result.Status);
        if (status != GeolocationStatus.Granted)
            return new GeolocationResult(status, null);

        Dictionary<string, string> fix = ParseFlatJsonObject(result.PayloadJson);
        var position = new GeolocationPosition(
            Latitude: ParseWireDouble(fix, "lat"),
            Longitude: ParseWireDouble(fix, "lng"),
            Accuracy: ParseWireDouble(fix, "accuracy"),
            Altitude: fix.TryGetValue("altitude", out string? alt)
                && double.TryParse(alt, NumberStyles.Float, CultureInfo.InvariantCulture, out double a)
                ? a : null,
            TimestampUnixMs: fix.TryGetValue("timestamp", out string? ts)
                && long.TryParse(ts, NumberStyles.Integer, CultureInfo.InvariantCulture, out long t)
                ? t : 0);
        return new GeolocationResult(status, position);
    }

    private static double ParseWireDouble(IReadOnlyDictionary<string, string> fix, string key)
        => fix.TryGetValue(key, out string? v)
            && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
            ? d : 0.0;

    // ── Notifications (Phase 9.1 — the FIRST reuse of the 9.0 generic ABI) ────
    //
    // schedule / show / cancel + the permission (request / check) are each a
    // permission-gated host call — an outbound HostCallBegin(op:Notifications, …)
    // the host completes with a NotificationStatus. They ride the EXISTING
    // InvokeHostCallAsync verbatim (the pending registry, the cancellation posture,
    // the unknown-id benignness, the old-shell RequireSlot guard — all reused, none
    // re-written), so the ONLY new .NET is the args-JSON builders + the status
    // mapping. NO struct grow, NO new export — the op-enum value + JSON vocabulary
    // is the whole ABI touch. The action lives INSIDE the flat JSON (geolocation's
    // `mode` precedent — one op, many sub-actions, no second slot). The completion
    // carries NO payload for schedule/show/cancel (a status is the whole answer).

    public async ValueTask<NotificationStatus> ScheduleNotificationAsync(NotificationSpec spec, CancellationToken ct = default)
    {
        HostCallResult result = await InvokeHostCallAsync(
            HostCallOp.Notifications, BuildNotificationArgs("schedule", spec), ct).ConfigureAwait(false);
        return ToNotificationStatus(result.Status);
    }

    public async ValueTask<NotificationStatus> ShowNotificationAsync(NotificationSpec spec, CancellationToken ct = default)
    {
        HostCallResult result = await InvokeHostCallAsync(
            HostCallOp.Notifications, BuildNotificationArgs("show", spec), ct).ConfigureAwait(false);
        return ToNotificationStatus(result.Status);
    }

    public async ValueTask<NotificationStatus> CancelNotificationAsync(int id, CancellationToken ct = default)
    {
        HostCallResult result = await InvokeHostCallAsync(
            HostCallOp.Notifications, BuildNotificationActionArgs("cancel", id), ct).ConfigureAwait(false);
        return ToNotificationStatus(result.Status);
    }

    public async ValueTask<NotificationStatus> RequestNotificationPermissionAsync(CancellationToken ct = default)
    {
        HostCallResult result = await InvokeHostCallAsync(
            HostCallOp.Notifications, NotificationRequestArgs, ct).ConfigureAwait(false);
        return ToNotificationStatus(result.Status);
    }

    public async ValueTask<NotificationStatus> CheckNotificationPermissionAsync(CancellationToken ct = default)
    {
        HostCallResult result = await InvokeHostCallAsync(
            HostCallOp.Notifications, NotificationCheckArgs, ct).ConfigureAwait(false);
        return ToNotificationStatus(result.Status);
    }

    // The permission calls carry only the action — the request-then-note-a-denial
    // dance (may prompt) and the read-only check (never prompts) on the SAME op,
    // geolocation's mode precedent verbatim.
    private const string NotificationRequestArgs = "{\"action\":\"request\"}";
    private const string NotificationCheckArgs = "{\"action\":\"check\"}";

    /// <summary>Builds the flat-JSON args for schedule/show: action + the spec's
    /// fields, EVERY field a string (numbers string-encoded, InvariantCulture — the
    /// geolocation fix-key discipline; `id` decimal, `when` Unix epoch
    /// MILLISECONDS). `when`/`route` are omitted when absent (show carries no
    /// `when`; a routeless notification carries no `route`). Reuses the existing
    /// WriteFlatJsonObject serializer — no new serializer, and its escaping keeps a
    /// title/body with a quote or newline wire-safe.</summary>
    private static string BuildNotificationArgs(string action, NotificationSpec spec)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["action"] = action,
            ["id"] = spec.Id.ToString(CultureInfo.InvariantCulture),
            ["title"] = spec.Title,
            ["body"] = spec.Body,
        };
        if (spec.When is { } when)
            map["when"] = when.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        if (spec.Route is { } route)
            map["route"] = route;
        return WriteFlatJsonObject(map);
    }

    /// <summary>Builds the flat-JSON args for an id-only action (cancel).</summary>
    private static string BuildNotificationActionArgs(string action, int id)
        => WriteFlatJsonObject(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["action"] = action,
            ["id"] = id.ToString(CultureInfo.InvariantCulture),
        });

    /// <summary>Maps the wire status integer to the typed status; an out-of-range
    /// value (a host bug) becomes <see cref="NotificationStatus.Error"/> — still
    /// data, never a throw (the ToGeolocationStatus twin).</summary>
    private static NotificationStatus ToNotificationStatus(int status)
        => status is >= (int)NotificationStatus.Granted and <= (int)NotificationStatus.Error
            ? (NotificationStatus)status
            : NotificationStatus.Error;

    // ── Biometrics + secure storage (Phase 9.2 — the SECOND reuse of the 9.0 ABI) ─
    //
    // TWO ops (Biometrics = 2, SecureStorage = 3), both riding the EXISTING
    // InvokeHostCallAsync verbatim (the pending registry, the cancellation posture,
    // the unknown-id benignness, the old-shell RequireSlot guard — all reused, none
    // re-written). The ONLY new .NET is the args-JSON builders, the two status maps
    // (incl. out-of-range → Error), and the SecretResult parse (the value in the
    // {"value":…} payload on Ok — the ParseGeolocationResult twin). NO struct grow,
    // NO new export — the op-enum values + JSON vocabulary are the whole ABI touch.
    // The action lives INSIDE the flat JSON (geolocation's `mode` precedent). Denial
    // is DATA: every terminal outcome resolves the ValueTask with a status.

    public async ValueTask<BiometricStatus> AuthenticateAsync(string reason, CancellationToken ct = default)
    {
        HostCallResult result = await InvokeHostCallAsync(
            HostCallOp.Biometrics, BuildBiometricArgs("authenticate", reason), ct).ConfigureAwait(false);
        return ToBiometricStatus(result.Status);
    }

    public async ValueTask<BiometricStatus> IsBiometricAvailableAsync(CancellationToken ct = default)
    {
        // The read-only availability check (never prompts) — geolocation's `check`
        // sibling. Authenticated means "present + enrolled + ready" (no auth ran).
        HostCallResult result = await InvokeHostCallAsync(
            HostCallOp.Biometrics, BiometricCheckArgs, ct).ConfigureAwait(false);
        return ToBiometricStatus(result.Status);
    }

    public async ValueTask<SecureStorageStatus> SetSecretAsync(string key, string value, bool requireAuth, CancellationToken ct = default)
    {
        // The soft 8 KB cap, enforced at THIS .NET boundary: an oversize value
        // RETURNS a status and never crosses the wire — a large value is a misuse,
        // not a store (§8). Never a crash, never a hang.
        if (Encoding.UTF8.GetByteCount(value) > SecretResult.MaxValueBytes)
            return SecureStorageStatus.Error;

        HostCallResult result = await InvokeHostCallAsync(
            HostCallOp.SecureStorage, BuildSecretSetArgs(key, value, requireAuth), ct).ConfigureAwait(false);
        return ToSecureStorageStatus(result.Status);
    }

    public async ValueTask<SecretResult> GetSecretAsync(string key, CancellationToken ct = default)
    {
        HostCallResult result = await InvokeHostCallAsync(
            HostCallOp.SecureStorage, BuildSecretGetArgs("get", key, reason: null), ct).ConfigureAwait(false);
        return ParseSecretResult(in result);
    }

    public async ValueTask<SecretResult> GetSecretWithAuthAsync(string key, string reason, CancellationToken ct = default)
    {
        // THE PAIRING: getWithAuth triggers the OS biometric prompt host-side and the
        // OS-key-bound decrypt; the plaintext returns in the SAME {"value":…} payload
        // as plain get. A denied/failed gate returns AuthFailed (no value) — the OS
        // refuses the plaintext, and that refusal is DATA.
        HostCallResult result = await InvokeHostCallAsync(
            HostCallOp.SecureStorage, BuildSecretGetArgs("getWithAuth", key, reason), ct).ConfigureAwait(false);
        return ParseSecretResult(in result);
    }

    public async ValueTask<SecureStorageStatus> DeleteSecretAsync(string key, CancellationToken ct = default)
    {
        HostCallResult result = await InvokeHostCallAsync(
            HostCallOp.SecureStorage, BuildSecretDeleteArgs(key), ct).ConfigureAwait(false);
        return ToSecureStorageStatus(result.Status);
    }

    // The biometric availability check carries only the action (the geolocation
    // `mode:"check"` / notifications `action:"check"` precedent, never prompts).
    private const string BiometricCheckArgs = "{\"action\":\"check\"}";

    /// <summary>Builds the flat-JSON args for a biometric action carrying a prompt
    /// reason (authenticate). Reuses WriteFlatJsonObject — the escaping keeps a
    /// reason with a quote or newline wire-safe.</summary>
    private static string BuildBiometricArgs(string action, string reason)
        => WriteFlatJsonObject(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["action"] = action,
            ["reason"] = reason,
        });

    /// <summary>Builds the flat-JSON args for a secure `set`: action + key + value +
    /// the auth flag ("0"/"1"). An auth-bound write (auth:"1") provisions the item
    /// under the OS-key auth binding so a matching getWithAuth can decrypt it (§4c).</summary>
    private static string BuildSecretSetArgs(string key, string value, bool requireAuth)
        => WriteFlatJsonObject(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["action"] = "set",
            ["key"] = key,
            ["value"] = value,
            ["auth"] = requireAuth ? "1" : "0",
        });

    /// <summary>Builds the flat-JSON args for a secure `get`/`getWithAuth`: action +
    /// key, plus the prompt reason for getWithAuth (the OS biometric sheet's message).</summary>
    private static string BuildSecretGetArgs(string action, string key, string? reason)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["action"] = action,
            ["key"] = key,
        };
        if (reason is not null)
            map["reason"] = reason;
        return WriteFlatJsonObject(map);
    }

    /// <summary>Builds the flat-JSON args for a secure `delete` (key only).</summary>
    private static string BuildSecretDeleteArgs(string key)
        => WriteFlatJsonObject(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["action"] = "delete",
            ["key"] = key,
        });

    /// <summary>Maps the wire status integer to <see cref="BiometricStatus"/>; an
    /// out-of-range value (a host bug) becomes <see cref="BiometricStatus.Error"/> —
    /// still data, never a throw (the ToGeolocationStatus twin).</summary>
    private static BiometricStatus ToBiometricStatus(int status)
        => status is >= (int)BiometricStatus.Authenticated and <= (int)BiometricStatus.Error
            ? (BiometricStatus)status
            : BiometricStatus.Error;

    /// <summary>Maps the wire status integer to <see cref="SecureStorageStatus"/>;
    /// an out-of-range value becomes <see cref="SecureStorageStatus.Error"/>.</summary>
    private static SecureStorageStatus ToSecureStorageStatus(int status)
        => status is >= (int)SecureStorageStatus.Ok and <= (int)SecureStorageStatus.Error
            ? (SecureStorageStatus)status
            : SecureStorageStatus.Error;

    /// <summary>Parses a completion into a typed <see cref="SecretResult"/>. Only an
    /// Ok status carries a value (the flat-JSON {"value":…} payload — the SECOND user
    /// of the payload channel geolocation's fix opened, proving it is generic, not
    /// geolocation-specific); every other status is the status alone with a null
    /// value. Reading the wrong payload key is the named get-parse mutation.</summary>
    private static SecretResult ParseSecretResult(in HostCallResult result)
    {
        SecureStorageStatus status = ToSecureStorageStatus(result.Status);
        if (status != SecureStorageStatus.Ok)
            return new SecretResult(status, null);

        Dictionary<string, string> payload = ParseFlatJsonObject(result.PayloadJson);
        return new SecretResult(status, payload.TryGetValue("value", out string? v) ? v : null);
    }

    // ── Camera (Phase 9.3 — the THIRD reuse of the 9.0 ABI, the last M9 capability) ─
    //
    // capture / check ride the EXISTING InvokeHostCallAsync verbatim (the pending
    // registry, the cancellation posture, the unknown-id benignness, the old-shell
    // RequireSlot guard — all reused, none re-written) with op=Camera and the action
    // inside the flat JSON (geolocation's `mode` precedent). The ONLY new .NET is the
    // args-JSON builders, the ToCameraStatus map (incl. out-of-range → Error), and the
    // PhotoResult parse. NO struct grow, NO new export — the op-enum value + JSON
    // vocabulary are the whole ABI touch.
    //
    // THE NEW SHAPE: the result is a LARGE artifact (a photo) handed by REFERENCE — a
    // file PATH the shell wrote — not by value. On Captured the payload is
    // {"path":"file:///…","width":…,"height":…,"bytes":…}: the SAME optional flat-JSON
    // payload channel host_call_complete has carried since 9.0 (geolocation's fix, then
    // secure get's value), used here to NAME a file rather than carry its contents. The
    // bytes never cross the C-ABI, so the image being large does not make the MESSAGE
    // large — which is exactly why the ABI stays frozen (§0/§2 of the design). Denial
    // is DATA: cancel / denied / unavailable / error each resolve the ValueTask with a
    // status, no throw, no hang.

    public async ValueTask<PhotoResult> CapturePhotoAsync(CaptureOptions options, CancellationToken ct = default)
    {
        HostCallResult result = await InvokeHostCallAsync(
            HostCallOp.Camera, BuildCaptureArgs(options), ct).ConfigureAwait(false);
        return ParsePhotoResult(in result);
    }

    public async ValueTask<CameraStatus> CheckCameraAvailabilityAsync(CancellationToken ct = default)
    {
        // The read-only availability check (never prompts, never launches the camera
        // UI) — geolocation's `check` sibling. Captured means "present + usable" (no
        // capture ran); Unavailable is the honest no-camera answer (the iOS simulator).
        HostCallResult result = await InvokeHostCallAsync(
            HostCallOp.Camera, CameraCheckArgs, ct).ConfigureAwait(false);
        return ToCameraStatus(result.Status);
    }

    // The availability check carries only the action (the geolocation `mode:"check"` /
    // notifications-and-biometrics `action:"check"` precedent, never prompts/launches).
    private const string CameraCheckArgs = "{\"action\":\"check\"}";

    /// <summary>Builds the flat-JSON args for a capture: action + the downscale cap +
    /// the JPEG re-encode quality, EVERY field a string (numbers string-encoded,
    /// InvariantCulture — the geolocation fix-key discipline). The shell downsamples
    /// the full-resolution capture to `maxDim` on the long edge and re-encodes at
    /// `quality` before writing the temp file (§2d). Reuses WriteFlatJsonObject — no
    /// new serializer.</summary>
    private static string BuildCaptureArgs(CaptureOptions options)
        => WriteFlatJsonObject(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["action"] = "capture",
            ["maxDim"] = options.MaxDimension.ToString(CultureInfo.InvariantCulture),
            ["quality"] = options.Quality.ToString(CultureInfo.InvariantCulture),
        });

    /// <summary>Maps the wire status integer to <see cref="CameraStatus"/>; an
    /// out-of-range value (a host bug) becomes <see cref="CameraStatus.Error"/> —
    /// still data, never a throw (the ToGeolocationStatus twin).</summary>
    private static CameraStatus ToCameraStatus(int status)
        => status is >= (int)CameraStatus.Captured and <= (int)CameraStatus.Error
            ? (CameraStatus)status
            : CameraStatus.Error;

    /// <summary>Parses a completion into a typed <see cref="PhotoResult"/>. Only a
    /// Captured status carries a file — the flat-JSON {"path":…,"width":…,"height":…,
    /// "bytes":…} payload NAMING the temp JPEG (a LARGE artifact by REFERENCE, the
    /// THIRD user of the 9.0 payload channel — geolocation's fix and secure get's value
    /// are the first two); every other status is the status alone with a null path and
    /// zero dims. Reading the wrong payload key (e.g. `file` for `path`, or a
    /// bytes-inline `data`) is the named composition-reds mutation.</summary>
    private static PhotoResult ParsePhotoResult(in HostCallResult result)
    {
        CameraStatus status = ToCameraStatus(result.Status);
        if (status != CameraStatus.Captured)
            return new PhotoResult(status, null, 0, 0, 0);

        Dictionary<string, string> payload = ParseFlatJsonObject(result.PayloadJson);
        return new PhotoResult(
            status,
            Path: payload.TryGetValue("path", out string? p) ? p : null,
            Width: ParseWireInt(payload, "width"),
            Height: ParseWireInt(payload, "height"),
            SizeBytes: ParseWireLong(payload, "bytes"));
    }

    private static int ParseWireInt(IReadOnlyDictionary<string, string> map, string key)
        => map.TryGetValue(key, out string? v)
            && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
            ? n : 0;

    private static long ParseWireLong(IReadOnlyDictionary<string, string> map, string key)
        => map.TryGetValue(key, out string? v)
            && long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n)
            ? n : 0;

    // ── Platform info ─────────────────────────────────────────────────────────
    //
    // Exports.Init stores its options (os / apiLevel / note / kind); the bridge
    // serves them. Pragmatic representation mirroring DevHostBridge's shape.
    // Phase 10.0 (#121): Kind is the shell's real PlatformKind (host-supplied
    // through the init options), NOT a hardcoded constant — the same runtime is
    // linked into every native shell, so hardcoding Android made iOS lie.

    public string PlatformInfo
    {
        get
        {
            PlatformOptions? opts = Volatile.Read(ref s_platformOptions);
            if (opts is null)
                return "{}"; // Init not called yet — same "{}" fallback the WASM-era WasiBridge used (deleted Phase 3.2)

            var sb = new StringBuilder(96);
            sb.Append("{\"kind\":\"").Append(opts.Kind).Append("\",\"os\":");
            AppendJsonString(sb, opts.Os);
            sb.Append(",\"apiLevel\":").Append(opts.ApiLevel);
            if (opts.Note is not null)
            {
                sb.Append(",\"note\":");
                AppendJsonString(sb, opts.Note);
            }
            sb.Append(",\"version\":");
            AppendJsonString(sb, Exports.VersionNumber);
            sb.Append(",\"isDebug\":false}");
            return sb.ToString();
        }
    }

    public ValueTask<PlatformInfo> GetPlatformInfoAsync(CancellationToken ct = default)
    {
        PlatformOptions? opts = Volatile.Read(ref s_platformOptions);
        return ValueTask.FromResult(new PlatformInfo(
            // Phase 10.0 (#121): serve the shell's real kind. No options yet (Init
            // not called) → DevHost, the same safe non-lying default an un-updated
            // shell's ordinal 0 maps to — never Android.
            opts?.Kind ?? PlatformKind.DevHost,
            OsVersion: opts?.Os ?? "",
            AppVersion: Exports.VersionNumber,
            IsDebug: false));
    }

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Real host→.NET lifecycle event ingress (Phase 5.1, closing the
    /// M5 DoD #5 fork the 3.2 no-op deferred). The 9th export
    /// blazornative_host_event → <see cref="Exports.DispatchHostEventCore"/>
    /// raises this; a mounted component (HostEventProbe, the on-device consumer)
    /// subscribes with a SYNC handler (BN0014 contract intact — the dispatch
    /// window is still synchronous). Static invocation list: one bridge per
    /// process, so the DI singleton components inject and the export both fire
    /// through the same subscribers.</summary>
    public event Action<NativeEvent> NativeEvents
    {
        add    => s_nativeEvents += value;
        remove => s_nativeEvents -= value;
    }

    /// <summary>Fires <see cref="NativeEvents"/> with per-subscriber isolation
    /// (the DevHostBridge.RaiseNativeEvent pattern): GetInvocationList + a
    /// per-subscriber try/catch so one faulting subscriber is stderr-logged and
    /// the remaining subscribers still run — the host event is still DELIVERED.
    /// Returns true if ANY subscriber (or the re-render its StateHasChanged
    /// drove, when strict rethrows it) faulted, so
    /// <see cref="Exports.DispatchHostEventCore"/> can surface it as export
    /// rc 2 for host (logcat) visibility. Strict-posture note: containment wins
    /// over abort — a lifecycle-handler bug must not strand later subscribers —
    /// but unlike RouteChanged (a post-success notification, always rc 0) a
    /// host-event fault IS reported up the rc channel because the host owns the
    /// lifecycle signal and wants to know its consumer choked.</summary>
    internal static bool RaiseNativeEvent(NativeEvent evt)
    {
        Action<NativeEvent>? handlers = s_nativeEvents;
        if (handlers is null)
            return false;

        bool faulted = false;
        foreach (Delegate subscriber in handlers.GetInvocationList())
        {
            try
            {
                ((Action<NativeEvent>)subscriber)(evt);
            }
            catch (Exception ex)
            {
                // ex.ToString(): the subscriber (and any re-render it drove) is
                // app code — keep its stack for logcat, same as RouteChanged.
                faulted = true;
                Console.Error.WriteLine(
                    $"[NativeShellBridge] NativeEvents subscriber threw: {ex}");
            }
        }
        return faulted;
    }

    // ── Callback invokers ─────────────────────────────────────────────────────

    private static unsafe int InvokeWithOneString(IntPtr fnPtr, string arg)
    {
        byte[] argZ = NulTerminatedUtf8(arg);
        fixed (byte* a = argZ)
            return ((delegate* unmanaged[Cdecl]<byte*, int>)fnPtr)(a);
    }

    private static unsafe int InvokeWithTwoStrings(IntPtr fnPtr, string arg1, string arg2)
    {
        byte[] arg1Z = NulTerminatedUtf8(arg1);
        byte[] arg2Z = NulTerminatedUtf8(arg2);
        fixed (byte* a1 = arg1Z)
        fixed (byte* a2 = arg2Z)
            return ((delegate* unmanaged[Cdecl]<byte*, byte*, int>)fnPtr)(a1, a2);
    }

    /// <summary>The .NET half of the buffer protocol: rent 4 KB, call; on
    /// -needed (only meaningful when |rc| exceeds the offered cap) retry ONCE
    /// at the exact demanded size; a second failure throws. -2 maps to null
    /// when <paramref name="nullOnAbsent"/> (StorageRead); -1/-2 otherwise
    /// throw.</summary>
    private static string? InvokeBufferProtocol(string opName, IntPtr fnPtr, string? key, bool nullOnAbsent)
    {
        byte[]? keyZ = key is null ? null : NulTerminatedUtf8(key);

        byte[] buffer = ArrayPool<byte>.Shared.Rent(DefaultBufferSize);
        try
        {
            int rc = CallBufferFn(fnPtr, keyZ, buffer, DefaultBufferSize);
            if (rc >= 0)
                return DecodeBuffer(buffer, rc);

            int needed = -rc;
            if (needed <= DefaultBufferSize)
            {
                // Not a size demand — an error code.
                if (rc == -2 && nullOnAbsent)
                    return null;
                throw HostError(opName, rc);
            }
            if (needed > MaxRetryBytes)
                throw new InvalidOperationException(
                    $"shell bridge '{opName}' demanded a {needed}-byte buffer (host returned {rc}) — " +
                    $"exceeds the {MaxRetryBytes}-byte retry ceiling; the host is misbehaving");

            // Retry once at the exact demanded size.
            byte[] retryBuffer = ArrayPool<byte>.Shared.Rent(needed);
            try
            {
                rc = CallBufferFn(fnPtr, keyZ, retryBuffer, needed);
                if (rc >= 0)
                    return DecodeBuffer(retryBuffer, rc);
                if (rc == -2 && nullOnAbsent)
                    return null; // value vanished between the two calls
                throw new InvalidOperationException(
                    $"shell bridge '{opName}' still failed after the buffer retry at {needed} bytes (return code {rc})");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(retryBuffer);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static unsafe int CallBufferFn(IntPtr fnPtr, byte[]? keyZ, byte[] buffer, int cap)
    {
        fixed (byte* buf = buffer)
        {
            if (keyZ is null)
                return ((delegate* unmanaged[Cdecl]<byte*, int, int>)fnPtr)(buf, cap);
            fixed (byte* k = keyZ)
                return ((delegate* unmanaged[Cdecl]<byte*, byte*, int, int>)fnPtr)(k, buf, cap);
        }
    }

    /// <summary>rc = bytes written INCLUDING the NUL terminator.</summary>
    private static string DecodeBuffer(byte[] buffer, int rc)
        => rc <= 1 ? "" : Encoding.UTF8.GetString(buffer, 0, rc - 1);

    private static byte[] NulTerminatedUtf8(string value)
    {
        int byteCount = Encoding.UTF8.GetByteCount(value);
        byte[] bytes = new byte[byteCount + 1]; // trailing 0 from array init
        Encoding.UTF8.GetBytes(value, 0, value.Length, bytes, 0);
        return bytes;
    }

    private static string? PtrToStringOrNull(IntPtr ptr)
        => ptr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(ptr);

    private static InvalidOperationException HostError(string opName, int rc)
        => new($"shell bridge '{opName}' failed with return code {rc}");

    // ── Flat JSON object (headers) — hand-written, no serializer ─────────────
    // The ABI ships headers as a single flat {"string":"string",...} object.
    // Header names are case-insensitive per RFC 9110, hence the comparer.
    //
    // Kotlin mirror: `FlatJson` in
    // src/BlazorNative.Jni/src/main/kotlin/io/blazornative/jni/ShellBridge.kt —
    // if you change the escaping/parsing rules here, change them there too;
    // both sides assert the same content matrix (FlatJsonTests.cs /
    // ShellBridgeTest.kt).

    internal static string WriteFlatJsonObject(IReadOnlyDictionary<string, string> map)
    {
        var sb = new StringBuilder(16 + map.Count * 24);
        sb.Append('{');
        bool first = true;
        foreach ((string key, string value) in map)
        {
            if (!first) sb.Append(',');
            first = false;
            AppendJsonString(sb, key);
            sb.Append(':');
            AppendJsonString(sb, value);
        }
        sb.Append('}');
        return sb.ToString();
    }

    internal static Dictionary<string, string> ParseFlatJsonObject(string? json)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
            return result;

        int i = 0;
        SkipWhitespace(json, ref i);
        Expect(json, ref i, '{');
        SkipWhitespace(json, ref i);
        if (i < json.Length && json[i] == '}')
            return result;

        while (true)
        {
            SkipWhitespace(json, ref i);
            string key = ParseJsonString(json, ref i);
            SkipWhitespace(json, ref i);
            Expect(json, ref i, ':');
            SkipWhitespace(json, ref i);
            string value = ParseJsonString(json, ref i);
            result[key] = value;
            SkipWhitespace(json, ref i);
            if (i >= json.Length)
                throw Malformed(json, i);
            char c = json[i++];
            if (c == '}')
                return result;
            if (c != ',')
                throw Malformed(json, i - 1);
        }
    }

    private static void AppendJsonString(StringBuilder sb, string value)
    {
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    private static string ParseJsonString(string json, ref int i)
    {
        Expect(json, ref i, '"');
        var sb = new StringBuilder();
        while (true)
        {
            if (i >= json.Length)
                throw Malformed(json, i);
            char c = json[i++];
            if (c == '"')
                return sb.ToString();
            if (c != '\\')
            {
                sb.Append(c);
                continue;
            }
            if (i >= json.Length)
                throw Malformed(json, i);
            char escape = json[i++];
            sb.Append(escape switch
            {
                '"' => '"',
                '\\' => '\\',
                '/' => '/',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                'b' => '\b',
                'f' => '\f',
                'u' => ParseHex4(json, ref i),
                _ => throw Malformed(json, i - 1),
            });
        }
    }

    /// <summary>Strict \uXXXX: exactly four hex digits, no sign/whitespace
    /// leniency (int.Parse with NumberStyles.HexNumber would tolerate
    /// leading/trailing whitespace inside the span).</summary>
    private static char ParseHex4(string json, ref int i)
    {
        if (i + 4 > json.Length)
            throw Malformed(json, i);
        int value = 0;
        for (int k = 0; k < 4; k++)
        {
            char c = json[i + k];
            int digit = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'f' => c - 'a' + 10,
                >= 'A' and <= 'F' => c - 'A' + 10,
                _ => -1,
            };
            if (digit < 0)
                throw Malformed(json, i + k);
            value = (value << 4) | digit;
        }
        i += 4;
        return (char)value;
    }

    private static void SkipWhitespace(string json, ref int i)
    {
        while (i < json.Length && char.IsWhiteSpace(json[i]))
            i++;
    }

    private static void Expect(string json, ref int i, char expected)
    {
        if (i >= json.Length || json[i] != expected)
            throw Malformed(json, i);
        i++;
    }

    /// <summary>Deliberately does NOT embed the full JSON: headers may carry
    /// Set-Cookie/Authorization values that must not leak into logs. Reports
    /// the failing character index plus a 32-char prefix only.</summary>
    private static FormatException Malformed(string json, int index)
    {
        string prefix = json.Length <= 32 ? json : json[..32] + "…";
        return new FormatException(
            $"malformed flat JSON object from the shell bridge at index {index} (prefix: '{prefix}')");
    }
}
