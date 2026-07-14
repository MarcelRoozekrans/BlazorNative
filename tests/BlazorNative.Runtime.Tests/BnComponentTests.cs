using BlazorNative.Components;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using static BlazorNative.Runtime.Tests.GoldenAssertions;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// BnComponentTests — Phase 3.4 Task 3 (design §2, DoD #4): per-component
// mount shapes for the public Bn* quartet, asserted at the patch level the
// hosts decode. Same session harness as CompositionProbeTests (HostSession +
// Frames capture + Exports.DispatchEventCore); components mount directly via
// Mount<T>(ParameterView) — they are library components, not registry
// entries.
//
// Emission buckets pinned by NativeRenderer.ProcessAttribute:
//   on* → AttachEventPatch; backgroundColor/padding/fontSize → SetStylePatch;
//   value/placeholder/enabled → UpdatePropPatch.
// Element → NodeType (MapElementToNodeType): div→view, span→text,
// button→button, input→input.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class BnComponentTests : IDisposable
{
    /// <summary>TEARDOWN — 6.2 Gate 1 review. Every test here mutates the SHARED
    /// static session (HostSession.EnsureSession) and none of them used to hand it
    /// back: the class relied on whichever class ran NEXT calling ResetForTests
    /// first. Under the full suite that happens to hold; run this class as a
    /// FILTERED SUBSET and the live renderer it leaves behind is the next golden's
    /// problem — a suite that only passes in one order is a trap for every future
    /// phase. xUnit calls this after each test (the class is the fixture), which is
    /// the same posture BnLayoutDemoTests/BnScrollDemoTests get from their
    /// try/finally TearDown.</summary>
    public void Dispose()
    {
        HostSession.ResetForTests();
        NativeShellBridge.ResetForTests();
    }

    private static (NativeRenderer Renderer, List<RenderFrame> Frames) CreateCapturingSession()
    {
        HostSession.ResetForTests();
        NativeRenderer renderer = HostSession.EnsureSession();
        var frames = new List<RenderFrame>();
        renderer.Frames += (f, _) =>
        {
            frames.Add(f);
            return ValueTask.CompletedTask;
        };
        return (renderer, frames);
    }

    // CreateOf / StylesOf come from GoldenAssertions (`using static`) — they were
    // a third partial copy of the goldens' helpers (6.2 Gate 1 review).

    private static SetStylePatch StyleOn(RenderFrame frame, int nodeId, string prop)
        => Assert.Single(frame.Patches.OfType<SetStylePatch>(),
            p => p.NodeId == nodeId && p.Property == prop);

    private static UpdatePropPatch PropOn(RenderFrame frame, int nodeId, string prop)
        => Assert.Single(frame.Patches.OfType<UpdatePropPatch>(),
            p => p.NodeId == nodeId && p.Name == prop);

    // ── BnView ────────────────────────────────────────────────────────────────

    [Fact]
    public void BnView_Mount_EmitsViewWithStylesAndParentedChildren()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<BnView>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(BnView.BackgroundColor)] = "#112233",
            [nameof(BnView.Padding)] = "12",
            [nameof(BnView.ChildContent)] = (RenderFragment)(b =>
            {
                b.OpenElement(0, "span");
                b.AddContent(1, "child");
                b.CloseElement();
            }),
        }));
        Assert.NotEmpty(frames);
        var mount = frames[0];

        // The div → "view" root with both style attrs.
        var root = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);
        Assert.Equal("view", root.NodeType);
        Assert.Equal("#112233", StyleOn(mount, root.NodeId, "backgroundColor").Value);
        Assert.Equal("12", StyleOn(mount, root.NodeId, "padding").Value);

        // ChildContent (a Region — the Gate 1 walk) parents under the div.
        var text = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(), p => p.Text == "child");
        var span = CreateOf(mount, Assert.IsType<int>(CreateOf(mount, text.NodeId).ParentId));
        Assert.Equal("text", span.NodeType);
        Assert.Equal(root.NodeId, span.ParentId);
    }

    // ── BnText ────────────────────────────────────────────────────────────────

    [Fact]
    public void BnText_Mount_EmitsSpanWithFontSizeAndText()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<BnText>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(BnText.Text)] = "hello-text",
            [nameof(BnText.FontSize)] = "18",
        }));
        Assert.NotEmpty(frames);
        var mount = frames[0];

        var root = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);
        Assert.Equal("text", root.NodeType); // span
        Assert.Equal("18", StyleOn(mount, root.NodeId, "fontSize").Value);

        var text = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(), p => p.Text == "hello-text");
        Assert.Equal(root.NodeId, CreateOf(mount, text.NodeId).ParentId);
    }

    // ── BnButton ──────────────────────────────────────────────────────────────

    [Fact]
    public void BnButton_Mount_AttachesClickAndRendersLabel()
    {
        var (renderer, frames) = CreateCapturingSession();
        var clicked = false;

        renderer.Mount<BnButton>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(BnButton.Label)] = "Tap",
            [nameof(BnButton.OnClick)] = EventCallback.Factory.Create<MouseEventArgs>(
                new object(), () => clicked = true),
        }));
        Assert.NotEmpty(frames);
        var mount = frames[0];

        var root = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);
        Assert.Equal("button", root.NodeType);

        var label = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(), p => p.Text == "Tap");
        Assert.Equal(root.NodeId, CreateOf(mount, label.NodeId).ParentId);

        // Enabled defaults true → NO enabled prop emitted.
        Assert.DoesNotContain(mount.Patches.OfType<UpdatePropPatch>(), p => p.Name == "enabled");

        // The click attach round-trips to OnClick through the dispatch core.
        var attach = Assert.Single(mount.Patches.OfType<AttachEventPatch>(),
            p => p.NodeId == root.NodeId && p.EventName == "click");
        Assert.Equal(0, Exports.DispatchEventCore(
            (ulong)attach.HandlerId, /*lang=json*/ """{"name":"click"}"""));
        Assert.True(clicked, "OnClick was not invoked by the click dispatch");
    }

    [Fact]
    public void BnButton_Disabled_EmitsEnabledFalseProp()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<BnButton>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(BnButton.Label)] = "Nope",
            [nameof(BnButton.Enabled)] = false,
        }));
        Assert.NotEmpty(frames);
        var mount = frames[0];

        var root = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);
        Assert.Equal("false", PropOn(mount, root.NodeId, "enabled").Value);
    }

    /// <summary>Host for the re-enable transition: starts disabled; the
    /// button's own click flips it enabled (EventCallback receiver semantics
    /// re-render this host with the new parameter).</summary>
    private sealed class ReEnableHost : ComponentBase
    {
        private bool _enabled;

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder b)
        {
            b.OpenComponent<BnButton>(0);
            b.AddComponentParameter(1, nameof(BnButton.Label), "Wake");
            b.AddComponentParameter(2, nameof(BnButton.Enabled), _enabled);
            b.AddComponentParameter(3, nameof(BnButton.OnClick),
                EventCallback.Factory.Create<MouseEventArgs>(this, () => _enabled = true));
            b.CloseComponent();
        }
    }

    [Fact]
    public void BnButton_ReEnabled_EmitsEnabledNullProp()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<ReEnableHost>();
        Assert.NotEmpty(frames);
        var mount = frames[0];
        var root = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);
        Assert.Equal("false", PropOn(mount, root.NodeId, "enabled").Value);
        var attach = Assert.Single(mount.Patches.OfType<AttachEventPatch>(),
            p => p.NodeId == root.NodeId && p.EventName == "click");

        // Enabled false → true: the boolean attribute LEAVES the tree —
        // RemoveAttribute → UpdatePropPatch("enabled", null), the documented
        // BnButton contract (hosts restore their default: WidgetMapper's
        // p.value?.toBoolean() ?: true).
        Assert.Equal(0, Exports.DispatchEventCore(
            (ulong)attach.HandlerId, /*lang=json*/ """{"name":"click"}"""));
        Assert.True(frames.Count >= 2, "expected a synchronous re-render frame");
        Assert.Null(PropOn(frames[^1], root.NodeId, "enabled").Value);
    }

    // ── BnInput ───────────────────────────────────────────────────────────────

    [Fact]
    public void BnInput_Mount_EmitsValuePlaceholderAndChangeAttach()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<BnInput>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(BnInput.Value)] = "seed",
            [nameof(BnInput.Placeholder)] = "Type here...",
        }));
        Assert.NotEmpty(frames);
        var mount = frames[0];

        var root = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);
        Assert.Equal("input", root.NodeType);
        Assert.Equal("seed", PropOn(mount, root.NodeId, "value").Value);
        Assert.Equal("Type here...", PropOn(mount, root.NodeId, "placeholder").Value);
        Assert.Single(mount.Patches.OfType<AttachEventPatch>(),
            p => p.NodeId == root.NodeId && p.EventName == "change");
    }

    [Fact]
    public void BnInput_ChangeDispatch_InvokesValueChangedWithPayload()
    {
        var (renderer, frames) = CreateCapturingSession();
        string? received = null;

        renderer.Mount<BnInput>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(BnInput.Value)] = "",
            [nameof(BnInput.ValueChanged)] = EventCallback.Factory.Create<string>(
                new object(), v => received = v),
        }));
        Assert.NotEmpty(frames);
        var attach = Assert.Single(frames[0].Patches.OfType<AttachEventPatch>(),
            p => p.EventName == "change");

        // The @bind mechanism half: change dispatch → ValueChanged(payload).
        Assert.Equal(0, Exports.DispatchEventCore(
            (ulong)attach.HandlerId, /*lang=json*/ """{"name":"change","payload":"typed"}"""));
        Assert.Equal("typed", received);
    }

    [Fact]
    public void BnInput_Disabled_EmitsEnabledFalseProp()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<BnInput>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(BnInput.Enabled)] = false,
        }));
        Assert.NotEmpty(frames);
        var mount = frames[0];

        var root = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);
        Assert.Equal("false", PropOn(mount, root.NodeId, "enabled").Value);
    }

    // ── BnView flex surface (Phase 6.1 Task 1.1) ──────────────────────────────
    //
    // Typed C# params → strings on the EXISTING SetStyle wire (design decision
    // 4): compile-time safety for the author, zero ABI change. The value
    // grammar the shells parse is pinned here, once, on the .NET side.

    /// <summary>The un-styled invariant (non-negotiable #4): a BnView with NO
    /// flex params must emit NO style patches at all — the whole reason the
    /// existing BnDemo/BnSettingsPage goldens do not churn when flex lands.
    /// If this ever fails, some new param stopped defaulting to null.</summary>
    [Fact]
    public void BnView_NoFlexParams_EmitsNoStylePatches()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<BnView>();
        Assert.NotEmpty(frames);

        Assert.Empty(frames[0].Patches.OfType<SetStylePatch>());
        Assert.Empty(frames[0].Patches.OfType<UpdatePropPatch>());
    }

    [Fact]
    public void BnView_FlexParams_EmitTypedValuesOnTheSetStyleWire()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<BnView>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(BnView.Direction)] = FlexDirection.Row,
            [nameof(BnView.Grow)] = 1f,
        }));
        Assert.NotEmpty(frames);
        var mount = frames[0];
        var root = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);

        Assert.Equal("row", StyleOn(mount, root.NodeId, "flexDirection").Value);
        Assert.Equal("1", StyleOn(mount, root.NodeId, "flexGrow").Value);
        // Nothing else — the unset params stay off the wire.
        Assert.Equal(2, mount.Patches.OfType<SetStylePatch>().Count());
    }

    /// <summary>EVERY BnView parameter except Direction and ChildContent, with a
    /// distinct value each — the input half of the style-surface contract. Shared
    /// with the BnRow/BnColumn forwarding tests: a preset must put THIS dictionary
    /// on the wire as the same table BnView does (plus its own direction), and the
    /// only way to know that is to feed both the same thing.</summary>
    private static Dictionary<string, object?> FullFlexParams() => new()
    {
        [nameof(BnView.BackgroundColor)] = "#112233",
        [nameof(BnView.Padding)] = "16",
        [nameof(BnView.Margin)] = "4",
        [nameof(BnView.Justify)] = FlexJustify.SpaceBetween,
        [nameof(BnView.Align)] = FlexAlign.Center,
        [nameof(BnView.AlignSelf)] = FlexAlign.FlexEnd,
        [nameof(BnView.Grow)] = 2f,
        [nameof(BnView.Shrink)] = 0f,
        [nameof(BnView.Basis)] = "auto",
        [nameof(BnView.Wrap)] = FlexWrap.WrapReverse,
        [nameof(BnView.Gap)] = "8",
        [nameof(BnView.Width)] = "300",
        [nameof(BnView.Height)] = "100",
        [nameof(BnView.MinWidth)] = "10",
        [nameof(BnView.MaxWidth)] = "50%",
        [nameof(BnView.MinHeight)] = "20",
        [nameof(BnView.MaxHeight)] = "400",
        [nameof(BnView.Position)] = FlexPosition.Absolute,
        [nameof(BnView.Top)] = "1",
        [nameof(BnView.Right)] = "2",
        [nameof(BnView.Bottom)] = "3",
        [nameof(BnView.Left)] = "4",
    };

    /// <summary>What <see cref="FullFlexParams"/> must become on the SetStyle
    /// wire — everything but flexDirection, which each caller adds (BnView from
    /// its own Direction param; the presets from their fixed direction). THE
    /// table the shells' string→Yoga mapping is written against.</summary>
    private static Dictionary<string, string> FullFlexWireTable() => new()
    {
        ["backgroundColor"] = "#112233",
        ["padding"] = "16",
        ["margin"] = "4",
        ["justifyContent"] = "space-between",
        ["alignItems"] = "center",
        ["alignSelf"] = "flex-end",
        ["flexGrow"] = "2",
        ["flexShrink"] = "0",
        ["flexBasis"] = "auto",
        ["flexWrap"] = "wrap-reverse",
        ["gap"] = "8",
        ["width"] = "300",
        ["height"] = "100",
        ["minWidth"] = "10",
        ["maxWidth"] = "50%",
        ["minHeight"] = "20",
        ["maxHeight"] = "400",
        ["position"] = "absolute",
        ["top"] = "1",
        ["right"] = "2",
        ["bottom"] = "3",
        ["left"] = "4",
    };

    /// <summary>The whole style surface, param → wire name/value. This table is
    /// the contract the shells' string→Yoga mapping is written against.</summary>
    [Fact]
    public void BnView_FullFlexSurface_EmitsEveryWireName()
    {
        var (renderer, frames) = CreateCapturingSession();

        Dictionary<string, object?> parameters = FullFlexParams();
        parameters[nameof(BnView.Direction)] = FlexDirection.ColumnReverse;
        renderer.Mount<BnView>(ParameterView.FromDictionary(parameters));
        Assert.NotEmpty(frames);
        var mount = frames[0];
        var root = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);

        Dictionary<string, string> expected = FullFlexWireTable();
        expected["flexDirection"] = "column-reverse";

        var actual = mount.Patches.OfType<SetStylePatch>()
            .Where(p => p.NodeId == root.NodeId)
            .ToDictionary(p => p.Property, p => p.Value!);
        Assert.Equal(expected, actual);
        // Every flex prop rides SetStyle — none leaked onto the prop wire.
        Assert.Empty(mount.Patches.OfType<UpdatePropPatch>());
    }

    /// <summary>Host for the null-reset round trip THROUGH BnView — the bug's
    /// real shape (<c>Grow = cond ? 1 : null</c>), not a hand-rolled
    /// <c>AddAttribute("flexGrow", null)</c>. Two links have to hold for the
    /// RemoveAttribute edit to exist at all: BnView must OMIT a null element
    /// attribute (rather than emit ""), and the renderer must route the removal
    /// onto the STYLE wire (StyleResetTests pins that half).</summary>
    private sealed class ConditionalGrowHost : ComponentBase
    {
        private bool _reset;

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder b)
        {
            b.OpenComponent<BnView>(0);
            b.AddComponentParameter(1, nameof(BnView.Grow), _reset ? null : (float?)1f);
            b.AddComponentParameter(2, nameof(BnView.ChildContent), (RenderFragment)(cb =>
            {
                cb.OpenElement(0, "button");
                cb.AddAttribute(1, "onclick",
                    EventCallback.Factory.Create<MouseEventArgs>(this, () => _reset = true));
                cb.CloseElement();
            }));
            b.CloseComponent();
        }
    }

    [Fact]
    public void BnView_GrowGoesNull_EmitsSetStyleNullOnTheStyleWire()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<ConditionalGrowHost>();
        Assert.NotEmpty(frames);
        var mount = frames[0];
        var root = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);
        Assert.Equal("1", StyleOn(mount, root.NodeId, "flexGrow").Value);

        var attach = Assert.Single(mount.Patches.OfType<AttachEventPatch>(),
            p => p.EventName == "click");
        Assert.Equal(0, Exports.DispatchEventCore(
            (ulong)attach.HandlerId, /*lang=json*/ """{"name":"click"}"""));
        Assert.True(frames.Count >= 2, "expected a synchronous re-render frame");
        var reset = frames[^1];

        // A null Grow must arrive as SetStyle(flexGrow, null) — "reset to the
        // Yoga default". The same null on the PROP wire is a prop no shell
        // routes to Yoga, so the node would keep flexGrow:1 forever.
        var style = Assert.Single(reset.Patches.OfType<SetStylePatch>());
        Assert.Equal(root.NodeId, style.NodeId);
        Assert.Equal("flexGrow", style.Property);
        Assert.Null(style.Value);
        Assert.Empty(reset.Patches.OfType<UpdatePropPatch>());
    }

    /// <summary>Numeric params stringify INVARIANTLY: a Dutch locale must not
    /// put "1,5" on the wire (the shells parse with a C/Java float parser).</summary>
    [Fact]
    public void BnView_FloatParams_StringifyInvariantlyUnderADutchLocale()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                new System.Globalization.CultureInfo("nl-NL");

            var (renderer, frames) = CreateCapturingSession();
            renderer.Mount<BnView>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(BnView.Grow)] = 1.5f,
                [nameof(BnView.Shrink)] = 0.25f,
            }));
            Assert.NotEmpty(frames);
            var mount = frames[0];
            var root = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);

            Assert.Equal("1.5", StyleOn(mount, root.NodeId, "flexGrow").Value);
            Assert.Equal("0.25", StyleOn(mount, root.NodeId, "flexShrink").Value);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>The enum → CSS-cased string mapping, exhaustively (the shells
    /// parse exactly these words).</summary>
    [Fact]
    public void FlexEnums_ToStyleValue_AreCssCased()
    {
        Assert.Equal("row", FlexDirection.Row.ToStyleValue());
        Assert.Equal("column", FlexDirection.Column.ToStyleValue());
        Assert.Equal("row-reverse", FlexDirection.RowReverse.ToStyleValue());
        Assert.Equal("column-reverse", FlexDirection.ColumnReverse.ToStyleValue());

        Assert.Equal("flex-start", FlexJustify.FlexStart.ToStyleValue());
        Assert.Equal("center", FlexJustify.Center.ToStyleValue());
        Assert.Equal("flex-end", FlexJustify.FlexEnd.ToStyleValue());
        Assert.Equal("space-between", FlexJustify.SpaceBetween.ToStyleValue());
        Assert.Equal("space-around", FlexJustify.SpaceAround.ToStyleValue());
        Assert.Equal("space-evenly", FlexJustify.SpaceEvenly.ToStyleValue());

        Assert.Equal("auto", FlexAlign.Auto.ToStyleValue());
        Assert.Equal("flex-start", FlexAlign.FlexStart.ToStyleValue());
        Assert.Equal("center", FlexAlign.Center.ToStyleValue());
        Assert.Equal("flex-end", FlexAlign.FlexEnd.ToStyleValue());
        Assert.Equal("stretch", FlexAlign.Stretch.ToStyleValue());
        Assert.Equal("baseline", FlexAlign.Baseline.ToStyleValue());

        Assert.Equal("nowrap", FlexWrap.NoWrap.ToStyleValue());
        Assert.Equal("wrap", FlexWrap.Wrap.ToStyleValue());
        Assert.Equal("wrap-reverse", FlexWrap.WrapReverse.ToStyleValue());

        Assert.Equal("relative", FlexPosition.Relative.ToStyleValue());
        Assert.Equal("absolute", FlexPosition.Absolute.ToStyleValue());
    }

    /// <summary>The public style TYPES are Flex-prefixed. They ship on nuget.org
    /// (M8) in the library's root namespace, where a bare <c>Align</c>/<c>Wrap</c>/
    /// <c>Justify</c>/<c>Position</c> collides with app-side types — free to rename
    /// now, a breaking change later. (The PARAM names on BnView stay short; this
    /// is only about the type names.)</summary>
    [Fact]
    public void FlexStyleTypes_AreFlexPrefixed_ToNotCollideWithAppTypes()
    {
        System.Reflection.Assembly components = typeof(BnView).Assembly;
        Assert.All(
            components.GetExportedTypes().Where(t => t.IsEnum),
            t => Assert.StartsWith("Flex", t.Name, StringComparison.Ordinal));
    }

    // ── BnRow / BnColumn (Phase 6.1 Task 1.3) ─────────────────────────────────
    //
    // Thin presets over BnView. A BnRow IS a row: it does not expose Direction
    // at all, so the preset cannot be silently overridden. No BnStack (design
    // decision 3 — it would be a BnColumn synonym, and two names for one thing
    // is a library smell on day one).

    [Fact]
    public void BnRow_Mount_EmitsFlexDirectionRowAndForwardsParams()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<BnRow>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(BnRow.Width)] = "300",
            [nameof(BnRow.Justify)] = FlexJustify.SpaceBetween,
            [nameof(BnRow.ChildContent)] = (RenderFragment)(b =>
            {
                b.OpenElement(0, "span");
                b.AddContent(1, "in-row");
                b.CloseElement();
            }),
        }));
        Assert.NotEmpty(frames);
        var mount = frames[0];

        var root = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);
        Assert.Equal("view", root.NodeType);
        Assert.Equal("row", StyleOn(mount, root.NodeId, "flexDirection").Value);
        Assert.Equal("300", StyleOn(mount, root.NodeId, "width").Value);
        Assert.Equal("space-between", StyleOn(mount, root.NodeId, "justifyContent").Value);

        // Children parent under the preset's own view (it IS a BnView).
        var text = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(), p => p.Text == "in-row");
        var span = CreateOf(mount, Assert.IsType<int>(CreateOf(mount, text.NodeId).ParentId));
        Assert.Equal(root.NodeId, span.ParentId);
    }

    [Fact]
    public void BnColumn_Mount_EmitsFlexDirectionColumn()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<BnColumn>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(BnColumn.Height)] = "200",
        }));
        Assert.NotEmpty(frames);
        var mount = frames[0];

        var root = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);
        Assert.Equal("column", StyleOn(mount, root.NodeId, "flexDirection").Value);
        Assert.Equal("200", StyleOn(mount, root.NodeId, "height").Value);
        // The preset emits its direction and NOTHING it wasn't asked for.
        Assert.Equal(2, mount.Patches.OfType<SetStylePatch>().Count());
    }

    /// <summary>The preset WINS by construction: neither BnRow nor BnColumn
    /// exposes a Direction parameter, so an author cannot make a row a column.
    /// Pinned reflectively — the ABSENCE of the param is the design decision.</summary>
    [Fact]
    public void BnRowAndBnColumn_DoNotExposeDirection()
    {
        Assert.Null(typeof(BnRow).GetProperty("Direction"));
        Assert.Null(typeof(BnColumn).GetProperty("Direction"));
        Assert.NotNull(typeof(BnView).GetProperty("Direction"));
    }

    // ── The presets forward the whole surface: TWO tests, and both are needed ──
    //
    // DECLARATION and FORWARDING are different claims, and a test of one is a
    // green light over the other's bug:
    //
    //   • The reflective test below pins DECLARATION — every BnView param exists
    //     on the preset. It cannot see BuildRenderTree at all: delete
    //     `AddComponentParameter(…, nameof(BnView.Grow), Grow)` from BnFlexPreset
    //     and it still passes, while <BnRow Grow="1"> silently does nothing.
    //   • The behavioural test pins FORWARDING — the preset, fed the FULL param
    //     dictionary, puts BnView's entire SetStyle table on the wire (plus its
    //     own flexDirection). That is the claim BnFlexPreset's header makes, so
    //     that is the claim under test. Deleting any one forwarding line reddens
    //     it (verified by mutation, 6.1 Gate 1 review).

    /// <summary>DECLARATION: the presets expose the WHOLE BnView surface minus
    /// Direction. A param that exists on BnView but not on the preset is a hole
    /// an author falls into — pinned, not eyeballed.</summary>
    [Theory]
    [InlineData(typeof(BnRow))]
    [InlineData(typeof(BnColumn))]
    public void Presets_ForwardEveryBnViewParameterExceptDirection(Type preset)
    {
        static IEnumerable<string> Parameters(Type t) => t.GetProperties()
            .Where(p => p.IsDefined(typeof(ParameterAttribute), inherit: true))
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal);

        Assert.Equal(
            Parameters(typeof(BnView)).Where(n => n != nameof(BnView.Direction)),
            Parameters(preset));
    }

    /// <summary>FORWARDING: mounted with the same full parameter dictionary
    /// BnView_FullFlexSurface_EmitsEveryWireName uses, a preset must emit that
    /// same wire table plus its own flexDirection — nothing dropped, nothing
    /// invented. A forwarding line missing from BnFlexPreset.BuildRenderTree
    /// fails HERE (the reflective test above cannot see it).</summary>
    [Fact]
    public void BnRow_ForwardsTheWholeFlexSurface_PlusFlexDirectionRow()
    {
        var (renderer, frames) = CreateCapturingSession();
        renderer.Mount<BnRow>(ParameterView.FromDictionary(FullFlexParams()));
        AssertPresetForwardedEverything(frames, "row");
    }

    /// <inheritdoc cref="BnRow_ForwardsTheWholeFlexSurface_PlusFlexDirectionRow"/>
    [Fact]
    public void BnColumn_ForwardsTheWholeFlexSurface_PlusFlexDirectionColumn()
    {
        var (renderer, frames) = CreateCapturingSession();
        renderer.Mount<BnColumn>(ParameterView.FromDictionary(FullFlexParams()));
        AssertPresetForwardedEverything(frames, "column");
    }

    private static void AssertPresetForwardedEverything(
        List<RenderFrame> frames, string expectedDirection)
    {
        Assert.NotEmpty(frames);
        var mount = frames[0];
        var root = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);
        Assert.Equal("view", root.NodeType);

        Dictionary<string, string> expected = FullFlexWireTable();
        expected["flexDirection"] = expectedDirection;

        Assert.Equal(
            expected.ToDictionary(e => e.Key, e => (string?)e.Value),
            StylesOf(mount, root.NodeId));
        // Every forwarded param rides SetStyle — none leaked onto the prop wire.
        Assert.Empty(mount.Patches.OfType<UpdatePropPatch>());
    }

    /// <summary>The other half of the forwarding contract: a preset forwards a
    /// NULL parameter too. BnFlexPreset's AddComponentParameter calls are
    /// deliberately unguarded — ParameterView only writes SUPPLIED parameters, so
    /// an `if (Grow is not null)` "optimisation" would leave BnView holding a
    /// stale Grow when the author writes `Grow = cond ? 1 : null`. Re-rendering
    /// the preset with Grow gone must reach the wire as SetStyle(flexGrow, null).</summary>
    private sealed class ConditionalGrowRowHost : ComponentBase
    {
        private bool _reset;

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder b)
        {
            b.OpenComponent<BnRow>(0);
            b.AddComponentParameter(1, nameof(BnRow.Grow), _reset ? null : (float?)1f);
            b.AddComponentParameter(2, nameof(BnRow.ChildContent), (RenderFragment)(cb =>
            {
                cb.OpenElement(0, "button");
                cb.AddAttribute(1, "onclick",
                    EventCallback.Factory.Create<MouseEventArgs>(this, () => _reset = true));
                cb.CloseElement();
            }));
            b.CloseComponent();
        }
    }

    [Fact]
    public void BnRow_GrowGoesNull_ForwardsTheNullThroughToTheStyleWire()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<ConditionalGrowRowHost>();
        Assert.NotEmpty(frames);
        var mount = frames[0];
        var root = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);
        Assert.Equal("1", StyleOn(mount, root.NodeId, "flexGrow").Value);

        var attach = Assert.Single(mount.Patches.OfType<AttachEventPatch>(),
            p => p.EventName == "click");
        Assert.Equal(0, Exports.DispatchEventCore(
            (ulong)attach.HandlerId, /*lang=json*/ """{"name":"click"}"""));
        Assert.True(frames.Count >= 2, "expected a synchronous re-render frame");

        var style = Assert.Single(frames[^1].Patches.OfType<SetStylePatch>());
        Assert.Equal(root.NodeId, style.NodeId);
        Assert.Equal("flexGrow", style.Property);
        Assert.Null(style.Value);
    }

    // ── BnScroll (Phase 6.2 Task 1.1) ─────────────────────────────────────────
    //
    // The `scroll` element — NodeType 6, on the wire since Phase 2.5 and stubbed
    // in both shells until 6.2. NO ABI CHANGE: MapElementToNodeType already maps
    // "scroll"/"overflow" → "scroll", so BnScroll is a pure Components-side
    // addition.
    //
    // SURFACE: BnView's ITEM parameters, and NOT its CONTAINER ones. A BnScroll
    // is a flex ITEM that scrolls its content; how the CONTENT lays out is the
    // synthetic content node's job, and that node is the shells' (6.2 decision 1,
    // Gate 1 review). So Direction, Justify, Align, Wrap, Gap and Padding are
    // absent BY CONSTRUCTION, the way BnRow's Direction is — each would land on
    // the SCROLL node, whose only Yoga child is the content node:
    //
    //   Direction  → lays the content node out across the cross axis: the page
    //                silently stops scrolling (scrolling is VERTICAL-ONLY).
    //   Gap        → spaces ONE child against nothing.
    //   Justify /  → free space is NEGATIVE on a scrolling viewport (200 − 800 =
    //   Align        −600), so Center offsets the content to y = −300 and FlexEnd
    //                to −600 — and a scroll view cannot scroll above offset 0, so
    //                the top of the content is PERMANENTLY unreachable.
    //   Padding    → insets the content node and moves every frame in the shells'
    //                parity table ("does contentSize include the padding?" — two
    //                shells would answer differently).
    //
    // An author who wants the content laid out COMPOSES: <BnScroll><BnColumn
    // Gap="8" Padding="16">…</BnColumn></BnScroll>. That is RN's
    // contentContainerStyle without a second style surface.
    //
    // Null-forwarding is not a hazard here: BnScroll emits ELEMENT attributes
    // (like BnView), and a null element attribute is simply not appended to the
    // frame array — the un-styled invariant. It is the PRESETS that forward
    // component parameters and must therefore forward nulls unconditionally.

    /// <summary>EVERY BnScroll parameter except ChildContent, with a distinct
    /// value each. Deliberately its OWN table and NOT FullFlexParams(): BnScroll's
    /// surface is a strict subset (no container-layout family), and BnView/BnRow/
    /// BnColumn are held to FullFlexParams — mutating that shared dictionary to
    /// suit BnScroll would silently weaken THEIR forwarding tests.</summary>
    private static Dictionary<string, object?> ScrollItemParams() => new()
    {
        [nameof(BnScroll.BackgroundColor)] = "#112233",
        [nameof(BnScroll.Margin)] = "4",
        [nameof(BnScroll.AlignSelf)] = FlexAlign.FlexEnd,
        [nameof(BnScroll.Grow)] = 2f,
        [nameof(BnScroll.Shrink)] = 0f,
        [nameof(BnScroll.Basis)] = "auto",
        [nameof(BnScroll.Width)] = "300",
        [nameof(BnScroll.Height)] = "100",
        [nameof(BnScroll.MinWidth)] = "10",
        [nameof(BnScroll.MaxWidth)] = "50%",
        [nameof(BnScroll.MinHeight)] = "20",
        [nameof(BnScroll.MaxHeight)] = "400",
        [nameof(BnScroll.Position)] = FlexPosition.Absolute,
        [nameof(BnScroll.Top)] = "1",
        [nameof(BnScroll.Right)] = "2",
        [nameof(BnScroll.Bottom)] = "3",
        [nameof(BnScroll.Left)] = "4",
    };

    /// <summary>What <see cref="ScrollItemParams"/> must become on the SetStyle
    /// wire — the ITEM half of FullFlexWireTable(), with the container names
    /// (flexDirection, justifyContent, alignItems, flexWrap, gap, padding)
    /// ABSENT. THE table the shells' scroll-node mapping is written against.</summary>
    private static Dictionary<string, string> ScrollItemWireTable() => new()
    {
        ["backgroundColor"] = "#112233",
        ["margin"] = "4",
        ["alignSelf"] = "flex-end",
        ["flexGrow"] = "2",
        ["flexShrink"] = "0",
        ["flexBasis"] = "auto",
        ["width"] = "300",
        ["height"] = "100",
        ["minWidth"] = "10",
        ["maxWidth"] = "50%",
        ["minHeight"] = "20",
        ["maxHeight"] = "400",
        ["position"] = "absolute",
        ["top"] = "1",
        ["right"] = "2",
        ["bottom"] = "3",
        ["left"] = "4",
    };

    /// <summary>The container-layout family: the six style names that are NOT
    /// BnScroll parameters, and that the shells must IGNORE-AND-LOG on a node of
    /// type `scroll` (6.2 design, "Container styles on a scroll node"). Stated
    /// once here, in the same order the design states it.</summary>
    private static readonly string[] ContainerLayoutParams =
    [
        nameof(BnView.Direction), nameof(BnView.Justify), nameof(BnView.Align),
        nameof(BnView.Wrap), nameof(BnView.Gap), nameof(BnView.Padding),
    ];

    [Fact]
    public void BnScroll_Mount_EmitsScrollNodeTypeAndParentsItsChildren()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<BnScroll>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(BnScroll.Width)] = "300",
            [nameof(BnScroll.Height)] = "200",
            [nameof(BnScroll.ChildContent)] = (RenderFragment)(b =>
            {
                b.OpenElement(0, "span");
                b.AddContent(1, "scrolled");
                b.CloseElement();
            }),
        }));
        Assert.NotEmpty(frames);
        var mount = frames[0];

        var root = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);
        Assert.Equal("scroll", root.NodeType);
        Assert.Equal("300", StyleOn(mount, root.NodeId, "width").Value);
        Assert.Equal("200", StyleOn(mount, root.NodeId, "height").Value);

        // Children parent under the SCROLL node ON THE WIRE. The content node the
        // shells interpose (scroll → content → children in the view/Yoga trees) is
        // SYNTHETIC: it is created shell-side and never appears in a patch.
        var text = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(), p => p.Text == "scrolled");
        var span = CreateOf(mount, Assert.IsType<int>(CreateOf(mount, text.NodeId).ParentId));
        Assert.Equal("text", span.NodeType);
        Assert.Equal(root.NodeId, span.ParentId);

        // …AND THE PLACEMENT IS PINNED, not assumed: InsertIndex -1 = APPEND.
        // The other half of non-negotiable #2 — a scroll node's wire child at
        // index i is the CONTENT node's child at index i, in both trees — so a
        // shell must apply this -1 by appending to the CONTENT node's children,
        // never to the scroll node's (whose only child is the content node
        // itself: appending there would give the ScrollView/UIScrollView a second
        // child and Yoga a sibling of the content node). BnScrollDemoTests owns
        // the many-children half of the placement contract; this owns the claim
        // that the number reaches the wire at all.
        Assert.Equal(-1, span.InsertIndex);
    }

    /// <summary>THE RAW-ELEMENT HATCH — the reason the shells' ignore-and-warn
    /// rule (6.2 decision 2) has to exist at all, pinned so it cannot quietly stop
    /// being true. Closing the container-layout family on BnScroll does NOT close
    /// it on the WIRE: YogaStyleAttributes is a global name-keyed allow-list and
    /// `scroll` is a mappable element, so a hand-written element still puts
    /// SetStyle(flexDirection=row) on a scroll node — which would kill scrolling.
    /// .NET deliberately does NOT filter here (the wire says exactly what the
    /// author said); the SHELLS ignore-and-log these six names on a `scroll` node.
    /// If this test ever goes red because the style stopped reaching the wire,
    /// the shells' rule is dead code — delete it deliberately, not by accident.</summary>
    [Fact]
    public void RawScrollElement_StillPutsContainerStylesOnTheWire_WhichIsWhyTheShellsIgnoreThem()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<RawScrollHost>();
        Assert.NotEmpty(frames);
        var mount = frames[0];
        var root = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);
        Assert.Equal("scroll", root.NodeType);

        Assert.Equal(
            new Dictionary<string, string?>
            {
                ["flexDirection"] = "row",
                ["justifyContent"] = "center",
                ["alignItems"] = "center",
                ["flexWrap"] = "wrap",
                ["gap"] = "8",
                ["padding"] = "16",
            },
            StylesOf(mount, root.NodeId));
    }

    /// <summary>A hand-written `scroll` element carrying the whole container-layout
    /// family — the surface BnScroll refuses to expose.</summary>
    private sealed class RawScrollHost : ComponentBase
    {
        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder b)
        {
            b.OpenElement(0, "scroll");
            b.AddAttribute(1, "flexDirection", "row");
            b.AddAttribute(2, "justifyContent", "center");
            b.AddAttribute(3, "alignItems", "center");
            b.AddAttribute(4, "flexWrap", "wrap");
            b.AddAttribute(5, "gap", "8");
            b.AddAttribute(6, "padding", "16");
            b.CloseElement();
        }
    }

    /// <summary>The un-styled invariant, on the new element too: no flex param
    /// supplied → no attribute → NO SetStyle patch. (A scroll node with no height
    /// sizes to its content and never scrolls — that is the shells' definite-height
    /// warning, Gates 2/3 — but it must not be papered over with a default HERE:
    /// the wire says exactly what the author said.)</summary>
    [Fact]
    public void BnScroll_Unstyled_EmitsNoStyleAtAll()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<BnScroll>();
        Assert.NotEmpty(frames);
        var mount = frames[0];

        var root = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);
        Assert.Equal("scroll", root.NodeType);
        Assert.Empty(mount.Patches.OfType<SetStylePatch>());
        Assert.Empty(mount.Patches.OfType<UpdatePropPatch>());
    }

    /// <summary>DECLARATION (the BnRow/BnColumn pair, applied to BnScroll): the
    /// BnView surface MINUS the container-layout family — Direction, Justify,
    /// Align, Wrap, Gap, Padding. A BnView ITEM param missing from BnScroll is a
    /// hole an author falls into; a CONTAINER param present on BnScroll is a
    /// silent, baffling layout bug an author can write (see the block comment
    /// above). The exclusion set is the DESIGN, pinned in both directions: this
    /// fails if a container param appears AND if an item param disappears.
    /// <para>This test is a green light over a forwarding bug — it cannot see
    /// BuildRenderTree at all. The behavioural one below is what bites (6.1's Gate
    /// 1 mutation lesson: two deleted forwarding lines, suite still green).</para></summary>
    [Fact]
    public void BnScroll_ExposesEveryBnViewItemParameter_AndNoContainerLayoutParameter()
    {
        static IEnumerable<string> Parameters(Type t) => t.GetProperties()
            .Where(p => p.IsDefined(typeof(ParameterAttribute), inherit: true))
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal);

        Assert.Equal(
            Parameters(typeof(BnView)).Where(n => !ContainerLayoutParams.Contains(n)),
            Parameters(typeof(BnScroll)));

        // Said again, bluntly, because the set-difference above reads as arithmetic
        // and THIS is the decision: none of the six is settable on a BnScroll.
        Assert.All(ContainerLayoutParams, p => Assert.Null(typeof(BnScroll).GetProperty(p)));
        // …and every one of them IS settable on a BnView — so the list above is a
        // real exclusion, not six misspelled names that exclude nothing.
        Assert.All(ContainerLayoutParams, p => Assert.NotNull(typeof(BnView).GetProperty(p)));
    }

    /// <summary>FORWARDING: fed its whole parameter surface, BnScroll must put the
    /// ITEM style table on the wire — and NOT ONE container name (no flexDirection,
    /// justifyContent, alignItems, flexWrap, gap or padding: it has no parameter
    /// that could produce one). Delete any one AddAttribute from BnScroll and this
    /// goes red (the reflective test above stays green while
    /// <c>&lt;BnScroll Grow="1"&gt;</c> silently does nothing).</summary>
    [Fact]
    public void BnScroll_ForwardsTheWholeItemSurface_AndNoContainerStyle()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<BnScroll>(ParameterView.FromDictionary(ScrollItemParams()));
        Assert.NotEmpty(frames);
        var mount = frames[0];
        var root = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);
        Assert.Equal("scroll", root.NodeType);

        // The WHOLE table, exactly — so a container style appearing here (from a
        // re-added parameter, or a copy-paste of BnView's BuildRenderTree) fails
        // as an unexpected key, not as a silent extra patch.
        Assert.Equal(
            ScrollItemWireTable().ToDictionary(e => e.Key, e => (string?)e.Value),
            StylesOf(mount, root.NodeId));
        // Every flex prop rides SetStyle — none leaked onto the prop wire.
        Assert.Empty(mount.Patches.OfType<UpdatePropPatch>());
    }

    // ── BnImage (Phase 6.3 Task 1.1) ──────────────────────────────────────────
    //
    // The `img` element — MapElementToNodeType has mapped it to NodeType 5
    // ("image") since Phase 2.5. NO ABI CHANGE: BnImage is a pure Components-side
    // addition, exactly as BnScroll was.
    //
    // ── `Src` IS A PROP, NOT A STYLE ──────────────────────────────────────────
    // It rides the EXISTING UpdateProp wire (kind 5), and it must NOT be added to
    // NativeRenderer's YogaStyleAttributes/VisualStyleAttributes partition — a
    // URL is neither a Yoga setter nor a paint attribute, and putting it on the
    // style wire would force both hand-written shell parsers to grow an arm that
    // routes to NEITHER destination the partition promises (6.3 non-negotiable #1;
    // the partition itself is pinned in Renderer.Tests/StyleAttributePartitionTests,
    // which owns the `src`-is-not-a-style half). The tests below assert the patch
    // KIND, not merely the value — a `src` that silently became a SetStyle would
    // still carry the right string.
    //
    // ── SURFACE: BnView's ITEM parameters, and NOT its CONTAINER ones ─────────
    // A BnImage is a LEAF: it has no children, so there is nothing for the
    // container-layout family (Direction, Justify, Align, Wrap, Gap, Padding) to
    // arrange. Same exclusion as BnScroll's — for a different reason, which is why
    // it is said out loud rather than inherited: BnScroll excludes them because
    // its only Yoga child is the shells' synthetic content node; BnImage excludes
    // them because it has NO Yoga children at all. And on top of BnScroll's
    // exclusion, BnImage drops ChildContent too (a leaf has no content), and adds
    // Src.
    //
    // NOT here, on purpose (6.3 design decision 3): Placeholder, OnError,
    // ContentMode. Each CHANGES MEASUREMENT — does a placeholder measure like the
    // image? does a failure keep the reserved space? does `cover` report the
    // natural size or the box? — so each deserves its own design rather than a
    // footnote in the fetch phase. Ledgered for M7.

    private const string ImageSrc = "http://127.0.0.1:8099/a.png";

    /// <summary>EVERY BnImage parameter, with a distinct value each. Its OWN table
    /// (BnScroll's rationale): BnImage's surface is BnScroll's minus ChildContent
    /// plus Src, and mutating a shared dictionary to suit it would silently weaken
    /// the tests BnView/BnRow/BnColumn/BnScroll are held to.</summary>
    private static Dictionary<string, object?> ImageParams() => new()
    {
        [nameof(BnImage.Src)] = ImageSrc,
        [nameof(BnImage.BackgroundColor)] = "#112233",
        [nameof(BnImage.Margin)] = "4",
        [nameof(BnImage.AlignSelf)] = FlexAlign.FlexEnd,
        [nameof(BnImage.Grow)] = 2f,
        [nameof(BnImage.Shrink)] = 0f,
        [nameof(BnImage.Basis)] = "auto",
        [nameof(BnImage.Width)] = "300",
        [nameof(BnImage.Height)] = "100",
        [nameof(BnImage.MinWidth)] = "10",
        [nameof(BnImage.MaxWidth)] = "50%",
        [nameof(BnImage.MinHeight)] = "20",
        [nameof(BnImage.MaxHeight)] = "400",
        [nameof(BnImage.Position)] = FlexPosition.Absolute,
        [nameof(BnImage.Top)] = "1",
        [nameof(BnImage.Right)] = "2",
        [nameof(BnImage.Bottom)] = "3",
        [nameof(BnImage.Left)] = "4",
    };

    /// <summary>What <see cref="ImageParams"/> must become on the SETSTYLE wire —
    /// the ITEM half, identical to BnScroll's (the two surfaces differ only in
    /// ChildContent and Src, neither of which is a style). `src` is DELIBERATELY
    /// ABSENT: it is a prop, and the forwarding test asserts the style table is
    /// EXACTLY this, so a `src` that drifted onto the style wire fails here as an
    /// unexpected key.</summary>
    private static Dictionary<string, string> ImageItemWireTable() => new()
    {
        ["backgroundColor"] = "#112233",
        ["margin"] = "4",
        ["alignSelf"] = "flex-end",
        ["flexGrow"] = "2",
        ["flexShrink"] = "0",
        ["flexBasis"] = "auto",
        ["width"] = "300",
        ["height"] = "100",
        ["minWidth"] = "10",
        ["maxWidth"] = "50%",
        ["minHeight"] = "20",
        ["maxHeight"] = "400",
        ["position"] = "absolute",
        ["top"] = "1",
        ["right"] = "2",
        ["bottom"] = "3",
        ["left"] = "4",
    };

    /// <summary>The element→NodeType half of "no ABI change": BnImage emits `img`,
    /// and MapElementToNodeType has mapped `img` → "image" (NodeType 5) since Phase
    /// 2.5. Nothing new reaches the wire — the last stubbed leaf simply gains a
    /// producer.
    /// <para>And <c>Src</c> rides the <b>UpdateProp</b> wire. The assertion is on
    /// the patch KIND: a `src` that drifted onto the style wire would still carry
    /// the right URL, so a value-only test would not see it.</para></summary>
    [Fact]
    public void BnImage_Mount_EmitsImageNodeType_AndSrcRidesTheUpdatePropWire()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<BnImage>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(BnImage.Src)] = ImageSrc,
        }));

        Assert.NotEmpty(frames);
        RenderFrame mount = frames[0];
        CreateNodePatch root = Root(mount);
        Assert.Equal("image", root.NodeType);

        // THE KIND. `src` is an UpdateProp…
        UpdatePropPatch src = Assert.Single(mount.Patches.OfType<UpdatePropPatch>());
        Assert.Equal(root.NodeId, src.NodeId);
        Assert.Equal("src", src.Name);
        Assert.Equal(ImageSrc, src.Value);
        // …and NOT a SetStyle. With Src as the ONLY parameter supplied, the style
        // wire must be completely silent: an image with a source and no declared
        // size carries no style at all, which is exactly the state whose measured
        // size is 0×0 until the bytes land (the parity contract's first row).
        Assert.Empty(mount.Patches.OfType<SetStylePatch>());

        // A LEAF. No ChildContent parameter exists, so nothing can parent under it.
        Assert.Single(mount.Patches.OfType<CreateNodePatch>());
    }

    /// <summary>The un-styled invariant, on the image element too: no parameter
    /// supplied → no attribute → NO patch of any kind. Not even a `src`: a null Src
    /// is an image with no source, and the wire says exactly what the author said
    /// (the shells then leave the node measuring 0×0 — nothing to cancel, nothing
    /// to fetch).</summary>
    [Fact]
    public void BnImage_Unstyled_EmitsNoStyleAndNoProp()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<BnImage>();
        Assert.NotEmpty(frames);
        RenderFrame mount = frames[0];

        Assert.Equal("image", Root(mount).NodeType);
        Assert.Empty(mount.Patches.OfType<SetStylePatch>());
        Assert.Empty(mount.Patches.OfType<UpdatePropPatch>());
    }

    /// <summary>DECLARATION (the BnScroll pair, applied to BnImage): BnView's
    /// surface MINUS the container-layout family MINUS ChildContent, PLUS Src. An
    /// item param missing from BnImage is a hole an author falls into; a container
    /// param present on a LEAF is a parameter that can only ever do nothing.
    /// <para>This test is a green light over a forwarding bug — it cannot see
    /// BuildRenderTree at all. The behavioural one below is what bites (6.1's Gate 1
    /// mutation lesson: two deleted forwarding lines, suite still green).</para></summary>
    [Fact]
    public void BnImage_ExposesEveryBnViewItemParameter_PlusSrc_AndIsALeaf()
    {
        static IEnumerable<string> Parameters(Type t) => t.GetProperties()
            .Where(p => p.IsDefined(typeof(ParameterAttribute), inherit: true))
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal);

        IEnumerable<string> expected = Parameters(typeof(BnView))
            .Where(n => !ContainerLayoutParams.Contains(n))
            .Where(n => n != nameof(BnView.ChildContent))
            .Append(nameof(BnImage.Src))
            .OrderBy(n => n, StringComparer.Ordinal);

        Assert.Equal(expected, Parameters(typeof(BnImage)));

        // Said again, bluntly, because the set arithmetic above reads as arithmetic
        // and THESE are the decisions.
        // (a) A leaf: no children, so no container-layout family…
        Assert.All(ContainerLayoutParams, p => Assert.Null(typeof(BnImage).GetProperty(p)));
        Assert.All(ContainerLayoutParams, p => Assert.NotNull(typeof(BnView).GetProperty(p)));
        // (b) …and no ChildContent at all — the one place BnImage's surface is
        //     narrower than BnScroll's.
        Assert.Null(typeof(BnImage).GetProperty(nameof(BnView.ChildContent)));
        Assert.NotNull(typeof(BnScroll).GetProperty(nameof(BnView.ChildContent)));
        // (c) 6.3 design decision 3: NOT Placeholder, NOT OnError, NOT ContentMode.
        //     Each changes MEASUREMENT and gets its own design (M7). A parameter
        //     that cannot be set is a bug that cannot be written.
        Assert.Null(typeof(BnImage).GetProperty("Placeholder"));
        Assert.Null(typeof(BnImage).GetProperty("OnError"));
        Assert.Null(typeof(BnImage).GetProperty("ContentMode"));
    }

    /// <summary>FORWARDING: fed its whole parameter surface, BnImage must put the
    /// ITEM style table on the SetStyle wire and `src` on the PROP wire — and NOT
    /// ONE container name. Delete any one AddAttribute from BnImage and this goes
    /// red (the reflective test above stays green while
    /// <c>&lt;BnImage Grow="1"&gt;</c> silently does nothing).</summary>
    [Fact]
    public void BnImage_ForwardsTheWholeItemSurface_AndSrcOnThePropWire()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<BnImage>(ParameterView.FromDictionary(ImageParams()));
        Assert.NotEmpty(frames);
        RenderFrame mount = frames[0];
        CreateNodePatch root = Root(mount);
        Assert.Equal("image", root.NodeType);

        // The WHOLE style table, exactly — `src` absent from it is the pin that the
        // prop did not drift onto the style wire.
        Assert.Equal(
            ImageItemWireTable().ToDictionary(e => e.Key, e => (string?)e.Value),
            StylesOf(mount, root.NodeId));

        // …and the WHOLE prop table, exactly: one prop, `src`. A flex name falling
        // out of the renderer's allow-list would land here as a second UpdateProp.
        UpdatePropPatch src = Assert.Single(mount.Patches.OfType<UpdatePropPatch>());
        Assert.Equal(root.NodeId, src.NodeId);
        Assert.Equal("src", src.Name);
        Assert.Equal(ImageSrc, src.Value);
    }

    /// <summary>Host for the `src` → null transition: an image with a source, and a
    /// button whose click takes the source away. (An image has no events of its own —
    /// it is a leaf — so the re-render has to be driven from a sibling.)</summary>
    private sealed class ClearSrcHost : ComponentBase
    {
        private bool _cleared;

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder b)
        {
            b.OpenComponent<BnColumn>(0);
            b.AddComponentParameter(1, nameof(BnColumn.ChildContent), (RenderFragment)(cb =>
            {
                cb.OpenComponent<BnImage>(0);
                cb.AddComponentParameter(1, nameof(BnImage.Src), _cleared ? null : ImageSrc);
                cb.CloseComponent();

                cb.OpenComponent<BnButton>(10);
                cb.AddComponentParameter(11, nameof(BnButton.Label), "Clear");
                cb.AddComponentParameter(12, nameof(BnButton.OnClick),
                    EventCallback.Factory.Create<MouseEventArgs>(this, () => _cleared = true));
                cb.CloseComponent();
            }));
            b.CloseComponent();
        }
    }

    /// <summary><c>Src</c> → null IS A WIRE EVENT, and the renderer emits it TODAY —
    /// which is why it needs a spec and a test rather than a silence for Gates 2/3 to
    /// each guess at differently.
    /// <para>A null attribute is not appended to the frame at all, so taking
    /// <c>Src</c> away is a <c>RemoveAttribute</c> on a NON-STYLE name, and the
    /// renderer turns that into <c>UpdateProp(nodeId, "src", null)</c> — structurally
    /// the same event as
    /// <see cref="BnButton_ReEnabled_EmitsEnabledNullProp"/> (the established
    /// precedent: a null on the prop wire means "the author took the attribute away",
    /// and the shell restores the default).</para>
    /// <para><b>What the shells owe</b> (BnImage.cs's header, normative): cancel the
    /// in-flight request, CLEAR the image, <c>markDirty</c>, re-solve — so an
    /// intrinsic image collapses back to <c>0 × 0</c> and its siblings move back UP.
    /// That is a SECOND REFLOW DIRECTION, and it is the one the parity contract would
    /// otherwise never have mentioned. This test pins the emission; Gates 2/3 pin the
    /// behaviour at the mapper level (no demo page flips a Src at run time — see the
    /// scope note in BnImage.cs).</para></summary>
    [Fact]
    public void BnImage_SrcGoesNull_EmitsUpdatePropNullOnThePropWire()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<ClearSrcHost>();
        Assert.NotEmpty(frames);
        RenderFrame mount = frames[0];

        CreateNodePatch image = Assert.Single(
            mount.Patches.OfType<CreateNodePatch>(), p => p.NodeType == "image");
        Assert.Equal(ImageSrc, PropOn(mount, image.NodeId, "src").Value);

        AttachEventPatch clear = Assert.Single(mount.Patches.OfType<AttachEventPatch>());
        Assert.Equal(0, Exports.DispatchEventCore(
            (ulong)clear.HandlerId, /*lang=json*/ """{"name":"click"}"""));

        Assert.True(frames.Count >= 2, "expected a synchronous re-render frame");
        RenderFrame after = frames[^1];

        // THE PROP WIRE, with a null value — and the assertion is on the patch KIND
        // by construction (it looks only at UpdatePropPatch). Exactly one: the image
        // is the only node that changed.
        UpdatePropPatch cleared = Assert.Single(after.Patches.OfType<UpdatePropPatch>());
        Assert.Equal(image.NodeId, cleared.NodeId);
        Assert.Equal("src", cleared.Name);
        Assert.Null(cleared.Value);

        // …and NOT the style wire. `src` leaving is not a layout event in .NET — the
        // re-layout is the SHELL's, off the back of this prop (clear + markDirty +
        // re-solve). Nothing here touches a style.
        Assert.Empty(after.Patches.OfType<SetStylePatch>());
    }
}
