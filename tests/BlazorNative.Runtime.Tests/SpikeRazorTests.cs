using BlazorNative.Components;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using Microsoft.AspNetCore.Components;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// SpikeRazorTests — Phase 7.0 (the Razor-compilation spike, M7 DoD #1).
//
// THE ONE QUESTION, answered at the patch level every host decodes: can a
// .razor file, compiled by the Razor source generator (Microsoft.NET.Sdk.Razor
// on BlazorNative.Components — not hand-translated), produce a component that
// renders through NativeRenderer — no web host, trim-clean?
//
// The load-bearing proof is GOLDEN-VS-TWIN: SpikeRazor (.razor-compiled) and
// SpikeRazorTwin (the same component hand-written pre-7.0 style) are mounted
// through the real host session and driven through the SAME dispatch sequence,
// and their patch streams must be IDENTICAL — every frame, every patch, in
// order (record equality; only CommitFramePatch is excluded, it carries a
// wall-clock timestamp). NodeIds/handlerIds/frameIds all restart with the
// fresh session per mount, so identity really is byte-for-byte, not modulo
// renaming. The .razor version's whitespace Markup frames must vanish
// wire-invisibly (the renderer's 7.0 Markup arm — MarkupFrameTests owns that
// contract; THIS file proves the invisibility end to end).
//
// The remaining tests drive the spike's four capabilities on the .razor-
// compiled component itself: markup shape, [Parameter], @onclick, @bind —
// through Exports.DispatchEventCore, the same harness as BnDemoTests.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class SpikeRazorTests : IDisposable
{
    private const string Title = "SpikeRazor";
    private const string ClickArgs = /*lang=json*/ """{"name":"click"}""";
    private const string ChangeHello = /*lang=json*/ """{"name":"change","payload":"hello"}""";

    public void Dispose() => HostSession.ResetForTests();

    private static (List<RenderFrame> Frames, RenderFrame Mount) MountFresh<T>() where T : IComponent
    {
        HostSession.ResetForTests();
        NativeRenderer renderer = HostSession.EnsureSession();
        var frames = new List<RenderFrame>();
        renderer.Frames += (f, _) =>
        {
            frames.Add(f);
            return ValueTask.CompletedTask;
        };
        renderer.Mount<T>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            ["Title"] = Title,
        }));
        Assert.NotEmpty(frames);
        return (frames, frames[0]);
    }

    private static int HandlerOf(RenderFrame mount, string eventName)
        => Assert.Single(mount.Patches.OfType<AttachEventPatch>(),
            p => p.EventName == eventName).HandlerId;

    private static int Dispatch(int handlerId, string args)
        => Exports.DispatchEventCore((ulong)handlerId, args);

    // ── The GREEN bar: golden-vs-twin ─────────────────────────────────────────

    /// <summary>Every patch of every frame across the whole lifecycle —
    /// mount, a change dispatch (@bind write-back), a click dispatch
    /// (@onclick state reset) — byte-identical between the .razor-compiled
    /// component and its hand-written twin. Mutation-verified (Gate 1):
    /// removing the .razor [Parameter], an attribute, or the bind reddens
    /// this test — see the spike conclusion's mutation table.</summary>
    [Fact]
    public void GoldenVsTwin_PatchStreams_AreIdentical_AcrossTheWholeLifecycle()
    {
        static List<RenderPatch[]> Drive<T>() where T : IComponent
        {
            var (frames, mount) = MountFresh<T>();
            Assert.Equal(0, Dispatch(HandlerOf(mount, "change"), ChangeHello));
            Assert.Equal(0, Dispatch(HandlerOf(mount, "click"), ClickArgs));
            // CommitFramePatch carries TimestampMs — the one nondeterministic
            // patch; everything else must match as records, in order.
            return frames
                .Select(f => f.Patches.Where(p => p is not CommitFramePatch).ToArray())
                .ToList();
        }

        List<RenderPatch[]> twin = Drive<SpikeRazorTwin>();
        List<RenderPatch[]> razor = Drive<SpikeRazor>();

        Assert.Equal(twin.Count, razor.Count);
        for (var i = 0; i < twin.Count; i++)
            Assert.Equal(twin[i], razor[i]); // sealed-record equality, order included
    }

    // ── The four capabilities, on the .razor-compiled component itself ────────

    /// <summary>Markup + [Parameter]: the element tree reaches the wire with
    /// the authored camelCase attribute names intact (backgroundColor — the
    /// Razor compiler preserves attribute case) and the supplied Title.</summary>
    [Fact]
    public void Mount_Shape_TreeStylesParameterAndAttaches()
    {
        var (_, mount) = MountFresh<SpikeRazor>();

        // Root: the div → "view" with both authored styles.
        var root = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.ParentId is null);
        Assert.Equal("view", root.NodeType);
        Assert.Equal("#FFEEAA", Assert.Single(mount.Patches.OfType<SetStylePatch>(),
            p => p.NodeId == root.NodeId && p.Property == "backgroundColor").Value);
        Assert.Equal("16", Assert.Single(mount.Patches.OfType<SetStylePatch>(),
            p => p.NodeId == root.NodeId && p.Property == "padding").Value);

        // Title span: fontSize 24, text = the [Parameter] value.
        var title = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(), p => p.Text == Title);
        var titleSpan = Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.NodeId == Assert.IsType<int>(
                Assert.Single(mount.Patches.OfType<CreateNodePatch>(), c => c.NodeId == title.NodeId).ParentId));
        Assert.Equal("text", titleSpan.NodeType);
        Assert.Equal(root.NodeId, titleSpan.ParentId);
        Assert.Equal("24", Assert.Single(mount.Patches.OfType<SetStylePatch>(),
            p => p.NodeId == titleSpan.NodeId && p.Property == "fontSize").Value);

        // The bound input: placeholder + value props, change attach.
        var input = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.NodeType == "input");
        Assert.Equal(root.NodeId, input.ParentId);
        Assert.Equal("Type here...", Assert.Single(mount.Patches.OfType<UpdatePropPatch>(),
            p => p.NodeId == input.NodeId && p.Name == "placeholder").Value);
        Assert.Equal("", Assert.Single(mount.Patches.OfType<UpdatePropPatch>(),
            p => p.NodeId == input.NodeId && p.Name == "value").Value);

        // The button with its click attach and label.
        var label = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(), p => p.Text == "Clear");
        var button = Assert.Single(mount.Patches.OfType<CreateNodePatch>(),
            p => p.NodeId == Assert.IsType<int>(
                Assert.Single(mount.Patches.OfType<CreateNodePatch>(), c => c.NodeId == label.NodeId).ParentId));
        Assert.Equal("button", button.NodeType);

        // Exactly 2 attaches (change + click), exactly 5 creates + 3 text
        // nodes = 8 — the whitespace Markup frames created NOTHING.
        Assert.Equal(2, mount.Patches.OfType<AttachEventPatch>().Count());
        Assert.Equal(8, mount.Patches.OfType<CreateNodePatch>().Count());
        Assert.DoesNotContain(mount.Patches.OfType<ReplaceTextPatch>(),
            p => p.Text.Length > 0 && string.IsNullOrWhiteSpace(p.Text));
    }

    /// <summary>@bind, the full loop: host change dispatch → CreateBinder
    /// handler → _text mutates → re-render → the echo span replaces its text
    /// AND the bound value writes back to the input's host prop.</summary>
    [Fact]
    public void Bind_ChangeDispatch_EchoRerendersAndValueWritesBack()
    {
        var (frames, mount) = MountFresh<SpikeRazor>();
        var input = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.NodeType == "input");
        var echo = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(), p => p.Text == "").NodeId;

        Assert.Equal(0, Dispatch(HandlerOf(mount, "change"), ChangeHello));
        Assert.True(frames.Count >= 2, "expected a synchronous re-render frame");
        var frame = frames[^1];

        var echoed = Assert.Single(frame.Patches.OfType<ReplaceTextPatch>(), p => p.Text == "hello");
        Assert.Equal(echo, echoed.NodeId);
        Assert.Equal("hello", Assert.Single(frame.Patches.OfType<UpdatePropPatch>(),
            p => p.NodeId == input.NodeId && p.Name == "value").Value);
    }

    /// <summary>@onclick: the Clear handler mutates state and the re-render
    /// resets both halves of the bind pair.</summary>
    [Fact]
    public void OnClick_Clear_ResetsValuePropAndEcho()
    {
        var (frames, mount) = MountFresh<SpikeRazor>();
        var input = Assert.Single(mount.Patches.OfType<CreateNodePatch>(), p => p.NodeType == "input");
        var echo = Assert.Single(mount.Patches.OfType<ReplaceTextPatch>(), p => p.Text == "").NodeId;

        Assert.Equal(0, Dispatch(HandlerOf(mount, "change"), ChangeHello));
        Assert.Equal(0, Dispatch(HandlerOf(mount, "click"), ClickArgs));
        var frame = frames[^1];

        Assert.Equal("", Assert.Single(frame.Patches.OfType<UpdatePropPatch>(),
            p => p.NodeId == input.NodeId && p.Name == "value").Value);
        var echoed = Assert.Single(frame.Patches.OfType<ReplaceTextPatch>(), p => p.Text == "");
        Assert.Equal(echo, echoed.NodeId);
    }
}
