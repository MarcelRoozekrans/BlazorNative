using BlazorNative.Components;
using BlazorNative.Core;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using static BlazorNative.Runtime.Tests.GoldenAssertions;
using BlazorNative.SampleApp;

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
// BnScrollDemo.razor's file header — the canonical table; keep THAT one updated.
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
//
// ── PHASE 6.3: A ROW GAINS AN IMAGE, AND THE FRAME TABLE DOES NOT MOVE ───────
// Images-in-a-scroll-viewport is the most common real usage of both features, and
// leaving it unexercised until someone hits it is how you find out the hard way.
// But this page's frame table IS the 6.2 cross-platform parity contract, and 6.3
// non-negotiable #2 is blunt about it: IF A NUMBER IN THAT TABLE MOVES, THE CHANGE
// IS WRONG.
//
// So the image goes INSIDE an existing row, at a FIXED size SMALLER than the row:
// 40 × 40 in a row that is 80 high and 300 wide. Two independent reasons the table
// cannot move, and both are pinned below (TheImageCannotMoveTheFrameTable):
//
//   • THE ROW'S HEIGHT IS DEFINITE (80). A child cannot grow a definite-height
//     parent, so the row is 80 whatever the image does — and every row's y is
//     80·i, the content node is 10 × 80 = 800, and the scroll range is 600.
//     Unchanged, all of it.
//   • THE IMAGE'S SIZE IS DEFINITE (40 × 40), so Yoga never calls its measure func
//     at all. Even a FAILED load moves nothing: there is no measurement to change.
//
// What DOES move, by exactly one node: the create count (19 → 20) and the style
// count (33 → 35, the image's width + height). Those are counts, not frames. And
// the page gains its first UpdateProp — the image's `src` — which is why the
// "not one prop patch" assertion below became "exactly one, and it is `src`":
// the guard it was really making (no flex name has fallen out of the renderer's
// style allow-list onto the prop wire) is preserved by NAMING the one prop that
// is allowed.
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

    /// <summary>The row that hosts the 6.3 image (design §"The proof surface":
    /// images-in-scroll, proven, WITHOUT touching this page's parity table). Row 0
    /// on purpose — like the flex row, it is fully inside the viewport at offset 0
    /// (y 0..80 of a 200-high viewport), so the load and the (absent) reflow are in
    /// the FIRST screenshot the shells take rather than behind a scroll.</summary>
    private const int ImageRowIndex = 0;

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

    // ── Structural pins ───────────────────────────────────────────────────────
    //
    // Root / CreateOf / ChildrenOf / StylesOf / AssertNode live in
    // GoldenAssertions (`using static`, above) — SHARED with BnLayoutDemoTests.
    // ChildrenOf *is* the shells' insert algorithm; two copies of it would mean
    // the two demo pages are held to different contracts (6.2 Gate 1 review).

    // ── The golden ────────────────────────────────────────────────────────────

    /// <summary>THE PAGE'S ARITHMETIC, CHECKED — not restated. BnScrollDemo owns
    /// the four numbers as `internal const`; this pins the PRODUCTS that Gates 2/3
    /// assert on a device, so the content size is computed by the contract rather
    /// than transcribed by a human into three file headers. Change RowHeightDp and
    /// this goes red before a shell test does.</summary>
    [Fact]
    public void TheContentSizeIsTheContractsArithmetic_NotAProseNumber()
    {
        Assert.Equal(800, BnScrollDemo.RowCount * BnScrollDemo.RowHeightDp);
        Assert.Equal(800, BnScrollDemo.ContentHeightDp);
        Assert.Equal(600, BnScrollDemo.ContentHeightDp - BnScrollDemo.ViewportHeightDp);
        Assert.Equal(600, BnScrollDemo.ScrollRangeDp);
        Assert.Equal(300, BnScrollDemo.ViewportWidthDp);
        Assert.Equal(200, BnScrollDemo.ViewportHeightDp);
        // One colour per row — this golden's row identity depends on it.
        Assert.Equal(BnScrollDemo.RowCount, RowColors.Length);
    }

    /// <summary>PHASE 6.3 NON-NEGOTIABLE #2, AS AN ASSERTION RATHER THAN A PROMISE —
    /// AND THE ASSERTION IS ON THE <b>WIRE</b>, NOT ON A CONSTANT.
    /// <para>The frame table above is the 6.2 cross-platform parity contract; adding
    /// an image to this page must not move a single number in it. It cannot, for two
    /// independent reasons:</para>
    /// <list type="number">
    /// <item><b>The row's height is definite (80).</b> A child cannot grow a
    /// definite-height parent. So every row is still 80 high, still at y = 80·i, the
    /// synthetic content node still computes to 800, and the scrollable range is
    /// still 600 — asserted, unchanged, in
    /// <see cref="TheContentSizeIsTheContractsArithmetic_NotAProseNumber"/>, which
    /// this phase did not touch.</item>
    /// <item><b>The image's size is definite.</b> Both axes are declared, so Yoga
    /// NEVER CALLS ITS MEASURE FUNC — the bytes cannot move anything even in
    /// principle, and a FAILED load moves nothing either. There is no measurement to
    /// change.</item>
    /// </list>
    /// <para><b>Reason 2 is a property of the WIRE, so that is where it is checked.</b>
    /// This test used to assert <c>RowImageWidthDp &gt; 0 &amp;&amp; RowImageHeightDp
    /// &gt; 0</c> — two compile-time constants — and claimed to be the guard that
    /// reddens if a future edit makes the image intrinsic. IT WAS NOT ONE. Delete the
    /// <c>Width</c> parameter from <c>BnScrollDemo.BuildRowImage</c> and the image
    /// becomes intrinsic ON THE WIRE while the constant still reads 40: the old test
    /// stayed green. What actually makes the frame table safe is that the image node
    /// ARRIVES AT THE SHELL carrying a definite width AND a definite height — present
    /// ⇒ definite ⇒ Yoga never calls its measure func ⇒ neither bytes nor failure can
    /// move a frame. So: mount, resolve the image node, and assert its style table is
    /// EXACTLY <c>{width:40, height:40}</c> — nothing missing (an absent axis is an
    /// intrinsic axis) and nothing extra.</para>
    /// <para>The constants are still checked, below, as a SECONDARY sanity check on
    /// the page's arithmetic — the image must be strictly smaller than the row in
    /// both axes, so it cannot overflow it and raise a clipping question two shells
    /// would answer differently. They are the arithmetic; they are not the
    /// guarantee.</para></summary>
    [Fact]
    public void TheImageCannotMoveTheFrameTable()
    {
        var (mount, _) = MountScrollDemo();
        try
        {
            // ── THE GUARANTEE: the image node's WIRE style table ────────────────
            // Resolved structurally (Root → scroll → rows → the image row's child),
            // exactly the way a shell resolves it — never by a raw nodeId.
            int scroll = ChildrenOf(mount, Root(mount).NodeId)[0];
            List<int> rows = ChildrenOf(mount, scroll);
            Assert.Equal(BnScrollDemo.RowCount, rows.Count);
            int image = Assert.Single(ChildrenOf(mount, rows[ImageRowIndex]));

            // EXACTLY these two styles. AssertNode pins the WHOLE table, so:
            //   • a MISSING axis (someone drops Width) → the image is intrinsic on
            //     the wire, Yoga calls its measure func, and the bytes reflow a row
            //     of the 6.2 parity table. RED, here, before any device test runs.
            //   • an EXTRA style (a margin, a grow) → also red: it could move the
            //     image inside the row, or the row itself.
            AssertNode(mount, image, "row 0's image", "image",
                ("width", Dp(BnScrollDemo.RowImageWidthDp)),
                ("height", Dp(BnScrollDemo.RowImageHeightDp)));

            // …and the component really forwarded the CONSTANTS, not two numbers
            // that happen to be 40 — so the arithmetic checked below is arithmetic
            // about the thing on the wire.
            Dictionary<string, string?> styles = StylesOf(mount, image);
            Assert.Equal(Dp(BnScrollDemo.RowImageWidthDp), styles["width"]);
            Assert.Equal(Dp(BnScrollDemo.RowImageHeightDp), styles["height"]);

            // One image, on the whole page — and it is in THIS row.
            CreateNodePatch onlyImage = Assert.Single(
                mount.Patches.OfType<CreateNodePatch>(), p => p.NodeType == "image");
            Assert.Equal(image, onlyImage.NodeId);
        }
        finally
        {
            TearDown();
        }

        // ── SECONDARY: the page's arithmetic (NOT the guarantee) ───────────────
        // The image is strictly smaller than the row in both axes, so it cannot
        // overflow it and raise a clipping question two shells would answer
        // differently. One conjunct per Assert — a two-conjunct Assert.True names
        // neither half when it fails (the 6.2 review's finding).
        Assert.True(BnScrollDemo.RowImageHeightDp < BnScrollDemo.RowHeightDp,
            $"the image ({BnScrollDemo.RowImageHeightDp}dp) must be SHORTER than the row "
            + $"({BnScrollDemo.RowHeightDp}dp) it sits in.");
        Assert.True(BnScrollDemo.RowImageWidthDp < BnScrollDemo.ViewportWidthDp,
            $"the image ({BnScrollDemo.RowImageWidthDp}dp) must be NARROWER than the row "
            + $"({BnScrollDemo.ViewportWidthDp}dp — the content node spans the viewport).");

        // One image, in one row, and NOT the row that already hosts the nested flex
        // row: two features in one row would make a failure ambiguous.
        Assert.NotEqual(FlexRowIndex, ImageRowIndex);
        Assert.InRange(ImageRowIndex, 0, BnScrollDemo.RowCount - 1);
        Assert.Equal(ImageRowIndex, BnScrollDemo.ImageRowIndex);

        // The fixture is the SAME one BnImageDemo's fixed case uses — one fixture,
        // one loopback origin, both demos (the shells stand up one server, and CI
        // never touches the public internet — non-negotiable #5).
        Assert.Equal(BnImageDemo.FixedSrc, BnScrollDemo.RowImageSrc);
    }

    /// <summary>A dp constant as the renderer puts it on the wire — bare, invariant,
    /// no unit suffix (the style value grammar, 6.1 non-negotiable).</summary>
    private static string Dp(int value)
        => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

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
            //     300×200 and NOTHING ELSE. AssertNode pins the WHOLE style table,
            //     so this is also the pin on decision 1: BnScroll is a flex ITEM.
            //     No flexDirection, no justifyContent/alignItems/flexWrap, no gap,
            //     no padding — the container-layout family is not a parameter, and
            //     on a scroll node the shells IGNORE those style names anyway (they
            //     would style the node whose only child is the synthetic content
            //     node: gap spaces nothing, justify pushes the content to a
            //     negative offset a scroll view can never scroll back to).
            AssertNode(mount, scroll, "scroll viewport", "scroll",
                ("width", "300"), ("height", "200"));

            // Ten rows, each 80 high, DIRECT children of the scroll node on the
            // wire. The shells re-parent them into the synthetic content node —
            // whose Yoga-computed height is therefore 10 × 80 = 800, against a
            // 200-high viewport: contentSize 800, scrollable range 600. THAT is
            // the whole phase, and it is arithmetic on THESE numbers (pinned as
            // products in TheContentSizeIsTheContractsArithmetic).
            List<int> rows = ChildrenOf(mount, scroll);
            Assert.Equal(BnScrollDemo.RowCount, rows.Count);
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

            // Row 0 hosts THE 6.3 IMAGE (design §"The proof surface": images inside
            // a scroll viewport, proven — without touching this page's parity
            // table). 40 × 40, FIXED, inside a row that is 80 high and 300 wide:
            //   • the row's height is DEFINITE, so the image cannot grow it;
            //   • the image's size is DEFINITE, so Yoga never calls its measure
            //     func — the bytes cannot move a frame even in principle, and a
            //     failed load moves nothing either.
            // Its frame is (0, 0, 40, 40) in the row's coordinates (the row is a
            // column with Yoga's default alignItems:stretch, but a definite width
            // does not stretch). EVERY NUMBER IN THE 6.2 FRAME TABLE IS UNCHANGED —
            // same ten rows at y = 80·i, same 800dp content node, same 600 range.
            // Pinned as a decision in TheImageCannotMoveTheFrameTable.
            int rowImage = Assert.Single(ChildrenOf(mount, rows[ImageRowIndex]));
            AssertNode(mount, rowImage, "row 0's image", "image",
                ("width", "40"), ("height", "40"));

            // Every OTHER row is childless — a scrolled row is a plain coloured
            // band, so nothing but rows 0 and 1 can perturb the 80-high grid.
            for (var i = 0; i < rows.Count; i++)
            {
                if (i == FlexRowIndex || i == ImageRowIndex)
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

            // One event on the page: the back click.
            AttachEventPatch attach = Assert.Single(mount.Patches.OfType<AttachEventPatch>());
            Assert.Equal(back, attach.NodeId);
            Assert.Equal("click", attach.EventName);

            // EXACTLY ONE prop patch: the image's `src` (Phase 6.3). This used to be
            // Assert.Empty, and the guard it was really making is preserved by
            // NAMING the one prop that is allowed: every STYLE rides the SetStyle
            // wire (kind 6), so if a flex name ever falls out of the renderer's
            // allow-list it lands here as a SECOND UpdateProp — with a name that is
            // not "src" — and this fails. (And `src` itself must never move the
            // other way, onto the style wire: pinned in
            // Renderer.Tests/StyleAttributePartitionTests.Src_IsAProp_NotAStyle.)
            UpdatePropPatch src = Assert.Single(mount.Patches.OfType<UpdatePropPatch>());
            Assert.Equal(rowImage, src.NodeId);
            Assert.Equal("src", src.Name);
            Assert.Equal(BnScrollDemo.RowImageSrc, src.Value);

            // The whole tree, counted: 1 root + 1 scroll + 10 rows + 1 image
            // + 1 flex row + 3 boxes + 1 back section + 1 button + 1 label node
            // = 20 creates. 19 → 20 IS THE 6.3 DELTA, and it is exactly one node:
            // the image. TWENTY, not twenty-one: the content node is SYNTHETIC. A
            // twenty-first create here would mean it leaked onto the wire.
            Assert.Equal(20, mount.Patches.OfType<CreateNodePatch>().Count());

            // …and the MIRROR pin on the styles. AssertNode covers 19 of the 20
            // nodes — a stray SetStyle on the 20th (the button's caption text node)
            // would pass every assertion above. The arithmetic:
            //     root         flexDirection                                 1
            //     scroll       width, height                                 2
            //     rows 0-9     height + backgroundColor, ×10                20
            //     row 0 image  width, height                                 2  ← 6.3
            //     flex row     flexDirection, flexGrow                       2
            //     boxes A/B/C  (width|flexGrow) + backgroundColor, ×3        6
            //     back section flexDirection, width                          2
            //     button       (none — its size is MEASURED)                 0
            //     caption      (none — a text node carries no style)         0
            //                                                        total  35
            // 33 → 35: the image's two, and NOTHING ELSE. These are COUNTS, not
            // frames — the 6.2 frame table is untouched (non-negotiable #2).
            Assert.Equal(35, mount.Patches.OfType<SetStylePatch>().Count());

            // Exactly ONE image node on the page — in exactly one row.
            Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.NodeType == "image");

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
