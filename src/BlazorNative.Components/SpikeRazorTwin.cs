using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorNative.Components;

// ─────────────────────────────────────────────────────────────────────────────
// SpikeRazorTwin — Phase 7.0 SPIKE SCAFFOLDING (7.1 decides what survives).
//
// SpikeRazor.razor, hand-written the pre-7.0 way: the SAME component authored
// as manual BuildRenderTree — the authoring style every component in this
// library used until the spike. SpikeRazorTests mounts both through the real
// host session and asserts the PATCH STREAMS ARE IDENTICAL (mount and after
// every dispatch): the proof that the Razor-compiled component speaks the
// same wire, byte for byte.
//
// Two DELIBERATE differences from the generated code, both invisible on the
// wire (and the twin test is what proves that invisibility):
//   • no AddMarkupContent whitespace frames — hand-written code never emitted
//     them; the renderer's Phase 7.0 Markup arm gives the .razor version's
//     whitespace a slot but no patch, so the streams still match;
//   • sequence numbers differ (the generator numbers per source position) —
//     sequence numbers are diff keys, never wire data.
// Everything else mirrors the bind/event expansion the generator produces:
// BindConverter.FormatValue for the value, CreateBinder for onchange,
// SetUpdatesAttributeName("value") after the pair, a typed MouseEventArgs
// callback for onclick.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The hand-written twin of <c>SpikeRazor.razor</c> — see the file
/// header. Golden-vs-twin is the spike's GREEN bar.</summary>
public sealed class SpikeRazorTwin : ComponentBase
{
    /// <summary>The title text — mirrors SpikeRazor's parameter.</summary>
    [Parameter] public string? Title { get; set; }

    private string _text = "";

    private void Clear() => _text = "";

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "div");
        b.AddAttribute(1, "backgroundColor", "#FFEEAA");
        b.AddAttribute(2, "padding", "16");

        b.OpenElement(3, "span");
        b.AddAttribute(4, "fontSize", "24");
        b.AddContent(5, Title);
        b.CloseElement();

        b.OpenElement(6, "input");
        b.AddAttribute(7, "placeholder", "Type here...");
        b.AddAttribute(8, "value", BindConverter.FormatValue(_text));
        // `?? ""` — CS8601 otherwise (the binder's setter delivers string?; _text
        // is non-nullable). The generated .razor lambda assigns __value directly,
        // but generated code suppresses nullable warnings; the coalesce is
        // wire-invisible for the pinned lifecycle (no null payload is ever
        // dispatched) and golden-vs-twin is the standing proof.
        b.AddAttribute(9, "onchange",
            EventCallback.Factory.CreateBinder(this, __value => _text = __value ?? "", _text));
        b.SetUpdatesAttributeName("value");
        b.CloseElement();

        b.OpenElement(10, "span");
        b.AddContent(11, _text);
        b.CloseElement();

        b.OpenElement(12, "button");
        b.AddAttribute(13, "onclick",
            EventCallback.Factory.Create<MouseEventArgs>(this, Clear));
        b.AddContent(14, "Clear");
        b.CloseElement();

        b.CloseElement();
    }
}
