using BlazorNative.Components;
using BlazorNative.Core;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using static BlazorNative.Runtime.Tests.GoldenAssertions;
using BlazorNative.SampleApp;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// BnFormDemoTests — Phase 7.3 Task 1.3 (design §"The proof surface").
//
// THE SOURCE OF TRUTH FOR GATES 2 AND 3. This golden pins what .NET puts on
// the wire for "/form": the creates (TWO each of the new NodeTypes 8/9/10 and
// two pickers), the full prop tables (values, min/max/step, THE ITEMS JSON
// LITERAL, the enabled flags), the attach count, and the four bind
// round-trips through the PRODUCTION dispatch ingress re-rendering the echo.
// The shells' device assertions are DERIVED from it — same numbers, same
// items literal, same echo strings.
//
// THE ATTACH COUNT IS NINE, and the arithmetic is a recorded decision
// (BnFormControlTests' header): ALL EIGHT controls attach `change` —
// disabled included, BnInput's unconditional-onchange precedent — plus the
// back click. "Disabled controls dispatch nothing" is the DEVICE's half of
// that decision (the widget is disabled host-side), asserted in Gates 2/3.
//
// Sizes: the sliders/pickers carry the DECLARED 240 width (asserted
// cross-platform); the checkboxes/switches carry NO styles — their intrinsic
// sizes are the platforms' own, asserted per-platform (the 6.3 oracle
// method). See BnFormDemo.razor's header.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class BnFormDemoTests
{
    private const string ClickArgs = /*lang=json*/ """{"name":"click"}""";

    /// <summary>THE items wire literal — BnFormDemo.PickerItems through the
    /// BnItemsJson grammar. Gates 2/3 transcribe exactly this string into
    /// their parser assertions.</summary>
    private const string ItemsJson = /*lang=json*/ """["Alpha","Bravo","Charlie"]""";

    private static (RenderFrame Mount, List<RenderFrame> Frames) MountFormDemo()
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
        Assert.Equal(0, HostSession.TryMount("BnFormDemo"));
        Assert.NotEmpty(frames);
        return (frames[0], frames);
    }

    private static void TearDown()
    {
        HostSession.ResetForTests();
        NativeShellBridge.ResetForTests();
    }

    /// <summary>Every prop a node carries in this frame — the prop-wire twin
    /// of GoldenAssertions.StylesOf.</summary>
    private static Dictionary<string, string?> PropsOf(RenderFrame frame, int nodeId)
        => frame.Patches.OfType<UpdatePropPatch>()
            .Where(p => p.NodeId == nodeId)
            .ToDictionary(p => p.Name, p => p.Value);

    /// <summary>Asserts a node's WHOLE prop table — nothing missing, nothing
    /// extra (the AssertNode discipline, on the prop wire).</summary>
    private static void AssertProps(
        RenderFrame frame, int nodeId, string what,
        params (string Name, string Value)[] expected)
    {
        Dictionary<string, string?> actual = PropsOf(frame, nodeId);
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

    private static string ChangeArgs(string payload)
        => "{\"name\":\"change\",\"payload\":\"" + payload + "\"}";

    /// <summary>The change attach on a node (each control has exactly one).</summary>
    private static AttachEventPatch ChangeAttachOn(RenderFrame frame, int nodeId)
        => Assert.Single(frame.Patches.OfType<AttachEventPatch>(),
            p => p.NodeId == nodeId && p.EventName == "change");

    /// <summary>The echo's TEXT NODE id, resolved structurally: child [8] of
    /// the root is the BnText span; its single child is the text node whose
    /// ReplaceText carries the echo.</summary>
    private static int EchoTextNode(RenderFrame mount)
    {
        int span = ChildrenOf(mount, Root(mount).NodeId)[8];
        Assert.Equal("text", CreateOf(mount, span).NodeType);
        return Assert.Single(ChildrenOf(mount, span));
    }

    // ── The golden ────────────────────────────────────────────────────────────

    [Fact]
    public void Mount_Golden_ControlsPropsStylesAndAttaches()
    {
        var (mount, _) = MountFormDemo();
        try
        {
            CreateNodePatch root = Root(mount);
            AssertNode(mount, root.NodeId, "root", "view", ("flexDirection", "column"));

            List<int> children = ChildrenOf(mount, root.NodeId);
            Assert.Equal(10, children.Count);

            // [0]/[1] the checkboxes — NodeType "checkbox" (wire id 8), NO
            // styles (intrinsic size — the platform's own; the header's
            // declared-vs-intrinsic rule).
            AssertNode(mount, children[0], "bound checkbox", "checkbox");
            AssertProps(mount, children[0], "bound checkbox", ("value", "false"));
            AssertNode(mount, children[1], "disabled checkbox", "checkbox");
            AssertProps(mount, children[1], "disabled checkbox",
                ("value", "true"), ("enabled", "false"));

            // [2]/[3] the switches — NodeType "switch" (wire id 9).
            AssertNode(mount, children[2], "bound switch", "switch");
            AssertProps(mount, children[2], "bound switch", ("value", "true"));
            AssertNode(mount, children[3], "disabled switch", "switch");
            AssertProps(mount, children[3], "disabled switch",
                ("value", "true"), ("enabled", "false"));

            // [4]/[5] the sliders — NodeType "slider" (wire id 10), the
            // DECLARED 240 width, the range DECLARED on the wire. The bound
            // one is stepped, the disabled one continuous (step ABSENT).
            AssertNode(mount, children[4], "bound slider", "slider", ("width", "240"));
            AssertProps(mount, children[4], "bound slider",
                ("value", "25"), ("min", "0"), ("max", "100"), ("step", "5"));
            AssertNode(mount, children[5], "disabled slider", "slider", ("width", "240"));
            AssertProps(mount, children[5], "disabled slider",
                ("value", "50"), ("min", "0"), ("max", "100"), ("enabled", "false"));

            // [6]/[7] the pickers — NodeType "picker" (wire id 7: the 2.5
            // stub made real), the SAME items JSON literal on both.
            AssertNode(mount, children[6], "bound picker", "picker", ("width", "240"));
            AssertProps(mount, children[6], "bound picker",
                ("items", ItemsJson), ("selectedIndex", "0"));
            AssertNode(mount, children[7], "disabled picker", "picker", ("width", "240"));
            AssertProps(mount, children[7], "disabled picker",
                ("items", ItemsJson), ("selectedIndex", "1"), ("enabled", "false"));

            // [8] the echo — its initial text is the Echo function over the
            // initial consts (derived, never transcribed).
            int echoNode = EchoTextNode(mount);
            ReplaceTextPatch echo = Assert.Single(
                mount.Patches.OfType<ReplaceTextPatch>(), p => p.NodeId == echoNode);
            Assert.Equal(
                BnFormDemo.Echo(BnFormDemo.InitialChecked, BnFormDemo.InitialSwitched,
                    BnFormDemo.InitialSliderValue, BnFormDemo.InitialSelectedIndex),
                echo.Text);
            Assert.Equal("cb:false sw:true sl:25 pk:0", echo.Text); // the literal, for Gates 2/3

            // [9] the back row — nav parity with every other page.
            AssertNode(mount, children[9], "back row", "view",
                ("flexDirection", "row"), ("width", "300"));
            int back = Assert.Single(ChildrenOf(mount, children[9]));
            Assert.Equal("button", CreateOf(mount, back).NodeType);
            Assert.Empty(StylesOf(mount, back)); // measured, never declared

            // ── The counted wire ─────────────────────────────────────────────
            // Creates, counted: 1 root + 8 controls + 2 echo (span + its
            // text node) + 1 back row + 2 button (button + its label text)
            // = 14. TWO of each new NodeType — the golden that makes a
            // missed Kotlin/Swift nodeTypes entry unmissable in Gates 2/3
            // (a "?" create fails their decode).
            Assert.Equal(14, mount.Patches.OfType<CreateNodePatch>().Count());
            Assert.Equal(2, mount.Patches.OfType<CreateNodePatch>().Count(p => p.NodeType == "checkbox"));
            Assert.Equal(2, mount.Patches.OfType<CreateNodePatch>().Count(p => p.NodeType == "switch"));
            Assert.Equal(2, mount.Patches.OfType<CreateNodePatch>().Count(p => p.NodeType == "slider"));
            Assert.Equal(2, mount.Patches.OfType<CreateNodePatch>().Count(p => p.NodeType == "picker"));

            // Attaches: NINE — all eight controls' change (disabled included:
            // the recorded decision) + the back click, and nothing else.
            Assert.Equal(8, mount.Patches.OfType<AttachEventPatch>().Count(p => p.EventName == "change"));
            Assert.Equal(1, mount.Patches.OfType<AttachEventPatch>().Count(p => p.EventName == "click"));
            Assert.Equal(9, mount.Patches.OfType<AttachEventPatch>().Count());

            // Props: 19 (cb 1+2, sw 1+2, sl 4+4, pk 2+3) — a 20th means a
            // prop leaked (or a style fell off the SetStyle wire onto this
            // one, the 6.2-era guard restated as a count).
            Assert.Equal(19, mount.Patches.OfType<UpdatePropPatch>().Count());

            // Styles: 7 (root 1, sliders 1+1, pickers 1+1, back row 2). The
            // checkbox/switch quartet carries ZERO styles — intrinsic size is
            // the platform's own, and a width appearing on one of them here
            // would silently turn a per-platform oracle assertion into a
            // wrong cross-platform one.
            Assert.Equal(7, mount.Patches.OfType<SetStylePatch>().Count());
        }
        finally
        {
            TearDown();
        }
    }

    /// <summary>THE MULTI-CONTROL BIND PROOF, through the production ingress:
    /// each bound control's change dispatch round-trips into the page state
    /// and re-renders the echo — and writes the control's own value prop back
    /// (the host applies it under its applyingBatch guard: no echo dispatch,
    /// Gates 2/3's half of the loop).</summary>
    [Fact]
    public void BoundControls_ChangeDispatches_RoundTripIntoTheEcho()
    {
        var (mount, frames) = MountFormDemo();
        try
        {
            List<int> children = ChildrenOf(mount, Root(mount).NodeId);
            int echoNode = EchoTextNode(mount);

            string EchoAfter() => Assert.Single(
                frames[^1].Patches.OfType<ReplaceTextPatch>(), p => p.NodeId == echoNode).Text;

            // Toggle the bound checkbox.
            Assert.Equal(0, Exports.DispatchEventCore(
                (ulong)ChangeAttachOn(mount, children[0]).HandlerId, ChangeArgs("true")));
            Assert.Equal("cb:true sw:true sl:25 pk:0", EchoAfter());
            Assert.Equal("true", Assert.Single(frames[^1].Patches.OfType<UpdatePropPatch>(),
                p => p.NodeId == children[0] && p.Name == "value").Value);

            // Flip the bound switch off.
            Assert.Equal(0, Exports.DispatchEventCore(
                (ulong)ChangeAttachOn(mount, children[2]).HandlerId, ChangeArgs("false")));
            Assert.Equal("cb:true sw:false sl:25 pk:0", EchoAfter());

            // Slide the bound slider to a FRACTIONAL value (the invariant
            // echo: "62.5" on every locale).
            Assert.Equal(0, Exports.DispatchEventCore(
                (ulong)ChangeAttachOn(mount, children[4]).HandlerId, ChangeArgs("62.5")));
            Assert.Equal("cb:true sw:false sl:62.5 pk:0", EchoAfter());

            // Pick the last item.
            Assert.Equal(0, Exports.DispatchEventCore(
                (ulong)ChangeAttachOn(mount, children[6]).HandlerId, ChangeArgs("2")));
            Assert.Equal("cb:true sw:false sl:62.5 pk:2", EchoAfter());
        }
        finally
        {
            TearDown();
        }
    }

    /// <summary>The DISABLED instances' handlers still exist on the wire (the
    /// recorded attach decision) and still round-trip if driven — which is
    /// exactly why "disabled controls dispatch nothing" MUST be enforced by
    /// the disabled host widget (Gates 2/3's device assertion), not assumed
    /// from the wire shape. The .NET golden's job is the wire; the disabled
    /// checkbox's callback is unbound, so nothing moves — pinned by the echo
    /// staying put.</summary>
    [Fact]
    public void DisabledControls_HaveNoBoundState_TheEchoNeverMoves()
    {
        var (mount, frames) = MountFormDemo();
        try
        {
            List<int> children = ChildrenOf(mount, Root(mount).NodeId);
            int echoNode = EchoTextNode(mount);
            int framesBefore = frames.Count;

            // Drive the DISABLED checkbox's handler through the ingress (a
            // thing only a test can do — a real disabled widget never fires).
            Assert.Equal(0, Exports.DispatchEventCore(
                (ulong)ChangeAttachOn(mount, children[1]).HandlerId, ChangeArgs("false")));

            // Its CheckedChanged is unbound: no page state moved, so the echo
            // text and the control's value prop are untouched in every frame
            // the dispatch produced (the control's own no-op re-render may
            // commit an empty frame — that is Blazor's HandleEventAsync, not
            // state).
            IEnumerable<RenderPatch> after =
                frames.Skip(framesBefore).SelectMany(f => f.Patches);
            Assert.DoesNotContain(after.OfType<ReplaceTextPatch>(), p => p.NodeId == echoNode);
            Assert.DoesNotContain(after.OfType<UpdatePropPatch>(), p => p.NodeId == children[1]);
        }
        finally
        {
            TearDown();
        }
    }

    /// <summary>The page is reachable BY ROUTE ("/form") and its back button
    /// leaves by the same nav path every page uses.</summary>
    [Fact]
    public void BackButton_NavigatesToTheDemoRoot()
    {
        var (mount, frames) = MountFormDemo();
        try
        {
            INavigationManager nav =
                Assert.IsAssignableFrom<INavigationManager>(HostSession.CurrentNavigationManager);
            Assert.Equal("/form", nav.CurrentRoute);

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

    /// <summary>The demo's numbers hold together (the BnScrollDemo
    /// arithmetic-pin discipline): the items literal IS the demo's list
    /// through the grammar, the disabled picker's fixed index is in range,
    /// and the initial echo derives from the initial consts.</summary>
    [Fact]
    public void TheDemosNumbers_AreTheContractsArithmetic()
    {
        Assert.Equal(ItemsJson, BnItemsJson.Write(BnFormDemo.PickerItems));
        Assert.InRange(BnFormDemo.DisabledPickerIndex, 0, BnFormDemo.PickerItems.Count - 1);
        Assert.InRange(BnFormDemo.InitialSelectedIndex, 0, BnFormDemo.PickerItems.Count - 1);
        Assert.InRange(BnFormDemo.InitialSliderValue, BnFormDemo.SliderMin, BnFormDemo.SliderMax);
        Assert.InRange(BnFormDemo.DisabledSliderValue, BnFormDemo.SliderMin, BnFormDemo.SliderMax);
        Assert.Equal("cb:false sw:true sl:25 pk:0",
            BnFormDemo.Echo(BnFormDemo.InitialChecked, BnFormDemo.InitialSwitched,
                BnFormDemo.InitialSliderValue, BnFormDemo.InitialSelectedIndex));
    }
}
