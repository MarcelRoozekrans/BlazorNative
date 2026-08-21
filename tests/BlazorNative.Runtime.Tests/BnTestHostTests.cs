using BlazorNative.Components;
using BlazorNative.Testing;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// BnTestHostTests — the consumer test harness (#25), tested the way a consumer
// would use it: against REAL Bn* components, never against hand-built patches.
//
// That choice is the point. The harness exists so an app author can assert what
// their page rendered without touching NativeRenderer or the NOT-API patch model,
// and a suite that fed it synthetic patches would prove the projection arithmetic
// while proving nothing about whether it works on the components people write.
//
// The load-bearing case is child ORDER. Blazor's render queue does not create
// children in sibling order — a chained child component renders after its later
// siblings — so `CreateNodePatch` carries its own placement and the projection
// has to replay the shells' insert algorithm. A consumer who re-derived it by
// walking creates in patch order would get a green test over a wrong tree, which
// is the failure this package exists to make impossible.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class BnTestHostTests
{
    // ── Fixtures: ordinary components, exactly what a consumer writes ────────

    private sealed class SimplePage : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenComponent<BnColumn>(0);
            b.AddComponentParameter(1, nameof(BnColumn.Gap), (BnLength)8f);   // Gap is BnLength? on the flex surface
            b.AddComponentParameter(2, nameof(BnColumn.ChildContent), (RenderFragment)(cb =>
            {
                cb.OpenComponent<BnText>(0);
                cb.AddComponentParameter(1, nameof(BnText.Text), "Hello");
                cb.CloseComponent();

                cb.OpenComponent<BnText>(2);
                cb.AddComponentParameter(3, nameof(BnText.Text), "World");
                cb.CloseComponent();
            }));
            b.CloseComponent();
        }
    }

    private sealed class CounterPage : ComponentBase
    {
        private int _count;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenComponent<BnColumn>(0);
            b.AddComponentParameter(1, nameof(BnColumn.ChildContent), (RenderFragment)(cb =>
            {
                cb.OpenComponent<BnText>(0);
                cb.AddComponentParameter(1, nameof(BnText.Text), $"count {_count}");
                cb.CloseComponent();

                cb.OpenComponent<BnButton>(2);
                cb.AddComponentParameter(3, nameof(BnButton.Label), "Tap");
                cb.AddComponentParameter(4, nameof(BnButton.OnClick),
                    EventCallback.Factory.Create<Microsoft.AspNetCore.Components.Web.MouseEventArgs>(
                        this, _ => _count++));
                cb.CloseComponent();
            }));
            b.CloseComponent();
        }
    }

    private sealed class InputPage : ComponentBase
    {
        public string Value = "";

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenComponent<BnInput>(0);
            b.AddComponentParameter(1, nameof(BnInput.Value), Value);
            b.AddComponentParameter(2, nameof(BnInput.ValueChanged),
                EventCallback.Factory.Create<string>(this, v => Value = v));
            b.CloseComponent();
        }
    }

    private sealed class NoButtonPage : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenComponent<BnButton>(0);
            b.AddComponentParameter(1, nameof(BnButton.Label), "Inert");
            b.CloseComponent();   // deliberately NO OnClick
        }
    }

    // ── The tree ─────────────────────────────────────────────────────────────

    [Fact]
    public void MountProjectsTheRenderedTree_WithoutExposingAPatch()
    {
        using var host = BnTestHost.Mount<SimplePage>();

        BnTestNode root = host.Tree.Root;
        Assert.Equal("view", root.NodeType);          // BnColumn is a flex view
        Assert.Equal(2, root.Children.Count);
        Assert.All(root.Children, c => Assert.Equal("text", c.NodeType));
        Assert.True(host.FrameCount > 0);
    }

    [Fact]
    public void ChildrenAreInFinalSiblingOrder_NotPatchOrder()
    {
        // THE ONE THAT MATTERS. If the projection appended creates in patch order
        // rather than honouring InsertIndex, this is where it shows — and it would
        // show as "World, Hello", i.e. a test that passes over a wrong tree.
        using var host = BnTestHost.Mount<SimplePage>();

        Assert.Equal(["Hello", "World"], host.Tree.Root.Children.Select(c => c.Text));
    }

    [Fact]
    public void StylesAreProjectedPerNode()
    {
        using var host = BnTestHost.Mount<SimplePage>();

        // BnColumn's Gap=8 rides the style wire, invariant-formatted.
        Assert.Equal("8", host.Tree.Root.Styles["gap"]);
        Assert.Equal("column", host.Tree.Root.Styles["flexDirection"]);
    }

    [Fact]
    public void FindTextLocatesANode_AndNamesTheAlternativesWhenItCannot()
    {
        using var host = BnTestHost.Mount<SimplePage>();

        Assert.Equal("text", host.Tree.FindText("World").NodeType);

        // The failure message is the feature: a test that cannot find its text
        // should say what WAS rendered, not just that the lookup failed.
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => host.Tree.FindText("Nope"));
        Assert.Contains("Hello", ex.Message);
        Assert.Contains("World", ex.Message);
    }

    [Fact]
    public void FindAllReturnsEveryNodeOfAType()
    {
        using var host = BnTestHost.Mount<SimplePage>();

        Assert.Equal(2, host.Tree.FindAll("text").Count);
        Assert.Empty(host.Tree.FindAll("button"));
    }

    // ── Driving events ───────────────────────────────────────────────────────

    [Fact]
    public async Task ClickReRendersAndTheTreeReflectsIt()
    {
        // The whole loop a consumer wants: render, act, assert the NEW tree.
        using var host = BnTestHost.Mount<CounterPage>();
        Assert.Equal("count 0", host.Tree.FindAll("text")[0].Text);

        await host.ClickAsync(host.Tree.FindAll("button")[0]);

        Assert.Equal("count 1", host.Tree.FindAll("text")[0].Text);
    }

    [Fact]
    public async Task ChangeDrivesATwoWayBind()
    {
        using var host = BnTestHost.Mount<InputPage>();

        await host.ChangeAsync(host.Tree.FindAll("input")[0], "typed");

        Assert.Equal("typed", host.Tree.FindAll("input")[0].Props["value"]);
    }

    [Fact]
    public async Task DispatchingWithNoHandlerNamesTheRealMistake()
    {
        // A BnButton with no OnClick emits no attach at all, so "nothing happened"
        // is indistinguishable from "the click did nothing" unless the harness
        // says which. Nearly always the actual bug.
        using var host = BnTestHost.Mount<NoButtonPage>();

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.ClickAsync(host.Tree.Root));

        Assert.Contains("no 'click' handler", ex.Message);
        Assert.Contains("(none)", ex.Message);
    }

    [Fact]
    public void EventsAreProjected()
    {
        using var host = BnTestHost.Mount<CounterPage>();

        Assert.Contains("click", host.Tree.FindAll("button")[0].Events);
        Assert.Empty(host.Tree.FindAll("text")[0].Events);
    }

    // ── The seam that makes it usable for a real page ────────────────────────

    [Fact]
    public void ConfigureServicesReachesTheComponentsContainer()
    {
        // A real page injects things. Without this the harness would only work for
        // components that need nothing, which is not the interesting case.
        var probe = new object();
        using var host = BnTestHost.Mount<SimplePage>(
            configureServices: s => s.AddSingleton(probe));

        Assert.NotNull(host.Tree.Root);   // mounting against a customised container works
    }

    [Fact]
    public void AnEmptyComponentFailsLoudlyRatherThanVacuously()
    {
        // A silently-empty tree makes every later assertion pass while proving
        // nothing — the house rule about pins that cannot see their subject.
        using var host = BnTestHost.Mount<EmptyPage>();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => host.Tree.Root);
        Assert.Contains("no root node", ex.Message);
    }

    private sealed class EmptyPage : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b) { }
    }

    // ── Phase 13.4: the harness must not be WEAKER than the device ───────────
    //
    // Both cases below are the same failure shape — the harness answered where a
    // real shell would have answered differently, so a test went green over an app
    // that does not work. The third case of that shape (#280's headline, the
    // encoder gate) is NOT here; the block above the collapse tests says why.

    /// <summary>A `checkbox` element with a lone text child — the wire shape the two
    /// shells project DIFFERENTLY. Written as a raw element rather than through
    /// BnCheckbox because BnCheckbox is a childless leaf: the divergence is about the
    /// wire, and the wire is what a shell sees.</summary>
    private sealed class CheckboxWithTextProbe : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "checkbox");
            b.AddContent(1, "hello");
            b.CloseElement();
        }
    }

    /// <summary>#280 sub-case. TextBearing matched iOS's collapse predicate only;
    /// Android's is broader (TextView &amp;&amp; !ViewGroup), so checkbox and switch
    /// absorb a text child there. The harness must not encode one shell's rule as
    /// the contract — a consumer asserting <c>node.Text</c> on a checkbox was being
    /// told something true of iOS and silently wrong for Android.</summary>
    [Fact]
    public void TheTextCollapse_FollowsTheNamedShell_NotIosAlways()
    {
        using BnTestHost ios = BnTestHost.Mount<CheckboxWithTextProbe>(shell: BnShell.Ios);
        using BnTestHost android = BnTestHost.Mount<CheckboxWithTextProbe>(shell: BnShell.Android);

        // iOS: checkbox is NOT text-bearing, so the text child stays a node.
        Assert.NotEmpty(ios.Tree.Root.Children);
        Assert.Null(ios.Tree.Root.Text);

        // Android: checkbox IS text-bearing, so it absorbs the child.
        Assert.Empty(android.Tree.Root.Children);
        Assert.Equal("hello", android.Tree.Root.Text);
    }

    /// <summary>The default is iOS, so every call written before the shell could be
    /// named keeps the answer it already had. Naming a shell is an ADDITION.</summary>
    [Fact]
    public void TheDefaultShell_IsIos_SoExistingCallsAreUnchanged()
    {
        using BnTestHost implicitShell = BnTestHost.Mount<CheckboxWithTextProbe>();
        using BnTestHost explicitIos = BnTestHost.Mount<CheckboxWithTextProbe>(shell: BnShell.Ios);

        Assert.Equal(explicitIos.Tree.Root.Children.Count, implicitShell.Tree.Root.Children.Count);
        Assert.Equal(explicitIos.Tree.Root.Text, implicitShell.Tree.Root.Text);
    }

    /// <summary>Throws from BuildRenderTree — the fault class StrictErrors = true
    /// exists to surface, and the one a consumer's expected-throw test produces.</summary>
    /// <remarks>The sentinel is [Inject]ed so the CONTAINER constructs it during the
    /// mount: a factory-registered singleton is tracked by the provider as a
    /// disposable it owns, so its flag answers "was the container torn down?"
    /// without the test needing a handle on the provider BnTestHost hides.</remarks>
    private sealed class ThrowingProbe : ComponentBase
    {
        [Inject] public DisposalSentinel Sentinel { get; set; } = default!;

        protected override void BuildRenderTree(RenderTreeBuilder b)
            => throw new InvalidOperationException("the mount threw, deliberately");
    }

    private sealed class DisposalSentinel : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    /// <summary>#281. Mount builds a ServiceProvider and a renderer before mounting.
    /// If the mount throws — the path StrictErrors = true exists to produce — both
    /// leaked, and a suite full of expected-throw mounts leaked one container per
    /// assertion.</summary>
    [Fact]
    public void AThrowingMount_DisposesTheProvider_RatherThanLeakingIt()
    {
        DisposalSentinel? sentinel = null;

        Assert.Throws<InvalidOperationException>(
            () => BnTestHost.Mount<ThrowingProbe>(
                configureServices: s => s.AddSingleton(_ => sentinel = new DisposalSentinel())));

        Assert.NotNull(sentinel);
        Assert.True(sentinel.Disposed,
            "BnTestHost.Mount leaked its ServiceProvider when the mount threw.");
    }
}
