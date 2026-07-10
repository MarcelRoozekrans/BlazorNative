using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorNative.Renderer.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// FrameSinkTests
//
// NativeRenderer.FrameSink is the host-pluggable frame transport: when set,
// DispatchFrame hands the RenderFrame to the sink; when null, there is no
// transport (the Frames event is the test channel). The Phase 2.4 "[FRAME]"
// stdout fallback was deleted with the WASM era (Phase 3.0e).
// ─────────────────────────────────────────────────────────────────────────────

public sealed class FrameSinkTests
{
    /// <summary>Minimal probe: one element + text so a mount emits exactly
    /// one frame with a non-empty patch list.</summary>
    private sealed class SinkProbe : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddContent(1, "sink-probe");
            b.CloseElement();
        }
    }

    private static NativeRenderer BuildRenderer()
    {
        var services = new ServiceCollection().AddBlazorNativeRenderer();
        var renderer = services.BuildServiceProvider().GetRequiredService<NativeRenderer>();
        renderer.StrictErrors = true; // Task 6: all fixtures run strict (DoD #9)
        return renderer;
    }

    [Fact]
    public async Task FrameSink_WhenSet_ReceivesFrame()
    {
        using var renderer = BuildRenderer();

        RenderFrame? captured = null;
        renderer.FrameSink = f => captured = f;

        await renderer.MountAsync<SinkProbe>(ParameterView.Empty);

        Assert.NotNull(captured);
        Assert.NotEmpty(captured!.Patches);
        Assert.Contains(captured.Patches.OfType<ReplaceTextPatch>(), p => p.Text == "sink-probe");
    }
}
