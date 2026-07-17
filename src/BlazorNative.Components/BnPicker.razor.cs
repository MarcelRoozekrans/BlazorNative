namespace BlazorNative.Components;

// See BnCheckbox.razor.cs for why the six .razor components carry their
// type-level summary in a partial class.

/// <summary>
/// A drop-down / wheel picker over a list of strings. Renders as a native
/// <c>Spinner</c> on Android and a <c>UIPickerView</c> on iOS.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="BnInput"/> — where the text lives in your code and the
/// widget echoes it — a native picker <b>owns its selection UI</b>, so the whole
/// item list is handed to the platform and the selection comes back as an index.
/// </para>
/// <para>
/// <b>The selection is always valid, and the picker will correct you.</b> With no
/// items the index is <c>-1</c>. With items, it is clamped into range: a negative
/// becomes <c>0</c>, and an index past the end becomes the last item — because a
/// native picker always shows something, so "nothing selected" is not a state it
/// can represent. When clamping moves the value, <c>SelectedIndexChanged</c>
/// fires with the clamped index, so your bound state re-syncs to what is actually
/// on screen instead of the two quietly disagreeing.
/// </para>
/// <example>
/// <code>
/// &lt;BnPicker Items="_cities" @bind-SelectedIndex="_city" /&gt;
///
/// @code {
///     private readonly string[] _cities = ["Amsterdam", "Rotterdam", "Utrecht"];
///     private int _city;
/// }
/// </code>
/// </example>
/// </remarks>
public partial class BnPicker
{
}
