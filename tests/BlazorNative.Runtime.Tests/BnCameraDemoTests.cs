using BlazorNative.Renderer;
using BlazorNative.Runtime;
using BlazorNative.SampleApp;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// BnCameraDemoTests — Phase 9.3 Gate 1 (M9 DoD #5): the camera surface reaches a
// mounted component, and — the phase's proof surface — THE CAPABILITIES COMPOSE: a
// captured file:// path becomes a BnImage.Src, headless. HostSession.TryMount
// ("BnCameraDemo") + Exports.DispatchEventCore — the full host path at the patch level
// (the BnSecureDemoTests precedent), with FakeShellHost's HostCallBegin driving the
// status (and, for the composition, the {"path",…} payload).
//
// The composition is asserted headless (before any device work): a Captured completion
// carrying a path drives an UpdateProp `src` onto the DISPLAY IMAGE node — which is a
// DEFINITE (Width+Height) image with ContentMode=Contain (the M6/M7 sizing contract,
// the ledger item discharged). Denial-as-data is proven end-to-end: a Cancelled capture
// echoes the status and sets NO src (a cancel has no file — the named no-path-on-cancel
// contract at the demo level), never a fault, never a blank hang.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class BnCameraDemoTests
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
        Assert.Equal(0, HostSession.TryMount("BnCameraDemo"));
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

    private static int ImageNode(RenderFrame mount)
        => Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.NodeType == "image").NodeId;

    private static int ClickHandlerForLabel(RenderFrame mount, string label)
    {
        var text = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(), p => p.Text == label);
        int buttonNode = Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.NodeId == text.NodeId).ParentId!.Value;
        return Assert.Single(mount.Patches.OfType<AttachEventPatch>(),
            p => p.NodeId == buttonNode && p.EventName == "click").HandlerId;
    }

    private static string? StyleOn(RenderFrame frame, int nodeId, string property)
        => frame.Patches.OfType<SetStylePatch>()
            .Where(p => p.NodeId == nodeId && p.Property == property)
            .Select(p => p.Value)
            .SingleOrDefault();

    // ── The mount shape: two buttons + a definite display image + the ready echo ──

    [Fact]
    public void Mount_Shape_TwoButtons_DefiniteContainImage_ReadyEcho()
    {
        var (mount, _) = MountDemo();
        try
        {
            // 3 buttons: the 2 action buttons (Take Photo, Check) plus the trailing
            // "← Back" (#204 — nav parity with the eight pages that already had one).
            Assert.Equal(3, mount.Patches.OfType<CreateNodePatch>().Count(p => p.NodeType == "button"));
            // …and it is WIRED, not just drawn: a back button with no handler is a
            // dead end that looks like an exit, which is worse than no button at all.
            Assert.True(ClickHandlerForLabel(mount, "← Back") > 0,
                "the trailing ← Back button must carry a click handler");

            // THE DISPLAY IMAGE is DEFINITE (Width+Height) with ContentMode=Contain —
            // never measured, so a multi-megapixel capture cannot reflow the layout
            // (the M6/M7 ledger discharge). ContentMode is a PROP (contentMode=contain),
            // width/height are STYLES.
            int image = ImageNode(mount);
            Assert.Equal(BnCameraDemo.DisplayWidthDp, StyleOn(mount, image, "width"));
            Assert.Equal(BnCameraDemo.DisplayHeightDp, StyleOn(mount, image, "height"));
            Assert.Equal("contain", Assert.Single(mount.Patches.OfType<UpdatePropPatch>(),
                p => p.NodeId == image && p.Name == "contentMode").Value);
            // No source yet — the box is reserved by the declared size, nothing loads.
            Assert.DoesNotContain(mount.Patches.OfType<UpdatePropPatch>(),
                p => p.NodeId == image && p.Name == "src" && p.Value is not null);

            int echo = EchoTextNode(mount);
            var initial = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(), p => p.NodeId == echo);
            Assert.Equal("ready", initial.Text);
        }
        finally { TearDown(); }
    }

    // ── THE COMPOSITION: a captured path becomes the BnImage's Src ────────────────

    [Fact]
    public void TakePhoto_Captured_SetsTheImageSrc_ToThePath_AndEchoesDims()
    {
        var (mount, frames) = MountDemo();
        try
        {
            const string path = "file:///cache/blazornative_captures/p.jpg";
            FakeShellHost.HostCallStatus = (int)Core.CameraStatus.Captured;
            FakeShellHost.HostCallPayloadJson =
                $$"""{"path":"{{path}}","width":"1600","height":"1200","bytes":"204800"}""";
            int image = ImageNode(mount);
            int echo = EchoTextNode(mount);
            int take = ClickHandlerForLabel(mount, "Take Photo");

            Assert.Equal(0, Exports.DispatchEventCore((ulong)take, """{"name":"click"}"""));

            // The capabilities compose: the captured file:// path is now the display
            // image's Src (an UpdateProp `src` on the image node) — the named
            // wrong-key mutation (reading `file`/`data`) reds THIS.
            var src = Assert.Single(frames[^1].Patches.OfType<UpdatePropPatch>(),
                p => p.NodeId == image && p.Name == "src");
            Assert.Equal(path, src.Value);

            // …and the dims are echoed (the file the path names has real bytes).
            var echoed = Assert.Single(frames[^1].Patches.OfType<ReplaceTextPatch>(),
                p => p.NodeId == echo);
            Assert.Equal(BnCameraDemo.CapturedPrefix + "1600x1200:204800", echoed.Text);
        }
        finally { TearDown(); }
    }

    // ── Cancel echoes the status and sets NO src (a cancel has no file) ───────────

    [Fact]
    public void TakePhoto_Cancelled_EchoesStatus_SetsNoImageSrc_NotAThrow()
    {
        var (mount, frames) = MountDemo();
        try
        {
            FakeShellHost.HostCallStatus = (int)Core.CameraStatus.Cancelled;
            FakeShellHost.HostCallPayloadJson = null;
            int image = ImageNode(mount);
            int echo = EchoTextNode(mount);
            int take = ClickHandlerForLabel(mount, "Take Photo");

            // Denial is DATA: dispatch returns 0 (handled cleanly), the echo shows the
            // status, and NO src is set on the image (never a fault, never a hang).
            Assert.Equal(0, Exports.DispatchEventCore((ulong)take, """{"name":"click"}"""));

            var echoed = Assert.Single(frames[^1].Patches.OfType<ReplaceTextPatch>(),
                p => p.NodeId == echo);
            Assert.Equal(BnCameraDemo.StatusPrefix + "Cancelled", echoed.Text);

            Assert.DoesNotContain(
                frames.SelectMany(f => f.Patches).OfType<UpdatePropPatch>(),
                p => p.NodeId == image && p.Name == "src" && p.Value is not null);
        }
        finally { TearDown(); }
    }

    // ── Check echoes availability (no capture UI) ─────────────────────────────────

    [Fact]
    public void Check_EchoesAvailability()
    {
        var (mount, frames) = MountDemo();
        try
        {
            FakeShellHost.HostCallStatus = (int)Core.CameraStatus.Unavailable;
            int echo = EchoTextNode(mount);
            int check = ClickHandlerForLabel(mount, "Check");

            Assert.Equal(0, Exports.DispatchEventCore((ulong)check, """{"name":"click"}"""));

            var echoed = Assert.Single(frames[^1].Patches.OfType<ReplaceTextPatch>(),
                p => p.NodeId == echo);
            Assert.Equal(BnCameraDemo.StatusPrefix + "Unavailable", echoed.Text);
        }
        finally { TearDown(); }
    }
}
