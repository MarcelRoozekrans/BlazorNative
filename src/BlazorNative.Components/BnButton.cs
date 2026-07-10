using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorNative.Components;

/// <summary>
/// Button — emits a <c>button</c> (host NodeType "button") with a
/// <c>click</c> event attach and the label as its text child.
/// </summary>
/// <remarks>
/// <see cref="Enabled"/> follows boolean-attribute semantics: the
/// <c>enabled</c> prop is emitted only when false (UpdatePropPatch
/// "enabled"="false"); re-enabling removes the attribute and the host's
/// null-value handling restores the default (WidgetMapper:
/// <c>p.value?.toBoolean() ?: true</c>). Hand-written BuildRenderTree with
/// gap-numbered sequences; Razor syntax awaits .razor compilation (M6).
/// </remarks>
public sealed class BnButton : ComponentBase
{
    /// <summary>The button caption.</summary>
    [Parameter] public string? Label { get; set; }

    /// <summary>Raised when the host dispatches a click for this button.</summary>
    [Parameter] public EventCallback<MouseEventArgs> OnClick { get; set; }

    /// <summary>False disables the host widget. Default true.</summary>
    [Parameter] public bool Enabled { get; set; } = true;

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "button");
        b.AddAttribute(1, "onclick", OnClick);
        if (!Enabled)
            b.AddAttribute(2, "enabled", "false");

        b.AddContent(10, Label ?? "");

        b.CloseElement();
    }
}
