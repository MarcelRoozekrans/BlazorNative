using BlazorNative.Components;
using BlazorNative.Core;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using static BlazorNative.Runtime.Tests.GoldenAssertions;
using BlazorNative.SampleApp;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// BnImagePolishDemoTests — Phase 7.5 (design §"The proof surface: /imagepolish").
//
// THE SOURCE OF TRUTH FOR GATES 2 AND 3. This golden pins what .NET puts on
// the wire for "/imagepolish": the STATIC frame table (a mount whose every
// section is declared except the two intrinsic cases — and those are the
// point), the per-image prop tables (`src` + `placeholderColor` /
// `contentMode` — the 7.5 vocabulary riding the 6.3 wire), the ONE `error`
// attach (case [1]'s, and nobody else's — attach iff HasDelegate, visible on
// a page), the error round-trip re-rendering ONLY the echo text, and the mode
// quartet's four IDENTICAL style tables under four DIFFERENT mode words.
//
// WHAT THIS GOLDEN CANNOT SEE — the reflow, the paint, the dispatch site —
// is exactly Gates 2/3's half, and the page header (BnImagePolishDemo.razor)
// states it: the placeholder state table's four rows are VIEW-STATE pins per
// shell; the band y's are frame assertions off these numbers; the counted
// OnError dispatch (at-most-once, behind the liveness guard, deferred out of
// any batch, CANCELLED-never-dispatches) is the shells' dispatch-site
// discipline, decision-table-tested on the JVM lane and staged on iOS. The
// synchronization gate is 6.3's, verbatim: AFTER assertions only once every
// request on the page has terminated, counted via loader terminal callbacks.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class BnImagePolishDemoTests
{
    private const string ClickArgs = /*lang=json*/ """{"name":"click"}""";

    private static string ErrorArgs(string payload)
        => "{\"name\":\"error\",\"payload\":\"" + payload + "\"}";

    // The band colours, in section order — the golden's row identity (nodeIds
    // are opaque; a colour is not). Mirrors BnImagePolishDemo's constants.
    private const string BandUnderLoading = "#42A5F5";          // blue
    private const string BandUnderError = "#EF5350";            // red
    private const string BandUnderModes = "#AB47BC";            // purple ×4
    private const string BandUnderIntrinsicFailing = "#FF7043"; // deep orange
    private const string BandUnderIntrinsicLoading = "#66BB6A"; // green
    private const string ModeBackdrop = "#263238";

    private static (RenderFrame Mount, List<RenderFrame> Frames) MountPolishDemo()
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
        Assert.Equal(0, HostSession.TryMount("BnImagePolishDemo"));
        Assert.NotEmpty(frames);
        return (frames[0], frames);
    }

    private static void TearDown()
    {
        HostSession.ResetForTests();
        NativeShellBridge.ResetForTests();
    }

    /// <summary>The prop table a node carries in this frame — asserted WHOLE
    /// (the AssertNode discipline, applied to the prop wire): nothing missing,
    /// nothing extra, and by construction on the patch KIND.</summary>
    private static void AssertProps(
        RenderFrame frame, int nodeId, string what,
        params (string Name, string Value)[] expected)
    {
        Dictionary<string, string?> actual = frame.Patches.OfType<UpdatePropPatch>()
            .Where(p => p.NodeId == nodeId)
            .ToDictionary(p => p.Name, p => p.Value);
        Dictionary<string, string?> want = expected.ToDictionary(e => e.Name, e => (string?)e.Value);

        Assert.True(
            want.Count == actual.Count
                && want.All(kv => actual.TryGetValue(kv.Key, out string? v) && v == kv.Value),
            $"""
             props of "{what}" (node {nodeId}) do not match the golden:
               expected: {RenderStyles(want)}
               actual:   {RenderStyles(actual)}
             """);
    }

    /// <summary>THE `error` attach — exactly one on the whole page, and it is
    /// case [1]'s image (the only bound OnError; [3] fails too and dispatches
    /// NOTHING — the attach-iff-HasDelegate rule as a page-level fact).</summary>
    private static AttachEventPatch TheErrorAttach(RenderFrame mount)
        => Assert.Single(mount.Patches.OfType<AttachEventPatch>(),
            p => p.EventName == "error");

    /// <summary>The echo's TEXT NODE: section [1]'s child [2] is the
    /// fixed-height echo row; its single child is the BnText span; the span's
    /// single child carries the string.</summary>
    private static int EchoTextNode(RenderFrame mount)
    {
        int errorSection = ChildrenOf(mount, Root(mount).NodeId)[1];
        int echoRow = ChildrenOf(mount, errorSection)[2];
        int span = Assert.Single(ChildrenOf(mount, echoRow));
        Assert.Equal("text", CreateOf(mount, span).NodeType);
        return Assert.Single(ChildrenOf(mount, span));
    }

    // ── The page's arithmetic, CHECKED — not restated ─────────────────────────

    /// <summary>THE FRAME TABLE'S ARITHMETIC (the BnImageDemo discipline).
    /// BnImagePolishDemo owns the inputs as `internal const`; this pins the
    /// PRODUCTS Gates 2/3 assert on a device — and the aliases that make this
    /// page 6.3's proof re-run rather than a new experiment: the sources ARE
    /// BnImageDemo's fixtures (one origin, one 404 path, one 160×90 reflow
    /// fixture, one 64×48 mode fixture), the declared box IS /image case [0]'s,
    /// and Hi — the page's only moving number — is the /image golden's own
    /// drift-pinned constant.</summary>
    [Fact]
    public void TheDemosNumbers_AreTheContractsArithmetic()
    {
        // The section offsets, computed: [0] 0..140, [1] 140..304 (echo row
        // included and FIXED), [2] 304..624 in 80dp steps, [3] 624..644
        // (band-only, forever), [4] 644..664 before the bytes, [5] at 664
        // before / 664 + Hi after.
        Assert.Equal(140, BnImagePolishDemo.LoadingSectionHeightDp);
        Assert.Equal(140, BnImagePolishDemo.ErrorSectionYDp);
        Assert.Equal(164, BnImagePolishDemo.ErrorSectionHeightDp);
        Assert.Equal(304, BnImagePolishDemo.QuartetSectionYDp);
        Assert.Equal(80, BnImagePolishDemo.ModeStepDp);
        Assert.Equal(320, BnImagePolishDemo.QuartetSectionHeightDp);
        Assert.Equal(624, BnImagePolishDemo.IntrinsicFailingSectionYDp);
        Assert.Equal(644, BnImagePolishDemo.IntrinsicLoadingSectionYDp);
        Assert.Equal(664, BnImagePolishDemo.BackSectionYDp);
        Assert.Equal(754, BnImagePolishDemo.BackSectionYDp + BnImageDemo.IntrinsicNaturalHeightPx);

        // The declared box is /image case [0]'s — the box the 6.3 contract
        // already proves is never measured.
        Assert.Equal(BnImageDemo.FixedWidthDp, BnImagePolishDemo.DeclaredWidthDp);
        Assert.Equal(BnImageDemo.FixedHeightDp, BnImagePolishDemo.DeclaredHeightDp);

        // The fixtures, BY REFERENCE — one origin, declared once, no drift.
        Assert.Equal(BnImageDemo.FixtureOrigin, BnImagePolishDemo.FixtureOrigin);
        Assert.Equal(BnImageDemo.FailingSrc, BnImagePolishDemo.ErrorSrc);
        Assert.Equal(BnImageDemo.IntrinsicSrc, BnImagePolishDemo.IntrinsicSrc);
        Assert.Equal(BnImageDemo.FixedSrc, BnImagePolishDemo.ModeSrc);
        // The held source is NEW (a fixture-server extension, Gates 2/3) and
        // loopback like everything else — CI never touches the internet.
        Assert.StartsWith(BnImagePolishDemo.FixtureOrigin, BnImagePolishDemo.SlowSrc,
            StringComparison.Ordinal);
        Assert.DoesNotContain(BnImagePolishDemo.SlowSrc,
            new[] { BnImageDemo.FixedSrc, BnImageDemo.IntrinsicSrc, BnImageDemo.FailingSrc });

        // The mode box DISAGREES with the fixture's aspect (2:1 vs 4:3) — the
        // clause that makes four modes four visibly different paints. Cross-
        // multiplied so it is exact integer arithmetic.
        Assert.NotEqual(
            BnImagePolishDemo.ModeBoxWidthDp * BnImageDemo.FixedNaturalHeightPx,
            BnImagePolishDemo.ModeBoxHeightDp * BnImageDemo.FixedNaturalWidthPx);

        // The echo, formatted once for every surface: the mount state and the
        // exactly-once state Gates 2/3 transcribe are the function's output.
        Assert.Equal("err:0", BnImagePolishDemo.Echo(0, null));
        Assert.Equal("err:1 " + BnImagePolishDemo.ErrorSrc,
            BnImagePolishDemo.Echo(1, BnImagePolishDemo.ErrorSrc));
    }

    // ── The golden ────────────────────────────────────────────────────────────

    /// <summary>The STATIC frame table, whole: six root children in the
    /// designed order, every declared number on the style wire, every 7.5
    /// prop on the PROP wire (the partition's live half), ONE error attach
    /// (case [1]'s image), and the counted wire so nothing can hide.</summary>
    [Fact]
    public void Mount_Golden_TheStaticFrameTable()
    {
        var (mount, _) = MountPolishDemo();
        try
        {
            CreateNodePatch root = Root(mount);
            AssertNode(mount, root.NodeId, "root", "view", ("flexDirection", "column"));

            List<int> sections = ChildrenOf(mount, root.NodeId);
            Assert.Equal(6, sections.Count);

            // Every case section is a hugging 300-wide flex-start column (the
            // /image rule: flex-start is what lets a measured leaf report its
            // own width — the two intrinsic cases need it, one rule for all).
            (string, string)[] caseColumn =
            [
                ("flexDirection", "column"), ("width", "300"), ("alignItems", "flex-start"),
            ];

            // ── [0] PLACEHOLDER-WHILE-LOADING ────────────────────────────────
            AssertNode(mount, sections[0], "loading section", "view", caseColumn);
            List<int> loadingKids = ChildrenOf(mount, sections[0]);
            Assert.Equal(2, loadingKids.Count);
            AssertNode(mount, loadingKids[0], "loading image", "image",
                ("width", "200"), ("height", "120"));
            AssertProps(mount, loadingKids[0], "loading image",
                ("src", BnImagePolishDemo.SlowSrc),
                ("placeholderColor", BnImagePolishDemo.PlaceholderHex));
            AssertNode(mount, loadingKids[1], "band L", "view",
                ("width", "300"), ("height", "20"), ("backgroundColor", BandUnderLoading));

            // ── [1] ERROR, SPACE KEPT ────────────────────────────────────────
            AssertNode(mount, sections[1], "error section", "view", caseColumn);
            List<int> errorKids = ChildrenOf(mount, sections[1]);
            Assert.Equal(3, errorKids.Count);
            AssertNode(mount, errorKids[0], "error image", "image",
                ("width", "200"), ("height", "120"));
            AssertProps(mount, errorKids[0], "error image",
                ("src", BnImagePolishDemo.ErrorSrc),
                ("placeholderColor", BnImagePolishDemo.PlaceholderHex));
            AssertNode(mount, errorKids[1], "band E", "view",
                ("width", "300"), ("height", "20"), ("backgroundColor", BandUnderError));
            // The echo row: FIXED height, so the round-trip below is a text
            // assertion inside a box that cannot move.
            AssertNode(mount, errorKids[2], "echo row", "view",
                ("width", "300"), ("height", "24"));
            Assert.Equal(BnImagePolishDemo.Echo(0, null),
                Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(),
                    p => p.NodeId == EchoTextNode(mount)).Text);

            // THE ONE ERROR ATTACH is this image's — [3]'s failure, unbound,
            // dispatches nothing (attach iff HasDelegate, as a page fact).
            Assert.Equal(errorKids[0], TheErrorAttach(mount).NodeId);

            // ── [2] THE FOUR MODES ───────────────────────────────────────────
            AssertNode(mount, sections[2], "quartet section", "view", caseColumn);
            List<int> quartetKids = ChildrenOf(mount, sections[2]);
            Assert.Equal(8, quartetKids.Count); // image, band, ×4 — the 80dp rhythm
            string[] modeWords = ["contain", "cover", "stretch", "center"];
            for (int m = 0; m < 4; m++)
            {
                int image = quartetKids[2 * m];
                int band = quartetKids[2 * m + 1];
                AssertNode(mount, image, $"mode image [{modeWords[m]}]", "image",
                    ("width", "120"), ("height", "60"), ("backgroundColor", ModeBackdrop));
                AssertProps(mount, image, $"mode image [{modeWords[m]}]",
                    ("src", BnImagePolishDemo.ModeSrc),
                    ("contentMode", modeWords[m]));
                AssertNode(mount, band, $"band M{m}", "view",
                    ("width", "300"), ("height", "20"), ("backgroundColor", BandUnderModes));
            }

            // ── [3] PLACEHOLDER NEVER MEASURES, failing side ─────────────────
            AssertNode(mount, sections[3], "intrinsic-failing section", "view", caseColumn);
            List<int> failingKids = ChildrenOf(mount, sections[3]);
            Assert.Equal(2, failingKids.Count);
            // The EMPTY style table IS the case: a stray width would turn the
            // never-measures proof into a declared box (the /image lesson).
            AssertNode(mount, failingKids[0], "intrinsic-failing image (NO width/height)", "image");
            AssertProps(mount, failingKids[0], "intrinsic-failing image",
                ("src", BnImagePolishDemo.ErrorSrc),
                ("placeholderColor", BnImagePolishDemo.PlaceholderHex));
            AssertNode(mount, failingKids[1], "band X", "view",
                ("width", "300"), ("height", "20"), ("backgroundColor", BandUnderIntrinsicFailing));

            // ── [4] PLACEHOLDER NEVER MEASURES, loading side — THE REFLOW ───
            AssertNode(mount, sections[4], "intrinsic-loading section", "view", caseColumn);
            List<int> reflowKids = ChildrenOf(mount, sections[4]);
            Assert.Equal(2, reflowKids.Count);
            AssertNode(mount, reflowKids[0], "intrinsic-loading image (NO width/height)", "image");
            AssertProps(mount, reflowKids[0], "intrinsic-loading image",
                ("src", BnImagePolishDemo.IntrinsicSrc),
                ("placeholderColor", BnImagePolishDemo.PlaceholderHex));
            AssertNode(mount, reflowKids[1], "band I", "view",
                ("width", "300"), ("height", "20"), ("backgroundColor", BandUnderIntrinsicLoading));

            // The two intrinsic images are the SAME node on the wire but for
            // their URL — the experiment's control, exactly as on /image, now
            // WITH a placeholder present on both.
            Assert.Equal(StylesOf(mount, failingKids[0]), StylesOf(mount, reflowKids[0]));
            Assert.Empty(StylesOf(mount, reflowKids[0]));

            // ── [5] the back row ─────────────────────────────────────────────
            AssertNode(mount, sections[5], "back section", "view",
                ("flexDirection", "row"), ("width", "300"));
            int back = Assert.Single(ChildrenOf(mount, sections[5]));
            Assert.Equal("button", CreateOf(mount, back).NodeType);
            Assert.Empty(StylesOf(mount, back));
            ReplaceTextPatch caption = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(),
                p => p.Text == "← Back");
            Assert.Equal(back, CreateOf(mount, caption.NodeId).ParentId);

            // ── The counted wire ─────────────────────────────────────────────
            // Creates: 1 root + 5 case sections + 8 images + 8 bands +
            // 1 echo row + 2 echo (span + text node) + 1 back row +
            // 2 back button (button + text node) = 28.
            Assert.Equal(28, mount.Patches.OfType<CreateNodePatch>().Count());
            Assert.Equal(8, mount.Patches.OfType<CreateNodePatch>().Count(p => p.NodeType == "image"));

            // Styles: root 1 + case sections 5×3 + back row 2 + declared
            // images 2×2 + mode images 4×3 + bands 8×3 + echo row 2 = 60.
            Assert.Equal(60, mount.Patches.OfType<SetStylePatch>().Count());

            // Props: 8 src + 4 placeholderColor + 4 contentMode = 16 — the
            // page's WHOLE prop vocabulary, counted by name so a 7.5 prop
            // drifting onto the style wire (or a style onto the prop wire)
            // fails as a count, not a silent extra.
            List<UpdatePropPatch> props = [.. mount.Patches.OfType<UpdatePropPatch>()];
            Assert.Equal(16, props.Count);
            Assert.Equal(8, props.Count(p => p.Name == "src"));
            Assert.Equal(4, props.Count(p => p.Name == "placeholderColor"));
            Assert.Equal(4, props.Count(p => p.Name == "contentMode"));

            // Attaches: the ONE error + the back click = 2.
            Assert.Equal(2, mount.Patches.OfType<AttachEventPatch>().Count());
            Assert.Equal(1, mount.Patches.OfType<AttachEventPatch>().Count(p => p.EventName == "click"));
        }
        finally
        {
            TearDown();
        }
    }

    // ── The error round-trip (decision 2's .NET half, on the page) ────────────

    /// <summary>The counted dispatch: an `error` through the production
    /// ingress re-renders ONLY the echo text — no create, no remove, no
    /// style, no prop — so the static frame table survives its own failure
    /// case (the space-kept assertion's wire half). And the echo COUNTS
    /// (err:1 → err:2), which is what makes "dispatched exactly once" a
    /// falsifiable device assertion rather than a latch: a shell that
    /// double-dispatched would print err:2 on a screen expecting err:1.</summary>
    [Fact]
    public void ErrorDispatch_RoundTripsIntoTheEcho_AndMovesNoFrame()
    {
        var (mount, frames) = MountPolishDemo();
        try
        {
            AttachEventPatch attach = TheErrorAttach(mount);
            int echo = EchoTextNode(mount);

            int before = frames.Count;
            Assert.Equal(0, Exports.DispatchEventCore(
                (ulong)attach.HandlerId, ErrorArgs(BnImagePolishDemo.ErrorSrc)));

            // ONE re-render frame; its ONLY patch is the echo's new text.
            RenderFrame after = Assert.Single(frames.Skip(before));
            ReplaceTextPatch text = Assert.IsType<ReplaceTextPatch>(
                Assert.Single(after.Patches, p => p is not CommitFramePatch));
            Assert.Equal(echo, text.NodeId);
            Assert.Equal(BnImagePolishDemo.Echo(1, BnImagePolishDemo.ErrorSrc), text.Text);

            // The counter is a counter, not a latch.
            Assert.Equal(0, Exports.DispatchEventCore(
                (ulong)attach.HandlerId, ErrorArgs(BnImagePolishDemo.ErrorSrc)));
            Assert.Equal(BnImagePolishDemo.Echo(2, BnImagePolishDemo.ErrorSrc),
                Assert.Single(frames[^1].Patches.OfType<ReplaceTextPatch>()).Text);
        }
        finally
        {
            TearDown();
        }
    }

    // ── The mode quartet (decision 3's .NET half) ─────────────────────────────

    /// <summary>FOUR IDENTICAL STYLE TABLES UNDER FOUR DIFFERENT MODE WORDS —
    /// the .NET statement of mode-invariance. The frames Gates 2/3 compute
    /// from these styles are identical BY CONSTRUCTION on this wire; a shell
    /// whose mode arm participates in measure breaks the DEVICE frame table,
    /// and this golden is the proof the difference cannot have come from
    /// .NET. The words are pinned in wire order and exactly lowercase (the
    /// strict four-word set the shells parse by exact match).</summary>
    [Fact]
    public void ModeQuartet_FourIdenticalStyleTables_FourDifferentModeWords()
    {
        var (mount, _) = MountPolishDemo();
        try
        {
            int quartet = ChildrenOf(mount, Root(mount).NodeId)[2];
            List<int> kids = ChildrenOf(mount, quartet);
            int[] images = [kids[0], kids[2], kids[4], kids[6]];

            // One style table, four nodes — byte-identical.
            Dictionary<string, string?> reference = StylesOf(mount, images[0]);
            Assert.Equal(3, reference.Count); // width, height, backgroundColor
            Assert.All(images, i => Assert.Equal(reference, StylesOf(mount, i)));

            // Four mode words, in wire order, exactly these strings.
            Assert.Equal(
                ["contain", "cover", "stretch", "center"],
                images.Select(i => Assert.Single(
                    mount.Patches.OfType<UpdatePropPatch>(),
                    p => p.NodeId == i && p.Name == "contentMode").Value));

            // One fixture, four requests — the same 64×48 bytes four ways.
            Assert.All(images, i => Assert.Equal(BnImagePolishDemo.ModeSrc,
                Assert.Single(mount.Patches.OfType<UpdatePropPatch>(),
                    p => p.NodeId == i && p.Name == "src").Value));
        }
        finally
        {
            TearDown();
        }
    }

    // ── Nav parity ────────────────────────────────────────────────────────────

    /// <summary>The page is reachable BY ROUTE ("/imagepolish") and leaves by
    /// the nav path every page uses. The removal purges EIGHT image nodes —
    /// Gates 2/3 owe the cancellation of every in-flight request in that
    /// purge (including the HELD /slow.png one: a suite that navigates away
    /// before releasing it must not leak a request, and CANCELLED must not
    /// dispatch an error — decision 2's rule, exercised by this page's own
    /// shape).</summary>
    [Fact]
    public void BackButton_NavigatesToTheDemoRoot()
    {
        var (mount, frames) = MountPolishDemo();
        try
        {
            INavigationManager nav =
                Assert.IsAssignableFrom<INavigationManager>(HostSession.CurrentNavigationManager);
            Assert.Equal("/imagepolish", nav.CurrentRoute);

            AttachEventPatch back = Assert.Single(mount.Patches.OfType<AttachEventPatch>(),
                p => p.EventName == "click");
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
