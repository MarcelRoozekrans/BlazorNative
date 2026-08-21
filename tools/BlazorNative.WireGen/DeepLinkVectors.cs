using System.Text.Json.Serialization;

namespace BlazorNative.WireGen;

/// <summary>One URL → route case, shared by all three shells' test suites.</summary>
public sealed class DeepLinkVector
{
    [JsonPropertyName("url")]   public string Url { get; init; } = "";
    /// <summary>The expected route, or null when the URL must produce none —
    /// rejected, not coerced into some other route.</summary>
    [JsonPropertyName("route")] public string? Route { get; init; }
    [JsonPropertyName("why")]   public string Why { get; init; } = "";
}

/// <summary>The deep-link vector manifest (src/deeplink-vectors.json).</summary>
public sealed class DeepLinkVectors
{
    [JsonPropertyName("vectors")] public DeepLinkVector[] Vectors { get; init; } = [];
}
