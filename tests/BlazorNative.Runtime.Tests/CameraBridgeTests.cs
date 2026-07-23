using BlazorNative.Core;
using BlazorNative.Device;
using BlazorNative.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// CameraBridgeTests — Phase 9.3 Gate 1 (M9 DoD #5): the camera surface on the host
// CLR, through the REAL function-pointer path (NativeShellBridge over FakeShellHost's
// HostCallBegin, completed via blazornative_host_call_complete).
//
// capture/check ride the EXISTING InvokeHostCallAsync with op=Camera and the action
// inside the flat JSON — NO struct grow, NO new export. THE PATH-PAYLOAD PROOF: a
// Captured completion returns {"path":…,"width":…,"height":…,"bytes":…} in the OPTIONAL
// payload host_call_complete has carried since 9.0 — the THIRD user of that channel
// (geolocation's fix, secure get's value are the first two), used here to NAME a FILE
// (a large artifact by reference) rather than carry its bytes. Denial is DATA: Cancelled
// / Denied / Unavailable / Error RETURN, never throw, never hang. The op constant, the
// args-JSON shapes (the capture maxDim/quality; the check action), the path-payload
// parse, and the wire-integer mapping (incl. out-of-range → Error) are each pinned.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class CameraBridgeTests
{
    private static NativeShellBridge RegisterFake()
    {
        FakeShellHost.Reset();
        NativeShellBridge.Register(FakeShellHost.BuildCallbacks());
        return new NativeShellBridge();
    }

    // ── The begin call carries op=Camera + the capture action + maxDim/quality ──

    [Fact]
    public async Task Capture_BeginsWithCameraOp_AndCaptureAction_MaxDimAndQuality()
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallStatus = (int)CameraStatus.Cancelled; // any terminal outcome
            await bridge.CapturePhotoAsync(new CaptureOptions(MaxDimension: 1024, Quality: 70));

            Assert.True(FakeShellHost.LastHostCallRequestId > 0);
            Assert.Equal((int)NativeShellBridge.HostCallOp.Camera, FakeShellHost.LastHostCallOp);
            Assert.Contains("\"action\":\"capture\"", FakeShellHost.LastHostCallArgs);
            Assert.Contains("\"maxDim\":\"1024\"", FakeShellHost.LastHostCallArgs);
            Assert.Contains("\"quality\":\"70\"", FakeShellHost.LastHostCallArgs);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    [Fact]
    public async Task Check_CarriesCheckAction_NoCaptureNoDims()
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallStatus = (int)CameraStatus.Captured; // "present + usable"
            await bridge.CheckCameraAvailabilityAsync();

            Assert.Equal((int)NativeShellBridge.HostCallOp.Camera, FakeShellHost.LastHostCallOp);
            Assert.Contains("\"action\":\"check\"", FakeShellHost.LastHostCallArgs);
            Assert.DoesNotContain("\"maxDim\"", FakeShellHost.LastHostCallArgs);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    // ── Captured parses the {"path",…} payload (the THIRD user of the 9.0 channel) ──

    [Fact]
    public async Task Captured_ParsesThePathAndDimsPayload()
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallStatus = (int)CameraStatus.Captured;
            FakeShellHost.HostCallPayloadJson =
                """{"path":"file:///cache/blazornative_captures/p.jpg","width":"1600","height":"1200","bytes":"204800"}""";

            PhotoResult result = await bridge.CapturePhotoAsync(default);

            Assert.Equal(CameraStatus.Captured, result.Status);
            Assert.Equal("file:///cache/blazornative_captures/p.jpg", result.Path);
            Assert.Equal(1600, result.Width);
            Assert.Equal(1200, result.Height);
            Assert.Equal(204800, result.SizeBytes);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    /// <summary>The payload key is `path`, NOT `data` (a bytes-inline assumption) and
    /// NOT `file`: a completion carrying the image under the wrong key yields a NULL
    /// path — the named "path-parse reds" mutation, pinned as a positive fact. The
    /// bytes-inline shape (rejected in §2) parses to nothing here, which is the point:
    /// the image is a FILE named by `path`, never inline `data`.</summary>
    [Fact]
    public async Task Captured_ReadsThePathKey_NotDataOrFile()
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallStatus = (int)CameraStatus.Captured;
            FakeShellHost.HostCallPayloadJson = """{"data":"/9j/4AAQ...","file":"x.jpg"}""";

            PhotoResult result = await bridge.CapturePhotoAsync(default);

            Assert.Equal(CameraStatus.Captured, result.Status);
            Assert.Null(result.Path);          // neither `data` nor `file` is the path key
            Assert.Equal(0, result.Width);
            Assert.Equal(0L, result.SizeBytes);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    // ── DENIAL IS DATA: a non-Captured capture RETURNS a null-path status ─────────

    [Theory]
    [InlineData(CameraStatus.Cancelled)]
    [InlineData(CameraStatus.Denied)]
    [InlineData(CameraStatus.Unavailable)]
    [InlineData(CameraStatus.Error)]
    public async Task NonCapturedCapture_ReturnsStatus_NullPath_ZeroDims_NoThrow_NoHang(CameraStatus status)
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallStatus = (int)status;
            FakeShellHost.HostCallPayloadJson = null; // every non-Captured carries none

            PhotoResult result = await bridge.CapturePhotoAsync(default)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(status, result.Status);
            Assert.Null(result.Path);
            Assert.Equal(0, result.Width);
            Assert.Equal(0, result.Height);
            Assert.Equal(0L, result.SizeBytes);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    // ── The wire mapping: each integer maps; out-of-range → Error (still data) ────

    [Theory]
    [InlineData(0, CameraStatus.Captured)]
    [InlineData(1, CameraStatus.Cancelled)]
    [InlineData(2, CameraStatus.Denied)]
    [InlineData(3, CameraStatus.Unavailable)]
    [InlineData(4, CameraStatus.Error)]
    [InlineData(99, CameraStatus.Error)]  // out-of-range → Error, still data
    [InlineData(-1, CameraStatus.Error)]
    public async Task WireStatusInteger_MapsToTypedStatus(int wire, CameraStatus expected)
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallStatus = wire;
            Assert.Equal(expected, await bridge.CheckCameraAvailabilityAsync());
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    // ── The pending registry is keyed by requestId (reused from 9.0) ──────────────

    [Fact]
    public async Task Completion_KeyedByRequestId_ResolvesTheRightCall()
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.AutoCompleteHostCall = false; // hold the call open

            Task<PhotoResult> task = bridge.CapturePhotoAsync(default).AsTask();
            Assert.False(task.IsCompleted);
            long id = FakeShellHost.LastHostCallRequestId;
            Assert.True(id > 0);

            // A completion for an unknown id is benign and moves nothing.
            Assert.Equal(1, NativeShellBridge.CompleteHostCall(id + 999, (int)CameraStatus.Captured, null));
            Assert.False(task.IsCompleted);

            Assert.Equal(0, NativeShellBridge.CompleteHostCall(id, (int)CameraStatus.Cancelled, null));
            PhotoResult result = await task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(CameraStatus.Cancelled, result.Status);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    // ── An old shell that predates the HostCallBegin slot: unsupported ────────────

    [Fact]
    public async Task OldShell_WithoutHostCallSlot_SurfacesNotSupported()
    {
        FakeShellHost.Reset();
        NativeShellBridge.Register(structSize: 72, FakeShellHost.BuildCallbacks());
        var bridge = new NativeShellBridge();
        try
        {
            var ex = await Assert.ThrowsAsync<NotSupportedException>(
                () => bridge.CapturePhotoAsync(default).AsTask());
            Assert.Contains("not supported", ex.Message);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    // ── Issue #178: the ICamera facade resolves the DOCUMENTED defaults ────────────
    //
    // The bug: CapturePhotoAsync had `CaptureOptions options = default`, and
    // default(CaptureOptions) zero-initialises the struct (MaxDimension=0,Quality=0),
    // bypassing the record's primary-constructor defaults (=2048,=85). The fix makes
    // the parameter `CaptureOptions? options = null` and substitutes `new CaptureOptions()`
    // for null. These pin the facade end-to-end THROUGH the real NativeShellBridge args
    // (the same BuildCaptureArgs the on-device shell parses), not just the struct.

    /// <summary>Resolves the ergonomic <see cref="ICamera"/> facade over the given
    /// bridge exactly as the runtime composition root does (AddBlazorNativeDevice
    /// wraps whatever IMobileBridge is registered).</summary>
    private static ICamera ResolveCamera(NativeShellBridge bridge)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMobileBridge>(bridge);
        services.AddBlazorNativeDevice();
        return services.BuildServiceProvider().GetRequiredService<ICamera>();
    }

    [Fact]
    public async Task NoArgCapture_ResolvesTheDocumentedDefaults_2048And85_ThroughToTheShellArgs()
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallStatus = (int)CameraStatus.Cancelled; // any terminal outcome
            ICamera camera = ResolveCamera(bridge);

            await camera.CapturePhotoAsync(); // the natural call — no options

            // null ⇒ new CaptureOptions() ⇒ the primary-ctor defaults, NOT default(T)'s 0/0.
            Assert.Contains("\"maxDim\":\"2048\"", FakeShellHost.LastHostCallArgs);
            Assert.Contains("\"quality\":\"85\"", FakeShellHost.LastHostCallArgs);
            Assert.DoesNotContain("\"quality\":\"0\"", FakeShellHost.LastHostCallArgs);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    [Fact]
    public async Task ExplicitCaptureOptions_StillFlowThrough_Unchanged()
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallStatus = (int)CameraStatus.Cancelled;
            ICamera camera = ResolveCamera(bridge);

            await camera.CapturePhotoAsync(new CaptureOptions(MaxDimension: 1024, Quality: 70));

            Assert.Contains("\"maxDim\":\"1024\"", FakeShellHost.LastHostCallArgs);
            Assert.Contains("\"quality\":\"70\"", FakeShellHost.LastHostCallArgs);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    /// <summary>Documents the STRAGGLER the shell-boundary fix defends against: an
    /// EXPLICIT default(CaptureOptions) (or a value marshalled from an older caller)
    /// still serializes maxDim=0/quality=0 on the wire. The API fix removes this for
    /// the no-arg call; the Kotlin/Swift shells map a 0 quality to the documented
    /// default rather than coerceIn(1,100)→1 (defense in depth, issue #178).</summary>
    [Fact]
    public async Task ExplicitDefaultCaptureOptions_StillSerializesZeroZero_TheShellStraggler()
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallStatus = (int)CameraStatus.Cancelled;
            ICamera camera = ResolveCamera(bridge);

            await camera.CapturePhotoAsync(default(CaptureOptions)); // an explicit zeroed struct

            Assert.Contains("\"maxDim\":\"0\"", FakeShellHost.LastHostCallArgs);
            Assert.Contains("\"quality\":\"0\"", FakeShellHost.LastHostCallArgs);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }
}
