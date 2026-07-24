using BlazorNative.Core;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// NullStringSerializationTests — #209.
//
// A null string field NRE'd inside NativeShellBridge.AppendJsonString, several
// frames below any API the caller invoked. Because the capability entry points
// are `async`, the NRE was captured into the returned ValueTask as a FAULT, so
//
//     await Notifications.ScheduleAsync(default);   // NullReferenceException
//
// threw instead of resolving with a status — breaking the contract the whole
// device milestone is built on: every terminal outcome is a completion carrying
// a status, never an exception, never a hang.
//
// WHY THE EXISTING SUITE WAS BLIND TO IT, which is the more useful half of this
// file. The headless harness records capability calls through DevHostBridge, and
// DevHostBridge.RecordNotification NEVER SERIALIZES Title/Body — it stores the
// struct. So the same null input returned a status normally in every .NET test
// while the on-device serialize-and-marshal path faulted. A green suite said
// nothing about the path that actually runs. Same harness-vs-device divergence
// class as #164 and #191.
//
// These tests therefore drive the REAL serializer (WriteFlatJsonObject, internal,
// reached via InternalsVisibleTo) rather than the recorder. That is the whole
// point: a test that went through DevHostBridge would pass on the broken build.
//
// The nulls are not hypothetical. NotificationSpec is a `readonly record struct`,
// so `default(NotificationSpec).Title` is null WITH NO NULLABLE WARNING, and a
// Title sourced from any `string?` model field is the same story. #196's struct
// sweep looked right past this: it hunted wrong-but-valid ZERO values
// (CaptureOptions.Quality == 0) and concluded the other capability structs were
// safe because "default is a valid empty/zero value". For a null reference field
// that crashes the serializer, it is precisely not.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class NullStringSerializationTests
{
    /// <summary>
    /// THE REGRESSION PIN. A null value serializes as an empty JSON string instead
    /// of throwing — asserted against the real serializer, one level below every
    /// capability builder, so it holds for builders that do not exist yet.
    /// </summary>
    [Fact]
    public void ANullValue_SerializesAsAnEmptyString_RatherThanThrowing()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["action"] = "schedule",
            ["title"] = null!,   // this is exactly what default(NotificationSpec) hands over
            ["body"] = null!,
        };

        string json = NativeShellBridge.WriteFlatJsonObject(map);

        Assert.Contains("\"title\":\"\"", json, StringComparison.Ordinal);
        Assert.Contains("\"body\":\"\"", json, StringComparison.Ordinal);
        // The non-null neighbour must be untouched — a "fix" that flattened every
        // value to "" would satisfy the two asserts above and be useless.
        Assert.Contains("\"action\":\"schedule\"", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// Escaping still works on the SAME call path. The null guard sits at the top of
    /// the escape loop, which is exactly where a careless fix (an early return, or a
    /// separate null branch that forgets to quote) would silently drop escaping — and
    /// a dropped escape is a malformed payload the host parses wrongly, which is far
    /// harder to notice than a crash.
    /// </summary>
    [Fact]
    public void TheNullGuard_DidNotCostTheEscaping()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["quote"] = "a\"b",
            ["backslash"] = "a\\b",
            ["newline"] = "a\nb",
            ["tab"] = "a\tb",
            ["control"] = "ab",   // the < 0x20 arm: no named escape, must become 
        };

        string json = NativeShellBridge.WriteFlatJsonObject(map);

        Assert.Contains("\"quote\":\"a\\\"b\"", json, StringComparison.Ordinal);
        Assert.Contains("\"backslash\":\"a\\\\b\"", json, StringComparison.Ordinal);
        Assert.Contains("\"newline\":\"a\\nb\"", json, StringComparison.Ordinal);
        Assert.Contains("\"tab\":\"a\\tb\"", json, StringComparison.Ordinal);
        Assert.Contains("\"control\":\"a\\u0001b\"", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// The end-to-end shape the issue reported: <c>default(NotificationSpec)</c> carries
    /// a null Title AND a null Body, and serializing them must produce parseable JSON.
    /// The struct's default-ness — the thing that made this reachable without a nullable
    /// warning — is part of the assertion rather than a story in a comment.
    /// </summary>
    [Fact]
    public void DefaultNotificationSpec_HasNullStrings_AndStillSerializes()
    {
        NotificationSpec spec = default;

        // The precondition that makes #209 reachable at all, asserted rather than
        // assumed: if NotificationSpec ever becomes a class, or gains a non-null
        // default, this test should be revisited rather than silently still passing.
        Assert.Null(spec.Title);
        Assert.Null(spec.Body);

        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["action"] = "schedule",
            ["title"] = spec.Title!,
            ["body"] = spec.Body!,
        };

        string json = NativeShellBridge.WriteFlatJsonObject(map);

        Assert.StartsWith("{", json, StringComparison.Ordinal);
        Assert.EndsWith("}", json, StringComparison.Ordinal);
        Assert.Contains("\"title\":\"\"", json, StringComparison.Ordinal);
    }
}
