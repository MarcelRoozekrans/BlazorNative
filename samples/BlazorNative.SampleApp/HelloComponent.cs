using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorNative.SampleApp;

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
// Phase 3.2: interactive — the button carries @onclick (a tap counter) so the
// M3 DoD #2 round-trip (tap → dispatch_event → handler → re-render → text
// update) has a demo surface. The counter renders into the EXISTING
// fontSize-24 text node, so the re-render is a single ReplaceText patch.
//
// Sequence-number scheme: 0/1/2 for the outer div + its style attrs, then
// 10/11/12 for the text container, 20/21/22 for the button (element /
// onclick attr / content), 30/31 for the input. Gap-leaving keeps the
// BuildRenderTree readable and accommodates future additions without
// renumbering.
// ─────────────────────────────────────────────────────────────────────────────
internal sealed class HelloComponent : ComponentBase
{
    private int _taps;

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "div");
        b.AddAttribute(1, "backgroundColor", "#FFEEAA");
        b.AddAttribute(2, "padding", "16");

        b.OpenElement(10, "div");
        b.AddAttribute(11, "fontSize", "24");
        b.AddContent(12, $"Hello, BlazorNative! (taps: {_taps})");
        b.CloseElement();

        b.OpenElement(20, "button");
        b.AddAttribute(21, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, () => _taps++));
        b.AddContent(22, "Tap");
        b.CloseElement();

        b.OpenElement(30, "input");
        b.AddAttribute(31, "placeholder", "Type here...");
        b.CloseElement();

        b.CloseElement();
    }
}
