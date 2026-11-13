using System.Text.Json.Serialization;

namespace BlazorNative.WireGen;

/// <summary>One stderr line → forwarded record case, shared by the Android JVM
/// suite and the iOS XCTest suite. Every vector expects the line to be forwarded:
/// the managed level is the only gate.</summary>
public sealed class StderrPumpVector
{
    [JsonPropertyName("name")]       public string Name { get; init; } = "";
    /// <summary>The shell log level in force when the line is pumped (BnLogLevel ordinal).</summary>
    [JsonPropertyName("shellLevel")] public int ShellLevel { get; init; }
    [JsonPropertyName("line")]       public string Line { get; init; } = "";
    /// <summary>The forwarded level (BnLogLevel ordinal).</summary>
    [JsonPropertyName("level")]      public int Level { get; init; }
    [JsonPropertyName("category")]   public string Category { get; init; } = "";
    [JsonPropertyName("message")]    public string Message { get; init; } = "";
    [JsonPropertyName("why")]        public string Why { get; init; } = "";
}

/// <summary>The stderr pump vector manifest (src/stderr-pump-vectors.json).</summary>
public sealed class StderrPumpVectors
{
    [JsonPropertyName("vectors")] public StderrPumpVector[] Vectors { get; init; } = [];
}
