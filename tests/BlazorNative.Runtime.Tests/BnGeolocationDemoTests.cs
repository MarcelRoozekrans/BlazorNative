using BlazorNative.Renderer;
using BlazorNative.Runtime;
using BlazorNative.SampleApp;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// BnGeolocationDemoTests — Phase 9.0 Gate 1 (M9 DoD #2): the geolocation surface
// reaches a mounted component and re-renders its echo. HostSession.TryMount
// ("BnGeolocationDemo") + Exports.DispatchEventCore — the full host path at the
// patch level (the ClipboardProbeTests precedent), with FakeShellHost's
// HostCallBegin driving a Granted fix or a denial. The worked example of the
// permission pattern proven end-to-end at Gate 1, before any device work.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class BnGeolocationDemoTests
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
        Assert.Equal(0, HostSession.TryMount("BnGeolocationDemo"));
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

    [Fact]
    public void Mount_Shape_TwoButtons_EmptyEcho()
    {
        var (mount, _) = MountDemo();
        try
        {
            Assert.Equal(2, mount.Patches.OfType<CreateNodePatch>().Count(p => p.NodeType == "button"));
            int echo = EchoTextNode(mount);
            var initial = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(), p => p.NodeId == echo);
            Assert.Equal("", initial.Text);
        }
        finally { TearDown(); }
    }

    [Fact]
    public void Locate_Granted_EchoesTheFix_NodeIdPinned()
    {
        var (mount, frames) = MountDemo();
        try
        {
            FakeShellHost.HostCallStatus = 0; // Granted
            FakeShellHost.HostCallPayloadJson =
                """{"lat":"52.3702","lng":"4.8952","accuracy":"12.0","altitude":"3.0","timestamp":"0"}""";
            int echo = EchoTextNode(mount);
            int locate = ClickHandlerForLabel(mount, "Locate");

            Assert.Equal(0, Exports.DispatchEventCore((ulong)locate, """{"name":"click"}"""));

            var echoed = Assert.Single(frames[^1].Patches.OfType<ReplaceTextPatch>(),
                p => p.Text.StartsWith(BnGeolocationDemo.FixPrefix, StringComparison.Ordinal));
            Assert.Equal(echo, echoed.NodeId);
            Assert.Contains("52.3702", echoed.Text);
        }
        finally { TearDown(); }
    }

    [Fact]
    public void Locate_Denied_EchoesTheStatus_NotAThrow()
    {
        var (mount, frames) = MountDemo();
        try
        {
            FakeShellHost.HostCallStatus = (int)Core.GeolocationStatus.Denied;
            FakeShellHost.HostCallPayloadJson = null;
            int echo = EchoTextNode(mount);
            int locate = ClickHandlerForLabel(mount, "Locate");

            // Denial is DATA: dispatch returns 0 (handled cleanly), and the echo
            // shows the status — never a fault (rc 2), never a blank hang.
            Assert.Equal(0, Exports.DispatchEventCore((ulong)locate, """{"name":"click"}"""));

            var echoed = Assert.Single(frames[^1].Patches.OfType<ReplaceTextPatch>(),
                p => p.Text == BnGeolocationDemo.StatusPrefix + "Denied");
            Assert.Equal(echo, echoed.NodeId);
        }
        finally { TearDown(); }
    }
}
