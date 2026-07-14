using BlazorNative.Core;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using static BlazorNative.Runtime.Tests.GoldenAssertions;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// BnLayoutDemoTests — Phase 6.1 Task 1.4 (design §"The proof surface").
//
// THE SOURCE OF TRUTH FOR GATES 2 AND 3. This golden pins what .NET puts on the
// wire for "/layout": the creates (with their insert indices) and the SetStyle
// values. The shells' frame assertions are DERIVED from it — Gate 2 (AVD) and
// Gate 3 (iOS simulator) assert the SAME NUMBERS, because Yoga computes in
// density-independent units on both. If a number here changes, the two shells'
// expectations change with it, and that is a deliberate act.
//
// The expected COMPUTED FRAMES (dp, relative to the parent) live in
// BnLayoutDemo.cs's file header — the canonical table; keep THAT one updated.
// In short: a 300×100 row of 50 · grow · 50; a 300×200 space-between column
// with an AlignSelf=Center child; a 300-wide wrap row that spills a 4th box
// onto line 2; a 150-wide row whose height is the natively MEASURED height of
// a wrapped label (DoD #3); a back-nav row.
//
// Insert indices matter as much as the styles: Blazor's FIFO render queue does
// not create children in sibling order (a chained child component renders after
// its later siblings — the BnDemo echo-panel lesson), so CreateNodePatch carries
// its own placement. ChildrenOf() below replays exactly what a shell does with
// it; the shells' Yoga tree must land on the same order or every frame is wrong.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class BnLayoutDemoTests
{
    private const string ClickArgs = /*lang=json*/ """{"name":"click"}""";

    private const string MeasuredText =
        "This label is measured natively: it wraps inside 150dp and its measured height drives the row.";

    private static (RenderFrame Mount, List<RenderFrame> Frames) MountLayoutDemo()
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
        Assert.Equal(0, HostSession.TryMount("BnLayoutDemo"));
        Assert.NotEmpty(frames);
        return (frames[0], frames);
    }

    private static void TearDown()
    {
        HostSession.ResetForTests();
        NativeShellBridge.ResetForTests();
    }

    // ── Structural pins ───────────────────────────────────────────────────────
    //
    // Root / CreateOf / ChildrenOf / StylesOf / AssertStyles live in
    // GoldenAssertions (`using static`, above) — SHARED with BnScrollDemoTests.
    // They used to be a verbatim copy in each golden, and ChildrenOf *is* the
    // shells' insert algorithm: if it drifts between the two goldens, the two
    // demo pages are being held to different contracts (6.2 Gate 1 review).

    // ── The golden ────────────────────────────────────────────────────────────

    [Fact]
    public void Mount_Golden_SectionsStylesAndInsertIndices()
    {
        var (mount, _) = MountLayoutDemo();
        try
        {
            // The root: a BnColumn. It says "column" explicitly even though that
            // is Yoga's default — the tree should say what it means.
            CreateNodePatch root = Root(mount);
            Assert.Equal("view", root.NodeType);
            AssertStyles(mount, root.NodeId, "root", ("flexDirection", "column"));

            // Five sections, in THIS order (the frame table's [0]..[4]).
            List<int> sections = ChildrenOf(mount, root.NodeId);
            Assert.Equal(5, sections.Count);
            (int rowSection, int colSection, int wrapSection, int textSection, int backSection) =
                (sections[0], sections[1], sections[2], sections[3], sections[4]);

            // [0] row 300×100: 50 · Grow=1 · 50 → frames (0,0,50,100) (50,0,200,100) (250,0,50,100)
            AssertStyles(mount, rowSection, "row section",
                ("flexDirection", "row"), ("width", "300"), ("height", "100"));
            List<int> boxes = ChildrenOf(mount, rowSection);
            Assert.Equal(3, boxes.Count);
            AssertStyles(mount, boxes[0], "box A", ("width", "50"), ("backgroundColor", "#E57373"));
            AssertStyles(mount, boxes[1], "box B", ("flexGrow", "1"), ("backgroundColor", "#64B5F6"));
            AssertStyles(mount, boxes[2], "box C", ("width", "50"), ("backgroundColor", "#81C784"));

            // [1] column 300×200 space-between; middle child AlignSelf=Center
            //     → frames (0,0,100,40) (100,80,100,40) (0,160,100,40)
            AssertStyles(mount, colSection, "column section",
                ("flexDirection", "column"), ("width", "300"), ("height", "200"),
                ("justifyContent", "space-between"));
            List<int> items = ChildrenOf(mount, colSection);
            Assert.Equal(3, items.Count);
            AssertStyles(mount, items[0], "item 0",
                ("width", "100"), ("height", "40"), ("backgroundColor", "#FFB74D"));
            AssertStyles(mount, items[1], "item 1",
                ("width", "100"), ("height", "40"), ("alignSelf", "center"),
                ("backgroundColor", "#BA68C8"));
            AssertStyles(mount, items[2], "item 2",
                ("width", "100"), ("height", "40"), ("backgroundColor", "#4DB6AC"));

            // [2] wrap row 300×100, four 90-wide boxes → 3 on line 1 (270 of 300),
            //     the 4th at y=40. NINETY, not 100: four 100s would put the row
            //     exactly ON Yoga's break boundary (consumed + item > available;
            //     300 > 300 is false), where a half-dp of rounding on either shell
            //     flips box 3 onto line 2 and the two platforms "disagree" for a
            //     non-engine reason. 30dp of slack makes the break a fact.
            AssertStyles(mount, wrapSection, "wrap section",
                ("flexDirection", "row"), ("width", "300"), ("height", "100"),
                ("flexWrap", "wrap"));
            List<int> wrapped = ChildrenOf(mount, wrapSection);
            Assert.Equal(4, wrapped.Count);
            Assert.All(wrapped, id => AssertStyles(mount, id, "wrap box",
                ("width", "90"), ("height", "40"), ("backgroundColor", "#90A4AE")));

            // [3] the measured text (DoD #3): a 150-wide row with NO height — the
            //     label wraps and its MEASURED height becomes the row's height.
            AssertStyles(mount, textSection, "text section",
                ("flexDirection", "row"), ("width", "150"));
            int textLeaf = Assert.Single(ChildrenOf(mount, textSection));
            Assert.Equal("text", CreateOf(mount, textLeaf).NodeType);
            Assert.Empty(StylesOf(mount, textLeaf)); // no size: the MEASURE drives it
            ReplaceTextPatch label = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(),
                p => p.Text == MeasuredText);
            Assert.Equal(textLeaf, CreateOf(mount, label.NodeId).ParentId);

            // [4] the back-nav row (measured button, nav parity with the other pages).
            AssertStyles(mount, backSection, "back section",
                ("flexDirection", "row"), ("width", "300"));
            int back = Assert.Single(ChildrenOf(mount, backSection));
            Assert.Equal("button", CreateOf(mount, back).NodeType);
            // The button carries NO style at all — its size is the MEASURED one
            // (DoD #3's second leaf). If a style ever appears here, the shells'
            // measure func is no longer what decides its frame.
            Assert.Empty(StylesOf(mount, back));
            ReplaceTextPatch caption = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(),
                p => p.Text == "← Back");
            Assert.Equal(back, CreateOf(mount, caption.NodeId).ParentId);

            // One event on the page: the back click. And NOT ONE prop patch —
            // every style rides the SetStyle wire (kind 6); if a flex name ever
            // falls out of the renderer allow-list it lands here as an
            // UpdateProp and this fails.
            AttachEventPatch attach = Assert.Single(mount.Patches.OfType<AttachEventPatch>());
            Assert.Equal(back, attach.NodeId);
            Assert.Equal("click", attach.EventName);
            Assert.Empty(mount.Patches.OfType<UpdatePropPatch>());

            // The whole tree, counted: 1 root + 5 sections + 3 + 3 + 4 boxes
            // + text leaf + its text node + button + its label node = 20 creates.
            Assert.Equal(20, mount.Patches.OfType<CreateNodePatch>().Count());
        }
        finally
        {
            TearDown();
        }
    }

    /// <summary>The page is reachable BY ROUTE, and its back button leaves by
    /// the same nav path the other pages use (INavigationManager → "/").</summary>
    [Fact]
    public void BackButton_NavigatesToTheDemoRoot()
    {
        var (mount, frames) = MountLayoutDemo();
        try
        {
            INavigationManager nav =
                Assert.IsAssignableFrom<INavigationManager>(HostSession.CurrentNavigationManager);
            // Mounting a ROUTED component syncs the route (HostSession.MountRoot).
            Assert.Equal("/layout", nav.CurrentRoute);

            AttachEventPatch back = Assert.Single(mount.Patches.OfType<AttachEventPatch>());
            Assert.Equal(0, Exports.DispatchEventCore((ulong)back.HandlerId, ClickArgs));

            // The swap happened inside the dispatch window: this page's root was
            // removed and BnDemo mounted.
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
