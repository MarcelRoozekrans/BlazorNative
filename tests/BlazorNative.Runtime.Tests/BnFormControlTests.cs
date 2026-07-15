using BlazorNative.Components;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using static BlazorNative.Runtime.Tests.GoldenAssertions;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// BnFormControlTests — Phase 7.3 Task 1.2 (design decisions 1-3): per-control
// mount shapes, bind round-trips through the PRODUCTION dispatch ingress
// (Exports.DispatchEventCore), the loud-garbage contract (rc 2 — the 7.2
// scroll-payload pattern), the programmatic-set-no-refire guard, and the
// picker's normative clamp rule. Same harness as BnComponentTests.
//
// THE DISABLED-ATTACH DECISION (recorded here, asserted below): a disabled
// control STILL attaches its `change` handler — BnInput's precedent (its
// onchange is unconditional; only OPTIONAL telemetry like OnFocus/OnScroll
// attaches on-subscribe). The host widget is disabled, so it never
// dispatches — "disabled controls dispatch nothing" is a Gates 2/3 device
// assertion; keeping the attach unconditional keeps the wire shape
// independent of Enabled.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class BnFormControlTests : IDisposable
{
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

    private static UpdatePropPatch PropOn(RenderFrame frame, int nodeId, string prop)
        => Assert.Single(frame.Patches.OfType<UpdatePropPatch>(),
            p => p.NodeId == nodeId && p.Name == prop);

    private static AttachEventPatch ChangeAttachOn(RenderFrame frame, int nodeId)
        => Assert.Single(frame.Patches.OfType<AttachEventPatch>(),
            p => p.NodeId == nodeId && p.EventName == "change");

    private static string ChangeArgs(string payload)
        => "{\"name\":\"change\",\"payload\":\"" + payload + "\"}";

    // ── BnCheckbox ────────────────────────────────────────────────────────────

    [Fact]
    public void BnCheckbox_Mount_EmitsCheckboxValueAndChangeAttach()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<BnCheckbox>(ParameterView.Empty);
        Assert.NotEmpty(frames);
        var mount = frames[0];

        var root = Root(mount);
        Assert.Equal("checkbox", root.NodeType); // → NodeType 8 on the wire
        Assert.Equal("false", PropOn(mount, root.NodeId, "value").Value);
        ChangeAttachOn(mount, root.NodeId);
        // Enabled defaults true → NO enabled prop (BnButton's boolean-attr
        // semantics), and the un-styled invariant: NO styles at all.
        Assert.DoesNotContain(mount.Patches.OfType<UpdatePropPatch>(), p => p.Name == "enabled");
        Assert.Empty(StylesOf(mount, root.NodeId));
    }

    [Fact]
    public void BnCheckbox_CheckedTrue_EmitsValueTrue()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<BnCheckbox>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(BnCheckbox.Checked)] = true,
        }));
        var mount = frames[0];
        Assert.Equal("true", PropOn(mount, Root(mount).NodeId, "value").Value);
    }

    /// <summary>The disabled-attach decision (file header): enabled "false"
    /// prop AND the change attach is STILL there — BnInput's precedent.</summary>
    [Fact]
    public void BnCheckbox_Disabled_EmitsEnabledFalse_AndStillAttachesChange()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<BnCheckbox>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(BnCheckbox.Enabled)] = false,
        }));
        var mount = frames[0];
        var root = Root(mount);
        Assert.Equal("false", PropOn(mount, root.NodeId, "enabled").Value);
        ChangeAttachOn(mount, root.NodeId);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void BnCheckbox_ChangeDispatch_InvokesCheckedChanged(string payload, bool expected)
    {
        var (renderer, frames) = CreateCapturingSession();
        bool? received = null;

        renderer.Mount<BnCheckbox>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(BnCheckbox.CheckedChanged)] = EventCallback.Factory.Create<bool>(
                new object(), v => received = v),
        }));
        var attach = ChangeAttachOn(frames[0], Root(frames[0]).NodeId);

        Assert.Equal(0, Exports.DispatchEventCore((ulong)attach.HandlerId, ChangeArgs(payload)));
        Assert.Equal(expected, received);
    }

    /// <summary>Garbage is LOUD (the 7.2 pattern): anything but the exact
    /// literals "true"/"false" — including a mis-cased "True" and a missing
    /// payload — is a shell contract violation → FormatException inside the
    /// dispatch window → export rc 2, never a silently-wrong bool.</summary>
    [Theory]
    [InlineData("""{"name":"change","payload":"True"}""")]
    [InlineData("""{"name":"change","payload":"1"}""")]
    [InlineData("""{"name":"change","payload":"banana"}""")]
    [InlineData("""{"name":"change"}""")]
    public void BnCheckbox_GarbagePayload_FaultsTheDispatch(string args)
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<BnCheckbox>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(BnCheckbox.CheckedChanged)] = EventCallback.Factory.Create<bool>(
                new object(), _ => { }),
        }));
        var attach = ChangeAttachOn(frames[0], Root(frames[0]).NodeId);

        Assert.Equal(2, Exports.DispatchEventCore((ulong)attach.HandlerId, args));
    }

    /// <summary>Host for the programmatic-set guard: its button sets Checked
    /// via state — never through the checkbox's own change wire.</summary>
    private sealed class CheckboxProgrammaticSetHost : ComponentBase
    {
        [Parameter] public Action? CheckedChangedFired { get; set; }

        private bool _checked;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenComponent<BnCheckbox>(0);
            b.AddComponentParameter(1, nameof(BnCheckbox.Checked), _checked);
            b.AddComponentParameter(2, nameof(BnCheckbox.CheckedChanged),
                EventCallback.Factory.Create<bool>(this, _ => CheckedChangedFired?.Invoke()));
            b.CloseComponent();
            b.OpenElement(3, "button");
            b.AddAttribute(4, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, () => _checked = true));
            b.CloseElement();
        }
    }

    /// <summary>The bind-loop guard at the .NET level: a PROGRAMMATIC Checked
    /// set re-renders to UpdateProp("value","true") and CheckedChanged never
    /// fires. (The host-side half — the applyingBatch guard / setOn fires
    /// nothing — is Gates 2/3's, per control.)</summary>
    [Fact]
    public void BnCheckbox_ProgrammaticSet_EmitsPropAndDoesNotRefireChange()
    {
        var (renderer, frames) = CreateCapturingSession();
        var fired = 0;

        renderer.Mount<CheckboxProgrammaticSetHost>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(CheckboxProgrammaticSetHost.CheckedChangedFired)] = (Action)(() => fired++),
        }));
        var mount = frames[0];
        var checkbox = Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.NodeType == "checkbox");
        Assert.Equal("false", PropOn(mount, checkbox.NodeId, "value").Value);
        var click = Assert.Single(mount.Patches.OfType<AttachEventPatch>(),
            p => p.EventName == "click");

        Assert.Equal(0, Exports.DispatchEventCore(
            (ulong)click.HandlerId, /*lang=json*/ """{"name":"click"}"""));

        Assert.True(frames.Count >= 2, "expected the programmatic set's re-render frame");
        Assert.Equal("true", PropOn(frames[^1], checkbox.NodeId, "value").Value);
        Assert.Equal(0, fired);
    }

    // ── BnSwitch (the NodeType is the difference — decision 1/2) ─────────────

    [Fact]
    public void BnSwitch_Mount_EmitsSwitchValueAndChangeAttach()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<BnSwitch>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(BnSwitch.Checked)] = true,
        }));
        var mount = frames[0];
        var root = Root(mount);
        Assert.Equal("switch", root.NodeType); // → NodeType 9 on the wire
        Assert.Equal("true", PropOn(mount, root.NodeId, "value").Value);
        ChangeAttachOn(mount, root.NodeId);
    }

    [Fact]
    public void BnSwitch_ChangeDispatch_InvokesCheckedChanged()
    {
        var (renderer, frames) = CreateCapturingSession();
        bool? received = null;

        renderer.Mount<BnSwitch>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(BnSwitch.Checked)] = true,
            [nameof(BnSwitch.CheckedChanged)] = EventCallback.Factory.Create<bool>(
                new object(), v => received = v),
        }));
        var attach = ChangeAttachOn(frames[0], Root(frames[0]).NodeId);

        Assert.Equal(0, Exports.DispatchEventCore((ulong)attach.HandlerId, ChangeArgs("false")));
        Assert.False(received!.Value);
    }

    [Fact]
    public void BnSwitch_GarbagePayload_FaultsTheDispatch()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<BnSwitch>(ParameterView.Empty);
        var attach = ChangeAttachOn(frames[0], Root(frames[0]).NodeId);

        Assert.Equal(2, Exports.DispatchEventCore((ulong)attach.HandlerId, ChangeArgs("on")));
    }

    // ── BnSlider ──────────────────────────────────────────────────────────────

    [Fact]
    public void BnSlider_Mount_EmitsValueMinMaxAndChangeAttach()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<BnSlider>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(BnSlider.Value)] = 25f,
        }));
        var mount = frames[0];
        var root = Root(mount);
        Assert.Equal("slider", root.NodeType); // → NodeType 10 on the wire
        Assert.Equal("25", PropOn(mount, root.NodeId, "value").Value);
        // The range is DECLARED on the wire (defaults 0/100 in the component,
        // never a shell-side default two platforms would have to keep equal).
        Assert.Equal("0", PropOn(mount, root.NodeId, "min").Value);
        Assert.Equal("100", PropOn(mount, root.NodeId, "max").Value);
        // Step unset → ABSENT (continuous — the un-styled invariant).
        Assert.DoesNotContain(mount.Patches.OfType<UpdatePropPatch>(), p => p.Name == "step");
        ChangeAttachOn(mount, root.NodeId);
    }

    [Fact]
    public void BnSlider_StepSet_EmitsStep()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<BnSlider>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(BnSlider.Min)] = 10f,
            [nameof(BnSlider.Max)] = 20f,
            [nameof(BnSlider.Step)] = 0.5f,
        }));
        var mount = frames[0];
        var root = Root(mount);
        Assert.Equal("10", PropOn(mount, root.NodeId, "min").Value);
        Assert.Equal("20", PropOn(mount, root.NodeId, "max").Value);
        Assert.Equal("0.5", PropOn(mount, root.NodeId, "step").Value);
    }

    /// <summary>The 6.1 invariant-stringification pattern, pinned for the
    /// slider's FOUR float params under a Dutch locale — a "62,5" on the wire
    /// is a shell parse error (extends the FloatParams culture pin to the
    /// prop wire).</summary>
    [Fact]
    public void BnSlider_FloatProps_StringifyInvariantlyUnderADutchLocale()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                new System.Globalization.CultureInfo("nl-NL");

            var (renderer, frames) = CreateCapturingSession();
            renderer.Mount<BnSlider>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(BnSlider.Value)] = 62.5f,
                [nameof(BnSlider.Min)] = 0.5f,
                [nameof(BnSlider.Max)] = 99.5f,
                [nameof(BnSlider.Step)] = 2.5f,
            }));
            var mount = frames[0];
            var root = Root(mount);
            Assert.Equal("62.5", PropOn(mount, root.NodeId, "value").Value);
            Assert.Equal("0.5", PropOn(mount, root.NodeId, "min").Value);
            Assert.Equal("99.5", PropOn(mount, root.NodeId, "max").Value);
            Assert.Equal("2.5", PropOn(mount, root.NodeId, "step").Value);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>…and the INBOUND half of the same pin: a fractional payload
    /// parses invariantly under the Dutch locale (the 7.2 OnScroll
    /// discipline, on the change wire).</summary>
    [Fact]
    public void BnSlider_ChangeDispatch_ParsesInvariantlyUnderADutchLocale()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                new System.Globalization.CultureInfo("nl-NL");

            var (renderer, frames) = CreateCapturingSession();
            float? received = null;
            renderer.Mount<BnSlider>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(BnSlider.ValueChanged)] = EventCallback.Factory.Create<float>(
                    new object(), v => received = v),
            }));
            var attach = ChangeAttachOn(frames[0], Root(frames[0]).NodeId);

            Assert.Equal(0, Exports.DispatchEventCore((ulong)attach.HandlerId, ChangeArgs("62.5")));
            Assert.Equal(62.5f, received);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("""{"name":"change","payload":"banana"}""")]
    [InlineData("""{"name":"change","payload":"62,5"}""")] // the Dutch comma IS garbage
    [InlineData("""{"name":"change","payload":""}""")]
    [InlineData("""{"name":"change"}""")]
    public void BnSlider_GarbagePayload_FaultsTheDispatch(string args)
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<BnSlider>(ParameterView.Empty);
        var attach = ChangeAttachOn(frames[0], Root(frames[0]).NodeId);

        Assert.Equal(2, Exports.DispatchEventCore((ulong)attach.HandlerId, args));
    }

    private sealed class SliderProgrammaticSetHost : ComponentBase
    {
        [Parameter] public Action? ValueChangedFired { get; set; }

        private float _value = 25f;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenComponent<BnSlider>(0);
            b.AddComponentParameter(1, nameof(BnSlider.Value), _value);
            b.AddComponentParameter(2, nameof(BnSlider.ValueChanged),
                EventCallback.Factory.Create<float>(this, _ => ValueChangedFired?.Invoke()));
            b.CloseComponent();
            b.OpenElement(3, "button");
            b.AddAttribute(4, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, () => _value = 80f));
            b.CloseElement();
        }
    }

    [Fact]
    public void BnSlider_ProgrammaticSet_EmitsPropAndDoesNotRefireChange()
    {
        var (renderer, frames) = CreateCapturingSession();
        var fired = 0;

        renderer.Mount<SliderProgrammaticSetHost>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(SliderProgrammaticSetHost.ValueChangedFired)] = (Action)(() => fired++),
        }));
        var mount = frames[0];
        var slider = Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.NodeType == "slider");
        Assert.Equal("25", PropOn(mount, slider.NodeId, "value").Value);
        var click = Assert.Single(mount.Patches.OfType<AttachEventPatch>(),
            p => p.EventName == "click");

        Assert.Equal(0, Exports.DispatchEventCore(
            (ulong)click.HandlerId, /*lang=json*/ """{"name":"click"}"""));

        Assert.True(frames.Count >= 2, "expected the programmatic set's re-render frame");
        Assert.Equal("80", PropOn(frames[^1], slider.NodeId, "value").Value);
        Assert.Equal(0, fired);
    }

    // ── BnPicker (decision 3: the state-owner precedent) ─────────────────────

    private static readonly string[] ThreeItems = ["Alpha", "Bravo", "Charlie"];

    [Fact]
    public void BnPicker_Mount_EmitsItemsJsonSelectedIndexAndChangeAttach()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<BnPicker>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(BnPicker.Items)] = (IReadOnlyList<string>)ThreeItems,
            [nameof(BnPicker.SelectedIndex)] = 1,
        }));
        var mount = frames[0];
        var root = Root(mount);
        Assert.Equal("picker", root.NodeType); // select → NodeType 7, the 2.5 stub made real
        Assert.Equal("""["Alpha","Bravo","Charlie"]""",
            PropOn(mount, root.NodeId, "items").Value);
        Assert.Equal("1", PropOn(mount, root.NodeId, "selectedIndex").Value);
        ChangeAttachOn(mount, root.NodeId);
    }

    /// <summary>The clamp rule, empty arm: no items → selectedIndex -1 (the
    /// only state an empty picker has) and items exactly "[]" — for a NULL
    /// Items parameter and an empty list alike.</summary>
    [Fact]
    public void BnPicker_NoItems_EmitsEmptyArrayAndMinusOne()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<BnPicker>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(BnPicker.SelectedIndex)] = 3, // ignored: empty clamps to -1
        }));
        var mount = frames[0];
        var root = Root(mount);
        Assert.Equal("[]", PropOn(mount, root.NodeId, "items").Value);
        Assert.Equal("-1", PropOn(mount, root.NodeId, "selectedIndex").Value);
    }

    /// <summary>The clamp rule on the WIRE at mount: an out-of-range
    /// SelectedIndex never reaches the shell raw — the wire carries the
    /// CLAMPED index (to the last item), and the state-owner notify re-syncs
    /// the bound state to it.</summary>
    [Fact]
    public void BnPicker_OutOfRangeSelectedIndex_ClampsOnTheWire_AndNotifies()
    {
        var (renderer, frames) = CreateCapturingSession();
        int? notified = null;

        renderer.Mount<BnPicker>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(BnPicker.Items)] = (IReadOnlyList<string>)ThreeItems,
            [nameof(BnPicker.SelectedIndex)] = 7,
            [nameof(BnPicker.SelectedIndexChanged)] = EventCallback.Factory.Create<int>(
                new object(), i => notified = i),
        }));
        var mount = frames[0];
        Assert.Equal("2", PropOn(mount, Root(mount).NodeId, "selectedIndex").Value);
        Assert.Equal(2, notified);
    }

    [Fact]
    public void BnPicker_ChangeDispatch_InvokesSelectedIndexChanged()
    {
        var (renderer, frames) = CreateCapturingSession();
        int? received = null;

        renderer.Mount<BnPicker>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(BnPicker.Items)] = (IReadOnlyList<string>)ThreeItems,
            [nameof(BnPicker.SelectedIndexChanged)] = EventCallback.Factory.Create<int>(
                new object(), i => received = i),
        }));
        var attach = ChangeAttachOn(frames[0], Root(frames[0]).NodeId);

        Assert.Equal(0, Exports.DispatchEventCore((ulong)attach.HandlerId, ChangeArgs("2")));
        Assert.Equal(2, received);
    }

    /// <summary>The clamp rule INBOUND: a well-formed but out-of-range index
    /// (the benign items-shrink race — the user picked just before a shrink
    /// frame applied) CLAMPS by the same rule rather than faulting.</summary>
    [Fact]
    public void BnPicker_OutOfRangeDispatch_ClampsByTheSameRule()
    {
        var (renderer, frames) = CreateCapturingSession();
        int? received = null;

        renderer.Mount<BnPicker>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(BnPicker.Items)] = (IReadOnlyList<string>)ThreeItems,
            [nameof(BnPicker.SelectedIndexChanged)] = EventCallback.Factory.Create<int>(
                new object(), i => received = i),
        }));
        var attach = ChangeAttachOn(frames[0], Root(frames[0]).NodeId);

        Assert.Equal(0, Exports.DispatchEventCore((ulong)attach.HandlerId, ChangeArgs("9")));
        Assert.Equal(2, received);
    }

    /// <summary>Only an UNPARSEABLE payload is garbage — rc 2, the 7.2
    /// pattern (out-of-range is the race above, not garbage).</summary>
    [Theory]
    [InlineData("""{"name":"change","payload":"Bravo"}""")] // the ITEM, not its index
    [InlineData("""{"name":"change","payload":"1.5"}""")]
    [InlineData("""{"name":"change"}""")]
    public void BnPicker_GarbagePayload_FaultsTheDispatch(string args)
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<BnPicker>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(BnPicker.Items)] = (IReadOnlyList<string>)ThreeItems,
        }));
        var attach = ChangeAttachOn(frames[0], Root(frames[0]).NodeId);

        Assert.Equal(2, Exports.DispatchEventCore((ulong)attach.HandlerId, args));
    }

    /// <summary>Host for the items-shrink clamp: its button replaces Items
    /// under a live selection (bound: the write-back stores the clamp).</summary>
    private sealed class PickerShrinkHost : ComponentBase
    {
        [Parameter] public Action<int>? Notified { get; set; }

        /// <summary>What the button shrinks Items to (parameterized so one
        /// host drives both the shrink-below-selection and the go-empty
        /// arms of the clamp rule).</summary>
        [Parameter] public IReadOnlyList<string> ShrinkTo { get; set; } = [];

        private IReadOnlyList<string> _items = ThreeItems;
        private int _selected = 2;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenComponent<BnPicker>(0);
            b.AddComponentParameter(1, nameof(BnPicker.Items), _items);
            b.AddComponentParameter(2, nameof(BnPicker.SelectedIndex), _selected);
            b.AddComponentParameter(3, nameof(BnPicker.SelectedIndexChanged),
                EventCallback.Factory.Create<int>(this, i =>
                {
                    _selected = i;
                    Notified?.Invoke(i);
                }));
            b.CloseComponent();
            b.OpenElement(4, "button");
            b.AddAttribute(5, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, () => _items = ShrinkTo));
            b.CloseElement();
        }
    }

    /// <summary>THE NORMATIVE CLAMP, shrink arm: items shrink below the
    /// selection → the selection clamps TO THE LAST item, the bound state is
    /// notified (the state-owner rule), and the wire carries the clamped
    /// index plus the new items JSON.</summary>
    [Fact]
    public void BnPicker_ItemsShrinkBelowSelection_ClampsToLastAndNotifies()
    {
        var (renderer, frames) = CreateCapturingSession();
        var notifications = new List<int>();

        renderer.Mount<PickerShrinkHost>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(PickerShrinkHost.Notified)] = (Action<int>)notifications.Add,
            [nameof(PickerShrinkHost.ShrinkTo)] = (IReadOnlyList<string>)new[] { "Alpha", "Bravo" },
        }));
        var mount = frames[0];
        var picker = Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.NodeType == "picker");
        Assert.Equal("2", PropOn(mount, picker.NodeId, "selectedIndex").Value);
        Assert.Empty(notifications); // in range at mount: the guard is quiet
        var click = Assert.Single(mount.Patches.OfType<AttachEventPatch>(),
            p => p.EventName == "click");

        Assert.Equal(0, Exports.DispatchEventCore(
            (ulong)click.HandlerId, /*lang=json*/ """{"name":"click"}"""));

        Assert.Equal([1], notifications); // clamped to the LAST item, exactly once
        var last = frames[^1];
        Assert.Equal("""["Alpha","Bravo"]""", PropOn(last, picker.NodeId, "items").Value);
        Assert.Equal("1", PropOn(last, picker.NodeId, "selectedIndex").Value);
    }

    /// <summary>THE NORMATIVE CLAMP, empty arm: items go empty → -1.</summary>
    [Fact]
    public void BnPicker_ItemsGoEmpty_ClampsToMinusOneAndNotifies()
    {
        var (renderer, frames) = CreateCapturingSession();
        var notifications = new List<int>();

        renderer.Mount<PickerShrinkHost>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(PickerShrinkHost.Notified)] = (Action<int>)notifications.Add,
            [nameof(PickerShrinkHost.ShrinkTo)] = (IReadOnlyList<string>)Array.Empty<string>(),
        }));
        var mount = frames[0];
        var picker = Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.NodeType == "picker");
        var click = Assert.Single(mount.Patches.OfType<AttachEventPatch>(),
            p => p.EventName == "click");

        Assert.Equal(0, Exports.DispatchEventCore(
            (ulong)click.HandlerId, /*lang=json*/ """{"name":"click"}"""));

        Assert.Equal([-1], notifications);
        var last = frames[^1];
        Assert.Equal("[]", PropOn(last, picker.NodeId, "items").Value);
        Assert.Equal("-1", PropOn(last, picker.NodeId, "selectedIndex").Value);
    }

    /// <summary>Host for the picker's programmatic-set guard: the button sets
    /// SelectedIndex to an IN-RANGE value via state.</summary>
    private sealed class PickerProgrammaticSetHost : ComponentBase
    {
        [Parameter] public Action? SelectedIndexChangedFired { get; set; }

        private int _selected;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenComponent<BnPicker>(0);
            b.AddComponentParameter(1, nameof(BnPicker.Items), (IReadOnlyList<string>)ThreeItems);
            b.AddComponentParameter(2, nameof(BnPicker.SelectedIndex), _selected);
            b.AddComponentParameter(3, nameof(BnPicker.SelectedIndexChanged),
                EventCallback.Factory.Create<int>(this, _ => SelectedIndexChangedFired?.Invoke()));
            b.CloseComponent();
            b.OpenElement(4, "button");
            b.AddAttribute(5, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, () => _selected = 2));
            b.CloseElement();
        }
    }

    /// <summary>The programmatic-set guard for the picker: an IN-RANGE set
    /// clamps to itself — the state-owner notify stays quiet, only the
    /// UpdateProp moves (SelectedIndexChanged fires ONLY on a real user pick
    /// or a real clamp).</summary>
    [Fact]
    public void BnPicker_InRangeProgrammaticSet_EmitsPropAndDoesNotRefire()
    {
        var (renderer, frames) = CreateCapturingSession();
        var fired = 0;

        renderer.Mount<PickerProgrammaticSetHost>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(PickerProgrammaticSetHost.SelectedIndexChangedFired)] = (Action)(() => fired++),
        }));
        var mount = frames[0];
        var picker = Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.NodeType == "picker");
        Assert.Equal("0", PropOn(mount, picker.NodeId, "selectedIndex").Value);
        var click = Assert.Single(mount.Patches.OfType<AttachEventPatch>(),
            p => p.EventName == "click");

        Assert.Equal(0, Exports.DispatchEventCore(
            (ulong)click.HandlerId, /*lang=json*/ """{"name":"click"}"""));

        Assert.True(frames.Count >= 2, "expected the programmatic set's re-render frame");
        Assert.Equal("2", PropOn(frames[^1], picker.NodeId, "selectedIndex").Value);
        Assert.Equal(0, fired);
    }

    // ── The flex-item surface rides the STYLE wire on all four ───────────────

    /// <summary>One representative pin per control that the item surface
    /// (BnScroll's) reaches the SetStyle wire — the golden (BnFormDemoTests)
    /// pins the demo's exact tables; this pins the mechanism per
    /// component.</summary>
    [Fact]
    public void FormControls_ItemSurface_RidesTheStyleWire()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<BnSlider>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(BnSlider.Width)] = "240",
            [nameof(BnSlider.AlignSelf)] = FlexAlign.Center,
            [nameof(BnSlider.Grow)] = 1f,
            [nameof(BnSlider.Margin)] = "8",
        }));
        var mount = frames[0];
        var root = Root(mount);
        AssertNode(mount, root.NodeId, "slider", "slider",
            ("width", "240"), ("alignSelf", "center"), ("flexGrow", "1"), ("margin", "8"));
    }
}
