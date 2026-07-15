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
