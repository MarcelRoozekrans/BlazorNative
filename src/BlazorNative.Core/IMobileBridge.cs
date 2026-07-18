namespace BlazorNative.Core;

// ─────────────────────────────────────────────────────────────────────────────
// IMobileBridge
// The .NET-side bridge contract between app code and the native host.
// (Began as the typed C# form of the WASM era's mobile-bridge.wit IDL; since
// Phase 3.1 the wire contract is the hand-declared C ABI — host-registered
// callbacks consumed by NativeShellBridge.)
// On-device, NativeShellBridge implements it over the host callbacks;
// DevHostBridge is the in-process mock for tests and dev harnesses.
// ─────────────────────────────────────────────────────────────────────────────

public interface IMobileBridge
{
    // Navigation
    ValueTask NavigateAsync(string route, CancellationToken ct = default);
    ValueTask<string> GetCurrentRouteAsync(CancellationToken ct = default);

    // Storage  (key/value, maps to SharedPreferences / UserDefaults)
    ValueTask<string?> ReadStorageAsync(string key, CancellationToken ct = default);
    ValueTask WriteStorageAsync(string key, string value, CancellationToken ct = default);
    ValueTask DeleteStorageAsync(string key, CancellationToken ct = default);

    // Network (thin fetch — TLS handled by native layer)
    ValueTask<BridgeHttpResponse> FetchAsync(BridgeHttpRequest request, CancellationToken ct = default);

    // Clipboard + Share (Phase 5.4 — size-negotiated bridge slots). A host that
    // predates these slots surfaces NotSupportedException (the null-slot guard);
    // the dev-host mock and both native shells implement them.
    ValueTask<string> ClipboardReadAsync(CancellationToken ct = default);
    ValueTask ClipboardWriteAsync(string text, CancellationToken ct = default);
    ValueTask ShareAsync(string text, CancellationToken ct = default);

    // Geolocation (Phase 9.0 — M9 DoD #2; the permission pattern's worked
    // example). A SINGLE call that requests-then-fetches: the whole permission
    // dance (check → prompt → obtain-a-fix / note-a-denial) is HOST-SIDE, and
    // the terminal outcome always comes back as a status VALUE — denial is DATA,
    // never an exception and never a hang (the milestone law). On-device this
    // rides the generic HostCallBegin/host_call_complete ABI (NativeShellBridge);
    // DevHostBridge mocks all six statuses headless. The CancellationToken lets a
    // caller abandon a never-completing call (a process killed during the prompt):
    // the pending entry is dropped and the task is cancelled — a cancel, never a
    // leak. A read-only CheckPermissionAsync (no prompt) is provided only so a UI
    // can SHOW the current state without triggering a dialog.
    ValueTask<GeolocationResult> GetCurrentPositionAsync(CancellationToken ct = default);
    ValueTask<GeolocationStatus> CheckGeolocationPermissionAsync(CancellationToken ct = default);

    // Notifications (Phase 9.1 — M9 DoD #3; the FIRST reuse of the 9.0 generic
    // ABI). schedule / show / cancel + the POST_NOTIFICATIONS / UNUserNotification
    // permission, each a permission-gated host call that rides the SAME
    // HostCallBegin/host_call_complete pair geolocation opened — NO struct grow,
    // NO new export, NO drift-pin move (op-enum value + wire vocabulary only). The
    // terminal outcome is always a NotificationStatus VALUE — denial is DATA, never
    // an exception and never a hang (the milestone law). schedule/show/cancel carry
    // NO completion payload (a status is the whole answer); the two permission calls
    // (request may prompt, check never does) reuse geolocation's mode-in-JSON shape.
    // On-device NativeShellBridge maps each op into argsJson{action:…} over the
    // generic InvokeHostCallAsync; DevHostBridge mocks the five statuses headless.
    // The inbound tap-through half (a notification tap → route) is NOT here: it is
    // an unsolicited host→.NET event, not a host-call completion — it rides the
    // cold deep-link path or the reserved "navigate" host-event name.
    ValueTask<NotificationStatus> ScheduleNotificationAsync(NotificationSpec spec, CancellationToken ct = default);
    ValueTask<NotificationStatus> ShowNotificationAsync(NotificationSpec spec, CancellationToken ct = default);
    ValueTask<NotificationStatus> CancelNotificationAsync(int id, CancellationToken ct = default);
    ValueTask<NotificationStatus> RequestNotificationPermissionAsync(CancellationToken ct = default);
    ValueTask<NotificationStatus> CheckNotificationPermissionAsync(CancellationToken ct = default);

    // Biometrics + Secure storage (Phase 9.2 — M9 DoD #4; the SECOND reuse of the
    // 9.0 generic ABI, and it closes the M5 secure-storage deferral). TWO op-enum
    // values (Biometrics = 2, SecureStorage = 3) + two wire-mirrored status enums —
    // and NOTHING else on the ABI: NO struct grow (still 80 bytes / 10 slots), NO
    // new export (still 10), NO drift-pin move. The "pay once (9.0), reuse thrice"
    // bet paying its THIRD draw. Every op is a permission-gated host call riding the
    // SAME HostCallBegin/host_call_complete pair geolocation opened; the action lives
    // INSIDE the flat JSON (geolocation's `mode` precedent).
    //
    // Biometrics: AuthenticateAsync shows an OS biometric prompt and returns a
    // BiometricStatus VALUE — failure / cancellation / lockout / no-hardware are all
    // DATA, never an exception and never a hang (the milestone law). The read-only
    // availability check (never prompts) is the geolocation-check sibling: it returns
    // Authenticated to mean "present + enrolled + ready" (no auth performed — the
    // check-returns-success precedent, geolocation's check returning Granted).
    //
    // Secure storage: an encrypted-at-rest key/value store (Keystore / Keychain
    // host-side), DISTINCT from the plain unencrypted StorageRead/Write/Delete slots
    // (offsets 16/24/32) — this is what M5 deferred. set/get/getWithAuth/delete each
    // return a SecureStorageStatus; get/getWithAuth carry the value back in the
    // OPTIONAL flat-JSON {"value":…} payload host_call_complete has carried since 9.0
    // (geolocation's fix is the first user; this is the second) — NO new export.
    // getWithAuth is THE PAIRING: the secret is bound to biometric auth at the
    // OS-KEY level (the OS itself refuses the plaintext without a fresh auth), so an
    // auth-bound write (requireAuth:true) MUST pair with an auth-bound read. A soft
    // 8 KB cap on the value is enforced at THIS .NET boundary (an oversize value
    // RETURNS a status, never crosses, never crashes — SecretResult.MaxValueBytes).
    ValueTask<BiometricStatus>     AuthenticateAsync(string reason, CancellationToken ct = default);
    ValueTask<BiometricStatus>     IsBiometricAvailableAsync(CancellationToken ct = default);
    ValueTask<SecureStorageStatus> SetSecretAsync(string key, string value, bool requireAuth, CancellationToken ct = default);
    ValueTask<SecretResult>        GetSecretAsync(string key, CancellationToken ct = default);
    ValueTask<SecretResult>        GetSecretWithAuthAsync(string key, string reason, CancellationToken ct = default);
    ValueTask<SecureStorageStatus> DeleteSecretAsync(string key, CancellationToken ct = default);

    // Platform info — sync raw-JSON form + async typed form. (The sync form is
    // a Phase 2.3 WASM-era shape — it read the BLAZOR_PLATFORM_INFO env var on
    // Mono-WASI; today NativeShellBridge builds the JSON from the
    // host-registered PlatformOptions.)
    string PlatformInfo { get; }
    ValueTask<PlatformInfo> GetPlatformInfoAsync(CancellationToken ct = default);

    // Events from the native host → .NET handlers
    event Action<NativeEvent> NativeEvents;
}

// ─────────────────────────────────────────────────────────────────────────────
// Value types  (zero-alloc friendly — structs where possible)
// ─────────────────────────────────────────────────────────────────────────────

public readonly record struct BridgeHttpRequest(
    string Url,
    string Method = "GET",
    string? Body = null,
    IReadOnlyDictionary<string, string>? Headers = null);

public readonly record struct BridgeHttpResponse(
    int StatusCode,
    string Body,
    IReadOnlyDictionary<string, string> Headers);

public readonly record struct PlatformInfo(
    PlatformKind Platform,
    string OsVersion,
    string AppVersion,
    bool IsDebug);

public readonly record struct NativeEvent(
    string Name,
    string? Payload = null);

public enum PlatformKind { DevHost, Android, iOS, Windows, Mac }

// ─────────────────────────────────────────────────────────────────────────────
// Geolocation value types (Phase 9.0 — M9 DoD #2)
//
// GeolocationStatus is the WIRE-MIRRORED tri-state: the host maps each
// platform's native permission/outcome into it host-side, and .NET only ever
// sees the integer + the payload. The numeric values are the ABI contract —
// mirrored byte-identically by the Kotlin and Swift enums (pinned like the
// callback struct). Do NOT reorder: the host passes these integers across
// blazornative_host_call_complete.
//
//   0 Granted             — permission held; a fix was obtained (payload = the fix)
//   1 Denied              — denied THIS time; a later request MAY prompt again
//   2 DeniedPermanently   — "don't ask again" / iOS .denied — only Settings changes it
//   3 Restricted          — parental controls / MDM — the user CANNOT grant it
//   4 LocationUnavailable — permission fine, but services off / no fix
//   5 Error               — unexpected host error (a caught Kotlin/Swift throw)
//
// Denial (1/2/3), unavailability (4) and error (5) are all VALUES, never
// exceptions and never hangs — the awaiting ValueTask always resolves.
// ─────────────────────────────────────────────────────────────────────────────

public enum GeolocationStatus
{
    Granted = 0,
    Denied = 1,
    DeniedPermanently = 2,
    Restricted = 3,
    LocationUnavailable = 4,
    Error = 5,
}

/// <summary>The terminal outcome of a <see cref="IMobileBridge.GetCurrentPositionAsync"/>
/// call: a status and, only when <see cref="GeolocationStatus.Granted"/>, a
/// position. Every non-Granted status carries a null position.</summary>
public readonly record struct GeolocationResult(GeolocationStatus Status, GeolocationPosition? Position);

/// <summary>A single position fix. Crosses the ABI as a flat JSON object of
/// string→string (numbers string-encoded, reusing the fetch-headers serializer);
/// .NET parses it into this typed form.</summary>
public readonly record struct GeolocationPosition(
    double Latitude,
    double Longitude,
    double Accuracy,
    double? Altitude,
    long TimestampUnixMs);

// ─────────────────────────────────────────────────────────────────────────────
// Notification value types (Phase 9.1 — M9 DoD #3)
//
// NotificationStatus is the WIRE-MIRRORED status: the host maps each platform's
// native permission/outcome into it host-side, and .NET only ever sees the
// integer. The numeric values are the ABI contract — mirrored byte-identically by
// the Kotlin (HostCallOp) and Swift (BnHostCallOp) shells at Gates 2/3 (pinned
// like the callback struct). Do NOT reorder: the host passes these integers back
// across blazornative_host_call_complete — the SAME export geolocation uses, with
// NO struct grow and NO new export (the phase headline).
//
// It is geolocation's shape minus LocationUnavailable (no notification analogue):
//
//   0 Granted            — permission held; the op ran (posted / scheduled / cancelled)
//   1 Denied             — denied THIS time; a later request MAY prompt again
//   2 DeniedPermanently  — "don't ask again" / iOS .denied — only Settings changes it
//   3 Restricted         — policy / MDM — the user CANNOT grant it
//   4 Error              — unexpected host error (a caught Kotlin/Swift throw)
//
// Denial (1/2/3) and error (4) are all VALUES, never exceptions and never hangs —
// the awaiting ValueTask always resolves.
// ─────────────────────────────────────────────────────────────────────────────

public enum NotificationStatus
{
    Granted = 0,
    Denied = 1,
    DeniedPermanently = 2,
    Restricted = 3,
    Error = 4,
}

/// <summary>A local notification to schedule or show. Crosses the ABI as the
/// existing flat JSON object of string→string (numbers string-encoded, reusing
/// the fetch-headers serializer — no new serializer). <see cref="Id"/> is the
/// app-chosen 32-bit key that <c>cancel</c> targets (collisions replace, Android
/// <c>notify</c> semantics); <see cref="When"/> null means show immediately, else
/// it is the fire time (crossing as Unix epoch milliseconds); <see cref="Route"/>
/// is the tap-through target (a <c>DEEP_LINK_COMPONENTS</c> key) or null.</summary>
public readonly record struct NotificationSpec(
    int Id, string Title, string Body, DateTimeOffset? When, string? Route);

// ─────────────────────────────────────────────────────────────────────────────
// Biometrics + secure-storage value types (Phase 9.2 — M9 DoD #4)
//
// BiometricStatus and SecureStorageStatus are WIRE-MIRRORED: the host maps each
// platform's native outcome into them host-side, and .NET only ever sees the
// integer (+ the get payload). The numeric values are the ABI contract —
// mirrored byte-identically by the Kotlin (HostCallOp) and Swift (BnHostCallOp)
// shells at Gates 2/3 (pinned like the callback struct). Do NOT reorder: the host
// passes these integers back across blazornative_host_call_complete — the SAME
// export geolocation/notifications use, with NO struct grow and NO new export (the
// phase headline, made falsifiable by SecureBiometricsAbiUnchangedTests).
//
// BiometricStatus — SIX values (a richer terminal set than notifications: a prompt
// can fail-but-retryable, be cancelled, or lock out, so it earns its own enum):
//
//   0 Authenticated  — the user proved presence (evaluatePolicy / onAuthenticationSucceeded).
//                       On a read-only availability check it means "present + enrolled + ready"
//                       (no auth performed — the geolocation-check-returns-Granted precedent).
//   1 Failed         — a biometric was presented and rejected; retry allowed
//   2 Cancelled      — the user dismissed the prompt (or the app cancelled)
//   3 Unavailable    — no hardware, or none enrolled
//   4 LockedOut      — too many failures — temporarily (or permanently) locked
//   5 Error          — unexpected host error (a caught Kotlin/Swift throw)
//
// Failure (1), cancellation (2), unavailability (3), lockout (4) and error (5) are
// all VALUES, never exceptions and never hangs — the awaiting ValueTask always
// resolves. An out-of-range integer maps to Error (still data, never a throw).
// ─────────────────────────────────────────────────────────────────────────────

public enum BiometricStatus
{
    Authenticated = 0,
    Failed = 1,
    Cancelled = 2,
    Unavailable = 3,
    LockedOut = 4,
    Error = 5,
}

// ─────────────────────────────────────────────────────────────────────────────
// SecureStorageStatus — FIVE values. The biometric-gate detail (failed vs
// cancelled vs lockout) folds into AuthFailed for storage — the caller only needs
// "couldn't unlock"; a consumer wanting the finer grain uses IBiometrics.
//
//   0 Ok           — set/delete succeeded; GET FOUND THE VALUE ({"value":…} payload on get)
//   1 NotFound     — get/getWithAuth of an absent key (no payload)
//   2 AuthFailed   — the biometric gate on getWithAuth denied / failed / cancelled / locked out
//   3 Unavailable  — no secure hardware / Keystore unusable / (getWithAuth) biometrics not enrolled
//   4 Error        — unexpected host error (a caught throw, a decrypt failure, malformed/oversize args)
//
// NotFound (1), AuthFailed (2), Unavailable (3) and Error (4) are all VALUES,
// never exceptions and never hangs. Out-of-range → Error.
// ─────────────────────────────────────────────────────────────────────────────

public enum SecureStorageStatus
{
    Ok = 0,
    NotFound = 1,
    AuthFailed = 2,
    Unavailable = 3,
    Error = 4,
}

/// <summary>The terminal outcome of a secure <c>get</c> / <c>getWithAuth</c>: a
/// status and, only when <see cref="SecureStorageStatus.Ok"/>, the stored value.
/// Every other status carries a null value (the <see cref="GeolocationResult"/>
/// twin). The value crosses the intra-process C-ABI as a UTF-8 string in the flat
/// JSON <c>{"value":…}</c> payload — see the security note in
/// <c>docs/bridge-extension.md §(f).9</c>: the wire is trusted (encryption is at
/// rest, not on the wire); the POC's accepted hazard is the in-memory lifetime of
/// non-zeroable plaintext copies, a zeroable-buffer hardening pass being M10.
/// Binary secrets cross base64-encoded.</summary>
public readonly record struct SecretResult(SecureStorageStatus Status, string? Value)
{
    /// <summary>The soft cap on a secret value, enforced at the .NET boundary (an
    /// oversize value RETURNS a status — never crosses, never crashes). Secure
    /// storage is for secrets (tokens, keys), not blobs; a large value is a misuse,
    /// not a store. Recorded as a decision (§8).</summary>
    public const int MaxValueBytes = 8 * 1024;
}
