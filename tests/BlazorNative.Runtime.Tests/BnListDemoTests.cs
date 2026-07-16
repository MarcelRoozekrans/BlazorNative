using BlazorNative.Components;
using BlazorNative.Core;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using static BlazorNative.Runtime.Tests.GoldenAssertions;
using BlazorNative.SampleApp;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// BnListDemoTests — Phase 7.2 Task 1.4 (design §"The proof surface").
//
// THE SOURCE OF TRUTH FOR GATES 2 AND 3. This golden pins what .NET puts on
// the wire for "/list" — the mount tree (2 spacers + the initial window,
// COUNTED), and what a window slide looks like as patches (rows entered/left,
// spacers resized, survivors untouched). The shells' liveness and frame
// assertions are DERIVED from it: at any offset the content node's children
// are 2 spacers + the window rows — 13 at offset 0, 17 at offset 640, 13 at
// the bottom — and the content size is 32,000dp BY CONSTRUCTION.
//
// LIVENESS IS DERIVED, NOT TRANSCRIBED: every expected window below comes out
// of BnListWindow.Compute fed with BnListDemo's own consts, so the golden
// cannot drift from the arithmetic it claims to pin.
//
// THE SLIDE RUNS THROUGH THE PRODUCTION INGRESS: Exports.DispatchEventCore
// with the wire's flat JSON ({"name":"scroll","payload":"640"}) — the exact
// call Android's dispatch lane and iOS's dispatcher make after conflation.
// And it runs STRICT (the suite default): Task 1.1 proved a keyed ELEMENT
// window slides as insert/remove only; this page's rows are keyed COMPONENTS
// (BnView), so the slide test doubles as the component-shaped half of that
// empirical answer — a PermutationListEntry here would throw through the 7.0
// default: arm and redden the dispatch.
//
// ROW STATE TRAVELS = NODE IDENTITY. The demo's inputs are deliberately
// unbound: typed text lives only in the native EditText/UITextField, so a row
// that survives a slide MUST keep its node (create/remove-free, prop-silent)
// or the text is visibly lost. That is what the survivor assertions pin.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class BnListDemoTests
{
    private const string ClickArgs = /*lang=json*/ """{"name":"click"}""";

    private static (RenderFrame Mount, List<RenderFrame> Frames) MountListDemo()
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
        Assert.Equal(0, HostSession.TryMount("BnListDemo"));
        Assert.NotEmpty(frames);
        return (frames[0], frames);
    }

    private static void TearDown()
    {
        HostSession.ResetForTests();
        NativeShellBridge.ResetForTests();
    }

    /// <summary>The demo's window at an offset — THE derivation the whole
    /// golden uses (never a hand-transcribed pair).</summary>
    private static (int Start, int End) WindowAt(float offset)
        => BnListWindow.Compute(offset, BnListDemo.ViewportHeightDp,
            BnListDemo.ItemHeightDp, BnListDemo.RowCount, BnListDemo.OverscanRows);

    /// <summary>A dp constant as the renderer puts it on the wire — bare,
    /// invariant, no unit suffix (the 6.1 style value grammar).</summary>
    private static string Dp(int value)
        => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string ScrollArgs(int offsetDp)
        => $$"""{"name":"scroll","payload":"{{Dp(offsetDp)}}"}""";

    // ── The shells' insert/remove algorithm, STATEFUL ─────────────────────────
    //
    // GoldenAssertions.ChildrenOf is the single-frame half (mount order). A
    // window slide is creates AND removes ACROSS frames, so the liveness
    // assertion needs the shells' whole child model: apply every frame's
    // CreateNode (append on -1, insert at InsertIndex otherwise) and
    // RemoveNode in patch order, then read a node's final children. If the
    // renderer's insert-index translation drifts, the final order here is
    // wrong — exactly as it would be on a device.
    private sealed class ShellMirror
    {
        private readonly Dictionary<int, List<int>> _children = new();
        private readonly Dictionary<int, int> _parentOf = new();
        private const int HostRoot = -1;

        public void Apply(RenderFrame frame)
        {
            foreach (RenderPatch patch in frame.Patches)
            {
                switch (patch)
                {
                    case CreateNodePatch c:
                    {
                        List<int> siblings = ChildrenOf(c.ParentId ?? HostRoot);
                        if (c.InsertIndex < 0)
                            siblings.Add(c.NodeId);
                        else
                            siblings.Insert(c.InsertIndex, c.NodeId);
                        _parentOf[c.NodeId] = c.ParentId ?? HostRoot;
                        break;
                    }
                    case RemoveNodePatch r when _parentOf.TryGetValue(r.NodeId, out int parent):
                        ChildrenOf(parent).Remove(r.NodeId);
                        _parentOf.Remove(r.NodeId);
                        break;
                }
            }
        }

        public List<int> ChildrenOf(int parentId)
        {
            if (!_children.TryGetValue(parentId, out List<int>? list))
                _children[parentId] = list = new List<int>();
            return list;
        }
    }

    /// <summary>input nodeId by placeholder value, from one frame's prop
    /// patches — the row-identity lookup ("Row 17" → its EditText node).</summary>
    private static Dictionary<string, int> InputsByPlaceholder(RenderFrame frame)
        => frame.Patches.OfType<UpdatePropPatch>()
            .Where(p => p.Name == "placeholder" && p.Value is not null)
            .ToDictionary(p => p.Value!, p => p.NodeId);

    // ── The arithmetic (checked, not restated — the BnScrollDemo discipline) ──

    /// <summary>The page's numbers as PRODUCTS, and every window in this golden
    /// as a DERIVATION: content 32,000, range 31,600, windows [0,11) / [6,21) /
    /// [489,500), and the design's liveness formula (ceil(viewport/item) + 2×
    /// overscan = 15 mid-scroll) reproduced by the pure function. Change any
    /// demo const and this reddens before a device test does.</summary>
    [Fact]
    public void TheContractsArithmetic_AndEveryWindowIsDerived()
    {
        Assert.Equal(32_000, BnListDemo.RowCount * BnListDemo.ItemHeightDp);
        Assert.Equal(32_000, BnListDemo.ContentHeightDp);
        Assert.Equal(31_600, BnListDemo.ContentHeightDp - BnListDemo.ViewportHeightDp);
        Assert.Equal(31_600, BnListDemo.ScrollRangeDp);
        Assert.Equal(300, BnListDemo.ViewportWidthDp);
        Assert.Equal(400, BnListDemo.ViewportHeightDp);
        Assert.Equal(64, BnListDemo.ItemHeightDp);
        Assert.Equal(4, BnListDemo.OverscanRows);

        // The three windows the proof surface stands on.
        Assert.Equal((0, 11), WindowAt(0f));
        Assert.Equal((6, 21), WindowAt(BnListDemo.MidScrollOffsetDp));
        Assert.Equal((489, 500), WindowAt(BnListDemo.ScrollRangeDp));

        // The design's liveness count, derived: mid-scroll live rows =
        // ceil(400/64) + 2×4 = 15. (At either end the clamps take it to 11.)
        int visible = (int)Math.Ceiling(
            (double)BnListDemo.ViewportHeightDp / BnListDemo.ItemHeightDp);
        (int start, int end) = WindowAt(BnListDemo.MidScrollOffsetDp);
        Assert.Equal(visible + 2 * BnListDemo.OverscanRows, end - start);
        Assert.Equal(15, end - start);

        // The mid-scroll offset sits on a row boundary (the header's numbers
        // stay clean) and strictly inside the range (it is a MID offset).
        Assert.Equal(0, BnListDemo.MidScrollOffsetDp % BnListDemo.ItemHeightDp);
        Assert.InRange(BnListDemo.MidScrollOffsetDp, 1, BnListDemo.ScrollRangeDp - 1);
    }

    // ── The mount golden ──────────────────────────────────────────────────────

    [Fact]
    public void Mount_Golden_SpacersInitialWindowAndCounts()
    {
        var (mount, _) = MountListDemo();
        try
        {
            (int start, int end) = WindowAt(0f);
            int liveRows = end - start;
            Assert.Equal(11, liveRows); // stated once as a number, for the reader

            // Root: a BnColumn with [BnList's scroll, back row].
            CreateNodePatch root = Root(mount);
            AssertNode(mount, root.NodeId, "root", "view", ("flexDirection", "column"));
            List<int> sections = ChildrenOf(mount, root.NodeId);
            Assert.Equal(2, sections.Count);
            (int scroll, int backSection) = (sections[0], sections[1]);

            // [0] the viewport: 300×400, NodeType scroll, nothing else — and
            //     it carries the page's ONE scroll attach.
            AssertNode(mount, scroll, "scroll viewport", "scroll",
                ("width", Dp(BnListDemo.ViewportWidthDp)),
                ("height", Dp(BnListDemo.ViewportHeightDp)));
            AttachEventPatch scrollAttach = Assert.Single(
                mount.Patches.OfType<AttachEventPatch>(), p => p.EventName == "scroll");
            Assert.Equal(scroll, scrollAttach.NodeId);

            // THE LIVENESS COUNT, AS AN ASSERTION: children of the scroll node
            // (= the shells' content-node children) are 2 spacers + the window.
            List<int> children = ChildrenOf(mount, scroll);
            Assert.Equal(2 + liveRows, children.Count); // 13
            int lead = children[0];
            int trail = children[^1];
            List<int> rows = children[1..^1];
            Assert.Equal(liveRows, rows.Count);

            // The spacers: lead 0 (window starts at 0), trail (500−11)×64 =
            // 31,296 — the content node's height is 32,000 BY CONSTRUCTION.
            AssertNode(mount, lead, "lead spacer", "view",
                ("height", Dp(start * BnListDemo.ItemHeightDp)));
            AssertNode(mount, trail, "trail spacer", "view",
                ("height", Dp((BnListDemo.RowCount - end) * BnListDemo.ItemHeightDp)));
            Assert.Equal("0", Dp(start * BnListDemo.ItemHeightDp));
            Assert.Equal("31296", Dp((BnListDemo.RowCount - end) * BnListDemo.ItemHeightDp));
            Assert.Empty(ChildrenOf(mount, lead));
            Assert.Empty(ChildrenOf(mount, trail));

            // The window rows: 64 high, one input each, placeholder "Row i" in
            // order — the row identity every later assertion resolves by.
            for (var i = 0; i < rows.Count; i++)
            {
                AssertNode(mount, rows[i], $"row {start + i}", "view",
                    ("height", Dp(BnListDemo.ItemHeightDp)));
                int input = Assert.Single(ChildrenOf(mount, rows[i]));
                Assert.Equal("input", CreateOf(mount, input).NodeType);
                UpdatePropPatch placeholder = Assert.Single(
                    mount.Patches.OfType<UpdatePropPatch>(),
                    p => p.NodeId == input && p.Name == "placeholder");
                Assert.Equal(BnListDemo.RowPlaceholder(start + i), placeholder.Value);
            }

            // [1] the back-nav row, OUTSIDE the viewport.
            AssertNode(mount, backSection, "back section", "view",
                ("flexDirection", "row"), ("width", Dp(BnListDemo.ViewportWidthDp)));
            int back = Assert.Single(ChildrenOf(mount, backSection));
            Assert.Equal("button", CreateOf(mount, back).NodeType);
            Assert.Empty(StylesOf(mount, back));
            ReplaceTextPatch caption = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>());
            Assert.Equal("← Back", caption.Text);
            Assert.Equal(back, CreateOf(mount, caption.NodeId).ParentId);

            // ── The whole tree, COUNTED (500 rows, 29 creates — virtualization
            //    in one number: 1 root + 1 scroll + 2 spacers + 11 rows +
            //    11 inputs + 1 back row + 1 button + 1 caption). A content
            //    node here would be a 30th create leaking onto the wire.
            Assert.Equal(29, mount.Patches.OfType<CreateNodePatch>().Count());

            // The mirror pin on styles: root 1 + scroll 2 + spacers 2 +
            // rows 11×1 + back row 2 = 18. (Inputs and the button carry none.)
            Assert.Equal(18, mount.Patches.OfType<SetStylePatch>().Count());

            // Props: each input always emits value ("") + our placeholder.
            Assert.Equal(2 * liveRows, mount.Patches.OfType<UpdatePropPatch>().Count());
            Assert.Equal(liveRows, mount.Patches.OfType<UpdatePropPatch>()
                .Count(p => p.Name == "value" && p.Value == ""));

            // Attaches: 1 scroll + 11 change (one per live input) + 1 click.
            Assert.Equal(2 + liveRows, mount.Patches.OfType<AttachEventPatch>().Count());
            Assert.Equal(liveRows, mount.Patches.OfType<AttachEventPatch>()
                .Count(p => p.EventName == "change"));
            Assert.Single(mount.Patches.OfType<AttachEventPatch>(), p => p.EventName == "click");
        }
        finally
        {
            TearDown();
        }
    }

    // ── The window slide (the phase's behaviour, end to end) ──────────────────

    /// <summary>Drive a conflated scroll (offset 640) through the production
    /// dispatch ingress and pin the slide: rows 0–5 leave (6 removes), rows
    /// 11–20 enter (10 rows + 10 inputs, correct placeholders), the spacers
    /// resize (0→384, 31,296→30,656), the survivors 6–10 keep their nodes
    /// untouched (STATE TRAVELS), and the shells' stateful child model lands
    /// on [lead, rows 6..20, trail] — 17 children, the liveness count.</summary>
    [Fact]
    public void ScrollDispatch_SlidesTheWindow_SpacersResize_AndRowStateTravels()
    {
        var (mount, frames) = MountListDemo();
        try
        {
            (int start0, int end0) = WindowAt(0f);
            (int start1, int end1) = WindowAt(BnListDemo.MidScrollOffsetDp);

            int scroll = ChildrenOf(mount, Root(mount).NodeId)[0];
            List<int> children = ChildrenOf(mount, scroll);
            (int lead, int trail) = (children[0], children[^1]);
            List<int> mountRows = children[1..^1];
            Dictionary<string, int> mountInputs = InputsByPlaceholder(mount);

            var mirror = new ShellMirror();
            mirror.Apply(mount);

            // The dispatch — the same flat JSON a shell sends after conflation.
            AttachEventPatch scrollAttach = Assert.Single(
                mount.Patches.OfType<AttachEventPatch>(), p => p.EventName == "scroll");
            int framesBefore = frames.Count;
            Assert.Equal(0, Exports.DispatchEventCore(
                (ulong)scrollAttach.HandlerId, ScrollArgs(BnListDemo.MidScrollOffsetDp)));
            Assert.True(frames.Count > framesBefore, "expected a synchronous re-render frame");
            RenderFrame slide = frames[^1];
            foreach (RenderFrame f in frames.Skip(framesBefore))
                mirror.Apply(f);

            // ── ROWS LEAVE: items 0..5 → 12 removes: the 6 departed row views
            //    PLUS their 6 inputs. The input removes are REDUNDANT on the
            //    host (each input left with its row's subtree) and the shells
            //    treat unknown ids in RemoveNode as a no-op — the documented
            //    EmitDisposedComponentRemoves contract since 3.3: a nested child
            //    COMPONENT (BnInput) disposed in the same batch as its removed
            //    ancestor still emits RemoveNode for its root views. Pinned
            //    exactly so Gates 2/3 count 12 remove patches and 6 actual
            //    view detachments, and nobody "fixes" either number.
            var expectedGone = mountRows[..(start1 - start0)];
            Assert.Equal(6, expectedGone.Count);
            var expectedGoneInputs = Enumerable.Range(start0, start1 - start0)
                .Select(item => mountInputs[BnListDemo.RowPlaceholder(item)]);
            Assert.Equal(
                expectedGone.Concat(expectedGoneInputs).OrderBy(id => id),
                slide.Patches.OfType<RemoveNodePatch>().Select(p => p.NodeId).OrderBy(id => id));

            // ── ROWS ENTER: items 11..20 → 10 new row views (64 high, one
            //    fresh input each, placeholders "Row 11".."Row 20") and
            //    NOTHING else is created — the survivors were not rebuilt.
            List<CreateNodePatch> creates = slide.Patches.OfType<CreateNodePatch>().ToList();
            Assert.Equal(20, creates.Count); // 10 rows + 10 inputs
            List<int> newRows = creates.Where(p => p.ParentId == scroll)
                .Select(p => p.NodeId).ToList();
            Assert.Equal(10, newRows.Count);
            Assert.Equal(end1 - end0, newRows.Count);
            Dictionary<string, int> newInputs = InputsByPlaceholder(slide);
            Assert.Equal(10, newInputs.Count);
            for (int item = end0; item < end1; item++)
                Assert.Contains(BnListDemo.RowPlaceholder(item), newInputs.Keys);

            // ── SPACERS RESIZE: exactly two spacer SetStyles — lead 0→384,
            //    trail 31,296→30,656 — plus the 10 new rows' heights. 12 style
            //    patches total: nothing else on this page may restyle.
            SetStylePatch leadResize = Assert.Single(
                slide.Patches.OfType<SetStylePatch>(), p => p.NodeId == lead);
            Assert.Equal(("height", Dp(start1 * BnListDemo.ItemHeightDp)),
                (leadResize.Property, leadResize.Value));
            Assert.Equal("384", leadResize.Value);
            SetStylePatch trailResize = Assert.Single(
                slide.Patches.OfType<SetStylePatch>(), p => p.NodeId == trail);
            Assert.Equal(("height", Dp((BnListDemo.RowCount - end1) * BnListDemo.ItemHeightDp)),
                (trailResize.Property, trailResize.Value));
            Assert.Equal("30656", trailResize.Value);
            Assert.Equal(12, slide.Patches.OfType<SetStylePatch>().Count());

            // ── STATE TRAVELS: the surviving rows (items 6..10) and their
            //    inputs appear in NO create, NO remove — and NO prop rewrite
            //    (their native text/focus is untouched by the slide). Every
            //    prop patch in the slide frame targets a NEW input.
            List<int> survivors = mountRows[(start1 - start0)..];
            Assert.Equal(5, survivors.Count);
            var survivorInputs = Enumerable.Range(start1, end0 - start1)
                .Select(item => mountInputs[BnListDemo.RowPlaceholder(item)]).ToList();
            foreach (int id in survivors.Concat(survivorInputs))
            {
                Assert.DoesNotContain(slide.Patches.OfType<CreateNodePatch>(), p => p.NodeId == id);
                Assert.DoesNotContain(slide.Patches.OfType<RemoveNodePatch>(), p => p.NodeId == id);
                Assert.DoesNotContain(slide.Patches.OfType<UpdatePropPatch>(), p => p.NodeId == id);
            }
            Assert.All(slide.Patches.OfType<UpdatePropPatch>(),
                p => Assert.Contains(p.NodeId, newInputs.Values));

            // The scroll handler survives too: attaches in the slide frame are
            // the 10 new inputs' `change` — no scroll re-attach, no detach.
            Assert.Equal(10, slide.Patches.OfType<AttachEventPatch>().Count());
            Assert.All(slide.Patches.OfType<AttachEventPatch>(),
                p => Assert.Equal("change", p.EventName));
            Assert.Empty(slide.Patches.OfType<DetachEventPatch>());

            // ── THE FINAL TREE, through the shells' own algorithm: [lead,
            //    rows 6..20, trail] — 17 children (2 + 15, the liveness
            //    count), survivors and newcomers in ITEM order.
            List<int> after = mirror.ChildrenOf(scroll);
            Assert.Equal(2 + (end1 - start1), after.Count); // 17
            Assert.Equal(lead, after[0]);
            Assert.Equal(trail, after[^1]);
            Assert.Equal(survivors, after[1..6]);
            List<int> orderedNewRows = Enumerable.Range(end0, end1 - end0)
                .Select(item => (int)CreateOf(slide,
                    newInputs[BnListDemo.RowPlaceholder(item)]).ParentId!)
                .ToList();
            Assert.Equal(orderedNewRows, after[6..^1]);
        }
        finally
        {
            TearDown();
        }
    }

    // ── The clamped offsets, over the wire ────────────────────────────────────

    /// <summary>A rubber-band NEGATIVE offset (iOS over-scrolls the top) leaves
    /// the window at [0, 11): the re-render diffs to an EMPTY frame — zero
    /// structural patches, zero churn. The wire cost of rubber-banding is one
    /// no-op frame per conflated sample, not a tree rebuild.</summary>
    [Fact]
    public void RubberBandNegativeOffset_DiffsToAnEmptyFrame()
    {
        var (mount, frames) = MountListDemo();
        try
        {
            AttachEventPatch scrollAttach = Assert.Single(
                mount.Patches.OfType<AttachEventPatch>(), p => p.EventName == "scroll");
            Assert.Equal(0, Exports.DispatchEventCore(
                (ulong)scrollAttach.HandlerId,
                /*lang=json*/ """{"name":"scroll","payload":"-40"}"""));

            RenderFrame after = frames[^1];
            Assert.Empty(after.Patches.OfType<CreateNodePatch>());
            Assert.Empty(after.Patches.OfType<RemoveNodePatch>());
            Assert.Empty(after.Patches.OfType<SetStylePatch>());
            Assert.Empty(after.Patches.OfType<UpdatePropPatch>());
            Assert.Empty(after.Patches.OfType<AttachEventPatch>());
            Assert.Empty(after.Patches.OfType<DetachEventPatch>());
        }
        finally
        {
            TearDown();
        }
    }

    /// <summary>An offset PAST the end (the 6.2 shrink case / bottom rubber-
    /// band) clamps to the bottom window [489, 500): lead spacer 31,296, trail
    /// 0, and the last row on the page is "Row 499" — never an index past the
    /// end.</summary>
    [Fact]
    public void OffsetPastTheEnd_ClampsToTheBottomWindow()
    {
        var (mount, frames) = MountListDemo();
        try
        {
            (int start, int end) = WindowAt(999_999f);
            Assert.Equal((489, 500), (start, end));

            int scroll = ChildrenOf(mount, Root(mount).NodeId)[0];
            List<int> children = ChildrenOf(mount, scroll);
            (int lead, int trail) = (children[0], children[^1]);

            var mirror = new ShellMirror();
            mirror.Apply(mount);

            AttachEventPatch scrollAttach = Assert.Single(
                mount.Patches.OfType<AttachEventPatch>(), p => p.EventName == "scroll");
            int framesBefore = frames.Count;
            Assert.Equal(0, Exports.DispatchEventCore(
                (ulong)scrollAttach.HandlerId,
                /*lang=json*/ """{"name":"scroll","payload":"999999"}"""));
            RenderFrame slide = frames[^1];
            foreach (RenderFrame f in frames.Skip(framesBefore))
                mirror.Apply(f);

            // The spacers land exactly: lead 489×64 = 31,296, trail 0.
            SetStylePatch leadResize = Assert.Single(
                slide.Patches.OfType<SetStylePatch>(), p => p.NodeId == lead);
            Assert.Equal("31296", leadResize.Value);
            SetStylePatch trailResize = Assert.Single(
                slide.Patches.OfType<SetStylePatch>(), p => p.NodeId == trail);
            Assert.Equal("0", trailResize.Value);

            // The whole mount window left, the bottom window arrived: liveness
            // 2 + 11 = 13, last placeholder "Row 499".
            List<int> after = mirror.ChildrenOf(scroll);
            Assert.Equal(2 + (end - start), after.Count);
            Dictionary<string, int> newInputs = InputsByPlaceholder(slide);
            Assert.Equal(end - start, newInputs.Count);
            Assert.Contains(BnListDemo.RowPlaceholder(BnListDemo.RowCount - 1), newInputs.Keys);
            Assert.DoesNotContain(BnListDemo.RowPlaceholder(BnListDemo.RowCount), newInputs.Keys);
        }
        finally
        {
            TearDown();
        }
    }

    // ── Nav parity ────────────────────────────────────────────────────────────

    /// <summary>The page is reachable BY ROUTE ("/list"), and its back button
    /// leaves by the same nav path every page uses (INavigationManager → "/").
    /// It lives OUTSIDE the viewport, so no scroll can hide the exit.</summary>
    [Fact]
    public void Route_AndBackButton_NavigateTheStandardPath()
    {
        var (mount, frames) = MountListDemo();
        try
        {
            INavigationManager nav =
                Assert.IsAssignableFrom<INavigationManager>(HostSession.CurrentNavigationManager);
            Assert.Equal("/list", nav.CurrentRoute);

            AttachEventPatch back = Assert.Single(
                mount.Patches.OfType<AttachEventPatch>(), p => p.EventName == "click");
            Assert.Equal(0, Exports.DispatchEventCore((ulong)back.HandlerId, ClickArgs));

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
