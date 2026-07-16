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

/// <summary>How an image's bytes paint INSIDE the box Yoga computed
/// (<c>contentMode</c> on the prop wire). Paint-only, normatively: the mode
/// never consults or changes measurement — every layout frame is
/// mode-invariant.</summary>
public enum ImageContentMode
{
    /// <summary><c>"contain"</c> — aspect-fit, the default (the 6.3 contract
    /// row both shells already render; deliberately NOT React Native's
    /// <c>cover</c> — see the file header). Letterbox bars show
    /// <see cref="BnImage.BackgroundColor"/>, never the placeholder.</summary>
    Contain,
    /// <summary><c>"cover"</c> — aspect-fill, cropped to the box (the paint
    /// never escapes it: both shells clip, Gate 3 pins
    /// <c>clipsToBounds</c>).</summary>
    Cover,
    /// <summary><c>"stretch"</c> — fill both axes, aspect not preserved.</summary>
    Stretch,
    /// <summary><c>"center"</c> — natural size, centered, no scaling (and
    /// clipped when bigger than the box).</summary>
    Center,
}

/// <summary>Enum → wire string (the <see cref="FlexStyleValues"/> shape). The
/// nullable overload is what <see cref="BnImage"/> calls: a null param yields
/// a null value, which <c>RenderTreeBuilder.AddAttribute</c> omits entirely —
/// no attribute, no patch, shell default (<see cref="ImageContentMode.Contain"/>).</summary>
public static class ImageContentModes
{
    /// <summary>The strict lowercase wire value for <paramref name="value"/> —
    /// exact membership in the four-string set both shells parse (an unknown
    /// value on the wire is diagnosed loudly shell-side and NOT applied).</summary>
    public static string ToWireValue(this ImageContentMode value) => value switch
    {
        ImageContentMode.Contain => "contain",
        ImageContentMode.Cover => "cover",
        ImageContentMode.Stretch => "stretch",
        ImageContentMode.Center => "center",
        _ => throw Undefined(value),
    };

    /// <inheritdoc cref="ToWireValue(ImageContentMode)"/>
    public static string? ToWireValue(this ImageContentMode? value)
        => value is { } v ? v.ToWireValue() : null;

    /// <summary>The "enum value outside the declared set" guard (a cast int) —
    /// <see cref="FlexStyleValues"/>'s, verbatim.</summary>
    private static ArgumentOutOfRangeException Undefined(
        ImageContentMode value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        => new(paramName, value, "undefined ImageContentMode value — no wire word exists for it");
}
