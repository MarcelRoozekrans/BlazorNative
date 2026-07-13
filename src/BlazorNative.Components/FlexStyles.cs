using System.Runtime.CompilerServices;

namespace BlazorNative.Components;

// ─────────────────────────────────────────────────────────────────────────────
// FlexStyles — Phase 6.1 Task 1.1 (design §"The style surface", decision 4).
//
// "Typed C# params, strings on the wire." The public flex surface is enums and
// numerics — compile-time safety for the app author — and they stringify at
// BuildRenderTree onto the EXISTING SetStyle wire (patch kind 6). Zero ABI
// change: still 9 exports + the 72-byte bridge; the shells still parse strings.
//
// The strings ARE the contract: each shell (Kotlin's YogaLayout, iOS's
// BnYogaLayout.mm) maps these exact CSS-cased words to a Yoga setter, and the
// two mappings are held to the same table. Never emit a .NET ToString() of an
// enum here — "RowReverse" is not a word any shell parses.
//
// ── THE VALUE GRAMMAR IS NORMATIVE AND LIVES IN ONE PLACE ────────────────────
// docs/plans/2026-07-13-phase-6.1-design.md §"Style value grammar (normative)".
// Both shell parsers are written FROM that section; it is not restated here,
// because three copies of a grammar is how the two parsers drift. The two
// things worth knowing at THIS file's altitude:
//   • .NET never emits a unit suffix. Every string-valued param below reaches
//     the wire as a bare number ("12" — density-independent units), a percent
//     ("50%") or "auto". "12dp"/"12px"/"12sp" are NOT in the 6.1 grammar.
//   • A NULL value on the wire means "reset to the Yoga default".
//
// Type names are prefixed Flex* on purpose (FlexAlign, not Align): these ship
// on nuget.org in M8 and a bare `Align`/`Wrap`/`Position` in a library's root
// namespace collides with app-side types. The PARAM names on BnView stay short
// (Align="…", Wrap="…") — that is the ergonomic surface.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Main-axis direction of a flex container (<c>flexDirection</c>).
/// Yoga's default is <see cref="Column"/> — which is why an un-styled tree
/// still lays out as today's vertical stack.</summary>
public enum FlexDirection
{
    /// <summary><c>"row"</c></summary>
    Row,
    /// <summary><c>"column"</c> — Yoga's default.</summary>
    Column,
    /// <summary><c>"row-reverse"</c></summary>
    RowReverse,
    /// <summary><c>"column-reverse"</c></summary>
    ColumnReverse,
}

/// <summary>Main-axis distribution (<c>justifyContent</c>).</summary>
public enum FlexJustify
{
    /// <summary><c>"flex-start"</c> — Yoga's default.</summary>
    FlexStart,
    /// <summary><c>"center"</c></summary>
    Center,
    /// <summary><c>"flex-end"</c></summary>
    FlexEnd,
    /// <summary><c>"space-between"</c></summary>
    SpaceBetween,
    /// <summary><c>"space-around"</c></summary>
    SpaceAround,
    /// <summary><c>"space-evenly"</c></summary>
    SpaceEvenly,
}

/// <summary>Cross-axis alignment — <c>alignItems</c> on a container,
/// <c>alignSelf</c> on a child.</summary>
public enum FlexAlign
{
    /// <summary><c>"auto"</c> — inherit the parent's alignItems (alignSelf only).</summary>
    Auto,
    /// <summary><c>"flex-start"</c></summary>
    FlexStart,
    /// <summary><c>"center"</c></summary>
    Center,
    /// <summary><c>"flex-end"</c></summary>
    FlexEnd,
    /// <summary><c>"stretch"</c> — Yoga's default alignItems.</summary>
    Stretch,
    /// <summary><c>"baseline"</c></summary>
    Baseline,
}

/// <summary>Line wrapping of a flex container (<c>flexWrap</c>).</summary>
public enum FlexWrap
{
    /// <summary><c>"nowrap"</c> — Yoga's default. Note the CSS spelling: ONE word.</summary>
    NoWrap,
    /// <summary><c>"wrap"</c></summary>
    Wrap,
    /// <summary><c>"wrap-reverse"</c></summary>
    WrapReverse,
}

/// <summary>Positioning mode (<c>position</c>). <see cref="Absolute"/> takes the
/// node out of flow; <c>Top/Right/Bottom/Left</c> then place it against the
/// padding box of its parent.</summary>
public enum FlexPosition
{
    /// <summary><c>"relative"</c> — Yoga's default (in flow).</summary>
    Relative,
    /// <summary><c>"absolute"</c></summary>
    Absolute,
}

/// <summary>Enum → wire string. The nullable overloads are what
/// <see cref="BnView"/> calls: a null param yields a null value, which
/// <c>RenderTreeBuilder.AddAttribute</c> omits entirely — no attribute, no
/// patch (the un-styled invariant).</summary>
public static class FlexStyleValues
{
    /// <summary>CSS-cased wire value for <paramref name="value"/>.</summary>
    public static string ToStyleValue(this FlexDirection value) => value switch
    {
        FlexDirection.Row => "row",
        FlexDirection.Column => "column",
        FlexDirection.RowReverse => "row-reverse",
        FlexDirection.ColumnReverse => "column-reverse",
        _ => throw Undefined(value),
    };

    /// <summary>CSS-cased wire value for <paramref name="value"/>.</summary>
    public static string ToStyleValue(this FlexJustify value) => value switch
    {
        FlexJustify.FlexStart => "flex-start",
        FlexJustify.Center => "center",
        FlexJustify.FlexEnd => "flex-end",
        FlexJustify.SpaceBetween => "space-between",
        FlexJustify.SpaceAround => "space-around",
        FlexJustify.SpaceEvenly => "space-evenly",
        _ => throw Undefined(value),
    };

    /// <summary>CSS-cased wire value for <paramref name="value"/>.</summary>
    public static string ToStyleValue(this FlexAlign value) => value switch
    {
        FlexAlign.Auto => "auto",
        FlexAlign.FlexStart => "flex-start",
        FlexAlign.Center => "center",
        FlexAlign.FlexEnd => "flex-end",
        FlexAlign.Stretch => "stretch",
        FlexAlign.Baseline => "baseline",
        _ => throw Undefined(value),
    };

    /// <summary>CSS-cased wire value for <paramref name="value"/>.</summary>
    public static string ToStyleValue(this FlexWrap value) => value switch
    {
        FlexWrap.NoWrap => "nowrap",
        FlexWrap.Wrap => "wrap",
        FlexWrap.WrapReverse => "wrap-reverse",
        _ => throw Undefined(value),
    };

    /// <summary>CSS-cased wire value for <paramref name="value"/>.</summary>
    public static string ToStyleValue(this FlexPosition value) => value switch
    {
        FlexPosition.Relative => "relative",
        FlexPosition.Absolute => "absolute",
        _ => throw Undefined(value),
    };

    // ── Nullable lifts: null param → null value → no attribute → no patch ─────

    /// <inheritdoc cref="ToStyleValue(FlexDirection)"/>
    public static string? ToStyleValue(this FlexDirection? value)
        => value is { } v ? v.ToStyleValue() : null;

    /// <inheritdoc cref="ToStyleValue(FlexJustify)"/>
    public static string? ToStyleValue(this FlexJustify? value)
        => value is { } v ? v.ToStyleValue() : null;

    /// <inheritdoc cref="ToStyleValue(FlexAlign)"/>
    public static string? ToStyleValue(this FlexAlign? value)
        => value is { } v ? v.ToStyleValue() : null;

    /// <inheritdoc cref="ToStyleValue(FlexWrap)"/>
    public static string? ToStyleValue(this FlexWrap? value)
        => value is { } v ? v.ToStyleValue() : null;

    /// <inheritdoc cref="ToStyleValue(FlexPosition)"/>
    public static string? ToStyleValue(this FlexPosition? value)
        => value is { } v ? v.ToStyleValue() : null;

    /// <summary>Numeric → wire string, INVARIANTLY (the shells parse with a
    /// C/Java float parser: a Dutch locale must never put <c>"1,5"</c> on the
    /// wire). Null stays null — no attribute, no patch.</summary>
    public static string? ToStyleValue(this float? value)
        => value?.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The "enum value outside the declared set" guard (a cast int).
    /// <paramref name="paramName"/> is captured from the CALL SITE, so the
    /// exception names the caller's parameter rather than this helper's.</summary>
    private static ArgumentOutOfRangeException Undefined<T>(
        T value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : struct, Enum
        => new(paramName, value, $"undefined {typeof(T).Name} value — no wire word exists for it");
}
