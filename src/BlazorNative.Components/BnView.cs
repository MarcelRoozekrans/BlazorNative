using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorNative.Components;

/// <summary>
/// Container component — emits a <c>div</c> (host NodeType "view") with an
/// optional flexbox style surface and nested children.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ChildContent"/> renders as a Region frame (Blazor's grouping
/// marker for RenderFragment content) — walked transparently by the renderer
/// since Phase 3.4 Gate 1, so children parent under this view and number as
/// if inline. Hand-written BuildRenderTree with gap-numbered sequences
/// (HelloComponent's scheme); Razor syntax awaits .razor compilation (M6).
/// </para>
/// <para>
/// Phase 6.1: the flex surface (design decision 4 — typed C# params, strings
/// on the wire). EVERY flex param is nullable and defaults to null → no
/// attribute → NO PATCH. That is the un-styled invariant, and it is a design
/// constraint, not an accident: adding flex must not perturb an un-styled tree
/// (the BnDemo / BnSettingsPage goldens on all four surfaces stay byte-identical,
/// and Yoga's default flexDirection is <c>column</c>, so an un-styled tree still
/// lays out as a vertical stack).
/// </para>
/// <para>
/// Attribute SEQUENCE numbers are load-bearing (Blazor's diff keys on them):
/// <c>backgroundColor</c>(1) and <c>padding</c>(2) keep the numbers they have
/// had since 3.4 and the flex block is APPENDED at 3…24. The ChildContent
/// region moved from 10 to 100 to make room — a content sequence only has to
/// be stable ACROSS RENDERS of the same component (both trees in a diff come
/// from this same method), and the gap leaves room for the next style block.
/// </para>
/// </remarks>
public sealed class BnView : ComponentBase
{
    /// <summary>Background color, e.g. <c>"#FFEEAA"</c>. Null = unset.</summary>
    [Parameter] public string? BackgroundColor { get; set; }

    /// <summary>Padding in dp, e.g. <c>"16"</c>. Null = unset.</summary>
    [Parameter] public string? Padding { get; set; }

    /// <summary>Margin in dp, e.g. <c>"8"</c>. Null = unset.</summary>
    [Parameter] public string? Margin { get; set; }

    // ── Container layout (how THIS view arranges its children) ────────────────

    /// <summary>Main axis (<c>flexDirection</c>). Null = Yoga's default (column).</summary>
    [Parameter] public FlexDirection? Direction { get; set; }

    /// <summary>Main-axis distribution (<c>justifyContent</c>). Null = flex-start.</summary>
    [Parameter] public Justify? Justify { get; set; }

    /// <summary>Cross-axis alignment of the children (<c>alignItems</c>). Null = stretch.</summary>
    [Parameter] public Align? Align { get; set; }

    /// <summary>Line wrapping (<c>flexWrap</c>). Null = nowrap.</summary>
    [Parameter] public Wrap? Wrap { get; set; }

    /// <summary>Gap between children, e.g. <c>"8"</c> (<c>gap</c>). Null = 0.</summary>
    [Parameter] public string? Gap { get; set; }

    // ── Item layout (how this view behaves INSIDE its parent) ─────────────────

    /// <summary>Cross-axis override for this item (<c>alignSelf</c>). Null = auto
    /// (inherit the parent's <see cref="Align"/>).</summary>
    [Parameter] public Align? AlignSelf { get; set; }

    /// <summary>Share of the free main-axis space this item absorbs
    /// (<c>flexGrow</c>). Null = 0.</summary>
    [Parameter] public float? Grow { get; set; }

    /// <summary>Share of the main-axis overflow this item gives back
    /// (<c>flexShrink</c>). Null = Yoga's default.</summary>
    [Parameter] public float? Shrink { get; set; }

    /// <summary>Main-axis base size (<c>flexBasis</c>): <c>"auto"</c> | <c>"50%"</c>
    /// | <c>"120"</c> (dp). Null = auto.</summary>
    [Parameter] public string? Basis { get; set; }

    // ── Box (the value grammar the shells parse: "12" | "12dp" | "50%" | "auto") ─

    /// <summary>Width, e.g. <c>"300"</c> (dp) or <c>"50%"</c>. Null = auto.</summary>
    [Parameter] public string? Width { get; set; }

    /// <summary>Height, e.g. <c>"100"</c> (dp) or <c>"50%"</c>. Null = auto.</summary>
    [Parameter] public string? Height { get; set; }

    /// <summary>Minimum width. Null = unset.</summary>
    [Parameter] public string? MinWidth { get; set; }

    /// <summary>Maximum width. Null = unset.</summary>
    [Parameter] public string? MaxWidth { get; set; }

    /// <summary>Minimum height. Null = unset.</summary>
    [Parameter] public string? MinHeight { get; set; }

    /// <summary>Maximum height. Null = unset.</summary>
    [Parameter] public string? MaxHeight { get; set; }

    // ── Positioning ───────────────────────────────────────────────────────────

    /// <summary>Positioning mode (<c>position</c>). Null = relative (in flow).</summary>
    [Parameter] public Position? Position { get; set; }

    /// <summary>Top inset. Null = unset.</summary>
    [Parameter] public string? Top { get; set; }

    /// <summary>Right inset. Null = unset.</summary>
    [Parameter] public string? Right { get; set; }

    /// <summary>Bottom inset. Null = unset.</summary>
    [Parameter] public string? Bottom { get; set; }

    /// <summary>Left inset. Null = unset.</summary>
    [Parameter] public string? Left { get; set; }

    /// <summary>Nested content rendered inside this view.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "div");

        // Every AddAttribute below is a no-op when its value is null (an
        // ELEMENT attribute with a null value is not appended to the frame
        // array at all) — that is how "unset" reaches the wire as "absent".
        b.AddAttribute(1, "backgroundColor", BackgroundColor);
        b.AddAttribute(2, "padding", Padding);
        b.AddAttribute(3, "margin", Margin);

        b.AddAttribute(4, "flexDirection", Direction.ToStyleValue());
        b.AddAttribute(5, "justifyContent", Justify.ToStyleValue());
        b.AddAttribute(6, "alignItems", Align.ToStyleValue());
        b.AddAttribute(7, "flexWrap", Wrap.ToStyleValue());
        b.AddAttribute(8, "gap", Gap);

        b.AddAttribute(9, "alignSelf", AlignSelf.ToStyleValue());
        b.AddAttribute(10, "flexGrow", Grow.ToStyleValue());
        b.AddAttribute(11, "flexShrink", Shrink.ToStyleValue());
        b.AddAttribute(12, "flexBasis", Basis);

        b.AddAttribute(13, "width", Width);
        b.AddAttribute(14, "height", Height);
        b.AddAttribute(15, "minWidth", MinWidth);
        b.AddAttribute(16, "maxWidth", MaxWidth);
        b.AddAttribute(17, "minHeight", MinHeight);
        b.AddAttribute(18, "maxHeight", MaxHeight);

        b.AddAttribute(19, "position", Position.ToStyleValue());
        b.AddAttribute(20, "top", Top);
        b.AddAttribute(21, "right", Right);
        b.AddAttribute(22, "bottom", Bottom);
        b.AddAttribute(23, "left", Left);

        b.AddContent(100, ChildContent);

        b.CloseElement();
    }
}
