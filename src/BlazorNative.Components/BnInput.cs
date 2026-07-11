using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorNative.Components;

/// <summary>
/// Single-line text input — emits an <c>input</c> (host NodeType "input":
/// EditText on Android) with <c>value</c> / <c>placeholder</c> props and a
/// <c>change</c> event attach.
/// </summary>
/// <remarks>
/// <para><b>@bind mechanics, not syntax</b> (Phase 3.4 design decision):
/// this component ships the <see cref="Value"/> +
/// <see cref="ValueChanged"/> (<c>EventCallback&lt;string&gt;</c>) pair —
/// exactly what Razor's <c>@bind-Value</c> compiles to. The Razor syntax
/// itself awaits .razor compilation (M6); until then parents wire the pair
/// by hand: <c>Value</c> in, <c>ValueChanged</c> out.</para>
///
/// <para>The loop: host change event (EditText TextWatcher → 3.2 dispatch
/// plumbing) → the <c>onchange</c> handler here invokes
/// <see cref="ValueChanged"/> with the <see cref="ChangeEventArgs"/> value →
/// parent state mutates → re-render → <c>UpdatePropPatch("value")</c> → the
/// host writes the widget under its <c>applyingBatch</c> guard (no echo
/// dispatch).</para>
///
/// <para><see cref="Enabled"/> follows BnButton's boolean-attribute
/// semantics: <c>enabled</c> emitted only when false. Hand-written
/// BuildRenderTree with gap-numbered sequences.</para>
/// </remarks>
public sealed class BnInput : ComponentBase
{
    /// <summary>The current text. Always emitted (empty string included) so
    /// the host prop exists from mount.</summary>
    [Parameter] public string? Value { get; set; }

    /// <summary>Raised with the new text on a host change event — the
    /// write-back half of the <c>@bind-Value</c> pair.</summary>
    [Parameter] public EventCallback<string> ValueChanged { get; set; }

    /// <summary>Hint text shown while empty. Null = unset.</summary>
    [Parameter] public string? Placeholder { get; set; }

    /// <summary>False disables the host widget. Default true.</summary>
    [Parameter] public bool Enabled { get; set; } = true;

    /// <summary>Raised when the host input gains focus (Phase 4.2, DoD #4).
    /// OPTIONAL: the <c>focus</c> attach is emitted only when a delegate is
    /// set (<c>HasDelegate</c>) — an unwired BnInput's patch shape is
    /// byte-identical to the pre-4.2 one, so BnDemo's canonical golden
    /// (4 attaches) does not churn.</summary>
    [Parameter] public EventCallback<FocusEventArgs> OnFocus { get; set; }

    /// <summary>Raised when the host input loses focus. Same
    /// attach-only-when-set contract as <see cref="OnFocus"/>.</summary>
    [Parameter] public EventCallback<FocusEventArgs> OnBlur { get; set; }

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
