using System.Text.RegularExpressions;
using Xunit;
using BlazorNative.Tests.Shared;

namespace BlazorNative.Renderer.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// ShellStyleTableDriftTests — Phase 6.1 Gate 2 review (finding I1), REDUCED BY
// #255 to the one thing codegen cannot do.
//
// ── WHAT THIS FILE USED TO BE, AND WHY MOST OF IT IS GONE ────────────────────
// The SetStyle allow-list is the shells' ROUTING TABLE, and it used to exist as
// four hand-written copies: .NET's set, Kotlin's `YOGA_STYLES`, the `.mm`'s
// `kYogaStyles`, and the Swift test suite's own `routedStyleNames`. This file
// held seven [Fact]s that parsed those literals back out of the source with
// regexes and asserted they agreed — plus two more for the scroll-ignore pair.
//
// That was the right pin for hand-written mirrors, and it caught real drift. But
// it could only ever catch divergence AFTER somebody wrote it, and it required
// every copy to keep a parser-friendly shape — the reason those files carried
// instructions like "keep the declaration a plain `setOf` of quoted names,
// declared at the start of its line".
//
// #255 made the divergence unrepresentable instead: src/wire-vocabulary.json is
// the only place the names live, and tools/BlazorNative.WireGen emits every
// copy. Set-equality between copies is no longer a fact worth asserting — they
// are the same bytes from the same source, and WireVocabularyCodegenTests
// (BlazorNative.Runtime.Tests) proves it by re-running the emitters in-process
// and byte-comparing the committed output.
//
// So the seven mirror-comparison facts are DELETED rather than re-pointed. A
// test asserting that two generated files agree would assert only that the
// generator is deterministic, which is not the property anybody was worried
// about.
//
// ── WHAT SURVIVES, AND WHY IT MUST ───────────────────────────────────────────
// One fact, because it pins something the manifest cannot: **being ON the table
// only says the shell ROUTES the name to Yoga — whether a SETTER exists at the
// other end is a separate fact.** A routed name with no dispatch arm behaves
// exactly like a rejected one: Kotlin's `when` lands on
// `else -> logIgnore("routing bug")`, and .NET emits the style, the wire carries
// it, Android drops it and iOS honours it. Two frame tables disagree and the
// ENGINE gets the blame.
//
// Codegen makes that failure MORE likely, not less: adding a name to the
// manifest now updates four tables in one command, and the one thing it cannot
// write for you is the implementation. This test is what makes that half loud.
//
// ── PHASE 15.1 — THE EXTRACTOR WAS COLLECTING THE WRONG THING ────────────────
// Census §5.1 found the one demonstrated false-green channel in the pin
// population, and it was here. `ParseNameTable` collected EVERY quoted string in
// the dispatch body, so value keywords (`row`, `center`, `nowrap`, `absolute`),
// comment text and log-message prose sat in the same bag as arm labels — 48
// strings for a dispatch with 26 arms.
//
// What that buys an unwritten arm depends on WHICH set the bag is compared
// against, and that is luck rather than design: a manifest style whose arm was
// never written reads as dispatched as soon as some unrelated quoted string in
// the body matches its name. The bag really did hold genuine manifest names —
// `gap`, a live yogaStyles entry, was in the Swift body's set, put there by a
// commented-out example. That one was never checked against a name it could
// collide with, because the Swift body's set is only ever compared with the
// three VISUAL styles. It is evidence of the defect operating on a real name,
// not evidence of an assertion that flipped.
//
// Two things changed, and neither is sufficient alone. The extractor now anchors
// on ARM-LABEL POSITION and outermost depth; and
// TheNameExtractor_CollectsArmLabelsOnly_NotEveryQuotedString is the negative
// control that reds if it is ever widened back. The third change is smaller and
// separate: the three facts now floor the set they ITERATE, not only the set they
// subtract — see FloorTheIteratedSet.
//
// iOS's equivalent is pinned at RUNTIME, in its own lane, by
// BnYogaStyleParserTests.testEveryRoutedNameReachesASetter — which feeds every
// routed name a legal value and demands rc == 1. Since #255 that suite reads the
// generated BnWireVocabulary rather than its own hand-copy, so it can no longer
// quietly stop covering a name.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class ShellStyleTableDriftTests
{
    private const string KotlinYogaLayout =
        "src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/YogaLayout.kt";

    /// <summary>The VISUAL dispatch lives in the widget mapper, not the Yoga layout —
    /// the two halves of the partition are routed in two different files, which is a
    /// large part of why only one of them had a pin.</summary>
    private const string KotlinWidgetMapper =
        "src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/WidgetMapper.kt";

    /// <summary>Kotlin's `setStyle` body: from the declaration to the first line that
    /// is exactly a 4-space-indented `}` — the function's own closing brace (every
    /// brace inside it is indented deeper).</summary>
    private const string KotlinSetStyleBody =
        @"(?ms)^    fun setStyle\(nodeId: Int, property: String, value: String\?\) \{(?<body>.*?)^    \}";

    /// <summary>THE DISPATCH PIN — the half of the contract the routing table does
    /// not carry (Gate 3 review, I4; kept and re-argued by #255).
    ///
    /// Kotlin's `setStyle` returns Unit, so there is no return code to demand the way
    /// the iOS parser test does. Its dispatch is pinned at the SOURCE instead: every
    /// name the manifest routes to Yoga must appear as a `when` literal inside
    /// `setStyle`'s own body.
    ///
    /// Weaker than the iOS pin, and honestly so: this proves the arm is WRITTEN, not
    /// that it reaches a setter. It still catches the failure that actually happens —
    /// and #255 makes that failure easier to cause, because adding a name to the
    /// manifest now updates every TABLE in one command while leaving every
    /// IMPLEMENTATION untouched.</summary>
    [Fact]
    public void AndroidSetStyleDispatch_HasAnArmForEveryYogaStyle()
    {
        // The routed set comes from the GENERATED vocabulary now, not from a regex
        // over Kotlin source: the Kotlin table is `BnWireVocabulary.YOGA_STYLES`, so
        // there is no literal left in that file to parse. Reading .NET's set is the
        // same question asked of the same source of truth.
        var routed = NativeRenderer.YogaStyleAttributes;
        FloorTheIteratedSet(routed, MinimumYogaStyles, nameof(NativeRenderer.YogaStyleAttributes));
        var dispatched = ParseNameTable(KotlinYogaLayout, KotlinSetStyleBody);

        var missing = routed.Except(dispatched).ToList();

        Assert.True(
            missing.Count == 0,
            $"YogaLayout.setStyle has no `when` arm for: {Join(missing)}.\n"
            + "The name is in the wire vocabulary, so `owns()` routes it to the Yoga node — and "
            + "the `when` drops it on `else -> logIgnore(\"routing bug\")`. .NET emits the style, "
            + "the wire carries it, Android ignores it and iOS honours it: the two frame tables "
            + "disagree and the ENGINE gets the blame.\n"
            + "Adding a name to src/wire-vocabulary.json updates every TABLE; it cannot write the "
            + "SETTER for you, and this is the test that says so.");
    }

    /// <summary>iOS routes the visual half in its own widget mapper, exactly as Android
    /// does — the Swift twin of <see cref="KotlinWidgetMapper"/>.</summary>
    private const string AppleWidgetMapper =
        "src/BlazorNative.Apple/BnHost/BnWidgetMapper.swift";

    /// <summary>Kotlin's `handleSetStyle` body — the VISUAL dispatch, which routes
    /// every name `owns()` did NOT claim.</summary>
    private const string KotlinHandleSetStyleBody =
        @"(?ms)^    private fun handleSetStyle\(p: RenderPatch\.SetStyle\) \{(?<body>.*?)^    \}";

    /// <summary>THE VISUAL-HALF DISPATCH PIN — the hole this file did not cover, and
    /// the reason three names sat in the table for months doing nothing.
    ///
    /// The Yoga half has been pinned since Phase 6.1 by the fact above. The VISUAL
    /// half never was — and an audit found the consequence: `color`, `fontWeight`,
    /// `background` and `style` were all in `VisualStyleAttributes`, all pinned there
    /// by StyleAttributePartitionTests as "belonging to the view", and **none of them
    /// had an arm in either shell**. Every use was accepted by the routing table and
    /// then dropped on `else -> "not yet supported"`. That is exactly the failure the
    /// wire-vocabulary manifest's own documentation says the apparatus exists to
    /// prevent, and it was live the whole time because the assertion covered one half
    /// of a two-half partition.
    ///
    /// So this is the missing symmetry, not a new idea: **every VISUAL name must have
    /// a dispatch arm too.** A name with no producer is a defensible thing to ledger;
    /// a name the table *accepts* with no arm is not.</summary>
    [Fact]
    public void AndroidSetStyleDispatch_HasAnArmForEveryVisualStyle()
    {
        var visual = NativeRenderer.VisualStyleAttributes;
        FloorTheIteratedSet(visual, MinimumVisualStyles, nameof(NativeRenderer.VisualStyleAttributes));
        var dispatched = ParseNameTable(KotlinWidgetMapper, KotlinHandleSetStyleBody, "handleSetStyle");

        var missing = visual.Except(dispatched).ToList();

        Assert.True(
            missing.Count == 0,
            $"WidgetMapper.handleSetStyle has no arm for: {Join(missing)}.\n"
            + "The name is in VisualStyleAttributes, so the renderer routes it to the SetStyle "
            + "wire and `owns()` declines it — which lands it on "
            + "`else -> \"SetStyle … not yet supported\"`. The style is accepted and then DROPPED, "
            + "silently, on every frame.\n"
            + "Either implement the arm in BOTH shells, or remove the name from "
            + "src/wire-vocabulary.json. A name in the table with no arm is the one state that is "
            + "not allowed.");
    }

    /// <summary>Swift's `handleSetStyle` body — the same VISUAL dispatch, same shape:
    /// a `switch property` whose `default:` warns, inside a function whose closing brace
    /// is the first 4-space-indented `}`.</summary>
    private const string AppleHandleSetStyleBody =
        @"(?ms)^    private func handleSetStyle\(nodeId: Int32, property: String, value: String\?\) \{(?<body>.*?)^    \}";

    /// <summary>THE VISUAL-HALF DISPATCH PIN, iOS SIDE — and the reason it is here rather
    /// than in the iOS suite.
    ///
    /// iOS pins its YOGA half at RUNTIME, in its own lane
    /// (`BnYogaStyleParserTests.testEveryRoutedNameReachesASetter` feeds every routed name
    /// a legal value and demands rc == 1). That works because `bn_yoga_node_set_style`
    /// returns an int whose fall-through value is 0. **`BnWidgetMapper.handleSetStyle`
    /// returns Void**, exactly like Kotlin's `setStyle` — there is no rc to demand, so the
    /// same trick is unavailable and the dispatch is pinned at the SOURCE instead.
    ///
    /// Which puts it in this file by necessity: `build-test` is the one required lane where
    /// the .NET set and BOTH shells' sources are checkout-visible. The iOS lane cannot see
    /// `VisualStyleAttributes`, and the Android lane cannot see the `.swift`.
    ///
    /// Without this, the fix that closed the visual hole was HALF a fix: Android could no
    /// longer accept a visual name with no arm, and iOS still could — the same silent drop,
    /// on one platform, which is the precise shape of every parity bug this repo has
    /// chased.</summary>
    [Fact]
    public void AppleSetStyleDispatch_HasAnArmForEveryVisualStyle()
    {
        var visual = NativeRenderer.VisualStyleAttributes;
        FloorTheIteratedSet(visual, MinimumVisualStyles, nameof(NativeRenderer.VisualStyleAttributes));
        var dispatched = ParseNameTable(AppleWidgetMapper, AppleHandleSetStyleBody, "handleSetStyle");

        var missing = visual.Except(dispatched).ToList();

        Assert.True(
            missing.Count == 0,
            $"BnWidgetMapper.handleSetStyle has no `case` for: {Join(missing)}.\n"
            + "The name is in VisualStyleAttributes, so the renderer routes it to the SetStyle "
            + "wire and the Yoga router declines it — which lands it on "
            + "`default: BnLog.warn(\"… not yet supported\")`. The style is accepted and then "
            + "DROPPED, silently, on iOS alone, while Android honours it: two frame tables that "
            + "disagree for a reason no frame assertion can see.\n"
            + "Either implement the arm in BOTH shells, or remove the name from "
            + "src/wire-vocabulary.json.");
    }

    // ── The parser ───────────────────────────────────────────────────────────

    /// <summary>Kotlin's arm-label shape, read off both dispatch bodies: a line whose
    /// first non-space text is one or more comma-separated quoted names followed by
    /// <c>-&gt;</c> (`"flexDirection" -&gt;`, `"top", "right", "bottom", "left" -&gt; {`).
    /// The indentation is captured because it is what separates a TOP-LEVEL arm of
    /// `when (property)` from a nested arm of `when (value)` — see
    /// <see cref="ParseNameTable"/>.</summary>
    private const string KotlinArmLabel =
        @"^(?<indent>[ ]*)(?<labels>""[^""\r\n]+""(?:[ ]*,[ ]*""[^""\r\n]+"")*)[ ]*->";

    /// <summary>Swift's case-label shape, read off `BnWidgetMapper.handleSetStyle`:
    /// `case "backgroundColor":`. Same capture, same reason.</summary>
    private const string SwiftCaseLabel =
        @"^(?<indent>[ ]*)case[ ]+(?<labels>""[^""\r\n]+""(?:[ ]*,[ ]*""[^""\r\n]+"")*)[ ]*:";

    /// <summary>Every name in ARM-LABEL POSITION inside the declaration
    /// <paramref name="pattern"/> matches in the shell source at
    /// <paramref name="relativePath"/>.
    ///
    /// **NOT every quoted string in the body, which is what this used to collect and
    /// is the defect phase 15.1 closed** (census §5.1). Yoga's VALUE vocabulary
    /// overlaps its PROPERTY vocabulary — `row`, `column`, `center`, `nowrap`,
    /// `absolute` are all value literals of a `when (value)` arm — and comments carry
    /// quoted names too: the Swift body's `OpenElement("scroll") + AddAttribute("gap",
    /// …)` example put **`gap`, a real manifest Yoga style name**, into the bag with
    /// no arm behind it.
    ///
    /// **Whether a name in that bag flips an assertion depends on which set the bag is
    /// compared against — and that is luck, not design.** A manifest style whose arm
    /// was never written reads as dispatched the moment some unrelated quoted string
    /// in the body matches its name. `gap` never did flip one: the Swift body's set is
    /// only ever compared with `VisualStyleAttributes`, which is `backgroundColor`,
    /// `color` and `fontSize`, and iOS pins its Yoga half at runtime through a
    /// different mechanism entirely. It shows the noise collection reaching a GENUINE
    /// manifest name rather than a synthetic probe, which is the reason to narrow the
    /// extractor rather than to argue about it.
    ///
    /// Two anchors do the narrowing, and both are derived from the shell sources
    /// rather than assumed:
    ///
    ///  1. **Shape** — the label must sit in arm position for the file's language
    ///     (<see cref="KotlinArmLabel"/>, <see cref="SwiftCaseLabel"/>). Prose,
    ///     log-message text and call arguments cannot reach that position.
    ///  2. **Depth** — of the arm lines found, only the SHALLOWEST-indented ones are
    ///     kept. The top-level arms of `when (property)` sit one level inside the
    ///     function; the nested arms of `when (value)` sit deeper. Nothing else
    ///     separates the two, because a nested arm has the identical shape.
    ///
    /// Fails loudly when the declaration cannot be found, and now also when the body
    /// contains no arm at all: either failure means the pin has lost its subject.
    ///
    /// **What this does NOT cover** (Rule 5), all four measured rather than guessed:
    ///
    ///  - **Depth is read from LEADING SPACES.** A shell reformatted to tabs, or one
    ///    that indented a nested arm more shallowly than a top-level one, would be
    ///    misread. No Kotlin or Swift formatter produces either; all three bodies were
    ///    checked and contain no tab. Both misreadings push NESTED value keywords back
    ///    into the set, which is precisely what
    ///    <see cref="TheNameExtractor_CollectsArmLabelsOnly_NotEveryQuotedString"/>
    ///    reds on — so they fail LOUD.
    ///  - **A LINE-commented arm is correctly excluded** — `// "flexGrow" -> …` does
    ///    not start with a quote, so the anchor misses it and the fact reds naming
    ///    `flexGrow`. Mutation-verified. The old every-quoted-string extractor passed
    ///    that mutation; this is one of the holes the narrowing closed.
    ///  - **A BLOCK-commented arm is NOT.** An arm inside `/* … */` keeps its
    ///    indentation and its shape, so it is collected and the pin stays green over a
    ///    dispatch that no longer exists. Mutation-verified, and it is a false GREEN —
    ///    Rule 5's defect direction, not its footnote direction. It is **bounded by
    ///    inspection, not by the parser**: none of the three bodies contains a `/*` at
    ///    all, verified by count. The real fix is Rule 8 consolidation — route this
    ///    through `CommentStrippedSource`, which is string-literal-aware and already
    ///    handles Kotlin and Swift — and that is blocked on the helper living in
    ///    `BlazorNative.Runtime.Tests` rather than in `tests/Shared`. **This limit
    ///    predates 15.1 and the narrowing did not widen it**: the old extractor
    ///    collected block-commented arms too, along with everything else.
    ///  - **It does not understand `#if`.** None of the three bodies has one.</summary>
    private static HashSet<string> ParseNameTable(string relativePath, string pattern, string what = "setStyle")
    {
        var source = ReadShellSource(relativePath);
        var match = Regex.Match(source, pattern, RegexOptions.Singleline);

        Assert.True(match.Success,
            $"could not find `{what}` in {relativePath} (pattern: {pattern}). It moved or its "
            + "signature changed — this dispatch pin IS the contract, so re-point it deliberately "
            + "rather than deleting it.");

        var body = match.Groups["body"].Value;
        var armPattern = ArmLabelPatternFor(relativePath);
        var arms = Regex.Matches(body, armPattern, RegexOptions.Multiline).Cast<Match>().ToList();

        Assert.True(arms.Count > 0,
            $"found `{what}` in {relativePath} but not one arm label inside it "
            + $"(pattern: {armPattern}). The dispatch was rewritten into some other shape, or it "
            + "was re-indented with tabs — either way this pin can no longer see the thing it "
            + "guards, so it reds instead of passing over an empty set. Re-point it deliberately.");

        var outermost = arms.Min(a => a.Groups["indent"].Value.Length);

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var arm in arms.Where(a => a.Groups["indent"].Value.Length == outermost))
            foreach (Match label in Regex.Matches(arm.Groups["labels"].Value, "\"([^\"]+)\""))
                names.Add(label.Groups[1].Value);

        Assert.NotEmpty(names);
        return names;
    }

    /// <summary>The arm-label pattern for the shell source's language. An unknown
    /// extension throws rather than defaulting: a third shell would otherwise be
    /// parsed with the wrong grammar and come back empty.</summary>
    private static string ArmLabelPatternFor(string relativePath) => relativePath switch
    {
        var p when p.EndsWith(".kt", StringComparison.Ordinal) => KotlinArmLabel,
        var p when p.EndsWith(".swift", StringComparison.Ordinal) => SwiftCaseLabel,
        _ => throw new InvalidOperationException(
            $"no arm-label grammar for {relativePath} — add one rather than letting this pin "
            + "parse a language it does not know and return an empty set"),
    };

    /// <summary>The OLD extractor, kept ONLY as the thing the negative control rules
    /// out: every quoted string in the body, arm or not. Nothing else may call it.</summary>
    private static HashSet<string> EveryQuotedString(string relativePath, string pattern)
    {
        var match = Regex.Match(ReadShellSource(relativePath), pattern, RegexOptions.Singleline);
        Assert.True(match.Success, $"could not find the declaration in {relativePath}");

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match name in Regex.Matches(match.Groups["body"].Value, "\"([^\"]+)\""))
            names.Add(name.Groups[1].Value);
        return names;
    }

    /// <summary>THE NEGATIVE CONTROL for the extractor's over-match direction
    /// (census §5.1, pin standard Rule 3's second form — *a known-mismatching subject
    /// the detector must still reject*).
    ///
    /// Before phase 15.1 <see cref="ParseNameTable"/> collected EVERY quoted string in
    /// the dispatch body — value keywords, comment text and log-message prose landed
    /// in the same bag as arm labels. Yoga's VALUE vocabulary overlaps its PROPERTY
    /// vocabulary, so a manifest style whose arm was never written reads as dispatched
    /// whenever an unrelated string matches its name — and whether that flips an
    /// assertion depends on which set the bag is compared against, which no part of
    /// the design controls. The narrowing that closed it is only safe for as long as
    /// something notices it being undone, and this is that something.
    ///
    /// The names below were MEASURED out of the three bodies, not assumed, and each is
    /// asserted twice: it must still be PRESENT as a quoted string in the body — so a
    /// fixed point that has been reworded away reds instead of silently becoming
    /// vacuous — and it must be ABSENT from the parsed set. Widen the extractor back
    /// and the second half fails, naming the name.
    ///
    /// **The sharpest of them is `gap` in the Swift body** — sharpest as a
    /// demonstration, and it is worth being exact about what it demonstrates. It is a
    /// real `yogaStyles` entry, its only appearance in that body is inside a
    /// commented-out `AddAttribute("gap", …)` example, and under the old extractor it
    /// was in the bag. It was **never checked against a name it could collide with**:
    /// this body's set is compared only with `VisualStyleAttributes`, never with
    /// `YogaStyleAttributes`. So it did not mask a wrong green on any assertion that
    /// existed — it shows the collection defect operating on a genuine manifest name
    /// instead of a name invented to prove a point.
    ///
    /// Limits (Rule 5): this controls the OVER-match direction only. Under-matching —
    /// an arm shape the grammar misses — is caught by the three facts above going red,
    /// and by <see cref="ParseNameTable"/>'s own no-arms-found guard.</summary>
    [Fact]
    public void TheNameExtractor_CollectsArmLabelsOnly_NotEveryQuotedString()
    {
        (string File, string Body, string What, string[] NonArms)[] tables =
        {
            (KotlinYogaLayout, KotlinSetStyleBody, "setStyle", new[]
            {
                // `when (value)` arms of the enum-word properties. Every one of these
                // is a VALUE, and every one sat in `dispatched` before 15.1.
                "row", "column", "row-reverse", "column-reverse",
                "flex-start", "center", "flex-end",
                "space-between", "space-around", "space-evenly",
                "nowrap", "wrap", "wrap-reverse",
                "relative", "absolute",
            }),
            (KotlinWidgetMapper, KotlinHandleSetStyleBody, "handleSetStyle", new[]
            {
                // Log-message prose. The visual body has no value keywords, so its
                // fixed points are the two strings the old extractor swallowed.
                "SetStyle for unknown nodeId ${p.nodeId}: ignored",
                "SetStyle '${p.property}' not yet supported (Phase 3+ extends)",
            }),
            (AppleWidgetMapper, AppleHandleSetStyleBody, "handleSetStyle", new[]
            {
                // `gap` and `scroll` come from a COMMENT's worked example
                // (`OpenElement("scroll") + AddAttribute("gap", …)`) and `gap` is a
                // live manifest style name; `BnWidgetMapper` is the BnLog subsystem
                // tag; `a modal node` is comment prose.
                "gap", "scroll", "BnWidgetMapper", "a modal node",
            }),
        };

        foreach (var (file, body, what, nonArms) in tables)
        {
            var bag = EveryQuotedString(file, body);
            var parsed = ParseNameTable(file, body, what);

            Assert.True(parsed.IsSubsetOf(bag),
                $"{file}: the arm-label extractor produced names that are not quoted strings in "
                + $"`{what}`'s body at all: {Join(parsed.Except(bag))}. The parse is wrong in a "
                + "way neither direction of this control anticipated.");

            foreach (var name in nonArms)
            {
                Assert.True(bag.Contains(name),
                    $"{file}: `{name}` is no longer a quoted string inside `{what}` at all, so it "
                    + "has stopped being a fixed point for this control. It was reworded or "
                    + "removed — re-measure the body and re-point this list deliberately rather "
                    + "than deleting the entry, because a control naming nothing proves nothing.");

                Assert.False(parsed.Contains(name),
                    $"{file}: `{name}` is NOT a dispatch arm of `{what}` — it is a value keyword, "
                    + "comment text or log prose — and the extractor collected it anyway. The "
                    + "arm-label narrowing has been widened back, and with it the false green "
                    + "census §5.1 records: a manifest style with no arm written now reads as "
                    + "dispatched on the strength of an unrelated string. The three facts above "
                    + "are checking less than they claim until this is put back.");
            }

            // The generic form of the same rule, for prose nobody thought to name: a
            // style name never contains a space, and a log message always does.
            var prose = parsed.Where(n => n.Any(char.IsWhiteSpace)).ToList();
            Assert.True(prose.Count == 0,
                $"{file}: the extractor collected names containing whitespace: {Join(prose)}. "
                + "Those are log messages or comment text, not arm labels.");

            // Last, and deliberately last: the named fixed points above are the
            // informative failure, so this catch-all only speaks when none of them did.
            Assert.True(bag.Count > parsed.Count,
                $"{file}: every quoted string in `{what}`'s body is now an arm label "
                + $"({bag.Count} == {parsed.Count}). That is not a state this body has ever been "
                + "in — each of the three carries log prose or comment text — so the likelier "
                + "explanation is that the narrowing was undone and this control has nothing "
                + "left to rule out.");
        }
    }

    /// <summary>`src/wire-vocabulary.json` carries 26 yogaStyles today; the floor is
    /// set at 20, leaving six names of headroom so that legitimately retiring a style
    /// is not a CI event while a collapse to empty is.</summary>
    private const int MinimumYogaStyles = 20;

    /// <summary>3 visualStyles today — backgroundColor, color, fontSize — and the
    /// floor is the measured count because there is no headroom to leave in a set that
    /// small. Removing one is a deliberate act; re-point this with it.</summary>
    private const int MinimumVisualStyles = 3;

    /// <summary>THE FLOOR ON THE ITERATED SET (pin standard Rule 2, census §5.1's
    /// first finding).
    ///
    /// Every fact in this file has the shape `routed.Except(dispatched)`, and until
    /// phase 15.1 the only anti-vacuity assertion was <see cref="ParseNameTable"/>'s
    /// `Assert.NotEmpty` — which floors `dispatched`, the set being SUBTRACTED. The
    /// set being ITERATED was unfloored, so an empty `routed` made `missing` empty and
    /// all three facts went green over nothing. Both style sets are built from
    /// `src/wire-vocabulary.json` through the generated `BnWireVocabulary`, so a
    /// manifest that failed to parse, or an emitter that wrote an empty table, is the
    /// realistic route to that state.</summary>
    private static void FloorTheIteratedSet(IReadOnlyCollection<string> routed, int minimum, string what)
        => Assert.True(routed.Count >= minimum,
            $"{what} holds {routed.Count} names, and at least {minimum} were expected. This is "
            + "the set the dispatch check ITERATES: empty it and `routed.Except(dispatched)` is "
            + "empty too, so this fact passes while checking nothing. Either the wire-vocabulary "
            + "manifest stopped being read, or names were retired — in which case lower this "
            + "floor deliberately.");

    private static string ReadShellSource(string relativePath)
    {
        var file = Path.Combine(BnRepo.Root(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(file), $"shell source not found: {file}");
        return File.ReadAllText(file);
    }

    private static string Join(IEnumerable<string> names)
    {
        var list = names.OrderBy(n => n, StringComparer.Ordinal).ToList();
        return list.Count == 0 ? "(none)" : string.Join(", ", list);
    }
}
