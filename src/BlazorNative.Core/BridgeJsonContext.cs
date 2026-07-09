using System.Text.Json.Serialization;

namespace BlazorNative.Core;

// ─────────────────────────────────────────────────────────────────────────────
// BridgeJsonContext
//
// AOT-safe JSON serialization context for the IMobileBridge contract types.
//
// IL2026 trim warnings fire on any JsonSerializer.Serialize<T>() / Deserialize<T>()
// call that doesn't pass a JsonTypeInfo<T> or JsonSerializerContext; bridge
// implementations use these context-typed accessors instead. Historical note:
// the WASM-era WasiBridge (deleted Phase 3.2) was the original consumer —
// kept as the AOT-safe serialization surface for the bridge contract types.
// ─────────────────────────────────────────────────────────────────────────────

[JsonSerializable(typeof(BridgeHttpRequest))]
[JsonSerializable(typeof(BridgeHttpResponse))]
[JsonSerializable(typeof(PlatformInfo))]
[JsonSerializable(typeof(NativeEvent))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class BridgeJsonContext : JsonSerializerContext { }
