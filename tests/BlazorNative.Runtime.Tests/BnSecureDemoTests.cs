using BlazorNative.Renderer;
using BlazorNative.Runtime;
using BlazorNative.SampleApp;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// BnSecureDemoTests — Phase 9.2 Gate 1 (M9 DoD #4): the biometrics + secure-storage
// surface reaches a mounted component and re-renders its echo. HostSession.TryMount
// ("BnSecureDemo") + Exports.DispatchEventCore — the full host path at the patch
// level (the BnNotificationsDemoTests precedent), with FakeShellHost's HostCallBegin
// driving the status (and, for the Unlock value echo, the {"value":…} payload).
// Denial-as-data is proven end-to-end: an AuthFailed Unlock echoes the status,
// never a fault, never a blank hang; and a successful Unlock echoes the VALUE from
// the payload channel (the named get-parse mutation — reading the wrong key — reds
// this value echo).
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class BnSecureDemoTests
{
    private static (RenderFrame Mount, List<RenderFrame> Frames) MountDemo()
    {
        FakeShellHost.Reset();
        NativeShellBridge.Register(FakeShellHost.BuildCallbacks());
        HostSession.ResetForTests();
        NativeRenderer renderer = HostSession.EnsureSession();
        var frames = new List<RenderFrame>();
        renderer.Frames += (f, _) =>
        {
            frames.Add(f);
            return ValueTask.CompletedTask;
        };
        Assert.Equal(0, HostSession.TryMount("BnSecureDemo"));
        Assert.NotEmpty(frames);
        return (frames[0], frames);
    }

    private static void TearDown()
    {
        HostSession.ResetForTests();
        NativeShellBridge.ResetForTests();
    }

    private static int EchoTextNode(RenderFrame mount)
    {
        var root = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);
        var span = Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.ParentId == root.NodeId && p.NodeType == "text");
        return Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.ParentId == span.NodeId).NodeId;
    }

    private static int ClickHandlerForLabel(RenderFrame mount, string label)
    {
        var text = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(), p => p.Text == label);
        int buttonNode = Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.NodeId == text.NodeId).ParentId!.Value;
        return Assert.Single(mount.Patches.OfType<AttachEventPatch>(),
            p => p.NodeId == buttonNode && p.EventName == "click").HandlerId;
    }

    // ── The mount shape: four buttons + the ready echo ────────────────────────

    [Fact]
    public void Mount_Shape_FourButtons_ReadyEcho()
    {
        var (mount, _) = MountDemo();
        try
        {
            // 5 buttons: the 4 action buttons (Authenticate, Set, Unlock, Delete) plus the trailing
            // "← Back" (#204 — nav parity with the eight pages that already had one).
            Assert.Equal(5, mount.Patches.OfType<CreateNodePatch>().Count(p => p.NodeType == "button"));
            // …and it is WIRED, not just drawn: a back button with no handler is a
            // dead end that looks like an exit, which is worse than no button at all.
            Assert.True(ClickHandlerForLabel(mount, "← Back") > 0,
                "the trailing ← Back button must carry a click handler");
            int echo = EchoTextNode(mount);
            var initial = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(), p => p.NodeId == echo);
            Assert.Equal("ready", initial.Text);
        }
        finally { TearDown(); }
    }

    // ── Authenticate echoes the BiometricStatus (denial as data) ──────────────

    [Fact]
    public void Authenticate_EchoesTheStatus()
    {
        var (mount, frames) = MountDemo();
        try
        {
            FakeShellHost.HostCallStatus = (int)Core.BiometricStatus.LockedOut;
            int echo = EchoTextNode(mount);
            int auth = ClickHandlerForLabel(mount, "Authenticate");

            Assert.Equal(0, Exports.DispatchEventCore((ulong)auth, """{"name":"click"}"""));

            var echoed = Assert.Single(frames[^1].Patches.OfType<ReplaceTextPatch>(),
                p => p.Text == BnSecureDemo.StatusPrefix + "LockedOut");
            Assert.Equal(echo, echoed.NodeId);
        }
        finally { TearDown(); }
    }

    // ── Unlock Ok echoes the VALUE from the payload channel ───────────────────

    [Fact]
    public void Unlock_Ok_EchoesTheValue_FromThePayload()
    {
        var (mount, frames) = MountDemo();
        try
        {
            FakeShellHost.HostCallStatus = (int)Core.SecureStorageStatus.Ok;
            FakeShellHost.HostCallPayloadJson = """{"value":"hunter2"}""";
            int echo = EchoTextNode(mount);
            int unlock = ClickHandlerForLabel(mount, "Unlock");

            Assert.Equal(0, Exports.DispatchEventCore((ulong)unlock, """{"name":"click"}"""));

            var echoed = Assert.Single(frames[^1].Patches.OfType<ReplaceTextPatch>(),
                p => p.Text == BnSecureDemo.ValuePrefix + "hunter2");
            Assert.Equal(echo, echoed.NodeId);
        }
        finally { TearDown(); }
    }

    // ── Unlock AuthFailed echoes the status (never a throw, never a hang) ──────

    [Fact]
    public void Unlock_AuthFailed_EchoesTheStatus_NotAThrow()
    {
        var (mount, frames) = MountDemo();
        try
        {
            FakeShellHost.HostCallStatus = (int)Core.SecureStorageStatus.AuthFailed;
            int echo = EchoTextNode(mount);
            int unlock = ClickHandlerForLabel(mount, "Unlock");

            Assert.Equal(0, Exports.DispatchEventCore((ulong)unlock, """{"name":"click"}"""));

            var echoed = Assert.Single(frames[^1].Patches.OfType<ReplaceTextPatch>(),
                p => p.Text == BnSecureDemo.StatusPrefix + "AuthFailed");
            Assert.Equal(echo, echoed.NodeId);
        }
        finally { TearDown(); }
    }
}
