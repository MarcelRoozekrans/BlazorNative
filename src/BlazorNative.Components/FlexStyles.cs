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
// The value grammar for the string-valued params (Width/Basis/Gap/Top/…) is
// shared with the shells: bare number ("12" = dp) | "12dp"/"12px" | "50%" |
// "auto". A NULL value on the wire means "reset to the Yoga default".
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
public enum Justify
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
public enum Align
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
public enum Wrap
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
public enum Position
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
    public static string ToStyleValue(this Justify value) => value switch
    {
        Justify.FlexStart => "flex-start",
        Justify.Center => "center",
        Justify.FlexEnd => "flex-end",
        Justify.SpaceBetween => "space-between",
        Justify.SpaceAround => "space-around",
        Justify.SpaceEvenly => "space-evenly",
        _ => throw Undefined(value),
    };

    /// <summary>CSS-cased wire value for <paramref name="value"/>.</summary>
    public static string ToStyleValue(this Align value) => value switch
    {
        Align.Auto => "auto",
        Align.FlexStart => "flex-start",
        Align.Center => "center",
        Align.FlexEnd => "flex-end",
        Align.Stretch => "stretch",
        Align.Baseline => "baseline",
        _ => throw Undefined(value),
    };

    /// <summary>CSS-cased wire value for <paramref name="value"/>.</summary>
    public static string ToStyleValue(this Wrap value) => value switch
    {
        Wrap.NoWrap => "nowrap",
        Wrap.Wrap => "wrap",
        Wrap.WrapReverse => "wrap-reverse",
        _ => throw Undefined(value),
    };

    /// <summary>CSS-cased wire value for <paramref name="value"/>.</summary>
    public static string ToStyleValue(this Position value) => value switch
    {
        Position.Relative => "relative",
        Position.Absolute => "absolute",
        _ => throw Undefined(value),
    };

    // ── Nullable lifts: null param → null value → no attribute → no patch ─────

    /// <inheritdoc cref="ToStyleValue(FlexDirection)"/>
    public static string? ToStyleValue(this FlexDirection? value)
        => value is { } v ? v.ToStyleValue() : null;

    /// <inheritdoc cref="ToStyleValue(Justify)"/>
    public static string? ToStyleValue(this Justify? value)
        => value is { } v ? v.ToStyleValue() : null;

    /// <inheritdoc cref="ToStyleValue(Align)"/>
    public static string? ToStyleValue(this Align? value)
        => value is { } v ? v.ToStyleValue() : null;

    /// <inheritdoc cref="ToStyleValue(Wrap)"/>
    public static string? ToStyleValue(this Wrap? value)
        => value is { } v ? v.ToStyleValue() : null;

    /// <inheritdoc cref="ToStyleValue(Position)"/>
    public static string? ToStyleValue(this Position? value)
        => value is { } v ? v.ToStyleValue() : null;

    /// <summary>Numeric → wire string, INVARIANTLY (the shells parse with a
    /// C/Java float parser: a Dutch locale must never put <c>"1,5"</c> on the
    /// wire). Null stays null — no attribute, no patch.</summary>
    public static string? ToStyleValue(this float? value)
        => value?.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static ArgumentOutOfRangeException Undefined<T>(T value) where T : struct, Enum
        => new(nameof(value), value, $"undefined {typeof(T).Name} value — no wire word exists for it");
}
