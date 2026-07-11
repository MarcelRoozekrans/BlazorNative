using BlazorNative.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorNative.Runtime;

// ─────────────────────────────────────────────────────────────────────────────
// FocusProbe — Phase 4.2 (M4 DoD #4): the focus/blur proof app. SCAFFOLDING,
// like CompositionProbe: registered in HostSession's mount registry (same
// statically-rooted generic Mount<T> idiom) so all three surfaces mount the
// SAME component — .NET (FocusProbeTests: DispatchEventCore), JVM
// (FocusBlurTest.kt: dispatchEventBlocking through the dll), Android
// instrumented (FocusBlurAndroidTest.kt: requestFocus()/clearFocus() on the
// real EditText — proving WidgetMapper's setOnFocusChangeListener arm).
//
// Shape:
//   root div
//     ├─ BnInput with OnFocus/OnBlur wired (+ its always-on change attach)
//     └─ BnText echo: "" → "focused" / "blurred" (mount-pinned text node —
//        BnText always emits the text frame, so transitions are ReplaceText
//        on a stable nodeId, the echo-pinning contract BnDemoTests use)
//
// Ledgered as scaffolding in the M4 audit (Phase 4.2 triage doc).
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class FocusProbe : ComponentBase
{
    private string _state = "";

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "div");

        b.OpenComponent<BnInput>(10);
        b.AddComponentParameter(11, nameof(BnInput.Placeholder), "focus me");
        b.AddComponentParameter(12, nameof(BnInput.OnFocus),
            EventCallback.Factory.Create<FocusEventArgs>(this, () => _state = "focused"));
        b.AddComponentParameter(13, nameof(BnInput.OnBlur),
            EventCallback.Factory.Create<FocusEventArgs>(this, () => _state = "blurred"));
        b.CloseComponent();

        b.OpenComponent<BnText>(20);                             // the echo
        b.AddComponentParameter(21, nameof(BnText.Text), _state);
        b.CloseComponent();

        b.CloseElement();
    }
}
