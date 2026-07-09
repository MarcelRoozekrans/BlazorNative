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

internal sealed class NativeWidgetTree
{
    private int _nextNodeId = 1;

    // (componentId, siblingIndex) → nodeId
    private readonly Dictionary<(int, int), int> _siblingMap = new();

    // componentId → nodeId  (for component-level removal)
    private readonly Dictionary<int, int> _componentMap = new();

    // nodeId → parent nodeId
    private readonly Dictionary<int, int> _parentMap = new();

    // Phase 3.2: ordered child lists, recorded in creation (= render-tree)
    // order so diff-cursor edits (StepIn/UpdateText SiblingIndex) can resolve
    // "child N of the current container". Key -1 per component = the
    // component's root level (top-level nodes have no parent node).
    // (componentId, parentNodeId or -1) → ordered child nodeIds
    private readonly Dictionary<(int, int), List<int>> _childOrderMap = new();

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

    /// <summary>Appends <paramref name="childNodeId"/> to the ordered child
    /// list of <paramref name="parentNodeId"/> (null = the component's root
    /// level). Called at node creation — creation order IS render-tree sibling
    /// order for the initial walk, which is what the diff cursor addresses.</summary>
    public void AppendChildOrder(int componentId, int? parentNodeId, int childNodeId)
    {
        var key = (componentId, parentNodeId ?? -1);
        if (!_childOrderMap.TryGetValue(key, out List<int>? children))
            _childOrderMap[key] = children = new List<int>();
        children.Add(childNodeId);
    }

    /// <summary>Node id of the child at diff-cursor <paramref name="siblingIndex"/>
    /// under <paramref name="parentNodeId"/> (null = component root level),
    /// or -1 when unknown/out of range.</summary>
    public int GetChildAt(int componentId, int? parentNodeId, int siblingIndex)
    {
        if (!_childOrderMap.TryGetValue((componentId, parentNodeId ?? -1), out List<int>? children))
            return -1;
        return siblingIndex >= 0 && siblingIndex < children.Count ? children[siblingIndex] : -1;
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

        // Drop the component's ordered child lists (Phase 3.2).
        var childKeys = _childOrderMap.Keys
            .Where(k => k.Item1 == componentId)
            .ToList();
        foreach (var key in childKeys)
            _childOrderMap.Remove(key);
    }

    public void Clear()
    {
        _siblingMap.Clear();
        _componentMap.Clear();
        _parentMap.Clear();
        _childOrderMap.Clear();
        _nextNodeId = 1;
    }

    // ── Diagnostics ───────────────────────────────────────────────────────────

    public int NodeCount => _siblingMap.Count;
    public int ComponentCount => _componentMap.Count;
}
