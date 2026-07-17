using BlazorNative.Core;

namespace BlazorNative.Device;

// ─────────────────────────────────────────────────────────────────────────────
// IGeolocation — Phase 9.0 (M9 DoD #2): the app-facing, DI-injectable ergonomic
// facade over the permission-gated host-call bridge. App code injects THIS, not
// the low-level IMobileBridge — a thin delegate that keeps the composition root's
// bridge plumbing out of component code.
//
// The permission state machine is HOST-SIDE (denial-as-data): a single
// GetCurrentPositionAsync does the whole dance (check -> prompt -> obtain-a-fix /
// note-a-denial) and always resolves with a status VALUE — never an exception,
// never a hang. CheckPermissionAsync is read-only (no prompt) so a UI can SHOW the
// current state without popping a dialog.
// ─────────────────────────────────────────────────────────────────────────────

public interface IGeolocation
{
    /// <summary>Requests-then-fetches the current position: runs the whole
    /// permission dance host-side and returns a <see cref="GeolocationResult"/>
    /// whose <see cref="GeolocationResult.Status"/> is the terminal outcome
    /// (granted / denied / permanent / restricted / unavailable / error). A
    /// non-Granted status carries a null position — denial is DATA. The
    /// <paramref name="ct"/> abandons a never-completing call (a process killed
    /// during the prompt): the pending entry is dropped and the task cancels.</summary>
    ValueTask<GeolocationResult> GetCurrentPositionAsync(CancellationToken ct = default);

    /// <summary>Reads the current permission status WITHOUT prompting — for a UI
    /// that wants to show state before offering a "use my location" action.</summary>
    ValueTask<GeolocationStatus> CheckPermissionAsync(CancellationToken ct = default);
}
