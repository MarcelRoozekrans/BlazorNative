using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorNative.Renderer.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// ComponentChainTests — Phase 3.4 Task 4 (found by BnDemo's composition).
//
// A component whose ROOT frame is ANOTHER component (wrapper → inner
// chaining — the natural Bn* pattern: BnThemedPanel renders a BnView) puts
// the inner's component slot in the wrapper's ROOT slot bucket
// (parentNodeId null) — but ProcessFrame's Component arm used to record the
// component-parent map entry with the EMIT parent (the enclosing HOST node).
// The host-index translation chain (HasHostViewsAfter / HostBaseOffset) and
// disposal's RemoveComponentSlot both key IndexOfComponentSlot by the
// recorded ParentNodeId, looked in the WRONG bucket, and fell back to
// "append" — so an inner view rendering AFTER its later siblings (Blazor's
// FIFO render queue) landed at the END of the host container instead of at
// its sibling position.
//
// Fix under test: the Component arm records the SLOT CONTAINER (null at a
// component's root level) and ResolveComponentEmitParent walks the
// component-parent chain to the nearest element container instead of
// reading one hop.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class ComponentChainTests
{
    private static (NativeRenderer Renderer, List<RenderFrame> Frames) BuildRenderer()
    {
        var services = new ServiceCollection().AddBlazorNativeRenderer();
        var renderer = services.BuildServiceProvider().GetRequiredService<NativeRenderer>();
        renderer.StrictErrors = true;
        var frames = new List<RenderFrame>();
        renderer.Frames += (f, _) =>
        {
            frames.Add(f);
            return ValueTask.CompletedTask;
        };
        return (renderer, frames);
    }

    /// <summary>The chained leaf: renders a single span.</summary>
    private sealed class Inner : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "span");
            b.AddContent(1, "inner");
            b.CloseElement();
        }
    }

    /// <summary>The chain link: its ENTIRE tree is another component — no
    /// element of its own, so Inner's slot lives in Wrapper's root bucket.</summary>
    private sealed class Wrapper : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenComponent<Inner>(0);
            b.CloseComponent();
        }
    }

    /// <summary>div > [span "a", Wrapper(→Inner), span "z"]. Blazor's render
    /// queue renders Inner LAST (Wrapper queues it behind nothing else, but
    /// after the div walk created "a" and "z") — Inner's span must insert at
    /// host index 1, between them, not append after "z".</summary>
    private sealed class HostWithChainMidList : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");

            b.OpenElement(10, "span");
            b.AddContent(11, "a");
            b.CloseElement();

            b.OpenComponent<Wrapper>(20);
            b.CloseComponent();

            b.OpenElement(30, "span");
            b.AddContent(31, "z");
            b.CloseElement();

            b.CloseElement();
        }
    }

    [Fact]
    public async Task ComponentChainedAtComponentRoot_InsertsAtItsSiblingPosition()
    {
        var (renderer, frames) = BuildRenderer();
        using var _ = renderer;

        await renderer.MountAsync<HostWithChainMidList>(ParameterView.Empty);
        Assert.NotEmpty(frames);
        var mount = frames[0];

        var container = Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.ParentId is null);

        var innerText = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(),
            p => p.Text == "inner");
        var innerTextCreate = Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.NodeId == innerText.NodeId);
        var innerSpan = Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.NodeId == innerTextCreate.ParentId);

        // The chained component's view roots under the div (emit-parent
        // resolution walks the chain)...
        Assert.Equal(container.NodeId, innerSpan.ParentId);

        // ...and lands at host index 1 — BETWEEN "a" and "z", its sibling
        // position — even though it was created after both (render-queue
        // order). Pre-fix this was -1: an append after "z".
        Assert.Equal(1, innerSpan.InsertIndex);
    }
}
