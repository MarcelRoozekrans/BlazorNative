using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using Xunit;

namespace BlazorNative.Runtime.Tests;

/// <summary>
/// Phase 13.0 Group 1 — the before-picture.
///
/// The item-surface extraction moves ~17 attribute emissions per component from
/// the component into a shared base. Attribute VALUES and NAMES are preserved by
/// construction, but their ORDER in the frame array follows sequence numbers,
/// which this refactor renumbers. Yoga does not care what order styles arrive in,
/// so layout is unaffected — but "unaffected" must be measured, not asserted.
///
/// This test pins the style multiset (node, property) -> value for a page that
/// exercises every item parameter. It is written BEFORE any component moves, so
/// it captures truth rather than the refactor's own output.
/// </summary>
/// <remarks>
/// Task 1 deviation from the brief's literal listing: [Collection("host-session")]
/// added. Every other class here that touches the static HostSession/NativeShellBridge
/// carries it (BnComponentTests et al.) to serialize against the shared state; without
/// it xUnit runs this class's collection in parallel with theirs and the full-suite run
/// showed exactly that — this test and TraceLoggingTests both failing together, gone
/// once this attribute was added. See task-1-report.md.
/// </remarks>
[Collection("host-session")]
public sealed class PatchEquivalenceTests : IDisposable
{
    public void Dispose()
    {
        HostSession.ResetForTests();
        NativeShellBridge.ResetForTests();
    }

    /// <summary>Order-independent projection of a frame's style patches.</summary>
    internal static Dictionary<(int NodeId, string Property), string?> StyleMultiset(RenderFrame frame)
        => frame.Patches
            .OfType<SetStylePatch>()
            .ToDictionary(p => (p.NodeId, p.Property), p => p.Value);

    [Fact]
    public async Task ItemSurface_EmitsEveryParameter_AsAStableMultiset()
    {
        HostSession.ResetForTests();
        NativeRenderer renderer = HostSession.EnsureSession();
        var frames = new List<RenderFrame>();
        renderer.Frames += (f, _) => { frames.Add(f); return ValueTask.CompletedTask; };

        await renderer.MountAsync<ItemSurfaceProbe>();

        Dictionary<(int, string), string?> styles = StyleMultiset(frames[0]);

        // Every one of the 17 item properties reached the wire, with its value.
        Assert.Equal("#ff0000", styles.Single(kv => kv.Key.Item2 == "backgroundColor").Value);
        Assert.Equal("8",       styles.Single(kv => kv.Key.Item2 == "margin").Value);
        Assert.Equal("center",  styles.Single(kv => kv.Key.Item2 == "alignSelf").Value);
        Assert.Equal("1",       styles.Single(kv => kv.Key.Item2 == "flexGrow").Value);
        Assert.Equal("0",       styles.Single(kv => kv.Key.Item2 == "flexShrink").Value);
        Assert.Equal("50%",     styles.Single(kv => kv.Key.Item2 == "flexBasis").Value);
        Assert.Equal("100",     styles.Single(kv => kv.Key.Item2 == "width").Value);
        Assert.Equal("200",     styles.Single(kv => kv.Key.Item2 == "height").Value);
        Assert.Equal("10",      styles.Single(kv => kv.Key.Item2 == "minWidth").Value);
        Assert.Equal("300",     styles.Single(kv => kv.Key.Item2 == "maxWidth").Value);
        Assert.Equal("20",      styles.Single(kv => kv.Key.Item2 == "minHeight").Value);
        Assert.Equal("400",     styles.Single(kv => kv.Key.Item2 == "maxHeight").Value);
        Assert.Equal("absolute", styles.Single(kv => kv.Key.Item2 == "position").Value);
        Assert.Equal("1",       styles.Single(kv => kv.Key.Item2 == "top").Value);
        Assert.Equal("2",       styles.Single(kv => kv.Key.Item2 == "right").Value);
        Assert.Equal("3",       styles.Single(kv => kv.Key.Item2 == "bottom").Value);
        Assert.Equal("4",       styles.Single(kv => kv.Key.Item2 == "left").Value);

        Assert.Equal(17, styles.Count);
    }
}
