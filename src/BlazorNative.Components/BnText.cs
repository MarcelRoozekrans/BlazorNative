using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorNative.Components;

/// <summary>
/// Text label — emits a <c>span</c> (host NodeType "text": TextView on
/// Android) with optional <c>fontSize</c> style attribute.
/// </summary>
/// <remarks>Hand-written BuildRenderTree with gap-numbered sequences;
/// Razor syntax awaits .razor compilation (M6).</remarks>
public sealed class BnText : ComponentBase
{
    /// <summary>The text content. Null renders an empty label.</summary>
    [Parameter] public string? Text { get; set; }

    /// <summary>Font size in sp, e.g. <c>"24"</c>. Null = unset.</summary>
    [Parameter] public string? FontSize { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "span");
        b.AddAttribute(1, "fontSize", FontSize); // null → omitted

        // Always emit the text frame (empty string included) so the host
        // text node exists from mount and later edits are a ReplaceText on
        // a stable nodeId — the echo-pinning contract BnDemoTests rely on.
        b.AddContent(10, Text ?? "");

        b.CloseElement();
    }
}
