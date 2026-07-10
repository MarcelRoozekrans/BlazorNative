using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorNative.Components;

/// <summary>
/// Container component — emits a <c>div</c> (host NodeType "view":
/// LinearLayout on Android) with optional <c>backgroundColor</c> /
/// <c>padding</c> style attributes and nested children.
/// </summary>
/// <remarks>
/// <see cref="ChildContent"/> renders as a Region frame (Blazor's grouping
/// marker for RenderFragment content) — walked transparently by the renderer
/// since Phase 3.4 Gate 1, so children parent under this view and number as
/// if inline. Hand-written BuildRenderTree with gap-numbered sequences
/// (HelloComponent's scheme); Razor syntax awaits .razor compilation (M6).
/// </remarks>
public sealed class BnView : ComponentBase
{
    /// <summary>Background color, e.g. <c>"#FFEEAA"</c>. Null = unset.</summary>
    [Parameter] public string? BackgroundColor { get; set; }

    /// <summary>Padding in dp, e.g. <c>"16"</c>. Null = unset.</summary>
    [Parameter] public string? Padding { get; set; }

    /// <summary>Nested content rendered inside this view.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "div");
        b.AddAttribute(1, "backgroundColor", BackgroundColor); // null → omitted
        b.AddAttribute(2, "padding", Padding);                 // null → omitted

        b.AddContent(10, ChildContent);

        b.CloseElement();
    }
}
