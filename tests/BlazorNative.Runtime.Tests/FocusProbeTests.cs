using BlazorNative.Renderer;
using BlazorNative.Runtime;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// FocusProbeTests — Phase 4.2 (M4 DoD #4): focus/blur carriers proven
// headless through the real host path — HostSession.TryMount("FocusProbe") +
// Exports.DispatchEventCore — at the patch level every host decodes. The
// .NET side of focus/blur was already complete (ProcessAttribute emits
// AttachEventPatch for any on*, BuildEventArgs maps "focus"/"blur" →
// FocusEventArgs); what was missing is a CARRIER: BnInput's optional
// OnFocus/OnBlur EventCallback<FocusEventArgs> parameters, exercised by the
// FocusProbe scaffolding component. The Kotlin twins (FocusBlurTest.kt,
// Gate 2; FocusBlurAndroidTest.kt, Gate 3) mirror these shapes through the
// published dll and the real EditText.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class FocusProbeTests
{
    private static (RenderFrame Mount, List<RenderFrame> Frames) MountProbe()
    {
        HostSession.ResetForTests();
        NativeRenderer renderer = HostSession.EnsureSession();
        var frames = new List<RenderFrame>();
        renderer.Frames += (f, _) =>
        {
            frames.Add(f);
            return ValueTask.CompletedTask;
        };
        Assert.Equal(0, HostSession.TryMount("FocusProbe"));
        Assert.NotEmpty(frames);
        return (frames[0], frames);
    }

    private static CreateNodePatch InputNode(RenderFrame mount)
        => Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.NodeType == "input");

    /// <summary>The echo BnText's TEXT node, pinned at mount ("" until a
    /// focus/blur lands): root div → the span (text-type child of the root)
    /// → its single child text node (BnDemoTests' structural-walk style).</summary>
    private static int EchoTextNode(RenderFrame mount)
    {
        var root = Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.ParentId is null);
        var span = Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.ParentId == root.NodeId && p.NodeType == "text");
        return Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.ParentId == span.NodeId).NodeId;
    }

    private static int HandlerOn(RenderFrame frame, int nodeId, string eventName)
        => Assert.Single(frame.Patches.OfType<AttachEventPatch>(),
            p => p.NodeId == nodeId && p.EventName == eventName).HandlerId;

    // ── Mount shape ───────────────────────────────────────────────────────────

    [Fact]
    public void Mount_Shape_InputWithFocusAndBlurAttaches_AndEmptyEcho()
    {
        var (mount, _) = MountProbe();
        try
        {
            // The input carries focus + blur attaches (change too — BnInput
            // always wires its bind half): exactly 3, nothing else.
            var input = InputNode(mount);
            _ = HandlerOn(mount, input.NodeId, "focus");
            _ = HandlerOn(mount, input.NodeId, "blur");
            _ = HandlerOn(mount, input.NodeId, "change");
            Assert.Equal(3, mount.Patches.OfType<AttachEventPatch>().Count());

            // The echo text node exists from mount, empty until an event lands.
            int echo = EchoTextNode(mount);
            var initial = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>());
            Assert.Equal(echo, initial.NodeId);
            Assert.Equal("", initial.Text);
        }
        finally
        {
            HostSession.ResetForTests();
        }
    }

    // ── The round trip: dispatch focus/blur → echo re-renders, nodeId-pinned ─

    [Fact]
    public void FocusThenBlurDispatch_EchoTransitions_NodeIdPinned()
    {
        var (mount, frames) = MountProbe();
        try
        {
            var input = InputNode(mount);
            int echo = EchoTextNode(mount);
            int focusHandler = HandlerOn(mount, input.NodeId, "focus");
            int blurHandler = HandlerOn(mount, input.NodeId, "blur");

            // focus → the echo BnText re-renders "focused" on ITS mount-pinned
            // text node (BuildEventArgs maps "focus" → FocusEventArgs).
            Assert.Equal(0, Exports.DispatchEventCore((ulong)focusHandler,
                /*lang=json*/ """{"name":"focus"}"""));
            var focused = Assert.Single(frames[^1].Patches.OfType<ReplaceTextPatch>(),
                p => p.Text == "focused");
            Assert.Equal(echo, focused.NodeId);

            // blur → "blurred", same node.
            Assert.Equal(0, Exports.DispatchEventCore((ulong)blurHandler,
                /*lang=json*/ """{"name":"blur"}"""));
            var blurred = Assert.Single(frames[^1].Patches.OfType<ReplaceTextPatch>(),
                p => p.Text == "blurred");
            Assert.Equal(echo, blurred.NodeId);
        }
        finally
        {
            HostSession.ResetForTests();
        }
    }
}
