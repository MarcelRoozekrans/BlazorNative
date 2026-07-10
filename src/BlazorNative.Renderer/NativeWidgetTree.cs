namespace BlazorNative.Renderer;

// ─────────────────────────────────────────────────────────────────────────────
// NativeWidgetTree
//
// Maintains the mapping between Blazor's internal component/frame identifiers
// and our native node IDs. This is the authoritative source of truth for
// which native widget corresponds to which Blazor render tree frame.
//
// Thread safety: none — mutated only from the renderer's single-threaded
// dispatcher (all render batches arrive there), so no locking is needed.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Discriminates the two things a slot-list entry can be.</summary>
internal enum SlotKind : byte
{
    /// <summary>default(Slot) — "no slot here" (out-of-range lookups).</summary>
    None = 0,
    /// <summary>A real native view, identified by NodeId.</summary>
    Node,
    /// <summary>A child component occupying a sibling position but owning
    /// no view of its own, identified by ComponentId.</summary>
    Component,
}

/// <summary>One entry in a slot list: a node slot XOR a component slot
/// (Phase 3.3 design §1). Accessing the wrong id property throws — the ids
/// live in different id spaces and silently conflating them is exactly the
/// class of bug the slot model exists to kill.</summary>
internal readonly record struct Slot
{
    private readonly int _id;
    public SlotKind Kind { get; }

    private Slot(SlotKind kind, int id) { Kind = kind; _id = id; }

    public static Slot ForNode(int nodeId) => new(SlotKind.Node, nodeId);
    public static Slot ForComponent(int componentId) => new(SlotKind.Component, componentId);

    public bool IsNode => Kind == SlotKind.Node;
    public bool IsComponent => Kind == SlotKind.Component;
    public bool IsNone => Kind == SlotKind.None;

    public int NodeId => IsNode
        ? _id
        : throw new InvalidOperationException($"Slot is {Kind}, not a node slot.");

    public int ComponentId => IsComponent
        ? _id
        : throw new InvalidOperationException($"Slot is {Kind}, not a component slot.");

    public override string ToString() => Kind switch
    {
        SlotKind.Node => $"Node({_id})",
        SlotKind.Component => $"Component({_id})",
        _ => "None",
    };
}

internal sealed class NativeWidgetTree
{
    private int _nextNodeId = 1;

    // ── Slot lists (Phase 3.3 §1) ─────────────────────────────────────────────
    //
    // Per container, the ordered slot list mirroring Blazor's sibling numbering
    // EXACTLY: node slots (real views) and component slots (a child component's
    // marker — occupies a sibling position, owns no view). Key -1 per component
    // = the component's root level (root-level slots have no parent node).
    //
    // Replaces the Phase 3.2 append-only _childOrderMap (List<int> of node ids,
    // no component entries). Fixed in 3.3 Tasks 2-3: (a) PrependFrame inserts
    // at the edit's SiblingIndex and RemoveFrame trims; (b) component frames
    // occupy a component slot (interleaved children no longer offset later
    // cursor indices; StepIn into a component slot descends into its root
    // list); (c) SetAttribute resolves through the cursor — the batch-relative
    // (componentId, frameIndex) sibling map is DELETED. Remaining carryovers:
    //  (d) PrependFrame under a poisoned cursor is DROPPED (explicit guard in
    //      NativeRenderer); with a-c fixed a poisoned cursor is a genuine bug
    //      and reports through strict mode — Task 6.
    //  (e) on* RemoveAttribute never becomes a DetachEventPatch (it flows out
    //      as UpdatePropPatch(name, null), which hosts ignore) — Task 5.
    // Plus: CreateNodePatch carries no host insert position yet — a mid-list
    // insert's slot bookkeeping is exact, but the HOST still appends the new
    // view at the end until Task 4 ships CreateNodePatch.InsertIndex
    // (via TranslateToViewIndex). And Region frames (RenderFragment /
    // CascadingValue bodies) are not walked: components inside a region get
    // no slot/parent record and root at the host root (pre-3.3 behavior,
    // unchanged — see ProcessFrame).
    private const int RootParentKey = -1;
    private readonly Dictionary<(int ComponentId, int ParentKey), List<Slot>> _slotLists = new();

    private static (int, int) SlotKey(int componentId, int? parentNodeId)
        => (componentId, parentNodeId ?? RootParentKey);

    /// <summary>Appends a slot at the end of the container's slot list
    /// (mount-walk creation order — creation order IS sibling order there).
    /// Deliberately NOT routed through <see cref="InsertSlotAt"/>: appends
    /// carry no diff-provided index, so the insert path's out-of-range
    /// handling (a Blazor contract violation, strict-mode reportable) must
    /// never see them.</summary>
    public void AppendSlot(int componentId, int? parentNodeId, Slot slot)
        => GetOrAddSlotList(componentId, parentNodeId).Add(slot);

    /// <summary>Inserts a slot at <paramref name="index"/> (a DIFF-PROVIDED
    /// Blazor sibling position — appends go through <see cref="AppendSlot"/>)
    /// in the container's slot list. Indices are clamped to [0, Count]:
    /// Blazor's diff contract guarantees in-range sibling indices, so an
    /// out-of-range index is a contract violation the caller reports through
    /// strict mode (NativeRenderer, Task 6); the clamp keeps the non-strict
    /// path alive.</summary>
    public void InsertSlotAt(int componentId, int? parentNodeId, int index, Slot slot)
    {
        var slots = GetOrAddSlotList(componentId, parentNodeId);
        index = Math.Clamp(index, 0, slots.Count);
        slots.Insert(index, slot);
    }

    private List<Slot> GetOrAddSlotList(int componentId, int? parentNodeId)
    {
        var key = SlotKey(componentId, parentNodeId);
        if (!_slotLists.TryGetValue(key, out List<Slot>? slots))
            _slotLists[key] = slots = new List<Slot>();
        return slots;
    }

    /// <summary>Removes and returns the slot at <paramref name="index"/>,
    /// trimming the list so later sibling indices shift down (Blazor's
    /// RemoveFrame semantics). Returns <c>default</c> (None) when the
    /// container or index is unknown.</summary>
    public Slot RemoveSlot(int componentId, int? parentNodeId, int index)
    {
        if (!_slotLists.TryGetValue(SlotKey(componentId, parentNodeId), out List<Slot>? slots)
            || index < 0 || index >= slots.Count)
            return default;
        var removed = slots[index];
        slots.RemoveAt(index);
        return removed;
    }

    /// <summary>The slot at diff-cursor <paramref name="siblingIndex"/> under
    /// <paramref name="parentNodeId"/> (null = component root level): a node
    /// slot, a component marker, or None when unknown/out of range.</summary>
    public Slot GetSlotAt(int componentId, int? parentNodeId, int siblingIndex)
    {
        if (!_slotLists.TryGetValue(SlotKey(componentId, parentNodeId), out List<Slot>? slots))
            return default;
        return siblingIndex >= 0 && siblingIndex < slots.Count ? slots[siblingIndex] : default;
    }

    public int GetSlotCount(int componentId, int? parentNodeId)
        => _slotLists.TryGetValue(SlotKey(componentId, parentNodeId), out List<Slot>? slots)
            ? slots.Count
            : 0;

    /// <summary>Translates a slot position into a HOST view index: counts real
    /// views only. Node slots before <paramref name="slotIndex"/> count 1 each;
    /// a component slot contributes its subtree's ROOT-view count (recursively —
    /// a component's root views attach to the SAME host container as its
    /// parent's siblings, so they occupy host child positions here).
    /// This is the CreateNodePatch.InsertIndex translation (Task 4 consumer).
    /// Complexity: O(subtree slots) per call — each component slot before
    /// <paramref name="slotIndex"/> is expanded recursively (acyclic by
    /// Blazor's tree guarantee, so termination is safe). Called once per
    /// mid-list insert; memoize per-diff if Bn* list sizes ever make this
    /// measurable.</summary>
    public int TranslateToViewIndex(int componentId, int? parentNodeId, int slotIndex)
    {
        if (!_slotLists.TryGetValue(SlotKey(componentId, parentNodeId), out List<Slot>? slots))
            return 0;
        var viewIndex = 0;
        var upTo = Math.Min(slotIndex, slots.Count);
        for (var i = 0; i < upTo; i++)
        {
            var slot = slots[i];
            viewIndex += slot.IsComponent ? RootViewCount(slot.ComponentId) : 1;
        }
        return viewIndex;
    }

    /// <summary>How many host views a component contributes at its parent's
    /// level: its root-level node slots plus, recursively, the root views of
    /// components sitting at its own root level.</summary>
    private int RootViewCount(int componentId)
        => TranslateToViewIndex(componentId, parentNodeId: null, int.MaxValue);

    /// <summary>Drops the slot-list bookkeeping for a removed node's subtree
    /// (its own child bucket plus, recursively, its node-slot children's).
    /// Node ids are never reused, so this is hygiene — stale buckets can't
    /// alias — but a long-lived session shouldn't accrete them. Component
    /// slots inside the subtree are NOT purged here: their buckets live under
    /// their own componentId and are cleaned by component disposal.</summary>
    public void PurgeNodeSubtree(int componentId, int nodeId)
    {
        if (!_slotLists.Remove((componentId, nodeId), out List<Slot>? slots))
            return;
        foreach (var slot in slots)
        {
            if (slot.IsNode)
                PurgeNodeSubtree(componentId, slot.NodeId);
        }
    }

    // ── Component-parent map (Phase 3.3 §2, DoD #8) ───────────────────────────
    //
    // childComponentId → (parentComponentId, parentNodeId). Recorded when the
    // parent's diff walks the child's Component frame; consulted when the
    // CHILD's own RenderTreeDiff arrives so its root-level views parent under
    // the recorded host container instead of the host root. parentNodeId null
    // = the child sits at the host root (root-level component chains).
    private readonly Dictionary<int, (int ParentComponentId, int? ParentNodeId)> _componentParents = new();

    public void RegisterComponentParent(int childComponentId, int parentComponentId, int? parentNodeId)
        => _componentParents[childComponentId] = (parentComponentId, parentNodeId);

    public bool TryGetComponentParent(int childComponentId, out (int ParentComponentId, int? ParentNodeId) parent)
        => _componentParents.TryGetValue(childComponentId, out parent);

    /// <summary>Removes the child component's slot from its recorded parent
    /// container's slot list (no-op when a RemoveFrame edit already trimmed it).</summary>
    public void RemoveComponentSlot(int parentComponentId, int? parentNodeId, int childComponentId)
    {
        if (_slotLists.TryGetValue(SlotKey(parentComponentId, parentNodeId), out List<Slot>? slots))
            slots.Remove(Slot.ForComponent(childComponentId));
    }

    // ── Allocation ────────────────────────────────────────────────────────────

    /// <summary>Next node id. Position bookkeeping lives in the slot lists —
    /// the Phase 3.2 (componentId, siblingIndex) map was deleted in 3.3
    /// Task 2 (ReferenceFrameIndex is batch-relative, never a node key).</summary>
    public int AllocateNode() => _nextNodeId++;

    // ── Cleanup ───────────────────────────────────────────────────────────────

    /// <summary>Purges a disposed component's bookkeeping: all its slot lists
    /// and its component-parent map entry. Callers emit the RemoveNodePatches
    /// and trim its sibling slot first (see NativeRenderer.ProcessDisposedComponent).</summary>
    public void RemoveComponent(int componentId)
    {
        _componentParents.Remove(componentId);

        // Drop the component's slot lists.
        var slotKeys = _slotLists.Keys
            .Where(k => k.ComponentId == componentId)
            .ToList();
        foreach (var key in slotKeys)
            _slotLists.Remove(key);
    }

    public void Clear()
    {
        _componentParents.Clear();
        _slotLists.Clear();
        _nextNodeId = 1;
    }

    // ── Diagnostics ───────────────────────────────────────────────────────────

    /// <summary>Live slot-list buckets — disposal tests assert bookkeeping
    /// doesn't accrete after component teardown.</summary>
    public int SlotListCount => _slotLists.Count;

    /// <summary>Live component-parent map entries — see SlotListCount.</summary>
    public int ComponentParentCount => _componentParents.Count;
}
