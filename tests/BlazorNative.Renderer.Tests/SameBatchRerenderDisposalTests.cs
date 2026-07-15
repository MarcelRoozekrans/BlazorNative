using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorNative.Renderer.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// SameBatchRerenderDisposalTests — Phase 7.2 Gate 1 review, Important 1.
//
// THE SHAPE: Blazor can put the SAME component in both UpdatedComponents and
// DisposedComponentIDs of one batch. It needs the child's re-render queued
// BEFORE the parent render that disposes it: ProcessRenderQueue dequeues the
// child first (it is not disposed yet, so it renders and its diff joins
// UpdatedComponents), then the parent's render removes the child's component
// frame — queueing its disposal into the SAME batch.
//
// HOW THIS FILE CONSTRUCTS IT (the public path): the child's own @onclick
// handler calls StateHasChanged() explicitly — queueing the CHILD — and only
// THEN invokes the parent's remove callback (whose ComponentBase
// HandleEventAsync queues the PARENT second). Blazor holds the batch open
// across the handler, so both queue entries drain in that order into one
// UpdateDisplayAsync.
//
// WHY IT MATTERED: disposal pass 1 (Phase 7.2 — removes must precede the
// batch's creates) reads a disposed component's root bucket BEFORE the diffs.
// In this shape the disposed child still gets a FINAL diff, and if that diff
// ADDS a root-level view, pass 1's removes (taken from the pre-diff bucket)
// miss it — a zombie view no patch ever removes. Pass 2's DELTA emission
// (EmitDisposedComponentRemovesDelta) closes it: after the diffs, any node
// still in a disposed component's root bucket that pass 1 did not cover gets
// its RemoveNodePatch at the frame's tail. The tail is safe: the only
// InsertIndex translation sites (ProcessFrame's Element/Text arms) run inside
// the UpdatedComponents loop, so nothing after the diffs translates against
// any bucket — a tail remove can perturb no already-emitted index, and the
// zombie's by-id removal is position-independent.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class SameBatchRerenderDisposalTests
{
    /// <summary>The child: one root-level div (the click surface). When
    /// expanded, its re-render ADDS a second ROOT-LEVEL div — root level is
    /// the point: pass 1 reads the root bucket. The handler queues its OWN
    /// re-render FIRST, then asks the parent to remove it.</summary>
    private sealed class SelfExpandingChild : ComponentBase
    {
        [Parameter] public EventCallback OnRemove { get; set; }

        private bool _expanded;

        private void OnClick()
        {
            _expanded = true;
            StateHasChanged();          // queue MY re-render FIRST…
            _ = OnRemove.InvokeAsync(); // …THEN the parent's, which disposes me
        }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, OnClick));
            b.AddContent(2, "child");
            b.CloseElement();
            if (_expanded)
            {
                b.OpenElement(3, "div");
                b.AddContent(4, "zombie");
                b.CloseElement();
            }
        }
    }

    /// <summary>The parent: [child component, trailing div] in a container.
    /// The trailing sibling survives the disposal, so the mirror's end state
    /// distinguishes "clean" from "zombie left behind".</summary>
    private sealed class DisposingParent : ComponentBase
    {
        private bool _showChild = true;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            if (_showChild)
            {
                b.OpenComponent<SelfExpandingChild>(1);
                b.AddComponentParameter(2, nameof(SelfExpandingChild.OnRemove),
                    EventCallback.Factory.Create(this, () => _showChild = false));
                b.CloseComponent();
            }
            b.OpenElement(3, "div"); // the surviving trailing sibling
            b.CloseElement();
            b.CloseElement();
        }
    }

    /// <summary>Minimal shell mirror (same algorithm as the real shells and
    /// KeyedWindowSlideTests): CreateNode appends on −1 / inserts at
    /// InsertIndex, RemoveNode removes by id — applied in PATCH ORDER.</summary>
    private sealed class ChildOrderMirror
    {
        private readonly Dictionary<int, List<int>> _children = new();
        private readonly Dictionary<int, int> _parentOf = new();

        public void Apply(RenderFrame frame)
        {
            foreach (var patch in frame.Patches)
            {
                switch (patch)
                {
                    case CreateNodePatch c:
                    {
                        var siblings = Of(c.ParentId ?? -1);
                        if (c.InsertIndex < 0) siblings.Add(c.NodeId);
                        else siblings.Insert(c.InsertIndex, c.NodeId);
                        _parentOf[c.NodeId] = c.ParentId ?? -1;
                        break;
                    }
                    case RemoveNodePatch r when _parentOf.TryGetValue(r.NodeId, out var parent):
                        Of(parent).Remove(r.NodeId);
                        _parentOf.Remove(r.NodeId);
                        break;
                }
            }
        }

        public List<int> Of(int parentId)
            => _children.TryGetValue(parentId, out var list)
                ? list
                : _children[parentId] = new List<int>();
    }

    /// <summary>THE PIN. One click: the child re-renders (its dying diff ADDS
    /// a root-level view) and is disposed, in one batch. Every view that diff
    /// created must be removed IN THE SAME FRAME (the pass-2 delta), the
    /// pass-1 removes still precede the creates, and a shell applying the
    /// frame in patch order ends on exactly [trailing div]. Revert the delta
    /// emission and the "zombie" div leaks: created, never removed.</summary>
    [Fact]
    public async Task SameBatchRerenderThenDispose_TheDyingDiffsRootCreates_AreRemovedInTheSameFrame()
    {
        var services = new ServiceCollection().AddBlazorNativeRenderer();
        using var renderer = services.BuildServiceProvider().GetRequiredService<NativeRenderer>();
        renderer.StrictErrors = true;
        var frames = new List<RenderFrame>();
        renderer.Frames += (f, _) =>
        {
            frames.Add(f);
            return ValueTask.CompletedTask;
        };

        var rootId = await renderer.MountAsync<DisposingParent>(ParameterView.Empty);
        Assert.NotEmpty(frames);
        var mount = frames[0];

        var container = Assert.Single(
            mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);
        var attach = Assert.Single(mount.Patches.OfType<AttachEventPatch>());
        var childText = Assert.Single(
            mount.Patches.OfType<ReplaceTextPatch>(), p => p.Text == "child");
        var childRootDiv = Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.NodeId == childText.NodeId).ParentId!.Value;
        var trailDiv = Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.ParentId == container.NodeId && p.NodeId != childRootDiv).NodeId;
        var childComponentId = renderer.WidgetTree
            .GetSlotAt(rootId, container.NodeId, 0).ComponentId;

        var mirror = new ChildOrderMirror();
        mirror.Apply(mount);
        Assert.Equal(new[] { childRootDiv, trailDiv }, mirror.Of(container.NodeId));

        // ── The click: child StateHasChanged (queued 1st) + parent removal
        //    (queued 2nd) → ONE batch where the child is diffed AND disposed.
        await renderer.DispatchUiEventAsync(
            new NativeUiEvent(0, attach.HandlerId, "click", null));
        var frame = frames[^1];
        mirror.Apply(frame);

        // The shape MATERIALIZED: the disposed child's final diff really did
        // create views (the expanded root-level div + its text). If Blazor
        // ever skipped the dying re-render, this guard — not the zombie
        // assertion — is what fails, and the shape is no longer producible.
        var creates = frame.Patches.OfType<CreateNodePatch>().ToList();
        Assert.NotEmpty(creates);
        var zombieText = Assert.Single(
            frame.Patches.OfType<ReplaceTextPatch>(), p => p.Text == "zombie");
        Assert.Contains(creates, p => p.NodeId == zombieText.NodeId);

        // Pass 1 held: the child's PRE-diff root view's remove precedes every
        // create in the frame (the c9d7b1b ordering contract is untouched).
        var ordered = frame.Patches.ToList();
        var firstCreate = ordered.FindIndex(p => p is CreateNodePatch);
        var childRootRemove = ordered.FindIndex(
            p => p is RemoveNodePatch r && r.NodeId == childRootDiv);
        Assert.True(childRootRemove >= 0 && childRootRemove < firstCreate,
            "the disposed child's pre-diff root remove must precede the batch's creates");

        // THE ZOMBIE ASSERTION (pass 2's delta): the ROOT-level view the
        // dying diff created gets its RemoveNodePatch in this same frame.
        // (Its text child rides the subtree — RemoveNode of a root implies
        // the subtree, the same contract disposal has always used; only
        // ROOT-level views get explicit removes.)
        var zombieDiv = Assert.Single(creates, p => p.NodeId == zombieText.NodeId)
            .ParentId!.Value;
        var removedIds = frame.Patches.OfType<RemoveNodePatch>()
            .Select(p => p.NodeId).ToHashSet();
        Assert.Contains(zombieDiv, removedIds);

        // And the shell-truth end state: only the trailing sibling remains —
        // this line alone reddens if the delta emission is reverted (the
        // zombie div stays a child of the container forever).
        Assert.Equal(new[] { trailDiv }, mirror.Of(container.NodeId));

        // Bookkeeping fully purged despite the dying diff's late slot.
        var tree = renderer.WidgetTree;
        Assert.Equal(0, tree.GetSlotCount(childComponentId, null));
        Assert.False(tree.TryGetComponentParent(childComponentId, out _));
    }
}
