using BlazorNative.Core;

namespace BlazorNative.Device;

// ─────────────────────────────────────────────────────────────────────────────
// Camera — the thin delegate that IS the facade (the Geolocation / Notifications /
// Biometrics / SecureStorage twin). It holds NO capture or file logic: the whole
// capture (system camera UI, downscale, EXIF normalization, the temp-file write and
// its prune backstop) is host-side (NativeShellBridge over ACTION_IMAGE_CAPTURE /
// UIImagePickerController on-device, DevHostBridge's canned test-image path headless),
// and this type only forwards to the IMobileBridge camera primitives. The image
// crosses as a PATH, so there are no bytes for the facade to touch — it hands the
// PhotoResult (path included) straight back. This facade rides whatever bridge DI
// hands it.
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class Camera(IMobileBridge bridge) : ICamera
{
    // options is CaptureOptions? (not `= default`): a bare default(CaptureOptions) zero-
    // initialises the struct and skips the record's primary-constructor defaults, sending
    // MaxDimension=0/Quality=0 — the #178 footgun. null ⇒ new CaptureOptions(), which runs
    // the 2048/85 defaults; a non-null value passes straight through.
    public ValueTask<PhotoResult> CapturePhotoAsync(CaptureOptions? options = null, CancellationToken ct = default)
        => bridge.CapturePhotoAsync(options ?? new CaptureOptions(), ct);

    public ValueTask<CameraStatus> CheckAvailabilityAsync(CancellationToken ct = default)
        => bridge.CheckCameraAvailabilityAsync(ct);
}
