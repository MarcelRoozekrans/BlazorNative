namespace BlazorNative.Components;

/// <summary>
/// A horizontal <see cref="BnView"/> — <c>flexDirection: row</c>. Forwards every
/// other <see cref="BnView"/> parameter (see <see cref="BnFlexPreset"/>).
/// </summary>
/// <remarks>Deliberately does NOT expose <see cref="BnView.Direction"/>: a BnRow
/// IS a row. Reach for <see cref="BnView"/> when the direction is dynamic.</remarks>
public sealed class BnRow : BnFlexPreset
{
    /// <inheritdoc/>
    protected override FlexDirection PresetDirection => FlexDirection.Row;
}
