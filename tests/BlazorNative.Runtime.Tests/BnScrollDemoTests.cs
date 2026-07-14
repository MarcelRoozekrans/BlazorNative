using BlazorNative.Core;
using BlazorNative.Renderer;
using BlazorNative.Runtime;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// BnScrollDemoTests — Phase 6.2 Task 1.3 (design §"The proof surface").
//
// THE SOURCE OF TRUTH FOR GATES 2 AND 3. This golden pins what .NET puts on the
// wire for "/scroll": the creates (with their insert indices) and the SetStyle
// values. The shells' content-size and frame assertions are DERIVED from it —
// Gate 2 (AVD) and Gate 3 (iOS simulator) assert the SAME NUMBERS, because Yoga
// computes in density-independent units on both. If a number here changes, the
// two shells' expectations change with it, and that is a deliberate act.
//
// The expected COMPUTED FRAMES (dp, relative to the parent) live in
// BnScrollDemo.cs's file header — the canonical table; keep THAT one updated.
// In short: a 300×200 viewport over ten 80-high rows → the content node computes
// to 800, so contentSize 300×800, viewport 200, scrollable range 600.
//
// ── THE CONTENT NODE IS NOT ON THIS WIRE ─────────────────────────────────────
// The wire tree is  scroll → rows.  The view/Yoga trees are  scroll → CONTENT →
// rows: the content node is SYNTHETIC, created by each shell when it creates a
// scroll node, and it never appears in a patch. That is why this golden asserts
// the rows are children of the SCROLL node — a shell that put them there in its
// own trees too would be the bug (non-negotiable #2: a scroll node's wire child
// at index i is the CONTENT node's child at index i, in both trees).
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class BnScrollDemoTests
{
    private const string ClickArgs = /*lang=json*/ """{"name":"click"}""";

    /// <summary>The ten row colours, in row order — the golden's row identity
    /// (nodeIds are opaque; a colour is not). Mirrors BnScrollDemo.RowColors.</summary>
    private static readonly string[] RowColors =
    [
        "#E57373", "#90A4AE", "#81C784", "#FFB74D", "#BA68C8",
        "#4DB6AC", "#F06292", "#7986CB", "#A1887F", "#DCE775",
    ];

    /// <summary>The row that hosts the nested flex row (design §Verification #4:
    /// flex inside a scroll). Row 1 on purpose — fully inside the viewport at
    /// offset 0 (y 80..160 of a 200-high viewport), so the nesting proof is in the
    /// FIRST screenshot the shells take, while rows 7-9 (y 560..800) are what
    /// scrolling has to reveal.</summary>
    private const int FlexRowIndex = 1;

    private static (RenderFrame Mount, List<RenderFrame> Frames) MountScrollDemo()
    {
        FakeShellHost.Reset();
        NativeShellBridge.Register(FakeShellHost.BuildCallbacks());
        HostSession.ResetForTests();
        NativeRenderer renderer = HostSession.EnsureSession();
        var frames = new List<RenderFrame>();
        renderer.Frames += (f, _) =>
        {
            frames.Add(f);
            return ValueTask.CompletedTask;
        };
        Assert.Equal(0, HostSession.TryMount("BnScrollDemo"));
        Assert.NotEmpty(frames);
        return (frames[0], frames);
    }

    private static void TearDown()
    {
        HostSession.ResetForTests();
        NativeShellBridge.ResetForTests();
    }

    // ── Structural pins (never raw nodeIds — BnLayoutDemoTests conventions) ───

    private static CreateNodePatch Root(RenderFrame mount)
        => Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);

    private static CreateNodePatch CreateOf(RenderFrame frame, int nodeId)
        => Assert.Single(frame.Patches.OfType<CreateNodePatch>(), p => p.NodeId == nodeId);

    /// <summary>A node's children in their FINAL sibling order — the shell's own
    /// algorithm: walk the creates in patch order, append on InsertIndex -1,
    /// insert at the index otherwise. (Blazor's FIFO render queue does not create
    /// children in sibling order, so CreateNodePatch carries its own placement;
    /// the shells' Yoga tree must land on the same order or every frame is wrong.)</summary>
    private static List<int> ChildrenOf(RenderFrame frame, int parentId)
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

    private static Dictionary<string, string?> StylesOf(RenderFrame frame, int nodeId)
        => frame.Patches.OfType<SetStylePatch>()
            .Where(p => p.NodeId == nodeId)
            .ToDictionary(p => p.Property, p => p.Value);

    /// <summary>Asserts a node's whole style table AND its NodeType.
    /// <paramref name="what"/> is the frame-table name of the node ("row 3", "box
    /// B", …), threaded into the failure message: this golden fails as a wall of
    /// anonymous nodeIds otherwise, and the first question is always "which node?".</summary>
    private static void AssertNode(
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
               expected: {Render(want)}
               actual:   {Render(actual)}
             """);

        Assert.True(CreateOf(frame, nodeId).NodeType == nodeType,
            $"""expected "{what}" (node {nodeId}) to be a {nodeType}, not a {CreateOf(frame, nodeId).NodeType}""");
    }

    private static string Render(Dictionary<string, string?> styles)
        => "{" + string.Join(", ", styles
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value ?? "<null>"}")) + "}";

    // ── The golden ────────────────────────────────────────────────────────────

    [Fact]
    public void Mount_Golden_ViewportRowsStylesAndInsertIndices()
    {
        var (mount, _) = MountScrollDemo();
        try
        {
            // The root: a BnColumn. The scroll sits INSIDE flex (design
            // §Verification #4, half one) — its frame comes from the column, like
            // any flex item's.
            CreateNodePatch root = Root(mount);
            Assert.Equal("view", root.NodeType);
            AssertNode(mount, root.NodeId, "root", "view", ("flexDirection", "column"));

            List<int> sections = ChildrenOf(mount, root.NodeId);
            Assert.Equal(2, sections.Count);
            (int scroll, int backSection) = (sections[0], sections[1]);

            // [0] THE VIEWPORT: NodeType "scroll" (NodeType 6 — the stub since 2.5),
            //     300×200. NO flexDirection: BnScroll has no Direction param
            //     (vertical-only), and the synthetic content node's direction is the
            //     shells' business, not the wire's.
            AssertNode(mount, scroll, "scroll viewport", "scroll",
                ("width", "300"), ("height", "200"));

            // Ten rows, each 80 high, DIRECT children of the scroll node on the
            // wire. The shells re-parent them into the synthetic content node —
            // whose Yoga-computed height is therefore 10 × 80 = 800, against a
            // 200-high viewport: contentSize 800, scrollable range 600. THAT is
            // the whole phase, and it is arithmetic on THESE numbers.
            List<int> rows = ChildrenOf(mount, scroll);
            Assert.Equal(10, rows.Count);
            for (var i = 0; i < rows.Count; i++)
            {
                AssertNode(mount, rows[i], $"row {i}", "view",
                    ("height", "80"), ("backgroundColor", RowColors[i]));
            }
            // No width on any row: the content node is width:100% of the viewport
            // and Yoga's default alignItems is stretch, so each row computes to
            // 300 wide. If a width ever appears here, the shells are no longer
            // proving that the content node spans the viewport.

            // Row 1 hosts a FLEX ROW (design §Verification #4, half two: flex
            // nested inside a scroll). Grow=1 in a definite-height (80) column
            // parent → it fills the row: (0,0,300,80). Inside it, the BnLayoutDemo
            // idiom: 50 · grow · 50 → (0,0,50,80) (50,0,200,80) (250,0,50,80).
            int flexRow = Assert.Single(ChildrenOf(mount, rows[FlexRowIndex]));
            AssertNode(mount, flexRow, "flex row (in row 1)", "view",
                ("flexDirection", "row"), ("flexGrow", "1"));
            List<int> boxes = ChildrenOf(mount, flexRow);
            Assert.Equal(3, boxes.Count);
            AssertNode(mount, boxes[0], "box A", "view",
                ("width", "50"), ("backgroundColor", "#E53935"));
            AssertNode(mount, boxes[1], "box B", "view",
                ("flexGrow", "1"), ("backgroundColor", "#1E88E5"));
            AssertNode(mount, boxes[2], "box C", "view",
                ("width", "50"), ("backgroundColor", "#43A047"));

            // Every OTHER row is childless — a scrolled row is a plain coloured
            // band, so nothing but row 1 can perturb the 80-high grid.
            for (var i = 0; i < rows.Count; i++)
            {
                if (i == FlexRowIndex)
                    continue;
                Assert.Empty(ChildrenOf(mount, rows[i]));
            }

            // [1] the back-nav row, OUTSIDE the scroll (nav parity with the other
            //     pages — and a page whose only exit scrolled away is not an exit).
            //     Its y is 200: the viewport's height, not the content's.
            AssertNode(mount, backSection, "back section", "view",
                ("flexDirection", "row"), ("width", "300"));
            int back = Assert.Single(ChildrenOf(mount, backSection));
            Assert.Equal("button", CreateOf(mount, back).NodeType);
            // The button carries NO style — its size is the MEASURED one.
            Assert.Empty(StylesOf(mount, back));
            ReplaceTextPatch caption = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(),
                p => p.Text == "← Back");
            Assert.Equal(back, CreateOf(mount, caption.NodeId).ParentId);

            // One event on the page: the back click. And NOT ONE prop patch —
            // every style rides the SetStyle wire (kind 6); if a flex name ever
            // falls out of the renderer allow-list it lands here as an UpdateProp
            // and this fails.
            AttachEventPatch attach = Assert.Single(mount.Patches.OfType<AttachEventPatch>());
            Assert.Equal(back, attach.NodeId);
            Assert.Equal("click", attach.EventName);
            Assert.Empty(mount.Patches.OfType<UpdatePropPatch>());

            // The whole tree, counted: 1 root + 1 scroll + 10 rows + 1 flex row
            // + 3 boxes + 1 back section + 1 button + 1 label node = 19 creates.
            // NINETEEN, not twenty: the content node is SYNTHETIC. A twentieth
            // create here would mean it leaked onto the wire.
            Assert.Equal(19, mount.Patches.OfType<CreateNodePatch>().Count());

            // Exactly ONE scroll node on the page. The shells create exactly one
            // synthetic content node in response.
            Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.NodeType == "scroll");
        }
        finally
        {
            TearDown();
        }
    }

    /// <summary>The page is reachable BY ROUTE ("/scroll"), and its back button
    /// leaves by the same nav path the other pages use (INavigationManager → "/").
    /// The back button lives OUTSIDE the viewport, so no amount of scrolling can
    /// take the exit off the screen.</summary>
    [Fact]
    public void BackButton_NavigatesToTheDemoRoot()
    {
        var (mount, frames) = MountScrollDemo();
        try
        {
            INavigationManager nav =
                Assert.IsAssignableFrom<INavigationManager>(HostSession.CurrentNavigationManager);
            // Mounting a ROUTED component syncs the route (HostSession.MountRoot).
            Assert.Equal("/scroll", nav.CurrentRoute);

            AttachEventPatch back = Assert.Single(mount.Patches.OfType<AttachEventPatch>());
            Assert.Equal(0, Exports.DispatchEventCore((ulong)back.HandlerId, ClickArgs));

            // The swap happened inside the dispatch window: this page's root was
            // removed and BnDemo mounted. The scroll node's removal is ONE
            // RemoveNodePatch for the whole subtree — and on the shells that
            // subtree now includes the synthetic content node (Gates 2/3: a missed
            // descendant is a dangling YGNodeRef on iOS, not a leak).
            Assert.True(frames.Count >= 2, "expected the navigation swap's frames");
            Assert.Contains(frames.Skip(1).SelectMany(f => f.Patches).OfType<RemoveNodePatch>(),
                p => p.NodeId == Root(mount).NodeId);
            Assert.Contains(frames.Skip(1).SelectMany(f => f.Patches).OfType<ReplaceTextPatch>(),
                p => p.Text == "BnDemo");
            Assert.Equal("/", nav.CurrentRoute);
        }
        finally
        {
            TearDown();
        }
    }
}
