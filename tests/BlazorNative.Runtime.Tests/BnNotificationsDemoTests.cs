using BlazorNative.Renderer;
using BlazorNative.Runtime;
using BlazorNative.SampleApp;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// BnNotificationsDemoTests — Phase 9.1 Gate 1 (M9 DoD #3): the notifications
// surface reaches a mounted component and re-renders its echo. HostSession.TryMount
// ("BnNotificationsDemo") + Exports.DispatchEventCore — the full host path at the
// patch level (the BnGeolocationDemoTests precedent), with FakeShellHost's
// HostCallBegin driving a Granted or a denied status. Also the tap-through LANDING
// proof: the page mounts with an "arrived:/notifications" marker (a tap that opens
// the app here is the observable). Denial-as-data is proven end-to-end: a denied
// Show echoes the status, never a fault, never a blank hang.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class BnNotificationsDemoTests
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
        Assert.Equal(0, HostSession.TryMount("BnNotificationsDemo"));
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

    // ── The tap-through landing proof: mounting shows the arrival marker ──────

    [Fact]
    public void Mount_Shape_ThreeButtons_ArrivedEcho()
    {
        var (mount, _) = MountDemo();
        try
        {
            // 4 buttons: the 3 action buttons (Show, Schedule, Cancel) plus the trailing
            // "← Back" (#204 — nav parity with the eight pages that already had one).
            Assert.Equal(4, mount.Patches.OfType<CreateNodePatch>().Count(p => p.NodeType == "button"));
            // …and it is WIRED, not just drawn: a back button with no handler is a
            // dead end that looks like an exit, which is worse than no button at all.
            Assert.True(ClickHandlerForLabel(mount, "← Back") > 0,
                "the trailing ← Back button must carry a click handler");
            int echo = EchoTextNode(mount);
            var initial = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(), p => p.NodeId == echo);
            // The arrival IS the proof: a tap that opens the app here lands on this page,
            // and the marker names the route it landed on.
            Assert.Equal(BnNotificationsDemo.ArrivedPrefix + BnNotificationsDemo.Route, initial.Text);
        }
        finally { TearDown(); }
    }

    // ── Show Granted echoes the status ────────────────────────────────────────

    [Fact]
    public void Show_Granted_EchoesTheStatus_NodeIdPinned()
    {
        var (mount, frames) = MountDemo();
        try
        {
            FakeShellHost.HostCallStatus = (int)Core.NotificationStatus.Granted;
            int echo = EchoTextNode(mount);
            int show = ClickHandlerForLabel(mount, "Show");

            Assert.Equal(0, Exports.DispatchEventCore((ulong)show, """{"name":"click"}"""));

            var echoed = Assert.Single(frames[^1].Patches.OfType<ReplaceTextPatch>(),
                p => p.Text == BnNotificationsDemo.StatusPrefix + "Granted");
            Assert.Equal(echo, echoed.NodeId);
        }
        finally { TearDown(); }
    }

    // ── Show Denied echoes the status (denial as data, not a throw) ───────────

    [Fact]
    public void Show_Denied_EchoesTheStatus_NotAThrow()
    {
        var (mount, frames) = MountDemo();
        try
        {
            FakeShellHost.HostCallStatus = (int)Core.NotificationStatus.Denied;
            int echo = EchoTextNode(mount);
            int show = ClickHandlerForLabel(mount, "Show");

            // Denial is DATA: dispatch returns 0 (handled cleanly), the echo shows
            // the status — never a fault (rc 2), never a blank hang.
            Assert.Equal(0, Exports.DispatchEventCore((ulong)show, """{"name":"click"}"""));

            var echoed = Assert.Single(frames[^1].Patches.OfType<ReplaceTextPatch>(),
                p => p.Text == BnNotificationsDemo.StatusPrefix + "Denied");
            Assert.Equal(echo, echoed.NodeId);
        }
        finally { TearDown(); }
    }
}
