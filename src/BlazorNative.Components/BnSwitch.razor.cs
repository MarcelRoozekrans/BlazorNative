namespace BlazorNative.Components;

// See BnCheckbox.razor.cs for why the six .razor components carry their
// type-level summary in a partial class: a `@* ... *@` header is a Razor comment
// and never reaches the assembly.

/// <summary>
/// An on/off switch. Renders as a native <c>Switch</c> on Android and a
/// <c>UISwitch</c> on iOS.
/// </summary>
/// <remarks>
/// <para>
/// On iOS this is the same control <see cref="BnCheckbox"/> maps to, since UIKit
/// has no checkbox; on Android the two are visually distinct. Pick the one that
/// says what you mean and let each platform draw it its own way.
/// </para>
/// <para>
/// It is a leaf with the platform's own intrinsic size, so you do not give it a
/// width or a height.
/// </para>
/// <example>
/// <code>
/// &lt;BnSwitch @bind-Checked="_wifi" /&gt;
///
/// @code {
///     private bool _wifi = true;
/// }
/// </code>
/// </example>
/// </remarks>
public partial class BnSwitch
{
}
