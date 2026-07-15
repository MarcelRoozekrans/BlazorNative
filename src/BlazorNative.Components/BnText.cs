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

    /// <summary>Font size in density-independent units, e.g. <c>24</c>.
    /// Null = unset. Typed (Phase 7.1, design decision 1 — the M4-ledger
    /// straggler): stringified invariantly onto the wire exactly like
    /// <see cref="BnView.Grow"/> (the 6.1 <see cref="FlexStyleValues"/>
    /// pattern) — <c>24f</c> reaches the shells as <c>"24"</c>, the same
    /// bytes the string parameter produced. Pre-1.0 breaking API change,
    /// recorded in the phase conclusion; no compat shim.</summary>
    [Parameter] public float? FontSize { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "span");
        b.AddAttribute(1, "fontSize", FontSize.ToStyleValue()); // null → omitted

        // Always emit the text frame (empty string included) so the host
        // text node exists from mount and later edits are a ReplaceText on
        // a stable nodeId — the echo-pinning contract BnDemoTests rely on.
        b.AddContent(10, Text ?? "");

        b.CloseElement();
    }
}
