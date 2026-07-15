using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorNative.Renderer.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// KeyedWindowSlideTests — Phase 7.2 Gate 1, Task 1.1 (design decision 4:
// "7.2 owns @key reorders — EMPIRICALLY first").
//
// THE QUESTION: when a keyed WINDOW slides — the exact diff BnList produces on
// every scroll ([A,B,C,D] → [B,C,D,E], and back) — does Blazor's diff emit
// insert/remove edits only, or PermutationListEntry/End?
//
// THE EMPIRICAL ANSWER (this file is the evidence): INSERT/REMOVE ONLY.
// A sliding window preserves the RELATIVE ORDER of every key that survives
// (B,C,D stay in the same order on both sides of the slide), and Blazor's
// keyed diff (RenderTreeDiffBuilder.AppendDiffEntriesForRange) only emits
// PermutationListEntry when keys that exist on BOTH sides CROSS — a true
// reorder, e.g. the two-item swap StrictModeTests pins. An old key absent from
// the new window is a RemoveFrame; a new key absent from the old window is a
// PrependFrame at its sibling index. No crossing, no permutation.
//
// These tests run STRICT: if a permutation ever appeared here, the 7.0
// default: arm would throw "unhandled render-tree edit type
// PermutationListEntry" and redden the dispatch — the test itself is the
// detector, not an assumption. (Verified live: StrictModeTests
// .Strict_UnhandledEditType_KeyedPermutation_FiresTheDefaultArm reddens a
// genuine swap through that arm today.)
//
// CONSEQUENCE FOR THE RENDERER (the design's "if not" branch): the
// PermutationListEntry/End arms are NOT implemented, the loud default: arm
// STAYS — a genuine keyed reorder remains a deliberate, named violation, and
// BnList's window slide never produces one. No wire/ABI move-concept is
// needed for virtualization. (If a future component wants true keyed
// reorders, the assessment lives in the Gate 1 report: a move could NOT be
// remove+insert of the same node id without destroying native view state —
// it would need CreateNode's InsertIndex semantics applied to an EXISTING id,
// i.e. a new patch meaning. That is an ABI conversation, and a sliding
// window — the only customer this milestone has — does not need it.)
//
// The fixture mirrors BnList's content shape exactly: a keyed lead spacer, a
// keyed slice of a larger item list, a keyed trail spacer — so the answer is
// measured on the tree the virtualized list will actually diff.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class KeyedWindowSlideTests
{
    /// <summary>Items A..E rendering a keyed 4-wide window. Click 1 slides the
    /// window forward ([A,B,C,D] → [B,C,D,E]); click 2 slides it back. Spacer
    /// divs with constant keys bracket the slice, exactly like BnList's
    /// lead/trail spacers.</summary>
    private sealed class SlidingWindow : ComponentBase
    {
        private static readonly string[] Items = ["A", "B", "C", "D", "E"];
        private int _start;

        private void OnClick() => _start = _start == 0 ? 1 : 0;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, OnClick));

            b.OpenElement(2, "div");   // the lead spacer — constant key
            b.SetKey("__lead");
            b.CloseElement();

            for (var i = _start; i < _start + 4; i++)
            {
                b.OpenElement(3, "div");
                b.SetKey(Items[i]);
                b.AddContent(4, Items[i]);
                b.CloseElement();
            }

            b.OpenElement(5, "div");   // the trail spacer — constant key
            b.SetKey("__trail");
            b.CloseElement();

            b.CloseElement();
        }
    }

    private static (NativeRenderer Renderer, List<RenderFrame> Frames) BuildRenderer()
    {
        var services = new ServiceCollection().AddBlazorNativeRenderer();
        var renderer = services.BuildServiceProvider().GetRequiredService<NativeRenderer>();
        renderer.StrictErrors = true; // the default: arm THROWS on any permutation
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

    /// <summary>THE FINDING, AS A TEST NAME. Slide forward and back under
    /// strict mode: were a PermutationListEntry ever emitted, the dispatch
    /// would throw through the 7.0 default: arm — so completing at all IS the
    /// empirical answer, and the patch assertions pin its exact shape.</summary>
    [Fact]
    public async Task KeyedWindowSlide_DiffsAsInsertRemoveOnly_NeverPermutations()
    {
        var (renderer, frames) = BuildRenderer();
        using var _ = renderer;

        await renderer.MountAsync<SlidingWindow>(ParameterView.Empty);
        Assert.NotEmpty(frames);

        var container = Assert.Single(
            frames[0].Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);
        var children = frames[0].Patches.OfType<CreateNodePatch>()
            .Where(p => p.ParentId == container.NodeId).Select(p => p.NodeId).ToList();
        // lead spacer + A,B,C,D + trail spacer.
        Assert.Equal(6, children.Count);
        var attach = Assert.Single(frames[0].Patches.OfType<AttachEventPatch>());

        // Harvest the item divs by their text (nodeIds are opaque; text is not).
        int ItemDiv(string text)
        {
            var textNode = frames[0].Patches.OfType<ReplaceTextPatch>()
                .Single(p => p.Text == text).NodeId;
            return frames[0].Patches.OfType<CreateNodePatch>()
                .Single(p => p.NodeId == textNode).ParentId!.Value;
        }
        var aDiv = ItemDiv("A");
        var bDiv = ItemDiv("B");
        var cDiv = ItemDiv("C");
        var dDiv = ItemDiv("D");

        // ── Slide forward: [A,B,C,D] → [B,C,D,E] ────────────────────────────────
        // Completing WITHOUT the strict throw is the empirical answer; the
        // assertions below pin the insert/remove shape it resolved to.
        await Click(renderer, attach);
        var forward = frames[^1];

        // A (the key that left) is REMOVED — one remove, and it is A's div.
        var removed = Assert.Single(forward.Patches.OfType<RemoveNodePatch>());
        Assert.Equal(aDiv, removed.NodeId);

        // E (the key that entered) is INSERTED — a fresh div + its text node,
        // and nothing else is created: B, C, D were NOT torn down and rebuilt.
        // That non-recreation is @key's whole value to BnList (native view
        // state — an EditText's text, focus — survives the slide).
        var creates = forward.Patches.OfType<CreateNodePatch>().ToList();
        Assert.Equal(2, creates.Count); // E's div + E's text node
        var eDiv = Assert.Single(creates, p => p.ParentId == container.NodeId);
        Assert.Single(forward.Patches.OfType<ReplaceTextPatch>(), p => p.Text == "E");
        // E lands BEFORE the trail spacer: sibling slot 5 of
        // [lead, B, C, D, •, trail] — host insert index 4 after A's removal
        // trimmed the list... but Blazor emits the remove and insert in ONE
        // batch; the diff-provided index is what the wire carries. Pin it.
        Assert.Equal(4, eDiv.InsertIndex);

        // ── Slide back: [B,C,D,E] → [A,B,C,D] ───────────────────────────────────
        await Click(renderer, attach);
        var back = frames[^1];

        // E removed, A re-created AT THE FRONT of the slice (sibling 1, after
        // the lead spacer) — a front insert, not a permutation.
        var removedBack = Assert.Single(back.Patches.OfType<RemoveNodePatch>());
        Assert.Equal(eDiv.NodeId, removedBack.NodeId);
        var createsBack = back.Patches.OfType<CreateNodePatch>().ToList();
        Assert.Equal(2, createsBack.Count);
        var newADiv = Assert.Single(createsBack, p => p.ParentId == container.NodeId);
        Assert.Single(back.Patches.OfType<ReplaceTextPatch>(), p => p.Text == "A");
        Assert.Equal(1, newADiv.InsertIndex);

        // The survivors kept their node ids across BOTH slides — no patch in
        // either re-render frame touched B, C or D structurally.
        foreach (var survivor in new[] { bDiv, cDiv, dDiv })
        {
            Assert.DoesNotContain(forward.Patches.OfType<RemoveNodePatch>(), p => p.NodeId == survivor);
            Assert.DoesNotContain(back.Patches.OfType<RemoveNodePatch>(), p => p.NodeId == survivor);
            Assert.DoesNotContain(forward.Patches.OfType<CreateNodePatch>(), p => p.NodeId == survivor);
            Assert.DoesNotContain(back.Patches.OfType<CreateNodePatch>(), p => p.NodeId == survivor);
        }
    }
}
