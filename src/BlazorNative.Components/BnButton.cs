using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorNative.Components;

/// <summary>
/// A push button. Renders as a native <c>Button</c> on Android and a
/// <c>UIButton</c> on iOS, so it draws and responds the way the platform's own
/// buttons do.
/// </summary>
/// <example>
/// <code>
/// &lt;BnButton Label="@($"Tapped {_taps} time(s)")" OnClick="OnTap" /&gt;
///
/// @code {
///     private int _taps;
///     private void OnTap() =&gt; _taps++;
/// }
/// </code>
/// </example>
public sealed class BnButton : BnLayoutItem
{
    /// <summary>The button's caption.</summary>
    [Parameter] public string? Label { get; set; }

    /// <summary>Raised when the button is tapped. Not raised while
    /// <see cref="Enabled"/> is false.</summary>
    [Parameter] public EventCallback<MouseEventArgs> OnClick { get; set; }

    /// <summary>Set false to show the platform's disabled button and stop
    /// <see cref="OnClick"/> firing. Default true.</summary>
    [Parameter] public bool Enabled { get; set; } = true;

    // The item surface (BackgroundColor, Margin, AlignSelf, Grow/Shrink/Basis,
    // the box, Position and its insets) is inherited from BnLayoutItem and
    // emitted at sequence 1-17 by EmitItemAttributes — see that type for the
    // parameters.

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "button");

        // Sequence 1-17: the shared item surface — see BnLayoutItem.EmitItemAttributes.
        EmitItemAttributes(b);

        // Sequence 100+: this component's own surface, clear of the base's 1-17.
        b.AddAttribute(100, "onclick", OnClick);
        if (!Enabled)
            b.AddAttribute(101, "enabled", "false");

        b.AddContent(109, Label ?? "");

        b.CloseElement();
    }
}
