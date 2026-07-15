using BlazorNative.Components;
using BlazorNative.Runtime;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// BnItemsJsonTests — Phase 7.3 Task 1.2 (design decision 3).
//
// The `items` wire grammar's writer half, pinned. The NORMATIVE grammar lives
// in BnItemsJson.cs; the shells' strict parsers (Gates 2/3) are written FROM
// that comment, and these expected strings are the literals a Kotlin/Swift
// parser test can transcribe — the drift-catcher shape FlatJsonTests set for
// the dispatch-args matrix in 3.1.
//
// Pure function — no bridge/session state, so no "host-session" collection
// membership (the FlatJsonTests posture).
// ─────────────────────────────────────────────────────────────────────────────

public sealed class BnItemsJsonTests
{
    /// <summary>U+0001, built as a char so no source-level escape sequence is
    /// involved anywhere a human compares expected-vs-actual.</summary>
    private static readonly string ControlItem = "pre" + (char)1 + "post";

    [Fact]
    public void Write_EmptyList_IsExactlyTwoBrackets()
        => Assert.Equal("[]", BnItemsJson.Write([]));

    [Fact]
    public void Write_PlainItems_NoWhitespaceBetweenTokens()
        => Assert.Equal("""["Alpha","Bravo","Charlie"]""",
            BnItemsJson.Write(["Alpha", "Bravo", "Charlie"]));

    [Fact]
    public void Write_SingleItem_NoSeparator()
        => Assert.Equal("""["only"]""", BnItemsJson.Write(["only"]));

    [Fact]
    public void Write_EmptyString_IsALegalItem()
        => Assert.Equal("""["",""]""", BnItemsJson.Write(["", ""]));

    [Fact]
    public void Write_QuotesAndBackslashes_Escape()
        => Assert.Equal("""["say \"hi\"","C:\\temp\\x"]""",
            BnItemsJson.Write(["say \"hi\"", "C:\\temp\\x"]));

    [Fact]
    public void Write_CommasInsideItems_AreContentNotSeparators()
        // The exact confusion the grammar must survive: an item CONTAINING
        // the array separator. It stays inside its quotes, unescaped.
        => Assert.Equal("""["a,b","c"]""", BnItemsJson.Write(["a,b", "c"]));

    [Fact]
    public void Write_NamedControlChars_UseShortEscapes()
        => Assert.Equal("""["line1\nline2","a\r\tb"]""",
            BnItemsJson.Write(["line1\nline2", "a\r\tb"]));

    [Fact]
    public void Write_OtherControlChars_EscapeAsLowercaseU00xx()
        // The expected wire text is the SIX characters \ u 0 0 0 1 inside the
        // quotes — spelled with a doubled backslash so the C# literal carries
        // the backslash itself, not the control char.
        => Assert.Equal("[\"pre\\u0001post\"]", BnItemsJson.Write([ControlItem]));

    [Fact]
    public void Write_NonAsciiAndEmoji_PassThroughRaw()
        // Raw pass-through, incl. the surrogate PAIR (🎉) — encoded as UTF-8
        // only when the string crosses the ABI, the FlatJson posture.
        => Assert.Equal("[\"héllo — 日本語\",\"party 🎉\"]",
            BnItemsJson.Write(["héllo — 日本語", "party 🎉"]));

    [Fact]
    public void Write_NullItem_ThrowsNamingTheIndex()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => BnItemsJson.Write(["ok", null!, "ok"]));
        Assert.Contains("item 1", ex.Message);
    }

    /// <summary>THE CROSS-WRITER DRIFT PIN: the items grammar's escaping
    /// matrix IS the flat-JSON dispatch-args matrix (BnItemsJson's normative
    /// comment says so) — so hold the two .NET writers to each other,
    /// character for character, over the full matrix. If either writer's
    /// escaping ever moves alone, this reddens before a shell parser can
    /// disagree with either of them.</summary>
    [Theory]
    [InlineData("plain")]
    [InlineData("say \"hi\"")]
    [InlineData("C:\\temp\\x")]
    [InlineData("line1\nline2")]
    [InlineData("a\r\tb")]
    [InlineData("héllo wörld — æøå 日本語")]
    [InlineData("party 🎉 face 😀")]
    [InlineData("")]
    public void Write_EscapesExactlyLikeTheDispatchArgsWriter(string value)
        => AssertMatchesDispatchArgsWriter(value);

    /// <summary>The control-char row of the same pin — a separate fact only
    /// because a raw U+0001 does not survive attribute-literal round-trips in
    /// every toolchain a repo passes through.</summary>
    [Fact]
    public void Write_ControlChar_EscapesExactlyLikeTheDispatchArgsWriter()
        => AssertMatchesDispatchArgsWriter(ControlItem);

    private static void AssertMatchesDispatchArgsWriter(string value)
    {
        // BnItemsJson.Write(["<v>"]) == "[" + segment + "]" and
        // WriteFlatJsonObject({k:<v>}) == "{\"k\":" + segment + "}" — the
        // SAME escaped string literal in both wrappers.
        string itemsSegment = BnItemsJson.Write([value])[1..^1];
        string bridgeJson = NativeShellBridge.WriteFlatJsonObject(
            new Dictionary<string, string> { ["k"] = value });

        Assert.Equal(bridgeJson, "{\"k\":" + itemsSegment + "}");
    }

    /// <summary>…and the segment round-trips through the .NET flat-JSON
    /// PARSER (the same strict parser the shells mirror), so the grammar is
    /// not merely self-consistent between writers — it decodes back to the
    /// exact item.</summary>
    [Theory]
    [InlineData("say \"hi\"")]
    [InlineData("a,b — the separator as content")]
    [InlineData("line1\nline2")]
    [InlineData("party 🎉 日本語")]
    public void Write_SegmentsRoundTripThroughTheStrictParser(string value)
    {
        string itemsSegment = BnItemsJson.Write([value])[1..^1];
        Dictionary<string, string> parsed =
            NativeShellBridge.ParseFlatJsonObject("{\"k\":" + itemsSegment + "}");
        Assert.Equal(value, parsed["k"]);
    }
}
