using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZeroAlloc.AsyncEvents;
using ZeroAlloc.TestHelpers;
using BlazorNative.Core;
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
        using var bridge = new DevHostBridge();
        using var renderer = new NativeRenderer(bridge, services);

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

    // Deferred to Milestone 4 (BACKLOG P3 "BlazorNative.Renderer.Tests").
    //
    // The intent: assert the UpdateDisplayAsync walk + PooledList lease stay within a
    // small per-frame allocation budget on steady-state re-renders. The blocker is
    // that triggering a steady-state re-render (StateHasChanged on the existing root
    // component) requires test access to ComponentState / Renderer.RenderRootComponent
    // overloads that aren't part of the public surface we built for M1's MountAsync.
    //
    // Re-mounting on every iteration (the obvious workaround) measures full component-
    // creation cost — ~230 KB/iteration — which is the wrong shape for validating the
    // hot-path zero-alloc design. M4 will plumb StateHasChanged-on-mounted-root through
    // the renderer; this test gets enabled then with a realistic budget (~512 B/call).
    [Fact(Skip = "Requires StateHasChanged on mounted root — deferred to Milestone 4 (BACKLOG P3).")]
    public void RenderWalk_IsAllocationFree_OnSteadyState()
    {
        // Placeholder — see Skip reason. Will be implemented in Milestone 4 using AllocationGate.AssertBudget.
    }

    // Documents a known bug: ProcessFrame's nested-element walk uses the child's
    // absolute frame index as the sibling-key, which then can't be re-found by
    // GetNodeIdBySibling on subsequent diffs. Flat components (HelloComponent)
    // dodge this because every child sits at sibling-index 1. The fix lands with
    // the real widget tree in Milestone 2 (BACKLOG P1 "Native widget mapper").
    [Fact(Skip = "Nested elements lose sibling-key on re-render — fix scheduled for Milestone 2 widget tree (BACKLOG P1 'Native widget mapper'). See NativeRenderer.cs:146-178.")]
    public async Task NestedElements_EmitCreateNodeForEachLevel()
    {
        var services = new ServiceCollection().AddBlazorNativeRenderer().BuildServiceProvider();
        using var bridge = new DevHostBridge();
        using var renderer = new NativeRenderer(bridge, services);

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
