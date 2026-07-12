using BlazorNative.Core;
using BlazorNative.Renderer;
using BlazorNative.Runtime;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// HostEventProbeTests — Phase 5.1 Gate 1 Task 2 (design §3, M5 DoD #5): a
// host-initiated lifecycle event REACHES a mounted component and re-renders it.
// HostSession.TryMount("HostEventProbe") + Exports.DispatchHostEventCore — the
// full host path at the patch level every host decodes (the FocusProbeTests
// precedent). The Kotlin twin (Gate 2) mirrors it through the published dll; the
// instrumented twin (Gate 3) drives it via ActivityScenario.moveToState.
//
// The probe subscribes to the REAL NativeShellBridge.NativeEvents (the DI
// singleton), so a bridge must be registered before mount — hence FakeShellHost
// + the shared "host-session" collection, same as NavigationTests.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class HostEventProbeTests
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
        Assert.Equal(0, HostSession.TryMount("HostEventProbe"));
        Assert.NotEmpty(frames);
        return (frames[0], frames);
    }

    private static void TearDown()
    {
        HostSession.ResetForTests();
        NativeShellBridge.ResetForTests();
    }

    /// <summary>The echo BnText's TEXT node, pinned at mount ("" until an event
    /// lands): root div → the span (text-type child of the root) → its single
    /// child text node (the FocusProbeTests structural-walk style).</summary>
    private static int EchoTextNode(RenderFrame mount)
    {
        var root = Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.ParentId is null);
        var span = Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.ParentId == root.NodeId && p.NodeType == "text");
        return Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.ParentId == span.NodeId).NodeId;
    }

    // ── Mount shape: empty echo, no event yet ─────────────────────────────────

    [Fact]
    public void Mount_Shape_EmptyEcho()
    {
        var (mount, _) = MountProbe();
        try
        {
            int echo = EchoTextNode(mount);
            var initial = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>());
            Assert.Equal(echo, initial.NodeId);
            Assert.Equal("", initial.Text);
        }
        finally { TearDown(); }
    }

    // ── The round trip: host event → echo re-renders, nodeId-pinned, counts ──

    [Fact]
    public void HostEventDispatch_EchoUpdates_NodeIdPinned_AndIncrements()
    {
        var (mount, frames) = MountProbe();
        try
        {
            int echo = EchoTextNode(mount);

            // onPause → the echo BnText re-renders "onPause (1)" on ITS
            // mount-pinned text node (the host event reached the mounted
            // component and drove a synchronous re-render).
            Assert.Equal(0, Exports.DispatchHostEventCore("onPause", null));
            var first = Assert.Single(frames[^1].Patches.OfType<ReplaceTextPatch>(),
                p => p.Text == "onPause (1)");
            Assert.Equal(echo, first.NodeId);

            // A second event increments the count — same node.
            Assert.Equal(0, Exports.DispatchHostEventCore("onResume", null));
            var second = Assert.Single(frames[^1].Patches.OfType<ReplaceTextPatch>(),
                p => p.Text == "onResume (2)");
            Assert.Equal(echo, second.NodeId);
        }
        finally { TearDown(); }
    }
}
