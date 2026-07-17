using BlazorNative.Core;
using BlazorNative.Runtime;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// GeolocationBridgeTests — Phase 9.0 Gate 1 (M9 DoD #1+#2): the permission-gated
// async machinery on the host CLR, through the REAL function-pointer path
// (NativeShellBridge over FakeShellHost's HostCallBegin, completed via the
// blazornative_host_call_complete export).
//
// The named risk, proven headless FIRST: denial RETURNS a status, never throws,
// never hangs; the pending registry is keyed by requestId (a mis-key completes
// the wrong call — the mutation target); cancellation drops the entry and cancels
// the task; a late/unknown completion is benign (return 1). The Kotlin twin (Gate
// 1 JVM) and the AVD/simulator (Gates 2/3) mirror this through the dll and real
// providers.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class GeolocationBridgeTests
{
    private static NativeShellBridge RegisterFake()
    {
        FakeShellHost.Reset();
        NativeShellBridge.Register(FakeShellHost.BuildCallbacks());
        return new NativeShellBridge();
    }

    // ── The begin call carries the op + flat-JSON args ────────────────────────

    [Fact]
    public async Task GetCurrentPosition_BeginsWithGeolocationOp_AndRequestMode()
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallStatus = (int)GeolocationStatus.Granted;
            FakeShellHost.HostCallPayloadJson =
                """{"lat":"52.3702","lng":"4.8952","accuracy":"12.0","altitude":"3.0","timestamp":"1700000000000"}""";

            await bridge.GetCurrentPositionAsync();

            Assert.True(FakeShellHost.LastHostCallRequestId > 0);
            Assert.Equal((int)NativeShellBridge.HostCallOp.Geolocation, FakeShellHost.LastHostCallOp);
            Assert.Contains("\"mode\":\"request\"", FakeShellHost.LastHostCallArgs);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    [Fact]
    public async Task CheckPermission_BeginsWithCheckMode()
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallStatus = (int)GeolocationStatus.Denied;
            Assert.Equal(GeolocationStatus.Denied, await bridge.CheckGeolocationPermissionAsync());
            Assert.Contains("\"mode\":\"check\"", FakeShellHost.LastHostCallArgs);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    // ── Granted parses the flat-JSON fix ──────────────────────────────────────

    [Fact]
    public async Task Granted_ParsesTheFlatJsonFix()
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallStatus = (int)GeolocationStatus.Granted;
            FakeShellHost.HostCallPayloadJson =
                """{"lat":"52.3702","lng":"4.8952","accuracy":"12.0","altitude":"3.0","timestamp":"1700000000000"}""";

            GeolocationResult result = await bridge.GetCurrentPositionAsync();

            Assert.Equal(GeolocationStatus.Granted, result.Status);
            Assert.NotNull(result.Position);
            GeolocationPosition p = result.Position!.Value;
            Assert.Equal(52.3702, p.Latitude, 4);
            Assert.Equal(4.8952, p.Longitude, 4);
            Assert.Equal(12.0, p.Accuracy, 4);
            Assert.Equal(3.0, p.Altitude!.Value, 4);
            Assert.Equal(1700000000000L, p.TimestampUnixMs);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    // ── DENIAL IS DATA: every non-Granted status RETURNS, no throw, no hang ────

    [Theory]
    [InlineData(GeolocationStatus.Denied)]
    [InlineData(GeolocationStatus.DeniedPermanently)]
    [InlineData(GeolocationStatus.Restricted)]
    [InlineData(GeolocationStatus.LocationUnavailable)]
    [InlineData(GeolocationStatus.Error)]
    public async Task NonGranted_ReturnsStatus_NullPosition_NoThrow_NoHang(GeolocationStatus status)
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallStatus = (int)status;
            FakeShellHost.HostCallPayloadJson = null; // every non-Granted carries none

            // A BOUNDED await: a hang would trip the timeout and redden the test —
            // the denial-as-data law asserted mechanically, not by inspection.
            GeolocationResult result = await bridge.GetCurrentPositionAsync()
                .AsTask().WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(status, result.Status);
            Assert.Null(result.Position);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    // ── The tri-state WIRE MAPPING: each integer maps to its status ───────────

    [Theory]
    [InlineData(0, GeolocationStatus.Granted)]
    [InlineData(1, GeolocationStatus.Denied)]
    [InlineData(2, GeolocationStatus.DeniedPermanently)]
    [InlineData(3, GeolocationStatus.Restricted)]
    [InlineData(4, GeolocationStatus.LocationUnavailable)]
    [InlineData(5, GeolocationStatus.Error)]
    [InlineData(99, GeolocationStatus.Error)]  // out-of-range host bug → Error, still data
    public async Task WireStatusInteger_MapsToTypedStatus(int wire, GeolocationStatus expected)
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallStatus = wire;
            // A Granted needs a payload to build a position; 0 with none yields a
            // Granted/no-position, which is fine for the mapping assertion here.
            FakeShellHost.HostCallPayloadJson = wire == 0 ? "{}" : null;
            GeolocationResult result = await bridge.GetCurrentPositionAsync();
            Assert.Equal(expected, result.Status);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    // ── The pending registry: keyed by requestId ──────────────────────────────

    [Fact]
    public async Task Completion_KeyedByRequestId_ResolvesTheRightCall()
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.AutoCompleteHostCall = false; // hold the call open

            Task<GeolocationResult> task = bridge.GetCurrentPositionAsync().AsTask();
            Assert.False(task.IsCompleted);
            long id = FakeShellHost.LastHostCallRequestId;
            Assert.True(id > 0);

            // A completion for a DIFFERENT id must NOT resolve this call (return 1,
            // never a throw) — the id-keying is what the mis-key mutation breaks.
            Assert.Equal(1, NativeShellBridge.CompleteHostCall(id + 999, (int)GeolocationStatus.Granted, null));
            Assert.False(task.IsCompleted);

            // The RIGHT id resolves it.
            Assert.Equal(0, NativeShellBridge.CompleteHostCall(id, (int)GeolocationStatus.Denied, null));
            GeolocationResult result = await task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(GeolocationStatus.Denied, result.Status);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    // ── Unknown / already-completed id is benign (the cancellation race) ──────

    [Fact]
    public void UnknownId_IsBenign_ReturnsOne_NeverThrows()
    {
        RegisterFake();
        try
        {
            Assert.Equal(1, NativeShellBridge.CompleteHostCall(424242, (int)GeolocationStatus.Granted, null));
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    // ── Cancellation drops the entry + cancels; a late completion is ignored ──

    [Fact]
    public async Task Cancellation_CancelsTask_AndLateCompletionIsIgnored()
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.AutoCompleteHostCall = false; // simulate the prompt still up
            using var cts = new CancellationTokenSource();
            Task<GeolocationResult> task = bridge.GetCurrentPositionAsync(cts.Token).AsTask();
            long id = FakeShellHost.LastHostCallRequestId;
            Assert.True(id > 0);

            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);

            // The app was killed during the prompt → the completion never arrives,
            // and if it DID it hits the unknown-id path: 1, never a throw, never a
            // leaked pending entry.
            Assert.Equal(1, NativeShellBridge.CompleteHostCall(id, (int)GeolocationStatus.Granted, null));
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    // ── A host-error begin return code surfaces as HostError ──────────────────

    [Fact]
    public async Task HostCallBegin_HostErrorReturnCode_ThrowsHostError()
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallBeginReturnCode = -1;
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => bridge.GetCurrentPositionAsync().AsTask());
            Assert.Contains("host-call-begin", ex.Message);
            Assert.Contains("return code -1", ex.Message);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    // ── An old shell that predates the slot: unsupported, not a null call ─────

    [Fact]
    public async Task OldShell_WithoutHostCallSlot_SurfacesNotSupported()
    {
        // Register with structSize == 72 (a pre-9.0 shell: 9 slots, no HostCallBegin).
        // The register min-copy zero-fills the HostCallBegin slot, so RequireSlot
        // surfaces NotSupportedException — never a null-pointer call.
        FakeShellHost.Reset();
        NativeShellBridge.Register(structSize: 72, FakeShellHost.BuildCallbacks());
        var bridge = new NativeShellBridge();
        try
        {
            var ex = await Assert.ThrowsAsync<NotSupportedException>(
                () => bridge.GetCurrentPositionAsync().AsTask());
            Assert.Contains("not supported", ex.Message);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }
}
