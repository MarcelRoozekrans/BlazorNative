namespace BlazorNative.Components;

/// <summary>
/// A vertical <see cref="BnView"/> — <c>flexDirection: column</c>. Forwards every
/// other <see cref="BnView"/> parameter (see <see cref="BnFlexPreset"/>).
/// </summary>
/// <remarks>Column is also Yoga's DEFAULT direction, so <c>BnColumn</c> is an
/// explicit statement of intent rather than a behaviour change over a bare
/// <see cref="BnView"/> — it emits <c>flexDirection: column</c> so the tree says
/// what it means. There is no <c>BnStack</c>: it would be a synonym for this
/// (design decision 3).</remarks>
public sealed class BnColumn : BnFlexPreset
{
    /// <inheritdoc/>
    protected override FlexDirection PresetDirection => FlexDirection.Column;
}
