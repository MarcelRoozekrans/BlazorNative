using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorNative.Components;

/// <summary>
/// A text label. Renders as a native <c>TextView</c> on Android and a
/// <c>UILabel</c> on iOS.
/// </summary>
/// <remarks>
/// The platform measures it, not the layout engine: a label with no explicit
/// size reports the height its text actually wraps to, and that measured height
/// is what the surrounding flex layout is given. Constrain the width — with
/// <see cref="BnView.Width"/> on a wrapper, or by letting a row size it — and
/// the text wraps inside it.
/// </remarks>
public sealed class BnText : ComponentBase
{
    /// <summary>The text to show. Null renders an empty label rather than
    /// nothing, so the label keeps its place in the layout.</summary>
    [Parameter] public string? Text { get; set; }

    /// <summary>Font size in density-independent units — <c>24</c> is 24dp on
    /// Android and 24pt on iOS. Null leaves the platform's default label
    /// size.</summary>
    [Parameter] public float? FontSize { get; set; }

    /// <inheritdoc />
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
