using BlazorNative.Components;
using BlazorNative.Core;
using BlazorNative.Device;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorNative.SampleApp;

// ─────────────────────────────────────────────────────────────────────────────
// BackHoldProbe — Phase 16.2 (#346): the page that HOLDS THE RENDER THREAD, so the
// instrumented BackAndroidTest can prove a back press no longer waits on .NET.
// SCAFFOLDING, like HostEventProbe/ClipboardProbe: a Named mount-registry entry, not a
// routed page, so it has no menu row and no deep link (RouteMenuDriftTests enumerates
// ROUTED pages only).
//
// Shape:
//   root div
//     ├─ BnButton "Hold" → a SYNCHRONOUS handler that blocks on a camera capture
//     └─ BnText echo: "idle" → "released:<CameraStatus>" once the capture returns
//
// WHY SYNCHRONOUS. An async handler yields at its first await, and since 16.1 the
// dispatch export returns then, so nothing stays held and a blocking back press would
// return quickly too: a test built on it cannot tell the old code from the new. This
// handler instead calls CapturePhotoAsync().AsTask().GetAwaiter().GetResult() ON THE
// RENDER THREAD. It does not deadlock: the camera call is a host call whose completion
// source runs its continuations asynchronously and whose await uses
// ConfigureAwait(false), and the shell completes it from its own thread. Until the test
// releases the capture, the render thread and the dispatch lane are both held, which is
// exactly when Android's old back — a blocking wait on that lane from the main thread —
// hung.
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class BackHoldProbe : ComponentBase
{
    /// <summary>The echo before the capture returns.</summary>
    internal const string Idle = "idle";

    /// <summary>The echo prefix once the held capture was released.</summary>
    internal const string ReleasedPrefix = "released:";

    private string _echo = Idle;

    [Inject] public ICamera Camera { get; set; } = default!;

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "div");

        b.OpenComponent<BnButton>(10);
        b.AddComponentParameter(11, nameof(BnButton.Label), "Hold");
        b.AddComponentParameter(12, nameof(BnButton.OnClick),
            EventCallback.Factory.Create<MouseEventArgs>(this, Hold));
        b.CloseComponent();

        b.OpenComponent<BnText>(20);                             // the echo
        b.AddComponentParameter(21, nameof(BnText.Text), _echo);
        b.CloseComponent();

        b.CloseElement();
    }

    // Deliberately synchronous: see the file header. Blocks the render thread until the
    // shell answers the camera call.
    private void Hold()
    {
        PhotoResult result = Camera.CapturePhotoAsync().AsTask().GetAwaiter().GetResult();
        _echo = $"{ReleasedPrefix}{result.Status}";
    }
}
