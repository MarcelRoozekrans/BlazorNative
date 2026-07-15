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

    // ── THE STRICT ACCEPTANCE SET (Gate 1 review I1 — DECIDED: STRICT) ────────
    //
    // A parser accepts EXACTLY what the writer emits; every escape not in the
    // char production is malformed; no whitespace, no trailing comma, the whole
    // string must be consumed. BnItemsStrictParser (this test project) is the
    // executable form of that sentence; the vectors below are NORMATIVE — the
    // Kotlin and Swift parsers (Gates 2/3) transcribe parser AND vectors, and
    // must reject every one of these (shell posture: log loudly, render an
    // EMPTY picker — never a wrong one).

    /// <summary>THE NORMATIVE MALFORMED VECTORS: each row is a wire text a
    /// STRICT parser must reject. Lenient-parser habits these pin against:
    /// whitespace tolerance, trailing commas, prefix parsing (not consuming
    /// the whole string), single quotes, JSON escapes outside the char
    /// production (\b, \/), \u for non-controls, uppercase hex, the long \u
    /// spelling of a short-escape control, and non-array roots.</summary>
    [Theory]
    [InlineData("""[ "a"]""")] // whitespace between tokens
    [InlineData("""["a",]""")] // trailing comma
    [InlineData("""["a"]x""")] // trailing garbage — the whole string must be consumed
    [InlineData("['a']")] // single quotes
    [InlineData("[\"\\b\"]")] // \b: JSON-legal, NOT in the char production (the writer spells it \u0008)
    [InlineData("[\"\\f\"]")] // \f: same
    [InlineData("[\"\\/\"]")] // \/: JSON-legal, NOT in the char production ('/' rides raw)
    [InlineData("[\"\\u0041\"]")] // \u for a NON-control ('A' rides raw)
    [InlineData("[\"\\u001F\"]")] // uppercase hex — the writer emits lowercase
    [InlineData("[\"\\u000a\"]")] // the long \u spelling of \n — the writer spells it short
    [InlineData("[\"abc")] // unterminated string
    [InlineData("[\"a\"")] // unterminated array
    [InlineData("\"a\"")] // a bare string is not an items array
    [InlineData("{}")] // an object is not an items array
    [InlineData("")] // empty input
    public void StrictParser_RejectsEveryNormativeMalformedVector(string wire)
        => Assert.Throws<FormatException>(() => BnItemsStrictParser.Parse(wire));

    /// <summary>The raw-control row of the same set — a separate fact because
    /// a raw U+0001 does not survive attribute literals (the ControlItem
    /// lesson above). The writer ALWAYS escapes controls, so a raw one inside
    /// a string is outside the image → malformed.</summary>
    [Fact]
    public void StrictParser_RejectsARawControlCharacterInsideAString()
        => Assert.Throws<FormatException>(
            () => BnItemsStrictParser.Parse("[\"a" + (char)1 + "b\"]"));

    /// <summary>The ACCEPT half: the strict parser decodes the writer's image
    /// back to the exact items — same matrix as the cross-writer pin, so the
    /// three artefacts (writer, reference parser, vectors) hold each other.</summary>
    [Theory]
    [InlineData("plain")]
    [InlineData("say \"hi\"")]
    [InlineData("C:\\temp\\x")]
    [InlineData("line1\nline2")]
    [InlineData("a\r\tb")]
    [InlineData("a,b — the separator as content")]
    [InlineData("héllo wörld — æøå 日本語")]
    [InlineData("party 🎉 face 😀")]
    [InlineData("")]
    public void StrictParser_RoundTripsTheWriterImage(string value)
        => Assert.Equal(new[] { value }, BnItemsStrictParser.Parse(BnItemsJson.Write([value])));

    [Fact]
    public void StrictParser_EmptyArray_IsTheEmptyList()
        => Assert.Empty(BnItemsStrictParser.Parse("[]"));

    [Fact]
    public void StrictParser_MultiItem_RoundTrips()
        => Assert.Equal(new[] { "Alpha", "Bravo", "Charlie" },
            BnItemsStrictParser.Parse(BnItemsJson.Write(["Alpha", "Bravo", "Charlie"])));

    /// <summary>The control-char row of the accept half (see ControlItem).</summary>
    [Fact]
    public void StrictParser_ControlCharItem_RoundTrips()
        => Assert.Equal(new[] { ControlItem },
            BnItemsStrictParser.Parse(BnItemsJson.Write([ControlItem])));

    /// <summary>U+001F / U+007F — the BOUNDARY of the control production,
    /// built as chars (the ControlItem lesson).</summary>
    private static readonly string LastEscapedControl = "a" + (char)0x1F + "b";
    private static readonly string FirstRawUpperChar = "c" + (char)0x7F + "d";

    /// <summary>THE BOUNDARY ROWS (Gate 1 review, nice-to-have 1): U+001F is
    /// the LAST char the writer escapes; U+007F (DEL) rides RAW — "control"
    /// in this grammar means &lt; U+0020, nothing else. Pinned as literal wire
    /// text, held to the dispatch-args writer (the cross-writer pin), and
    /// round-tripped through the strict parser.</summary>
    [Fact]
    public void Write_BoundaryControls_U001fEscapes_U007fRidesRaw()
    {
        Assert.Equal("[\"a\\u001fb\"]", BnItemsJson.Write([LastEscapedControl]));
        Assert.Equal("[\"c\u007fd\"]", BnItemsJson.Write([FirstRawUpperChar]));

        AssertMatchesDispatchArgsWriter(LastEscapedControl);
        AssertMatchesDispatchArgsWriter(FirstRawUpperChar);

        Assert.Equal(new[] { LastEscapedControl, FirstRawUpperChar },
            BnItemsStrictParser.Parse(BnItemsJson.Write([LastEscapedControl, FirstRawUpperChar])));
    }
}
