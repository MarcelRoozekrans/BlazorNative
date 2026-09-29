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
    //
    // WHAT THIS DOES NOT COVER (Rule 5):
    //
    // - PUBLIC TYPES ONLY. The sweep reads types that are public or nested
    //   public in BlazorNative.Components. An internal component, or anything
    //   outside that assembly such as a sample-app or third-party component, is
    //   never seen.
    //
    // - PROPERTIES ONLY. Only `[Parameter]` PROPERTIES are swept. A length
    //   passed as a method argument, a field, or a cascading value that is not
    //   a `[Parameter]` is out of reach.
    //
    // - FLOAT "LENGTHS" ARE OUT OF SCOPE BY DESIGN. A parameter the author
    //   declared `float`, such as BnList's Height and ItemHeight, never mentions
    //   BnLength, so the filter does not see it. That is deliberate, spec 3.4:
    //   Height is BnListWindow.Compute's divisor and must be a point value.
    //   BnListSurfaceTests below pins that decision. It also means a NEW length
    //   parameter typed as a bare float is invisible to this pin.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>The length parameters found on 2026-09-26: 12 on BnLayoutItem, 6 on
    /// BnLayoutContainer (Padding, Gap and the four per-edge paddings since 14.2), 3 on
    /// BnModal and 1 on BnList&lt;TItem&gt;. Both facts floor at exactly this, with no
    /// headroom. Adding a length parameter is fine and is covered automatically; losing
    /// one reds, and the floor moves down in the same change that removes it.</summary>
    private const int MeasuredLengthParameters = 22;

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
        => LengthParametersIn(typeof(BnLayoutItem).Assembly.GetTypes());

    /// <summary>The filter behind <see cref="LengthParameters"/>, over any type list, so
    /// the control runs a fixture through the same filter the fact uses.</summary>
    private static List<PropertyInfo> LengthParametersIn(IEnumerable<Type> types)
        => types
            .Where(t => t.IsPublic || t.IsNestedPublic)
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(p => p.IsDefined(typeof(ParameterAttribute), inherit: true))
            .Where(p => MentionsALength(p.PropertyType))
            .DistinctBy(p => (p.DeclaringType!.FullName, p.Name))
            .OrderBy(p => $"{p.DeclaringType!.Name}.{p.Name}", StringComparer.Ordinal)
            .ToList();

    /// <summary>The detector: every swept parameter that is not exactly
    /// <c>BnLength?</c> or <c>BnAutoLength?</c>. Shared by the fact and its control.</summary>
    private static List<string> Offenders(IEnumerable<PropertyInfo> parameters)
        => parameters
            .Where(p => !IsTheApprovedShape(p.PropertyType))
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name} is declared " +
                         $"{p.PropertyType.Name} — it must be BnLength? or BnAutoLength?")
            .ToList();

    [Fact]
    public void EveryLengthParameter_IsDeclaredNullable()
    {
        List<PropertyInfo> swept = LengthParameters();

        // This fact's OWN floor. Before 15.7 it relied on its sibling below for
        // non-vacuity, so run alone, or with the sibling deleted, it passed over
        // an empty sweep.
        Assert.True(swept.Count >= MeasuredLengthParameters,
            $"The nullability pin swept only {swept.Count} length parameters, and it " +
            $"swept {MeasuredLengthParameters} when measured. A sweep that reaches nothing " +
            "passes vacuously.");

        var offenders = Offenders(swept);

        Assert.True(offenders.Count == 0,
            "A length parameter is not nullable, so its `default` is a real value the " +
            "author never chose (spec §3.3, this repo's #178/#181 bug class):" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>A synthetic component for the control. It is never mounted.</summary>
    public sealed class LengthShapeFixture
    {
        [Parameter] public BnLength Bare { get; set; }
        [Parameter] public BnAutoLength BareAuto { get; set; }
        [Parameter] public List<BnLength?>? Wrapped { get; set; }
        [Parameter] public BnLength? Approved { get; set; }
        [Parameter] public BnAutoLength? ApprovedAuto { get; set; }
        [Parameter] public float NotALength { get; set; }
    }

    /// <summary>The positive control, run through the fact's own filter and detector. The
    /// bare and wrapped shapes must be rejected, the two nullable shapes cleared, and a
    /// float never swept at all.</summary>
    [Fact]
    public void TheDetector_RejectsABareLength_AndClearsTheNullableShape()
    {
        List<PropertyInfo> swept = LengthParametersIn([typeof(LengthShapeFixture)]);
        Assert.Equal(
            new[] { "Approved", "ApprovedAuto", "Bare", "BareAuto", "Wrapped" },
            swept.Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());

        var offenders = Offenders(swept);
        Assert.Contains(offenders, o => o.StartsWith("LengthShapeFixture.Bare is declared", StringComparison.Ordinal));
        Assert.Contains(offenders, o => o.StartsWith("LengthShapeFixture.BareAuto is declared", StringComparison.Ordinal));
        Assert.Contains(offenders, o => o.StartsWith("LengthShapeFixture.Wrapped is declared", StringComparison.Ordinal));
        Assert.Equal(3, offenders.Count);
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

        // 12 on BnLayoutItem + 6 on BnLayoutContainer + 3 on BnModal + 1 on
        // BnList<TItem> = 22, measured on 2026-09-26, floored at exactly that with
        // no headroom. The comment said "2 on BnLayoutContainer" and floored at 18
        // until 15.7, which left four parameters of silent headroom since 14.2.
        // A floor, not an equality: adding a length parameter is fine and is
        // covered automatically; LOSING one is not.
        Assert.True(found.Count >= MeasuredLengthParameters,
            $"The length sweep found only {found.Count} parameters; it should reach at " +
            $"least {MeasuredLengthParameters}. A sweep that reaches nothing makes the " +
            "nullability pin vacuous.");
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
