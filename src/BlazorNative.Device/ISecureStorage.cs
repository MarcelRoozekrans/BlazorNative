using BlazorNative.Core;

namespace BlazorNative.Device;

// ─────────────────────────────────────────────────────────────────────────────
// ISecureStorage — Phase 9.2 (M9 DoD #4): the app-facing, DI-injectable ergonomic
// facade over the ENCRYPTED-at-rest secret store (Keystore / Keychain host-side).
// A sibling of IGeolocation / INotifications / IBiometrics in the SAME 7th package
// BlazorNative.Device — a thin delegate over IMobileBridge; no 8th package.
//
// This is the M5 secure-storage deferral, CLOSED (MILESTONE.md:114 — "secure
// storage, consumed by DoD #4"). It is DISTINCT from the plain unencrypted
// key/value store (IMobileBridge.Read/Write/DeleteStorageAsync, the sync
// StorageRead/Write/Delete slots): those stay exactly as they are; this is the
// encrypted, optionally biometric-bound variant.
//
// Every op returns a status VALUE (denial-as-data): NotFound / AuthFailed /
// Unavailable / Error never throw and never hang. get / getWithAuth return a typed
// SecretResult (value only on Ok). GetWithAuthAsync is THE PAIRING: it binds the
// read to a biometric prompt at the OS-KEY level — the OS itself refuses the
// plaintext without a fresh auth. An auth-bound read requires an auth-bound WRITE,
// so SetAsync takes a requireAuth flag; a plain GetAsync of an auth-bound item
// (no prompt) correctly fails AuthFailed. A soft 8 KB value cap is enforced at the
// .NET boundary (SecretResult.MaxValueBytes) — an oversize value returns Error.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>DI-injectable façade over the encrypted-at-rest secret store (Keystore /
/// Keychain host-side), distinct from the plain unencrypted key/value storage on
/// <see cref="IMobileBridge"/>. Inject this rather than the low-level bridge; set, get and
/// delete secrets, each resolving with a <see cref="SecureStorageStatus"/> value (a failure
/// is DATA, never an exception). <see cref="GetWithAuthAsync"/> is the pairing: an
/// auth-bound write (<c>requireAuth: true</c>) can only be read back behind a fresh OS
/// biometric prompt. Register it with
/// <see cref="ServiceCollectionExtensions.AddBlazorNativeDevice"/>.</summary>
public interface ISecureStorage
{
    /// <summary>Stores <paramref name="value"/> under <paramref name="key"/>,
    /// encrypted at rest. When <paramref name="requireAuth"/> is true the item is
    /// provisioned under the OS-key biometric binding so a later
    /// <see cref="GetWithAuthAsync"/> can decrypt it (and a plain
    /// <see cref="GetAsync"/> cannot). Returns the terminal
    /// <see cref="SecureStorageStatus"/> — a failure is DATA. An oversize value
    /// (> <see cref="SecretResult.MaxValueBytes"/>) returns
    /// <see cref="SecureStorageStatus.Error"/> without crossing the wire.</summary>
    ValueTask<SecureStorageStatus> SetAsync(string key, string value, bool requireAuth = false, CancellationToken ct = default);

    /// <summary>Reads a PLAIN (non-auth-bound) secret WITHOUT a prompt. An auth-bound
    /// item correctly fails <see cref="SecureStorageStatus.AuthFailed"/> here; an
    /// absent key is <see cref="SecureStorageStatus.NotFound"/>. Only
    /// <see cref="SecureStorageStatus.Ok"/> carries the value.</summary>
    ValueTask<SecretResult> GetAsync(string key, CancellationToken ct = default);

    /// <summary>THE PAIRING: reads an auth-bound secret behind an OS biometric prompt
    /// (<paramref name="reason"/> is the prompt message), the OS decrypting the value
    /// only after a fresh auth. A denied / failed / cancelled / locked-out gate returns
    /// <see cref="SecureStorageStatus.AuthFailed"/> (no value) — the refusal is DATA.</summary>
    ValueTask<SecretResult> GetWithAuthAsync(string key, string reason, CancellationToken ct = default);

    /// <summary>Deletes a secret by key (idempotent — an absent key still statuses
    /// <see cref="SecureStorageStatus.Ok"/>).</summary>
    ValueTask<SecureStorageStatus> DeleteAsync(string key, CancellationToken ct = default);
}
