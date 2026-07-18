using BlazorNative.Core;
using BlazorNative.Runtime;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// SecureStorageBridgeTests — Phase 9.2 Gate 1 (M9 DoD #4): the secure-storage
// surface on the host CLR, through the REAL function-pointer path (NativeShellBridge
// over FakeShellHost's HostCallBegin, completed via blazornative_host_call_complete).
//
// set/get/getWithAuth/delete all ride the EXISTING InvokeHostCallAsync with
// op=SecureStorage and the action inside the flat JSON — NO struct grow, NO new
// export. THE PAYLOAD PROOF: get/getWithAuth return the value in the OPTIONAL
// {"value":…} payload host_call_complete has carried since 9.0 (geolocation's fix is
// the first user; this is the SECOND — the channel is generic, not
// geolocation-specific). Denial is DATA: NotFound / AuthFailed / Unavailable / Error
// RETURN, never throw, never hang. The 8 KB cap is enforced at the .NET boundary
// (an oversize value RETURNS Error and never crosses). The op constant, the args-JSON
// shapes (incl. the auth "0"/"1" flag), the get-payload parse, and the wire-integer
// mapping (incl. out-of-range → Error) are each pinned.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class SecureStorageBridgeTests
{
    private static NativeShellBridge RegisterFake()
    {
        FakeShellHost.Reset();
        NativeShellBridge.Register(FakeShellHost.BuildCallbacks());
        return new NativeShellBridge();
    }

    // ── The begin call carries op=SecureStorage + the action-in-JSON payload ──

    [Fact]
    public async Task Set_BeginsWithSecureStorageOp_AndSetAction_AuthFlagOne()
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallStatus = (int)SecureStorageStatus.Ok;
            await bridge.SetSecretAsync("k", "v", requireAuth: true);

            Assert.True(FakeShellHost.LastHostCallRequestId > 0);
            Assert.Equal((int)NativeShellBridge.HostCallOp.SecureStorage, FakeShellHost.LastHostCallOp);
            Assert.Contains("\"action\":\"set\"", FakeShellHost.LastHostCallArgs);
            Assert.Contains("\"key\":\"k\"", FakeShellHost.LastHostCallArgs);
            Assert.Contains("\"value\":\"v\"", FakeShellHost.LastHostCallArgs);
            Assert.Contains("\"auth\":\"1\"", FakeShellHost.LastHostCallArgs);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    [Fact]
    public async Task Set_RequireAuthFalse_CarriesAuthFlagZero()
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallStatus = (int)SecureStorageStatus.Ok;
            await bridge.SetSecretAsync("k", "v", requireAuth: false);
            Assert.Contains("\"auth\":\"0\"", FakeShellHost.LastHostCallArgs);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    [Fact]
    public async Task Get_CarriesGetAction_KeyOnly_NoReason()
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallStatus = (int)SecureStorageStatus.NotFound;
            await bridge.GetSecretAsync("k");
            Assert.Contains("\"action\":\"get\"", FakeShellHost.LastHostCallArgs);
            Assert.Contains("\"key\":\"k\"", FakeShellHost.LastHostCallArgs);
            Assert.DoesNotContain("\"reason\"", FakeShellHost.LastHostCallArgs);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    [Fact]
    public async Task GetWithAuth_CarriesGetWithAuthAction_AndReason()
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallStatus = (int)SecureStorageStatus.AuthFailed;
            await bridge.GetSecretWithAuthAsync("k", "Unlock your secret");
            Assert.Contains("\"action\":\"getWithAuth\"", FakeShellHost.LastHostCallArgs);
            Assert.Contains("\"key\":\"k\"", FakeShellHost.LastHostCallArgs);
            Assert.Contains("\"reason\":\"Unlock your secret\"", FakeShellHost.LastHostCallArgs);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    [Fact]
    public async Task Delete_CarriesDeleteAction_KeyOnly()
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallStatus = (int)SecureStorageStatus.Ok;
            await bridge.DeleteSecretAsync("k");
            Assert.Contains("\"action\":\"delete\"", FakeShellHost.LastHostCallArgs);
            Assert.Contains("\"key\":\"k\"", FakeShellHost.LastHostCallArgs);
            Assert.DoesNotContain("\"value\"", FakeShellHost.LastHostCallArgs);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    // ── Ok parses the {"value":…} payload (the SECOND user of the 9.0 channel) ──

    [Theory]
    [InlineData("get")]
    [InlineData("getWithAuth")]
    public async Task Ok_ParsesTheValuePayload(string via)
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallStatus = (int)SecureStorageStatus.Ok;
            FakeShellHost.HostCallPayloadJson = """{"value":"hunter2"}""";

            SecretResult result = via == "get"
                ? await bridge.GetSecretAsync("k")
                : await bridge.GetSecretWithAuthAsync("k", "reason");

            Assert.Equal(SecureStorageStatus.Ok, result.Status);
            Assert.Equal("hunter2", result.Value);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    // ── DENIAL IS DATA: a non-Ok get RETURNS a null-value status, no throw/hang ──

    [Theory]
    [InlineData(SecureStorageStatus.NotFound)]
    [InlineData(SecureStorageStatus.AuthFailed)]
    [InlineData(SecureStorageStatus.Unavailable)]
    [InlineData(SecureStorageStatus.Error)]
    public async Task NonOkGet_ReturnsStatus_NullValue_NoThrow_NoHang(SecureStorageStatus status)
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallStatus = (int)status;
            FakeShellHost.HostCallPayloadJson = null; // every non-Ok carries none

            SecretResult result = await bridge.GetSecretWithAuthAsync("k", "reason")
                .AsTask().WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(status, result.Status);
            Assert.Null(result.Value);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    // ── The wire mapping for set/delete: each integer maps; OOR → Error ───────

    [Theory]
    [InlineData(0, SecureStorageStatus.Ok)]
    [InlineData(1, SecureStorageStatus.NotFound)]
    [InlineData(2, SecureStorageStatus.AuthFailed)]
    [InlineData(3, SecureStorageStatus.Unavailable)]
    [InlineData(4, SecureStorageStatus.Error)]
    [InlineData(99, SecureStorageStatus.Error)]  // out-of-range → Error, still data
    [InlineData(-1, SecureStorageStatus.Error)]
    public async Task WireStatusInteger_MapsToTypedStatus(int wire, SecureStorageStatus expected)
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallStatus = wire;
            Assert.Equal(expected, await bridge.SetSecretAsync("k", "v", requireAuth: false));
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    // ── The 8 KB cap, enforced at the .NET boundary (never crosses, never crashes) ─

    [Fact]
    public async Task OversizeValue_ReturnsError_WithoutCrossingTheWire()
    {
        var bridge = RegisterFake();
        try
        {
            // A value one byte over the soft cap. If the cap were dropped this would
            // cross the wire (or crash) instead of statusing — the named oversize
            // mutation reds here.
            string oversize = new('x', SecretResult.MaxValueBytes + 1);
            FakeShellHost.HostCallStatus = (int)SecureStorageStatus.Ok;

            SecureStorageStatus status = await bridge.SetSecretAsync("k", oversize, requireAuth: false)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(SecureStorageStatus.Error, status);
            // Enforced at the boundary: the begin call was never made.
            Assert.Equal(-1, FakeShellHost.LastHostCallRequestId);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    [Fact]
    public async Task ValueAtExactlyTheCap_IsAccepted()
    {
        var bridge = RegisterFake();
        try
        {
            string atCap = new('x', SecretResult.MaxValueBytes);
            FakeShellHost.HostCallStatus = (int)SecureStorageStatus.Ok;

            Assert.Equal(SecureStorageStatus.Ok, await bridge.SetSecretAsync("k", atCap, requireAuth: false));
            Assert.True(FakeShellHost.LastHostCallRequestId > 0); // it DID cross
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    // ── The pending registry is keyed by requestId (reused from 9.0) ──────────

    [Fact]
    public async Task Completion_KeyedByRequestId_ResolvesTheRightCall()
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.AutoCompleteHostCall = false; // hold the call open

            Task<SecureStorageStatus> task = bridge.DeleteSecretAsync("k").AsTask();
            Assert.False(task.IsCompleted);
            long id = FakeShellHost.LastHostCallRequestId;
            Assert.True(id > 0);

            Assert.Equal(1, NativeShellBridge.CompleteHostCall(id + 999, (int)SecureStorageStatus.Ok, null));
            Assert.False(task.IsCompleted);

            Assert.Equal(0, NativeShellBridge.CompleteHostCall(id, (int)SecureStorageStatus.NotFound, null));
            Assert.Equal(SecureStorageStatus.NotFound, await task.WaitAsync(TimeSpan.FromSeconds(5)));
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
                () => bridge.GetSecretAsync("k").AsTask());
            Assert.Contains("not supported", ex.Message);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }
}
