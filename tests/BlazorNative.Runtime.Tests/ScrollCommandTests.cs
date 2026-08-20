using System.Globalization;
using System.Runtime.InteropServices;
using BlazorNative.Components;
using BlazorNative.Renderer;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// #256 — programmatic scroll: the COMMAND path, end to end on the .NET side.
//
// The feature's correctness argument is that a command is NOT state, so these
// tests are built around the ways that distinction gets lost rather than around
// the happy path:
//
//   1. A REPEAT MUST SURVIVE THE DIFF. Blazor emits an attribute edit only when
//      the value CHANGES. Two identical scroll requests are two commands; a
//      naive implementation collapses them into one, and fails only on the
//      SECOND call — the worst shape a bug can have. This is what the nonce
//      exists to prevent and it is asserted first.
//   2. THE NONCE MUST NOT REACH THE SHELL. It is a .NET-side trick for making a
//      diff fire. A shell that could observe it could come to depend on it.
//   3. ORDER MUST HOLD WHERE IT IS PROMISED. "Append rows, then scroll to the
//      end" is only true if the ScrollTo lands after the rows' CreateNodes in
//      the SAME frame. That is guaranteed for AutoScrollToEnd and for calls made
//      inside one render cycle — and NOT for a call made from outside one, which
//      is pinned here as a fact rather than left to be discovered.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class ScrollCommandTests
{
    // ── The vocabulary that must agree across the seam ───────────────────────

    [Fact]
    public void TheCommandVocabulary_IsTheSameOnBothSidesOfTheSeam()
    {
        // BnScroll authors the value; NativeRenderer parses it. They live in
        // different assemblies and neither can see the other's constant, so a
        // rename on one side would produce a command the renderer logs and
        // drops: a scroll that silently never happens, with no build error.
        Assert.Equal(NativeRenderer.ScrollToAttributeName, BnScroll.ScrollToAttributeName);
        Assert.Equal(NativeRenderer.ScrollToEndTarget, BnScroll.ScrollToEndTarget);
    }

    [Fact]
    public void TheCommandAttribute_IsNotAStyleName()
    {
        // If `scrollTo` ever entered the style partition it would ride the
        // SetStyle wire and reach the shells' Yoga/visual tables as a name
        // neither implements — dropped silently on the device.
        Assert.DoesNotContain(NativeRenderer.ScrollToAttributeName, NativeRenderer.StyleAttributes);
    }

    // ── Parsing: the renderer's half ─────────────────────────────────────────

    [Theory]
    [InlineData("end#1", true, 0f)]
    [InlineData("end#274", true, 0f)]
    [InlineData("0#1", false, 0f)]
    [InlineData("123.5#7", false, 123.5f)]
    [InlineData("-40.25#2", false, -40.25f)]   // iOS rubber-banding is a real negative
    public void Parse_AcceptsTheGrammar_AndDropsTheNonce(string value, bool expectedToEnd, float expectedOffset)
    {
        Assert.True(NativeRenderer.TryParseScrollToCommand(value, out bool toEnd, out float offset));
        Assert.Equal(expectedToEnd, toEnd);
        Assert.Equal(expectedOffset, offset);
    }

    [Theory]
    [InlineData(null)]          // an absent attribute is not a command
    [InlineData("")]
    [InlineData("end")]         // no nonce: a repeat could never be seen
    [InlineData("123.5")]
    [InlineData("#7")]          // empty target
    [InlineData("bottom#7")]    // not a target we define
    [InlineData("12,5#7")]      // comma decimal — a locale leak, not a number
    public void Parse_RejectsAnythingElse(string? value)
    {
        Assert.False(NativeRenderer.TryParseScrollToCommand(value, out _, out _));
    }

    [Fact]
    public void Parse_IsInvariantCulture_NotTheAmbientOne()
    {
        // The shells format and parse offsets invariantly. If this side followed
        // the ambient culture, a device in a comma-decimal locale would send
        // "123,5" one way and fail to read "123.5" the other.
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("nl-NL");
            Assert.True(NativeRenderer.TryParseScrollToCommand("123.5#1", out _, out float offset));
            Assert.Equal(123.5f, offset);
            Assert.False(NativeRenderer.TryParseScrollToCommand("123,5#1", out _, out _));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // ── The end-to-end path through a real render ────────────────────────────

    private sealed class ScrollHost : ComponentBase
    {
        public static ScrollHost? Last;

        public BnScroll? Scroll;
        public int Rows;
        public bool AutoScroll;

        public ScrollHost() => Last = this;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenComponent<BnScroll>(0);
            b.AddComponentParameter(1, nameof(BnScroll.Height), (BnAutoLength)200f);
            b.AddComponentParameter(2, nameof(BnScroll.AutoScrollToEnd), AutoScroll);
            b.AddComponentParameter(3, nameof(BnScroll.ChildContent), (RenderFragment)(cb =>
            {
                for (int i = 0; i < Rows; i++)
                {
                    cb.OpenElement(i * 2, "text");
                    cb.AddContent(i * 2 + 1, $"row {i}");
                    cb.CloseElement();
                }
            }));
            b.AddComponentReferenceCapture(4, r => Scroll = (BnScroll)r);
            b.CloseComponent();
        }

        public Task Rerender() => InvokeAsync(StateHasChanged);
    }

    private sealed record Harness(NativeRenderer Renderer, ScrollHost Host, List<RenderFrame> Frames) : IDisposable
    {
        public void Dispose() => Renderer.Dispose();

        public ScrollToPatch[] Commands()
            => Frames.SelectMany(f => f.Patches.OfType<ScrollToPatch>()).ToArray();
    }

    private static async Task<Harness> MountAsync()
    {
        var services = new ServiceCollection().AddBlazorNativeRenderer().BuildServiceProvider();
        var renderer = new NativeRenderer(services) { StrictErrors = true };
        var frames = new List<RenderFrame>();
        renderer.Frames += (f, _) => { frames.Add(f); return ValueTask.CompletedTask; };

        await renderer.MountAsync<ScrollHost>();
        return new Harness(renderer, ScrollHost.Last!, frames);
    }

    [Fact]
    public async Task ScrollToEndAsync_TwiceInARow_EmitsTwoCommands()
    {
        // THE REGRESSION THIS FEATURE IS BUILT AROUND — see note 1 in the header.
        using Harness h = await MountAsync();
        h.Frames.Clear();

        await h.Renderer.Dispatcher.InvokeAsync(() => h.Host.Scroll!.ScrollToEndAsync().AsTask());
        await h.Renderer.Dispatcher.InvokeAsync(() => h.Host.Scroll!.ScrollToEndAsync().AsTask());

        ScrollToPatch[] commands = h.Commands();
        Assert.Equal(2, commands.Length);
        Assert.All(commands, c => Assert.True(c.ToEnd));
        Assert.All(commands, c => Assert.Equal(0f, c.Offset));   // ignored in end mode
    }

    [Fact]
    public async Task ScrollToAsync_CarriesTheOffset_AndNoNonceReachesTheWire()
    {
        using Harness h = await MountAsync();
        h.Frames.Clear();

        await h.Renderer.Dispatcher.InvokeAsync(() => h.Host.Scroll!.ScrollToAsync(240.5f).AsTask());

        ScrollToPatch command = Assert.Single(h.Commands());
        Assert.False(command.ToEnd);
        Assert.Equal(240.5f, command.Offset);

        // Note 2: the nonce is a .NET-side diff trick. It must not appear on the
        // wire in any form — and in particular the command must never have been
        // routed to the PROP wire, which is where an unrecognised name lands.
        Assert.DoesNotContain(h.Frames.SelectMany(f => f.Patches),
            p => p is UpdatePropPatch u && u.Name == NativeRenderer.ScrollToAttributeName);
    }

    [Fact]
    public async Task AnUnscrolledBnScroll_EmitsNoCommandAtAll()
    {
        // The un-styled invariant, for commands: a BnScroll nobody has scrolled
        // has the same wire shape it had before #256 existed.
        using Harness h = await MountAsync();
        h.Frames.Clear();

        await h.Host.Rerender();

        Assert.Empty(h.Commands());
        Assert.DoesNotContain(h.Frames.SelectMany(f => f.Patches),
            p => p is UpdatePropPatch u && u.Name == NativeRenderer.ScrollToAttributeName);
    }

    [Fact]
    public async Task AutoScrollToEnd_ShipsTheCommandInTheSAMEFrameAsTheRowsItFollows()
    {
        // Note 3, the guaranteed half — and the place where the obvious guess is
        // WRONG, so it is worth being precise about what is being promised.
        //
        // The guarantee is FRAME-level, not patch-index-level. The command is an
        // attribute of the SCROLL element and the rows are that element's
        // children, so Blazor's diff necessarily emits the attribute edit BEFORE
        // it steps into the children: within the frame, the ScrollTo comes
        // first. An earlier draft of this test asserted the opposite and failed,
        // which is the only reason the distinction is written down here.
        //
        // Patch index is irrelevant because a shell cannot honour a ScrollTo at
        // the moment it decodes it: "the end" is content height, a Yoga result
        // that does not exist until the batch has been applied and laid out. So
        // both shells queue the command and apply it AFTER layout of the frame
        // it arrived in — which makes "same frame" exactly the right and only
        // needed guarantee, and is pinned shell-side by their own scroll tests.
        using Harness h = await MountAsync();
        h.Frames.Clear();

        await h.Renderer.Dispatcher.InvokeAsync(() =>
        {
            h.Host.AutoScroll = true;
            h.Host.Rows = 3;
            return h.Host.Rerender();
        });

        RenderFrame frame = Assert.Single(h.Frames, f => f.Patches.OfType<ScrollToPatch>().Any());
        Assert.Contains(frame.Patches, p => p is CreateNodePatch);
        Assert.Single(frame.Patches.OfType<ScrollToPatch>());
    }

    [Fact]
    public async Task AutoScrollToEnd_ReArmsOnEveryRender_NotOnlyWhenTheContentGrew()
    {
        // Stated as a fact rather than left to be discovered: this component
        // cannot see that its content changed, only that it re-rendered. The
        // parameter's own documentation says so, and an author who needs
        // "only when items were added" is told to call ScrollToEndAsync instead.
        using Harness h = await MountAsync();

        await h.Renderer.Dispatcher.InvokeAsync(() =>
        {
            h.Host.AutoScroll = true;
            return h.Host.Rerender();
        });
        h.Frames.Clear();

        await h.Host.Rerender();     // nothing about the CONTENT changed
        await h.Host.Rerender();

        Assert.Equal(2, h.Commands().Length);
    }

    [Fact]
    public async Task AnExplicitCallOutsideARenderCycle_ShipsInItsOwnFrame()
    {
        // Note 3, the honest half — the ordering caveat, pinned so it stays true
        // to what BnScroll's own documentation promises.
        //
        // Blazor renders immediately for a StateHasChanged made outside a batch,
        // so a scroll requested outside a render cycle is its own frame. If the
        // content changes in a LATER frame, "end" is evaluated by the shell
        // against the content it has NOW — i.e. before the new rows. Inside an
        // event handler (the normal case) Blazor coalesces both into one batch
        // and the order is the one the author wrote.
        using Harness h = await MountAsync();
        h.Frames.Clear();

        await h.Renderer.Dispatcher.InvokeAsync(() => h.Host.Scroll!.ScrollToEndAsync().AsTask());
        await h.Renderer.Dispatcher.InvokeAsync(() =>
        {
            h.Host.Rows = 3;
            return h.Host.Rerender();
        });

        RenderFrame commandFrame = Assert.Single(h.Frames, f => f.Patches.OfType<ScrollToPatch>().Any());
        Assert.DoesNotContain(commandFrame.Patches, p => p is CreateNodePatch);
        Assert.Contains(h.Frames, f => f.Patches.OfType<CreateNodePatch>().Any()
                                    && !f.Patches.OfType<ScrollToPatch>().Any());
    }

    [Fact]
    public async Task TheCommand_SurvivesTheEncoderOntoTheWire()
    {
        // The renderer's patch and the 48-byte struct the shells decode are two
        // different things. A mode/offset field mix-up would look perfect on
        // this side and scroll to 0 on the device.
        using Harness h = await MountAsync();
        h.Frames.Clear();

        await h.Renderer.Dispatcher.InvokeAsync(() => h.Host.Scroll!.ScrollToAsync(42f).AsTask());
        RenderFrame frame = Assert.Single(h.Frames, f => f.Patches.OfType<ScrollToPatch>().Any());

        using FrameArena arena = FrameArena.Rent();
        BlazorNativeFrame native = FrameEncoder.Encode(frame, arena);

        BlazorNativePatch? scroll = null;
        for (int i = 0; i < native.PatchCount; i++)
        {
            var p = Marshal.PtrToStructure<BlazorNativePatch>(
                native.Patches + i * Marshal.SizeOf<BlazorNativePatch>());
            if (p.Kind == BlazorNativePatchKind.ScrollTo) scroll = p;
        }

        Assert.NotNull(scroll);
        Assert.Equal(0, scroll!.Value.AuxInt);                                 // 0 = to an offset
        Assert.Equal("42", Marshal.PtrToStringUTF8(scroll.Value.PropValue));
        Assert.Equal(IntPtr.Zero, scroll.Value.PropName);
        Assert.Equal(IntPtr.Zero, scroll.Value.Text);
    }

    [Fact]
    public async Task ScrollToEnd_CarriesNoOffsetOnTheWire()
    {
        // NULL rather than a zero: a shell that reads an offset in end mode gets
        // nothing, not a plausible-looking 0 it might scroll to.
        using Harness h = await MountAsync();
        h.Frames.Clear();

        await h.Renderer.Dispatcher.InvokeAsync(() => h.Host.Scroll!.ScrollToEndAsync().AsTask());
        RenderFrame frame = Assert.Single(h.Frames, f => f.Patches.OfType<ScrollToPatch>().Any());

        using FrameArena arena = FrameArena.Rent();
        BlazorNativeFrame native = FrameEncoder.Encode(frame, arena);

        BlazorNativePatch? scroll = null;
        for (int i = 0; i < native.PatchCount; i++)
        {
            var p = Marshal.PtrToStructure<BlazorNativePatch>(
                native.Patches + i * Marshal.SizeOf<BlazorNativePatch>());
            if (p.Kind == BlazorNativePatchKind.ScrollTo) scroll = p;
        }

        Assert.NotNull(scroll);
        Assert.Equal(1, scroll!.Value.AuxInt);                 // 1 = to the end
        Assert.Equal(IntPtr.Zero, scroll.Value.PropValue);
    }
}
