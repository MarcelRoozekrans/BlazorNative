using System.Text.Json.Serialization;

namespace BlazorNative.Core;

// ─────────────────────────────────────────────────────────────────────────────
// BridgeJsonContext
//
// AOT-safe JSON serialization context for the IMobileBridge contract types.
// Required for wasi-wasm builds (Mono-AOT trims reflection-based serializers).
//
// IL2026 trim warnings fire on any JsonSerializer.Serialize<T>() / Deserialize<T>()
// call that doesn't pass a JsonTypeInfo<T> or JsonSerializerContext. WasiBridge
// uses these context-typed accessors instead.
// ─────────────────────────────────────────────────────────────────────────────

[JsonSerializable(typeof(BridgeHttpRequest))]
[JsonSerializable(typeof(BridgeHttpResponse))]
[JsonSerializable(typeof(PlatformInfo))]
[JsonSerializable(typeof(NativeEvent))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class BridgeJsonContext : JsonSerializerContext { }
