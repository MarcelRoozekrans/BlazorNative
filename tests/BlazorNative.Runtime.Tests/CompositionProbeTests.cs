using BlazorNative.Renderer;
using BlazorNative.Runtime;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// CompositionProbeTests — Phase 3.3 Task 7: the Gate 1 end-to-end proof on
// the .NET surface. Drives the registered CompositionProbe through the REAL
// host path — HostSession.TryMount (the blazornative_mount core) + Exports
// .DispatchEventCore (the blazornative_dispatch_event core) — and asserts the
// patch stream every host will decode:
//   • mount shape: component slots parent correctly (badge under the root
//     container, items under the list container — DoD #8), and the
//     INTERLEAVED badge's create carries the mid-container InsertIndex;
//   • insert-at-front → CreateNodePatch(InsertIndex: 0) under the list;
//   • add-at-end → CreateNodePatch(InsertIndex: -1) (genuine append);
//   • remove-first → RemoveNodePatch for the FIRST item's view;
//   • a child ItemComponent's own @onclick round-trips (its own re-render
//     targets its own text node).
//
// The Kotlin twin (CompositionProbeTest.kt, Gate 2) asserts the same shapes
// through the published dll. Shares the "host-session" collection —
// HostSession is a process-wide singleton.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class CompositionProbeTests
{
    private const string ClickArgs = /*lang=json*/ """{"name":"click"}""";

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

    private static (RenderFrame Mount, List<RenderFrame> Frames) MountProbe()
    {
        var (_, frames) = CreateCapturingSession();
        Assert.Equal(0, HostSession.TryMount("CompositionProbe"));
        Assert.NotEmpty(frames);
        return (frames[0], frames);
    }

    /// <summary>NodeId of the element CONTAINING the given text (the text
    /// node's create parent).</summary>
    private static int ContainerOfText(RenderFrame frame, string text)
    {
        var t = Assert.Single(frame.Patches.OfType<ReplaceTextPatch>(), p => p.Text == text);
        var c = Assert.Single(frame.Patches.OfType<CreateNodePatch>(), p => p.NodeId == t.NodeId);
        return Assert.IsType<int>(c.ParentId);
    }

    private static CreateNodePatch CreateOf(RenderFrame frame, int nodeId)
        => Assert.Single(frame.Patches.OfType<CreateNodePatch>(), p => p.NodeId == nodeId);

    private static int HandlerOn(RenderFrame frame, int nodeId)
    {
        var attach = Assert.Single(frame.Patches.OfType<AttachEventPatch>(),
            p => p.NodeId == nodeId && p.EventName == "click");
        Assert.True(attach.HandlerId > 0);
        return attach.HandlerId;
    }

    private static int Dispatch(int handlerId)
        => Exports.DispatchEventCore((ulong)handlerId, ClickArgs);

    // ── Mount shape (component slots + DoD #8 parenting) ─────────────────────

    [Fact]
    public void Mount_Shape_ComponentsParentUnderTheirContainers()
    {
        var (mount, _) = MountProbe();

        // Exactly one parentless create: the root container.
        var root = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);

        // Root-level elements all parent under the root container.
        var header = ContainerOfText(mount, "CompositionProbe");
        var label = ContainerOfText(mount, "list:");
        var addBtn = ContainerOfText(mount, "Add");
        var insertBtn = ContainerOfText(mount, "Insert");
        var removeBtn = ContainerOfText(mount, "Remove");
        foreach (var nodeId in new[] { header, label, addBtn, insertBtn, removeBtn })
            Assert.Equal(root.NodeId, CreateOf(mount, nodeId).ParentId);

        // DoD #8: the INTERLEAVED badge ItemComponent roots under the root
        // container (its recorded parent), NOT the host root…
        var badgeDiv = ContainerOfText(mount, "badge (taps: 0)");
        var badgeCreate = CreateOf(mount, badgeDiv);
        Assert.Equal(root.NodeId, badgeCreate.ParentId);

        // …and — because the badge's own diff runs AFTER the parent finished
        // walking (creating header/label/list/buttons first) — its create
        // carries the mid-container view index 1 (right after the header),
        // not an append. THE interleave-order proof at the wire level.
        Assert.Equal(1, badgeCreate.InsertIndex);

        // The keyed ItemComponents root under the LIST container, which is
        // itself under root — and they appended in order (nothing after them
        // existed at their render time → InsertIndex -1).
        var item1Div = ContainerOfText(mount, "item-1 (taps: 0)");
        var item2Div = ContainerOfText(mount, "item-2 (taps: 0)");
        var listDiv = Assert.IsType<int>(CreateOf(mount, item1Div).ParentId);
        Assert.Equal(listDiv, CreateOf(mount, item2Div).ParentId);
        Assert.Equal(root.NodeId, CreateOf(mount, listDiv).ParentId);
        Assert.Equal(-1, CreateOf(mount, item1Div).InsertIndex);
        Assert.Equal(-1, CreateOf(mount, item2Div).InsertIndex);

        // Handlers: badge + 2 items + 3 buttons.
        Assert.Equal(6, mount.Patches.OfType<AttachEventPatch>().Count());
    }

    // ── Insert-at-front (the DoD #10 payoff) ─────────────────────────────────

    [Fact]
    public void InsertAtFront_NewItemCreateCarriesFrontViewIndex()
    {
        var (mount, frames) = MountProbe();
        var listDiv = Assert.IsType<int>(
            CreateOf(mount, ContainerOfText(mount, "item-1 (taps: 0)")).ParentId);
        var insertHandler = HandlerOn(mount, ContainerOfText(mount, "Insert"));

        Assert.Equal(0, Dispatch(insertHandler));
        Assert.True(frames.Count >= 2, "expected a synchronous re-render frame");
        var frame = frames[^1];

        // The new ItemComponent's view is created under the list container
        // with InsertIndex 0 — visibly FIRST, before item-1 and item-2.
        var created = Assert.Single(frame.Patches.OfType<CreateNodePatch>(),
            p => p.ParentId == listDiv);
        Assert.Equal(0, created.InsertIndex);
        var newText = Assert.Single(frame.Patches.OfType<ReplaceTextPatch>(),
            p => p.Text == "item-3 (taps: 0)");
        Assert.Equal(created.NodeId, CreateOf(frame, newText.NodeId).ParentId);
    }

    [Fact]
    public void AddAtEnd_NewItemCreateIsAnAppend()
    {
        var (mount, frames) = MountProbe();
        var listDiv = Assert.IsType<int>(
            CreateOf(mount, ContainerOfText(mount, "item-1 (taps: 0)")).ParentId);
        var addHandler = HandlerOn(mount, ContainerOfText(mount, "Add"));

        Assert.Equal(0, Dispatch(addHandler));
        var frame = frames[^1];

        var created = Assert.Single(frame.Patches.OfType<CreateNodePatch>(),
            p => p.ParentId == listDiv);
        Assert.Equal(-1, created.InsertIndex); // genuine append at the list end
        Assert.Single(frame.Patches.OfType<ReplaceTextPatch>(), p => p.Text == "item-3 (taps: 0)");
    }

    // ── Remove-first ──────────────────────────────────────────────────────────

    [Fact]
    public void RemoveFirst_RemovesTheFirstItemsView()
    {
        var (mount, frames) = MountProbe();
        var item1Div = ContainerOfText(mount, "item-1 (taps: 0)");
        var removeHandler = HandlerOn(mount, ContainerOfText(mount, "Remove"));

        Assert.Equal(0, Dispatch(removeHandler));
        var frame = frames[^1];

        // The FIRST item's root view is removed (the disposed child's
        // RemoveNodePatch); no creates — a pure removal.
        var removed = Assert.Single(frame.Patches.OfType<RemoveNodePatch>());
        Assert.Equal(item1Div, removed.NodeId);
        Assert.Empty(frame.Patches.OfType<CreateNodePatch>());
    }

    // ── Child ItemComponent click round-trip ──────────────────────────────────

    [Fact]
    public void ChildItemComponent_Click_RoundTripsToItsOwnTextNode()
    {
        var (mount, frames) = MountProbe();
        var badgeDiv = ContainerOfText(mount, "badge (taps: 0)");
        var badgeText = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(),
            p => p.Text == "badge (taps: 0)");
        var badgeHandler = HandlerOn(mount, badgeDiv);

        Assert.Equal(0, Dispatch(badgeHandler));
        Assert.True(frames.Count >= 2, "expected a synchronous re-render frame");

        // The child's OWN state mutated and its OWN text node updated —
        // the nested component's diff resolved against its own slot list.
        var updated = Assert.Single(frames[^1].Patches.OfType<ReplaceTextPatch>(),
            p => p.Text == "badge (taps: 1)");
        Assert.Equal(badgeText.NodeId, updated.NodeId);
    }
}
