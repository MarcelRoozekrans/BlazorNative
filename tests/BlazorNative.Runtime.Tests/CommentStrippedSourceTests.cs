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
//
// AND THEN THE FIX FOR F3 DID THE SAME THING WITH THE SIGN FLIPPED, which is why
// half the facts below are about the fix rather than about F3. Its first cut
// tracked raw state with no bound, and C#'s verbatim `@"` + the `""` escape spells
// `@"""` — a three-quote run that opens a fence nothing ever closes. Two files in
// this repo were blind to EOF behind it. The invariant the class now holds is not
// "strip more" or "strip less": NO INPUT MAY MAKE THE STRIPPER BLIND PAST THE
// CONSTRUCT THAT CONFUSED IT.
//
// THE ROSTER — every property the helper's comments claim, and the fact holding it.
// The helper points here rather than naming facts one by one, so a rename is found
// by reading one list instead of by grepping prose.
//
//   F3 itself, a `/*` in a raw body ..... ARawStringBodyCarryingABlockOpener_…
//   …and that the fixture is about the
//   raw string, not about the token ..... TheSameBodyWithoutTheBlockOpener_…
//   raw state closes, and does not leak . ARawStringState_DoesNotLeakIntoTheCode…
//   single-line `"""…"""` pairs .........  ASingleLineRawString_ClosesOnItsOwnLine
//   FENCE WIDTH is three, not two ....... AnEmptyStringLiteral_…                  ×2
//                                         TwoEmptyStringLiterals_…
//   a run of four is consumed WHOLE ..... AFourQuoteRun_IsConsumedAsOneFence
//   `@"""` is not a fence ............... AVerbatimStringOpener_IsNotAFence
//   …and two of them do not PAIR ........ TwoVerbatimStringOpeners_DoNotPairIntoAFence
//   `$$"""` still IS a fence ............ AnInterpolatedRawStringPrefix_IsStillAFence
//   an unterminated fence is refused .... AnUnterminatedFence_DoesNotBlindTheRest…
//   ordinary literals still bounded ..... AnOrdinaryStringLiteral_StillBounds…
//   line numbers survive ................ TheStrippedText_KeepsOneLinePerSourceLine
//
// WHAT THE ROSTER IS NOT. Nothing enforces that the names above still exist — it is
// prose, it can rot, and a renamed fact leaves it stale without reddening anything.
// It is here because the alternative the helper had was naming ONE fact by string in
// a doc comment, which rots the same way and covers a twelfth as much. If a future
// phase wants this mechanical, the honest shape is a fact asserting this class has at
// least N facts, not a `BnRepo.Root()` call: adding one would move a non-pin into the
// Rule 6 pin population, which is worse than the gap it closes.
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
        //
        // THE RAW STRING ON LINE 2 IS LOAD-BEARING and was added after the fence gained
        // its closing-fence precondition. Without it the narrowed fence finds no closer
        // ahead, the precondition refuses the opener for the WRONG REASON, and this
        // fact goes green over the mutation it exists to catch. A fixture that passes
        // because a different rule saved it is a fixture that has stopped testing its
        // own subject.
        const string source = "var empty = \"\";   // Authenticators.DEVICE_CREDENTIAL is merely DESCRIBED here.\n"
                            + "var raw = \"\"\"x\"\"\";\n"
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
    public void AFourQuoteRun_IsConsumedAsOneFence()
    {
        // FENCE RUNS ARE CONSUMED WHOLE. Taking exactly three quotes from a run of
        // four leaves a stray quote behind in code state, which opens an ordinary
        // string and swallows the rest of the line's comment. `ItemsJsonTest.kt:91`
        // and `:106` are this shape in the tree — `"""["a""""` and `""""a""""` — and
        // they mis-parsed exactly this way under the fix's first cut.
        //
        // ISOLATES: whole-run consumption. Take three instead of the run and this reds.
        const string source = "val rows = listOf(\n"
                            + "    \"\"\"[\"a\"\"\"\",   // Authenticators.DEVICE_CREDENTIAL is merely DESCRIBED here.\n"
                            + ")\n"
                            + "val live = Authenticators.BIOMETRIC_STRONG\n";

        string stripped = CommentStrippedSource.Strip(source);

        Assert.DoesNotContain("Authenticators.DEVICE_CREDENTIAL", stripped, StringComparison.Ordinal);
        Assert.Contains("Authenticators.BIOMETRIC_STRONG", stripped, StringComparison.Ordinal);
    }

    [Fact]
    public void AVerbatimStringOpener_IsNotAFence()
    {
        // THE DEFECT THE FIRST CUT OF THE F3 FIX SHIPPED, reproduced at the line that
        // carried it. C# spells a verbatim string `@"` and escapes an embedded quote
        // by DOUBLING it, so a verbatim literal beginning with a quote is `@"""` —
        // three consecutive quotes that open nothing. The rest of the literal then
        // offers only one- and two-quote runs, so no closer ever arrives and the state
        // ran to END OF FILE.
        //
        // This is not a hypothetical spelling. The fixture below is
        // `ShellFrameTableDriftTests.cs:296` with a comment under it;
        // `TemplateDriftTests.cs:1344` is the same shape, and between them 358 lines
        // of comment stripping were switched off inside `PinPopulationTests`' own walk
        // while every pin stayed green.
        //
        // ISOLATES: nothing on its own — it reds only when BOTH the `@` refusal and
        // the closing-fence lookahead are gone, which is the state that shipped. The
        // two rules are isolated one each by the two facts below. It is here because a
        // defect that was live deserves a fact that reproduces it verbatim.
        const string source = "static readonly Regex R = new(@\"\"\"(?<k>[^\"\"]+)\"\"\\s*\");\n"
                            + "// Authenticators.DEVICE_CREDENTIAL is merely DESCRIBED here.\n"
                            + "void live() { var x = Authenticators.BIOMETRIC_STRONG; }\n";

        string stripped = CommentStrippedSource.Strip(source);

        Assert.DoesNotContain("Authenticators.DEVICE_CREDENTIAL", stripped, StringComparison.Ordinal);
        Assert.Contains("Authenticators.BIOMETRIC_STRONG", stripped, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoVerbatimStringOpeners_DoNotPairIntoAFence()
    {
        // WHY THE LOOKAHEAD IS NOT SUFFICIENT ON ITS OWN, and the reason the `@`
        // refusal shipped beside it. A lookahead asks only "does a closer exist
        // ahead"; two `@"""` occurrences in one file each answer yes for the other,
        // and they pair into a raw-string span NEITHER OF THEM OPENED. Bounded, so it
        // never reaches EOF — and still wrong, over a region no reader could predict
        // from either line.
        //
        // ISOLATES: the `@` refusal. Remove it and these two pair, the comment between
        // them becomes raw body, and this reds.
        const string source = "static readonly Regex A = new(@\"\"\"(?<k>[^\"\"]+)\"\"\\s*\");\n"
                            + "// Authenticators.DEVICE_CREDENTIAL is merely DESCRIBED here.\n"
                            + "static readonly Regex B = new(@\"\"\"(?<v>[^\"\"]+)\"\"\\s*\");\n"
                            + "void live() { var x = Authenticators.BIOMETRIC_STRONG; }\n";

        string stripped = CommentStrippedSource.Strip(source);

        Assert.DoesNotContain("Authenticators.DEVICE_CREDENTIAL", stripped, StringComparison.Ordinal);
        Assert.Contains("Authenticators.BIOMETRIC_STRONG", stripped, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnterminatedFence_DoesNotBlindTheRestOfTheFile()
    {
        // THE INVARIANT, stated as a test: no input may make the stripper blind past
        // the construct that confused it. Raw state is entered only when a closing
        // fence already exists ahead, found with the same predicate the closing arm
        // uses — so a state that was entered will be left, for ALL inputs rather than
        // for the ones someone happened to check. An opener with no closer degrades to
        // ordinary quotes and is bounded by the newline reset, which is the same
        // choice the unterminated `/*` branch has always made.
        //
        // ISOLATES: the closing-fence lookahead. Remove it and this runs to EOF.
        const string source = "val pattern = \"\"\"\n"
                            + "// Authenticators.DEVICE_CREDENTIAL is merely DESCRIBED here.\n"
                            + "val live = Authenticators.BIOMETRIC_STRONG\n";

        string stripped = CommentStrippedSource.Strip(source);

        Assert.DoesNotContain("Authenticators.DEVICE_CREDENTIAL", stripped, StringComparison.Ordinal);
        Assert.Contains("Authenticators.BIOMETRIC_STRONG", stripped, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInterpolatedRawStringPrefix_IsStillAFence()
    {
        // THE NEGATIVE HALF OF THE `@` RULE, and it needs one: the look-back walks
        // over C#'s `$` and `@` prefix characters and refuses only when it finds an
        // `@`. Widen it to any prefix character and `$$"""` — a genuine interpolated
        // raw string, live at `DevHostBridge.cs:430` — stops being a fence, its body
        // returns to code state, and the `//` inside the URL below eats the rest of
        // the line. That is F3's own failure mode coming back through the fix.
        //
        // ISOLATES: the `@`-only look-back. Refuse on `$` too and this reds.
        const string source = "var json = $$\"\"\"{\"url\":\"https://x/y\"}\"\"\";\n"
                            + "var live = Authenticators.BIOMETRIC_STRONG;\n";

        string stripped = CommentStrippedSource.Strip(source);

        Assert.Contains("https://x/y", stripped, StringComparison.Ordinal);
        Assert.Contains("Authenticators.BIOMETRIC_STRONG", stripped, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOrdinaryStringLiteral_StillBoundsItsMisParseToOneLine()
    {
        // The pre-existing 14.4/15.0 behaviour this change must not disturb: a single
        // `"` toggles, a `//` inside a literal is not a comment, and an UNBALANCED
        // quote is bounded by the newline reset rather than blinding the file.
        //
        // THIS FIXTURE CONTAINS NO `"""`, so it does NOT exercise the `!inString`
        // guard on the fence and would stay green with that guard deleted. An earlier
        // version of this comment said otherwise. The guard is defence in depth and
        // the helper says so at its own site — it is reachable only after the ordinary
        // flag has already mis-parsed, so there is no valid source shape to pin it
        // with. Claiming a fact covers a branch it never enters is Rule 7's scar in
        // miniature, which is why the correction is written here rather than dropped.
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
