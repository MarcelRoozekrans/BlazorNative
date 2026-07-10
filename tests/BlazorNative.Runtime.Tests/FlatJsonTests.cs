using BlazorNative.Runtime;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3.1 Gate 1 review follow-up — content-half drift-catchers for the
// flat headers-JSON that crosses the bridge ABI. The .NET writer/parser live
// in NativeShellBridge (WriteFlatJsonObject / ParseFlatJsonObject); Gate 2's
// Kotlin writer/parser must round-trip the SAME matrix (escaping, \uXXXX,
// surrogate pairs, malformed rejection). Pure functions — no bridge state,
// so no "host-session" collection membership needed (the collection that
// serializes all bridge/session singleton access since Phase 3.5).
// ─────────────────────────────────────────────────────────────────────────────

public sealed class FlatJsonTests
{
    // ── Write → Parse round-trips ────────────────────────────────────────────

    [Theory]
    [InlineData("plain", "value")]
    [InlineData("quote", "say \"hi\"")]
    [InlineData("backslash", "C:\\temp\\x")]
    [InlineData("newline", "line1\nline2")]
    [InlineData("carriage-return-tab", "a\r\tb")]
    [InlineData("control-char", "pre\u0001post")]
    [InlineData("non-ascii", "héllo wörld — æøå 日本語")]
    [InlineData("emoji-surrogate-pair", "party 🎉 face 😀")]
    [InlineData("empty-value", "")]
    public void WriteThenParse_RoundTrips(string key, string value)
    {
        var map = new Dictionary<string, string> { [key] = value };

        string json = NativeShellBridge.WriteFlatJsonObject(map);
        Dictionary<string, string> parsed = NativeShellBridge.ParseFlatJsonObject(json);

        Assert.Equal(value, parsed[key]);
        Assert.Single(parsed);
    }

    [Fact]
    public void WriteThenParse_MultiplePairs_AllSurvive()
    {
        var map = new Dictionary<string, string>
        {
            ["Content-Type"] = "text/plain; charset=\"utf-8\"",
            ["X-Path"] = "a\\b\nc",
            ["X-Emoji"] = "🎉",
        };

        Dictionary<string, string> parsed =
            NativeShellBridge.ParseFlatJsonObject(NativeShellBridge.WriteFlatJsonObject(map));

        Assert.Equal(3, parsed.Count);
        foreach ((string key, string value) in map)
            Assert.Equal(value, parsed[key]);
    }

    // ── Writer escaping specifics ────────────────────────────────────────────

    [Fact]
    public void Write_EscapesQuotesBackslashesAndControlChars()
    {
        string json = NativeShellBridge.WriteFlatJsonObject(
            new Dictionary<string, string> { ["k"] = "\"\\\n\u0001" });

        Assert.Equal("""{"k":"\"\\\n\u0001"}""", json);
    }

    [Fact]
    public void Write_EmptyMap_ProducesEmptyObject()
    {
        Assert.Equal("{}", NativeShellBridge.WriteFlatJsonObject(new Dictionary<string, string>()));
    }

    // ── Parser specifics ─────────────────────────────────────────────────────

    [Fact]
    public void Parse_UnicodeEscape_Decodes()
    {
        // The parser receives the six-char sequences \u0041 and \u00e9 (raw string) and must decode them.
        var parsed = NativeShellBridge.ParseFlatJsonObject("""{"k":"\u0041\u00e9"}""");
        Assert.Equal("A" + (char)0xE9, parsed["k"]);
    }

    [Fact]
    public void Parse_SurrogatePairEscapes_DecodeToEmoji()
    {
        // U+1F389 PARTY POPPER crosses as the surrogate-pair escapes \ud83c\udf89.
        var parsed = NativeShellBridge.ParseFlatJsonObject("""{"k":"\ud83c\udf89"}""");
        Assert.Equal(char.ConvertFromUtf32(0x1F389), parsed["k"]);
    }

    [Fact]
    public void Parse_ToleratesWhitespace_BetweenTokens()
    {
        var parsed = NativeShellBridge.ParseFlatJsonObject("  { \"a\" : \"b\" ,\n\t\"c\" : \"d\" }  ");
        Assert.Equal("b", parsed["a"]);
        Assert.Equal("d", parsed["c"]);
    }

    [Fact]
    public void Parse_NullEmptyOrEmptyObject_YieldEmptyMap()
    {
        Assert.Empty(NativeShellBridge.ParseFlatJsonObject(null));
        Assert.Empty(NativeShellBridge.ParseFlatJsonObject(""));
        Assert.Empty(NativeShellBridge.ParseFlatJsonObject("{}"));
        Assert.Empty(NativeShellBridge.ParseFlatJsonObject("  { }  "));
    }

    // ── Malformed inputs → FormatException ───────────────────────────────────

    [Theory]
    [InlineData("not json")]                       // no object at all
    [InlineData("{")]                              // truncated after brace
    [InlineData("""{"a"}""")]                      // missing colon + value
    [InlineData("""{"a":1}""")]                    // non-string value
    [InlineData("{'a':'b'}")]                      // single quotes
    [InlineData("""{"a":"b",}""")]                 // trailing comma
    [InlineData("""{"a":"b" "c":"d"}""")]          // missing comma
    [InlineData("""{"a":"b""")]                    // unterminated object
    [InlineData("""{"a":"b""" + "\\")]             // dangling escape at end
    [InlineData("""{"k":"\x41"}""")]               // unknown escape
    [InlineData("""{"k":"\u12"}""")]               // short hex run
    [InlineData("""{"k":"\u12G4"}""")]             // non-hex digit
    [InlineData("""{"k":"\u 123"}""")]             // whitespace in hex (strictness)
    [InlineData("""{"k":"\u+123"}""")]             // sign in hex (strictness)
    public void Parse_Malformed_ThrowsFormatException(string json)
    {
        Assert.Throws<FormatException>(() => NativeShellBridge.ParseFlatJsonObject(json));
    }

    [Fact]
    public void Parse_MalformedError_DoesNotLeakFullPayload()
    {
        // Header values can carry Set-Cookie/Authorization material — the
        // exception must only surface the failing index + a 32-char prefix.
        string json = """{"padding-padding-padding-padding":"SECRET-TOKEN-VALUE""" ; // unterminated
        var ex = Assert.Throws<FormatException>(() => NativeShellBridge.ParseFlatJsonObject(json));

        Assert.DoesNotContain("SECRET-TOKEN-VALUE", ex.Message);
        Assert.Contains("index", ex.Message);
    }
}
