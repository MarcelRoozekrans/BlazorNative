using BlazorNative.Core;
using BlazorNative.Renderer;
using BlazorNative.Runtime;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// ClipboardProbeTests — Phase 5.4 Gate 1 (design §3, M5 DoD #6): the clipboard
// write→read round-trip reaches a mounted component and re-renders its echo.
// HostSession.TryMount("ClipboardProbe") + Exports.DispatchEventCore — the full
// host path at the patch level (the FocusProbeTests/HostEventProbeTests
// precedent). The Kotlin twin (Gate 1 JVM) mirrors it through the published dll.
//
// The probe [Inject]s the REAL NativeShellBridge over FakeShellHost's clipboard
// callbacks, so a bridge must be registered before mount — hence FakeShellHost +
// the shared "host-session" collection, same as HostEventProbeTests.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class ClipboardProbeTests
{
    private static (RenderFrame Mount, List<RenderFrame> Frames) MountProbe()
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
        Assert.Equal(0, HostSession.TryMount("ClipboardProbe"));
        Assert.NotEmpty(frames);
        return (frames[0], frames);
    }

    private static void TearDown()
    {
        HostSession.ResetForTests();
        NativeShellBridge.ResetForTests();
    }

    /// <summary>The echo BnText's TEXT node, pinned at mount ("" until a paste):
    /// root div → the single text-type child (the BnText span; the buttons are
    /// "button" nodes) → its child text node.</summary>
    private static int EchoTextNode(RenderFrame mount)
    {
        var root = Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.ParentId is null);
        var span = Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.ParentId == root.NodeId && p.NodeType == "text");
        return Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.ParentId == span.NodeId).NodeId;
    }

    /// <summary>Click handlerId of the button whose label text is
    /// <paramref name="label"/> — the ReplaceText's node → its button parent →
    /// the click AttachEvent on that button.</summary>
    private static int ClickHandlerForLabel(RenderFrame mount, string label)
    {
        var text = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(), p => p.Text == label);
        int textNode = text.NodeId;
        int buttonNode = Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.NodeId == textNode).ParentId!.Value;
        return Assert.Single(mount.Patches.OfType<AttachEventPatch>(),
            p => p.NodeId == buttonNode && p.EventName == "click").HandlerId;
    }

    // ── Mount shape: three buttons + an empty echo ────────────────────────────

    [Fact]
    public void Mount_Shape_ThreeButtons_EmptyEcho()
    {
        var (mount, _) = MountProbe();
        try
        {
            Assert.Equal(3, mount.Patches.OfType<CreateNodePatch>().Count(p => p.NodeType == "button"));
            int echo = EchoTextNode(mount);
            var initial = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(), p => p.NodeId == echo);
            Assert.Equal("", initial.Text);
        }
        finally { TearDown(); }
    }

    // ── The round trip: Copy → Paste → echo shows the copied value ────────────

    [Fact]
    public void Copy_Then_Paste_EchoesClipboard_NodeIdPinned()
    {
        var (mount, frames) = MountProbe();
        try
        {
            int echo = EchoTextNode(mount);
            int copy = ClickHandlerForLabel(mount, "Copy");
            int paste = ClickHandlerForLabel(mount, "Paste");

            // Copy writes the fixed literal to the host clipboard (no echo change).
            Assert.Equal(0, Exports.DispatchEventCore((ulong)copy, """{"name":"click"}"""));
            Assert.Equal(ClipboardProbe.CopyPayload, FakeShellHost.Clipboard);

            // Paste reads it back → the echo BnText re-renders the value on ITS
            // mount-pinned text node (the clipboard round-trip reached the
            // mounted component and drove a synchronous re-render).
            Assert.Equal(0, Exports.DispatchEventCore((ulong)paste, """{"name":"click"}"""));
            var pasted = Assert.Single(frames[^1].Patches.OfType<ReplaceTextPatch>(),
                p => p.Text == ClipboardProbe.CopyPayload);
            Assert.Equal(echo, pasted.NodeId);
        }
        finally { TearDown(); }
    }

    // ── Share forwards the current echo through the bridge ────────────────────

    [Fact]
    public void Share_ForwardsEcho_ThroughBridge()
    {
        var (mount, _) = MountProbe();
        try
        {
            int copy = ClickHandlerForLabel(mount, "Copy");
            int paste = ClickHandlerForLabel(mount, "Paste");
            int share = ClickHandlerForLabel(mount, "Share");

            Assert.Equal(0, Exports.DispatchEventCore((ulong)copy, """{"name":"click"}"""));
            Assert.Equal(0, Exports.DispatchEventCore((ulong)paste, """{"name":"click"}"""));
            Assert.Equal(0, Exports.DispatchEventCore((ulong)share, """{"name":"click"}"""));

            Assert.Equal(ClipboardProbe.CopyPayload, FakeShellHost.LastShared);
        }
        finally { TearDown(); }
    }
}
