using BlazorNative.Components;
using BlazorNative.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorNative.SampleApp;

// ─────────────────────────────────────────────────────────────────────────────
// HostEventProbe — Phase 5.1 (M5 DoD #5): the consumer that proves a
// host-initiated lifecycle event REACHES a mounted component and re-renders it.
// SCAFFOLDING, like FocusProbe/CompositionProbe: registered in HostSession's
// mount registry (the same statically-rooted generic Mount<T> idiom) so all
// three surfaces mount the SAME component — .NET (HostEventProbeTests:
// DispatchHostEventCore), JVM (Gate 2: dispatchHostEvent through the dll),
// Android instrumented (Gate 3: ActivityScenario.moveToState → onPause reaches
// the screen).
//
// Shape:
//   root div
//     └─ BnText echo: "" at mount → "<name> (<count>)" after each host event
//        (mount-pinned text node — BnText always emits the text frame, so
//        transitions are ReplaceText on a stable nodeId, the echo-pinning
//        contract BnDemoTests/FocusProbe use)
//
// Subscribes to IMobileBridge.NativeEvents in OnInitialized with a SYNC handler
// (BN0014-clean — the dispatch window is synchronous); unsubscribes in Dispose
// so a torn-down probe can't be re-rendered after the fact. The handler bumps a
// counter, records the name, and StateHasChanged() — on the InlineDispatcher the
// re-render + frame delivery complete inside the host_event export call.
//
// Ledgered as scaffolding in the M5 audit (Phase 5.1).
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class HostEventProbe : ComponentBase, IDisposable
{
    private string _lastEvent = "";
    private int _count;

    [Inject] public IMobileBridge Bridge { get; set; } = default!;

    protected override void OnInitialized()
        // SYNC subscription (BN0014: an async handler here would move
        // continuations off the synchronous dispatch window).
        => Bridge.NativeEvents += OnHostEvent;

    private void OnHostEvent(NativeEvent evt)
    {
        _count++;
        _lastEvent = evt.Name;
        // Steady-state re-render: on the InlineDispatcher the diff →
        // UpdateDisplayAsync → frame has fully completed when this returns,
        // so the host_event export delivers the updated echo synchronously.
        StateHasChanged();
    }

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "div");

        b.OpenComponent<BnText>(10);                             // the echo
        b.AddComponentParameter(11, nameof(BnText.Text),
            _count == 0 ? "" : $"{_lastEvent} ({_count})");
        b.CloseComponent();

        b.CloseElement();
    }

    public void Dispose() => Bridge.NativeEvents -= OnHostEvent;
}
