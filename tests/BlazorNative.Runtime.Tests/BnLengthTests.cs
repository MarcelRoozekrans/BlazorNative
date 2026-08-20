using System.Globalization;
using BlazorNative.Components;

namespace BlazorNative.Runtime.Tests;

public sealed class BnLengthTests
{
    [Fact]
    public void Points_FormatsAsABareNumber()
        => Assert.Equal("100", ((BnLength)100f).ToStyleValue());

    [Fact]
    public void Percent_FormatsWithATrailingSign()
        => Assert.Equal("50%", BnLength.Percent(50f).ToStyleValue());

    [Fact]
    public void DoubleLiteral_Converts()
        => Assert.Equal("12.5", ((BnLength)12.5).ToStyleValue());

    [Fact]
    public void Negative_IsRepresentable_AndStaysShellEnforced()
        => Assert.Equal("-8", ((BnLength)(-8f)).ToStyleValue());
}

public sealed class BnAutoLengthTests
{
    [Fact]
    public void Auto_FormatsAsTheWord()
        => Assert.Equal("auto", BnAutoLength.Auto.ToStyleValue());

    [Fact]
    public void Points_FormatsAsABareNumber()
        => Assert.Equal("100", ((BnAutoLength)100f).ToStyleValue());

    [Fact]
    public void Percent_FormatsWithATrailingSign()
        => Assert.Equal("50%", BnAutoLength.Percent(50f).ToStyleValue());

    [Fact]
    public void ABnLength_ConvertsIn()
        => Assert.Equal("25%", ((BnAutoLength)BnLength.Percent(25f)).ToStyleValue());

    // THE TRAP, pinned. default(BnAutoLength) has a null inner Length, which this
    // type encodes as `auto` -- NOT as unset. That is exactly #178's shape: a
    // struct's zero-value silently meaning something the author never chose. On
    // Margin, `auto` re-centres the node. The guarantee is that parameters are
    // BnAutoLength?, so `default` is the OUTER null.
    [Fact]
    public void DefaultOfTheBareStruct_IsAuto_WhichIsWhyParametersAreNullable()
        => Assert.Equal("auto", default(BnAutoLength).ToStyleValue());

    [Fact]
    public void DefaultOfTheNullable_IsNull_MeaningUnset()
    {
        Assert.Null(default(BnAutoLength?));
        Assert.Null(default(BnLength?));
    }
}
