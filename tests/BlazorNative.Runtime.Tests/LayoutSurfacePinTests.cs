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
    /// declared any of the 17 names; <see cref="BnModal"/> declared one
    /// (<c>BackgroundColor</c>) with no way to set the other sixteen.
    /// <see cref="BnActivityIndicator"/> joins too: Task 8's Step 1 found it is a
    /// measured Yoga leaf on both shells — the same node-creation path and
    /// measured-node-types membership as <c>image</c>/<c>checkbox</c>/<c>switch</c>/
    /// <c>slider</c>/<c>picker</c>, none of which is exempt — so it is a genuine
    /// layout participant, not the phase's allowlist exception.
    /// </summary>
    public static TheoryData<Type> NewlyGranted => new()
    {
        typeof(BnText), typeof(BnButton), typeof(BnInput), typeof(BnModal),
        typeof(BnActivityIndicator),
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
