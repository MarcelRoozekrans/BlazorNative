using BlazorNative.Renderer;
using BlazorNative.Runtime;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// BnDemoTests — Phase 3.4 Task 4 (design §3/§4): the bind loop (DoD #5) and
// the cascading theme toggle (DoD #6) proven HEADLESS through the real host
// path — HostSession.TryMount("BnDemo") + Exports.DispatchEventCore — at the
// patch level every host decodes. The Kotlin twin (BnDemoTest.kt, Gate 3)
// mirrors these shapes through the published dll.
//
// Shape: see BnDemo.cs's file header — the CANONICAL pinned tree lives
// there; keep THAT one updated (this header and Gate 3's Kotlin twin
// deliberately don't duplicate it — same instruction applies to
// BnDemoTest.kt).
// Theme toggle: #FFEEAA ⇄ #334455 on BOTH themed divs (the cascaded BnTheme
// consumers re-render — DoD #6).
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class BnDemoTests
{
    private const string DefaultBackground = "#FFEEAA";
    private const string AltBackground = "#334455";
    private const string ClickArgs = /*lang=json*/ """{"name":"click"}""";

    private static (RenderFrame Mount, List<RenderFrame> Frames) MountDemo()
    {
        HostSession.ResetForTests();
        NativeRenderer renderer = HostSession.EnsureSession();
        var frames = new List<RenderFrame>();
        renderer.Frames += (f, _) =>
        {
            frames.Add(f);
            return ValueTask.CompletedTask;
        };
        Assert.Equal(0, HostSession.TryMount("BnDemo"));
        Assert.NotEmpty(frames);
        return (frames[0], frames);
    }

    private static CreateNodePatch CreateOf(RenderFrame frame, int nodeId)
        => Assert.Single(frame.Patches.OfType<CreateNodePatch>(), p => p.NodeId == nodeId);

    /// <summary>NodeId of the element CONTAINING the given text (the text
    /// node's create parent).</summary>
    private static int ContainerOfText(RenderFrame frame, string text)
    {
        var t = Assert.Single(frame.Patches.OfType<ReplaceTextPatch>(), p => p.Text == text);
        return Assert.IsType<int>(CreateOf(frame, t.NodeId).ParentId);
    }

    private static SetStylePatch StyleOn(RenderFrame frame, int nodeId, string prop)
        => Assert.Single(frame.Patches.OfType<SetStylePatch>(),
            p => p.NodeId == nodeId && p.Property == prop);

    private static UpdatePropPatch PropOn(RenderFrame frame, int nodeId, string prop)
        => Assert.Single(frame.Patches.OfType<UpdatePropPatch>(),
            p => p.NodeId == nodeId && p.Name == prop);

    private static int ClickHandlerOn(RenderFrame frame, int nodeId)
        => Assert.Single(frame.Patches.OfType<AttachEventPatch>(),
            p => p.NodeId == nodeId && p.EventName == "click").HandlerId;

    // Structural pins (stable across re-renders — the Gate 3 mirror uses the
    // same walk): root = the single parentless create; echo panel = the only
    // "view" child of the root; echo TEXT NODE = grandchild via the echo span.
    private static CreateNodePatch Root(RenderFrame mount)
        => Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);

    private static CreateNodePatch EchoPanel(RenderFrame mount, int rootId)
        => Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.ParentId == rootId && p.NodeType == "view");

    private static CreateNodePatch InputNode(RenderFrame mount)
        => Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.NodeType == "input");

    private static int EchoTextNode(RenderFrame mount)
    {
        var root = Root(mount);
        var panel = EchoPanel(mount, root.NodeId);
        var span = Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.ParentId == panel.NodeId);
        return Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.ParentId == span.NodeId).NodeId;
    }

    private static int Dispatch(int handlerId, string args)
        => Exports.DispatchEventCore((ulong)handlerId, args);

    // ── Mount shape ───────────────────────────────────────────────────────────

    [Fact]
    public void Mount_Shape_FormWithThemedPanelsInputEchoAndButtons()
    {
        var (mount, _) = MountDemo();

        // Root: the themed form div.
        var root = Root(mount);
        Assert.Equal("view", root.NodeType);
        Assert.Equal(DefaultBackground, StyleOn(mount, root.NodeId, "backgroundColor").Value);
        Assert.Equal("16", StyleOn(mount, root.NodeId, "padding").Value);

        // Title span with fontSize, under the form.
        var title = ContainerOfText(mount, "BnDemo");
        Assert.Equal("text", CreateOf(mount, title).NodeType);
        Assert.Equal(root.NodeId, CreateOf(mount, title).ParentId);
        Assert.Equal("24", StyleOn(mount, title, "fontSize").Value);

        // The bound input: value + placeholder props, change attach.
        var input = InputNode(mount);
        Assert.Equal(root.NodeId, input.ParentId);
        Assert.Equal("", PropOn(mount, input.NodeId, "value").Value);
        Assert.Equal("Type here...", PropOn(mount, input.NodeId, "placeholder").Value);
        Assert.Single(mount.Patches.OfType<AttachEventPatch>(),
            p => p.NodeId == input.NodeId && p.EventName == "change");

        // Echo panel: the second themed view, with the echo span inside it.
        var panel = EchoPanel(mount, root.NodeId);
        Assert.Equal(DefaultBackground, StyleOn(mount, panel.NodeId, "backgroundColor").Value);
        Assert.Equal("8", StyleOn(mount, panel.NodeId, "padding").Value);
        var echoText = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(), p => p.Text == "");
        Assert.Equal(EchoTextNode(mount), echoText.NodeId);

        // Buttons under the form, each with a click attach (Phase 3.5:
        // + "Settings →", the navigation entry — DoD #7).
        var clear = ContainerOfText(mount, "Clear");
        var theme = ContainerOfText(mount, "Theme");
        var settings = ContainerOfText(mount, "Settings →");
        foreach (var btn in new[] { clear, theme, settings })
        {
            Assert.Equal("button", CreateOf(mount, btn).NodeType);
            Assert.Equal(root.NodeId, CreateOf(mount, btn).ParentId);
            _ = ClickHandlerOn(mount, btn);
        }

        // Order pin for Gate 3. Blazor's FIFO render queue CREATES the form
        // children as title → input → Clear → Theme → Settings → echo panel
        // (the panel's BnView is a chained child component, queued behind the
        // buttons), so the panel's create carries the MID-LIST InsertIndex 2 —
        // after title + input, before the buttons — while everything else
        // appends. FINAL child order: title, input, echo panel, Clear,
        // Theme, Settings →.
        var formChildren = mount.Patches.OfType<CreateNodePatch>()
            .Where(p => p.ParentId == root.NodeId)
            .ToList();
        Assert.Equal(
            new[] { title, input.NodeId, clear, theme, settings, panel.NodeId },
            formChildren.Select(p => p.NodeId)); // creation (patch) order
        Assert.Equal(2, panel.InsertIndex);
        Assert.All(formChildren.Where(p => p.NodeId != panel.NodeId),
            p => Assert.Equal(-1, p.InsertIndex));

        // Exactly 4 event attaches: change + Clear + Theme + Settings →.
        Assert.Equal(4, mount.Patches.OfType<AttachEventPatch>().Count());
    }

    // ── The bind loop headless (DoD #5) ───────────────────────────────────────

    [Fact]
    public void ChangeDispatch_EchoRerendersAndValueWritesBack()
    {
        var (mount, frames) = MountDemo();
        var input = InputNode(mount);
        var echoNode = EchoTextNode(mount);
        var changeHandler = Assert.Single(mount.Patches.OfType<AttachEventPatch>(),
            p => p.EventName == "change").HandlerId;

        Assert.Equal(0, Dispatch(changeHandler,
            /*lang=json*/ """{"name":"change","payload":"hello"}"""));
        Assert.True(frames.Count >= 2, "expected a synchronous re-render frame");
        var frame = frames[^1];

        // The echo BnText re-rendered "hello" on ITS mount-pinned text node…
        var echoed = Assert.Single(frame.Patches.OfType<ReplaceTextPatch>(), p => p.Text == "hello");
        Assert.Equal(echoNode, echoed.NodeId);

        // …and the bound Value wrote back to the input's host prop.
        Assert.Equal("hello", PropOn(frame, input.NodeId, "value").Value);
    }

    [Fact]
    public void ClearClick_ResetsValuePropAndEcho()
    {
        var (mount, frames) = MountDemo();
        var input = InputNode(mount);
        var echoNode = EchoTextNode(mount);
        var changeHandler = Assert.Single(mount.Patches.OfType<AttachEventPatch>(),
            p => p.EventName == "change").HandlerId;
        var clearHandler = ClickHandlerOn(mount, ContainerOfText(mount, "Clear"));

        Assert.Equal(0, Dispatch(changeHandler,
            /*lang=json*/ """{"name":"change","payload":"hello"}"""));
        Assert.Equal(0, Dispatch(clearHandler, ClickArgs));
        var frame = frames[^1];

        // Both halves reset: the input's value prop AND the echo text.
        Assert.Equal("", PropOn(frame, input.NodeId, "value").Value);
        var echoed = Assert.Single(frame.Patches.OfType<ReplaceTextPatch>(), p => p.Text == "");
        Assert.Equal(echoNode, echoed.NodeId);
    }

    // ── The cascading theme toggle headless (DoD #6) ──────────────────────────

    [Fact]
    public void ThemeClick_FlipsBackgroundOnBothThemedChildren()
    {
        var (mount, frames) = MountDemo();
        var root = Root(mount);
        var panel = EchoPanel(mount, root.NodeId);
        var themeHandler = ClickHandlerOn(mount, ContainerOfText(mount, "Theme"));

        Assert.Equal(0, Dispatch(themeHandler, ClickArgs));
        Assert.True(frames.Count >= 2, "expected a synchronous re-render frame");
        var frame = frames[^1];

        // The cascaded BnTheme changed → BOTH consumers re-rendered with the
        // alt background (DoD #6: parent change → children re-render).
        var flipped = frame.Patches.OfType<SetStylePatch>()
            .Where(p => p.Property == "backgroundColor" && p.Value == AltBackground)
            .Select(p => p.NodeId)
            .ToHashSet();
        Assert.Contains(root.NodeId, flipped);
        Assert.Contains(panel.NodeId, flipped);
        Assert.True(flipped.Count >= 2, "expected ≥2 themed children to flip");

        // Toggling again restores the default on both.
        Assert.Equal(0, Dispatch(themeHandler, ClickArgs));
        var back = frames[^1].Patches.OfType<SetStylePatch>()
            .Where(p => p.Property == "backgroundColor" && p.Value == DefaultBackground)
            .Select(p => p.NodeId)
            .ToHashSet();
        Assert.Contains(root.NodeId, back);
        Assert.Contains(panel.NodeId, back);
    }
}
