using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using BlazorNative.Core;
using BlazorNative.Runtime;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3.1 Gate 1 — NativeShellBridge behavior on the host CLR.
//
// Exercises the REAL function-pointer path: Register receives a struct of
// [UnmanagedCallersOnly] cdecl method addresses (exactly what JNA hands
// blazornative_register_bridge), and the bridge's six IMobileBridge ops must
// invoke them honoring the return-code protocol (>= 0 / -needed / -1 / -2 —
// see BridgeProtocolNative.cs).
//
// The full cross-language path (JNA callback objects → dll → Kotlin handlers)
// is covered by the Gate 2 JVM test (ShellBridgeTest.kt).
//
// State note: NativeShellBridge holds process-wide static state (registered
// callbacks + pending-fetch table), and FakeShellHost is static too — so
// every test class touching either serializes via the shared "host-session"
// xUnit collection (Phase 3.5 merged the former "native-shell-bridge"
// collection into it: NavigationTests spans BOTH singletons, and separate
// collections run in parallel), and each test resets the bridge in finally.
// ─────────────────────────────────────────────────────────────────────────────

// ── FakeShellHost — managed stand-in for the Kotlin host ────────────────────
// Static state + [UnmanagedCallersOnly] methods whose addresses populate a
// real BlazorNativeBridgeCallbacks struct (HostSessionTests pattern).
internal static unsafe class FakeShellHost
{
    public static string Route = "/";
    public static readonly Dictionary<string, string> Store = new();

    /// <summary>When true, CurrentRoute always answers "buffer too small"
    /// with a needed-size larger than whatever cap it was given — drives the
    /// bridge's second-retry-fails path.</summary>
    public static bool CurrentRouteAlwaysTooSmall;
    public static int CurrentRouteCallCount;

    public static long LastFetchRequestId = -1;
    public static string? LastFetchUrl;
    public static string? LastFetchMethod;
    public static string? LastFetchBody;
    public static string? LastFetchHeadersJson;
    public static int FetchBeginReturnCode;

    // When set, FetchBegin completes the fetch synchronously via
    // NativeShellBridge.CompleteFetch with the canned response below —
    // scripts the host side for the probe-runner and HttpClient E2E tests.
    public static bool AutoCompleteFetch;
    public static bool AutoCompleteOk = true;
    public static int AutoCompleteStatus = 200;
    public static string AutoCompleteBody = "";
    public static string AutoCompleteError = "fake transport error";
    public static string? AutoCompleteHeadersJson;

    public static void Reset()
    {
        Route = "/";
        Store.Clear();
        CurrentRouteAlwaysTooSmall = false;
        CurrentRouteCallCount = 0;
        LastFetchRequestId = -1;
        LastFetchUrl = LastFetchMethod = LastFetchBody = LastFetchHeadersJson = null;
        FetchBeginReturnCode = 0;
        AutoCompleteFetch = false;
        AutoCompleteOk = true;
        AutoCompleteStatus = 200;
        AutoCompleteBody = "";
        AutoCompleteError = "fake transport error";
        AutoCompleteHeadersJson = null;
    }

    public static BlazorNativeBridgeCallbacks BuildCallbacks() => new()
    {
        Navigate      = (IntPtr)(delegate* unmanaged[Cdecl]<byte*, int>)&NavigateFn,
        CurrentRoute  = (IntPtr)(delegate* unmanaged[Cdecl]<byte*, int, int>)&CurrentRouteFn,
        StorageRead   = (IntPtr)(delegate* unmanaged[Cdecl]<byte*, byte*, int, int>)&StorageReadFn,
        StorageWrite  = (IntPtr)(delegate* unmanaged[Cdecl]<byte*, byte*, int>)&StorageWriteFn,
        StorageDelete = (IntPtr)(delegate* unmanaged[Cdecl]<byte*, int>)&StorageDeleteFn,
        FetchBegin    = (IntPtr)(delegate* unmanaged[Cdecl]<long, BlazorNativeFetchRequest*, int>)&FetchBeginFn,
    };

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int NavigateFn(byte* routeUtf8)
    {
        string? route = Marshal.PtrToStringUTF8((IntPtr)routeUtf8);
        if (route is null) return -1;
        Route = route; // copy — the pointer dies when we return
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int CurrentRouteFn(byte* buf, int cap)
    {
        CurrentRouteCallCount++;
        if (CurrentRouteAlwaysTooSmall)
            return -(cap + 1); // always demand more than offered
        return WriteUtf8(Route, buf, cap);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int StorageReadFn(byte* keyUtf8, byte* buf, int cap)
    {
        string? key = Marshal.PtrToStringUTF8((IntPtr)keyUtf8);
        if (key is null) return -1;
        if (!Store.TryGetValue(key, out string? value)) return -2;
        return WriteUtf8(value, buf, cap);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int StorageWriteFn(byte* keyUtf8, byte* valueUtf8)
    {
        string? key = Marshal.PtrToStringUTF8((IntPtr)keyUtf8);
        string? value = Marshal.PtrToStringUTF8((IntPtr)valueUtf8);
        if (key is null || value is null) return -1;
        Store[key] = value;
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int StorageDeleteFn(byte* keyUtf8)
    {
        string? key = Marshal.PtrToStringUTF8((IntPtr)keyUtf8);
        if (key is null) return -1;
        Store.Remove(key);
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int FetchBeginFn(long requestId, BlazorNativeFetchRequest* req)
    {
        // Copy everything — the request struct + strings die when we return.
        LastFetchRequestId = requestId;
        LastFetchUrl = Marshal.PtrToStringUTF8(req->Url);
        LastFetchMethod = Marshal.PtrToStringUTF8(req->Method);
        LastFetchBody = req->Body == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(req->Body);
        LastFetchHeadersJson = req->HeadersJson == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(req->HeadersJson);
        if (FetchBeginReturnCode != 0)
            return FetchBeginReturnCode;

        if (AutoCompleteFetch)
        {
            // Host-owned response memory, valid only during CompleteFetch —
            // freed right after, per the ABI lifetime rules.
            IntPtr body = Marshal.StringToCoTaskMemUTF8(AutoCompleteBody);
            IntPtr error = AutoCompleteOk ? IntPtr.Zero : Marshal.StringToCoTaskMemUTF8(AutoCompleteError);
            IntPtr headers = AutoCompleteHeadersJson is null
                ? IntPtr.Zero
                : Marshal.StringToCoTaskMemUTF8(AutoCompleteHeadersJson);
            try
            {
                var resp = new BlazorNativeFetchResponse
                {
                    StatusCode = AutoCompleteStatus,
                    Ok = AutoCompleteOk ? 1 : 0,
                    BodyUtf8 = body,
                    ErrorMessage = error,
                    HeadersJson = headers,
                };
                NativeShellBridge.CompleteFetch(requestId, in resp);
            }
            finally
            {
                Marshal.FreeCoTaskMem(body);
                if (error != IntPtr.Zero) Marshal.FreeCoTaskMem(error);
                if (headers != IntPtr.Zero) Marshal.FreeCoTaskMem(headers);
            }
        }
        return 0;
    }

    /// <summary>The host half of the buffer protocol: write UTF-8 + NUL when
    /// it fits and return bytes written (incl. NUL); return -needed when the
    /// buffer is too small.</summary>
    private static int WriteUtf8(string value, byte* buf, int cap)
    {
        int needed = Encoding.UTF8.GetByteCount(value) + 1;
        if (cap < needed) return -needed;
        Span<byte> target = new(buf, cap);
        int written = Encoding.UTF8.GetBytes(value, target);
        target[written] = 0;
        return written + 1;
    }
}

// ── Tests ────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class NativeShellBridgeTests
{
    private const string NotRegisteredMessage =
        "shell bridge not registered — call blazornative_register_bridge before mount";

    private static NativeShellBridge RegisterFake()
    {
        FakeShellHost.Reset();
        NativeShellBridge.Register(FakeShellHost.BuildCallbacks());
        return new NativeShellBridge();
    }

    [Fact]
    public async Task Navigate_RoundTrips_ThroughCurrentRoute()
    {
        var bridge = RegisterFake();
        try
        {
            await bridge.NavigateAsync("/a");
            Assert.Equal("/a", await bridge.GetCurrentRouteAsync());
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    [Fact]
    public async Task StorageWrite_Read_Delete_RoundTrip()
    {
        var bridge = RegisterFake();
        try
        {
            await bridge.WriteStorageAsync("greeting", "hallo wereld");
            Assert.Equal("hallo wereld", await bridge.ReadStorageAsync("greeting"));

            await bridge.DeleteStorageAsync("greeting");
            Assert.Null(await bridge.ReadStorageAsync("greeting"));
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    [Fact]
    public async Task StorageRead_AbsentKey_ReturnsNull()
    {
        var bridge = RegisterFake();
        try
        {
            Assert.Null(await bridge.ReadStorageAsync("never-written"));
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    [Fact]
    public async Task CurrentRoute_BufferRetry()
    {
        var bridge = RegisterFake();
        try
        {
            // > 4 KB route: first call (4096-byte buffer) must get -needed,
            // the single retry at the exact needed size must succeed.
            string longRoute = "/" + new string('r', 5000);
            FakeShellHost.Route = longRoute;

            Assert.Equal(longRoute, await bridge.GetCurrentRouteAsync());
            Assert.Equal(2, FakeShellHost.CurrentRouteCallCount);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    [Fact]
    public async Task BufferRetry_SecondFailure_Throws()
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.CurrentRouteAlwaysTooSmall = true;
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => bridge.GetCurrentRouteAsync().AsTask());
            Assert.Equal(2, FakeShellHost.CurrentRouteCallCount); // exactly one retry
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    [Fact]
    public async Task Fetch_CompletesViaCompletionExport()
    {
        var bridge = RegisterFake();
        try
        {
            var request = new BridgeHttpRequest(
                Url: "http://fake.test/data",
                Method: "POST",
                Body: "hello",
                Headers: new Dictionary<string, string> { ["X-Req"] = "1" });

            Task<BridgeHttpResponse> task = bridge.FetchAsync(request).AsTask();

            // FetchBegin runs synchronously inside FetchAsync — the fake has
            // the request; the task pends until completion arrives.
            Assert.False(task.IsCompleted);
            Assert.True(FakeShellHost.LastFetchRequestId > 0);
            Assert.Equal("http://fake.test/data", FakeShellHost.LastFetchUrl);
            Assert.Equal("POST", FakeShellHost.LastFetchMethod);
            Assert.Equal("hello", FakeShellHost.LastFetchBody);
            Assert.Contains("\"X-Req\":\"1\"", FakeShellHost.LastFetchHeadersJson);

            // Host completes: build a host-owned response, valid only during
            // the CompleteFetch call (freed right after — the bridge copies).
            IntPtr body = Marshal.StringToCoTaskMemUTF8("world");
            IntPtr headers = Marshal.StringToCoTaskMemUTF8(
                """{"Content-Type":"text/plain","X-Resp":"yes"}""");
            try
            {
                var resp = new BlazorNativeFetchResponse
                {
                    StatusCode = 201,
                    Ok = 1,
                    BodyUtf8 = body,
                    ErrorMessage = IntPtr.Zero,
                    HeadersJson = headers,
                };
                Assert.Equal(0, NativeShellBridge.CompleteFetch(FakeShellHost.LastFetchRequestId, in resp));
            }
            finally
            {
                Marshal.FreeCoTaskMem(body);
                Marshal.FreeCoTaskMem(headers);
            }

            BridgeHttpResponse response = await task;
            Assert.Equal(201, response.StatusCode);
            Assert.Equal("world", response.Body);
            Assert.Equal("text/plain", response.Headers["Content-Type"]);
            Assert.Equal("yes", response.Headers["X-Resp"]);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    [Fact]
    public async Task Fetch_Cancellation_CancelsTask_AndLateCompletionIsIgnored()
    {
        var bridge = RegisterFake();
        try
        {
            using var cts = new CancellationTokenSource();
            Task<BridgeHttpResponse> task =
                bridge.FetchAsync(new BridgeHttpRequest("http://fake.test/slow"), cts.Token).AsTask();
            long id = FakeShellHost.LastFetchRequestId;
            Assert.True(id > 0);

            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);

            // The late completion hits the unknown-id path: 1, never a throw.
            var resp = new BlazorNativeFetchResponse { StatusCode = 200, Ok = 1 };
            Assert.Equal(1, NativeShellBridge.CompleteFetch(id, in resp));
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    [Fact]
    public async Task UnregisteredHost_Throws()
    {
        NativeShellBridge.ResetForTests();
        var bridge = new NativeShellBridge();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => bridge.NavigateAsync("/x").AsTask());
        Assert.Equal(NotRegisteredMessage, ex.Message);
    }
}
