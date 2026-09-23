using BlazorNative.Tests.Shared;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// CommentStrippedSourceTests — unit tests for the ONE shared stripper, held
// directly rather than through a pin that happens to call it.
//
// These are NOT drift pins: they never touch the checkout, so they are outside
// the Rule 6 population and `BnRepo.Root()` does not appear below. They are
// ordinary unit tests over `tests/Shared/CommentStrippedSource.cs`, and they
// exist because ten pins in three projects read their subject through that one
// method — a hole in it is a hole in all ten at once, and issue #364's F3 was
// exactly that.
//
// F3, precisely. `Strip` tracked string state with a single `inString` flag that
// RESET AT EVERY NEWLINE. A `"""` run toggled that flag three times and landed
// INSIDE-string; the newline reset then put the raw string's BODY back into CODE
// state, where a `/*` opened a block comment that ran to the next `*/` — which
// can be arbitrarily far away, in another declaration entirely. Everything
// between the two was deleted from the scanned text. That OVER-STRIPS live code,
// and over-stripping is the FALSE-GREEN direction: a call site a pin is looking
// for simply is not there to be found. It is the same direction the unterminated
// block-opener branch was already written to avoid.
// ─────────────────────────────────────────────────────────────────────────────

public class CommentStrippedSourceTests
{
    [Fact]
    public void ARawStringBodyCarryingABlockOpener_DoesNotSwallowTheCodeAfterIt()
    {
        // THE BUG THIS PINS. `"""` toggles the simple string flag three times and lands
        // inside-string; the newline reset then puts the BODY back into code state, where a
        // `/*` opens a block comment that runs to the next `*/` — arbitrarily far. That
        // OVER-STRIPS live code, which is the false-green direction: an auth token between
        // the two would vanish from the scan entirely.
        const string source = """
            val pattern = \"\"\"
            a raw string body containing /* a block opener
            \"\"\"
            val live = Authenticators.DEVICE_CREDENTIAL
            val blockCloser = "*/"
            """;

        string stripped = CommentStrippedSource.Strip(source.Replace("\\\"\\\"\\\"", "\"\"\""));

        Assert.Contains("Authenticators.DEVICE_CREDENTIAL", stripped, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSameBodyWithoutTheBlockOpener_KeepsTheCodeAfterIt()
    {
        // THE NEGATIVE HALF, and the reason the fact above is about the RAW STRING and
        // not about the token. Identical fixture with the `/*` removed: the token
        // survives under the OLD stripper too, so a green here proves nothing on its
        // own — it is only worth anything read against the fact above, which is red
        // without the raw-string state and green with it.
        const string source = """
            val pattern = \"\"\"
            a raw string body containing a block opener
            \"\"\"
            val live = Authenticators.DEVICE_CREDENTIAL
            val blockCloser = "*/"
            """;

        string stripped = CommentStrippedSource.Strip(source.Replace("\\\"\\\"\\\"", "\"\"\""));

        Assert.Contains("Authenticators.DEVICE_CREDENTIAL", stripped, StringComparison.Ordinal);
    }

    [Fact]
    public void ARawStringState_DoesNotLeakIntoTheCodeThatFollowsItsClose()
    {
        // The over-correction guard. Raw-string state deliberately SURVIVES the
        // newline, which is the whole fix — so the close has to actually close, or
        // every `//` and `/* … */` below the first `"""` in a file stops being
        // stripped and the pins start reading prose as live code. That is the
        // opposite direction and it is a false RED, but a pin that reds on a comment
        // is a pin somebody weakens.
        const string source = """
            val pattern = \"\"\"
            a raw string body
            \"\"\"
            // Authenticators.DEVICE_CREDENTIAL is merely DESCRIBED here.
            val live = BiometricManager.Authenticators.BIOMETRIC_STRONG
            """;

        string stripped = CommentStrippedSource.Strip(source.Replace("\\\"\\\"\\\"", "\"\"\""));

        Assert.DoesNotContain("Authenticators.DEVICE_CREDENTIAL", stripped, StringComparison.Ordinal);
        Assert.Contains("BiometricManager.Authenticators.BIOMETRIC_STRONG", stripped, StringComparison.Ordinal);
    }

    [Fact]
    public void ASingleLineRawString_ClosesOnItsOwnLine()
    {
        // `MainActivity.kt:486` is this shape verbatim — a one-line `"""…"""` payload
        // whose body carries ordinary `"` characters. If the open/close pairing were
        // wrong the rest of that file would be read as raw-string body and its
        // comments would stop being stripped, which is how this fix could quietly
        // undo the `//`-stripping the other nine pins depend on.
        const string source = """
            val payload = \"\"\"{"top":"1","left":"2"}\"\"\"
            // Authenticators.DEVICE_CREDENTIAL is merely DESCRIBED here.
            val live = Authenticators.BIOMETRIC_STRONG
            """;

        string stripped = CommentStrippedSource.Strip(source.Replace("\\\"\\\"\\\"", "\"\"\""));

        Assert.DoesNotContain("Authenticators.DEVICE_CREDENTIAL", stripped, StringComparison.Ordinal);
        Assert.Contains("Authenticators.BIOMETRIC_STRONG", stripped, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyStringLiteral_DoesNotOpenARawString()
    {
        // THE FENCE WIDTH, pinned. Narrowing the `"""` match to `""` is the obvious
        // off-by-one in the fix, and it turns every empty string literal in every
        // scanned file into a raw-string OPENER — after which nothing below it is
        // stripped until the next `""` pairs with it, so comments start reading as
        // live code.
        //
        // WHY THIS FACT EXISTS AT ALL: the planned mutation set said "make the `"""`
        // branch match `""` instead → must RED", and against the F3 fixture alone it
        // did NOT — the narrowed fence still opened and closed in the same two places
        // and the token still survived. The mutation was harmless to that fixture and
        // catastrophic to the repo, which is Rule 7 exactly: mutate the PIN's code
        // paths and then go and find the fact that notices.
        const string source = "var empty = \"\";   // Authenticators.DEVICE_CREDENTIAL is merely DESCRIBED here.\n"
                            + "var live = Authenticators.BIOMETRIC_STRONG;\n";

        string stripped = CommentStrippedSource.Strip(source);

        Assert.DoesNotContain("Authenticators.DEVICE_CREDENTIAL", stripped, StringComparison.Ordinal);
        Assert.Contains("Authenticators.BIOMETRIC_STRONG", stripped, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoEmptyStringLiterals_DoNotPairIntoARawStringSpanningTheCodeBetweenThem()
    {
        // The same mutation's SECOND shape, and the one that reaches across lines.
        // Under a `""` fence the two empty literals below pair up, everything between
        // them becomes raw-string body, and the `/* … */` in the middle survives the
        // strip. `Assert.Equal("", x)` is an ordinary thing for a scanned test source
        // to contain, so this is not an adversarial shape.
        const string source = "var a = \"\";\n"
                            + "/* Authenticators.DEVICE_CREDENTIAL is merely DESCRIBED here. */\n"
                            + "var b = \"\";\n"
                            + "var live = Authenticators.BIOMETRIC_STRONG;\n";

        string stripped = CommentStrippedSource.Strip(source);

        Assert.DoesNotContain("Authenticators.DEVICE_CREDENTIAL", stripped, StringComparison.Ordinal);
        Assert.Contains("Authenticators.BIOMETRIC_STRONG", stripped, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOrdinaryStringLiteral_StillBoundsItsMisParseToOneLine()
    {
        // The pre-existing 14.4/15.0 behaviour this change must not disturb: a single
        // `"` toggles, a `//` inside a literal is not a comment, and an UNBALANCED
        // quote is bounded by the newline reset rather than blinding the file. The
        // raw-string state must not be reachable from inside an ordinary literal,
        // which is what the `!inString` guard on the `"""` branch buys.
        const string source = "var docs = \"see http://example.com/readme\";\n"
                            + "var live = Authenticators.DEVICE_CREDENTIAL;\n";

        string stripped = CommentStrippedSource.Strip(source);

        Assert.Contains("example.com/readme", stripped, StringComparison.Ordinal);
        Assert.Contains("Authenticators.DEVICE_CREDENTIAL", stripped, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStrippedText_KeepsOneLinePerSourceLine()
    {
        // Line-number fidelity through the raw-string branch. Four pins report
        // `file:line` at a human, so a branch that consumed a newline without
        // emitting one would misnumber every line below the first `"""` in the file.
        const string source = """
            val pattern = \"\"\"
            line two
            line three
            \"\"\"
            val live = Authenticators.DEVICE_CREDENTIAL
            """;

        string stripped = CommentStrippedSource.Strip(source.Replace("\\\"\\\"\\\"", "\"\"\""));

        Assert.Equal(
            source.Split('\n').Length,
            stripped.Split('\n').Length);
    }
}
