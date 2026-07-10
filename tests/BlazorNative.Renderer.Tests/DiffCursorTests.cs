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

        // Click 1: keyed front insert → PrependFrame(SiblingIndex: 0) under the container.
        await Click(renderer, attach);
        Assert.True(frames.Count >= 2, "expected a re-render frame after click 1");
        var aDiv = Assert.Single(
            frames[^1].Patches.OfType<CreateNodePatch>(), p => p.ParentId == container.NodeId);
        Assert.Single(frames[^1].Patches.OfType<ReplaceTextPatch>(), p => p.Text == "a");

        // Slot order: the new item's slot sits at sibling 0 — BEFORE b and c —
        // and the view-index translation ("InsertIndex-to-be", Task 4's input)
        // places it at host child position 0.
        var tree = renderer.WidgetTree;
        Assert.Equal(aDiv.NodeId,    tree.GetChildAt(componentId, container.NodeId, 0).NodeId);
        Assert.Equal(itemDivs[0],    tree.GetChildAt(componentId, container.NodeId, 1).NodeId);
        Assert.Equal(itemDivs[1],    tree.GetChildAt(componentId, container.NodeId, 2).NodeId);
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
}
