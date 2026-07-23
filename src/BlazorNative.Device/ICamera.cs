using BlazorNative.Core;

namespace BlazorNative.Device;

// ─────────────────────────────────────────────────────────────────────────────
// ICamera — Phase 9.3 (M9 DoD #5): the app-facing, DI-injectable ergonomic facade
// over camera photo capture. App code injects THIS, not the low-level IMobileBridge
// — a thin delegate (the IGeolocation / INotifications / IBiometrics / ISecureStorage
// sibling) in the SAME 7th package BlazorNative.Device. No 8th package; the M9 device
// roster CLOSES here.
//
// CapturePhotoAsync hands off to the system camera UI and returns a PhotoResult VALUE
// — a user cancel, a denied permission, no camera and a host error are all DATA, never
// an exception, never a hang (the milestone law). THE IMAGE CROSSES AS A FILE PATH:
// on Captured, PhotoResult.Path is a file:// URI into the app's private cache that the
// shell just wrote — a VALID BnImage.Src, so a captured photo feeds straight into the
// M7 image component (the capabilities compose). The bytes never cross the wire.
//
// THE OWNERSHIP BOUNDARY: once the path returns, the APP OWNS THE FILE. This façade
// does NOT delete it (it lives in the shell's cache dir, and BnImage decodes it
// asynchronously); the shell prunes its own capture subdir on each capture as a leak
// backstop. A bridge-mediated delete is a deferred, labelled follow-up (§2c).
//
// CheckAvailabilityAsync reads whether a camera is present + usable WITHOUT launching
// the capture UI (the read-only sibling), so a UI can decide whether to OFFER a camera
// action — and it returns Unavailable on a device with no camera (the honest iOS
// simulator result).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>DI-injectable façade over camera photo capture. Inject this rather than the
/// low-level <see cref="IMobileBridge"/>; <see cref="CapturePhotoAsync"/> hands off to the
/// system camera UI and returns a <see cref="PhotoResult"/> whose
/// <see cref="PhotoResult.Path"/> is a <c>file://</c> URI (a valid <c>BnImage.Src</c>) on
/// capture — a cancel, denial, missing camera or error is DATA, never an exception. The
/// app owns the returned file. <see cref="CheckAvailabilityAsync"/> reports usability
/// without launching the UI. Register it with
/// <see cref="ServiceCollectionExtensions.AddBlazorNativeDevice"/>.</summary>
public interface ICamera
{
    /// <summary>Launches the system camera UI and returns the terminal
    /// <see cref="PhotoResult"/>. On <see cref="CameraStatus.Captured"/> the result's
    /// <see cref="PhotoResult.Path"/> is a <c>file://</c> URI to the captured JPEG
    /// (in the app's private cache) plus its final dimensions and size — hand the path
    /// straight to a <c>BnImage.Src</c> to display it. A cancel / denied / unavailable
    /// / error outcome is DATA (a status with a null path), never a throw. The app OWNS
    /// the file after this returns (see the interface remarks). <paramref name="options"/>
    /// caps the file's long edge and JPEG quality; passing <c>null</c> (the default) uses
    /// the documented defaults — a ~2048 px long edge and JPEG quality 85. NOTE: the
    /// parameter is <see cref="CaptureOptions"/><c>?</c> rather than
    /// <c>CaptureOptions = default</c> ON PURPOSE — <c>default(CaptureOptions)</c> zero-
    /// initialises the struct (<c>MaxDimension=0, Quality=0</c>), bypassing the record's
    /// primary-constructor defaults, so a bare <c>default</c> would silently request the
    /// worst-possible quality (issue #178). <c>null</c> ⇒ <c>new CaptureOptions()</c>,
    /// which DOES run those defaults.</summary>
    ValueTask<PhotoResult> CapturePhotoAsync(CaptureOptions? options = null, CancellationToken ct = default);

    /// <summary>Reads whether a camera is available (present + usable) WITHOUT
    /// launching the capture UI — for a UI that wants to show state before offering a
    /// camera action. Returns <see cref="CameraStatus.Captured"/> to mean "usable"
    /// (no capture ran) and <see cref="CameraStatus.Unavailable"/> when there is no
    /// camera.</summary>
    ValueTask<CameraStatus> CheckAvailabilityAsync(CancellationToken ct = default);
}
