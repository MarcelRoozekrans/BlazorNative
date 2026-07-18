using BlazorNative.Core;
using BlazorNative.Runtime;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// BiometricsBridgeTests — Phase 9.2 Gate 1 (M9 DoD #4): the biometrics surface on
// the host CLR, through the REAL function-pointer path (NativeShellBridge over
// FakeShellHost's HostCallBegin, completed via blazornative_host_call_complete) —
// the SECOND reuse of the 9.0 generic permission-gated ABI.
//
// authenticate + check both ride the EXISTING InvokeHostCallAsync with
// op=Biometrics and the action inside the flat JSON — NO struct grow, NO new export
// (asserted UNCHANGED in SecureBiometricsAbiUnchangedTests). Denial is DATA: every
// non-Authenticated status RETURNS, never throws, never hangs (a bounded await
// proves the no-hang law mechanically — the named "auth-failure-throws" mutation
// reds this matrix). The op constant, the args-JSON shape, and the wire-integer
// mapping (incl. out-of-range → Error) are each pinned.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class BiometricsBridgeTests
{
    private static NativeShellBridge RegisterFake()
    {
        FakeShellHost.Reset();
        NativeShellBridge.Register(FakeShellHost.BuildCallbacks());
        return new NativeShellBridge();
    }

    // ── The begin call carries op=Biometrics + the action-in-JSON payload ─────

    [Fact]
    public async Task Authenticate_BeginsWithBiometricsOp_AndAuthenticateAction()
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallStatus = (int)BiometricStatus.Authenticated;
            await bridge.AuthenticateAsync("Prove it's you");

            Assert.True(FakeShellHost.LastHostCallRequestId > 0);
            Assert.Equal((int)NativeShellBridge.HostCallOp.Biometrics, FakeShellHost.LastHostCallOp);
            Assert.Contains("\"action\":\"authenticate\"", FakeShellHost.LastHostCallArgs);
            Assert.Contains("\"reason\":\"Prove it's you\"", FakeShellHost.LastHostCallArgs);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    [Fact]
    public async Task IsAvailable_BeginsWithCheckAction()
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallStatus = (int)BiometricStatus.Authenticated;
            Assert.Equal(BiometricStatus.Authenticated, await bridge.IsBiometricAvailableAsync());
            Assert.Contains("\"action\":\"check\"", FakeShellHost.LastHostCallArgs);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    // ── DENIAL IS DATA: every non-Authenticated status RETURNS, no throw, no hang ─

    [Theory]
    [InlineData(BiometricStatus.Failed)]
    [InlineData(BiometricStatus.Cancelled)]
    [InlineData(BiometricStatus.Unavailable)]
    [InlineData(BiometricStatus.LockedOut)]
    [InlineData(BiometricStatus.Error)]
    public async Task NonAuthenticated_ReturnsStatus_NoThrow_NoHang(BiometricStatus status)
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallStatus = (int)status;

            // A BOUNDED await: a hang would trip the timeout and redden the test —
            // the denial-as-data law asserted mechanically. A failed auth that THREW
            // (the named mutation) reds here instead of returning a status.
            BiometricStatus result = await bridge.AuthenticateAsync("reason")
                .AsTask().WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(status, result);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    // ── The wire mapping: each integer maps to its status; OOR → Error ────────

    [Theory]
    [InlineData(0, BiometricStatus.Authenticated)]
    [InlineData(1, BiometricStatus.Failed)]
    [InlineData(2, BiometricStatus.Cancelled)]
    [InlineData(3, BiometricStatus.Unavailable)]
    [InlineData(4, BiometricStatus.LockedOut)]
    [InlineData(5, BiometricStatus.Error)]
    [InlineData(99, BiometricStatus.Error)]  // out-of-range host bug → Error, still data
    [InlineData(-1, BiometricStatus.Error)]  // negative is out of range too
    public async Task WireStatusInteger_MapsToTypedStatus(int wire, BiometricStatus expected)
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallStatus = wire;
            Assert.Equal(expected, await bridge.AuthenticateAsync("reason"));
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    // ── An old shell that predates the HostCallBegin slot: unsupported ────────

    [Fact]
    public async Task OldShell_WithoutHostCallSlot_SurfacesNotSupported()
    {
        FakeShellHost.Reset();
        NativeShellBridge.Register(structSize: 72, FakeShellHost.BuildCallbacks());
        var bridge = new NativeShellBridge();
        try
        {
            var ex = await Assert.ThrowsAsync<NotSupportedException>(
                () => bridge.AuthenticateAsync("reason").AsTask());
            Assert.Contains("not supported", ex.Message);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }
}
