using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorNative.Renderer.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// DiffCursorTests — Phase 3.2 Gate 3 review follow-up.
//
// Pins the diff-cursor node resolution in NativeRenderer.ProcessRenderTreeDiff
// beyond the Hello shape: an UpdateText in the SECOND of two sibling
// containers forces the cursor through StepIn(1) → UpdateText(0) → StepOut,
// i.e. a NON-ZERO StepIn sibling index. Before the cursor existed (and under
// any regression to sibling-map/frame-index lookups), the ReplaceText would
// resolve to the wrong node while text-only assertions stayed green — the
// exact failure mode Android Gate 3 exposed on Hello.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class DiffCursorTests
{
    /// <summary>Two sibling containers; the click handler mutates only the
    /// SECOND container's text. The lambda target/method pair is identical
    /// across renders, so re-renders diff to a pure UpdateText (no
    /// SetAttribute churn).</summary>
    private sealed class TwoContainers : ComponentBase
    {
        private int _n;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");                       // container A — root sibling 0
            b.AddAttribute(1, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, () => _n++));
            b.AddContent(2, "static-a");
            b.CloseElement();

            b.OpenElement(3, "div");                       // container B — root sibling 1
            b.AddContent(4, $"count: {_n}");               // the ONLY text that changes
            b.CloseElement();
        }
    }

    [Fact]
    public async Task UpdateText_InSecondSiblingContainer_TargetsThatContainersTextNode()
    {
        var services = new ServiceCollection().AddBlazorNativeRenderer();
        using var renderer = services.BuildServiceProvider().GetRequiredService<NativeRenderer>();
        renderer.StrictErrors = true; // Task 6: all fixtures run strict (DoD #9)

        // Frames-event capture pattern (test channel; no FrameSink needed).
        var frames = new List<RenderFrame>();
        renderer.Frames += (f, _) =>
        {
            frames.Add(f);
            return ValueTask.CompletedTask;
        };

        await renderer.MountAsync<TwoContainers>(ParameterView.Empty);
        Assert.NotEmpty(frames);

        // Mount frame: harvest the two text nodes + the click handler.
        ReplaceTextPatch mountCount = Assert.Single(
            frames[0].Patches.OfType<ReplaceTextPatch>(), p => p.Text == "count: 0");
        ReplaceTextPatch mountStatic = Assert.Single(
            frames[0].Patches.OfType<ReplaceTextPatch>(), p => p.Text == "static-a");
        Assert.NotEqual(mountStatic.NodeId, mountCount.NodeId); // distinct targets, or the test proves nothing
        AttachEventPatch attach = Assert.Single(
            frames[0].Patches.OfType<AttachEventPatch>(), p => p.EventName == "click");

        // Re-render via the real event path: handler → StateHasChanged → diff.
        await renderer.DispatchUiEventAsync(new NativeUiEvent(0, attach.HandlerId, "click", null));

        Assert.True(frames.Count >= 2,
            $"expected a synchronous re-render frame, got {frames.Count} frame(s)");
        ReplaceTextPatch updated = Assert.Single(
            frames[^1].Patches.OfType<ReplaceTextPatch>(), p => p.Text == "count: 1");

        // THE assertion: the cursor stepped into root sibling 1 (container B)
        // and resolved ITS text node — not container A's text, not a container.
        Assert.Equal(mountCount.NodeId, updated.NodeId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Phase 3.3 Task 2 — cursor completeness: mid-list inserts land at the
    // edit's SiblingIndex, RemoveFrame trims the slot list, and SetAttribute
    // resolves through the cursor (the batch-relative sibling map is deleted).
    // ─────────────────────────────────────────────────────────────────────────

    private static (NativeRenderer Renderer, List<RenderFrame> Frames) BuildRenderer()
    {
        var services = new ServiceCollection().AddBlazorNativeRenderer();
        var renderer = services.BuildServiceProvider().GetRequiredService<NativeRenderer>();
        renderer.StrictErrors = true; // Task 6: all fixtures run strict (DoD #9)
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

    /// <summary>A keyed item list inside one container div. Click 1 inserts a
    /// new item at the FRONT (keyed diff → a true mid-list PrependFrame at
    /// SiblingIndex 0, not positional UpdateTexts); click 2 mutates the LAST
    /// item's text (its StepIn index only resolves correctly if the front
    /// insert landed at slot 0).</summary>
    private sealed class FrontInsertList : ComponentBase
    {
        private sealed class Item { public required string Key; public required string Text; }

        private readonly List<Item> _items =
        [
            new() { Key = "b", Text = "b" },
            new() { Key = "c", Text = "c" },
        ];
        private int _clicks;

        private void OnClick()
        {
            _clicks++;
            if (_clicks == 1) _items.Insert(0, new Item { Key = "a", Text = "a" });
            else _items[^1].Text = "c2";
        }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, OnClick));
            foreach (var item in _items)
            {
                b.OpenElement(2, "div");
                b.SetKey(item.Key);
                b.AddContent(3, item.Text);
                b.CloseElement();
            }
            b.CloseElement();
        }
    }

    [Fact]
    public async Task PrependFrame_MidList_InsertsAtSiblingIndex()
    {
        var (renderer, frames) = BuildRenderer();
        using var _ = renderer;

        var componentId = await renderer.MountAsync<FrontInsertList>(ParameterView.Empty);
        Assert.NotEmpty(frames);

        // Mount shape: container div, then item divs b and c with their texts.
        var container = frames[0].Patches.OfType<CreateNodePatch>().First(p => p.ParentId is null);
        var itemDivs = frames[0].Patches.OfType<CreateNodePatch>()
            .Where(p => p.ParentId == container.NodeId).Select(p => p.NodeId).ToList();
        Assert.Equal(2, itemDivs.Count);
        var cText = Assert.Single(frames[0].Patches.OfType<ReplaceTextPatch>(), p => p.Text == "c");
        var attach = Assert.Single(frames[0].Patches.OfType<AttachEventPatch>());

        // Mount creates are all genuine appends → InsertIndex -1 (Task 4).
        Assert.All(frames[0].Patches.OfType<CreateNodePatch>(),
            p => Assert.Equal(-1, p.InsertIndex));

        // Click 1: keyed front insert → PrependFrame(SiblingIndex: 0) under the container.
        await Click(renderer, attach);
        Assert.True(frames.Count >= 2, "expected a re-render frame after click 1");
        var aDiv = Assert.Single(
            frames[^1].Patches.OfType<CreateNodePatch>(), p => p.ParentId == container.NodeId);
        Assert.Single(frames[^1].Patches.OfType<ReplaceTextPatch>(), p => p.Text == "a");

        // Task 4 (DoD #10): the mid-list insert carries its HOST view index —
        // the new item div lands at child position 0 of the container.
        Assert.Equal(0, aDiv.InsertIndex);

        // Slot order: the new item's slot sits at sibling 0 — BEFORE b and c —
        // and the view-index translation ("InsertIndex-to-be", Task 4's input)
        // places it at host child position 0.
        var tree = renderer.WidgetTree;
        Assert.Equal(aDiv.NodeId,    tree.GetSlotAt(componentId, container.NodeId, 0).NodeId);
        Assert.Equal(itemDivs[0],    tree.GetSlotAt(componentId, container.NodeId, 1).NodeId);
        Assert.Equal(itemDivs[1],    tree.GetSlotAt(componentId, container.NodeId, 2).NodeId);
        Assert.Equal(0, tree.TranslateToViewIndex(componentId, container.NodeId, 0));
        Assert.Equal(2, tree.TranslateToViewIndex(componentId, container.NodeId, 2));

        // Click 2: mutate the LAST item ("c" → "c2"). Its edit arrives as
        // StepIn(2) relative to the post-insert list — under append-only slot
        // bookkeeping this resolved into the "a" div and rewrote a's text.
        await Click(renderer, attach);
        var updated = Assert.Single(
            frames[^1].Patches.OfType<ReplaceTextPatch>(), p => p.Text == "c2");
        Assert.Equal(cText.NodeId, updated.NodeId);
    }

    /// <summary>Three keyed items; click 1 removes the FIRST, click 2 mutates
    /// the LAST. After the removal, the last item's StepIn index shifts down —
    /// it only resolves if RemoveFrame trimmed the slot list.</summary>
    private sealed class RemoveFirstList : ComponentBase
    {
        private sealed class Item { public required string Key; public required string Text; }

        private readonly List<Item> _items =
        [
            new() { Key = "a", Text = "a" },
            new() { Key = "b", Text = "b" },
            new() { Key = "c", Text = "c" },
        ];
        private int _clicks;

        private void OnClick()
        {
            _clicks++;
            if (_clicks == 1) _items.RemoveAt(0);
            else _items[^1].Text = "c2";
        }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, OnClick));
            foreach (var item in _items)
            {
                b.OpenElement(2, "div");
                b.SetKey(item.Key);
                b.AddContent(3, item.Text);
                b.CloseElement();
            }
            b.CloseElement();
        }
    }

    [Fact]
    public async Task RemoveFrame_TrimsSlots_SoLaterEditsStillResolve()
    {
        var (renderer, frames) = BuildRenderer();
        using var _ = renderer;

        await renderer.MountAsync<RemoveFirstList>(ParameterView.Empty);
        Assert.NotEmpty(frames);

        var container = frames[0].Patches.OfType<CreateNodePatch>().First(p => p.ParentId is null);
        var itemDivs = frames[0].Patches.OfType<CreateNodePatch>()
            .Where(p => p.ParentId == container.NodeId).Select(p => p.NodeId).ToList();
        Assert.Equal(3, itemDivs.Count);
        var cText = Assert.Single(frames[0].Patches.OfType<ReplaceTextPatch>(), p => p.Text == "c");
        var attach = Assert.Single(frames[0].Patches.OfType<AttachEventPatch>());

        // Click 1: remove the first item → RemoveFrame(SiblingIndex: 0).
        await Click(renderer, attach);
        var removed = Assert.Single(frames[^1].Patches.OfType<RemoveNodePatch>());
        Assert.Equal(itemDivs[0], removed.NodeId);

        // Click 2: mutate the LAST item. Its StepIn arrives at the POST-REMOVE
        // sibling index — without the trim it resolved one slot early ("b").
        await Click(renderer, attach);
        var updated = Assert.Single(
            frames[^1].Patches.OfType<ReplaceTextPatch>(), p => p.Text == "c2");
        Assert.Equal(cText.NodeId, updated.NodeId);
    }

    /// <summary>Two sibling divs; the click flips the SECOND div's
    /// backgroundColor. The re-render diff is a single SetAttribute edit at
    /// SiblingIndex 1 — it must resolve through the cursor, not the
    /// batch-relative (componentId, frameIndex) map (which pointed this edit
    /// at the FIRST div, or nowhere, depending on batch layout).</summary>
    private sealed class SecondSiblingStyle : ComponentBase
    {
        private string _color = "red";
        private void OnClick() => _color = "blue";

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");                       // sibling 0
            b.AddAttribute(1, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, OnClick));
            b.AddContent(2, "first");
            b.CloseElement();

            b.OpenElement(3, "div");                       // sibling 1
            b.AddAttribute(4, "backgroundColor", _color);
            b.AddContent(5, "second");
            b.CloseElement();
        }
    }

    [Fact]
    public async Task SetAttribute_OnReRender_ResolvesThroughCursor()
    {
        var (renderer, frames) = BuildRenderer();
        using var _ = renderer;

        await renderer.MountAsync<SecondSiblingStyle>(ParameterView.Empty);
        Assert.NotEmpty(frames);

        // Mount: the style patch identifies the SECOND div's node id.
        var mountStyle = Assert.Single(frames[0].Patches.OfType<SetStylePatch>(),
            p => p.Property == "backgroundColor" && p.Value == "red");
        var attach = Assert.Single(frames[0].Patches.OfType<AttachEventPatch>());

        // Click: _color flips → SetAttribute(SiblingIndex: 1) in the re-render diff.
        await Click(renderer, attach);
        Assert.True(frames.Count >= 2, "expected a re-render frame after the click");
        var updatedStyle = Assert.Single(frames[^1].Patches.OfType<SetStylePatch>());
        Assert.Equal("backgroundColor", updatedStyle.Property);
        Assert.Equal("blue", updatedStyle.Value);

        // THE assertion: the style change landed on the second div — the node
        // the mount styled — not on sibling 0 (the batch-relative map's miss).
        Assert.Equal(mountStyle.NodeId, updatedStyle.NodeId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Phase 3.3 Task 3 — component slots + nested parenting + disposal
    // (DoD #8): component frames occupy a sibling slot (elements AFTER an
    // interleaved component keep their cursor indices); a child component's
    // own diff roots its views under the PARENT component's container node,
    // not the host root; DisposedComponentIDs removes the child's views,
    // slots, and component-parent map entries.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Static single-root child — the interleaving marker.</summary>
    private sealed class ChildBadge : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "span");
            b.AddContent(1, "badge");
            b.CloseElement();
        }
    }

    /// <summary>Root level: element, CHILD COMPONENT, element. The click
    /// mutates text inside the element AFTER the component — its StepIn index
    /// (2) counts the component's sibling slot.</summary>
    private sealed class InterleavedParent : ComponentBase
    {
        private int _n;
        private void OnClick() => _n++;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");                     // root sibling 0
            b.AddAttribute(1, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, OnClick));
            b.AddContent(2, "before");
            b.CloseElement();

            b.OpenComponent<ChildBadge>(3);              // root sibling 1
            b.CloseComponent();

            b.OpenElement(4, "div");                     // root sibling 2
            b.AddContent(5, $"after: {_n}");
            b.CloseElement();
        }
    }

    [Fact]
    public async Task InterleavedComponent_ElementIndicesAfterComponentStayCorrect()
    {
        var (renderer, frames) = BuildRenderer();
        using var _ = renderer;

        await renderer.MountAsync<InterleavedParent>(ParameterView.Empty);
        Assert.NotEmpty(frames);

        var afterText = Assert.Single(
            frames[0].Patches.OfType<ReplaceTextPatch>(), p => p.Text == "after: 0");
        var attach = Assert.Single(frames[0].Patches.OfType<AttachEventPatch>());

        // Click → the parent's diff steps ACROSS the component slot:
        // StepIn(2) / UpdateText(0). Without a component slot the cursor
        // poisoned (slot list held only two entries) and the edit was DROPPED.
        await Click(renderer, attach);
        Assert.True(frames.Count >= 2, "expected a re-render frame after the click");
        var updated = Assert.Single(
            frames[^1].Patches.OfType<ReplaceTextPatch>(), p => p.Text == "after: 1");
        Assert.Equal(afterText.NodeId, updated.NodeId);
    }

    /// <summary>Child with its own state + handler, mounted INSIDE the
    /// parent's div — its views must root under that div (DoD #8), and its
    /// own re-render diffs must resolve against its own slot list.</summary>
    private sealed class ChildCounter : ComponentBase
    {
        private int _n;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "span");
            b.AddAttribute(1, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, () => _n++));
            b.AddContent(2, $"n:{_n}");
            b.CloseElement();
        }
    }

    private sealed class NestedChildParent : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.OpenComponent<ChildCounter>(1);
            b.CloseComponent();
            b.CloseElement();
        }
    }

    [Fact]
    public async Task NestedChild_RootsUnderParentContainerNode_NotHostRoot()
    {
        var (renderer, frames) = BuildRenderer();
        using var _ = renderer;

        await renderer.MountAsync<NestedChildParent>(ParameterView.Empty);
        Assert.NotEmpty(frames);

        var parentDiv = Assert.Single(frames[0].Patches.OfType<CreateNodePatch>(),
            p => p.NodeType == "view");
        var childText = Assert.Single(
            frames[0].Patches.OfType<ReplaceTextPatch>(), p => p.Text == "n:0");
        var childTextCreate = Assert.Single(frames[0].Patches.OfType<CreateNodePatch>(),
            p => p.NodeId == childText.NodeId);
        var childSpanCreate = Assert.Single(frames[0].Patches.OfType<CreateNodePatch>(),
            p => p.NodeId == childTextCreate.ParentId);

        // DoD #8: the child component's root view parents under the PARENT
        // component's container node — NOT the host root (ParentId null).
        Assert.NotNull(childSpanCreate.ParentId);
        Assert.Equal(parentDiv.NodeId, childSpanCreate.ParentId);

        // The child's OWN re-render resolves against its own slot list.
        var attach = Assert.Single(frames[0].Patches.OfType<AttachEventPatch>());
        await Click(renderer, attach);
        var updated = Assert.Single(
            frames[^1].Patches.OfType<ReplaceTextPatch>(), p => p.Text == "n:1");
        Assert.Equal(childText.NodeId, updated.NodeId);
    }

    /// <summary>Click hides the child component → the parent's diff removes
    /// the component's sibling slot (RemoveFrame) and the batch's
    /// DisposedComponentIDs carries the child.</summary>
    private sealed class ToggleChildParent : ComponentBase
    {
        private bool _show = true;
        private void OnClick() => _show = false;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");                     // root sibling 0
            b.AddAttribute(1, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, OnClick));
            b.AddContent(2, "host");
            b.CloseElement();

            if (_show)
            {
                b.OpenComponent<ChildBadge>(3);          // root sibling 1
                b.CloseComponent();
            }
        }
    }

    [Fact]
    public async Task DisposedComponent_RemovesItsViews_Slots_AndMapEntries()
    {
        var (renderer, frames) = BuildRenderer();
        using var rendererScope = renderer;

        var rootId = await renderer.MountAsync<ToggleChildParent>(ParameterView.Empty);
        Assert.NotEmpty(frames);

        // Harvest the child's componentId from the slot bookkeeping (root
        // sibling 1 is its component slot) — no assumption about Blazor's
        // id-assignment order.
        var childId = renderer.WidgetTree.GetSlotAt(rootId, null, 1).ComponentId;

        var badgeText = Assert.Single(
            frames[0].Patches.OfType<ReplaceTextPatch>(), p => p.Text == "badge");
        var badgeTextCreate = Assert.Single(frames[0].Patches.OfType<CreateNodePatch>(),
            p => p.NodeId == badgeText.NodeId);
        var badgeSpanId = Assert.IsType<int>(badgeTextCreate.ParentId); // the child's root view
        var attach = Assert.Single(frames[0].Patches.OfType<AttachEventPatch>());

        // Click → _show = false → RemoveFrame(component slot) in the parent's
        // diff + childId in RenderBatch.DisposedComponentIDs.
        await Click(renderer, attach);
        Assert.True(frames.Count >= 2, "expected a re-render frame after the click");

        // The child's ROOT view is removed on the host.
        var removed = Assert.Single(frames[^1].Patches.OfType<RemoveNodePatch>());
        Assert.Equal(badgeSpanId, removed.NodeId);

        // Bookkeeping is gone: the child's slot lists (its root bucket held
        // the span) and its component-parent map entry.
        var tree = renderer.WidgetTree;
        Assert.Equal(0, tree.GetSlotCount(childId, null));
        Assert.False(tree.TryGetComponentParent(childId, out _));
        Assert.Equal(0, tree.ComponentParentCount);
        // Only the root component's buckets remain: its root level + the div.
        Assert.Equal(2, tree.SlotListCount);
        // The component's sibling slot is trimmed from the parent's root list.
        Assert.Equal(1, tree.GetSlotCount(rootId, null));
    }

    /// <summary>Click removes the wrapper ELEMENT whose subtree contains the
    /// child component — the ancestor's RemoveFrame and the child's disposal
    /// land in the same batch.</summary>
    private sealed class ToggleWrappedChildParent : ComponentBase
    {
        private bool _show = true;
        private void OnClick() => _show = false;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");                     // root sibling 0
            b.AddAttribute(1, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, OnClick));
            b.AddContent(2, "host");
            b.CloseElement();

            if (_show)
            {
                b.OpenElement(3, "div");                 // root sibling 1 — wrapper ELEMENT
                b.OpenComponent<ChildBadge>(4);          //   wrapper child 0 — the component
                b.CloseComponent();
                b.CloseElement();
            }
        }
    }

    [Fact]
    public async Task RemovedElement_WithChildComponentInSubtree_AncestorRemoveEmitted_ChildDisposalTolerated()
    {
        var (renderer, frames) = BuildRenderer();
        using var rendererScope = renderer;

        var rootId = await renderer.MountAsync<ToggleWrappedChildParent>(ParameterView.Empty);
        Assert.NotEmpty(frames);

        // Identify the wrapper div and the badge span inside it.
        var badgeText = Assert.Single(
            frames[0].Patches.OfType<ReplaceTextPatch>(), p => p.Text == "badge");
        var badgeTextCreate = Assert.Single(frames[0].Patches.OfType<CreateNodePatch>(),
            p => p.NodeId == badgeText.NodeId);
        var badgeSpanId = Assert.IsType<int>(badgeTextCreate.ParentId);   // child's root view
        var badgeSpanCreate = Assert.Single(frames[0].Patches.OfType<CreateNodePatch>(),
            p => p.NodeId == badgeSpanId);
        var wrapperId = Assert.IsType<int>(badgeSpanCreate.ParentId);     // the wrapper element (DoD #8 parenting)
        var attach = Assert.Single(frames[0].Patches.OfType<AttachEventPatch>());

        // Harvest the child's componentId from the wrapper's slot bucket.
        var childId = renderer.WidgetTree.GetSlotAt(rootId, wrapperId, 0).ComponentId;

        // Click → the parent's diff removes the wrapper element (RemoveFrame)
        // AND the batch's DisposedComponentIDs carries the child. Must not throw.
        await Click(renderer, attach);
        Assert.True(frames.Count >= 2, "expected a re-render frame after the click");

        var removes = frames[^1].Patches.OfType<RemoveNodePatch>().Select(p => p.NodeId).ToList();
        // The ANCESTOR element's remove is emitted…
        Assert.Contains(wrapperId, removes);
        // …and the disposed child's root-view remove is ALSO emitted, although
        // the host already detached that view with the wrapper's subtree. This
        // is the documented host contract (ProcessDisposedComponent remarks):
        // hosts must tolerate RemoveNode for nodes inside already-removed
        // subtrees (treat unknown ids as a no-op).
        Assert.Contains(badgeSpanId, removes);

        // Bookkeeping fully cleaned regardless of emission order.
        var tree = renderer.WidgetTree;
        Assert.Equal(0, tree.GetSlotCount(childId, null));
        Assert.False(tree.TryGetComponentParent(childId, out _));
        Assert.Equal(0, tree.ComponentParentCount);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Phase 3.3 Task 5 — DetachEventPatch emission (carryover e): an on*
    // RemoveAttribute resolves the ORIGINAL handlerId through the renderer's
    // (nodeId, eventName) → handlerId registry and emits DetachEventPatch
    // (with EventName — the host stops routing without guessing). Re-attach
    // after detach registers the NEW handlerId; the registry cleans on node
    // removal and component disposal.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Driver div with a permanent handler; target div whose onclick
    /// toggles on every driver click.</summary>
    private sealed class DetachableSecond : ComponentBase
    {
        private bool _attached = true;
        private void OnDriverClick() => _attached = !_attached;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");                       // sibling 0 — driver
            b.AddAttribute(1, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, OnDriverClick));
            b.AddContent(2, "driver");
            b.CloseElement();

            b.OpenElement(3, "div");                       // sibling 1 — target
            if (_attached)
                b.AddAttribute(4, "onclick",
                    EventCallback.Factory.Create<MouseEventArgs>(this, () => { }));
            b.AddContent(5, "target");
            b.CloseElement();
        }
    }

    [Fact]
    public async Task RemoveAttribute_OnEvent_EmitsDetach_AndReattachRegistersNewHandler()
    {
        var (renderer, frames) = BuildRenderer();
        using var rendererScope = renderer;

        await renderer.MountAsync<DetachableSecond>(ParameterView.Empty);
        Assert.NotEmpty(frames);

        // Identify driver/target attaches via their text nodes' parents.
        int NodeOf(string text)
        {
            var t = Assert.Single(frames[0].Patches.OfType<ReplaceTextPatch>(), p => p.Text == text);
            var c = Assert.Single(frames[0].Patches.OfType<CreateNodePatch>(), p => p.NodeId == t.NodeId);
            return Assert.IsType<int>(c.ParentId);
        }
        var driverNode = NodeOf("driver");
        var targetNode = NodeOf("target");
        var driverAttach = Assert.Single(
            frames[0].Patches.OfType<AttachEventPatch>(), p => p.NodeId == driverNode);
        var targetAttach = Assert.Single(
            frames[0].Patches.OfType<AttachEventPatch>(), p => p.NodeId == targetNode);

        // Click 1: toggle OFF → RemoveAttribute("onclick") on the target →
        // DetachEventPatch carrying the ORIGINAL attach handlerId + EventName.
        await Click(renderer, driverAttach);
        Assert.True(frames.Count >= 2, "expected a re-render frame after click 1");
        var detach = Assert.Single(frames[^1].Patches.OfType<DetachEventPatch>());
        Assert.Equal(targetNode, detach.NodeId);
        Assert.Equal(targetAttach.HandlerId, detach.HandlerId);
        Assert.Equal("click", detach.EventName);
        // The detach is a DETACH — not the 3.2-era UpdateProp(onclick, null)
        // that hosts ignored.
        Assert.DoesNotContain(frames[^1].Patches.OfType<UpdatePropPatch>(),
            p => p.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase));

        // Click 2: toggle back ON → a fresh AttachEventPatch with a NEW
        // handlerId (Blazor allocates a new table entry).
        await Click(renderer, driverAttach);
        var reattach = Assert.Single(
            frames[^1].Patches.OfType<AttachEventPatch>(), p => p.NodeId == targetNode);
        Assert.NotEqual(targetAttach.HandlerId, reattach.HandlerId);

        // Click 3: toggle OFF again → the detach carries the RE-ATTACHED
        // handlerId — the registry followed the re-attach.
        await Click(renderer, driverAttach);
        var detach2 = Assert.Single(frames[^1].Patches.OfType<DetachEventPatch>());
        Assert.Equal(targetNode, detach2.NodeId);
        Assert.Equal(reattach.HandlerId, detach2.HandlerId);
        Assert.Equal("click", detach2.EventName);
    }

    /// <summary>Driver div plus a target whose onclick DELEGATE swaps between
    /// two methods (phase 0 → 1), then disappears (phase 2). The swap is a
    /// pure SetAttribute with a fresh handlerId — NO RemoveAttribute.</summary>
    private sealed class SwappingHandler : ComponentBase
    {
        private int _phase;
        private void OnDriverClick() => _phase++;
        private void HandlerA() { }
        private void HandlerB() { }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");                       // sibling 0 — driver
            b.AddAttribute(1, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, OnDriverClick));
            b.AddContent(2, "driver");
            b.CloseElement();

            b.OpenElement(3, "div");                       // sibling 1 — target
            if (_phase == 0)
                b.AddAttribute(4, "onclick",
                    EventCallback.Factory.Create<MouseEventArgs>(this, HandlerA));
            else if (_phase == 1)
                b.AddAttribute(4, "onclick",
                    EventCallback.Factory.Create<MouseEventArgs>(this, HandlerB));
            b.AddContent(5, "target");
            b.CloseElement();
        }
    }

    [Fact]
    public async Task SetAttribute_HandlerReplacement_NoDetach_NextDetachCarriesNewestHandlerId()
    {
        var (renderer, frames) = BuildRenderer();
        using var rendererScope = renderer;

        await renderer.MountAsync<SwappingHandler>(ParameterView.Empty);
        Assert.NotEmpty(frames);

        int NodeOf(string text)
        {
            var t = Assert.Single(frames[0].Patches.OfType<ReplaceTextPatch>(), p => p.Text == text);
            var c = Assert.Single(frames[0].Patches.OfType<CreateNodePatch>(), p => p.NodeId == t.NodeId);
            return Assert.IsType<int>(c.ParentId);
        }
        var driverAttach = Assert.Single(
            frames[0].Patches.OfType<AttachEventPatch>(), p => p.NodeId == NodeOf("driver"));
        var targetNode = NodeOf("target");
        var originalAttach = Assert.Single(
            frames[0].Patches.OfType<AttachEventPatch>(), p => p.NodeId == targetNode);

        // Click 1: HandlerA → HandlerB — a direct SetAttribute replacement.
        // The wire contract (AttachEventPatch remarks): re-attach REPLACES,
        // last wins, and NO DetachEventPatch precedes it.
        await Click(renderer, driverAttach);
        Assert.True(frames.Count >= 2, "expected a re-render frame after click 1");
        var swapped = Assert.Single(
            frames[^1].Patches.OfType<AttachEventPatch>(), p => p.NodeId == targetNode);
        Assert.NotEqual(originalAttach.HandlerId, swapped.HandlerId);
        Assert.Empty(frames[^1].Patches.OfType<DetachEventPatch>());

        // Click 2: the handler disappears → the detach carries the SWAPPED
        // (newest) handlerId, not the mount-time one — the registry's
        // last-wins overwrite followed the SetAttribute replacement.
        await Click(renderer, driverAttach);
        var detach = Assert.Single(frames[^1].Patches.OfType<DetachEventPatch>());
        Assert.Equal(targetNode, detach.NodeId);
        Assert.Equal(swapped.HandlerId, detach.HandlerId);
        Assert.Equal("click", detach.EventName);
    }

    /// <summary>Keyed item divs, each carrying its own onclick; the driver
    /// removes the first item.</summary>
    private sealed class RemovableHandlerList : ComponentBase
    {
        private readonly List<string> _keys = ["a", "b"];
        private void OnDriverClick() => _keys.RemoveAt(0);

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, OnDriverClick));
            foreach (var key in _keys)
            {
                b.OpenElement(2, "div");
                b.SetKey(key);
                b.AddAttribute(3, "onclick",
                    EventCallback.Factory.Create<MouseEventArgs>(this, () => { }));
                b.AddContent(4, key);
                b.CloseElement();
            }
            b.CloseElement();
        }
    }

    [Fact]
    public async Task EventRegistry_CleansOnNodeRemoval()
    {
        var (renderer, frames) = BuildRenderer();
        using var rendererScope = renderer;

        await renderer.MountAsync<RemovableHandlerList>(ParameterView.Empty);
        Assert.NotEmpty(frames);

        // Driver + 2 items = 3 live registrations.
        Assert.Equal(3, renderer.EventRegistrationCount);
        var driverAttach = frames[0].Patches.OfType<AttachEventPatch>().First();

        // Remove the first item (its div carries a handler): the node's
        // registration goes with it — no leak, and no DetachEventPatch either
        // (the node itself is being removed; RemoveNode subsumes detach).
        await Click(renderer, driverAttach);
        Assert.Single(frames[^1].Patches.OfType<RemoveNodePatch>());
        Assert.Empty(frames[^1].Patches.OfType<DetachEventPatch>());
        Assert.Equal(2, renderer.EventRegistrationCount);
    }

    /// <summary>Driver + a disposable child component whose span carries its
    /// own handler.</summary>
    private sealed class ToggleCounterParent : ComponentBase
    {
        private bool _show = true;
        private void OnClick() => _show = false;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, OnClick));
            b.AddContent(2, "host");
            b.CloseElement();

            if (_show)
            {
                b.OpenComponent<ChildCounter>(3);
                b.CloseComponent();
            }
        }
    }

    [Fact]
    public async Task EventRegistry_CleansOnComponentDisposal()
    {
        var (renderer, frames) = BuildRenderer();
        using var rendererScope = renderer;

        await renderer.MountAsync<ToggleCounterParent>(ParameterView.Empty);
        Assert.NotEmpty(frames);

        // Driver + the child's span handler = 2 registrations.
        Assert.Equal(2, renderer.EventRegistrationCount);
        var hostText = Assert.Single(frames[0].Patches.OfType<ReplaceTextPatch>(), p => p.Text == "host");
        var hostCreate = Assert.Single(frames[0].Patches.OfType<CreateNodePatch>(), p => p.NodeId == hostText.NodeId);
        var driverAttach = Assert.Single(frames[0].Patches.OfType<AttachEventPatch>(),
            p => p.NodeId == hostCreate.ParentId);

        // Dispose the child → its node registrations are purged with it.
        await Click(renderer, driverAttach);
        Assert.Equal(1, renderer.EventRegistrationCount);
    }
}
