using System.Reflection;
using BlazorNative.Components;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using static BlazorNative.Runtime.Tests.GoldenAssertions;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// BnActivityIndicatorTests — the parity survey's cheap win (design decision 5).
// The component has NO CONTROL surface of its own ("no props, no new wire
// surface"), so the whole contract is three claims:
//
//   • the DEFAULT wire shape is ONE CreateNodePatch (NodeType
//     "activityindicator" → wire id 12) and NOTHING else — no props, no
//     styles, no attaches, no children (a measure func owns the size when
//     nothing overrides it — the measured-leaf shape; the shells add the
//     NodeType to MEASURED_NODE_TYPES);
//   • it declares NO OWN parameters, pinned reflectively — a param growing
//     here is a deliberate design change, not drift — but it DOES inherit
//     the shared item surface from BnLayoutItem (13.0 Task 8: it is a
//     measured leaf exactly like `image`/`checkbox`/`switch`/`slider`, so it
//     takes a Margin/Width/Height/etc. the same as any other layout
//     participant; left unset, none of it is appended to the wire, which is
//     why the shape above stays a single bare create);
//   • presence is `@if` (the decision-2 posture): unmount is the ordinary
//     RemoveNode disposal, which is what "animating while mounted" means —
//     there is no start/stop prop for a shell to mirror.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class BnActivityIndicatorTests : IDisposable
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

    /// <summary>The measured-leaf wire shape: ONE create — NodeType
    /// "activityindicator" (wire id 12) — and nothing else on any wire.</summary>
    [Fact]
    public void Mount_TheMeasuredLeafWireShape_OneCreateAndNothingElse()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<BnActivityIndicator>(ParameterView.Empty);
        Assert.NotEmpty(frames);
        var mount = frames[0];

        var root = Root(mount);
        Assert.Equal("activityindicator", root.NodeType);

        Assert.Single(mount.Patches.OfType<CreateNodePatch>());
        Assert.Empty(mount.Patches.OfType<UpdatePropPatch>());
        Assert.Empty(mount.Patches.OfType<SetStylePatch>());
        Assert.Empty(mount.Patches.OfType<AttachEventPatch>());
        Assert.Empty(mount.Patches.OfType<ReplaceTextPatch>());
    }

    /// <summary>"No props, no new wire surface" (design decision 5), pinned
    /// reflectively: BnActivityIndicator declares no parameters OF ITS OWN.
    /// State does not exist, presence is <c>@if</c> — there is nothing for
    /// this control to declare. It DOES inherit the 17-parameter item surface
    /// from <see cref="BnLayoutItem"/> (13.0 Task 8) — asserted here as the
    /// other half of the same claim, so a future redeclaration of one of
    /// those names on this type reds this test rather than silently
    /// shadowing the base.</summary>
    [Fact]
    public void DeclaresNoOwnParameters_ButInheritsTheFullItemSurface()
    {
        Assert.DoesNotContain(typeof(BnActivityIndicator)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            p => p.IsDefined(typeof(ParameterAttribute), inherit: true));

        Assert.True(typeof(BnLayoutItem).IsAssignableFrom(typeof(BnActivityIndicator)));
        foreach (string name in LayoutSurfacePinTests.ItemParameters)
            Assert.NotNull(typeof(BnActivityIndicator).GetProperty(name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy));
    }

    /// <summary>Host for the presence-is-<c>@if</c> posture: a button whose
    /// click unmounts the indicator through ordinary state.</summary>
    private sealed class PresenceHost : ComponentBase
    {
        private bool _spinning = true;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            if (_spinning)
            {
                b.OpenComponent<BnActivityIndicator>(0);
                b.CloseComponent();
            }
            b.OpenElement(1, "button");
            b.AddAttribute(2, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, () => _spinning = false));
            b.CloseElement();
        }
    }

    /// <summary>Presence is <c>@if</c> (the decision-2 posture): "animating
    /// while mounted" means stop == unmount — one RemoveNodePatch for the
    /// indicator node, no stop prop for two shells to keep equal.</summary>
    [Fact]
    public void PresenceIsIf_HideIsTheOrdinaryUnmount()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<PresenceHost>(ParameterView.Empty);
        var mount = frames[0];
        var indicator = Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.NodeType == "activityindicator");
        var click = Assert.Single(mount.Patches.OfType<AttachEventPatch>(),
            p => p.EventName == "click");

        Assert.Equal(0, Exports.DispatchEventCore(
            (ulong)click.HandlerId, /*lang=json*/ """{"name":"click"}"""));

        Assert.True(frames.Count >= 2, "expected the unmount re-render frame");
        Assert.Contains(frames.Skip(1).SelectMany(f => f.Patches).OfType<RemoveNodePatch>(),
            p => p.NodeId == indicator.NodeId);
    }
}
