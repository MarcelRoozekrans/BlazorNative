using BlazorNative.Core;

namespace BlazorNative.Device;

// ─────────────────────────────────────────────────────────────────────────────
// SecureStorage — the thin delegate that IS the facade (the Geolocation /
// Notifications / Biometrics twin). It holds NO storage or crypto logic: the whole
// store is host-side (NativeShellBridge over the Keystore/Keychain on-device,
// DevHostBridge's in-memory dict headless), and this type only forwards to the
// IMobileBridge secret primitives — including the 8 KB cap, enforced at the bridge
// boundary. This facade rides whatever bridge DI hands it.
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class SecureStorage(IMobileBridge bridge) : ISecureStorage
{
    public ValueTask<SecureStorageStatus> SetAsync(string key, string value, bool requireAuth = false, CancellationToken ct = default)
        => bridge.SetSecretAsync(key, value, requireAuth, ct);

    public ValueTask<SecretResult> GetAsync(string key, CancellationToken ct = default)
        => bridge.GetSecretAsync(key, ct);

    public ValueTask<SecretResult> GetWithAuthAsync(string key, string reason, CancellationToken ct = default)
        => bridge.GetSecretWithAuthAsync(key, reason, ct);

    public ValueTask<SecureStorageStatus> DeleteAsync(string key, CancellationToken ct = default)
        => bridge.DeleteSecretAsync(key, ct);
}
