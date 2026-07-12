using BlazorNative.Core;
using BlazorNative.Runtime;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// HostEventTests — Phase 5.1 Gate 1 (design §1, M5 DoD #5): the 9th export's
// managed core. Exercises Exports.DispatchHostEventCore (the [UnmanagedCallersOnly]
// wrapper delegates to it — same split as dispatch_event → DispatchEventCore)
// against the REAL NativeShellBridge.NativeEvents multicast (the 3.2 no-op is
// gone). The rc contract mirrors dispatch_event:
//
//   0 = delivered (incl. no subscribers — an unheard lifecycle signal is fine)
//   2 = a subscriber (or its re-render) faulted — CONTAINED (isolation) but
//       surfaced so the host logs loudly
//   3 = malformed: NULL / empty event name (a NULL payload is legal)
//
// The Kotlin twin (Gate 2, HostEventTest.kt) mirrors the firing through the dll;
// the on-device consumer (HostEventProbe, Gate 1 Task 2) proves it re-renders a
// mounted component.
//
// State note: NativeShellBridge holds its event multicast in process-wide static
// state (one bridge per process), so this class shares the "host-session"
// collection and resets the bridge in finally — the same posture as
// NativeShellBridgeTests.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class HostEventTests
{
    private static string CaptureStderr(Action action)
    {
        TextWriter original = Console.Error;
        using var capture = new StringWriter();
        Console.SetError(capture);
        try { action(); }
        finally { Console.SetError(original); }
        return capture.ToString();
    }

    // ── rc 0: delivered to a subscriber ───────────────────────────────────────

    [Fact]
    public void HostEvent_Delivered_FiresSubscriber_Returns0()
    {
        var bridge = new NativeShellBridge();
        NativeEvent? received = null;
        Action<NativeEvent> handler = e => received = e;
        bridge.NativeEvents += handler;
        try
        {
            int rc = Exports.DispatchHostEventCore("onPause", null);

            Assert.Equal(0, rc);
            Assert.NotNull(received);
            Assert.Equal("onPause", received!.Value.Name);
            Assert.Null(received.Value.Payload);
        }
        finally
        {
            bridge.NativeEvents -= handler;
            NativeShellBridge.ResetForTests();
        }
    }

    [Fact]
    public void HostEvent_CarriesPayload_ToSubscriber()
    {
        var bridge = new NativeShellBridge();
        NativeEvent? received = null;
        Action<NativeEvent> handler = e => received = e;
        bridge.NativeEvents += handler;
        try
        {
            int rc = Exports.DispatchHostEventCore("deepLink", "/settings");

            Assert.Equal(0, rc);
            Assert.Equal("deepLink", received!.Value.Name);
            Assert.Equal("/settings", received.Value.Payload);
        }
        finally
        {
            bridge.NativeEvents -= handler;
            NativeShellBridge.ResetForTests();
        }
    }

    // ── rc 0: no subscribers is NOT an error ──────────────────────────────────

    [Fact]
    public void HostEvent_NoSubscribers_Returns0()
    {
        NativeShellBridge.ResetForTests();

        int rc = Exports.DispatchHostEventCore("onResume", null);

        Assert.Equal(0, rc); // an unheard lifecycle signal is delivered-to-none
    }

    // ── rc 2: a subscriber faults — contained (isolation) but surfaced ────────

    [Fact]
    public void HostEvent_OneSubscriberThrows_OthersStillFire_Returns2_Logged()
    {
        // Mirrors DevHostBridgeEventTests: per-subscriber isolation — the first
        // subscriber's throw must not strand the second — AND the fault is
        // surfaced up the rc channel (rc 2) with stderr detail.
        var bridge = new NativeShellBridge();
        bool secondRan = false;
        Action<NativeEvent> bad = _ => throw new InvalidOperationException("boom-lifecycle");
        Action<NativeEvent> good = _ => secondRan = true;
        bridge.NativeEvents += bad;
        bridge.NativeEvents += good;
        try
        {
            int rc = 999; // sentinel
            string stderr = CaptureStderr(() =>
                rc = Exports.DispatchHostEventCore("onPause", null));

            Assert.Equal(2, rc);
            Assert.True(secondRan, "the second subscriber must fire despite the first throwing");
            Assert.Contains("boom-lifecycle", stderr);
            Assert.Contains(nameof(InvalidOperationException), stderr);
        }
        finally
        {
            bridge.NativeEvents -= bad;
            bridge.NativeEvents -= good;
            NativeShellBridge.ResetForTests();
        }
    }

    // ── rc 3: malformed — NULL / empty name ───────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void HostEvent_NullOrEmptyName_Returns3_DoesNotFire(string? name)
    {
        var bridge = new NativeShellBridge();
        bool fired = false;
        Action<NativeEvent> handler = _ => fired = true;
        bridge.NativeEvents += handler;
        try
        {
            int rc = Exports.DispatchHostEventCore(name, "payload-ignored");

            Assert.Equal(3, rc);
            Assert.False(fired, "a malformed (unnamed) host event must not fire subscribers");
        }
        finally
        {
            bridge.NativeEvents -= handler;
            NativeShellBridge.ResetForTests();
        }
    }
}
