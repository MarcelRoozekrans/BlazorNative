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

    // Phase 5.4 — clipboard/share host state.
    public static string Clipboard = "";
    public static string? LastShared;
    /// <summary>When non-zero, ClipboardWrite returns this instead of 0 — drives
    /// the -1 host-error path.</summary>
    public static int ClipboardWriteReturnCode;

    // Phase 9.0 — permission-gated host-call state (the fetch mirror). HostCallBegin
    // records the request, then (when AutoCompleteHostCall) pushes a canned tri-state
    // completion via host_call_complete — scripting the host's whole permission dance
    // in-process.
    public static long LastHostCallRequestId = -1;
    public static int LastHostCallOp = -1;
    public static string? LastHostCallArgs;
    public static int HostCallBeginReturnCode;
    public static bool AutoCompleteHostCall = true;
    /// <summary>The wire status the auto-completion returns (0 = Granted).</summary>
    public static int HostCallStatus;
    /// <summary>The flat-JSON fix payload the auto-completion returns (Granted only);
    /// null = no payload, the shape every non-Granted status uses.</summary>
    public static string? HostCallPayloadJson;

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
        Clipboard = "";
        LastShared = null;
        ClipboardWriteReturnCode = 0;
        LastHostCallRequestId = -1;
        LastHostCallOp = -1;
        LastHostCallArgs = null;
        HostCallBeginReturnCode = 0;
        AutoCompleteHostCall = true;
        HostCallStatus = 0;
        HostCallPayloadJson = null;
    }

    public static BlazorNativeBridgeCallbacks BuildCallbacks() => new()
    {
        Navigate       = (IntPtr)(delegate* unmanaged[Cdecl]<byte*, int>)&NavigateFn,
        CurrentRoute   = (IntPtr)(delegate* unmanaged[Cdecl]<byte*, int, int>)&CurrentRouteFn,
        StorageRead    = (IntPtr)(delegate* unmanaged[Cdecl]<byte*, byte*, int, int>)&StorageReadFn,
        StorageWrite   = (IntPtr)(delegate* unmanaged[Cdecl]<byte*, byte*, int>)&StorageWriteFn,
        StorageDelete  = (IntPtr)(delegate* unmanaged[Cdecl]<byte*, int>)&StorageDeleteFn,
        FetchBegin     = (IntPtr)(delegate* unmanaged[Cdecl]<long, BlazorNativeFetchRequest*, int>)&FetchBeginFn,
        ClipboardRead  = (IntPtr)(delegate* unmanaged[Cdecl]<byte*, int, int>)&ClipboardReadFn,
        ClipboardWrite = (IntPtr)(delegate* unmanaged[Cdecl]<byte*, int>)&ClipboardWriteFn,
        Share          = (IntPtr)(delegate* unmanaged[Cdecl]<byte*, int>)&ShareFn,
        HostCallBegin  = (IntPtr)(delegate* unmanaged[Cdecl]<long, int, byte*, int>)&HostCallBeginFn,
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

    // ── Phase 5.4 clipboard/share callbacks ──────────────────────────────────

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ClipboardReadFn(byte* buf, int cap)
        => WriteUtf8(Clipboard, buf, cap); // buffer protocol — large values force -needed

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ClipboardWriteFn(byte* textUtf8)
    {
        if (ClipboardWriteReturnCode != 0)
            return ClipboardWriteReturnCode;
        string? text = Marshal.PtrToStringUTF8((IntPtr)textUtf8);
        if (text is null) return -1;
        Clipboard = text; // copy — the pointer dies when we return
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ShareFn(byte* textUtf8)
    {
        string? text = Marshal.PtrToStringUTF8((IntPtr)textUtf8);
        if (text is null) return -1;
        LastShared = text; // copy — fire-and-forget record for the assertion
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

    // ── Phase 9.0 permission-gated host-call callback ─────────────────────────

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int HostCallBeginFn(long requestId, int op, byte* argsUtf8)
    {
        // Copy — the args string dies when we return (the ABI lifetime rule).
        LastHostCallRequestId = requestId;
        LastHostCallOp = op;
        LastHostCallArgs = argsUtf8 == null ? null : Marshal.PtrToStringUTF8((IntPtr)argsUtf8);
        if (HostCallBeginReturnCode != 0)
            return HostCallBeginReturnCode;

        if (AutoCompleteHostCall)
        {
            // Push the tri-state completion via the managed core (the CompleteFetch
            // pattern — the [UnmanagedCallersOnly] export cannot be called directly
            // from managed code; the thin Exports.HostCallComplete wrapper only
            // marshals the payload pointer, exactly like FetchComplete).
            NativeShellBridge.CompleteHostCall(requestId, HostCallStatus, HostCallPayloadJson);
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
    public async Task FetchBegin_HostErrorReturnCode_ThrowsHostError()
    {
        // The .NET leg of the guarded-catch wire path (ABI exception posture):
        // a throwing Kotlin handler surfaces across the ABI as -1, and the
        // bridge must turn any negative FetchBegin return into the HostError
        // InvalidOperationException naming the op + return code. (The Kotlin
        // leg — throw → guarded() → onError + -1 — is pinned by
        // ShellBridgeTest.guarded_callback_maps_throw_to_host_error.)
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.FetchBeginReturnCode = -1;
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => bridge.FetchAsync(new BridgeHttpRequest("http://fake.test/boom")).AsTask());
            Assert.Contains("fetch-begin", ex.Message);
            Assert.Contains("return code -1", ex.Message);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    // ── Phase 5.4: clipboard/share round-trip + size negotiation ─────────────

    [Fact]
    public async Task Clipboard_Write_Read_RoundTrip()
    {
        var bridge = RegisterFake();
        try
        {
            await bridge.ClipboardWriteAsync("copied text");
            Assert.Equal("copied text", FakeShellHost.Clipboard);
            Assert.Equal("copied text", await bridge.ClipboardReadAsync());
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    [Fact]
    public async Task ClipboardRead_Empty_ReturnsEmptyString()
    {
        var bridge = RegisterFake();
        try
        {
            Assert.Equal("", await bridge.ClipboardReadAsync());
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    [Fact]
    public async Task ClipboardRead_LargeValue_BufferRetry()
    {
        var bridge = RegisterFake();
        try
        {
            // > 4 KB clipboard: first call (4096 buffer) gets -needed, the single
            // retry at the exact size succeeds (the CurrentRoute buffer protocol,
            // reused unchanged).
            string big = new string('c', 5000);
            FakeShellHost.Clipboard = big;
            Assert.Equal(big, await bridge.ClipboardReadAsync());
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    [Fact]
    public async Task ClipboardWrite_HostErrorReturnCode_ThrowsHostError()
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.ClipboardWriteReturnCode = -1;
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => bridge.ClipboardWriteAsync("x").AsTask());
            Assert.Contains("clipboard-write", ex.Message);
            Assert.Contains("return code -1", ex.Message);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    [Fact]
    public async Task Share_ForwardsText()
    {
        var bridge = RegisterFake();
        try
        {
            await bridge.ShareAsync("share me");
            Assert.Equal("share me", FakeShellHost.LastShared);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    [Fact]
    public async Task OldShell_48ByteRegistration_ClipboardUnsupported_ButNavigateStillWorks()
    {
        // The forward-compat proof of the size negotiation: build the FULL
        // 80-byte struct (all 10 slots set, clipboard/share/host-call are REAL
        // pointers) but register with structSize == 48 (an old shell that predates
        // the slots).
        // The register min-copy truncates to the first 6 slots and zero-fills the
        // tail, so clipboard/share read back as Zero → NotSupportedException — yet
        // Navigate (a copied slot) still works.
        FakeShellHost.Reset();
        NativeShellBridge.Register(structSize: 48, FakeShellHost.BuildCallbacks());
        var bridge = new NativeShellBridge();
        try
        {
            var readEx = await Assert.ThrowsAsync<NotSupportedException>(
                () => bridge.ClipboardReadAsync().AsTask());
            Assert.Contains("not supported", readEx.Message);

            await Assert.ThrowsAsync<NotSupportedException>(
                () => bridge.ClipboardWriteAsync("x").AsTask());
            await Assert.ThrowsAsync<NotSupportedException>(
                () => bridge.ShareAsync("x").AsTask());

            // The graceful forward-compat: a copied slot still works.
            await bridge.NavigateAsync("/still-works");
            Assert.Equal("/still-works", await bridge.GetCurrentRouteAsync());
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    [Fact]
    public async Task NegativeStructSize_ClampsToNoCopy_EverythingUnsupported()
    {
        // The Clamp lower-bound invariant (Gate 1 review nit): a stray negative
        // structSize reaching Register must be a SAFE no-copy (Clamp(-8,0,72)=0 →
        // every slot stays zero → everything unsupported), never an
        // OverflowException from a negative Buffer.MemoryCopy length. Register the
        // full 72-byte struct but claim -8 bytes: nothing is copied, so all three
        // guarded capabilities read back as unsupported and Register itself does
        // not throw.
        FakeShellHost.Reset();
        NativeShellBridge.Register(structSize: -8, FakeShellHost.BuildCallbacks());
        var bridge = new NativeShellBridge();
        try
        {
            await Assert.ThrowsAsync<NotSupportedException>(
                () => bridge.ClipboardReadAsync().AsTask());
            await Assert.ThrowsAsync<NotSupportedException>(
                () => bridge.ClipboardWriteAsync("x").AsTask());
            await Assert.ThrowsAsync<NotSupportedException>(
                () => bridge.ShareAsync("x").AsTask());
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

    // ── Phase 10.0 (#121): the reported PlatformKind is the shell's real one ──────
    //
    // The runtime is linked into BOTH native shells' static archives, so a kind
    // hardcoded to Android meant an iOS app reported Android through the public
    // IMobileBridge.PlatformInfo / GetPlatformInfoAsync surface. The kind now flows
    // from the shell through the init options, exactly like the os string.

    [Fact]
    public async Task PlatformInfo_ReportsTheShellsKind_iOS_NotAndroid()
    {
        // The RED-FIRST case: an iOS shell passes os "ios" + kind iOS. Against the
        // pre-fix hardcoded-Android runtime this asserts Android and FAILS; after the
        // fix the stored kind is served and it reports iOS.
        NativeShellBridge.SetPlatformInfo("ios", apiLevel: 0, note: "ios-shell", kind: PlatformKind.iOS);
        try
        {
            var bridge = new NativeShellBridge();

            PlatformInfo info = await bridge.GetPlatformInfoAsync();
            Assert.Equal(PlatformKind.iOS, info.Platform);
            Assert.NotEqual(PlatformKind.Android, info.Platform);
            Assert.Equal("ios", info.OsVersion);

            // The JSON builder serves the same kind (it is the on-device Blazor
            // surface's string view — must not say "Android" either).
            Assert.Contains("\"kind\":\"iOS\"", bridge.PlatformInfo);
            Assert.DoesNotContain("Android", bridge.PlatformInfo);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    [Fact]
    public async Task PlatformInfo_ReportsTheShellsKind_Android_StillAndroid()
    {
        // The regression guard: the Android shell passes kind Android and still
        // reports Android (the fix must not merely swap one constant for another).
        NativeShellBridge.SetPlatformInfo("android", apiLevel: 34, note: "android-shell", kind: PlatformKind.Android);
        try
        {
            var bridge = new NativeShellBridge();

            PlatformInfo info = await bridge.GetPlatformInfoAsync();
            Assert.Equal(PlatformKind.Android, info.Platform);
            Assert.Equal("android", info.OsVersion);
            Assert.Contains("\"kind\":\"Android\"", bridge.PlatformInfo);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    [Fact]
    public async Task PlatformInfo_UnsetKind_DefaultsToDevHost_NotAndroid()
    {
        // A shell that predates the field passes ordinal 0 → DevHost, the safe
        // non-lying default. It must NOT be silently reported as Android (the old
        // hardcoded value). Exports.ToPlatformKind(0) is DevHost; drive that.
        NativeShellBridge.SetPlatformInfo("dev", apiLevel: 0, note: null, kind: Exports.ToPlatformKind(0));
        try
        {
            var bridge = new NativeShellBridge();

            PlatformInfo info = await bridge.GetPlatformInfoAsync();
            Assert.Equal(PlatformKind.DevHost, info.Platform);
            Assert.NotEqual(PlatformKind.Android, info.Platform);
            Assert.Contains("\"kind\":\"DevHost\"", bridge.PlatformInfo);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    [Theory]
    [InlineData(0, PlatformKind.DevHost)]
    [InlineData(1, PlatformKind.Android)]
    [InlineData(2, PlatformKind.iOS)]
    [InlineData(3, PlatformKind.Windows)]
    [InlineData(4, PlatformKind.Mac)]
    [InlineData(5, PlatformKind.DevHost)]    // out of range → safe default, not Android
    [InlineData(-1, PlatformKind.DevHost)]   // negative junk → safe default, not Android
    public void ToPlatformKind_MapsTheInitOptionsOrdinal_ByEnumValue(int ordinal, PlatformKind expected)
    {
        // THE ORDINAL CONTRACT the shells encode against (Android=1, iOS=2, …). If
        // the enum is ever reordered this pin reds — the Kotlin/Swift shells pass
        // these exact integers across the ABI, so the mapping is load-bearing.
        Assert.Equal(expected, Exports.ToPlatformKind(ordinal));
    }

    [Fact]
    public void InitOptionsStruct_Is32Bytes_MirroringTheCHeaderAndKotlinJna()
    {
        // Phase 10.0 (#121): the init-INPUT struct grew 24 → 32 bytes when
        // platformInfoKind (int) was appended. This pin holds the .NET side of the
        // ABI mirror equal to the C header (bn_init_options → 32 bytes) and the
        // Kotlin JNA size assertion (BootSmokeNativeAndroidTest → 32). The frozen
        // 80-byte callbacks bridge is a SEPARATE struct and is unaffected — see
        // BridgeProtocolNativeTests. If this reds, the three mirrors have drifted:
        // re-sync deliberately, do not just bump the number.
        Assert.Equal(32, Marshal.SizeOf<BlazorNativeInitOptions>());
    }
}
