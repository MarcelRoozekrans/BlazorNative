using BlazorNative.Components;
using BlazorNative.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorNative.SampleApp;

// ─────────────────────────────────────────────────────────────────────────────
// ClipboardProbe — Phase 5.4 (M5 DoD #6): the consumer that proves the
// size-negotiated clipboard/share bridge slots reach a mounted component.
// SCAFFOLDING, like FocusProbe/HostEventProbe: registered in HostSession's mount
// registry (the same statically-rooted generic Mount<T> idiom) so all three
// surfaces mount the SAME component — .NET (ClipboardProbeTests: DispatchEventCore),
// JVM (Gate 1: Copy/Paste through the dll with an in-memory host), Android/iOS
// instrumented (Gates 2/3: real ClipboardManager / UIPasteboard).
//
// Shape:
//   root div
//     ├─ BnButton "Copy"  → ClipboardWriteAsync(CopyPayload)
//     ├─ BnButton "Paste" → ClipboardReadAsync() → echo
//     ├─ BnButton "Share" → ShareAsync(echo)
//     └─ BnText echo: "" → the pasted clipboard value (mount-pinned text node —
//        BnText always emits the text frame, so transitions are ReplaceText on a
//        stable nodeId, the echo-pinning contract BnDemoTests/FocusProbe use)
//
// The Copy button writes a fixed literal (CopyPayload); Paste reads the host
// clipboard back into the echo. Driving Copy then Paste proves the write→read
// round-trip through the host bridge callbacks — echo shows CopyPayload.
//
// The bridge calls complete synchronously in-memory (dev-host / fake host), so
// on the InlineDispatcher the re-render + frame delivery complete inside the
// dispatch_event export call, exactly like FocusProbe's handlers.
//
// Ledgered as scaffolding in the M5 audit (Phase 5.4).
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class ClipboardProbe : ComponentBase
{
    /// <summary>The literal the Copy button writes — Paste reads it back and
    /// echoes it, so a Copy→Paste round-trip proves the clipboard write/read
    /// path through the host bridge. Distinctive so a stale echo is obvious.</summary>
    internal const string CopyPayload = "clip!";

    private string _echo = "";

    [Inject] public IMobileBridge Bridge { get; set; } = default!;

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "div");

        b.OpenComponent<BnButton>(10);
        b.AddComponentParameter(11, nameof(BnButton.Label), "Copy");
        b.AddComponentParameter(12, nameof(BnButton.OnClick),
            EventCallback.Factory.Create<MouseEventArgs>(this, CopyAsync));
        b.CloseComponent();

        b.OpenComponent<BnButton>(20);
        b.AddComponentParameter(21, nameof(BnButton.Label), "Paste");
        b.AddComponentParameter(22, nameof(BnButton.OnClick),
            EventCallback.Factory.Create<MouseEventArgs>(this, PasteAsync));
        b.CloseComponent();

        b.OpenComponent<BnButton>(30);
        b.AddComponentParameter(31, nameof(BnButton.Label), "Share");
        b.AddComponentParameter(32, nameof(BnButton.OnClick),
            EventCallback.Factory.Create<MouseEventArgs>(this, ShareCurrentAsync));
        b.CloseComponent();

        b.OpenComponent<BnText>(40);                             // the echo
        b.AddComponentParameter(41, nameof(BnText.Text), _echo);
        b.CloseComponent();

        b.CloseElement();
    }

    // Copy writes a fixed literal to the host clipboard.
    private async Task CopyAsync() => await Bridge.ClipboardWriteAsync(CopyPayload);

    // Paste reads the host clipboard back into the echo (EventCallback drives the
    // re-render after the handler completes).
    private async Task PasteAsync() => _echo = await Bridge.ClipboardReadAsync();

    // Share hands the current echo to the host share sheet (fire-and-forget).
    private async Task ShareCurrentAsync() => await Bridge.ShareAsync(_echo);
}
