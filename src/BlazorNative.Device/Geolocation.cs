using BlazorNative.Core;

namespace BlazorNative.Device;

// ─────────────────────────────────────────────────────────────────────────────
// Geolocation — the thin delegate that IS the facade. It holds NO permission
// logic: the whole state machine is host-side (NativeShellBridge on-device,
// DevHostBridge headless), and this type only forwards to
// IMobileBridge.GetCurrentPositionAsync. The IMobileBridge async primitive stays
// in Core precisely so DevHostBridge can mock the tri-state headless — this facade
// rides whatever bridge DI hands it.
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class Geolocation(IMobileBridge bridge) : IGeolocation
{
    public ValueTask<GeolocationResult> GetCurrentPositionAsync(CancellationToken ct = default)
        => bridge.GetCurrentPositionAsync(ct);

    public ValueTask<GeolocationStatus> CheckPermissionAsync(CancellationToken ct = default)
        => bridge.CheckGeolocationPermissionAsync(ct);
}
