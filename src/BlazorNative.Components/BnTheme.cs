namespace BlazorNative.Components;

/// <summary>
/// A pair of background colors, handed down the tree as a cascading value.
/// </summary>
/// <remarks>
/// <para>
/// It is a <c>record</c> for a reason worth knowing before you use it: cascading
/// values notify their consumers by <b>reference</b>, so a theme change has to
/// produce a <b>new instance</b>. Replace it — <c>theme with { ... }</c>, or a
/// fresh <c>new BnTheme(...)</c> — and every component reading the
/// <c>CascadingValue&lt;BnTheme&gt;</c> re-renders. Mutating state in place and
/// keeping the same instance changes nothing on screen.
/// </para>
/// <para>
/// The colors are hex strings, the same grammar
/// <see cref="BnView.BackgroundColor"/> takes.
/// </para>
/// </remarks>
/// <param name="Background">The background for themed surfaces.</param>
/// <param name="AltBackground">The background a toggle swaps in.</param>
public sealed record BnTheme(string Background, string AltBackground);
