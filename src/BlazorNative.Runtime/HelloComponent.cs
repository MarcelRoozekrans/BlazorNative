using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorNative.Runtime;

// ─────────────────────────────────────────────────────────────────────────────
// HelloComponent
//
// The SOLE copy since Phase 3.0e deleted the WASM-era WasiHost. (Began life as
// a Phase 3.0d duplicate of WasiHost's HelloComponent for the
// blazornative_mount registry; the twin died with WasiHost.)
//
// Phase 2.8 Hello demo component — the M2-closing visible artifact. Exercises
// Phase 2.5/2.6 surface in one screenshot:
//   - LinearLayout container with backgroundColor + padding (SetStyle)
//   - Inner TextView with fontSize (SetStyle, sp units)
//   - Button (NodeType variety)
//   - EditText with placeholder (UpdateProp)
//
// Sequence-number scheme: 0/1/2 for the outer div + its style attrs, then
// 10/11/12 for the text container, 20/21 for the button, 30/31 for the input.
// Gap-leaving keeps the BuildRenderTree readable and accommodates future
// additions without renumbering.
// ─────────────────────────────────────────────────────────────────────────────
internal sealed class HelloComponent : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "div");
        b.AddAttribute(1, "backgroundColor", "#FFEEAA");
        b.AddAttribute(2, "padding", "16");

        b.OpenElement(10, "div");
        b.AddAttribute(11, "fontSize", "24");
        b.AddContent(12, "Hello, BlazorNative!");
        b.CloseElement();

        b.OpenElement(20, "button");
        b.AddContent(21, "Tap");
        b.CloseElement();

        b.OpenElement(30, "input");
        b.AddAttribute(31, "placeholder", "Type here...");
        b.CloseElement();

        b.CloseElement();
    }
}
