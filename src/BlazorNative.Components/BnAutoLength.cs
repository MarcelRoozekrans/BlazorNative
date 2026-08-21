namespace BlazorNative.Components;

/// <summary>
/// A layout length that may also be <c>auto</c>. Composes <see cref="BnLength"/> so the
/// grammar is implemented once; this type adds only the <c>auto</c> case.
/// </summary>
/// <remarks>
/// <para>
/// Use it as <c>BnAutoLength?</c> on a parameter, never bare, and note that the two
/// nulls mean different things. The OUTER null (the <c>?</c>) is <b>unset</b> — no
/// attribute, so the shell resets the style. The INNER null (<see cref="Length"/>) is
/// <b>auto</b>. The shells already draw that distinction: resetting a removed margin to
/// <c>auto</c> would move the node rather than restore it, which is why <c>null</c> and
/// <c>auto</c> cannot be the same value.
/// </para>
/// <para>
/// It follows that <c>default(BnAutoLength)</c> is <c>auto</c>, not unset. That is the
/// reason parameters are nullable and never bare.
/// </para>
/// </remarks>
/// <param name="Length">The length, or <c>null</c> for <c>auto</c>.</param>
public readonly record struct BnAutoLength(BnLength? Length)
{
    /// <summary>Absorb the free space — the <c>auto</c> value.</summary>
    public static BnAutoLength Auto => new((BnLength?)null);

    /// <summary>A length in points.</summary>
    /// <param name="points">The magnitude, in density-independent points.</param>
    public static implicit operator BnAutoLength(float points) => new((BnLength)points);

    /// <summary>A length in points, from a <c>double</c>. See <see cref="BnLength"/> for why this exists.</summary>
    /// <param name="points">The magnitude, in density-independent points.</param>
    public static implicit operator BnAutoLength(double points) => new((BnLength)points);

    /// <summary>Widens a <see cref="BnLength"/> into the auto-capable type.</summary>
    /// <param name="length">The length to widen.</param>
    public static implicit operator BnAutoLength(BnLength length) => new(length);

    /// <summary>A length as a percentage of the parent's corresponding dimension.</summary>
    /// <param name="value">The percentage, without the sign.</param>
    /// <returns>A percentage-valued length.</returns>
    public static BnAutoLength Percent(float value) => new(BnLength.Percent(value));

    /// <summary>The wire form, INVARIANTLY — see <see cref="BnLength.ToStyleValue"/>.</summary>
    /// <returns><c>"auto"</c>, a bare number, or a number with a trailing percent sign.</returns>
    public string ToStyleValue() => Length?.ToStyleValue() ?? "auto";
}
