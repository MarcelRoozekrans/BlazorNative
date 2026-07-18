using BlazorNative.Components;
using BlazorNative.Core;
using BlazorNative.Device;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorNative.SampleApp;

// ─────────────────────────────────────────────────────────────────────────────
// BnCameraDemo — Phase 9.3 (M9 DoD #5): the routed page that proves camera photo
// capture reaches a mounted component AND that the capabilities COMPOSE — a captured
// file:// path becomes a BnImage.Src. The FOURTH worked example of the permission
// pattern (the THIRD reuse of the 9.0 generic ABI) and the LAST M9 capability. It
// [Inject]s ICamera (the 5th facade, from the 7th package BlazorNative.Device — no
// 8th package).
//
// THE COMPOSITION, made a demo: "Take Photo" → CapturePhotoAsync() → on Captured, the
// returned file:// path is set as the display BnImage's Src — the M7 image component
// consuming the captured file (camera → file → BnImage), and the status/dims echoed
// beside it. On the AVD (Gate 2) the file is a REAL capture; headless (this Gate) the
// DevHostBridge's canned test-image path drives it.
//
// THE M6/M7 LEDGER ITEM, DISCHARGED. A captured photo has a HUGE natural pixel size
// (even downscaled to MaxDimension 2048, that is 2048 dp/pt of intrinsic size), and
// the M7 loaders measure at one file pixel = one dp/pt with NO downsampling — so an
// INTRINSIC BnImage of a captured photo would reflow to thousands of dp and blow the
// layout. So the display BnImage is given an explicit Width+Height (definite → never
// measured → no reflow) with ContentMode="Contain" (aspect-fit inside the box,
// paint-only) — the exact M7 sizing contract, now consumed by a REAL natural-size
// photo rather than a fixture. This is the M6/M7 "revisit ContentMode with a real
// natural-size image" ledger item, discharged here as a demo rule. (The density-assets
// item is NOT tripped: a captured photo is a runtime file, not a bundled @2x asset.)
//
// Denial is DATA (the geolocation/notifications/secure discipline): a Cancelled (the
// user backed out), a Denied (the OS refused, iOS), an Unavailable (no camera, the
// honest simulator) and an Error are all SHOWN as status, never thrown, never hung —
// and the Cancelled/Denied split is echoed distinctly. "Check" reads availability
// without launching the camera UI.
//
// Sample-only (the 9.0/9.1/9.2 template-minimal precedent): BnGeolocationDemo /
// BnNotificationsDemo / BnSecureDemo are sample-only because the template tree is
// pinned minimal; BnCameraDemo joins them — NOT added to the template.
//
// Shape:
//   root div
//     ├─ BnButton "Take Photo" → ICamera.CapturePhotoAsync  → set Src / echo
//     ├─ BnButton "Check"       → ICamera.CheckAvailabilityAsync → echo status
//     ├─ BnImage (definite 240×320, Contain) Src = the captured path
//     └─ BnText echo (the status / dims echo — the ClipboardProbe echo contract)
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class BnCameraDemo : ComponentBase
{
    /// <summary>The route this page is mounted at (the 13th routed page).</summary>
    internal const string Route = "/camera";

    /// <summary>The display BnImage's DEFINITE box — Width+Height so Yoga never calls
    /// the measure func and a multi-megapixel photo cannot reflow the layout (the
    /// M6/M7 ledger discharge). Contain aspect-fits the photo inside it, paint-only.</summary>
    internal const string DisplayWidthDp = "240";
    internal const string DisplayHeightDp = "320";

    /// <summary>Echo prefixes — distinctive so a stale echo is obvious and a denial is
    /// provably DATA. Captured echoes the FINAL dims + size (the composition's proof
    /// the file the path names has real bytes); every other outcome echoes its
    /// status.</summary>
    internal const string StatusPrefix = "status:";
    internal const string CapturedPrefix = "captured:";

    private string _echo = "ready";
    private string? _src;

    [Inject] public ICamera Camera { get; set; } = default!;

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "div");

        b.OpenComponent<BnButton>(10);
        b.AddComponentParameter(11, nameof(BnButton.Label), "Take Photo");
        b.AddComponentParameter(12, nameof(BnButton.OnClick),
            EventCallback.Factory.Create<MouseEventArgs>(this, TakePhotoAsync));
        b.CloseComponent();

        b.OpenComponent<BnButton>(20);
        b.AddComponentParameter(21, nameof(BnButton.Label), "Check");
        b.AddComponentParameter(22, nameof(BnButton.OnClick),
            EventCallback.Factory.Create<MouseEventArgs>(this, CheckAsync));
        b.CloseComponent();

        // THE COMPOSITION: a DEFINITE (Width+Height) BnImage with ContentMode=Contain,
        // whose Src is the captured file:// path. Definite → never measured → no
        // reflow (the M6/M7 ledger discharge); Contain → aspect-fit, paint-only. Before
        // a capture Src is null (no source, the box still reserved by the declared size).
        b.OpenComponent<BnImage>(30);
        b.AddComponentParameter(31, nameof(BnImage.Width), DisplayWidthDp);
        b.AddComponentParameter(32, nameof(BnImage.Height), DisplayHeightDp);
        b.AddComponentParameter(33, nameof(BnImage.ContentMode), ImageContentMode.Contain);
        b.AddComponentParameter(34, nameof(BnImage.Src), _src);
        b.CloseComponent();

        b.OpenComponent<BnText>(40);                             // the echo
        b.AddComponentParameter(41, nameof(BnText.Text), _echo);
        b.CloseComponent();

        b.CloseElement();
    }

    // Each action echoes its returned status/value as DATA — a denial is SHOWN, never
    // thrown, never left hanging (the BnSecureDemo discipline).
    private async Task TakePhotoAsync()
    {
        PhotoResult result = await Camera.CapturePhotoAsync();
        if (result.Status == CameraStatus.Captured)
        {
            // THE COMPOSITION: the captured path feeds the display BnImage as its Src.
            _src = result.Path;
            _echo = $"{CapturedPrefix}{result.Width}x{result.Height}:{result.SizeBytes}";
        }
        else
        {
            // Cancelled / Denied / Unavailable / Error — the status alone, shown as
            // data (the Cancelled vs Denied split is visible), never thrown.
            _echo = $"{StatusPrefix}{result.Status}";
        }
    }

    private async Task CheckAsync()
    {
        CameraStatus status = await Camera.CheckAvailabilityAsync();
        _echo = $"{StatusPrefix}{status}";
    }
}
