using BlazorNative.Core;
using Xunit;

namespace BlazorNative.Renderer.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// BridgeEventTests
//
// Pure managed unit tests for WasiBridge.DispatchEventCore — the managed
// entry point the [UnmanagedCallersOnly] DispatchEventNative export
// delegates to. Bypasses the unmanaged-callback boundary so these run in
// milliseconds with no WASI publish involved.
//
// See docs/plans/2026-05-25-phase-2.0-design.md (Layer 1 testing).
// ─────────────────────────────────────────────────────────────────────────────

[Collection("WasiBridge")]
public sealed class BridgeEventTests
{
    [Fact]
    public void DispatchEventCore_WithSyncSubscriber_FiresAndDoesNotThrow()
    {
        using var bridge = new WasiBridge();
        var received = new List<NativeEvent>();
        bridge.NativeEvents += e => received.Add(e);

        WasiBridge.DispatchEventCore("test", "payload-1");

        Assert.Single(received);
        Assert.Equal("test", received[0].Name);
        Assert.Equal("payload-1", received[0].Payload);
    }

    [Fact]
    public void DispatchEventCore_WithMultipleSubscribers_FiresAll()
    {
        using var bridge = new WasiBridge();
        var a = 0;
        var b = 0;
        bridge.NativeEvents += _ => a++;
        bridge.NativeEvents += _ => b++;

        WasiBridge.DispatchEventCore("x", null);

        Assert.Equal(1, a);
        Assert.Equal(1, b);
    }

    [Fact]
    public void DispatchEventCore_OneSubscriberThrows_OthersStillFire()
    {
        using var bridge = new WasiBridge();
        var ran = false;
        bridge.NativeEvents += _ => throw new InvalidOperationException("boom");
        bridge.NativeEvents += _ => ran = true;

        // Must NOT throw — DispatchEventCore catches per-handler.
        WasiBridge.DispatchEventCore("x", null);

        Assert.True(ran, "Second subscriber should fire even when the first threw.");
    }

    [Fact]
    public void DispatchEventCore_AfterUnsubscribe_DoesNotFire()
    {
        using var bridge = new WasiBridge();
        var ran = false;
        Action<NativeEvent> probe = _ => ran = true;
        bridge.NativeEvents += probe;
        bridge.NativeEvents -= probe;

        WasiBridge.DispatchEventCore("x", null);

        Assert.False(ran);
    }

    [Fact]
    public void DevHostBridge_InjectEvent_OneSubscriberThrows_OthersStillFire()
    {
        // Symmetric coverage for DevHostBridge's RaiseNativeEvent helper —
        // near-duplicate of WasiBridge.DispatchEventCore but worth pinning
        // separately so the two implementations don't drift silently.
        using var bridge = new DevHostBridge();
        var ran = false;
        bridge.NativeEvents += _ => throw new InvalidOperationException("boom");
        bridge.NativeEvents += _ => ran = true;

        bridge.InjectEvent("x", null);

        Assert.True(ran, "Second subscriber should fire even when the first threw.");
    }
}
