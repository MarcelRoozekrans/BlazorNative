using BlazorNative.Renderer;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// GoldenAssertions — Phase 6.2 Gate 1 review.
//
// THE golden vocabulary, stated ONCE. BnLayoutDemoTests ("/layout") and
// BnScrollDemoTests ("/scroll") are the two contracts Gates 2/3 assert their
// device frames against; until now each carried its own verbatim copy of these
// five helpers, and BnComponentTests a third partial one.
//
// ChildrenOf() is the load-bearing one: it IS the shells' insert algorithm —
// walk the creates in patch order, append on InsertIndex -1, insert at the index
// otherwise. Blazor's FIFO render queue does not create children in sibling
// order (a chained child component renders after its later siblings — the BnDemo
// echo-panel lesson), so CreateNodePatch carries its own placement, and the
// shells' Yoga tree must land on the same order or every frame is wrong. If that
// algorithm ever drifts between the two goldens, the two demo pages are being
// held to DIFFERENT contracts — which is exactly the failure the shells' own
// "one rule, stated once" discipline exists to prevent. So it lives here, once.
// ─────────────────────────────────────────────────────────────────────────────

internal static class GoldenAssertions
{
    /// <summary>The mount frame's root node — the one create with no parent.</summary>
    internal static CreateNodePatch Root(RenderFrame mount)
        => Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);

    /// <summary>The create patch for a node (never addressed by raw nodeId in an
    /// assertion — nodeIds are opaque; structure is not).</summary>
    internal static CreateNodePatch CreateOf(RenderFrame frame, int nodeId)
        => Assert.Single(frame.Patches.OfType<CreateNodePatch>(), p => p.NodeId == nodeId);

    /// <summary>A node's children in their FINAL sibling order — the shell's own
    /// algorithm (see the file header). This is the .NET mirror of what Android's
    /// and iOS's CreateNode handlers do with <c>InsertIndex</c>.</summary>
    internal static List<int> ChildrenOf(RenderFrame frame, int parentId)
    {
        var order = new List<int>();
        foreach (CreateNodePatch c in frame.Patches.OfType<CreateNodePatch>()
                     .Where(p => p.ParentId == parentId))
        {
            if (c.InsertIndex < 0)
                order.Add(c.NodeId);
            else
                order.Insert(c.InsertIndex, c.NodeId);
        }
        return order;
    }

    /// <summary>Every style a node carries in this frame — the golden's unit.</summary>
    internal static Dictionary<string, string?> StylesOf(RenderFrame frame, int nodeId)
        => frame.Patches.OfType<SetStylePatch>()
            .Where(p => p.NodeId == nodeId)
            .ToDictionary(p => p.Property, p => p.Value);

    /// <summary>Asserts a node's WHOLE style table (nothing missing, nothing
    /// extra) and its NodeType. <paramref name="what"/> is the frame-table name
    /// of the node ("row 3", "box B", …), threaded into the failure message: a
    /// golden fails as a wall of anonymous nodeIds otherwise, and the first
    /// question is always "which node?".</summary>
    internal static void AssertNode(
        RenderFrame frame, int nodeId, string what, string nodeType,
        params (string Property, string Value)[] expected)
    {
        Dictionary<string, string?> actual = StylesOf(frame, nodeId);
        Dictionary<string, string?> want = expected.ToDictionary(e => e.Property, e => (string?)e.Value);

        Assert.True(
            want.Count == actual.Count
                && want.All(kv => actual.TryGetValue(kv.Key, out string? v) && v == kv.Value),
            $"""
             styles of "{what}" (node {nodeId}) do not match the golden:
               expected: {RenderStyles(want)}
               actual:   {RenderStyles(actual)}
             """);

        Assert.True(CreateOf(frame, nodeId).NodeType == nodeType,
            $"""expected "{what}" (node {nodeId}) to be a {nodeType}, not a {CreateOf(frame, nodeId).NodeType}""");
    }

    /// <summary><see cref="AssertNode"/> for a container view — the common case
    /// (BnLayoutDemo names nothing else).</summary>
    internal static void AssertStyles(
        RenderFrame frame, int nodeId, string what, params (string Property, string Value)[] expected)
        => AssertNode(frame, nodeId, what, "view", expected);

    internal static string RenderStyles(Dictionary<string, string?> styles)
        => "{" + string.Join(", ", styles
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value ?? "<null>"}")) + "}";
}
