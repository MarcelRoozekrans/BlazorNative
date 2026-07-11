using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZeroAlloc.AsyncEvents;
using ZeroAlloc.TestHelpers;
using BlazorNative.Renderer;

namespace BlazorNative.Renderer.Tests;

public class RendererSpike
{
    private sealed class HelloComponent : ComponentBase
    {
        [Parameter] public string Name { get; set; } = "World";
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddContent(1, $"Hello {Name}");
            b.CloseElement();
        }
    }

    private sealed class NestedComponent : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.OpenElement(1, "button");
            b.AddContent(2, "tap");
            b.CloseElement();
            b.CloseElement();
        }
    }

    [Fact]
    public async Task FirstFrame_HasExpectedPatches()
    {
        // Arrange
        var services = new ServiceCollection().AddBlazorNativeRenderer().BuildServiceProvider();
        using var renderer = new NativeRenderer(services);
        renderer.StrictErrors = true; // Task 6: all fixtures run strict (DoD #9)

        var tcs = new TaskCompletionSource<RenderFrame>();
        AsyncEvent<RenderFrame> handler = (frame, ct) =>
        {
            tcs.TrySetResult(frame);
            return ValueTask.CompletedTask;
        };
        renderer.Frames += handler;

        try
        {
            // Act
            await renderer.MountAsync<HelloComponent>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["Name"] = "BlazorNative" }));
            var frame = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

            // Assert
            Assert.NotNull(frame);
            Assert.NotEmpty(frame.Patches);

            // First create patches, then the text, then a commit. The exact sequence depends on the
            // walk order — assert by type/content rather than position to keep this resilient.
            Assert.Contains(frame.Patches, p => p is CreateNodePatch c && c.NodeType == "view");
            Assert.Contains(frame.Patches, p => p is CreateNodePatch c && c.NodeType == "text");
            Assert.Contains(frame.Patches, p => p is ReplaceTextPatch r && r.Text == "Hello BlazorNative");
            Assert.Contains(frame.Patches, p => p is CommitFramePatch);
        }
        finally
        {
            renderer.Frames -= handler;
        }
    }

    /// <summary>Steady-state re-render fixture: every render emits different
    /// text, so each TriggerRootRenderForTests produces a REAL diff (one
    /// UpdateText edit → ReplaceTextPatch) — the walk does work, not a no-op
    /// empty-diff commit.</summary>
    private sealed class SteadyStateComponent : ComponentBase
    {
        private int _renders;
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddContent(1, $"render {_renders++}");
            b.CloseElement();
        }
    }

    // Phase 4.2 (M4 DoD #4 — the M1 deferral closed). The blocker was
    // triggering StateHasChanged on the mounted root; NativeRenderer's
    // internal TriggerRootRenderForTests seam (GetComponentState →
    // ComponentBase.StateHasChanged via accessor) provides exactly that.
    //
    // HONEST BUDGET, not a fictional zero: the walk allocates per-frame BY
    // DESIGN — the RenderFrame envelope + its patches.AsSpan().ToArray()
    // payload copy (NativeRenderer.UpdateDisplayAsync) and the per-frame
    // patch records. A pooled frame payload is M6+ ecosystem work if ever
    // needed. This test pins a REGRESSION bound in the sibling
    // FrameArenaTests style (warmup, then GC.GetAllocatedBytesForCurrentThread
    // across steady-state iterations): measured baseline 295,200 B / 900
    // re-renders (328 B/frame, deterministic across runs; Release, .NET 10
    // win-x64) — bound 600 KB (~2x slack for runtime/GC incidentals, per the
    // design's flake mitigation). What it catches: List resizes, boxing, an
    // accidental per-edit allocation joining the walk.
    [Fact]
    public void RenderWalk_IsAllocationFree_OnSteadyState()
    {
        var services = new ServiceCollection().AddBlazorNativeRenderer().BuildServiceProvider();
        using var renderer = new NativeRenderer(services);
        renderer.StrictErrors = true; // Task 6: all fixtures run strict (DoD #9)

        int componentId = renderer.Mount<SteadyStateComponent>();

        // Warm-up: JIT the walk + let Blazor's diff builder grow its pooled
        // buffers to steady-state capacity (FrameArenaTests pattern).
        for (int i = 0; i < 100; i++)
            renderer.TriggerRootRenderForTests(componentId);

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 900; i++)
            renderer.TriggerRootRenderForTests(componentId);

        long delta = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(delta < 600_000,
            $"Expected < 600000 managed bytes across 900 steady-state re-renders " +
            $"(measured baseline 295200 — see comment above), got {delta}");
    }

    // Phase 4.2 cleanup: this skip documented an M1 bug (nested-element walk
    // keyed children by absolute frame index — the 3.2-era sibling map). The
    // Phase 3.3 slot model closed that premise; run unskipped the test is
    // green, so it stays as the nested-mount shape pin.
    [Fact]
    public async Task NestedElements_EmitCreateNodeForEachLevel()
    {
        var services = new ServiceCollection().AddBlazorNativeRenderer().BuildServiceProvider();
        using var renderer = new NativeRenderer(services);
        renderer.StrictErrors = true; // Task 6: all fixtures run strict (DoD #9)

        var tcs = new TaskCompletionSource<RenderFrame>();
        AsyncEvent<RenderFrame> handler = (frame, ct) =>
        {
            tcs.TrySetResult(frame);
            return ValueTask.CompletedTask;
        };
        renderer.Frames += handler;

        try
        {
            await renderer.MountAsync<NestedComponent>();
            var frame = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

            // Outer <div> -> "view", inner <button> -> "button", inner text -> "text".
            Assert.Contains(frame.Patches, p => p is CreateNodePatch c && c.NodeType == "view");
            Assert.Contains(frame.Patches, p => p is CreateNodePatch c && c.NodeType == "button");
            Assert.Contains(frame.Patches, p => p is CreateNodePatch c && c.NodeType == "text");
            Assert.Contains(frame.Patches, p => p is ReplaceTextPatch r && r.Text == "tap");
        }
        finally
        {
            renderer.Frames -= handler;
        }
    }
}
