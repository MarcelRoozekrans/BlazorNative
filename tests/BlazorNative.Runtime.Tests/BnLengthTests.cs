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
