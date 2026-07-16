using BlazorNative.Components;
using BlazorNative.Core;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using static BlazorNative.Runtime.Tests.GoldenAssertions;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// BnModalDemoTests — Phase 7.4 (design §"The proof surface").
//
// THE SOURCE OF TRUTH FOR GATES 2 AND 3. This golden pins what .NET puts on
// the wire for "/modal": the CLOSED mount (zero modal wire presence, the five
// root children), the SHOW frame (the modal create AT INSERT INDEX 2 — the
// anchor's slot between the two declared-size siblings, the third
// index-mapping rule's .NET half — with the always-emitted scrimColor, the
// content box's declared 280×180, and the four attaches), the HIDE frame (the
// modal's own remove PLUS the four nested components' disposal removes —
// nothing outside the subtree, no creates), the switch round-trip INSIDE the
// overlay re-rendering the echo, scrim-tap-dismisses vs content-tap-does-not,
// and re-show = re-create with BOUND state intact. The shells' device
// assertions (the centered frames, the overlay LAST at the root, the overlay
// count back to 0, Android's back routing) are DERIVED from these numbers.
//
// THE HIDE FRAME'S REMOVE COUNT IS FIVE, and the arithmetic is recorded
// (BnModalTests' unit half pins the pure shape): the modal element's own diff
// is ONE RemoveNodePatch, and each of the four components inside ChildContent
// (BnText, BnSwitch, BnActivityIndicator, BnButton) adds its own root's
// disposal remove — the 7.2 removes-first shape, descendants before the
// modal, every one of them inside the modal's own subtree. The shells process
// them in order; the modal's remove is the one their two-subtree purge
// (anchor + overlay + the modalOverlays eviction) hangs off in Gates 2/3.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class BnModalDemoTests
{
    private const string ClickArgs = /*lang=json*/ """{"name":"click"}""";

    private static string ChangeArgs(string payload)
        => "{\"name\":\"change\",\"payload\":\"" + payload + "\"}";

    private static (RenderFrame Mount, List<RenderFrame> Frames) MountModalDemo()
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
        Assert.Equal(0, HostSession.TryMount("BnModalDemo"));
        Assert.NotEmpty(frames);
        return (frames[0], frames);
    }

    private static void TearDown()
    {
        HostSession.ResetForTests();
        NativeShellBridge.ResetForTests();
    }

    private static AttachEventPatch ClickAttachOn(RenderFrame frame, int nodeId)
        => Assert.Single(frame.Patches.OfType<AttachEventPatch>(),
            p => p.NodeId == nodeId && p.EventName == "click");

    /// <summary>Dispatches the trigger's click and returns the SHOW frame —
    /// the one carrying the modal create.</summary>
    private static RenderFrame ShowModal(RenderFrame mount, List<RenderFrame> frames)
    {
        int trigger = ChildrenOf(mount, Root(mount).NodeId)[0];
        int before = frames.Count;
        Assert.Equal(0, Exports.DispatchEventCore(
            (ulong)ClickAttachOn(mount, trigger).HandlerId, ClickArgs));
        return Assert.Single(frames.Skip(before),
            f => f.Patches.OfType<CreateNodePatch>().Any(p => p.NodeType == "modal"));
    }

    private static CreateNodePatch ModalOf(RenderFrame show)
        => Assert.Single(show.Patches.OfType<CreateNodePatch>(), p => p.NodeType == "modal");

    private static int ContentBoxOf(RenderFrame show)
        => Assert.Single(ChildrenOf(show, ModalOf(show).NodeId));

    /// <summary>The echo's TEXT NODE: content-box child [0] is the BnText
    /// span; its single child carries the echo.</summary>
    private static int EchoTextNode(RenderFrame show)
    {
        int span = ChildrenOf(show, ContentBoxOf(show))[0];
        Assert.Equal("text", CreateOf(show, span).NodeType);
        return Assert.Single(ChildrenOf(show, span));
    }

    // ── The closed mount ──────────────────────────────────────────────────────

    /// <summary>Visible=false at mount: the modal has ZERO wire presence —
    /// five root children, the two declared-size siblings adjacent (nothing
    /// between them for an anchor to stand in for yet), and only the trigger
    /// and back clicks on the wire.</summary>
    [Fact]
    public void Mount_Golden_TheModalIsAbsent()
    {
        var (mount, _) = MountModalDemo();
        try
        {
            CreateNodePatch root = Root(mount);
            AssertNode(mount, root.NodeId, "root", "view", ("flexDirection", "column"));

            List<int> children = ChildrenOf(mount, root.NodeId);
            Assert.Equal(5, children.Count);

            // [0] the trigger.
            Assert.Equal("button", CreateOf(mount, children[0]).NodeType);
            ClickAttachOn(mount, children[0]);

            // [1]/[2] the declared-size siblings — the frames Gates 2/3 assert
            // hold EXACTLY these values with the modal open (the anchor's
            // zero-footprint rule).
            AssertNode(mount, children[1], "box A", "view",
                ("width", "220"), ("height", "48"), ("backgroundColor", "#336699"));
            AssertNode(mount, children[2], "box B", "view",
                ("width", "220"), ("height", "48"), ("backgroundColor", "#66AA33"));

            // [3] the page-level indicator (decision 5's second hosting
            // context) — the measured-leaf shape: no props, no styles.
            AssertNode(mount, children[3], "page indicator", "activityindicator");
            Assert.DoesNotContain(mount.Patches.OfType<UpdatePropPatch>(),
                p => p.NodeId == children[3]);

            // [4] the back row — nav parity with every other page.
            AssertNode(mount, children[4], "back row", "view",
                ("flexDirection", "row"), ("width", "300"));
            int back = Assert.Single(ChildrenOf(mount, children[4]));
            Assert.Equal("button", CreateOf(mount, back).NodeType);

            // ── The counted wire ─────────────────────────────────────────────
            // Creates: 1 root + 2 trigger (button + text) + 2 boxes +
            // 1 indicator + 1 back row + 2 back button = 9; NO modal, NO
            // switch — closed means zero wire presence (decision 2).
            Assert.Equal(9, mount.Patches.OfType<CreateNodePatch>().Count());
            Assert.DoesNotContain(mount.Patches.OfType<CreateNodePatch>(),
                p => p.NodeType is "modal" or "switch");

            // Attaches: the trigger and the back click, nothing else.
            Assert.Equal(2, mount.Patches.OfType<AttachEventPatch>().Count());

            // Props: NONE — scrimColor exists only while the modal does.
            Assert.Empty(mount.Patches.OfType<UpdatePropPatch>());

            // Styles: root 1 + boxes 3+3 + back row 2 = 9 (the indicator and
            // the buttons carry ZERO — measured, never declared).
            Assert.Equal(9, mount.Patches.OfType<SetStylePatch>().Count());
        }
        finally
        {
            TearDown();
        }
    }

    // ── The show golden ───────────────────────────────────────────────────────

    /// <summary>The SHOW frame: the modal lands AT INSERT INDEX 2 — the
    /// anchor's slot between the declared-size siblings (the third
    /// index-mapping rule's .NET half) — with the always-emitted scrimColor,
    /// ZERO styles on the modal node, the content box's declared 280×180×12
    /// white, the echo/switch/indicator/dismiss inside, four attaches, and
    /// NOT ONE patch touching a node that existed before the show.</summary>
    [Fact]
    public void Show_Golden_TheModalCreateBatch()
    {
        var (mount, frames) = MountModalDemo();
        try
        {
            RenderFrame show = ShowModal(mount, frames);
            CreateNodePatch modal = ModalOf(show);

            // The anchor's slot: root's child list, index 2 (after the
            // trigger and box A — BnModalDemo.razor's frame table).
            Assert.Equal(Root(mount).NodeId, modal.ParentId);
            Assert.Equal(2, modal.InsertIndex);

            // The modal node: scrimColor (the default — this page does not
            // override it, so the golden pins the component default), the
            // dismissal click, and NO styles (the style-ignore rule's .NET
            // half — the surface cannot emit one).
            Assert.Equal("#80000000", Assert.Single(show.Patches.OfType<UpdatePropPatch>(),
                p => p.NodeId == modal.NodeId && p.Name == "scrimColor").Value);
            ClickAttachOn(show, modal.NodeId);
            Assert.Empty(StylesOf(show, modal.NodeId));

            // The content box: the DECLARED 280×180 (the cross-platform
            // numbers; centering is host-derived, Gates 2/3), the swallow.
            int box = ContentBoxOf(show);
            AssertNode(show, box, "content box", "view",
                ("width", "280"), ("height", "180"), ("padding", "12"),
                ("backgroundColor", "#FFFFFF"));
            ClickAttachOn(show, box);

            // Inside: echo, switch (value from the INITIAL const), indicator,
            // dismiss — in page order.
            List<int> inside = ChildrenOf(show, box);
            Assert.Equal(4, inside.Count);
            Assert.Equal("text", CreateOf(show, inside[0]).NodeType);
            Assert.Equal(BnModalDemo.Echo(BnModalDemo.InitialSwitched),
                Assert.Single(show.Patches.OfType<ReplaceTextPatch>(),
                    p => p.NodeId == EchoTextNode(show)).Text);
            Assert.Equal("switch", CreateOf(show, inside[1]).NodeType);
            Assert.Equal("false", Assert.Single(show.Patches.OfType<UpdatePropPatch>(),
                p => p.NodeId == inside[1] && p.Name == "value").Value);
            Assert.Equal("activityindicator", CreateOf(show, inside[2]).NodeType);
            Assert.Equal("button", CreateOf(show, inside[3]).NodeType);

            // ── The counted wire ─────────────────────────────────────────────
            // Creates: modal + box + echo (span + text node) + switch +
            // indicator + dismiss (button + text node) = 8.
            Assert.Equal(8, show.Patches.OfType<CreateNodePatch>().Count());
            // Attaches: the dismissal click, the swallow click, the dismiss
            // button's click + the switch's change = 4.
            Assert.Equal(3, show.Patches.OfType<AttachEventPatch>().Count(p => p.EventName == "click"));
            Assert.Equal(1, show.Patches.OfType<AttachEventPatch>().Count(p => p.EventName == "change"));
            Assert.Equal(4, show.Patches.OfType<AttachEventPatch>().Count());
            // Props: scrimColor + the switch value = 2.
            Assert.Equal(2, show.Patches.OfType<UpdatePropPatch>().Count());
            // Styles: the content box's four, and NOTHING else.
            Assert.Equal(4, show.Patches.OfType<SetStylePatch>().Count());
            // Removes/detaches: ZERO — pinned so a spurious remove cannot
            // hide in a channel the counts above never mention (review S1-1).
            Assert.Empty(show.Patches.OfType<RemoveNodePatch>());
            Assert.Empty(show.Patches.OfType<DetachEventPatch>());

            // The pre-existing tree is UNTOUCHED by the show — "NOT ONE patch
            // naming a pre-existing node" (the zero-footprint rule's wire
            // half: their frames cannot move if no patch names them; the
            // geometric half is Gates 2/3's frame table). The set is the
            // mount frame's FULL create set — all nine ids, not just the two
            // siblings (review S1-1) — so the claim is as wide as the words.
            HashSet<int> preexisting = mount.Patches.OfType<CreateNodePatch>()
                .Select(p => p.NodeId).ToHashSet();
            Assert.Equal(9, preexisting.Count); // the mount golden's own count
            Assert.All(show.Patches, p => Assert.DoesNotContain(NodeIdOf(p), preexisting));
        }
        finally
        {
            TearDown();
        }
    }

    /// <summary>Every patch kind's node id, LOUDLY (review S1-2): the frame
    /// boundary names no node (-1 — never a real id), and any FUTURE kind
    /// throws instead of silently sailing past the untouched-set pin.</summary>
    private static int NodeIdOf(RenderPatch p) => p switch
    {
        CreateNodePatch c => c.NodeId,
        RemoveNodePatch r => r.NodeId,
        UpdatePropPatch u => u.NodeId,
        ReplaceTextPatch t => t.NodeId,
        SetStylePatch s => s.NodeId,
        AttachEventPatch a => a.NodeId,
        DetachEventPatch d => d.NodeId,
        CommitFramePatch => -1, // the frame boundary — no node to name
        _ => throw new InvalidOperationException(
            $"NodeIdOf has no arm for {p.GetType().Name} — extend it so the golden stays loud."),
    };

    // ── The hide golden ───────────────────────────────────────────────────────

    /// <summary>The HIDE frame (the dismiss button — the app's own trigger):
    /// FIVE removes — the modal's own diff (one) plus the four nested
    /// components' disposal removes (the recorded arithmetic, file header) —
    /// every one of them a node the show frame created, the modal's among
    /// them, and NO creates: hide is unmount, nothing is rebuilt.</summary>
    [Fact]
    public void Hide_Golden_TheRemoves()
    {
        var (mount, frames) = MountModalDemo();
        try
        {
            RenderFrame show = ShowModal(mount, frames);
            CreateNodePatch modal = ModalOf(show);
            int dismiss = ChildrenOf(show, ContentBoxOf(show))[3];

            int before = frames.Count;
            Assert.Equal(0, Exports.DispatchEventCore(
                (ulong)ClickAttachOn(show, dismiss).HandlerId, ClickArgs));

            RenderFrame hide = Assert.Single(frames.Skip(before),
                f => f.Patches.OfType<RemoveNodePatch>().Any());
            List<RemoveNodePatch> removes = hide.Patches.OfType<RemoveNodePatch>().ToList();

            Assert.Equal(5, removes.Count);
            // …and five DISTINCT nodes (review Q-2): the count cannot be
            // padded by a double-remove of one id.
            Assert.Equal(5, removes.Select(r => r.NodeId).Distinct().Count());
            Assert.Contains(removes, r => r.NodeId == modal.NodeId);
            // Nothing outside the modal's own subtree leaves.
            HashSet<int> created = show.Patches.OfType<CreateNodePatch>()
                .Select(p => p.NodeId).ToHashSet();
            Assert.All(removes, r => Assert.Contains(r.NodeId, created));
            // Hide is unmount, not a rebuild.
            Assert.Empty(hide.Patches.OfType<CreateNodePatch>());
        }
        finally
        {
            TearDown();
        }
    }

    // ── The dismissal round trips (decision 4's .NET half) ────────────────────

    /// <summary>Scrim tap → the dismissal request on the wire → the bound
    /// page writes Visible=false back → the modal unmounts. The shell closed
    /// NOTHING; the remove is .NET's answer.</summary>
    [Fact]
    public void ScrimTap_DismissRequests_TheBoundPageHides()
    {
        var (mount, frames) = MountModalDemo();
        try
        {
            RenderFrame show = ShowModal(mount, frames);
            CreateNodePatch modal = ModalOf(show);

            int before = frames.Count;
            Assert.Equal(0, Exports.DispatchEventCore(
                (ulong)ClickAttachOn(show, modal.NodeId).HandlerId, ClickArgs));

            Assert.Contains(frames.Skip(before).SelectMany(f => f.Patches)
                .OfType<RemoveNodePatch>(), r => r.NodeId == modal.NodeId);
        }
        finally
        {
            TearDown();
        }
    }

    /// <summary>A content-box tap does NOT dismiss (decision 4's rule): the
    /// swallow dispatch is real (rc 0 — the device counters account for it)
    /// and nothing unmounts, nothing re-renders the page.</summary>
    [Fact]
    public void ContentBoxTap_DoesNotDismiss()
    {
        var (mount, frames) = MountModalDemo();
        try
        {
            RenderFrame show = ShowModal(mount, frames);
            int before = frames.Count;

            Assert.Equal(0, Exports.DispatchEventCore(
                (ulong)ClickAttachOn(show, ContentBoxOf(show)).HandlerId, ClickArgs));

            Assert.Empty(frames.Skip(before).SelectMany(f => f.Patches)
                .OfType<RemoveNodePatch>());
        }
        finally
        {
            TearDown();
        }
    }

    // ── The wire INSIDE the overlay (the switch + echo) ───────────────────────

    /// <summary>Interactivity inside the overlay rides the EXISTING wire
    /// (decision 1's whole point): the switch's change dispatch round-trips
    /// into page state and re-renders the echo — the same production ingress,
    /// no overlay-special path anywhere.</summary>
    [Fact]
    public void SwitchInsideTheModal_RoundTripsIntoTheEcho()
    {
        var (mount, frames) = MountModalDemo();
        try
        {
            RenderFrame show = ShowModal(mount, frames);
            int sw = ChildrenOf(show, ContentBoxOf(show))[1];
            int echo = EchoTextNode(show);
            var change = Assert.Single(show.Patches.OfType<AttachEventPatch>(),
                p => p.NodeId == sw && p.EventName == "change");

            Assert.Equal(0, Exports.DispatchEventCore((ulong)change.HandlerId, ChangeArgs("true")));

            Assert.Equal(BnModalDemo.Echo(true), Assert.Single(
                frames[^1].Patches.OfType<ReplaceTextPatch>(), p => p.NodeId == echo).Text);
        }
        finally
        {
            TearDown();
        }
    }

    /// <summary>Re-show is a RE-CREATE (decision 1's stacking rule rides
    /// this), and BOUND state survives the eviction: the page owns _switched,
    /// so the re-shown switch mounts with the toggled value — the decision-2
    /// consequence ("state does not survive hide unless bound"), positive
    /// half.</summary>
    [Fact]
    public void ReShow_IsARecreate_BoundStateSurvives()
    {
        var (mount, frames) = MountModalDemo();
        try
        {
            RenderFrame show = ShowModal(mount, frames);
            int sw = ChildrenOf(show, ContentBoxOf(show))[1];
            var change = Assert.Single(show.Patches.OfType<AttachEventPatch>(),
                p => p.NodeId == sw && p.EventName == "change");
            Assert.Equal(0, Exports.DispatchEventCore((ulong)change.HandlerId, ChangeArgs("true")));

            // Hide via the scrim (any trigger — same unmount).
            Assert.Equal(0, Exports.DispatchEventCore(
                (ulong)ClickAttachOn(show, ModalOf(show).NodeId).HandlerId, ClickArgs));

            // Re-show: a FRESH create batch, switch value "true" from mount.
            RenderFrame reshow = ShowModal(mount, frames);
            int sw2 = ChildrenOf(reshow, ContentBoxOf(reshow))[1];
            Assert.Equal("switch", CreateOf(reshow, sw2).NodeType);
            Assert.Equal("true", Assert.Single(reshow.Patches.OfType<UpdatePropPatch>(),
                p => p.NodeId == sw2 && p.Name == "value").Value);
            Assert.Equal(BnModalDemo.Echo(true), Assert.Single(
                reshow.Patches.OfType<ReplaceTextPatch>(),
                p => p.NodeId == EchoTextNode(reshow)).Text);
        }
        finally
        {
            TearDown();
        }
    }

    // ── Nav parity ────────────────────────────────────────────────────────────

    /// <summary>The page is reachable BY ROUTE ("/modal") and its back button
    /// leaves by the same nav path every page uses.</summary>
    [Fact]
    public void BackButton_NavigatesToTheDemoRoot()
    {
        var (mount, frames) = MountModalDemo();
        try
        {
            INavigationManager nav =
                Assert.IsAssignableFrom<INavigationManager>(HostSession.CurrentNavigationManager);
            Assert.Equal("/modal", nav.CurrentRoute);

            List<int> children = ChildrenOf(mount, Root(mount).NodeId);
            int back = Assert.Single(ChildrenOf(mount, children[4]));
            Assert.Equal(0, Exports.DispatchEventCore(
                (ulong)ClickAttachOn(mount, back).HandlerId, ClickArgs));

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

    /// <summary>The demo's numbers hold together (the arithmetic-pin
    /// discipline): the echo derives from the initial const, and the two
    /// echo literals Gates 2/3 transcribe are the function's own output.</summary>
    [Fact]
    public void TheDemosNumbers_AreTheContractsArithmetic()
    {
        Assert.Equal("sw:false", BnModalDemo.Echo(BnModalDemo.InitialSwitched));
        Assert.Equal("sw:true", BnModalDemo.Echo(true));
        Assert.Equal(280, BnModalDemo.ContentWidthDp);  // the design's declared box,
        Assert.Equal(180, BnModalDemo.ContentHeightDp); // pinned against the doc
    }
}
