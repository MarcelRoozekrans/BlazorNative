using System.Text;

namespace BlazorNative.Tests.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// CommentStrippedSource — THE single "remove comments before scanning" for every
// pin in this repo. Swift, Kotlin and C# share both comment forms, so one
// implementation serves all ten callers: GeneratedSymbolShadowTests,
// DispatchSurfaceDriftTests, AuthSemanticsDriftTests, PinPopulationTests,
// NSLogDriftTests, ConsoleErrorDriftTests, AndroidLogDriftTests,
// DeepLinkSeedDriftTests, BnSafeAreaCoverageTests and ShellStyleTableDriftTests.
// Phase 15.2 added an eleventh caller that is NOT one of them:
// CommentStrippedSourceTests holds this type's own behaviour directly, so the
// limits below are facts rather than a paragraph. See ROUND FIVE.
//
// IT LIVES IN tests/Shared BECAUSE "ONE IMPLEMENTATION" WAS ONLY TRUE INSIDE ONE
// PROJECT. Until phase 15.1 this file sat in tests/BlazorNative.Runtime.Tests, and
// neither BlazorNative.Renderer.Tests nor BlazorNative.Analyzers.Tests had a project
// reference to it -- both reference only src/. So "one stripper, eight callers" was
// a statement about Runtime.Tests, and a pin in either other project could not
// reach the shared copy even having been told to. That was not hypothetical: it is
// exactly why ShellStyleTableDriftTests carried a DISCLOSED FALSE GREEN over
// block-commented dispatch arms, with its own comment naming this move as the
// blocker. It is linked into every test project by tests/Directory.Build.props, the
// same way BnRepo.cs is and for the same reason -- a project cannot forget it, and
// a project cannot be unable to reach it.
//
// WHY IT IS ONE TYPE, TWICE OVER.
//
// Round one, phase 14.1: `GeneratedSymbolShadowTests.CodeLines` and
// `DispatchSurfaceDriftTests.CodeText` held two copies of a line-based stripper
// and the second had already dropped a check the first had, so a single-line
// KDoc swallowed the rest of the file -- silently, inside pins whose whole job
// is noticing silent divergence. They were merged here.
//
// Round two, phase 15.0: the merge had MISSED A THIRD COPY.
// `AuthSemanticsDriftTests.StripComments` was a separate, hand-written stripper,
// and in phase 14.4 issue #364's F2 hardened THAT copy -- teaching it that a
// `//` inside a string literal is not a comment, because `://` appears in both
// shells' literals -- while this one, with three times the callers, kept the
// bug. A line reading
//
//     var docs = "see http://example.com/readme"; ... AppContext.BaseDirectory
//
// lost everything after the URL's `//`, so PinPopulationTests went green over a
// verbatim, unobfuscated bypass. The fix for a bug that exists in two copies is
// never to patch the second copy: two strippers agreeing today is the
// PRECONDITION for the divergence class, not its absence. So the hardened
// algorithm moved here and the private copy was deleted.
//
// Round three, same phase: there were FOUR MORE, one per `file:line` drift pin,
// each a byte-identical private `CodeLines`. One of them was exploitable against
// a real security guard rather than merely untidy. `NSLogDriftTests` calls itself
// the sole pre-CI signal for the iOS shell on a non-Mac machine, and NSLog is
// unconditional and always public -- it has printed keychain keys. A bare,
// undeclared `NSLog` added to BnBiometrics.swift behind a URL in a string literal
// passed all four of its facts. That is the cost of a copy nobody could enumerate,
// and it is why the population, not just the algorithm, has to be pinned.
//
// NOTE FOR THE NEXT READER: `TemplateDriftTests.StripLineComments` is deliberately
// NOT here. It is unhardened, but it DISCLOSES that in its own doc comment, it is
// scoped to a four-statement body with no string literals, and it FAILS SAFE --
// over-stripping there yields a false red, never a false green. A guard against a
// ninth copy is phase 15.1's job, not a fifth hand-migration.
//
// ROUND FOUR, phase 15.1: RAZOR, and why it is a SIBLING rather than a MODE.
//
// `BnSafeAreaCoverageTests` scans a `.razor` file, whose comment form is `@* ... *@`
// and is not `//` or `/* */`. The census filed that as a disclosed small gap; it was
// a live false green in all five of that pin's files, demonstrated by deleting the
// wrap outright from BnStarterPage.razor and watching the pin pass off the header
// comment alone.
//
// Rule 8 says consolidate rather than port, and the ARGUMENT FOR THIS HOME survives
// the grammar difference even though the usual one does not. `@* ... *@` is a
// different grammar from `//` and `/* */`, so a Razor stripper is not a second copy
// of the one above and there is nothing for it to diverge FROM -- the divergence
// class Rule 8 exists to prevent does not apply. What DOES apply is the home: the
// next pin that needs to scan a `.razor` file will look for comment stripping here,
// and if it finds nothing it will write the ninth copy. Discoverability is the part
// of Rule 8 that is grammar-independent.
//
// But a MODE would have been the wrong shape, and the reasoning is worth keeping.
// A `razor: true` flag on `Strip` threads through `Lines` and `NumberedCodeLines`,
// needs a default for eight callers who must never get Razor behaviour, and a
// defaulted flag on a shared hot path is exactly the mechanism by which a
// shared-helper change leaves the aggregate test count correct while quietly moving
// what ONE pin sees. So: same home, separate entry point, and `Strip`'s body is not
// edited at all -- the eight callers are unchanged BY CONSTRUCTION rather than by
// re-verification. (They were re-run anyway; the counts are in the phase report.)
//
// ROUND FIVE, phase 15.2: RAW STRINGS, and the comment that was the bug.
//
// Issue #364's F3. `Strip` had ONE string flag and it reset at every newline. A
// `"""` toggled that flag three times and landed inside-string; the reset then put
// the raw string's BODY back into CODE state, and a `/*` in the body opened a block
// comment that ran to the next `*/` -- which can be in another declaration
// entirely. The live code between the two was deleted from the scanned text. That
// is OVER-stripping, which is the FALSE-GREEN direction and the exact direction the
// unterminated-opener rule below already existed to avoid.
//
// The part worth keeping is not the fix. It is that the doc comment on `Strip`
// asserted the newline reset "confines any mis-parse to a single line rather than
// letting one stray quote blind the rest of the file", and named raw strings in the
// same paragraph as an example of something it bounded. The reset did not bound the
// raw-string case; it CAUSED it. An unenforced safety claim in a comment is this
// repo's most expensive bug class -- three incidents in one week were each exactly
// that -- and this one had been read past by everyone who touched the file through
// four rounds. `CommentStrippedSourceTests` is the fix for that half: the limits are
// now held by facts rather than by a paragraph.
//
// This round ALSO recorded a scope refusal, because the next reader will be tempted.
// C#'s fence-WIDTH rule and Swift's `#"..."#` delimiters are deliberately NOT
// implemented, and the reason is in the doc comment below rather than in anyone's
// head: scope escaping through a general parser is how this pin family got into
// trouble in the first place.
//
// ROUND FIVE, SECOND PASS -- and this is the part to read if you are about to change
// the fence.
//
// The first cut of the F3 fix tracked raw state with NO BOUND. Review found it live
// on two files. C#'s verbatim string escapes an embedded quote by doubling it, so a
// verbatim literal that begins with a quote spells `@"""` -- three consecutive quotes
// -- and the rest of that literal never offers a three-run to close on.
// `ShellFrameTableDriftTests.cs:296` and `TemplateDriftTests.cs:1344` each opened a
// raw string that stayed open to EOF, switching comment stripping off over 358 lines
// between them, inside PinPopulationTests' own walk, with every pin green.
//
// SAY THE SHAPE OUT LOUD, because it is the reusable part. F3 blinded the scan past
// the construct that confused it by OVER-stripping. The first fix blinded the scan
// past the construct that confused it by UNDER-stripping. Same hazard, opposite sign,
// and the second arrived through a door the fix itself opened. The rule the file now
// holds is neither "strip more" nor "strip less": NO INPUT MAY MAKE THE STRIPPER BLIND
// PAST THE CONSTRUCT THAT CONFUSED IT. The unterminated-`/*` branch had been obeying
// it since before any of this; the raw-string branch now does too, by refusing to
// enter the state at all unless a closer already exists ahead.
//
// THE OTHER HALF OF THE CORRECTION WAS A CLAIM, not code. The first cut's disclosure
// said an unterminated fence "fails RED, never green". That is true for an ABSENCE
// pin and FALSE for a PRESENCE pin, and this repo has presence pins BY DESIGN -- every
// Rule 3 fixed point is one. A commented-out `Log.i` left visible by under-stripping
// satisfies `AndroidLogDriftTests`' `Assert.True(hits > 0, ...)`, measured 0 -> 1. A
// commit whose entire point was deleting an unenforced safety claim had shipped a new
// one. That is how cheap the mistake is to make; it is written here so the next reader
// spends their suspicion on the claims and not only on the code.
// ─────────────────────────────────────────────────────────────────────────────

internal static class CommentStrippedSource
{
    /// <summary><paramref name="source"/> with both comment forms removed, so a token
    /// merely DESCRIBED in prose is never mistaken for a live call site: `//` to end of
    /// line, AND `/* … */` blocks, which includes KDoc `/** … */` — AndroidShellBridge.kt
    /// carries 284 block openers, and the completeness scans read the same stripped text,
    /// so an unstripped KDoc token would demand an `ignored` entry documenting nothing
    /// real. Blocks NEST, as Swift and Kotlin both define them. Newlines inside a stripped
    /// block are preserved so reported line numbers stay true, and so
    /// <see cref="Lines"/> can rebuild one entry per source line. An UNTERMINATED opener
    /// is left in place rather than swallowing the rest of the file: over-stripping hides
    /// live call sites, which is a false green.
    ///
    /// String literals ARE parsed, simply: an unescaped `"` toggles in/out of a string and
    /// the state RESETS AT EVERY NEWLINE, so a `//` or `/*` inside a literal is no longer
    /// read as a comment — `://` already appears in both shells' literals, and in C# test
    /// sources, so this is a live shape and not a hypothetical.
    ///
    /// RAW STRINGS (a run of three or more `"`) ARE TRACKED SEPARATELY, and that state
    /// deliberately SURVIVES the newline. It has to: this is issue #364's F3. Under the
    /// single flag alone a `"""` toggled three times and landed INSIDE-string, the newline
    /// reset then put the raw string's BODY back into CODE state, and a `/*` in that body
    /// opened a block comment that ran to the next `*/` — which can be in another
    /// declaration entirely. Everything between the two was deleted from the scanned text.
    /// Over-stripping hides live call sites, which is a false GREEN, and it is the same
    /// direction the unterminated-opener rule above already exists to avoid.
    ///
    /// THE STATE CANNOT REACH END OF FILE, BY CONSTRUCTION, and that property is load
    /// bearing rather than decorative — see <see cref="IsFenceAt"/> for the two rules that
    /// produce it and for the review that made them necessary. A first cut of this fix
    /// tracked raw state with no bound at all and traded F3's block-comment blinding for a
    /// raw-body blinding that ran to EOF; it was live on two files in this repo before it
    /// was caught. The bound is not a disclosure. It is a precondition on entering the
    /// state, and <c>CommentStrippedSourceTests</c> holds it.
    ///
    /// REMAINING LIMITS — and the direction each one fails. NOTE THE DIRECTIONS FIRST,
    /// because the natural assumption about them is wrong:
    ///
    /// OVER-stripping is a false GREEN — a pin's subject is deleted before it is looked
    /// for. UNDER-stripping is a false RED for an ABSENCE pin, which is most of them, and
    /// a false GREEN for a PRESENCE pin, of which this repo has several BY DESIGN: every
    /// Rule 3 fixed point is one. `AndroidLogDriftTests`' `Assert.True(hits > 0, …)` is
    /// satisfied by a COMMENTED-OUT `Log.i` that under-stripping left visible, and that
    /// was measured going 0 → 1, not reasoned about. So "it only ever costs a red" is not
    /// available as a defence here for either direction, and nothing below claims it.
    ///
    /// · C#'s FENCE-WIDTH RULE is not implemented. A run of three or more quotes opens,
    ///   and any later run of three or more closes it — so a C# `""""` fence, which the
    ///   language says must close on four or more, would close here on three. The runs in
    ///   this repo are `ItemsJsonTest.kt:91` and `:106`, Kotlin rather than C#, and both
    ///   open and close on whole runs and parse correctly. Deliberately narrow: a general
    ///   raw-string parser is how this pin family got into trouble, and scope escaping
    ///   through one is the thing to refuse. EITHER DIRECTION, bounded by the construction
    ///   above.
    /// · Swift's CUSTOM DELIMITERS `#"…"#` and `#"""…"""#` are not understood. The first
    ///   is live — `BnWidgetMapper.swift:3461`, inside both `ShellStyleTableDriftTests`'
    ///   and `NSLogDriftTests`' walks, and again across `BnHostTests/*.swift` — and it
    ///   costs nothing, because `#"` presents only a ONE-quote run and is read as an
    ///   ordinary literal exactly as it was before this change. The second does not occur
    ///   in the tree; were it to, its `"""` would be read as a fence and its `#` as code.
    ///   FAILS RED for absence pins, GREEN for presence pins, bounded to the file.
    /// · A C# `'"'` char literal still toggles the ordinary string flag once, and is
    ///   still bounded by the newline reset. Same for any unbalanced quote in an ordinary
    ///   literal: the mis-parse ends at the end of its line.
    /// · What the newline reset does and does not buy, stated exactly, because the
    ///   previous wording of this paragraph claimed more than the code did and F3 was the
    ///   counterexample. It bounds a mis-parse arising from the ORDINARY string flag to a
    ///   single line. It does NOT bound the raw-string flag — that is the point of it —
    ///   and the raw flag is bounded instead by the entry precondition above, which is a
    ///   stronger guarantee than the reset ever gave: it holds for ALL inputs rather than
    ///   for the ones anyone happened to look at.</summary>
    public static string Strip(string source)
    {
        var sb = new StringBuilder(source.Length);
        int i = 0;
        bool inString = false;
        bool inRawString = false;

        while (i < source.Length)
        {
            // RAW STRINGS, Kotlin and Swift both, and C# in the test sources. This
            // state deliberately SURVIVES the newline, unlike the single-quote state
            // below. #364 F3: `"""` toggled the simple flag three times, landed
            // inside-string, and the newline reset then put the BODY back into CODE
            // state — where a `/*` opened a block comment running to the next `*/`,
            // which can be arbitrarily far away. Over-stripping hides live call sites;
            // that is a false GREEN, and it is the direction the unterminated-opener
            // branch below was already written to avoid.
            //
            // ENTERING THE STATE HAS A PRECONDITION: a fence that closes it must
            // already exist ahead, found with the SAME predicate the closing arm uses.
            // That is what makes "raw state cannot reach EOF" true by construction
            // rather than by inspection of today's tree, and it is the direct mirror
            // of the unterminated-block-opener branch below, which emits its `/*`
            // verbatim rather than swallowing the file. `IsFenceAt` carries the
            // reasoning and the scar; the short version is that the first cut of this
            // fix had no precondition and blinded two real files to EOF.
            //
            // The `!inString` guard says an ordinary literal's INTERIOR is never a
            // fence. It is defence in depth and is deliberately NOT claimed as
            // pinned: no valid C#, Kotlin or Swift reaches it, because three adjacent
            // quotes while the ordinary flag is set means the first of them is that
            // literal's own closing quote. It can only be reached after the ordinary
            // flag has ALREADY mis-parsed — a `'"'` char literal, or an odd backslash
            // run the two-character escape look-back gets wrong — and in that state
            // neither answer is right. What it buys is that such a mis-parse stays
            // bounded by the newline reset instead of escalating into a whole-file
            // raw-string mis-parse.
            //
            // The FENCE WIDTH is pinned, and it needed to be: narrowing this to `""`
            // leaves the F3 fixture green while turning every empty string literal in
            // the repo into a raw-string opener. So is the `@` refusal, and so is the
            // closing-fence precondition. The facts are in CommentStrippedSourceTests
            // — see the roster in its header, which names each property and the fact
            // that holds it, so a renamed fact is found by reading one list.
            if (!inString && IsFenceAt(source, i, out int fence)
                && (inRawString || HasFenceAtOrAfter(source, i + fence)))
            {
                inRawString = !inRawString;
                sb.Append('"', fence);
                i += fence;
                continue;
            }

            if (inRawString)
            {
                sb.Append(source[i]);   // newlines included — line numbers must stay true
                i++;
                continue;
            }

            // STRING LITERALS (14.4's F2, and the reason this type absorbed the
            // AuthSemantics copy in 15.0). A `//` inside a string is not a comment —
            // the shells already contain `://` in literals, at BnDeepLink.swift and
            // BnCamera.swift. Tracking is deliberately simple: toggle on an unescaped
            // `"`, and RESET AT EVERY NEWLINE. No ORDINARY string in Swift, Kotlin or
            // C# spans lines, and the reset is what BOUNDS an ordinary-literal
            // mis-parse to one line. It bounds THIS flag only — the raw-string flag
            // above is deliberately outside it, because a raw string DOES span lines
            // and #364's F3 is what the reset did when applied to one.
            if (source[i] == '"')
            {
                bool escaped = i > 0 && source[i - 1] == '\\'
                               && !(i > 1 && source[i - 2] == '\\');
                if (!escaped) inString = !inString;
                sb.Append(source[i]);
                i++;
                continue;
            }

            if (source[i] == '\n')
            {
                inString = false;
                sb.Append(source[i]);
                i++;
                continue;
            }

            if (inString)
            {
                sb.Append(source[i]);
                i++;
                continue;
            }

            if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                int depth = 0;
                int j = i;
                while (j < source.Length)
                {
                    if (source[j] == '/' && j + 1 < source.Length && source[j + 1] == '*')
                    {
                        depth++;
                        j += 2;
                    }
                    else if (source[j] == '*' && j + 1 < source.Length && source[j + 1] == '/')
                    {
                        depth--;
                        j += 2;
                        if (depth == 0) break;
                    }
                    else
                    {
                        j++;
                    }
                }

                if (depth != 0)
                {
                    // Unterminated — emit the '/' verbatim and resume one character on.
                    // Swallowing to EOF would blind the scan to everything below it.
                    sb.Append(source[i]);
                    i++;
                    continue;
                }

                // Keep the block's newlines so line numbers survive the strip.
                for (int k = i; k < j; k++)
                    if (source[k] == '\n') sb.Append('\n');
                i = j;
                continue;
            }

            if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n') i++;
                continue;
            }

            sb.Append(source[i]);
            i++;
        }

        return sb.ToString();
    }

    /// <summary>True when <paramref name="i"/> begins a run of quotes that may act as a
    /// raw-string fence, with <paramref name="run"/> set to the run's full length. ONE
    /// predicate, used by the opening arm, the closing arm AND the lookahead — that
    /// identity is what makes the termination argument hold, so do not inline a variant
    /// of it at any of the three sites.
    ///
    /// TWO RULES, and the second is a scar.
    ///
    /// 1. THE RUN MUST BE THREE OR MORE, and the whole run is consumed. Taking exactly
    ///    three would split a four-quote run into a fence plus a stray quote, which is
    ///    what put `ItemsJsonTest.kt:91` and `:106` — `"""["a""""` and `""""a""""` — into
    ///    a mid-line mis-parse that retained their trailing `//` comments.
    ///
    /// 2. A C# VERBATIM OPENER IS NOT A FENCE. `@"` escapes an embedded quote by DOUBLING
    ///    it, so a verbatim string whose first character is a quote spells `@"""` — three
    ///    consecutive quotes that open nothing. The rest of such a literal then offers
    ///    only one- and two-quote runs, so it never presents a closer.
    ///
    ///    THIS WAS NOT HYPOTHETICAL AND IT IS WHY THIS METHOD EXISTS. The first cut of
    ///    #364's F3 fix had neither rule, and both `ShellFrameTableDriftTests.cs:296` and
    ///    `TemplateDriftTests.cs:1344` opened a raw string that stayed open to EOF —
    ///    blinding 358 lines of comment stripping between them, inside
    ///    `PinPopulationTests`' own walk, while every pin stayed green. The recognisable
    ///    part is that this is F3's hazard with the sign flipped: F3 blinded the scan by
    ///    over-stripping, the first fix blinded it by under-stripping, and BOTH ran to an
    ///    arbitrary distance from the construct that caused them.
    ///
    ///    Refusing on `@` alone is NOT sufficient and was not what shipped. Two `@"""`
    ///    occurrences in one file would otherwise pair into a span neither of them opened
    ///    — bounded, but wrong, and wrong over a region no reader could predict. The
    ///    lookahead in <see cref="HasFenceAtOrAfter"/> is the half that bounds it; this
    ///    rule is the half that stops the bogus pairing in the first place. They are not
    ///    alternatives.
    ///
    /// The `$` in the look-back is for C#'s `$@"` and `$$@"` prefixes: the verbatim marker
    /// is the `@` ANYWHERE in the prefix, not just the character immediately before the
    /// run. `$$"""` — a genuine interpolated raw string, live at `DevHostBridge.cs:430` —
    /// carries no `@` and is correctly a fence.
    ///
    /// WHAT THIS REFUSES THAT IT NEED NOT: a raw-string body ending in `@`, spelled
    /// `"""…@"""`, is not recognised as a closer, so the opener is refused too and the
    /// whole construct degrades to ordinary quotes bounded by the newline reset. No such
    /// body exists in the tree. The degradation is to the PRE-15.2 behaviour rather than
    /// to something new, which is the property worth having when a rule has to guess.</summary>
    private static bool IsFenceAt(string source, int i, out int run)
    {
        run = 0;
        if (source[i] != '"') return false;

        while (i + run < source.Length && source[i + run] == '"') run++;
        if (run < 3) return false;

        for (int k = i - 1; k >= 0 && (source[k] == '$' || source[k] == '@'); k--)
            if (source[k] == '@') return false;

        return true;
    }

    /// <summary>True when a fence exists at or after <paramref name="from"/>. This is the
    /// ENTRY PRECONDITION on raw-string state and the whole of why that state cannot reach
    /// end of file: the opening arm refuses to enter without a closer, the closing arm
    /// accepts any fence, and both ask <see cref="IsFenceAt"/>, so a state that was
    /// entered will be left. An unterminated `"""` is therefore emitted as ordinary quotes
    /// and bounded by the newline reset — the same choice, for the same reason, as the
    /// unterminated `/*` in <see cref="Strip"/>.
    ///
    /// Cost is one scan of the remaining quotes per CANDIDATE opener, and a candidate is
    /// already a three-or-more run in code state, of which the largest file in this repo
    /// has single digits. It is not called for the `@`-refused shapes at all: the
    /// short-circuit in <see cref="Strip"/> puts <see cref="IsFenceAt"/> first.</summary>
    private static bool HasFenceAtOrAfter(string source, int from)
    {
        for (int j = source.IndexOf('"', from); j >= 0; j = source.IndexOf('"', j + 1))
            if (IsFenceAt(source, j, out _)) return true;

        return false;
    }

    /// <summary>A `.razor` source with BOTH of its comment grammars removed: Razor's
    /// `@* … *@` first, then <see cref="Strip"/> for the `//` and `/* … */` a
    /// `@code` block can carry. One call, because a Razor file is a mixed grammar and
    /// a caller that remembers only one half has the false green this method exists
    /// to close.
    ///
    /// Razor comments DO NOT NEST — `@* a @* b *@ c *@` ends at the FIRST `*@`, and
    /// this matches that rather than the nesting rule <see cref="Strip"/> implements
    /// for Kotlin and Swift. `@@` is Razor's escape for a literal `@`, so `@@*` is
    /// text and not an opener; it is skipped as a pair. An UNTERMINATED `@*` is left
    /// in place rather than swallowing the rest of the file — the same policy and the
    /// same reason: over-stripping hides live call sites, which is a false green.
    /// Newlines inside a removed comment are preserved, so line numbers survive and a
    /// caller can still align the result index-for-index with the original.
    ///
    /// WHAT THIS DOES NOT COVER, and the direction each one fails.
    /// · An HTML comment, `&lt;!-- … --&gt;`, is NOT removed. A `&lt;BnSafeArea&gt;` inside one
    ///   would still be counted as live. FAILS GREEN, and is the one limit here that
    ///   does — it is left uncovered deliberately, because Razor's own handling of
    ///   components inside HTML comments is not a thing this repo has pinned, and a
    ///   stripper that guessed would be asserting a compiler behaviour nobody
    ///   measured. If it ever matters, measure it first.
    /// · A `@*` or a `*@` inside a C# string literal in a `@code` block is treated as
    ///   a delimiter, because the Razor pass runs before any string-literal
    ///   awareness. Over-strips. FAILS RED.
    /// · Running <see cref="Strip"/> over MARKUP means markup quoting drives its
    ///   string state. The state resets at every newline, so any mis-parse is bounded
    ///   to one line, and a mis-parse can only remove text. FAILS RED.
    /// · Nothing here understands `@if` / `@foreach`: a wrap inside a branch that
    ///   never executes is live text and reads as live. FAILS GREEN, and is out of
    ///   reach of any text scan — it is a parse problem, not a stripper problem.</summary>
    public static string StripRazor(string source)
    {
        var sb = new StringBuilder(source.Length);
        int i = 0;

        while (i < source.Length)
        {
            // `@@` is an escaped literal `@`. Consume both so `@@*` cannot open a
            // comment — it is text, and treating it as an opener would over-strip.
            if (source[i] == '@' && i + 1 < source.Length && source[i + 1] == '@')
            {
                sb.Append(source[i]);
                sb.Append(source[i + 1]);
                i += 2;
                continue;
            }

            if (source[i] == '@' && i + 1 < source.Length && source[i + 1] == '*')
            {
                int end = source.IndexOf("*@", i + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    // Unterminated — emit the '@' verbatim and resume one character
                    // on, rather than swallowing to EOF and blinding the scan.
                    sb.Append(source[i]);
                    i++;
                    continue;
                }

                for (int k = i; k < end + 2; k++)
                    if (source[k] == '\n') sb.Append('\n');
                i = end + 2;
                continue;
            }

            sb.Append(source[i]);
            i++;
        }

        return Strip(sb.ToString());
    }

    /// <summary>The file's lines that are CODE, numbered from 1 against the ORIGINAL
    /// file, with blank and comment-only lines dropped. This is the shape four drift
    /// pins scan with -- NSLog, ConsoleError, AndroidLog and DeepLinkSeed -- each of
    /// which carried a private byte-identical copy of the pre-15.0 line walk until this
    /// phase. They report `file:line` at a human, so the number must be the line the
    /// reader will open, not an index into the surviving lines.</summary>
    public static IEnumerable<(int Number, string Text)> NumberedCodeLines(string file)
    {
        string[] lines = Lines(file);
        for (int i = 0; i < lines.Length; i++)
            if (lines[i].Trim().Length > 0)
                yield return (i + 1, lines[i]);
    }

    /// <summary>Every line of <paramref name="file"/> with comment text removed, one entry
    /// per source line so line numbers and any forwarding-window scan still line up — the
    /// same entry count <c>File.ReadLines</c> would give. A comment-only line becomes an
    /// empty string, not a dropped entry: callers that need line indices to survive
    /// (GeneratedSymbolShadowTests) rely on that; callers that only want code text
    /// (DispatchSurfaceDriftTests) filter blanks themselves.</summary>
    public static string[] Lines(string file)
    {
        string source = File.ReadAllText(file);
        if (source.Length == 0) return [];

        string[] lines = Strip(source).Split('\n');

        // `Strip` preserves every newline, so the split yields one entry per source
        // line — plus one trailing empty when the file ends with a newline, which
        // `File.ReadLines` does not report. Drop exactly that one, so index-based
        // callers see the entry count they saw before this type owned the walk.
        int count = source.EndsWith('\n') ? lines.Length - 1 : lines.Length;

        var result = new string[count];
        for (int i = 0; i < count; i++) result[i] = lines[i].TrimEnd('\r');
        return result;
    }
}
