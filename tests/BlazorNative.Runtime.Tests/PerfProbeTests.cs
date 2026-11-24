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

                // The page's contract (owner decision D1): a click is exactly 51 ReplaceText
                // patches, the count label and every one of the 50 rows, plus the frame's
                // commit marker, nothing else.
                RenderPatch[] changes = [.. frames[^1].Patches.Where(p => p is not CommitFramePatch)];
                Assert.Equal(PerfProbe.RowCount + 1, changes.Length);
                Assert.Equal(51, changes.Length);
                Assert.All(changes, p => Assert.IsType<ReplaceTextPatch>(p));
                texts.Add([.. changes.Cast<ReplaceTextPatch>().Select(p => p.Text)]);
            }

            // The settle signal the device tests wait for, unchanged and unique: exactly one
            // node reads "Count: <n>", and every row carries the new count too.
            for (int i = 1; i <= 5; i++)
            {
                Assert.Single(texts[i - 1], t => t == $"Count: {i}");
                Assert.Single(texts[i - 1], t => t.StartsWith("Count: ", StringComparison.Ordinal));
                for (int row = 0; row < PerfProbe.RowCount; row++)
                    Assert.Contains($"Row {row} count {i}", texts[i - 1]);
            }

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
