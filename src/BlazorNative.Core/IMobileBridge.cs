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
