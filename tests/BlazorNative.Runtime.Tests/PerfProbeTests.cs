using BlazorNative.Renderer;
using BlazorNative.Runtime;
using BlazorNative.SampleApp;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// PerfProbeTests — Phase 19.2 Task 3. The measurement tests on Android and iOS
// click PerfProbe's button 25+ times and time each click-to-frame. That only
// means something if ONE click causes EXACTLY ONE frame and every click's frame
// carries the SAME number of patches. This pins both on the production dispatch
// ingress, the way BnFormDemoTests drives it.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class PerfProbeTests
{
    private const string ClickArgs = /*lang=json*/ """{"name":"click"}""";

    [Fact]
    public void OneClick_ProducesExactlyOneFrame_WithFixedPatchCount()
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
        try
        {
            Assert.Equal(0, HostSession.TryMount("PerfProbe"));
            Assert.NotEmpty(frames);
            RenderFrame mount = frames[0];
            AttachEventPatch click = Assert.Single(
                mount.Patches.OfType<AttachEventPatch>(), p => p.EventName == "click");

            var perClick = new List<int>();
            var texts = new List<string[]>();
            for (int i = 1; i <= 5; i++)
            {
                int before = frames.Count;
                Assert.Equal(0, Exports.DispatchEventCore((ulong)click.HandlerId, ClickArgs));
                Assert.Equal(before + 1, frames.Count);
                perClick.Add(frames[^1].Patches.Count());

                // The page's contract: a click is exactly two ReplaceText patches (the
                // count label and row 0) plus the frame's commit marker, nothing else.
                RenderPatch[] changes = [.. frames[^1].Patches.Where(p => p is not CommitFramePatch)];
                Assert.Equal(2, changes.Length);
                Assert.All(changes, p => Assert.IsType<ReplaceTextPatch>(p));
                texts.Add([.. changes.Cast<ReplaceTextPatch>().Select(p => p.Text)]);
            }

            // The settle signal the device tests wait for.
            Assert.Contains("Count: 1", texts[0]);
            Assert.Contains("Count: 2", texts[1]);

            Assert.True(perClick[0] > 0, "a click must change something");
            Assert.All(perClick, n => Assert.Equal(perClick[1], n));
        }
        finally
        {
            HostSession.ResetForTests();
            NativeShellBridge.ResetForTests();
        }
    }
}
