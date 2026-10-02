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
// handler instead BLOCKS THE RENDER THREAD on the capture with GetAwaiter().GetResult().
// Until the test releases the capture, the render thread and the dispatch lane are both
// held, which is exactly when Android's old back — a blocking wait on that lane from the
// main thread — hung.
//
// WHY THE CALL IS STARTED ON THE THREAD POOL. Since 16.7 every host call does
// `await Task.Yield()` after its begin, and on the render thread that yield posts the
// rest of the call to the render thread's own queue. Blocking the render thread on a call
// started there would then wait for a continuation that can only run on the thread it is
// blocking: a deadlock, measured on the JVM suite, where BackNoticeTest hung for 30 min.
// Started with Task.Run, the call has no render-thread context, so its yield and its
// completion run on the pool, and the shell's answer unblocks the render thread.
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
    // shell answers the camera call, which is started on the pool so it cannot deadlock.
    private void Hold()
    {
        PhotoResult result = Task.Run(() => Camera.CapturePhotoAsync().AsTask()).GetAwaiter().GetResult();
        _echo = $"{ReleasedPrefix}{result.Status}";
    }
}
