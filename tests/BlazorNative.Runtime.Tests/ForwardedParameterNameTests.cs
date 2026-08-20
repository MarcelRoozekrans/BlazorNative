using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BlazorNative.Components;
using BlazorNative.Renderer;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorNative.Runtime.Tests;

/// <summary>
/// Wrappers forward parameters by NAME. <c>AddComponentParameter</c> matches
/// those names as strings at RUNTIME, so a rename that updated a declaration and
/// missed a forward fails silently in production rather than loudly at compile
/// time — the wrapper renders, the target keeps its default, and nothing is red.
///
/// <para><c>nameof</c> keeps the forwards honest against their OWN declarations.
/// It cannot see the TARGET at all: <c>nameof(Width)</c> inside
/// <c>BnLayoutItem.ForwardItemParameters</c> compiles whether or not the
/// component being rendered has a <c>Width</c> parameter. These tests close that
/// half — first reflectively (the target declares every forwarded name), then
/// behaviourally (a mounted wrapper's values actually arrive on the target
/// instance).</para>
/// </summary>
public sealed class ForwardedParameterNameTests
{
    // ── Half 1: the target DECLARES every name the base forwards ─────────────

    // BnView is the live forward TARGET: BnFlexPreset renders it through
    // ForwardItemParameters/ForwardContainerParameters, so every name those
    // helpers write has to be a parameter on BnView or the value is dropped at
    // runtime with nothing red.
    //
    // BnScroll is NOT a forward target and never has been. BnList renders it,
    // but from markup (<BnScroll Width="@…" Height="@…" …>) and
    // binds two parameters by hand, not by name at runtime — and BnList does
    // not derive from BnLayoutItem at all, so it has no Forward* helper to
    // call. The row is kept because BnScroll is the other multi-child item
    // component a future wrapper would forward into, and the assertion it
    // makes — that BnScroll really does carry the whole item surface it
    // inherits — is worth having on its own.
    [Theory]
    [InlineData(typeof(BnView))]
    [InlineData(typeof(BnScroll))]
    public void ForwardTarget_DeclaresEveryItemParameter(Type target)
    {
        foreach (string name in LayoutSurfacePinTests.ItemParameters)
        {
            PropertyInfo? p = target.GetProperty(name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

            Assert.True(p is not null, $"{target.Name} has no parameter named '{name}'");
            Assert.NotNull(p!.GetCustomAttribute<ParameterAttribute>());
        }
    }

    [Fact]
    public void ForwardTarget_DeclaresEveryContainerParameter()
    {
        foreach (string name in LayoutSurfacePinTests.ContainerParameters)
        {
            PropertyInfo? p = typeof(BnView).GetProperty(name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

            Assert.True(p is not null, $"BnView has no parameter named '{name}'");
            Assert.NotNull(p!.GetCustomAttribute<ParameterAttribute>());
        }
    }

    // ── Half 2: the values actually LAND on the target instance ──────────────
    //
    // The reflective half above is a green light over a forward that was simply
    // deleted: BnView still declares Width, so it still passes while
    // <BnRow Width="100"> does nothing. This half mounts the real thing and
    // reads the target BnView back through a component reference capture, so a
    // dropped line in ForwardItemParameters / ForwardContainerParameters shows
    // up as a property that is null when it should not be.

    /// <summary>The whole shared surface, with a distinct value per parameter so
    /// that a forward wired to the WRONG name lands somewhere the assertion can
    /// see, instead of landing a value indistinguishable from the right one.</summary>
    private static readonly (string Name, object Value)[] FullSurface =
    {
        // Phase 13.1: the twelve lengths are BnLength/BnAutoLength, not strings.
        // The values are BOXED into an object here and Blazor's parameter writer
        // unboxes with a cast, so a plain "5" (or a bare 15f for Padding) would
        // not merely read oddly — it would throw at the first render.
        // BnLayoutItem — 17
        ("BackgroundColor", "#010203"),
        ("Margin",          (BnAutoLength)1f),
        ("AlignSelf",       FlexAlign.Center),
        ("Grow",            2f),
        ("Shrink",          3f),
        ("Basis",           (BnAutoLength)4f),
        ("Width",           (BnAutoLength)5f),
        ("Height",          (BnAutoLength)6f),
        ("MinWidth",        (BnLength)7f),
        ("MaxWidth",        (BnLength)8f),
        ("MinHeight",       (BnLength)9f),
        ("MaxHeight",       (BnLength)10f),
        ("Position",        FlexPosition.Absolute),
        ("Top",             (BnLength)11f),
        ("Right",           (BnLength)12f),
        ("Bottom",          (BnLength)13f),
        ("Left",            (BnLength)14f),
        // BnLayoutContainer — 5
        ("Padding",         (BnLength)15f),
        ("Justify",         FlexJustify.SpaceEvenly),
        ("Align",           FlexAlign.Baseline),
        ("Wrap",            FlexWrap.WrapReverse),
        ("Gap",             (BnLength)16f),
    };

    [Fact]
    public void FullSurface_CoversExactlyTheTwoSharedSurfaces()
        => Assert.Equal(
            LayoutSurfacePinTests.ItemParameters
                .Concat(LayoutSurfacePinTests.ContainerParameters)
                .OrderBy(n => n, StringComparer.Ordinal),
            FullSurface.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));

    [Fact]
    public async Task ForwardingAWrapper_LandsEveryValueOnTheTargetInstance()
    {
        await using Harness h = await MountAsync();

        BnView target = h.Target;
        foreach ((string name, object value) in FullSurface)
            Assert.Equal(value, Read(target, name));
    }

    /// <summary>
    /// THE ASYMMETRY, PINNED. A COMPONENT forward keeps its nulls, and must:
    /// Blazor writes only the parameters a render SUPPLIES, so a forward that
    /// skipped its nulls would leave the target holding the value from the
    /// previous render. A cleared parameter has to reach the target as a null,
    /// not as an absence.
    /// <para>
    /// The mutation that reddens this: wrap the calls in
    /// <c>BnLayoutItem.ForwardItemParameters</c> in <c>if (x is not null)</c>
    /// and every property below keeps its first-render value.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ClearingEveryParameter_ForwardsTheNullsRatherThanOmittingThem()
    {
        await using Harness h = await MountAsync();
        Assert.Equal("#010203", Read(h.Target, "BackgroundColor")); // the first render landed

        await h.Host.ClearAsync();

        BnView target = h.Target;
        foreach ((string name, _) in FullSurface)
            Assert.True(Read(target, name) is null,
                $"'{name}' kept a stale value — the forward omitted its null");
    }

    /// <summary>
    /// The other side of the same coin, and the reason the element splat and the
    /// component forward are NOT interchangeable: the ELEMENT splat drops nulls.
    /// An element attribute with no value must be ABSENT from the wire (the
    /// un-styled invariant), whereas a component parameter with no value must be
    /// PRESENT and null. Both spell "unset"; they are implemented oppositely.
    /// Anyone tempted to harmonise them reddens this first.
    /// </summary>
    [Fact]
    public void TheElementSplat_DropsNulls_UnlikeTheComponentForward()
    {
        var probe = new SplatProbe();
        Assert.Empty(probe.Attributes);

        probe.SetWidth((BnAutoLength)5f);
        Assert.Equal(new[] { "width" }, probe.Attributes.Keys);
    }

    private static object? Read(BnView target, string name)
        => typeof(BnView)
            .GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy)!
            .GetValue(target);

    // ── Probes ────────────────────────────────────────────────────────────────

    /// <summary>A wrapper of exactly the shape BnFlexPreset now is: it declares
    /// nothing of its own and forwards the two shared surfaces into a BnView.</summary>
    private sealed class ForwardProbe : BnLayoutContainer
    {
        public BnView? Target { get; private set; }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenComponent<BnView>(0);
            ForwardItemParameters(b);
            ForwardContainerParameters(b);
            b.AddComponentReferenceCapture(300, r => Target = (BnView)r);
            b.CloseComponent();
        }
    }

    private sealed class SplatProbe : BnLayoutItem
    {
        public IReadOnlyDictionary<string, object?> Attributes => ItemAttributes;

        // Set from inside the component: assigning a [Parameter] from the test
        // body would be BL0005, and the analyzer is right — this probe is
        // standing in for the framework, so it does the framework's job here.
        public void SetWidth(BnAutoLength? width) => Width = width;
    }

    /// <summary>Supplies the whole surface, then supplies it again as nulls.
    /// Supplying nulls rather than OMITTING the parameters is the point: an
    /// omitted parameter is never written, so omitting would test nothing.</summary>
    private sealed class ProbeHost : ComponentBase
    {
        public static ProbeHost? Last;
        public ForwardProbe? Probe;
        private bool _cleared;

        public ProbeHost() => Last = this;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenComponent<ForwardProbe>(0);

            int seq = 1;
            foreach ((string name, object value) in FullSurface)
                b.AddComponentParameter(seq++, name, _cleared ? null : value);

            b.AddComponentReferenceCapture(100, r => Probe = (ForwardProbe)r);
            b.CloseComponent();
        }

        public Task ClearAsync() => InvokeAsync(() => { _cleared = true; StateHasChanged(); });
    }

    private sealed class Harness : IAsyncDisposable
    {
        public required NativeRenderer Renderer { get; init; }
        public required ProbeHost Host { get; init; }

        public BnView Target => Host.Probe!.Target!;

        public ValueTask DisposeAsync()
        {
            Renderer.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private static async Task<Harness> MountAsync()
    {
        ServiceProvider services = new ServiceCollection()
            .AddBlazorNativeRenderer().BuildServiceProvider();
        var renderer = new NativeRenderer(services) { StrictErrors = true };
        await renderer.MountAsync<ProbeHost>();
        return new Harness { Renderer = renderer, Host = ProbeHost.Last! };
    }
}
