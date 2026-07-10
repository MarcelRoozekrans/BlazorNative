using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorNative.Renderer.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// RegionWalkTests — Phase 3.4 Task 1 (the 3.3 MUST-FIX carryover).
//
// Blazor emits Region frames for RenderFragment / CascadingValue ChildContent:
// grouping markers that occupy NO sibling slot — a region's children number
// as if inline in the parent (region-transparent sibling numbering), regions
// nest, and their children can be elements, text, components, or further
// regions. These tests pin that the renderer walks Region frames
// transparently: content renders, re-render edits resolve to the right
// nodes, sibling numbering stays aligned across (nested) region boundaries,
// diff-time region inserts land at consecutive correct slots, and components
// inside regions get their sibling slot + component-parent record (DoD #8).
//
// Harness: same patterns as DiffCursorTests (strict mode, Frames capture,
// dispatch-driven re-renders).
// ─────────────────────────────────────────────────────────────────────────────

public sealed class RegionWalkTests
{
    private static (NativeRenderer Renderer, List<RenderFrame> Frames) BuildRenderer()
    {
        var services = new ServiceCollection().AddBlazorNativeRenderer();
        var renderer = services.BuildServiceProvider().GetRequiredService<NativeRenderer>();
        renderer.StrictErrors = true; // all fixtures run strict (DoD #9)
        var frames = new List<RenderFrame>();
        renderer.Frames += (f, _) =>
        {
            frames.Add(f);
            return ValueTask.CompletedTask;
        };
        return (renderer, frames);
    }

    private static Task Click(NativeRenderer renderer, AttachEventPatch attach)
        => renderer.DispatchUiEventAsync(new NativeUiEvent(0, attach.HandlerId, "click", null));

    // ── 1. CascadingValue ChildContent: ELEMENTS render ──────────────────────

    /// <summary>CascadingValue's entire body is `AddContent(0, ChildContent)`
    /// — a Region ROOT at the component's root level. The elements inside
    /// must produce CreateNodePatches parented correctly.</summary>
    private sealed class CascadingElements : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenComponent<CascadingValue<string>>(0);
            b.AddComponentParameter(1, "Value", "dark");
            b.AddComponentParameter(2, "ChildContent", (RenderFragment)(cb =>
            {
                cb.OpenElement(0, "div");
                cb.AddContent(1, "inside-region");
                cb.CloseElement();
            }));
            b.CloseComponent();
        }
    }

    [Fact]
    public async Task CascadingValue_ChildContent_ElementsRender()
    {
        var (renderer, frames) = BuildRenderer();
        using var _ = renderer;

        await renderer.MountAsync<CascadingElements>(ParameterView.Empty);
        Assert.NotEmpty(frames);

        // The region's div renders...
        var div = Assert.Single(frames[0].Patches.OfType<CreateNodePatch>(),
            p => p.NodeType == "view");
        // ...with its text node parented under it (not dropped, not misrooted).
        var text = Assert.Single(frames[0].Patches.OfType<ReplaceTextPatch>(),
            p => p.Text == "inside-region");
        var textCreate = Assert.Single(frames[0].Patches.OfType<CreateNodePatch>(),
            p => p.NodeId == text.NodeId);
        Assert.Equal(div.NodeId, textCreate.ParentId);
    }

    // ── 2. RenderFragment parameter renders inline ───────────────────────────

    private sealed class FragmentHost : ComponentBase
    {
        [Parameter] public RenderFragment? Body { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddContent(1, Body);       // Region INSIDE the div's subtree
            b.CloseElement();
        }
    }

    private sealed class FragmentParent : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenComponent<FragmentHost>(0);
            b.AddComponentParameter(1, "Body", (RenderFragment)(cb =>
            {
                cb.OpenElement(0, "span");
                cb.AddContent(1, "frag");
                cb.CloseElement();
            }));
            b.CloseComponent();
        }
    }

    [Fact]
    public async Task RenderFragment_Parameter_ChildrenRender()
    {
        var (renderer, frames) = BuildRenderer();
        using var _ = renderer;

        await renderer.MountAsync<FragmentParent>(ParameterView.Empty);
        Assert.NotEmpty(frames);

        var hostDiv = Assert.Single(frames[0].Patches.OfType<CreateNodePatch>(),
            p => p.NodeType == "view");
        var fragText = Assert.Single(frames[0].Patches.OfType<ReplaceTextPatch>(),
            p => p.Text == "frag");
        var fragTextCreate = Assert.Single(frames[0].Patches.OfType<CreateNodePatch>(),
            p => p.NodeId == fragText.NodeId);
        var span = Assert.Single(frames[0].Patches.OfType<CreateNodePatch>(),
            p => p.NodeId == fragTextCreate.ParentId);
        // The fragment's span parents under the HOST's div — the region is
        // transparent, not a re-rooting boundary.
        Assert.Equal(hostDiv.NodeId, span.ParentId);
    }

    // ── 3. Re-render edits INSIDE a region resolve to the right node ─────────

    /// <summary>The div's content is a region (inline RenderFragment) holding
    /// a static span and a counter span. The click mutates the counter — the
    /// UpdateText edit's sibling indices number the region children as if
    /// inline in the div.</summary>
    private sealed class RegionTextChanges : ComponentBase
    {
        private int _n;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, () => _n++));
            b.AddContent(2, (RenderFragment)(cb =>
            {
                cb.OpenElement(0, "span");
                cb.AddContent(1, "static");
                cb.CloseElement();
                cb.OpenElement(2, "span");
                cb.AddContent(3, $"n:{_n}");
                cb.CloseElement();
            }));
            b.CloseElement();
        }
    }

    [Fact]
    public async Task Region_ReRender_EditsResolve()
    {
        var (renderer, frames) = BuildRenderer();
        using var _ = renderer;

        await renderer.MountAsync<RegionTextChanges>(ParameterView.Empty);
        Assert.NotEmpty(frames);

        var mountStatic = Assert.Single(frames[0].Patches.OfType<ReplaceTextPatch>(),
            p => p.Text == "static");
        var mountCount = Assert.Single(frames[0].Patches.OfType<ReplaceTextPatch>(),
            p => p.Text == "n:0");
        Assert.NotEqual(mountStatic.NodeId, mountCount.NodeId);
        var attach = Assert.Single(frames[0].Patches.OfType<AttachEventPatch>());

        await Click(renderer, attach);
        Assert.True(frames.Count >= 2, "expected a re-render frame after the click");
        var updated = Assert.Single(frames[^1].Patches.OfType<ReplaceTextPatch>(),
            p => p.Text == "n:1");
        // THE assertion: the edit resolved through region-transparent sibling
        // numbering to the counter span's text node — not the static one.
        Assert.Equal(mountCount.NodeId, updated.NodeId);
    }

    // ── 4. Nested regions + trailing sibling numbering ────────────────────────

    /// <summary>div > region > (region > span "inner"), span "outer-tail",
    /// then a TRAILING sibling element p AFTER the region. All three occupy
    /// consecutive sibling slots in the div (regions occupy none); the click
    /// mutates the trailing p's text — its edit index only resolves if the
    /// nested-region children numbered inline.</summary>
    private sealed class NestedRegionsTrailing : ComponentBase
    {
        private int _n;

        private static RenderFragment Inner => cb =>
        {
            cb.OpenElement(0, "span");
            cb.AddContent(1, "inner");
            cb.CloseElement();
        };

        private static RenderFragment Outer => cb =>
        {
            cb.AddContent(0, Inner);       // nested region
            cb.OpenElement(2, "span");
            cb.AddContent(3, "outer-tail");
            cb.CloseElement();
        };

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, () => _n++));
            b.AddContent(2, Outer);        // outer region
            b.OpenElement(3, "p");         // trailing sibling AFTER the region
            b.AddContent(4, $"tail:{_n}");
            b.CloseElement();
            b.CloseElement();
        }
    }

    [Fact]
    public async Task NestedRegions_SiblingNumbering()
    {
        var (renderer, frames) = BuildRenderer();
        using var _ = renderer;

        var componentId = await renderer.MountAsync<NestedRegionsTrailing>(ParameterView.Empty);
        Assert.NotEmpty(frames);

        int NodeOf(string text)
        {
            var t = Assert.Single(frames[0].Patches.OfType<ReplaceTextPatch>(), p => p.Text == text);
            var c = Assert.Single(frames[0].Patches.OfType<CreateNodePatch>(), p => p.NodeId == t.NodeId);
            return Assert.IsType<int>(c.ParentId);
        }
        var innerSpan = NodeOf("inner");
        var tailSpan = NodeOf("outer-tail");
        var pNode = NodeOf($"tail:0");
        var mountTail = Assert.Single(frames[0].Patches.OfType<ReplaceTextPatch>(),
            p => p.Text == "tail:0");

        // Slot bookkeeping: the div's slot list numbers all three inline —
        // inner span = 0, outer-tail span = 1, trailing p = 2 (regions
        // occupy NO slot).
        var container = frames[0].Patches.OfType<CreateNodePatch>().First(p => p.ParentId is null);
        var tree = renderer.WidgetTree;
        Assert.Equal(3, tree.GetSlotCount(componentId, container.NodeId));
        Assert.Equal(innerSpan, tree.GetSlotAt(componentId, container.NodeId, 0).NodeId);
        Assert.Equal(tailSpan,  tree.GetSlotAt(componentId, container.NodeId, 1).NodeId);
        Assert.Equal(pNode,     tree.GetSlotAt(componentId, container.NodeId, 2).NodeId);

        // Re-render: the trailing p's text edit resolves through index 2.
        var attach = Assert.Single(frames[0].Patches.OfType<AttachEventPatch>());
        await Click(renderer, attach);
        Assert.True(frames.Count >= 2, "expected a re-render frame after the click");
        var updated = Assert.Single(frames[^1].Patches.OfType<ReplaceTextPatch>(),
            p => p.Text == "tail:1");
        Assert.Equal(mountTail.NodeId, updated.NodeId);
    }

    // ── 5. Region content arriving MID-LIST in a re-render diff ──────────────

    /// <summary>Click 1 toggles a two-span fragment ON between two existing
    /// siblings — the region content must be created at consecutive correct
    /// slots (1 and 2), shifting the trailing div to slot 3. Click 2 mutates
    /// the trailing div's text: it only resolves if the insert landed
    /// mid-list, not appended.</summary>
    private sealed class ToggleRegionMidList : ComponentBase
    {
        private bool _show;
        private int _n;

        private static RenderFragment Two => cb =>
        {
            cb.OpenElement(0, "span");
            cb.AddContent(1, "r1");
            cb.CloseElement();
            cb.OpenElement(2, "span");
            cb.AddContent(3, "r2");
            cb.CloseElement();
        };

        private void OnClick()
        {
            if (!_show) _show = true;
            else _n++;
        }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, OnClick));
            b.OpenElement(2, "div");
            b.AddContent(3, "first");
            b.CloseElement();
            if (_show)
                b.AddContent(4, Two);      // region arriving in a DIFF, mid-list
            b.OpenElement(5, "div");
            b.AddContent(6, $"last:{_n}");
            b.CloseElement();
            b.CloseElement();
        }
    }

    [Fact]
    public async Task Region_Prepend_MidList()
    {
        var (renderer, frames) = BuildRenderer();
        using var _ = renderer;

        var componentId = await renderer.MountAsync<ToggleRegionMidList>(ParameterView.Empty);
        Assert.NotEmpty(frames);

        var container = frames[0].Patches.OfType<CreateNodePatch>().First(p => p.ParentId is null);
        var mountLast = Assert.Single(frames[0].Patches.OfType<ReplaceTextPatch>(),
            p => p.Text == "last:0");
        var attach = Assert.Single(frames[0].Patches.OfType<AttachEventPatch>());

        // Click 1: the fragment toggles ON between "first" and "last".
        await Click(renderer, attach);
        Assert.True(frames.Count >= 2, "expected a re-render frame after click 1");
        var r1Text = Assert.Single(frames[^1].Patches.OfType<ReplaceTextPatch>(), p => p.Text == "r1");
        var r2Text = Assert.Single(frames[^1].Patches.OfType<ReplaceTextPatch>(), p => p.Text == "r2");
        var r1Create = Assert.Single(frames[^1].Patches.OfType<CreateNodePatch>(),
            p => p.NodeId == r1Text.NodeId);
        var r2Create = Assert.Single(frames[^1].Patches.OfType<CreateNodePatch>(),
            p => p.NodeId == r2Text.NodeId);
        var r1Span = Assert.Single(frames[^1].Patches.OfType<CreateNodePatch>(),
            p => p.NodeId == r1Create.ParentId);
        var r2Span = Assert.Single(frames[^1].Patches.OfType<CreateNodePatch>(),
            p => p.NodeId == r2Create.ParentId);

        // Both spans parent under the container at consecutive HOST positions
        // 1 and 2 (mid-list — "last" sits after them, so not appends).
        Assert.Equal(container.NodeId, r1Span.ParentId);
        Assert.Equal(container.NodeId, r2Span.ParentId);
        Assert.Equal(1, r1Span.InsertIndex);
        Assert.Equal(2, r2Span.InsertIndex);

        // Slot order in the container: first, r1, r2, last.
        var tree = renderer.WidgetTree;
        Assert.Equal(4, tree.GetSlotCount(componentId, container.NodeId));
        Assert.Equal(r1Span.NodeId, tree.GetSlotAt(componentId, container.NodeId, 1).NodeId);
        Assert.Equal(r2Span.NodeId, tree.GetSlotAt(componentId, container.NodeId, 2).NodeId);

        // Click 2: mutate the trailing div's text — its edit arrives at the
        // POST-INSERT sibling index (3) and must resolve to the mount node.
        await Click(renderer, attach);
        var updated = Assert.Single(frames[^1].Patches.OfType<ReplaceTextPatch>(),
            p => p.Text == "last:1");
        Assert.Equal(mountLast.NodeId, updated.NodeId);
    }

    // ── 6. Component inside a region gets slot + parent record ───────────────

    private sealed class RegionChildCounter : ComponentBase
    {
        private int _n;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "span");
            b.AddAttribute(1, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, () => _n++));
            b.AddContent(2, $"c:{_n}");
            b.CloseElement();
        }
    }

    /// <summary>div > region > (element "before", COMPONENT). The component
    /// sits inside the region: it must occupy sibling slot 1 of the div's
    /// slot list (region-transparent) and gain a component-parent record
    /// rooting its views under the div (DoD #8) — 3.3's documented Region
    /// limitation on the component-parent map.</summary>
    private sealed class ComponentInRegion : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddContent(1, (RenderFragment)(cb =>
            {
                cb.OpenElement(0, "span");
                cb.AddContent(1, "before");
                cb.CloseElement();
                cb.OpenComponent<RegionChildCounter>(2);
                cb.CloseComponent();
            }));
            b.CloseElement();
        }
    }

    [Fact]
    public async Task ComponentInsideRegion_GetsSlotAndParent()
    {
        var (renderer, frames) = BuildRenderer();
        using var _ = renderer;

        var rootId = await renderer.MountAsync<ComponentInRegion>(ParameterView.Empty);
        Assert.NotEmpty(frames);

        var container = frames[0].Patches.OfType<CreateNodePatch>().First(p => p.ParentId is null);

        // The component occupies sibling slot 1 of the div (after "before").
        var tree = renderer.WidgetTree;
        Assert.Equal(2, tree.GetSlotCount(rootId, container.NodeId));
        var componentSlot = tree.GetSlotAt(rootId, container.NodeId, 1);
        Assert.True(componentSlot.IsComponent,
            $"expected a component slot at sibling 1, got {componentSlot}");
        var childId = componentSlot.ComponentId;

        // The component-parent record roots the child under the div.
        Assert.True(tree.TryGetComponentParent(childId, out var parent),
            "component inside region has no component-parent record");
        Assert.Equal(container.NodeId, parent.ParentNodeId);
        Assert.Equal(rootId, parent.ParentComponentId);

        // Its views actually rendered under the div — NOT the host root.
        var childText = Assert.Single(frames[0].Patches.OfType<ReplaceTextPatch>(),
            p => p.Text == "c:0");
        var childTextCreate = Assert.Single(frames[0].Patches.OfType<CreateNodePatch>(),
            p => p.NodeId == childText.NodeId);
        var childSpan = Assert.Single(frames[0].Patches.OfType<CreateNodePatch>(),
            p => p.NodeId == childTextCreate.ParentId);
        Assert.Equal(container.NodeId, childSpan.ParentId);

        // And its OWN re-render resolves against its own slot list.
        var attach = Assert.Single(frames[0].Patches.OfType<AttachEventPatch>());
        await Click(renderer, attach);
        var updated = Assert.Single(frames[^1].Patches.OfType<ReplaceTextPatch>(),
            p => p.Text == "c:1");
        Assert.Equal(childText.NodeId, updated.NodeId);
    }
}
