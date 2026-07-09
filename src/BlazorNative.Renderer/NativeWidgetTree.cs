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
    }

    public void Clear()
    {
        _siblingMap.Clear();
        _componentMap.Clear();
        _parentMap.Clear();
        _nextNodeId = 1;
    }

    // ── Diagnostics ───────────────────────────────────────────────────────────

    public int NodeCount => _siblingMap.Count;
    public int ComponentCount => _componentMap.Count;
}
