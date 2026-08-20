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
// For those, an explicit parameterless constructor must exist — the #178 fix.
//
// It deliberately does NOT flag a struct with a REQUIRED parameter (one with no
// default, e.g. BridgeHttpRequest's `Url`, NotificationSpec's `Title`). There
// `default(T)`/`new T()` is invalid INPUT by nature — there is no sensible
// zero-arg value to synthesise — and the right treatment is a documented
// contract plus defensive consumers, not a fabricated default. Those fail LOUD
// (a null Method throws at `new HttpMethod(null)`; a null Title is coalesced at
// the serializer sink, #209), which is categorically less dangerous than #178's
// silent-but-wrong image and is handled at those boundaries, not here.
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

    private static IEnumerable<Type> PublicValueTypes =>
        ApiAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsPublic: true, IsValueType: true, IsEnum: false });

    /// <summary>The primary constructor of a record struct: the public instance ctor with
    /// the most parameters. (A record struct also has the implicit parameterless one, which
    /// carries zero parameters, so "most parameters" selects the positional ctor.) Null for
    /// a plain struct with no declared ctors.</summary>
    private static ConstructorInfo? PrimaryCtor(Type t)
        => t.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Where(c => c.GetParameters().Length > 0)
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();

    /// <summary>Whether an EXPLICIT parameterless constructor is declared. For a struct,
    /// <c>GetConstructor(EmptyTypes)</c> is non-null ONLY when one is written in source — the
    /// implicit field-zeroing default is not a real <see cref="ConstructorInfo"/>. This is
    /// what distinguishes CaptureOptions-after-#178 from CaptureOptions-before.</summary>
    private static bool HasExplicitParameterlessCtor(Type t)
        => t.GetConstructor(BindingFlags.Public | BindingFlags.Instance, binder: null,
            Type.EmptyTypes, modifiers: null) is not null;

    private static bool IsNonDefaultDefault(ParameterInfo p)
    {
        if (!p.HasDefaultValue) return false;
        object? dv = p.DefaultValue;
        if (dv is null) return false;                                   // = null is the zero
        Type pt = Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType;
        object? zero = pt.IsValueType ? Activator.CreateInstance(pt) : null;
        return !Equals(dv, zero);                                       // e.g. 2048, 85, "GET"
    }

    /// <summary>
    /// THE STANDING GUARD. Every public record struct that is "all-optional with a
    /// meaningful default" must carry an explicit parameterless constructor, or
    /// <c>new T()</c> silently drops the author's defaults — the #178 shape.
    /// </summary>
    [Fact]
    public void EveryAllOptionalRecordStruct_WithAMeaningfulDefault_HasAnExplicitParameterlessCtor()
    {
        var offenders = new List<string>();
        int examined = 0;

        foreach (Type t in PublicValueTypes)
        {
            ConstructorInfo? primary = PrimaryCtor(t);
            if (primary is null) continue; // plain interop struct, no positional defaults

            ParameterInfo[] ps = primary.GetParameters();
            bool allOptional = ps.All(p => p.HasDefaultValue);
            bool anyMeaningful = ps.Any(IsNonDefaultDefault);

            if (!allOptional || !anyMeaningful) continue; // required field, or all-zero defaults
            examined++;

            if (!HasExplicitParameterlessCtor(t))
            {
                string defaults = string.Join(", ",
                    ps.Where(IsNonDefaultDefault).Select(p => $"{p.Name}={p.DefaultValue}"));
                offenders.Add($"{t.FullName} (defaults: {defaults})");
            }
        }

        // NON-VACUITY: CaptureOptions is the known member of this set, so the sweep must
        // have examined at least it — a filter that matched nothing would pass silently and
        // guard nothing, which is the failure mode #178 itself was.
        Assert.True(examined >= 1,
            "the sweep matched NO all-optional-with-meaningful-default record struct — "
            + "CaptureOptions should qualify. The filter has drifted and now guards nothing.");

        Assert.True(offenders.Count == 0,
            "these public record structs give new T() DIFFERENT values than their declared "
            + "defaults — add an explicit parameterless constructor chaining to the primary "
            + "(the #178 / CaptureOptions fix):\n  " + string.Join("\n  ", offenders));
    }

    // ── Fixtures proving the DETECTION logic, since a real trapped type cannot be added
    //    to the product assemblies without tripping the PublicAPI baseline first ────────

    /// <summary>A trapped shape: all-optional, a meaningful default (7), NO explicit ctor.
    /// The sweep must flag this exact shape when it appears in a product assembly.</summary>
    public readonly record struct TrappedFixture(int Value = 7);

    /// <summary>The fixed shape: same defaults, WITH the explicit parameterless ctor.</summary>
    public readonly record struct FixedFixture(int Value = 7)
    {
        public FixedFixture() : this(Value: 7) { }
    }

    /// <summary>A required-field shape: NOT flagged — `default` is invalid input by nature,
    /// there is no zero-arg value to synthesise (the BridgeHttpRequest / NotificationSpec
    /// category).</summary>
    public readonly record struct RequiredFieldFixture(int Required, int Value = 7);

    [Fact]
    public void TheSweepPredicates_Detect_TheTrap_AndClear_TheFix()
    {
        // The trapped fixture matches the filter (all-optional, meaningful default) and has
        // NO explicit ctor → it is an offender.
        Assert.False(HasExplicitParameterlessCtor(typeof(TrappedFixture)));
        Assert.True(PrimaryCtor(typeof(TrappedFixture))!.GetParameters().All(p => p.HasDefaultValue));
        Assert.True(PrimaryCtor(typeof(TrappedFixture))!.GetParameters().Any(IsNonDefaultDefault));

        // The fixed fixture matches the filter but HAS the ctor → cleared.
        Assert.True(HasExplicitParameterlessCtor(typeof(FixedFixture)));

        // The required-field fixture is excluded before the ctor question — not all-optional.
        Assert.False(PrimaryCtor(typeof(RequiredFieldFixture))!.GetParameters().All(p => p.HasDefaultValue));
    }

    /// <summary>
    /// CaptureOptions specifically — the regression the sweep is built around. Belt to the
    /// sweep's braces: `new CaptureOptions()` must carry the documented 2048/85, not 0/0.
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
