using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorNative.Renderer.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// FrameSinkTests — Phase 3.0d Task 4
//
// NativeRenderer.FrameSink is the host-pluggable frame transport: when set,
// DispatchFrame hands the RenderFrame to the sink instead of writing the
// [FRAME] JSON line to stdout; when null, the stdout fallback stays byte-for-
// byte identical to the Phase 2.4 transport (WasiHost depends on it until
// Phase 3.0e deletes the WASM era).
//
// Console note: these tests swap Console.Out, which is PROCESS-GLOBAL state.
// TrimSafetyTests deliberately avoids Console.SetOut for exactly that reason
// (fragile under parallel runs). We accept it here because stdout IS the
// contract under test — and keep both tests in this single class, which xUnit
// executes sequentially (tests within one class never run in parallel).
// OTHER classes still run in parallel, though, and their default-sink mounts
// write [FRAME] lines into whatever Console.Out currently is — including our
// swapped writer. So every assertion below is keyed on this probe's unique
// "sink-probe" text, never on "any [FRAME] line at all".
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
        // Same service surface as TrimSafetyTests (production WasiHost DI).
        var services = new ServiceCollection().AddBlazorNativeRenderer();
        return services.BuildServiceProvider().GetRequiredService<NativeRenderer>();
    }

    [Fact]
    public async Task FrameSink_WhenSet_ReceivesFrame_AndSuppressesStdout()
    {
        using var renderer = BuildRenderer();

        RenderFrame? captured = null;
        renderer.FrameSink = f => captured = f;

        var original = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            await renderer.MountAsync<SinkProbe>(ParameterView.Empty);
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.NotNull(captured);
        Assert.NotEmpty(captured!.Patches);
        // No [FRAME] line for OUR probe's frame (parallel tests may write
        // their own [FRAME] lines into the swapped writer — see header).
        Assert.DoesNotContain(
            writer.ToString().Split('\n'),
            l => l.StartsWith("[FRAME]", StringComparison.Ordinal) && l.Contains("sink-probe"));
    }

    [Fact]
    public async Task FrameSink_WhenNull_EmitsFrameLineToStdout()
    {
        using var renderer = BuildRenderer(); // FrameSink left null

        var original = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            await renderer.MountAsync<SinkProbe>(ParameterView.Empty);
        }
        finally
        {
            Console.SetOut(original);
        }

        // Exactly the Phase 2.4 contract: a "[FRAME] {json}" line whose JSON
        // tail round-trips through RendererJsonContext. Keyed on the probe's
        // unique text because parallel tests write their own [FRAME] lines
        // into the swapped writer (see header).
        var frameLines = writer.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.StartsWith("[FRAME] ", StringComparison.Ordinal) && l.Contains("sink-probe"))
            .ToList();

        var line = Assert.Single(frameLines);
        var frame = JsonSerializer.Deserialize(
            line["[FRAME] ".Length..], RendererJsonContext.Default.RenderFrame);
        Assert.NotNull(frame);
        Assert.NotEmpty(frame!.Patches);
        Assert.Contains(frame.Patches.OfType<ReplaceTextPatch>(), p => p.Text == "sink-probe");
    }
}
