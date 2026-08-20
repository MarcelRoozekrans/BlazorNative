using System.Globalization;

namespace BlazorNative.Components;

/// <summary>The unit a <see cref="BnLength"/> carries.</summary>
public enum BnLengthUnit
{
    /// <summary>Density-independent points — a bare number on the wire.</summary>
    Points,
    /// <summary>A percentage of the parent's corresponding dimension.</summary>
    Percent,
}

/// <summary>
/// A layout length: a number of points, or a percentage. There is deliberately no
/// conversion from <c>string</c> — that absence is what makes <c>Width="12px"</c> a
/// compile error rather than a value both shells log and ignore.
/// </summary>
/// <remarks>
/// <para>
/// Use it as <c>BnLength?</c> on a parameter, never bare. <c>null</c> means "unset"
/// and reaches the wire as an absent attribute, which is what makes the shells reset
/// the style. A bare <c>default(BnLength)</c> is <b>zero points</b> — a real length —
/// and would silently collapse a node nobody meant to size.
/// </para>
/// <para>
/// Negative values are representable and are rejected by the shells, not by this type.
/// They are legal on <c>margin</c> and the insets and illegal elsewhere, which is a
/// per-property rule this type does not carry.
/// </para>
/// </remarks>
/// <param name="Value">The magnitude.</param>
/// <param name="Unit">Whether <paramref name="Value"/> is points or a percentage.</param>
public readonly record struct BnLength(float Value, BnLengthUnit Unit)
{
    /// <summary>A length in points.</summary>
    /// <param name="points">The magnitude, in density-independent points.</param>
    public static implicit operator BnLength(float points) => new(points, BnLengthUnit.Points);

    /// <summary>
    /// A length in points, from a <c>double</c>. Required, not a convenience: Razor
    /// compiles <c>Width="12.5"</c> as a <c>double</c> literal, and <c>double</c> to
    /// <c>float</c> is a narrowing conversion, so without this every decimal literal in
    /// markup fails to compile.
    /// </summary>
    /// <param name="points">The magnitude, in density-independent points.</param>
    public static implicit operator BnLength(double points) => new((float)points, BnLengthUnit.Points);

    /// <summary>A length as a percentage of the parent's corresponding dimension.</summary>
    /// <param name="value">The percentage, without the sign.</param>
    /// <returns>A percentage-valued length.</returns>
    public static BnLength Percent(float value) => new(value, BnLengthUnit.Percent);

    /// <summary>
    /// The wire form, INVARIANTLY. The shells parse with a C/Java float parser, so a
    /// Dutch locale must never put <c>"1,5"</c> on the wire.
    /// </summary>
    /// <returns>A bare number, or a number with a trailing percent sign.</returns>
    public string ToStyleValue()
    {
        var n = Value.ToString(CultureInfo.InvariantCulture);
        return Unit == BnLengthUnit.Percent ? n + "%" : n;
    }
}
