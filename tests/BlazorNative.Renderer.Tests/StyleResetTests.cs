using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorNative.Renderer.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// StyleResetTests — Phase 6.1 Task 1.2 (design §"The null-reset bug").
//
// Two things, both about the SetStyle allow-list (NativeRenderer.StyleAttributes):
//
//   1. Classification. The flex surface (flexGrow, flexShrink, flexBasis,
//      alignSelf, alignContent, flexWrap, gap, rowGap, columnGap, position,
//      top, right, bottom, left, minWidth, maxWidth, minHeight, maxHeight —
//      joining the eight flex-ish names already there) must route to
//      SetStylePatch, not UpdatePropPatch. No ABI change: these ride the
//      EXISTING SetStyle wire (patch kind 6).
//
//   2. THE BUG (found by the 6.1 codebase survey). REMOVING an attribute on
//      re-render emitted UpdatePropPatch(name, null) for EVERY name, style or
//      not. Harmless while nobody unsets Padding; fatal once FlexGrow can go
//      conditionally null — the shell's Yoga node would keep the old value
//      forever, because the reset arrived on the wrong wire. The fix: branch
//      on StyleAttributes at the removal site so a removed style emits
//      SetStylePatch(name, null) — null ALREADY means "reset to default" on
//      that wire (PatchProtocol.cs). Both shells honour it in Gates 2/3.
//
// These assert the patch KIND, not just the value: a null value on the wrong
// patch type is precisely the bug.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class StyleResetTests
{
    /// <summary>The names Phase 6.1 ADDS to the allow-list (the flex surface).</summary>
    public static TheoryData<string> NewStyleNames() =>
    [
        "flexGrow", "flexShrink", "flexBasis", "alignSelf", "alignContent",
        "flexWrap", "gap", "rowGap", "columnGap", "position",
        "top", "right", "bottom", "left",
        "minWidth", "maxWidth", "minHeight", "maxHeight",
    ];

    private static (NativeRenderer Renderer, List<RenderFrame> Frames) BuildRenderer()
    {
        var services = new ServiceCollection().AddBlazorNativeRenderer();
        var renderer = services.BuildServiceProvider().GetRequiredService<NativeRenderer>();
        renderer.StrictErrors = true;
        var frames = new List<RenderFrame>();
        renderer.Frames += (f, _) =>
        {
            frames.Add(f);
            return ValueTask.CompletedTask;
        };
        return (renderer, frames);
    }

    /// <summary>A div carrying ONE attribute whose name is a test parameter.</summary>
    private sealed class OneAttribute : ComponentBase
    {
        [Parameter] public string Name { get; set; } = "";
        [Parameter] public string Value { get; set; } = "";

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, Name, Value);
            b.CloseElement();
        }
    }

    [Theory]
    [MemberData(nameof(NewStyleNames))]
    public async Task NewFlexNames_ClassifyAsSetStyle_NotUpdateProp(string name)
    {
        var (renderer, frames) = BuildRenderer();

        await renderer.MountAsync<OneAttribute>(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(OneAttribute.Name)] = name,
                [nameof(OneAttribute.Value)] = "1",
            }));
        Assert.NotEmpty(frames);

        var style = Assert.Single(frames[0].Patches.OfType<SetStylePatch>());
        Assert.Equal(name, style.Property);
        Assert.Equal("1", style.Value);
        Assert.Empty(frames[0].Patches.OfType<UpdatePropPatch>());
    }

    // ── The null-reset bug ────────────────────────────────────────────────────

    /// <summary>Renders a STYLE attribute (flexGrow) that a click removes —
    /// the conditional-flex-prop shape ("Grow = _grown ? 1 : null").</summary>
    private sealed class ConditionalFlexGrow : ComponentBase
    {
        private bool _reset;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "flexGrow", _reset ? null : "1"); // null → attribute GONE
            b.AddAttribute(2, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, () => _reset = true));
            b.CloseElement();
        }
    }

    /// <summary>The same shape on a NON-style prop (placeholder) — the guard
    /// that the fix does not swallow the UpdateProp removal path (BnButton's
    /// re-enable contract rides it: UpdatePropPatch("enabled", null)).</summary>
    private sealed class ConditionalPlaceholder : ComponentBase
    {
        private bool _reset;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "input");
            b.AddAttribute(1, "placeholder", _reset ? null : "type…");
            b.AddAttribute(2, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, () => _reset = true));
            b.CloseElement();
        }
    }

    [Fact]
    public async Task RemovedStyleAttribute_EmitsSetStyleNull_NotUpdatePropNull()
    {
        var (renderer, frames) = BuildRenderer();
        await renderer.MountAsync<ConditionalFlexGrow>(ParameterView.Empty);
        var node = Assert.Single(frames[0].Patches.OfType<CreateNodePatch>());
        var attach = Assert.Single(frames[0].Patches.OfType<AttachEventPatch>());
        Assert.Equal("1", Assert.Single(frames[0].Patches.OfType<SetStylePatch>()).Value);

        // Grow goes conditionally null → Blazor emits a RemoveAttribute edit.
        await renderer.DispatchUiEventAsync(
            new NativeUiEvent(0, attach.HandlerId, "click", null));
        Assert.True(frames.Count >= 2, "expected a synchronous re-render frame");
        var reset = frames[^1];

        // THE assertion — the KIND. A null on SetStyle is "reset to the Yoga
        // default"; the same null on UpdateProp is a prop the shell never
        // routes to Yoga, so the node would keep flexGrow:1 forever.
        var style = Assert.Single(reset.Patches.OfType<SetStylePatch>());
        Assert.Equal(node.NodeId, style.NodeId);
        Assert.Equal("flexGrow", style.Property);
        Assert.Null(style.Value);
        Assert.Empty(reset.Patches.OfType<UpdatePropPatch>());
    }

    [Fact]
    public async Task RemovedNonStyleAttribute_StillEmitsUpdatePropNull()
    {
        var (renderer, frames) = BuildRenderer();
        await renderer.MountAsync<ConditionalPlaceholder>(ParameterView.Empty);
        var node = Assert.Single(frames[0].Patches.OfType<CreateNodePatch>());
        var attach = Assert.Single(frames[0].Patches.OfType<AttachEventPatch>());

        await renderer.DispatchUiEventAsync(
            new NativeUiEvent(0, attach.HandlerId, "click", null));
        Assert.True(frames.Count >= 2, "expected a synchronous re-render frame");
        var reset = frames[^1];

        var prop = Assert.Single(reset.Patches.OfType<UpdatePropPatch>());
        Assert.Equal(node.NodeId, prop.NodeId);
        Assert.Equal("placeholder", prop.Name);
        Assert.Null(prop.Value);
        Assert.Empty(reset.Patches.OfType<SetStylePatch>());
    }
}
