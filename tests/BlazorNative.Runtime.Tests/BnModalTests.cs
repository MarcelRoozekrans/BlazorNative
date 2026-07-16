using BlazorNative.Components;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using static BlazorNative.Runtime.Tests.GoldenAssertions;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// BnModalTests — Phase 7.4 (design decisions 1-4): the .NET half of the
// overlay contract, through the PRODUCTION dispatch ingress
// (Exports.DispatchEventCore). Same harness as BnFormControlTests.
//
// What Gate 1 owns and pins here:
//   • the wire shape — NodeType "modal" (wire id 11), the always-emitted
//     scrimColor PROP, the always-attached dismissal `click`, ONE wire child
//     (the content box) carrying the declared surface, ZERO styles on the
//     modal node itself (SetStyle on a modal is shell-ignored by design —
//     this component must not be able to emit one);
//   • visibility — Visible=false is ZERO wire presence; hide is unmount, the
//     whole subtree leaving as ONE RemoveNodePatch (decision 2);
//   • dismissal is a REQUEST — VisibleChanged(false), nothing self-closes: an
//     unbound parent keeps the modal open, a raced second dismissal is
//     absorbed (stale handler, at-most-once delivery);
//   • the content-box swallow — a REAL click dispatch that moves nothing
//     (decision 4's Android fall-through half; iOS's touch-view filter is
//     Gate 3's).
//
// The `click`-on-a-plain-view .NET end (decision 4) needs NO renderer change
// and these tests are its proof: the attach emission is name-generic (any
// `on*` attribute) and BuildEventArgs("click") is element-agnostic — the
// modal element and the content-box DIV both attach and dispatch through the
// production ingress below. The shells' halves (Android's generic
// setOnClickListener, iOS's tap-recognizer arm) are Gates 2/3.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class BnModalTests : IDisposable
{
    private const string ClickArgs = /*lang=json*/ """{"name":"click"}""";

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

    private static AttachEventPatch ClickAttachOn(RenderFrame frame, int nodeId)
        => Assert.Single(frame.Patches.OfType<AttachEventPatch>(),
            p => p.NodeId == nodeId && p.EventName == "click");

    /// <summary>Mounts a visible BnModal with a BnText child and returns the
    /// mount frame's landmarks. <paramref name="extra"/> merges over the
    /// defaults.</summary>
    private static (RenderFrame Mount, List<RenderFrame> Frames, CreateNodePatch Modal, int ContentBox)
        MountVisibleModal(Dictionary<string, object?>? extra = null)
    {
        var (renderer, frames) = CreateCapturingSession();
        var parameters = new Dictionary<string, object?>
        {
            [nameof(BnModal.Visible)] = true,
            [nameof(BnModal.ChildContent)] = (RenderFragment)(b =>
            {
                b.OpenComponent<BnText>(0);
                b.AddComponentParameter(1, nameof(BnText.Text), "inside");
                b.CloseComponent();
            }),
        };
        foreach (var kv in extra ?? new Dictionary<string, object?>())
            parameters[kv.Key] = kv.Value;

        renderer.Mount<BnModal>(ParameterView.FromDictionary(parameters));
        Assert.NotEmpty(frames);
        RenderFrame mount = frames[0];
        CreateNodePatch modal = Root(mount);
        int contentBox = Assert.Single(ChildrenOf(mount, modal.NodeId));
        return (mount, frames, modal, contentBox);
    }

    // ── The wire shape (decision 1's .NET half) ───────────────────────────────

    /// <summary>Visible=false is ZERO wire presence (decision 2): no create,
    /// no prop, no attach — nothing for a shell to hide because nothing
    /// exists.</summary>
    [Fact]
    public void VisibleFalse_ZeroWirePresence()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<BnModal>(ParameterView.Empty); // Visible defaults false

        Assert.Empty(frames.SelectMany(f => f.Patches).OfType<CreateNodePatch>());
        Assert.Empty(frames.SelectMany(f => f.Patches).OfType<UpdatePropPatch>());
        Assert.Empty(frames.SelectMany(f => f.Patches).OfType<AttachEventPatch>());
    }

    /// <summary>The mount shape: the modal node (NodeType "modal" → wire id
    /// 11) with the always-emitted scrimColor prop and the always-attached
    /// dismissal click; ONE wire child — the content box — with its own
    /// swallow click; ChildContent inside the box; and ZERO styles on the
    /// modal node (the style-ignore rule's .NET half: the surface cannot
    /// emit one).</summary>
    [Fact]
    public void VisibleTrue_MountShape_ModalPlusContentBox()
    {
        var (mount, _, modal, contentBox) = MountVisibleModal();

        Assert.Equal("modal", modal.NodeType);
        Assert.Equal("#80000000", Assert.Single(mount.Patches.OfType<UpdatePropPatch>(),
            p => p.NodeId == modal.NodeId && p.Name == "scrimColor").Value);
        ClickAttachOn(mount, modal.NodeId);
        Assert.Empty(StylesOf(mount, modal.NodeId));

        Assert.Equal("view", CreateOf(mount, contentBox).NodeType);
        ClickAttachOn(mount, contentBox);

        // ChildContent parents into the BOX (the wire tree the shells' overlay
        // redirection reproduces one level up).
        int textSpan = Assert.Single(ChildrenOf(mount, contentBox));
        Assert.Equal("text", CreateOf(mount, textSpan).NodeType);
        Assert.Contains(mount.Patches.OfType<ReplaceTextPatch>(), p => p.Text == "inside");

        // Exactly the two clicks — the dismissal wire and the swallow.
        Assert.Equal(2, mount.Patches.OfType<AttachEventPatch>().Count());
        Assert.All(mount.Patches.OfType<AttachEventPatch>(),
            p => Assert.Equal("click", p.EventName));
    }

    /// <summary>The content box forwards the WHOLE declared surface
    /// (ContentWidth/ContentHeight/Padding/BackgroundColor — the BnScroll
    /// forwarding precedent) onto the SetStyle wire, and nothing else.</summary>
    [Fact]
    public void ContentBox_ForwardsTheDeclaredSurface()
    {
        var (mount, _, modal, contentBox) = MountVisibleModal(new()
        {
            [nameof(BnModal.ContentWidth)] = "280",
            [nameof(BnModal.ContentHeight)] = "180",
            [nameof(BnModal.Padding)] = 12f,
            [nameof(BnModal.BackgroundColor)] = "#FFFFFF",
        });

        AssertNode(mount, contentBox, "content box", "view",
            ("width", "280"), ("height", "180"), ("padding", "12"),
            ("backgroundColor", "#FFFFFF"));
        // …and the modal node STILL carries none of it.
        Assert.Empty(StylesOf(mount, modal.NodeId));
    }

    /// <summary>scrimColor is ALWAYS on the wire (the BnInput posture — no
    /// shell-side default two platforms would have to keep equal): the
    /// default and a custom value both ride the prop wire.</summary>
    [Theory]
    [InlineData(null, "#80000000")] // the default — nothing passed
    [InlineData("#CC112233", "#CC112233")]
    public void ScrimColor_AlwaysEmitted(string? passed, string expected)
    {
        var extra = new Dictionary<string, object?>();
        if (passed is not null)
            extra[nameof(BnModal.ScrimColor)] = passed;
        var (mount, _, modal, _) = MountVisibleModal(extra);

        Assert.Equal(expected, Assert.Single(mount.Patches.OfType<UpdatePropPatch>(),
            p => p.NodeId == modal.NodeId && p.Name == "scrimColor").Value);
    }

    // ── Dismissal is a REQUEST (decision 2) ───────────────────────────────────

    /// <summary>The dismissal-request wire contract: a click dispatch on the
    /// MODAL node invokes VisibleChanged(false) — and nothing else. The
    /// component never flips its own state.</summary>
    [Fact]
    public void DismissRequest_InvokesVisibleChangedFalse()
    {
        bool? received = null;
        var (mount, _, modal, _) = MountVisibleModal(new()
        {
            [nameof(BnModal.VisibleChanged)] = EventCallback.Factory.Create<bool>(
                new object(), v => received = v),
        });

        Assert.Equal(0, Exports.DispatchEventCore(
            (ulong)ClickAttachOn(mount, modal.NodeId).HandlerId, ClickArgs));

        Assert.False(received!.Value);
    }

    /// <summary>A parent that ignores the request keeps the modal open
    /// (decision 2): VisibleChanged unbound → the dispatch succeeds and NO
    /// RemoveNodePatch ever appears. The shell never closes anything
    /// itself — there is nothing here that could.</summary>
    [Fact]
    public void UnboundDismissRequest_TheModalStaysMounted()
    {
        var (mount, frames, modal, _) = MountVisibleModal();

        Assert.Equal(0, Exports.DispatchEventCore(
            (ulong)ClickAttachOn(mount, modal.NodeId).HandlerId, ClickArgs));

        Assert.Empty(frames.SelectMany(f => f.Patches).OfType<RemoveNodePatch>());
    }

    /// <summary>The content-box swallow (decision 4): its click is a REAL
    /// dispatch (rc 0 — the device counters account for it) that requests
    /// NOTHING — VisibleChanged never fires, nothing unmounts. Together with
    /// the dismissal test above this is the .NET half of "a tap dismisses
    /// ONLY on the scrim, never on a descendant".</summary>
    [Fact]
    public void ContentBoxTap_IsSwallowed_NoDismissRequest()
    {
        int fired = 0;
        var (mount, frames, _, contentBox) = MountVisibleModal(new()
        {
            [nameof(BnModal.VisibleChanged)] = EventCallback.Factory.Create<bool>(
                new object(), (bool _) => fired++),
        });

        Assert.Equal(0, Exports.DispatchEventCore(
            (ulong)ClickAttachOn(mount, contentBox).HandlerId, ClickArgs));

        Assert.Equal(0, fired);
        Assert.Empty(frames.SelectMany(f => f.Patches).OfType<RemoveNodePatch>());
    }

    // ── Hide is unmount (decision 2's patch shape) ────────────────────────────

    /// <summary>Host for the bound pair: the scrim tap's VisibleChanged(false)
    /// writes back, exactly like <c>@bind-Visible</c> expands to. ChildContent
    /// is INLINE text on purpose — no nested component — so the hide frame
    /// below is the modal's own diff and nothing else (a nested COMPONENT adds
    /// its own disposal remove alongside; BnModalDemoTests pins that composite
    /// shape).</summary>
    private sealed class BoundVisibilityHost : ComponentBase
    {
        private bool _visible = true;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenComponent<BnModal>(0);
            b.AddComponentParameter(1, nameof(BnModal.Visible), _visible);
            b.AddComponentParameter(2, nameof(BnModal.VisibleChanged),
                EventCallback.Factory.Create<bool>(this, v => _visible = v));
            b.AddComponentParameter(3, nameof(BnModal.ChildContent),
                (RenderFragment)(cb => cb.AddContent(0, "inside")));
            b.CloseComponent();
        }
    }

    /// <summary>Hide unmounts as ONE RemoveNodePatch for the modal node — the
    /// whole subtree leaves in the disposal shape 7.2's removes-first fix
    /// hardened; the shells' two-subtree purge (anchor + overlay) hangs off
    /// exactly this patch in Gates 2/3.</summary>
    [Fact]
    public void BoundDismiss_HideUnmounts_OneRemoveNodePatch()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<BoundVisibilityHost>(ParameterView.Empty);
        var mount = frames[0];
        var modal = Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.NodeType == "modal");
        int framesBefore = frames.Count;

        Assert.Equal(0, Exports.DispatchEventCore(
            (ulong)ClickAttachOn(mount, modal.NodeId).HandlerId, ClickArgs));

        RemoveNodePatch remove = Assert.Single(
            frames.Skip(framesBefore).SelectMany(f => f.Patches).OfType<RemoveNodePatch>());
        Assert.Equal(modal.NodeId, remove.NodeId);
    }

    /// <summary>A dismissal that races a second one is absorbed BY
    /// CONSTRUCTION (decisions 2/3): the first unmounts, so the second's
    /// handler is stale — at-most-once delivery logs it (rc 0, a stale tap is
    /// not an error) and nothing moves. This is the .NET half of "a back that
    /// races an in-flight dismissal is absorbed".</summary>
    [Fact]
    public void RacedSecondDismissRequest_IsAbsorbed()
    {
        var (renderer, frames) = CreateCapturingSession();

        renderer.Mount<BoundVisibilityHost>(ParameterView.Empty);
        var mount = frames[0];
        var modal = Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.NodeType == "modal");
        ulong handler = (ulong)ClickAttachOn(mount, modal.NodeId).HandlerId;

        Assert.Equal(0, Exports.DispatchEventCore(handler, ClickArgs));
        int framesAfterFirst = frames.Count;

        Assert.Equal(0, Exports.DispatchEventCore(handler, ClickArgs)); // stale, absorbed

        Assert.Empty(frames.Skip(framesAfterFirst).SelectMany(f => f.Patches));
    }

    // ── The declared surface, pinned (the I3 declaration-pin method) ──────────

    /// <summary>The parameter surface is EXACTLY the design's (decision 2):
    /// the bind pair, ChildContent, ScrimColor and the content box's four
    /// declared-surface params. NO flex item surface — a modal is not IN the
    /// flex flow (the anchor is 0-sized and absolute; a Width or Margin here
    /// would style a node the author does not own) — and NO container family
    /// (the box is a plain view; compose a BnColumn inside for layout).</summary>
    [Fact]
    public void DeclaresExactlyTheDesignedSurface()
    {
        string[] expected =
        [
            $"{nameof(BnModal.BackgroundColor)}: {typeof(string)}",
            $"{nameof(BnModal.ChildContent)}: {typeof(RenderFragment)}",
            $"{nameof(BnModal.ContentHeight)}: {typeof(string)}",
            $"{nameof(BnModal.ContentWidth)}: {typeof(string)}",
            $"{nameof(BnModal.Padding)}: {typeof(float?)}",
            $"{nameof(BnModal.ScrimColor)}: {typeof(string)}",
            $"{nameof(BnModal.Visible)}: {typeof(bool)}",
            $"{nameof(BnModal.VisibleChanged)}: {typeof(EventCallback<bool>)}",
        ];

        Assert.Equal(expected, typeof(BnModal).GetProperties()
            .Where(p => p.IsDefined(typeof(ParameterAttribute), inherit: true))
            .Select(p => $"{p.Name}: {p.PropertyType}")
            .OrderBy(n => n, StringComparer.Ordinal));
    }
}
