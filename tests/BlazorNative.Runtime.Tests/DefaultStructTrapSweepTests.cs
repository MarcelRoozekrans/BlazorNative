using System.Reflection;
using BlazorNative.Components;
using BlazorNative.Core;
using BlazorNative.Device;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// DefaultStructTrapSweepTests — #178 / #181, criterion Q5.
//
// THE TRAP, stated once. A C# `record struct` gets its primary-constructor
// parameter defaults ONLY through the primary constructor. `default(T)` calls no
// constructor at all, and `new T()` binds to the implicit field-zeroing struct
// constructor unless an EXPLICIT parameterless one is declared. So a struct the
// author gave meaningful defaults —
//
//     readonly record struct CaptureOptions(int MaxDimension = 2048, int Quality = 85)
//
// silently yields MaxDimension=0, Quality=0 from `default(CaptureOptions)` AND
// from `new CaptureOptions()`. #178 was exactly this: a full-resolution,
// quality-0 (all-255 DQT, visibly posterised) photo on a real device.
//
// #178 and #181 were the two found by READING the Gate B public-API baseline.
// Both are fixed and pinned elsewhere (CameraBridgeTests, RegistrationTests).
// #181 asked for the missing third step — "treat as a pattern to look for …
// sweep the remaining public structs" — and this file is that sweep, made a
// STANDING reflection guard rather than a one-time read, so a NEW public struct
// that reintroduces the trap reds here instead of on a device.
//
// WHAT THIS PINS, PRECISELY. The dangerous shape is a record struct where EVERY
// primary-ctor parameter has a default (so `new T()` is meant to be a valid,
// fully-defaulted value) AND at least one of those defaults is non-zero/non-null
// (so the zero-init a missing explicit ctor produces is WRONG, not merely empty).
// For those, `new T()` must yield the SAME VALUE as the primary constructor called
// with its declared defaults. That is compared as a value, not inferred from the
// presence of an explicit parameterless ctor: until 15.7 the sweep only checked
// that such a ctor EXISTED, so `public T() : this(Value: 0) { }` passed while
// reproducing #178 exactly. WrongValueFixture below is that case, and it reds.
//
// It deliberately does NOT flag a struct with a REQUIRED parameter (one with no
// default, e.g. BridgeHttpRequest's `Url`, NotificationSpec's `Title`). There
// `default(T)`/`new T()` is invalid INPUT by nature — there is no sensible
// zero-arg value to synthesise — and the right treatment is a documented
// contract plus defensive consumers, not a fabricated default. Those fail LOUD
// (a null Method throws at `new HttpMethod(null)`; a null Title is coalesced at
// the serializer sink, #209), which is categorically less dangerous than #178's
// silent-but-wrong image and is handled at those boundaries, not here.
//
// WHAT THIS DOES NOT COVER (Rule 5):
//
// - THE FOUR ASSEMBLIES ONLY. Core, Runtime, Device and Components are swept.
//   A public struct in Renderer, Http, Testing or the sample app is never seen,
//   and neither is an app's own struct. Within those four, only a type that is
//   public all the way out is swept: a public struct nested in an internal type
//   is not API and is skipped, which IsPublicAllTheWayOut decides and
//   TheVisibilityFilter_SeesNestedPublic_AndSkipsAHiddenChain controls.
//
// - THE PRIMARY-CONSTRUCTOR HEURISTIC. "The primary ctor" is the public
//   instance ctor with the MOST parameters. For a positional record struct that
//   is the positional ctor. A record struct that also declares a longer
//   secondary ctor would have THAT taken as primary, and the sweep would judge
//   the wrong parameter list. Ties are broken by reflection order, which is not
//   specified. No such struct exists in the four assemblies today, and nothing
//   here detects one arriving.
//
// - RECORD STRUCTS ONLY, IN EFFECT. The value comparison uses Equals, which is
//   field-wise for a record struct and for a plain struct without an override.
//   A plain struct that overrides Equals loosely could hide a disagreement. And
//   a struct whose defaults live in field initialisers or properties, rather
//   than in ctor parameter defaults, has no "declared default" this sweep can
//   read, so it is never examined at all.
//
// - default(T) IS NOT GUARDED. `default(T)` calls no constructor, ever, so it
//   yields zeroes for every struct and no declaration can change that. What
//   protects consumers from it is the nullable-null API shape, for example
//   `CapturePhotoAsync(CaptureOptions? = null)`, which this file does not pin.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class DefaultStructTrapSweepTests
{
    /// <summary>The four product assemblies whose public value types face app code.</summary>
    private static IEnumerable<Assembly> ApiAssemblies =>
    [
        typeof(IMobileBridge).Assembly,        // BlazorNative.Core
        typeof(BlazorNativePage).Assembly,     // BlazorNative.Runtime
        typeof(IGeolocation).Assembly,         // BlazorNative.Device
        typeof(BnLength).Assembly,             // BlazorNative.Components (13.1)
    ];

    /// <summary>The value types the sweep runs over: every non-enum struct in the four
    /// assemblies that is public all the way out, nested ones included.</summary>
    private static IEnumerable<Type> PublicValueTypes =>
        PublicValueTypesIn(ApiAssemblies.SelectMany(a => a.GetTypes()));

    /// <summary>The visibility and kind filter, shared by the fact and its controls so
    /// the control exercises the filter the fact uses rather than a copy of it.</summary>
    internal static IEnumerable<Type> PublicValueTypesIn(IEnumerable<Type> candidates)
        => candidates.Where(t => t is { IsValueType: true, IsEnum: false }
                                 && IsPublicAllTheWayOut(t));

    /// <summary>True when the type is public and so is every type enclosing it. A struct
    /// nested in a public type is API, and <c>IsPublic</c> alone is false for it, which
    /// is why the sweep missed nested structs until 15.7. A public struct nested in an
    /// internal type is not API, and <c>IsNestedPublic</c> alone would wrongly admit it.</summary>
    internal static bool IsPublicAllTheWayOut(Type t)
    {
        for (Type? cur = t; cur is not null; cur = cur.DeclaringType)
        {
            if (!(cur.IsPublic || cur.IsNestedPublic)) return false;
        }
        return true;
    }

    /// <summary>The primary constructor of a record struct: the public instance ctor with
    /// the most parameters. (A record struct also has the implicit parameterless one, which
    /// carries zero parameters, so "most parameters" selects the positional ctor.) Null for
    /// a plain struct with no declared ctors.</summary>
    private static ConstructorInfo? PrimaryCtor(Type t)
        => t.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Where(c => c.GetParameters().Length > 0)
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();

    private static bool IsNonDefaultDefault(ParameterInfo p)
    {
        if (!p.HasDefaultValue) return false;
        object? dv = p.DefaultValue;
        if (dv is null) return false;                                   // = null is the zero
        Type pt = Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType;
        object? zero = pt.IsValueType ? Activator.CreateInstance(pt) : null;
        return !Equals(dv, zero);                                       // e.g. 2048, 85, "GET"
    }

    /// <summary>The sweep's detector. For every all-optional record struct with a meaningful
    /// default, <c>new T()</c> must equal the primary constructor called with its declared
    /// defaults — compared as VALUES, so an explicit ctor that chains to the wrong values is
    /// caught, not just a missing one.
    ///
    /// <c>Activator.CreateInstance(t)</c> stands in for <c>new T()</c>. For a struct it
    /// invokes an EXPLICIT parameterless constructor when one is declared, which .NET has
    /// done since Core 3.0, and zero-initialises when none is, exactly as <c>new T()</c>
    /// does. That is proven here rather than assumed, by
    /// <c>Activator_RunsTheExplicitParameterlessCtor_AndZeroInitsWithoutOne</c>.</summary>
    internal static (int Examined, List<string> Offenders) Offenders(IEnumerable<Type> types)
    {
        var offenders = new List<string>();
        int examined = 0;
        foreach (Type t in types)
        {
            ConstructorInfo? primary = PrimaryCtor(t);
            if (primary is null) continue; // plain interop struct, no positional defaults
            ParameterInfo[] ps = primary.GetParameters();
            if (!ps.All(p => p.HasDefaultValue) || !ps.Any(IsNonDefaultDefault))
                continue; // required field, or all-zero defaults
            examined++;
            object declared = primary.Invoke(ps.Select(p => p.DefaultValue).ToArray());
            object viaNew = Activator.CreateInstance(t)!;   // what `new T()` yields
            if (!Equals(declared, viaNew))
                offenders.Add($"{t.FullName}: new() = {viaNew}, declared defaults = {declared}");
        }
        return (examined, offenders);
    }

    /// <summary>
    /// THE STANDING GUARD. For every public record struct that is "all-optional with a
    /// meaningful default", <c>new T()</c> must equal the struct built from its declared
    /// defaults, or it silently drops the author's defaults — the #178 shape.
    /// </summary>
    [Fact]
    public void EveryAllOptionalRecordStruct_WithAMeaningfulDefault_NewYieldsTheDeclaredDefaults()
    {
        var (examined, offenders) = Offenders(PublicValueTypes);

        // NON-VACUITY. Measured on 2026-09-26: the sweep examines 1 struct, CaptureOptions,
        // floored at exactly that with no headroom. A filter that matched nothing would pass
        // silently and guard nothing, which is the failure mode #178 itself was. A new
        // qualifying struct raises the count and is fine; losing CaptureOptions reds here,
        // and the anchor below says which one went missing.
        Assert.True(examined >= 1,
            $"the sweep examined {examined} all-optional-with-meaningful-default record "
            + "structs, and it examined 1 when measured. CaptureOptions should qualify. The "
            + "filter has drifted and now guards nothing.");
        Assert.True(
            Offenders([typeof(CaptureOptions)]).Examined == 1
                && PublicValueTypes.Contains(typeof(CaptureOptions)),
            "CaptureOptions, the anchor of this sweep, is no longer both swept and examined. "
            + "Either the visibility filter or the all-optional filter has stopped seeing "
            + "it. Re-point the anchor deliberately if it was renamed.");

        Assert.True(offenders.Count == 0,
            "these public record structs give new T() DIFFERENT values than their declared "
            + "defaults — add an explicit parameterless constructor chaining to the primary "
            + "with the SAME defaults (the #178 / CaptureOptions fix):\n  "
            + string.Join("\n  ", offenders));
    }

    // ── Fixtures proving the DETECTION logic, since a real trapped type cannot be added
    //    to the product assemblies without tripping the PublicAPI baseline first ────────

    /// <summary>A trapped shape: all-optional, a meaningful default (7), NO explicit ctor.
    /// The sweep must flag this exact shape when it appears in a product assembly.</summary>
    public readonly record struct TrappedFixture(int Value = 7);

    /// <summary>The fixed shape: same defaults, WITH the explicit parameterless ctor
    /// chaining to the same value.</summary>
    public readonly record struct FixedFixture(int Value = 7)
    {
        public FixedFixture() : this(Value: 7) { }
    }

    /// <summary>The case the pre-15.7 existence check missed: an explicit parameterless
    /// ctor EXISTS, but it chains to the wrong value, so <c>new()</c> still yields 0.</summary>
    public readonly record struct WrongValueFixture(int Value = 7)
    {
        public WrongValueFixture() : this(Value: 0) { }
    }

    /// <summary>A required-field shape: NOT flagged — `default` is invalid input by nature,
    /// there is no zero-arg value to synthesise (the BridgeHttpRequest / NotificationSpec
    /// category).</summary>
    public readonly record struct RequiredFieldFixture(int Required, int Value = 7);

    /// <summary>A public host, so its nested trapped struct is public all the way out and
    /// must be swept. Before 15.7 the filter was <c>IsPublic</c>, which is false for any
    /// nested type, so this shape was invisible in a product assembly.</summary>
    public static class PublicHost
    {
        public readonly record struct NestedTrappedFixture(int Value = 7);
    }

    /// <summary>An internal host: its nested struct is declared public but is not API,
    /// so the visibility filter must skip it.</summary>
    internal static class InternalHost
    {
        public readonly record struct HiddenTrappedFixture(int Value = 7);
    }

    /// <summary>The claim <see cref="Offenders"/> rests on, measured rather than assumed:
    /// <c>Activator.CreateInstance</c> on a struct runs a declared parameterless ctor, and
    /// zero-initialises when there is none, which is exactly what <c>new T()</c> does.</summary>
    [Fact]
    public void Activator_RunsTheExplicitParameterlessCtor_AndZeroInitsWithoutOne()
    {
        Assert.Equal(7, ((FixedFixture)Activator.CreateInstance(typeof(FixedFixture))!).Value);
        Assert.Equal(new FixedFixture().Value,
            ((FixedFixture)Activator.CreateInstance(typeof(FixedFixture))!).Value);

        Assert.Equal(0, ((WrongValueFixture)Activator.CreateInstance(typeof(WrongValueFixture))!).Value);
        Assert.Equal(0, ((TrappedFixture)Activator.CreateInstance(typeof(TrappedFixture))!).Value);
        Assert.Equal(new TrappedFixture().Value,
            ((TrappedFixture)Activator.CreateInstance(typeof(TrappedFixture))!).Value);
    }

    /// <summary>The positive control, driven through the fact's own detector.</summary>
    [Fact]
    public void TheSweep_Detects_TheTrap_AndTheWrongValue_AndClears_TheFix()
    {
        var (examined, offenders) = Offenders(
        [
            typeof(TrappedFixture),
            typeof(FixedFixture),
            typeof(WrongValueFixture),
            typeof(RequiredFieldFixture),
        ]);

        // Three qualify. The required-field fixture is excluded before any comparison.
        Assert.Equal(3, examined);

        Assert.Contains(offenders, o => o.StartsWith(typeof(TrappedFixture).FullName + ":", StringComparison.Ordinal));
        Assert.Contains(offenders, o => o.StartsWith(typeof(WrongValueFixture).FullName + ":", StringComparison.Ordinal));
        Assert.DoesNotContain(offenders, o => o.StartsWith(typeof(FixedFixture).FullName + ":", StringComparison.Ordinal));
        Assert.DoesNotContain(offenders, o => o.StartsWith(typeof(RequiredFieldFixture).FullName + ":", StringComparison.Ordinal));
        Assert.Equal(2, offenders.Count);
    }

    /// <summary>The visibility filter, driven with the same helper the fact uses. A trapped
    /// struct nested in a public type is swept and reported; one nested in an internal type
    /// is skipped.</summary>
    [Fact]
    public void TheVisibilityFilter_SeesNestedPublic_AndSkipsAHiddenChain()
    {
        Type nested = typeof(PublicHost.NestedTrappedFixture);
        Type hidden = typeof(InternalHost.HiddenTrappedFixture);

        var swept = PublicValueTypesIn([nested, hidden, typeof(TrappedFixture)]).ToList();
        Assert.Contains(nested, swept);
        Assert.Contains(typeof(TrappedFixture), swept);
        Assert.DoesNotContain(hidden, swept);

        var (_, offenders) = Offenders(swept);
        Assert.Contains(offenders, o => o.StartsWith(nested.FullName + ":", StringComparison.Ordinal));
    }

    /// <summary>
    /// CaptureOptions specifically — the regression the sweep is built around. Since 15.7
    /// the sweep itself compares <c>new CaptureOptions()</c> against the declared defaults
    /// by value, so this fact is implied by it. It stays as a named anchor: it states the
    /// documented numbers in the open, and it pins that <c>default(T)</c> still bypasses
    /// the explicit ctor, which the sweep does not check.
    /// </summary>
    [Fact]
    public void NewCaptureOptions_CarriesTheDocumentedDefaults_NotZero()
    {
        var opts = new CaptureOptions();
        Assert.Equal(2048, opts.MaxDimension);
        Assert.Equal(85, opts.Quality);

        // And the trap the guard is about is still real: default(T) bypasses even the
        // explicit ctor. Asserting it documents WHY consumers must take the nullable-null
        // path (Camera.CapturePhotoAsync(CaptureOptions? = null)) rather than `= default`.
        var raw = default(CaptureOptions);
        Assert.Equal(0, raw.MaxDimension);
        Assert.Equal(0, raw.Quality);
    }
}
