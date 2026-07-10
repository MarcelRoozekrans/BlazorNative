using BlazorNative.Components;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

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
public sealed class BnComponentTests
{
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

    private static CreateNodePatch CreateOf(RenderFrame frame, int nodeId)
        => Assert.Single(frame.Patches.OfType<CreateNodePatch>(), p => p.NodeId == nodeId);

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
}
