using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorNative.Components;

/// <summary>
/// A single-line text box. Renders as a native <c>EditText</c> on Android and a
/// <c>UITextField</c> on iOS.
/// </summary>
/// <remarks>
/// <para>
/// It exposes the <see cref="Value"/> / <see cref="ValueChanged"/> pair, which
/// is what <c>@bind-Value</c> needs — so binding is the ordinary Blazor syntax:
/// </para>
/// <example>
/// <code>
/// &lt;BnInput @bind-Value="_name" Placeholder="Your name" /&gt;
///
/// @code {
///     private string _name = "";
/// }
/// </code>
/// </example>
/// <para>
/// You can also wire the two halves by hand — <see cref="Value"/> in,
/// <see cref="ValueChanged"/> out — when you need to intercept the edit.
/// </para>
/// </remarks>
public sealed class BnInput : ComponentBase
{
    /// <summary>The current text. Null is sent as an empty string, so the text
    /// box always has a definite value.</summary>
    [Parameter] public string? Value { get; set; }

    /// <summary>Raised with the new text when the user edits the box — the
    /// write-back half of the <c>@bind-Value</c> pair.</summary>
    [Parameter] public EventCallback<string> ValueChanged { get; set; }

    /// <summary>Hint text shown while the box is empty. Null = none.</summary>
    [Parameter] public string? Placeholder { get; set; }

    /// <summary>Set false to show the platform's disabled text box and stop it
    /// accepting input. Default true.</summary>
    [Parameter] public bool Enabled { get; set; } = true;

    /// <summary>Raised when the text box gains focus. Optional: nothing is
    /// attached to the native control unless you supply a handler.</summary>
    [Parameter] public EventCallback<FocusEventArgs> OnFocus { get; set; }

    /// <summary>Raised when the text box loses focus. Optional, like
    /// <see cref="OnFocus"/>.</summary>
    [Parameter] public EventCallback<FocusEventArgs> OnBlur { get; set; }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "input");
        b.AddAttribute(1, "value", Value ?? "");
        b.AddAttribute(2, "placeholder", Placeholder); // null → omitted
        b.AddAttribute(3, "onchange",
            EventCallback.Factory.Create<ChangeEventArgs>(this, HandleChange));
        if (!Enabled)
            b.AddAttribute(4, "enabled", "false");
        if (OnFocus.HasDelegate)
            b.AddAttribute(5, "onfocus", OnFocus);
        if (OnBlur.HasDelegate)
            b.AddAttribute(6, "onblur", OnBlur);
        b.CloseElement();
    }

    private Task HandleChange(ChangeEventArgs e)
        => ValueChanged.InvokeAsync(e.Value?.ToString() ?? "");
}
