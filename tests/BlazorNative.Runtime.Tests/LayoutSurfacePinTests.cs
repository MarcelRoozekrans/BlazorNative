using System;
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
