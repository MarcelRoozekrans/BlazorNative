using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BlazorNative.Components;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace BlazorNative.Runtime.Tests;

public sealed class LayoutSurfacePinTests
{
    /// <summary>The 17 parameters that constitute the item surface, by name.</summary>
    internal static readonly string[] ItemParameters =
    {
        "BackgroundColor", "Margin", "AlignSelf", "Grow", "Shrink", "Basis",
        "Width", "Height", "MinWidth", "MaxWidth", "MinHeight", "MaxHeight",
        "Position", "Top", "Right", "Bottom", "Left",
    };

    /// <summary>
    /// Components deliberately NOT deriving from BnLayoutItem, each with a reason.
    ///
    /// <para>Written as an exception ledger rather than left implicit, so the day
    /// a 13th component is added, a false-by-omission surface has to be argued in
    /// writing — exactly how <see cref="BnText"/> ended up with three parameters
    /// for months before this phase: nothing forced the question.</para>
    ///
    /// <para><b>Two entries, both discovered while migrating (13.0 Tasks 7-8),
    /// not planned in advance</b> — the plan guessed a THIRD exception
    /// (<see cref="BnActivityIndicator"/>) that turned out not to be one; see
    /// <see cref="NewlyGranted"/>.</para>
    /// </summary>
    internal static readonly Dictionary<Type, string> AllowedNonLayoutComponents = new()
    {
        [typeof(BnList<>)] =
            "Its EditorRequired Height is float (required — the window arithmetic " +
            "needs a number), while BnLayoutItem.Height is the string? length " +
            "grammar. Same name, two types: Blazor's ComponentProperties.CreateWriters " +
            "collects base AND new-shadowed properties (only override pairs dedupe) " +
            "and throws InvalidOperationException — \"declares more than one " +
            "parameter matching the name 'height'\" — on the first render of a type " +
            "that tried it (see BnList_CannotTakeTheItemBase_BecauseItsHeightIsNarrowedToFloat " +
            "in this file). Renaming or retyping either Height is forbidden this " +
            "phase; reconciling the collision is Phase 13.1's job (it types the " +
            "lengths). See also BnList_KeepsExactlyTheTwoItemNamesTheCollisionStrandsThere.",

        [typeof(BnModal)] =
            "The modal node cannot carry layout styles at all: both shells " +
            "diagnose-and-ignore every SetStyle on it (Android WidgetMapper.kt's " +
            "\"modal\" arm; iOS BnWidgetMapper.swift's equivalent guard), and " +
            "\"modal\" is deliberately absent from measuredNodeTypes on both " +
            "sides — its wire node is a 0-sized, shell-fixed anchor no author-set " +
            "style can ever reach a pixel through. Inheriting the surface would " +
            "advertise 16 parameters that compile, offer IntelliSense, and " +
            "silently do nothing at every layer — the exact accepted-then-" +
            "silently-dropped defect class this phase exists to eliminate. See " +
            "BnModal_DoesNotTakeTheItemSurface_TheModalNodeAcceptsNoStyles in " +
            "BnModalTests.cs for the full citation and the tripwire.",
    };

    [Fact]
    public void BnLayoutItem_DeclaresExactlyTheItemSurface()
    {
        string[] declared = typeof(BnLayoutItem)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.GetCustomAttribute<ParameterAttribute>() is not null)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ItemParameters.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
            declared);
    }

    /// <summary>
    /// PIN 1 — the package-wide guard against a component being written
    /// against <see cref="ComponentBase"/> and never gaining the surface at
    /// all. Scans every non-abstract <see cref="IComponent"/> the package
    /// exports; anything not deriving from <see cref="BnLayoutItem"/> must be
    /// named in <see cref="AllowedNonLayoutComponents"/> with a reason, or
    /// this reds naming it.
    /// </summary>
    [Fact]
    public void EveryComponentInThePackage_DerivesFromBnLayoutItem()
    {
        Type[] offenders = typeof(BnLayoutItem).Assembly
            .GetExportedTypes()
            .Where(t => typeof(IComponent).IsAssignableFrom(t))
            .Where(t => !t.IsAbstract)
            .Where(t => !typeof(BnLayoutItem).IsAssignableFrom(t))
            .Where(t => !AllowedNonLayoutComponents.ContainsKey(t))
            .ToArray();

        Assert.Empty(offenders);
    }

    /// <summary>
    /// PIN 2 — the package-wide guard against a component that DOES derive
    /// from <see cref="BnLayoutItem"/> re-declaring one of the inherited
    /// names with its own <c>[Parameter]</c> (a silent shadow: C# lets a
    /// <c>new</c> property compile, and the base one keeps binding while the
    /// derived one sits dead).
    ///
    /// <para>Filtered to types that DO derive from <see cref="BnLayoutItem"/>,
    /// so <see cref="BnList{TItem}"/> and <see cref="BnModal"/> — which
    /// legitimately declare colliding NAMES precisely because they do NOT
    /// derive — cannot false-positive here; they are constrained instead by
    /// their own size-of-what's-left pins:
    /// <see cref="BnList_KeepsExactlyTheTwoItemNamesTheCollisionStrandsThere"/>
    /// bounds <see cref="BnList{TItem}"/> to exactly the two names its
    /// collision strands there, and <c>BnModalTests.DeclaresExactlyTheDesignedSurface</c>
    /// bounds <see cref="BnModal"/> to its own eight-parameter surface — so
    /// the redeclaration risk in both non-derived exceptions is already
    /// pinned shut, just not by this test.</para>
    /// </summary>
    [Fact]
    public void NoComponent_RedeclaresAnInheritedLayoutParameter()
    {
        string[] surface = ItemParameters.Concat(ContainerParameters).ToArray();

        var offenders = typeof(BnLayoutItem).Assembly
            .GetExportedTypes()
            .Where(t => typeof(BnLayoutItem).IsAssignableFrom(t))
            .Where(t => t != typeof(BnLayoutItem) && t != typeof(BnLayoutContainer))
            .SelectMany(t => t
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => p.GetCustomAttribute<ParameterAttribute>() is not null)
                .Where(p => surface.Contains(p.Name))
                .Select(p => $"{t.Name}.{p.Name}"))
            .ToArray();

        Assert.Empty(offenders);
    }

    /// <summary>The 5 parameters that constitute the container surface, by name.</summary>
    internal static readonly string[] ContainerParameters =
        { "Padding", "Justify", "Align", "Wrap", "Gap" };

    [Fact]
    public void BnLayoutContainer_DeclaresExactlyTheContainerSurface()
    {
        string[] declared = typeof(BnLayoutContainer)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.GetCustomAttribute<ParameterAttribute>() is not null)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ContainerParameters.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
            declared);
    }

    [Fact]
    public void BnLayoutContainer_ExtendsBnLayoutItem()
        => Assert.True(typeof(BnLayoutItem).IsAssignableFrom(typeof(BnLayoutContainer)));

    [Fact]
    public void BnLayoutContainer_DoesNotDeclareChildContent()
        => Assert.Null(typeof(BnLayoutContainer).GetProperty(
            "ChildContent", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

    [Fact]
    public void BnImage_TakesTheItemSurfaceFromTheBase_AndRedeclaresNothing()
    {
        Assert.True(typeof(BnLayoutItem).IsAssignableFrom(typeof(BnImage)));

        string[] redeclared = typeof(BnImage)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.GetCustomAttribute<ParameterAttribute>() is not null)
            .Select(p => p.Name)
            .Intersect(ItemParameters)
            .ToArray();

        Assert.Empty(redeclared);
    }

    public static TheoryData<Type> RazorEmitters => new()
        { typeof(BnCheckbox), typeof(BnPicker), typeof(BnSlider), typeof(BnSwitch) };

    [Theory]
    [MemberData(nameof(RazorEmitters))]
    public void RazorEmitter_TakesTheItemSurfaceFromTheBase(Type component)
    {
        Assert.True(typeof(BnLayoutItem).IsAssignableFrom(component));

        string[] redeclared = component
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.GetCustomAttribute<ParameterAttribute>() is not null)
            .Select(p => p.Name)
            .Intersect(ItemParameters)
            .ToArray();

        Assert.Empty(redeclared);
    }

    [Fact]
    public void BnScroll_IsALayoutItem_NotAContainer()
    {
        Assert.True(typeof(BnLayoutItem).IsAssignableFrom(typeof(BnScroll)));
        Assert.False(typeof(BnLayoutContainer).IsAssignableFrom(typeof(BnScroll)));
    }

    [Fact]
    public void BnFlexPreset_TakesItsSurfaceFromTheContainerBase()
    {
        Assert.True(typeof(BnLayoutContainer).IsAssignableFrom(typeof(BnFlexPreset)));

        string[] redeclared = typeof(BnFlexPreset)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.GetCustomAttribute<ParameterAttribute>() is not null)
            .Select(p => p.Name)
            .Intersect(ItemParameters.Concat(ContainerParameters))
            .ToArray();

        Assert.Empty(redeclared);   // all 22 were declared here before Phase 13.0
    }

    /// <summary>
    /// BnList is the one layout wrapper that CANNOT take the shared item base,
    /// and the reason is a property of Blazor rather than a preference: its
    /// <c>Height</c> is <c>float</c> (required, and the window arithmetic needs a
    /// number — a viewport whose height only the layout engine knows cannot be
    /// turned into a row range), while the shared surface's <c>Height</c> is the
    /// <c>string?</c> length grammar. Two parameters, one name.
    ///
    /// <para>C# would let the derived one shadow the base one with <c>new</c>.
    /// Blazor would not: parameter binding walks the whole hierarchy and treats a
    /// shadowed pair as an AMBIGUOUS parameter, throwing on the first render. So
    /// this is not a migration that was skipped — it is one that does not exist
    /// until either the narrowing goes away or the name does.</para>
    ///
    /// <para>Both halves of that are asserted here rather than written in a
    /// comment: the collision, and the framework behaviour that makes it fatal.
    /// The day <c>BnList.Height</c> stops being narrowed, this test reds and the
    /// migration becomes available.</para>
    /// </summary>
    [Fact]
    public void BnList_CannotTakeTheItemBase_BecauseItsHeightIsNarrowedToFloat()
    {
        Assert.Equal(
            typeof(float),
            typeof(BnList<string>).GetProperty("Height")!.PropertyType);
        Assert.Equal(
            typeof(string),
            typeof(BnLayoutItem).GetProperty("Height")!.PropertyType);

        // …and a `new`-shadowed [Parameter] is not expressible in Blazor.
        var ex = Assert.Throws<InvalidOperationException>(
            () => ParameterView
                .FromDictionary(new Dictionary<string, object?> { ["Height"] = 12f })
                .SetParameterProperties(new ShadowingHeightProbe()));

        Assert.Contains("more than one parameter matching the name", ex.Message, StringComparison.Ordinal);
    }

    private abstract class NarrowableHeightProbe : ComponentBase
    {
        [Parameter] public string? Height { get; set; }
    }

    private sealed class ShadowingHeightProbe : NarrowableHeightProbe
    {
        [Parameter] public new float Height { get; set; }
    }

    /// <summary>The size of what is left behind, pinned so it cannot quietly
    /// grow. BnList keeps exactly two item-surface names — the narrowed
    /// <c>Height</c> the test above explains, and the <c>Width</c> that has to
    /// stay declared alongside it because removing it without the base to supply
    /// it would delete a parameter authors bind. Every OTHER item parameter is
    /// still absent from BnList, as it always was; this is the residue of one
    /// collision, not a component opting out of the shared surface.</summary>
    [Fact]
    public void BnList_KeepsExactlyTheTwoItemNamesTheCollisionStrandsThere()
    {
        string[] declared = typeof(BnList<string>)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.GetCustomAttribute<ParameterAttribute>() is not null)
            .Select(p => p.Name)
            .Intersect(ItemParameters)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "Height", "Width" }, declared);
    }

    /// <summary>
    /// The components that had NO item surface at all before 13.0 Task 8 —
    /// <see cref="BnText"/>, <see cref="BnButton"/> and <see cref="BnInput"/> never
    /// declared any of the 17 names. <see cref="BnActivityIndicator"/> joins too:
    /// Task 8's Step 1 found it is a measured Yoga leaf on both shells — the same
    /// node-creation path and measured-node-types membership as
    /// <c>image</c>/<c>checkbox</c>/<c>switch</c>/<c>slider</c>/<c>picker</c>, none
    /// of which is exempt — so it is a genuine layout participant.
    /// <see cref="BnModal"/> is NOT here: see
    /// <see cref="BnModal_DoesNotTakeTheItemSurface_TheModalNodeAcceptsNoStyles"/>.
    /// </summary>
    public static TheoryData<Type> NewlyGranted => new()
    {
        typeof(BnText), typeof(BnButton), typeof(BnInput), typeof(BnActivityIndicator),
    };

    [Theory]
    [MemberData(nameof(NewlyGranted))]
    public void NewlyGranted_HasTheFullItemSurface(Type component)
    {
        Assert.True(typeof(BnLayoutItem).IsAssignableFrom(component));

        foreach (string name in ItemParameters)
            Assert.NotNull(component.GetProperty(name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy));
    }

    /// <summary>
    /// <b>BnModal is the phase's allowlist exception</b> (13.0 Task 8 fix round 1;
    /// Task 9 records it in <c>AllowedNonLayoutComponents</c> alongside
    /// <see cref="BnList{TItem}"/>, excluded for the unrelated <c>Height</c>
    /// collision). It keeps its OWN single <c>BackgroundColor</c> parameter and
    /// derives from <see cref="ComponentBase"/> — NOT <see cref="BnLayoutItem"/> —
    /// on purpose: both shells diagnose-and-ignore every SetStyle on a <c>modal</c>
    /// node (Android's WidgetMapper.kt <c>handleSetStyle</c> "modal" arm; iOS's
    /// BnWidgetMapper.swift equivalent guard), and <c>modal</c> is deliberately
    /// absent from <c>measuredNodeTypes</c> — its wire node is a 0-sized,
    /// shell-fixed anchor that no author-set style can ever reach a pixel through.
    /// Inheriting <see cref="BnLayoutItem"/> would let <c>&lt;BnModal Width="100"
    /// Margin="8"&gt;</c> compile, offer IntelliSense, and silently do nothing at
    /// every layer — the accepted-then-silently-dropped defect class this whole
    /// phase exists to eliminate, and worse than the original bug, because the
    /// shells' own diagnostic never even fires for it (that diagnostic guards the
    /// SetStyle wire; a BnLayoutItem's item attributes ride the CREATE frame's
    /// element attributes instead, so the ignore-and-log path is never
    /// exercised). This test is the tripwire: the day <c>BnModal</c> starts
    /// deriving from <see cref="BnLayoutItem"/>, it reds, and this comment is
    /// why.
    /// </summary>
    [Fact]
    public void BnModal_DoesNotTakeTheItemSurface_TheModalNodeAcceptsNoStyles()
        => Assert.False(typeof(BnLayoutItem).IsAssignableFrom(typeof(BnModal)));

    [Fact]
    public void BnView_IsALayoutContainer_AndKeepsOnlyDirectionAndChildContent()
    {
        Assert.True(typeof(BnLayoutContainer).IsAssignableFrom(typeof(BnView)));

        string[] own = typeof(BnView)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.GetCustomAttribute<ParameterAttribute>() is not null)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "ChildContent", "Direction" }, own);
    }
}
