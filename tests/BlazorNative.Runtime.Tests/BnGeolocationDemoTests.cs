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

    // The demo now hangs TWO text nodes off root — the echo, then a TRAILING accuracy
    // line (issue #169). They can no longer be told apart by uniqueness, so they are
    // pinned by ORDER: node ids are allocated in render order (NativeWidgetTree._nextNodeId
    // ascends), and the echo (BuildRenderTree sequence 30) is created before the accuracy
    // line (sequence 40) — so ascending node id = [echo, accuracy]. This is the node-id
    // selection the issue calls for, replacing the Assert.Single-on-uniqueness pin.
    private static int EchoTextNode(RenderFrame mount) => TextNodeAt(mount, 0);
    private static int AccuracyTextNode(RenderFrame mount) => TextNodeAt(mount, 1);

    private static int TextNodeAt(RenderFrame mount, int ordinal)
    {
        var root = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);
        // The BnText spans (NodeType "text"), root's direct children, ordered by node id
        // (= render order). Ordinal 0 is the echo, 1 is the trailing accuracy line.
        CreateNodePatch span = mount.Patches.OfType<CreateNodePatch>()
            .Where(p => p.ParentId == root.NodeId && p.NodeType == "text")
            .OrderBy(p => p.NodeId)
            .ElementAt(ordinal);
        return Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.ParentId == span.NodeId).NodeId;
    }

    /// <summary>The span (BnText host node) that owns the given text node — its parent.</summary>
    private static int EchoSpan(RenderFrame mount, int textNodeId)
    {
        int spanId = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.NodeId == textNodeId).ParentId!.Value;
        return spanId;
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
    public void Mount_Shape_TwoButtons_EmptyEcho_TrailingEmptyAccuracy()
    {
        var (mount, _) = MountDemo();
        try
        {
            Assert.Equal(2, mount.Patches.OfType<CreateNodePatch>().Count(p => p.NodeType == "button"));

            int echo = EchoTextNode(mount);
            var initial = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(), p => p.NodeId == echo);
            Assert.Equal("", initial.Text);

            // Issue #169: the accuracy line is a SEPARATE text node, empty at mount, placed
            // AFTER the echo — the device suites' "first TextView/UILabel is the echo"
            // selectors depend on that trailing placement. Both spans are direct children of
            // root, and the accuracy span/text are created (and thus appended) AFTER the echo:
            // node ids ascend in render order, so accuracy > echo proves the trailing order.
            int accuracy = AccuracyTextNode(mount);
            var accuracyInitial = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(), p => p.NodeId == accuracy);
            Assert.Equal("", accuracyInitial.Text);
            Assert.True(accuracy > echo, "the accuracy node must be created after the echo (trailing)");

            var root = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);
            int echoSpan = EchoSpan(mount, echo);
            int accuracySpan = EchoSpan(mount, accuracy);
            Assert.Equal(root.NodeId, Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.NodeId == echoSpan).ParentId);
            Assert.Equal(root.NodeId, Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.NodeId == accuracySpan).ParentId);
            Assert.True(accuracySpan > echoSpan, "the accuracy span must be appended after the echo span (trailing)");
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
    public void Locate_Granted_RendersTheAccuracyLine_Trailing_NodeIdPinned()
    {
        var (mount, frames) = MountDemo();
        try
        {
            FakeShellHost.HostCallStatus = 0; // Granted
            FakeShellHost.HostCallPayloadJson =
                """{"lat":"52.3702","lng":"4.8952","accuracy":"12.0","altitude":"3.0","timestamp":"0"}""";
            int accuracy = AccuracyTextNode(mount);
            int locate = ClickHandlerForLabel(mount, "Locate");

            Assert.Equal(0, Exports.DispatchEventCore((ulong)locate, """{"name":"click"}"""));

            // The accuracy value (12.0) reaches the SEPARATE trailing node as "acc:<metres>"
            // — pinned by node id (issue #169), not by uniqueness, and distinct from the echo.
            var accuracyEcho = Assert.Single(frames[^1].Patches.OfType<ReplaceTextPatch>(),
                p => p.Text.StartsWith(BnGeolocationDemo.AccuracyPrefix, StringComparison.Ordinal));
            Assert.Equal(accuracy, accuracyEcho.NodeId);
            Assert.Contains("12", accuracyEcho.Text);
        }
        finally { TearDown(); }
    }

    [Fact]
    public void Locate_Denied_LeavesTheAccuracyLineBlank()
    {
        var (mount, frames) = MountDemo();
        try
        {
            FakeShellHost.HostCallStatus = (int)Core.GeolocationStatus.Denied;
            FakeShellHost.HostCallPayloadJson = null;
            int accuracy = AccuracyTextNode(mount);
            int locate = ClickHandlerForLabel(mount, "Locate");

            Assert.Equal(0, Exports.DispatchEventCore((ulong)locate, """{"name":"click"}"""));

            // Denial-as-data for accuracy too: the accuracy node NEVER carries an "acc:" value
            // on a non-Granted outcome (it stays "" — either unchanged, so no patch, or a patch
            // back to ""). Assert no accuracy patch ever announces a value on this node.
            Assert.DoesNotContain(frames[^1].Patches.OfType<ReplaceTextPatch>(),
                p => p.NodeId == accuracy && p.Text.StartsWith(BnGeolocationDemo.AccuracyPrefix, StringComparison.Ordinal));
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
