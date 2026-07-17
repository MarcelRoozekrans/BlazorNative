namespace BlazorNative.Components;

// See BnCheckbox.razor.cs for why the six .razor components carry their
// type-level summary in a partial class.

/// <summary>
/// A modal overlay: a scrim across the whole screen with a content box on top.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is an overlay inside your existing page, not a native dialog window.</b>
/// That is worth knowing because it is what makes the modal behave like the rest
/// of your tree: it lays out with the same engine, and it does not own its own
/// dismissal.
/// </para>
/// <para>
/// <b>You own whether it is open.</b> Dismissal is a <em>request</em>: tapping the
/// scrim — and, on Android, the hardware back button — raises
/// <c>VisibleChanged</c> with <c>false</c>, and nothing closes until your state
/// says so. Taps inside the content box never dismiss. Bind <c>Visible</c> and
/// the ordinary Blazor flow does the rest; refuse the request (an unsaved edit,
/// say) and the modal simply stays open.
/// </para>
/// <para>
/// The content box carries its own <c>ContentWidth</c>, <c>ContentHeight</c>,
/// <c>Padding</c> and <c>BackgroundColor</c>; <c>ChildContent</c> renders inside
/// it. Stacking is creation order, so a modal shown later lands on top.
/// </para>
/// <example>
/// <code>
/// &lt;BnModal @bind-Visible="_confirming" ContentWidth="280" Padding="16"
///          BackgroundColor="#FFFFFF"&gt;
///     &lt;BnColumn Gap="12"&gt;
///         &lt;BnText Text="Delete this item?" /&gt;
///         &lt;BnButton Label="Cancel" OnClick="() =&gt; _confirming = false" /&gt;
///     &lt;/BnColumn&gt;
/// &lt;/BnModal&gt;
/// </code>
/// </example>
/// </remarks>
public partial class BnModal
{
}
