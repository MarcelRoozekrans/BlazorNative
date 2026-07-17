using System.Runtime.CompilerServices;

namespace BlazorNative.Components;

// ─────────────────────────────────────────────────────────────────────────────
// ImageContentMode — Phase 7.5 (design decision 3).
//
// "Typed C# params, strings on the wire" (the FlexStyles discipline), applied
// to BnImage's paint mode. A .NET enum so an invalid value is UNREPRESENTABLE
// from the component; the wire carries the strict lowercase strings each shell
// maps to its own widget vocabulary — held to ONE table:
//
//     wire        .NET       Android ScaleType   iOS UIView.ContentMode
//     contain     Contain    FIT_CENTER          .scaleAspectFit
//     cover       Cover      CENTER_CROP         .scaleAspectFill
//     stretch     Stretch    FIT_XY              .scaleToFill
//     center      Center     CENTER              .center
//
// This is React Native's `resizeMode` set MINUS `repeat` (an iOS-first
// curiosity RN itself half-supports; no customer; ledger-on-request).
//
// ── MODE IS PAINT-ONLY (the parity rule, normative) ──────────────────────────
// The layout box is Yoga's and never changes with mode: the measure func
// reports the natural pixel size (or is never called, if declared) WITHOUT
// consulting the mode; the mode decides only how the bytes paint INSIDE the
// box Yoga computed. Every frame in every table is mode-invariant — the
// /imagepolish quartet asserts exactly that, four identical frames under four
// modes. `contentMode` is therefore a PROP (the UpdateProp wire, where `src`
// rides), never a style: it is not layout (Yoga never sees it) and it is not
// a name ANY node can carry (the style partition's admission bar).
//
// ── THE DEFAULT IS Contain, AND RN'S IS cover — recorded, with the why ──────
// Today's behavior IS aspect-fit: the 6.3 contract row set FIT_CENTER /
// .scaleAspectFit explicitly on both shells because the framework defaults
// disagreed, and chose fit "because it is the value an M7 ContentMode would
// default to". This phase honors that. Adopting RN's `cover` would silently
// repaint every existing image — /image's case [0] and BnScrollDemo's row
// image — FRAME-NEUTRALLY, the class of change no test on either shell can
// see. A default that cannot lie about pixels beats RN-compatibility on a
// property every RN author sets explicitly anyway. The default reaches the
// shells as ABSENCE: an unset ContentMode emits nothing (the un-styled
// invariant), and `contentMode → null` restores it (the `enabled`-null
// precedent — null on the prop wire means "the author took it away").
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>How an image's pixels are painted inside the box the layout gave
/// it.</summary>
/// <remarks>
/// <b>The mode is paint-only, and that is a guarantee rather than an
/// implementation note.</b> The layout box belongs to Yoga — it comes from the
/// image's natural size and the flex parameters you set — and it is identical
/// under all four modes. Changing the mode repaints; it never reflows, and it
/// can never move anything else on the page. If you want the image to occupy a
/// different amount of space, set <see cref="BnImage.Width"/> or
/// <see cref="BnImage.Height"/>; the mode will not do it for you.
/// </remarks>
public enum ImageContentMode
{
    /// <summary>Aspect-fit: the whole image is visible, scaled down to fit the
    /// box with its aspect ratio kept. <b>The default.</b> The leftover bars
    /// show <see cref="BnImage.BackgroundColor"/>.</summary>
    Contain,
    /// <summary>Aspect-fill: the image covers the whole box with its aspect
    /// ratio kept, and the overflow is cropped. The paint never escapes the
    /// box — both platforms clip it.</summary>
    Cover,
    /// <summary>Fill the box on both axes, ignoring the aspect ratio. The image
    /// is distorted unless the box happens to match it.</summary>
    Stretch,
    /// <summary>Natural size, centered, never scaled — and cropped when the
    /// image is bigger than the box.</summary>
    Center,
}

/// <summary>Converts <see cref="ImageContentMode"/> to the lowercase string the
/// Android and iOS shells read.</summary>
/// <remarks><see cref="BnImage"/> calls this for you — you need it only if you
/// are building a component that speaks to the shells directly.</remarks>
public static class ImageContentModes
{
    /// <summary>The value for <paramref name="value"/>: one of <c>"contain"</c>,
    /// <c>"cover"</c>, <c>"stretch"</c> or <c>"center"</c>.</summary>
    /// <param name="value">The mode to convert.</param>
    /// <returns>The lowercase string both platforms parse.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not one of the
    /// four declared modes — for example an integer cast to the enum.</exception>
    public static string ToWireValue(this ImageContentMode value) => value switch
    {
        ImageContentMode.Contain => "contain",
        ImageContentMode.Cover => "cover",
        ImageContentMode.Stretch => "stretch",
        ImageContentMode.Center => "center",
        _ => throw Undefined(value),
    };

    /// <summary>As <see cref="ToWireValue(ImageContentMode)"/>, but null maps to
    /// null — which a component emits as no value at all, leaving the platform's
    /// default in place.</summary>
    /// <param name="value">The mode to convert, or null.</param>
    /// <returns>The lowercase string, or null when <paramref name="value"/> is
    /// null.</returns>
    public static string? ToWireValue(this ImageContentMode? value)
        => value is { } v ? v.ToWireValue() : null;

    /// <summary>The guard for an enum value outside the declared set — typically
    /// an integer cast to the enum type.</summary>
    private static ArgumentOutOfRangeException Undefined(
        ImageContentMode value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        => new(paramName, value, "undefined ImageContentMode value — no wire word exists for it");
}
