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

    // (componentId, siblingIndex) → nodeId
    private readonly Dictionary<(int, int), int> _siblingMap = new();

    // componentId → nodeId  (for component-level removal)
    private readonly Dictionary<int, int> _componentMap = new();

    // nodeId → parent nodeId
    private readonly Dictionary<int, int> _parentMap = new();

    // ── Slot lists (Phase 3.3 §1) ─────────────────────────────────────────────
    //
    // Per container, the ordered slot list mirroring Blazor's sibling numbering
    // EXACTLY: node slots (real views) and component slots (a child component's
    // marker — occupies a sibling position, owns no view). Key -1 per component
    // = the component's root level (root-level slots have no parent node).
    //
    // Replaces the Phase 3.2 append-only _childOrderMap (List<int> of node ids,
    // no component entries). Remaining 3.3 carryovers on this model — the
    // renderer-side wiring lands task-by-task:
    //  (a) renderer inserts are still APPEND-ONLY (PrependFrame ignores the
    //      edit's SiblingIndex; RemoveFrame doesn't trim) — Task 2 routes them
    //      through InsertSlotAt/RemoveSlot.
    //  (b) component frames get NO slot yet (NativeRenderer skips them without
    //      allocating one), so an interleaved child component still offsets
    //      every cursor index after it — Task 3 allocates component slots.
    //  (c) SetAttribute still resolves through the batch-relative
    //      (componentId, frameIndex) sibling map, not the cursor — Task 2
    //      deletes that map.
    //  (d) PrependFrame under a poisoned cursor is DROPPED (explicit guard in
    //      NativeRenderer); with a-c fixed a poisoned cursor is a genuine bug
    //      and reports through strict mode — Task 6.
    //  (e) on* RemoveAttribute never becomes a DetachEventPatch (it flows out
    //      as UpdatePropPatch(name, null), which hosts ignore) — Task 5.
    private const int RootParentKey = -1;
    private readonly Dictionary<(int ComponentId, int ParentKey), List<Slot>> _slotLists = new();

    private static (int, int) SlotKey(int componentId, int? parentNodeId)
        => (componentId, parentNodeId ?? RootParentKey);

    /// <summary>Appends a slot at the end of the container's slot list
    /// (mount-walk creation order — creation order IS sibling order there).</summary>
    public void AppendSlot(int componentId, int? parentNodeId, Slot slot)
        => InsertSlotAt(componentId, parentNodeId, int.MaxValue, slot);

    /// <summary>Inserts a slot at <paramref name="index"/> (Blazor sibling
    /// position) in the container's slot list. Indices are clamped to
    /// [0, Count]: Blazor's diff contract guarantees in-range sibling indices,
    /// so the clamp is POC defense-in-depth, not a semantic (strict-mode
    /// surfacing of contract violations is Task 6).</summary>
    public void InsertSlotAt(int componentId, int? parentNodeId, int index, Slot slot)
    {
        var key = SlotKey(componentId, parentNodeId);
        if (!_slotLists.TryGetValue(key, out List<Slot>? slots))
            _slotLists[key] = slots = new List<Slot>();
        index = Math.Clamp(index, 0, slots.Count);
        slots.Insert(index, slot);
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
    public Slot GetChildAt(int componentId, int? parentNodeId, int siblingIndex)
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
    /// This is the CreateNodePatch.InsertIndex translation (Task 4 consumer).</summary>
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

    // ── Allocation ────────────────────────────────────────────────────────────

    public int AllocateNode(int componentId, int siblingIndex)
    {
        var nodeId = _nextNodeId++;
        _siblingMap[(componentId, siblingIndex)] = nodeId;
        return nodeId;
    }

    public void RegisterComponent(int componentId, int nodeId)
    {
        _componentMap[componentId] = nodeId;
    }

    public void SetParent(int nodeId, int parentNodeId)
    {
        _parentMap[nodeId] = parentNodeId;
    }

    // ── Lookups ───────────────────────────────────────────────────────────────

    public int GetNodeIdBySibling(int componentId, int siblingIndex)
    {
        _siblingMap.TryGetValue((componentId, siblingIndex), out var id);
        return id == 0 ? -1 : id;
    }

    public int GetNodeId(int componentId)
    {
        _componentMap.TryGetValue(componentId, out var id);
        return id == 0 ? -1 : id;
    }

    public int GetParentId(int nodeId)
    {
        _parentMap.TryGetValue(nodeId, out var id);
        return id == 0 ? -1 : id;
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    public void RemoveComponent(int componentId)
    {
        _componentMap.Remove(componentId);

        // Remove all sibling mappings for this component
        var keys = _siblingMap.Keys
            .Where(k => k.Item1 == componentId)
            .ToList();

        foreach (var key in keys)
        {
            var nodeId = _siblingMap[key];
            _siblingMap.Remove(key);
            _parentMap.Remove(nodeId);
        }

        // Drop the component's slot lists.
        var slotKeys = _slotLists.Keys
            .Where(k => k.ComponentId == componentId)
            .ToList();
        foreach (var key in slotKeys)
            _slotLists.Remove(key);
    }

    public void Clear()
    {
        _siblingMap.Clear();
        _componentMap.Clear();
        _parentMap.Clear();
        _slotLists.Clear();
        _nextNodeId = 1;
    }

    // ── Diagnostics ───────────────────────────────────────────────────────────

    public int NodeCount => _siblingMap.Count;
    public int ComponentCount => _componentMap.Count;
}
