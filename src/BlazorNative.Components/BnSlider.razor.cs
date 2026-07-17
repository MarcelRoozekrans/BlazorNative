namespace BlazorNative.Components;

// See BnCheckbox.razor.cs for why the six .razor components carry their
// type-level summary in a partial class.

/// <summary>
/// A slider for picking a number from a range. Renders as a native
/// <c>SeekBar</c> on Android and a <c>UISlider</c> on iOS.
/// </summary>
/// <remarks>
/// <para>
/// The range is declared, not inherited: <c>Min</c> defaults to <c>0</c> and
/// <c>Max</c> to <c>100</c>, and both always travel to the platform, so the two
/// never disagree about what the track means. <c>Step</c> is optional — leave it
/// unset for a continuous slider.
/// </para>
/// <para>
/// Its intrinsic height is the platform's, but its natural width is not
/// something you should rely on: <b>give it a <c>Width</c></b> (or let a flex
/// parent size it) whenever the exact width matters.
/// </para>
/// <example>
/// <code>
/// &lt;BnSlider @bind-Value="_volume" Min="0" Max="100" Step="5" Width="240" /&gt;
///
/// @code {
///     private float _volume = 25f;
/// }
/// </code>
/// </example>
/// </remarks>
public partial class BnSlider
{
}
