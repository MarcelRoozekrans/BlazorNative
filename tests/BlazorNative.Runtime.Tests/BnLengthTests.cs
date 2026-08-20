using System.Globalization;
using System.Reflection;
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

public sealed class BnLengthGuardTests
{
    // R1. The shells parse with a C/Java float parser. A comma decimal separator
    // on the wire is rejected as "not a number, a percentage or 'auto'" -- and it
    // is invisible on any English dev machine, which is why this is a test and not
    // a comment.
    [Theory]
    [InlineData("nl-NL")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void ToStyleValue_IsInvariant_UnderACommaDecimalCulture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("1.5", ((BnLength)1.5f).ToStyleValue());
            Assert.Equal("1.5%", BnLength.Percent(1.5f).ToStyleValue());
            Assert.Equal("1.5", ((BnAutoLength)1.5f).ToStyleValue());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // THE MECHANISM, pinned. Width="12px" is a compile error because no conversion
    // from string exists -- not because anything checks the text. Add one and the
    // whole phase silently reverts to runtime log-and-ignore, with every other test
    // still green. Proven by compiling a probe during design (spec 6.1); this guards
    // the regression.
    [Theory]
    [InlineData(typeof(BnLength))]
    [InlineData(typeof(BnAutoLength))]
    public void NoConversionFromString_Exists(Type t)
    {
        var fromString = t
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name is "op_Implicit" or "op_Explicit")
            .Where(m => m.GetParameters() is [{ ParameterType: var p }] && p == typeof(string))
            .ToList();

        Assert.True(fromString.Count == 0,
            $"{t.Name} has a conversion from string. That single member turns every " +
            "malformed length back into a runtime log line and undoes phase 13.1.");
    }

    [Fact]
    public void NullableExtension_KeepsNullAsNull_SoNoAttributeIsEmitted()
    {
        Assert.Null(((BnLength?)null).ToStyleValue());
        Assert.Null(((BnAutoLength?)null).ToStyleValue());
        Assert.Equal("100", ((BnLength?)(BnLength)100f).ToStyleValue());
        Assert.Equal("auto", ((BnAutoLength?)BnAutoLength.Auto).ToStyleValue());
    }
}

public sealed class BnListSurfaceTests
{
    // Spec 3.4. BnList stays allowlisted because Height is BnListWindow.Compute's
    // divisor and must be a point value -- BnAutoLength cannot promise one. Width
    // has no such constraint and IS typed. This pins both halves so a later reader
    // does not "finish the job" and break the virtualization arithmetic.
    [Fact]
    public void Width_IsTyped_ButHeightAndItemHeightStayFloat()
    {
        var t = typeof(BnList<string>);
        Assert.Equal(typeof(BnLength?), t.GetProperty("Width")!.PropertyType);
        Assert.Equal(typeof(float),     t.GetProperty("Height")!.PropertyType);
        Assert.Equal(typeof(float),     t.GetProperty("ItemHeight")!.PropertyType);
    }
}
