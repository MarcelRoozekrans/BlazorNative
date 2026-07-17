namespace BlazorNative.Components;

/// <summary>
/// A vertical <see cref="BnView"/> — <c>flexDirection: column</c>. Forwards every
/// other <see cref="BnView"/> parameter (see <see cref="BnFlexPreset"/>).
/// </summary>
/// <remarks>Column is also the layout engine's default direction, so
/// <c>BnColumn</c> is a statement of intent rather than a behaviour change over
/// a bare <see cref="BnView"/> — it says what the markup means. There is no
/// <c>BnStack</c>; it would be a synonym for this.</remarks>
public sealed class BnColumn : BnFlexPreset
{
    /// <inheritdoc/>
    protected override FlexDirection PresetDirection => FlexDirection.Column;
}
