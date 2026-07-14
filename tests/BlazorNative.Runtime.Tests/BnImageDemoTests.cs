using BlazorNative.Components;
using BlazorNative.Core;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using static BlazorNative.Runtime.Tests.GoldenAssertions;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// BnImageDemoTests — Phase 6.3 Task 1.3 (design §"The proof surface").
//
// THE SOURCE OF TRUTH FOR GATES 2 AND 3. This golden pins what .NET puts on the
// wire for "/image": the creates (with their insert indices), the SetStyle values
// and — new in this phase — the UpdateProp `src` values. The shells' frame
// assertions are DERIVED from it: Gate 2 (AVD, Coil) and Gate 3 (iOS simulator,
// Kingfisher) assert THE SAME NUMBERS, because Yoga computes in
// density-independent units on both. If a number here changes, the two shells'
// expectations change with it, and that is a deliberate act.
//
// The expected COMPUTED FRAMES (dp, relative to the parent) live in
// BnImageDemo.cs's file header — the canonical table, in TWO states (before the
// bytes land and after), because THAT DIFFERENCE IS THE PHASE. Keep that one
// updated.
//
// ── WHAT THIS GOLDEN CAN AND CANNOT SEE ──────────────────────────────────────
// It sees the WIRE. It cannot see a measured frame, so it cannot assert the
// reflow — measurement happens in the shells, against a fixture whose natural
// size (Wi × Hi) Gates 2/3 supply. What it CAN pin, and does, is the exact input
// that makes the three cases distinguishable at all:
//
//   fixed image      styles = {width:200, height:120}   → definite. Yoga never
//                                                          calls measure; the bytes
//                                                          never move the frame.
//   intrinsic image  styles = {}  (NOTHING)             → a measured leaf. 0×0
//                                                          before the bytes, its
//                                                          natural size after.
//   failing image    styles = {}  (NOTHING)             → the same measured leaf;
//                                                          only the URL differs, and
//                                                          a failure keeps it 0×0.
//
// The EMPTY style tables are load-bearing, not an omission: a stray width on the
// intrinsic image would silently turn the reflow proof into the no-reflow proof,
// and both shells would still be "green". AssertNode pins the WHOLE table, so a
// style appearing on either of them fails here first.
//
// ── AND THE THREE THINGS THIS GOLDEN CANNOT PIN, WHICH GATES 2/3 MUST ───────
// Stated here as well as in BnImageDemo.cs's header, because this is the file a
// Gate 2/3 implementer reads to learn what to assert — and all three are ways a
// device suite goes GREEN having loaded nothing:
//
//   1. THE SYNCHRONIZATION GATE. The AFTER table may only be asserted once ALL
//      THREE requests have TERMINATED. Two of the three cases assert "nothing
//      moved", so asserting the AFTER table straight after mount passes both of
//      them. Await each loader's PER-NODE TERMINAL CALLBACK (Coil's
//      ImageRequest.Listener onSuccess/onError; Kingfisher's completionHandler) for
//      all three nodes — NOT band I's movement, which only witnesses case [1].
//   2. THE CLEARTEXT OPT-IN. Cleartext HTTP is blocked by default on BOTH
//      platforms, and a blocked load is INDISTINGUISHABLE from the 404 case [2]
//      expects. Android: covered for instrumented tests by the debug
//      network_security_config (release blocks it — see the header). iOS: ATS,
//      and Info.plist has no NSAppTransportSecurity key yet. Gate 3 must add one.
//   3. THE FIXTURE PRECONDITIONS, ASSERTED IN THE TEST, before any frame:
//      0 < Wi ≤ 300, Hi > 0, and (Wfixed, Hfixed) ≠ (200, 120). Plus the positive
//      assertion Wi > 0 && Hi > 0 on the intrinsic image's computed frame, so that
//      "band F did not move" means "the bytes landed and did not move it" rather
//      than "no bytes landed".
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class BnImageDemoTests
{
    private const string ClickArgs = /*lang=json*/ """{"name":"click"}""";

    /// <summary>The three sibling colours, in section order — the golden's row
    /// identity (nodeIds are opaque; a colour is not). Mirrors BnImageDemo's
    /// constants, and it is a sibling's MOVEMENT that Gates 2/3 read off a
    /// screenshot, so each one is distinct.</summary>
    private const string SiblingUnderFixed = "#42A5F5";     // blue
    private const string SiblingUnderIntrinsic = "#66BB6A"; // green
    private const string SiblingUnderFailing = "#EF5350";   // red

    private static (RenderFrame Mount, List<RenderFrame> Frames) MountImageDemo()
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
        Assert.Equal(0, HostSession.TryMount("BnImageDemo"));
        Assert.NotEmpty(frames);
        return (frames[0], frames);
    }

    private static void TearDown()
    {
        HostSession.ResetForTests();
        NativeShellBridge.ResetForTests();
    }

    /// <summary>The ONE prop a node carries — and the assertion is on the patch
    /// KIND by construction (it looks only at UpdatePropPatch). A `src` that
    /// drifted onto the SetStyle wire would carry the right URL and fail HERE, as
    /// a missing prop, rather than passing a value-only check.</summary>
    private static string? PropOn(RenderFrame frame, int nodeId, string name)
        => Assert.Single(frame.Patches.OfType<UpdatePropPatch>(),
            p => p.NodeId == nodeId && p.Name == name).Value;

    // ── The page's arithmetic, CHECKED — not restated ──────────────────────────

    /// <summary>THE FRAME TABLE'S ARITHMETIC. BnImageDemo owns the inputs as
    /// `internal const`; this pins the PRODUCTS that Gates 2/3 assert on a device,
    /// so the section offsets are computed by the contract rather than transcribed
    /// by a human into three file headers.
    /// <para>The two that MATTER, and why:</para>
    /// <list type="bullet">
    /// <item><b>NOTHING SITS ABOVE THE FIXED IMAGE.</b> Section [0] is the root
    /// column's FIRST child, so its y is 0 by construction and no reflow anywhere on
    /// this page can move it. That is what makes "band F's y is 120 before AND after
    /// the bytes" a real no-reflow assertion about the IMAGE, instead of a
    /// coincidence about the page.</item>
    /// <item>Every section is a hugging column, so a section's height is the sum of
    /// its two children — which is why the intrinsic image's Hi propagates DOWN the
    /// page (and only down) when the bytes land.</item>
    /// </list></summary>
    [Fact]
    public void TheFrameTableIsTheContractsArithmetic_NotAProseNumber()
    {
        // The fixed case: 200 × 120, plus a 20dp sibling → a 140dp section.
        Assert.Equal(200, BnImageDemo.FixedWidthDp);
        Assert.Equal(120, BnImageDemo.FixedHeightDp);
        Assert.Equal(20, BnImageDemo.SiblingHeightDp);
        Assert.Equal(140, BnImageDemo.FixedHeightDp + BnImageDemo.SiblingHeightDp);
        Assert.Equal(140, BnImageDemo.FixedSectionHeightDp);

        // …so the intrinsic section starts at y = 140, and (BEFORE the bytes, with
        // its image at 0×0) is 20 high — which puts the failing section at 160 and
        // the back row at 180. After the bytes land, everything from the intrinsic
        // image's sibling DOWN shifts by exactly Hi. That is the whole reflow, and
        // it is arithmetic on THESE numbers.
        Assert.Equal(140, BnImageDemo.IntrinsicSectionYDp);
        Assert.Equal(160, BnImageDemo.FailingSectionYDp);
        Assert.Equal(180, BnImageDemo.BackSectionYDp);

        // Every section is 300 wide, and its children are flex-start on the cross
        // axis — see the WhyEverySectionIsAlignFlexStart pin below.
        Assert.Equal(300, BnImageDemo.SectionWidthDp);

        // The three URLs are DISTINCT — three cases, three sources. A copy-paste
        // that pointed the intrinsic image at the failing URL would leave every
        // structural assertion below green and quietly delete the reflow proof.
        Assert.Equal(3, new HashSet<string>(
        [
            BnImageDemo.FixedSrc, BnImageDemo.IntrinsicSrc, BnImageDemo.FailingSrc,
        ]).Count);
        // …and all three are LOOPBACK (6.3 non-negotiable #5: CI never touches the
        // public internet). A suite whose green depends on a remote host is not a
        // suite — and this page IS what Gates 2/3 load on a device.
        Assert.All(
            new[] { BnImageDemo.FixedSrc, BnImageDemo.IntrinsicSrc, BnImageDemo.FailingSrc },
            src => Assert.StartsWith(BnImageDemo.FixtureOrigin, src, StringComparison.Ordinal));
        Assert.StartsWith("http://127.0.0.1:", BnImageDemo.FixtureOrigin, StringComparison.Ordinal);
    }

    /// <summary>WHY EVERY SECTION IS <c>Align=FlexStart</c>, pinned as a decision
    /// rather than left as a value in the golden below.
    /// <para>A section is a COLUMN, so its cross axis is WIDTH — and Yoga's default
    /// <c>alignItems</c> is <b>stretch</b>. An intrinsic image has no width, so
    /// under the default it would be STRETCHED to the section's 300 and its
    /// measured width would never be seen: the "natural size" half of the parity
    /// contract would be untestable, and the page would silently prove only half of
    /// what it claims. <c>flex-start</c> is what lets a measured leaf report its own
    /// width. (The siblings all carry an explicit width, so they do not care either
    /// way — but the image does, and one rule for the section is one rule to
    /// state.)</para></summary>
    [Fact]
    public void EverySectionIsAlignFlexStart_SoAMeasuredImageReportsItsOwnWidth()
    {
        var (mount, _) = MountImageDemo();
        try
        {
            // The count FIRST: Take(3) iterates however many exist, so on a
            // truncated tree (two sections, or none) this test would pass while
            // asserting nothing. The page has FOUR sections — the three cases and
            // the back row — and only the three cases are columns holding an image.
            List<int> sections = ChildrenOf(mount, Root(mount).NodeId);
            Assert.Equal(4, sections.Count);

            foreach (int section in sections.Take(3))
            {
                Assert.Equal("flex-start", StylesOf(mount, section)["alignItems"]);
            }
        }
        finally
        {
            TearDown();
        }
    }

    // ── The golden ────────────────────────────────────────────────────────────

    [Fact]
    public void Mount_Golden_ThreeImageCasesTheirSiblingsStylesAndSrcProps()
    {
        var (mount, _) = MountImageDemo();
        try
        {
            // The root: a BnColumn. The three cases stack down it, and the back row
            // last — so a reflow in [1] can move [2] and the back row, and NOTHING
            // can move [0].
            CreateNodePatch root = Root(mount);
            AssertNode(mount, root.NodeId, "root", "view", ("flexDirection", "column"));

            List<int> sections = ChildrenOf(mount, root.NodeId);
            Assert.Equal(4, sections.Count);
            (int fixedSection, int intrinsicSection, int failingSection, int backSection) =
                (sections[0], sections[1], sections[2], sections[3]);

            // Each case is a hugging 300-wide column whose children are flex-start
            // on the cross axis (see EverySectionIsAlignFlexStart). NO height: a
            // section HUGS its two children, which is what makes the intrinsic
            // image's Hi propagate down the page when the bytes land.
            (string, string)[] caseColumn =
            [
                ("flexDirection", "column"), ("width", "300"), ("alignItems", "flex-start"),
            ];

            // ── [0] FIXED: sized immediately, and NOTHING can move it ────────────
            // Width AND Height are set, so Yoga never calls the measure func: the
            // frame is (0,0,200,120) before the bytes and (0,0,200,120) after. Its
            // sibling sits at y = 120 in BOTH states — that identity IS the
            // "no reflow" proof, and it is only trustworthy because this section is
            // FIRST (nothing above it can reflow and move it for unrelated reasons).
            AssertNode(mount, fixedSection, "fixed section", "view", caseColumn);
            List<int> fixedKids = ChildrenOf(mount, fixedSection);
            Assert.Equal(2, fixedKids.Count);
            AssertNode(mount, fixedKids[0], "fixed image", "image",
                ("width", "200"), ("height", "120"));
            Assert.Equal(BnImageDemo.FixedSrc, PropOn(mount, fixedKids[0], "src"));
            AssertNode(mount, fixedKids[1], "sibling under the fixed image", "view",
                ("width", "300"), ("height", "20"), ("backgroundColor", SiblingUnderFixed));

            // ── [1] INTRINSIC: the reflow ───────────────────────────────────────
            // NO style at all. That empty table is the whole case: an image node is
            // a Yoga leaf with a measure func (6.1 attaches it BY NODETYPE, and
            // `image` is already in the set), so with no declared size it measures
            // 0×0 until the bytes land and its NATURAL size (Wi × Hi) after — at
            // which point its sibling's y goes 0 → Hi. THE SIBLING'S y IS THE PROOF
            // OF THE REFLOW; the image's own frame is not (a shell could paint it
            // and never re-solve, and the image would still look right).
            AssertNode(mount, intrinsicSection, "intrinsic section", "view", caseColumn);
            List<int> intrinsicKids = ChildrenOf(mount, intrinsicSection);
            Assert.Equal(2, intrinsicKids.Count);
            AssertNode(mount, intrinsicKids[0], "intrinsic image (NO width/height)", "image");
            Assert.Equal(BnImageDemo.IntrinsicSrc, PropOn(mount, intrinsicKids[0], "src"));
            AssertNode(mount, intrinsicKids[1], "sibling under the intrinsic image", "view",
                ("width", "300"), ("height", "20"), ("backgroundColor", SiblingUnderIntrinsic));

            // ── [2] FAILING: reserves nothing ───────────────────────────────────
            // Structurally IDENTICAL to the intrinsic case — same empty style table,
            // same measured leaf. ONLY THE URL DIFFERS. That is deliberate: it means
            // the difference the shells observe (0×0 forever, versus 0×0 → Wi × Hi)
            // can only have come from the LOAD, and not from anything .NET said.
            AssertNode(mount, failingSection, "failing section", "view", caseColumn);
            List<int> failingKids = ChildrenOf(mount, failingSection);
            Assert.Equal(2, failingKids.Count);
            AssertNode(mount, failingKids[0], "failing image (NO width/height)", "image");
            Assert.Equal(BnImageDemo.FailingSrc, PropOn(mount, failingKids[0], "src"));
            AssertNode(mount, failingKids[1], "sibling under the failing image", "view",
                ("width", "300"), ("height", "20"), ("backgroundColor", SiblingUnderFailing));

            // The intrinsic and failing images are the SAME node on the wire but for
            // their URL — said as an assertion, because it is the experiment's
            // control and a stray style on either would ruin it.
            Assert.Equal(StylesOf(mount, intrinsicKids[0]), StylesOf(mount, failingKids[0]));
            Assert.Empty(StylesOf(mount, failingKids[0]));

            // ── [3] the back row ────────────────────────────────────────────────
            AssertNode(mount, backSection, "back section", "view",
                ("flexDirection", "row"), ("width", "300"));
            int back = Assert.Single(ChildrenOf(mount, backSection));
            Assert.Equal("button", CreateOf(mount, back).NodeType);
            // The button carries NO style — its size is the MEASURED one, and it is
            // LAST so a font-dependent height cannot shift the frames above it.
            Assert.Empty(StylesOf(mount, back));
            ReplaceTextPatch caption = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(),
                p => p.Text == "← Back");
            Assert.Equal(back, CreateOf(mount, caption.NodeId).ParentId);

            AttachEventPatch attach = Assert.Single(mount.Patches.OfType<AttachEventPatch>());
            Assert.Equal(back, attach.NodeId);
            Assert.Equal("click", attach.EventName);

            // ── The counts ──────────────────────────────────────────────────────
            //
            // The whole tree, counted: 1 root + 4 sections + 3 images + 3 siblings
            // + 1 button + 1 caption text node = 13 creates. A fourteenth would mean
            // a node appeared that this golden does not name.
            Assert.Equal(13, mount.Patches.OfType<CreateNodePatch>().Count());

            // …and the MIRROR pin on the styles — AssertNode covers 11 of the 13
            // nodes, so a stray SetStyle on the button or its caption would pass
            // every assertion above. The arithmetic:
            //     root              flexDirection                            1
            //     3 case sections   flexDirection, width, alignItems, ×3     9
            //     back section      flexDirection, width                     2
            //     fixed image       width, height                            2
            //     intrinsic image   (NONE — that IS the case)                0
            //     failing image     (NONE — the same)                        0
            //     3 siblings        width, height, backgroundColor, ×3       9
            //     button            (none — its size is MEASURED)            0
            //     caption           (none — a text node carries no style)    0
            //                                                        total  23
            Assert.Equal(23, mount.Patches.OfType<SetStylePatch>().Count());

            // …and on the PROPS: exactly THREE, all of them `src`, one per image.
            // The count is the pin that `src` is the ONLY prop this page emits —
            // and the name check is the pin that a flex style has not fallen out of
            // the renderer's allow-list and landed on the prop wire.
            List<UpdatePropPatch> props = [.. mount.Patches.OfType<UpdatePropPatch>()];
            Assert.Equal(3, props.Count);
            Assert.All(props, p => Assert.Equal("src", p.Name));

            // Exactly three image nodes on the page — one per case.
            Assert.Equal(3, mount.Patches.OfType<CreateNodePatch>().Count(p => p.NodeType == "image"));
        }
        finally
        {
            TearDown();
        }
    }

    /// <summary>The page is reachable BY ROUTE ("/image"), and its back button
    /// leaves by the same nav path every other page uses (INavigationManager → "/").
    /// <para>The removal path matters more here than it reads: navigating away
    /// removes three image nodes, and Gates 2/3 must CANCEL their in-flight requests
    /// as part of that purge. A completion firing into a removed node is not
    /// hygiene — on iOS it would touch a freed YGNodeRef (6.2's dangling-pointer
    /// lesson in a new costume). This test pins that ONE RemoveNodePatch for the
    /// root is all the wire says about it; the shells owe the cancellation.</para>
    /// </summary>
    [Fact]
    public void BackButton_NavigatesToTheDemoRoot()
    {
        var (mount, frames) = MountImageDemo();
        try
        {
            INavigationManager nav =
                Assert.IsAssignableFrom<INavigationManager>(HostSession.CurrentNavigationManager);
            // Mounting a ROUTED component syncs the route (HostSession.MountRoot).
            Assert.Equal("/image", nav.CurrentRoute);

            AttachEventPatch back = Assert.Single(mount.Patches.OfType<AttachEventPatch>());
            Assert.Equal(0, Exports.DispatchEventCore((ulong)back.HandlerId, ClickArgs));

            // The swap happened inside the dispatch window: this page's root was
            // removed (ONE RemoveNodePatch for the whole subtree — the three image
            // nodes are inside it) and BnDemo mounted.
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
