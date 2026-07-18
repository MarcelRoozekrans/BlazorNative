using BlazorNative.Core;

namespace BlazorNative.Device;

// ─────────────────────────────────────────────────────────────────────────────
// Biometrics — the thin delegate that IS the facade (the Geolocation /
// Notifications twin). It holds NO auth logic: the whole flow is host-side
// (NativeShellBridge on-device, DevHostBridge headless), and this type only
// forwards to the IMobileBridge biometric primitives. IsAvailableAsync collapses
// the wire status to a bool: available iff the check reports Authenticated
// ("present + enrolled + ready").
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class Biometrics(IMobileBridge bridge) : IBiometrics
{
    public ValueTask<BiometricStatus> AuthenticateAsync(string reason, CancellationToken ct = default)
        => bridge.AuthenticateAsync(reason, ct);

    public async ValueTask<bool> IsAvailableAsync(CancellationToken ct = default)
        => await bridge.IsBiometricAvailableAsync(ct).ConfigureAwait(false) == BiometricStatus.Authenticated;
}
