using System.Globalization;
using System.Reflection;
using BlazorNative.Components;
using Microsoft.AspNetCore.Components;

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

public sealed class LengthParameterNullabilityPinTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // THE PHASE'S OWN #178 ARGUMENT, CHECKED RATHER THAN ARGUED.
    //
    // Spec §3.3 spends a page on why every length parameter MUST be declared
    // `BnLength?` / `BnAutoLength?` and never bare: `default(BnLength)` is a real
    // zero-POINT length, and `default(BnAutoLength)` reads as `auto` (inner-null
    // encodes auto), so a parameter nobody assigned would silently mean something
    // the author never chose -- #178/#181's exact shape, which on Margin
    // re-centres the node.
    //
    // Nothing checked it. `DefaultOfTheNullable_IsNull_MeaningUnset` above asserts
    // that `default(BnAutoLength?)` is null, which is a C# LANGUAGE AXIOM and
    // cannot fail -- it never looks at a declaration, so changing
    // `[Parameter] public BnAutoLength? Width` to a bare `BnAutoLength` left the
    // whole suite green. That is a vacuous pin guarding the phase's central claim.
    //
    // This sweeps the ASSEMBLY by reflection rather than a hand-written list, so a
    // component added later is covered on the day it is written -- and it is
    // deliberately strict: a length parameter must be EXACTLY `BnLength?` or
    // `BnAutoLength?`, so a bare struct, a `List<BnLength>` or any other shape that
    // merely mentions the type reds and has to be argued for on purpose.
    // ─────────────────────────────────────────────────────────────────────────

    private static bool IsALength(Type t)
        => t == typeof(BnLength) || t == typeof(BnAutoLength);

    /// <summary>Does this type MENTION a length anywhere -- itself, or inside a
    /// generic argument? Deliberately wider than "is a length" so that a parameter
    /// smuggling one through a wrapper still has to answer to the pin.</summary>
    private static bool MentionsALength(Type t)
        => IsALength(t)
        || (Nullable.GetUnderlyingType(t) is { } u && IsALength(u))
        || t.GetGenericArguments().Any(MentionsALength);

    private static bool IsTheApprovedShape(Type t)
        => Nullable.GetUnderlyingType(t) is { } u && IsALength(u);

    /// <summary>Every <c>[Parameter]</c> property in BlazorNative.Components that
    /// mentions a length type, found by reflection over the whole public surface --
    /// including <c>BnLayoutItem</c>, <c>BnLayoutContainer</c>, <c>BnModal</c> and
    /// <c>BnList&lt;TItem&gt;</c>, which are reached, not enumerated.</summary>
    private static List<PropertyInfo> LengthParameters()
        => typeof(BnLayoutItem).Assembly.GetTypes()
            .Where(t => t.IsPublic || t.IsNestedPublic)
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(p => p.IsDefined(typeof(ParameterAttribute), inherit: true))
            .Where(p => MentionsALength(p.PropertyType))
            .DistinctBy(p => (p.DeclaringType!.FullName, p.Name))
            .OrderBy(p => $"{p.DeclaringType!.Name}.{p.Name}", StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void EveryLengthParameter_IsDeclaredNullable()
    {
        var offenders = LengthParameters()
            .Where(p => !IsTheApprovedShape(p.PropertyType))
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name} is declared " +
                         $"{p.PropertyType.Name} — it must be BnLength? or BnAutoLength?")
            .ToList();

        Assert.True(offenders.Count == 0,
            "A length parameter is not nullable, so its `default` is a real value the " +
            "author never chose (spec §3.3, this repo's #178/#181 bug class):" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    // Anti-vacuity, because the pin this one REPLACES was vacuous. A filter that
    // silently matched nothing would make the theory above pass forever; this
    // fails if the sweep stops reaching the surface it claims to cover. The
    // anchors are one property from each of the four declaring types, checked by
    // membership in the reflected set -- not by enumerating that set.
    [Fact]
    public void TheSweep_ActuallyReachesTheLengthSurface()
    {
        var found = LengthParameters()
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}")
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("BnLayoutItem.MinWidth",     found);
        Assert.Contains("BnLayoutContainer.Gap",     found);
        Assert.Contains("BnModal.ContentWidth",      found);
        Assert.Contains("BnList`1.Width",            found);

        // 12 on BnLayoutItem + 2 on BnLayoutContainer + 3 on BnModal + 1 on
        // BnList<TItem>. A floor, not an equality: adding a length parameter is
        // fine and is covered automatically; LOSING the sweep is not.
        Assert.True(found.Count >= 18,
            $"The length sweep found only {found.Count} parameters; it should reach at " +
            "least 18. A sweep that reaches nothing makes the nullability pin vacuous.");
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
