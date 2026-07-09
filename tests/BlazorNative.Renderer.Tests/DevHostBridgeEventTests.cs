using BlazorNative.Core;
using Xunit;

namespace BlazorNative.Renderer.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// DevHostBridgeEventTests
//
// Phase 3.2: relocated from the deleted BridgeEventTests.cs — that file's
// WasiBridge tests died with WasiBridge (their multicast subject is gone),
// but THIS test's subject survives: DevHostBridge keeps its
// IMobileBridge.NativeEvents implementation as the DevHost-facing contract
// (RaiseNativeEvent multicast; BN0014 stays valid).
// ─────────────────────────────────────────────────────────────────────────────

public sealed class DevHostBridgeEventTests
{
    [Fact]
    public void DevHostBridge_InjectEvent_OneSubscriberThrows_OthersStillFire()
    {
        // Pins DevHostBridge's RaiseNativeEvent helper: manual multicast so
        // one subscriber's exception doesn't strand later subscribers in the
        // invocation list (log + continue).
        using var bridge = new DevHostBridge();
        var ran = false;
        bridge.NativeEvents += _ => throw new InvalidOperationException("boom");
        bridge.NativeEvents += _ => ran = true;

        bridge.InjectEvent("x", null);

        Assert.True(ran, "Second subscriber should fire even when the first threw.");
    }
}
