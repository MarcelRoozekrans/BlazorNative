using System.Buffers;
using System.Collections.Concurrent;
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
    /// can never observe a torn 48-byte struct during re-registration — an
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

    /// <summary>Real host→.NET event multicast (Phase 5.1, replacing the 3.2
    /// no-op). Static, like every other bridge field: the C ABI has one bridge
    /// per process, so components resolving the DI singleton and the
    /// blazornative_host_event export both address THIS invocation list.
    /// Single-threaded post-boot (the dispatch lane), but a bare field is fine —
    /// += / -= produce a new immutable delegate the raiser snapshots.</summary>
    private static Action<NativeEvent>? s_nativeEvents;

    // Init-time platform options (Exports.Init stores them; PlatformInfo serves them).
    private sealed record PlatformOptions(string Os, int ApiLevel, string? Note);
    private static PlatformOptions? s_platformOptions;

    // ── Registration (blazornative_register_bridge / Exports.Init plumbing) ──

    /// <summary>Copies the host's callback struct into a fresh immutable
    /// holder and swaps the reference atomically. Re-registration is allowed
    /// (last wins) — same posture as the frame callback.</summary>
    internal static void Register(in BlazorNativeBridgeCallbacks callbacks)
        => Volatile.Write(ref s_callbacks, new CallbackHolder(in callbacks));

    /// <summary>Stores Init's platform options; <see cref="PlatformInfo"/> and
    /// <see cref="GetPlatformInfoAsync"/> serve them.</summary>
    internal static void SetPlatformInfo(string os, int apiLevel, string? note)
        => Volatile.Write(ref s_platformOptions, new PlatformOptions(os, apiLevel, note));

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

    // ── Platform info ─────────────────────────────────────────────────────────
    //
    // Exports.Init stores its options (os / apiLevel / note); the bridge
    // serves them. Pragmatic representation mirroring DevHostBridge's shape.
    // Kind is Android — the only native shell registering this bridge in M3.

    public string PlatformInfo
    {
        get
        {
            PlatformOptions? opts = Volatile.Read(ref s_platformOptions);
            if (opts is null)
                return "{}"; // Init not called yet — same "{}" fallback the WASM-era WasiBridge used (deleted Phase 3.2)

            var sb = new StringBuilder(96);
            sb.Append("{\"kind\":\"Android\",\"os\":");
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
            PlatformKind.Android,
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
