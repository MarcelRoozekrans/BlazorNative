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
}
