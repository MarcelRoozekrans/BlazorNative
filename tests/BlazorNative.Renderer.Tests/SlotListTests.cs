using Xunit;

namespace BlazorNative.Renderer.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// SlotListTests — Phase 3.3 Task 1 (TDD).
//
// Pins the slot-list model that replaces NativeWidgetTree's append-only
// _childOrderMap (the 3.3 carryover block, items a-c). Slot lists mirror
// Blazor's sibling numbering EXACTLY: each entry is either a node slot
// (a real native view) or a component slot (a componentId marker that
// occupies a sibling position but owns no view).
//
// Contract under test (design doc §1):
//   • lists hold node AND component slots;
//   • InsertSlotAt honors the sibling position (mid-list inserts);
//   • RemoveSlot trims the list (later indices shift down);
//   • GetChildAt resolves node slots by sibling index and returns the
//     component marker for component slots;
//   • TranslateToViewIndex skips component slots — a component slot
//     contributes its subtree's ROOT-VIEW count (recursively) when
//     translating positions after it.
//
// These tests exercise NativeWidgetTree directly (InternalsVisibleTo) —
// the renderer-driven behavior lands in DiffCursorTests (Tasks 2-3).
// ─────────────────────────────────────────────────────────────────────────────

public sealed class SlotListTests
{
    private const int Comp = 0;          // the component whose slot lists we exercise
    private static readonly int? Root = null; // component root level (no parent node)

    // ── Slot discriminated struct ─────────────────────────────────────────────

    [Fact]
    public void Slot_IsNodeXorComponent()
    {
        var node = Slot.ForNode(42);
        Assert.True(node.IsNode);
        Assert.False(node.IsComponent);
        Assert.Equal(42, node.NodeId);
        Assert.Throws<InvalidOperationException>(() => node.ComponentId);

        var comp = Slot.ForComponent(7);
        Assert.True(comp.IsComponent);
        Assert.False(comp.IsNode);
        Assert.Equal(7, comp.ComponentId);
        Assert.Throws<InvalidOperationException>(() => comp.NodeId);

        var none = default(Slot);
        Assert.False(none.IsNode);
        Assert.False(none.IsComponent);
        Assert.True(none.IsNone);
    }

    // ── Insert / order ────────────────────────────────────────────────────────

    [Fact]
    public void InsertSlotAt_MidList_HonorsSiblingPosition()
    {
        var tree = new NativeWidgetTree();
        tree.AppendSlot(Comp, Root, Slot.ForNode(10));   // sibling 0
        tree.AppendSlot(Comp, Root, Slot.ForNode(11));   // sibling 1

        // Mid-list insert at sibling 1 — the append-only carryover (a) shape.
        tree.InsertSlotAt(Comp, Root, 1, Slot.ForNode(12));

        Assert.Equal(3, tree.GetSlotCount(Comp, Root));
        Assert.Equal(10, tree.GetChildAt(Comp, Root, 0).NodeId);
        Assert.Equal(12, tree.GetChildAt(Comp, Root, 1).NodeId);
        Assert.Equal(11, tree.GetChildAt(Comp, Root, 2).NodeId);
    }

    [Fact]
    public void InsertSlotAt_AtFront_ShiftsEverything()
    {
        var tree = new NativeWidgetTree();
        tree.AppendSlot(Comp, Root, Slot.ForNode(10));
        tree.InsertSlotAt(Comp, Root, 0, Slot.ForNode(11));

        Assert.Equal(11, tree.GetChildAt(Comp, Root, 0).NodeId);
        Assert.Equal(10, tree.GetChildAt(Comp, Root, 1).NodeId);
    }

    [Fact]
    public void SlotList_HoldsNodeAndComponentSlots()
    {
        var tree = new NativeWidgetTree();
        tree.AppendSlot(Comp, Root, Slot.ForNode(10));       // sibling 0: element
        tree.AppendSlot(Comp, Root, Slot.ForComponent(7));   // sibling 1: child component
        tree.AppendSlot(Comp, Root, Slot.ForNode(11));       // sibling 2: element

        Assert.Equal(10, tree.GetChildAt(Comp, Root, 0).NodeId);
        var marker = tree.GetChildAt(Comp, Root, 1);
        Assert.True(marker.IsComponent);
        Assert.Equal(7, marker.ComponentId);
        // THE carryover (b) fix at model level: the element AFTER the
        // component still resolves at ITS Blazor sibling index.
        Assert.Equal(11, tree.GetChildAt(Comp, Root, 2).NodeId);
    }

    // ── Remove ────────────────────────────────────────────────────────────────

    [Fact]
    public void RemoveSlot_Trims_LaterIndicesShiftDown()
    {
        var tree = new NativeWidgetTree();
        tree.AppendSlot(Comp, Root, Slot.ForNode(10));
        tree.AppendSlot(Comp, Root, Slot.ForNode(11));
        tree.AppendSlot(Comp, Root, Slot.ForNode(12));

        var removed = tree.RemoveSlot(Comp, Root, 0);

        Assert.True(removed.IsNode);
        Assert.Equal(10, removed.NodeId);
        Assert.Equal(2, tree.GetSlotCount(Comp, Root));
        Assert.Equal(11, tree.GetChildAt(Comp, Root, 0).NodeId);
        Assert.Equal(12, tree.GetChildAt(Comp, Root, 1).NodeId);
    }

    [Fact]
    public void RemoveSlot_OutOfRange_ReturnsNone()
    {
        var tree = new NativeWidgetTree();
        tree.AppendSlot(Comp, Root, Slot.ForNode(10));

        Assert.True(tree.RemoveSlot(Comp, Root, 5).IsNone);
        Assert.True(tree.RemoveSlot(Comp, Root, -1).IsNone);
        Assert.True(tree.RemoveSlot(Comp, (int?)999, 0).IsNone); // unknown container
        Assert.Equal(1, tree.GetSlotCount(Comp, Root));
    }

    [Fact]
    public void GetChildAt_OutOfRangeOrUnknownContainer_ReturnsNone()
    {
        var tree = new NativeWidgetTree();
        tree.AppendSlot(Comp, Root, Slot.ForNode(10));

        Assert.True(tree.GetChildAt(Comp, Root, 1).IsNone);
        Assert.True(tree.GetChildAt(Comp, Root, -1).IsNone);
        Assert.True(tree.GetChildAt(Comp, (int?)999, 0).IsNone);
    }

    // ── Slot → view-index translation ─────────────────────────────────────────

    [Fact]
    public void TranslateToViewIndex_NodeSlotsOnly_IsIdentity()
    {
        var tree = new NativeWidgetTree();
        tree.AppendSlot(Comp, Root, Slot.ForNode(10));
        tree.AppendSlot(Comp, Root, Slot.ForNode(11));

        Assert.Equal(0, tree.TranslateToViewIndex(Comp, Root, 0));
        Assert.Equal(1, tree.TranslateToViewIndex(Comp, Root, 1));
        Assert.Equal(2, tree.TranslateToViewIndex(Comp, Root, 2)); // end = append position
    }

    [Fact]
    public void TranslateToViewIndex_SkipsComponentSlots()
    {
        var tree = new NativeWidgetTree();
        tree.AppendSlot(Comp, Root, Slot.ForNode(10));       // 1 view
        tree.AppendSlot(Comp, Root, Slot.ForComponent(7));   // 0 OWN views…
        tree.AppendSlot(Comp, Root, Slot.ForNode(11));

        // …component 7 contributes its ROOT-view count: two root views.
        tree.AppendSlot(7, Root, Slot.ForNode(20));
        tree.AppendSlot(7, Root, Slot.ForNode(21));

        Assert.Equal(0, tree.TranslateToViewIndex(Comp, Root, 0));
        Assert.Equal(1, tree.TranslateToViewIndex(Comp, Root, 1)); // before the component slot
        Assert.Equal(3, tree.TranslateToViewIndex(Comp, Root, 2)); // after it: 1 + 2 subtree roots
        Assert.Equal(4, tree.TranslateToViewIndex(Comp, Root, 3));
    }

    [Fact]
    public void TranslateToViewIndex_ComponentSubtreeCount_IsRecursive()
    {
        var tree = new NativeWidgetTree();
        tree.AppendSlot(Comp, Root, Slot.ForComponent(7));
        tree.AppendSlot(Comp, Root, Slot.ForNode(10));

        // Component 7's root level: one view + a nested component 8.
        tree.AppendSlot(7, Root, Slot.ForNode(20));
        tree.AppendSlot(7, Root, Slot.ForComponent(8));
        // Component 8's root level: two views.
        tree.AppendSlot(8, Root, Slot.ForNode(30));
        tree.AppendSlot(8, Root, Slot.ForNode(31));

        // Slot 1 sits after component 7 → 1 (its own view) + 2 (nested 8's views) = 3.
        Assert.Equal(3, tree.TranslateToViewIndex(Comp, Root, 1));
    }

    [Fact]
    public void TranslateToViewIndex_EmptyComponentSlot_ContributesZero()
    {
        var tree = new NativeWidgetTree();
        tree.AppendSlot(Comp, Root, Slot.ForComponent(7));   // no views registered yet
        tree.AppendSlot(Comp, Root, Slot.ForNode(10));

        Assert.Equal(0, tree.TranslateToViewIndex(Comp, Root, 1));
    }
}
