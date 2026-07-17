using BlazorNative.Core;
using BlazorNative.Runtime;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// NotificationBridgeTests — Phase 9.1 Gate 1 (M9 DoD #3): the notifications
// surface on the host CLR, through the REAL function-pointer path
// (NativeShellBridge over FakeShellHost's HostCallBegin, completed via the
// blazornative_host_call_complete export) — the FIRST reuse of the 9.0 generic
// permission-gated ABI.
//
// The reuse is the headline: schedule/show/cancel/request/check all ride the
// EXISTING InvokeHostCallAsync with op=Notifications and the action inside the
// flat JSON — NO struct grow, NO new export (asserted UNCHANGED in
// NotificationsAbiUnchangedTests). Denial is DATA: every non-Granted status
// RETURNS, never throws, never hangs (a bounded await proves the no-hang law
// mechanically). The op constant, the args-JSON shape, and the wire-integer
// mapping (incl. out-of-range → Error) are each pinned; the registry keying and
// old-shell-unsupported paths are reused from geolocation and re-asserted here so
// the notifications lane stands on its own.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class NotificationBridgeTests
{
    private static NotificationSpec Spec(int id = 7, DateTimeOffset? when = null, string? route = "/notifications")
        => new(id, "Hello", "A body", when, route);

    private static NativeShellBridge RegisterFake()
    {
        FakeShellHost.Reset();
        NativeShellBridge.Register(FakeShellHost.BuildCallbacks());
        return new NativeShellBridge();
    }

    // ── The begin call carries op=Notifications + the action-in-JSON payload ──

    [Fact]
    public async Task Show_BeginsWithNotificationsOp_AndShowAction()
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallStatus = (int)NotificationStatus.Granted;
            await bridge.ShowNotificationAsync(Spec());

            Assert.True(FakeShellHost.LastHostCallRequestId > 0);
            Assert.Equal((int)NativeShellBridge.HostCallOp.Notifications, FakeShellHost.LastHostCallOp);
            Assert.Contains("\"action\":\"show\"", FakeShellHost.LastHostCallArgs);
            Assert.Contains("\"id\":\"7\"", FakeShellHost.LastHostCallArgs);
            Assert.Contains("\"title\":\"Hello\"", FakeShellHost.LastHostCallArgs);
            Assert.Contains("\"route\":\"/notifications\"", FakeShellHost.LastHostCallArgs);
            // show carries NO `when`.
            Assert.DoesNotContain("\"when\"", FakeShellHost.LastHostCallArgs);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    [Fact]
    public async Task Schedule_CarriesWhenAsUnixMilliseconds()
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallStatus = (int)NotificationStatus.Granted;
            var when = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
            await bridge.ScheduleNotificationAsync(Spec(when: when));

            Assert.Contains("\"action\":\"schedule\"", FakeShellHost.LastHostCallArgs);
            Assert.Contains("\"when\":\"1700000000000\"", FakeShellHost.LastHostCallArgs);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    [Fact]
    public async Task Cancel_CarriesCancelAction_AndIdOnly()
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallStatus = (int)NotificationStatus.Granted;
            await bridge.CancelNotificationAsync(42);

            Assert.Contains("\"action\":\"cancel\"", FakeShellHost.LastHostCallArgs);
            Assert.Contains("\"id\":\"42\"", FakeShellHost.LastHostCallArgs);
            Assert.DoesNotContain("\"title\"", FakeShellHost.LastHostCallArgs);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    [Fact]
    public async Task RequestAndCheck_CarryTheirActions()
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallStatus = (int)NotificationStatus.Granted;
            await bridge.RequestNotificationPermissionAsync();
            Assert.Contains("\"action\":\"request\"", FakeShellHost.LastHostCallArgs);

            await bridge.CheckNotificationPermissionAsync();
            Assert.Contains("\"action\":\"check\"", FakeShellHost.LastHostCallArgs);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    // ── DENIAL IS DATA: every non-Granted status RETURNS, no throw, no hang ────

    [Theory]
    [InlineData(NotificationStatus.Denied)]
    [InlineData(NotificationStatus.DeniedPermanently)]
    [InlineData(NotificationStatus.Restricted)]
    [InlineData(NotificationStatus.Error)]
    public async Task NonGranted_ReturnsStatus_NoThrow_NoHang(NotificationStatus status)
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallStatus = (int)status;

            // A BOUNDED await: a hang would trip the timeout and redden the test —
            // the denial-as-data law asserted mechanically, not by inspection.
            NotificationStatus result = await bridge.ShowNotificationAsync(Spec())
                .AsTask().WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(status, result);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }

    // ── The wire mapping: each integer maps to its status; OOR → Error ────────

    [Theory]
    [InlineData(0, NotificationStatus.Granted)]
    [InlineData(1, NotificationStatus.Denied)]
    [InlineData(2, NotificationStatus.DeniedPermanently)]
    [InlineData(3, NotificationStatus.Restricted)]
    [InlineData(4, NotificationStatus.Error)]
    [InlineData(99, NotificationStatus.Error)]  // out-of-range host bug → Error, still data
    [InlineData(-1, NotificationStatus.Error)]  // negative is out of range too
    public async Task WireStatusInteger_MapsToTypedStatus(int wire, NotificationStatus expected)
    {
        var bridge = RegisterFake();
        try
        {
            FakeShellHost.HostCallStatus = wire;
            Assert.Equal(expected, await bridge.ShowNotificationAsync(Spec()));
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

            Task<NotificationStatus> task = bridge.ShowNotificationAsync(Spec()).AsTask();
            Assert.False(task.IsCompleted);
            long id = FakeShellHost.LastHostCallRequestId;
            Assert.True(id > 0);

            // A completion for a DIFFERENT id must NOT resolve this call (return 1).
            Assert.Equal(1, NativeShellBridge.CompleteHostCall(id + 999, (int)NotificationStatus.Granted, null));
            Assert.False(task.IsCompleted);

            // The RIGHT id resolves it.
            Assert.Equal(0, NativeShellBridge.CompleteHostCall(id, (int)NotificationStatus.Denied, null));
            Assert.Equal(NotificationStatus.Denied, await task.WaitAsync(TimeSpan.FromSeconds(5)));
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
                () => bridge.ShowNotificationAsync(Spec()).AsTask());
            Assert.Contains("not supported", ex.Message);
        }
        finally { NativeShellBridge.ResetForTests(); }
    }
}
