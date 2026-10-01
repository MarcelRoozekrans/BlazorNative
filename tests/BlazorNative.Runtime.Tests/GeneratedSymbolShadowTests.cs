using System.Globalization;
using System.Text.RegularExpressions;
using BlazorNative.Tests.Shared;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// GeneratedSymbolShadowTests — Phase 13.4, issue #279.
//
// THE SHAPE THIS REFUSES. src/wire-vocabulary.json is the single source for the
// wire vocabulary and WireGen emits a copy per language. That makes divergence
// unrepresentable ONLY IF the shells actually consume the generated symbol. If a
// shell declares its own literal of the same name, the generated symbol goes dead,
// the hand-written one wins every use, and the manifest silently stops governing
// that vocabulary — while WireVocabularyCodegenTests stays green, because it
// compares generated files to the manifest and never looks at a hand-written twin.
//
// That is exactly what #279 found on iOS: BnWidgetMapper declared
// `private static let measuredNodeTypes`, and BnWireVocabulary.measuredNodeTypes
// had zero qualified references.
//
// THREE GENERATED SHELL FILES, NOT TWO. WireGen emits Swift, Kotlin AND an
// Objective-C++ header (BnWireVocabulary.g.h) that BnYogaLayout.mm includes. The
// header was originally outside this scan, which made the pin blind to a
// `static const char* const kNodeTypes[]` twin in a .mm — #279 reproduced in the
// third language — and let a dead C symbol sit there unallowlisted while both
// pins reported green.
//
// DEAD IS COMMON; DEAD-AND-SHADOWED IS THE DEFECT. Four generated symbols are
// unreferenced and harmless because nothing competes with them. A blunt "every
// generated symbol must be used" guard would red on those and force four
// pointless consumptions, so the primary pin is narrower: no generated symbol may
// be SHADOWED. The reference check is the second, advisory pin below.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class GeneratedSymbolShadowTests
{
    /// <summary>How many symbols the three generated shell files hold TODAY (11 Swift,
    /// 12 Kotlin and 3 C). Swift: the 5 BnWireVocabulary members and the 6 BnHostCallOp
    /// constants. Kotlin: the same 11, plus BnHostEvent's constructor property
    /// `wireName`, which Swift has no equivalent of because Swift's BnHostEvent consumes
    /// the built-in `.rawValue` instead. The op constants arrived with Phase 16.1. The
    /// floor below is measured, not guessed; if the manifest legitimately loses a name,
    /// lower it in the same commit.</summary>
    private const int GeneratedSymbolFloor = 26;

    /// <summary>Swift `static let NAME` / Kotlin `val NAME` / `@JvmField val NAME`.</summary>
    private const string SwiftKotlinDeclaration = @"\b(?:static\s+let|val)\s+([A-Za-z_][A-Za-z0-9_]*)\b";

    /// <summary>C `static const char* const NAME[] = {`. Empty brackets are what makes
    /// this a DEFINITION rather than a use — every use in the shell indexes with
    /// something (`kYogaStyles[i]`, `sizeof(kYogaStyles[0])`).</summary>
    private const string CArrayDeclaration = @"\b([A-Za-z_][A-Za-z0-9_]*)\s*\[\s*\]\s*=\s*\{";

    /// <summary>A top-level `enum ... BnHostEvent` opening — Kotlin's
    /// `internal enum class BnHostEvent(val wireName: String) {` and Swift's
    /// `enum BnHostEvent: String {` both match. Everything declared from this line
    /// to the block's closing brace is a member of that enum, not of the
    /// `BnWireVocabulary` object — see the note on <see cref="GeneratedSymbols"/>.</summary>
    private const string HostEventEnumOpen = @"\benum\s+(?:class\s+)?BnHostEvent\b";

    /// <summary>A top-level host-call op table: Kotlin's <c>object HostCallOp {</c> and
    /// Swift's <c>enum BnHostCallOp {</c> (Phase 16.1). Group 1 is the type name, which is
    /// how its constants are referenced: <c>HostCallOp.CAMERA</c>, <c>BnHostCallOp.camera</c>.
    /// Anchored at column 0 so prose that mentions the type cannot open the block.</summary>
    private const string HostCallOpTableOpen = @"^(?:object|enum)\s+(HostCallOp|BnHostCallOp)\b";

    /// <summary>Every symbol WireGen emits into a shell, by generated-file path, plus
    /// whether it was declared inside the <c>BnHostEvent</c> enum rather than the
    /// <c>BnWireVocabulary</c> object.
    ///
    /// <para>THE ENUM IS A DIFFERENT NAMESPACE. Kotlin's
    /// <c>enum class BnHostEvent(val wireName: String)</c> declares `wireName` as a
    /// per-case constructor property, consumed as `event.wireName` — never as
    /// `BnWireVocabulary.wireName`, because it is not a `BnWireVocabulary` member at
    /// all. The two generated types share this file only because WireGen emits them
    /// together; a symbol's enclosing block, not its file, decides how it is
    /// referenced. Swift's `BnHostEvent: String` has no equivalent property (Swift
    /// consumes the built-in `.rawValue` instead), so this only ever tags Kotlin
    /// symbols today — tracked by block rather than hard-coded to `wireName` so a
    /// future manifest-driven enum property is covered the same way.</para>
    ///
    /// <para>MATERIALISED, AND FLOORED, DELIBERATELY. Both pins below iterate this
    /// one helper, so a parse that silently stopped matching would green BOTH of
    /// them over an empty set — the exact silent-degradation shape this phase
    /// exists to remove, sitting inside its own flagship guard. `File.Exists`
    /// guards the file MOVING; only a count guards the parse FAILING.</para></summary>
    private static IReadOnlyList<(string File, string Symbol, bool InHostEventEnum, string? OpTable)> GeneratedSymbols()
    {
        string root = BnRepo.Root();
        (string Path, string Pattern)[] generated =
        [
            (Path.Combine(root, "src", "BlazorNative.Apple", "BnHost", "BnWireVocabulary.g.swift"), SwiftKotlinDeclaration),
            (Path.Combine(root, "src", "BlazorNative.Jni", "src", "main", "kotlin", "io", "blazornative", "jni", "BnWireVocabulary.g.kt"), SwiftKotlinDeclaration),
            (Path.Combine(root, "src", "BlazorNative.Apple", "BnHost", "BnWireVocabulary.g.h"), CArrayDeclaration),
        ];

        var symbols = new List<(string File, string Symbol, bool InHostEventEnum, string? OpTable)>();
        foreach ((string path, string pattern) in generated)
        {
            Assert.True(File.Exists(path), $"generated file missing: {path}");

            bool inHostEventEnum = false;
            string? opTable = null;
            foreach (string line in File.ReadAllLines(path))
            {
                // Entering counts on the SAME line: Kotlin declares the enum and its
                // `wireName` property in one statement (`BnHostEvent(val wireName: ...)`).
                if (!inHostEventEnum && Regex.IsMatch(line, HostEventEnumOpen))
                    inHostEventEnum = true;
                Match open = Regex.Match(line, HostCallOpTableOpen);
                if (opTable is null && open.Success)
                    opTable = open.Groups[1].Value;

                Match m = Regex.Match(line, pattern);
                if (m.Success)
                    symbols.Add((path, m.Groups[1].Value, inHostEventEnum, opTable));

                // Every top-level type in these files closes with an unindented `}`,
                // so this is sufficient without a full brace-depth parser.
                if (line.Trim() == "}")
                {
                    inHostEventEnum = false;
                    opTable = null;
                }
            }
        }

        Assert.True(symbols.Count >= GeneratedSymbolFloor,
            $"parsed only {symbols.Count} symbols out of the {generated.Length} generated shell files, "
            + $"below the measured floor of {GeneratedSymbolFloor}. The declaration patterns stopped "
            + "seeing their subject (an emitter changed its syntax, or a file was reformatted), and "
            + "BOTH pins in this class would then pass over an empty set — green while asserting "
            + "NOTHING. Fix the scan. If the manifest genuinely lost a name, lower this floor in the "
            + "same commit so the loss is a decision on the record.");

        return symbols;
    }

    /// <summary>Is this generated file the Objective-C++ header? Its symbols are plain
    /// C arrays: referenced UNQUALIFIED (there is no `BnWireVocabulary.` namespace in
    /// C) and declared with a different grammar than Swift's or Kotlin's.</summary>
    private static bool IsCHeader(string generatedFile) =>
        generatedFile.EndsWith(".h", StringComparison.Ordinal);

    /// <summary>The shell source a generated file's consumers live in.
    ///
    /// <para>The Apple tree is searched as Swift AND Objective-C++ (`.mm`/`.h`/`.m`):
    /// `BnWireVocabulary.g.h`'s consumers are in `BnYogaLayout.mm`, and a hand-written
    /// C twin could equally be declared in a `.h`.</para></summary>
    private static string[] ShellSources(string generatedFile)
    {
        string root = BnRepo.Root();
        bool apple = generatedFile.Contains($"{Path.DirectorySeparatorChar}BlazorNative.Apple{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

        string dir = apple
            ? Path.Combine(root, "src", "BlazorNative.Apple")
            : Path.Combine(root, "src", "BlazorNative.Jni");

        string[] extensions = apple
            ? [".swift", ".mm", ".h", ".m"]
            : [".kt"];

        return [.. Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
            .Where(f => extensions.Any(e => f.EndsWith(e, StringComparison.Ordinal)))
            .Where(f => !f.Contains(".g.", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}build{Path.DirectorySeparatorChar}", StringComparison.Ordinal))];
    }

    /// <summary>Every line of the file with COMMENT TEXT REMOVED, one entry per source
    /// line so line numbers and the forwarding window still line up.
    ///
    /// <para>The mirror image of what the four `file:line` drift pins want from
    /// <see cref="CommentStrippedSource.NumberedCodeLines"/>: those scans must not
    /// count PROSE as an offence, this one must not
    /// count prose as an EXEMPTION. The forwarding window is three lines of source;
    /// scanned raw, a shadowing declaration followed within two lines by a comment
    /// that merely MENTIONS `BnWireVocabulary.&lt;symbol&gt;` would be waved through —
    /// the exemption-too-broad direction, which leaves the pin green while the class
    /// stays open. `///` and `//!` are covered by the `//` rule.</para>
    ///
    /// <para>Phase 14.1: the stripper itself moved to <see cref="CommentStrippedSource"/> so
    /// DispatchSurfaceDriftTests shares this exact logic instead of maintaining a second,
    /// driftable copy — the second copy is what had the single-line-block-comment bug.
    /// Phase 15.0 finished the job: five more copies were still out there, one of them
    /// hiding a bare NSLog behind a URL in a string literal.</para></summary>
    private static string[] CodeLines(string file) => CommentStrippedSource.Lines(file);

    /// <summary>A hand-written DECLARATION of the given generated symbol's name — not a
    /// use of it. In C, specifically a definition with its own initializer;
    /// `kYogaStyles[i]` and `sizeof(kYogaStyles[0])` index with something and do not
    /// match.</summary>
    private static string DeclarationPattern(string symbol, bool c, bool opConstant = false) => c
        ? $@"\b{Regex.Escape(symbol)}\s*\[\s*\]\s*=\s*\{{"
        : opConstant
            ? OpConstantDeclarationPattern(symbol)
            : $@"\b(?:static\s+let|let|val|var)\s+{Regex.Escape(symbol)}\b";

    /// <summary>A hand-written twin of a host-call op constant: the same name bound to an
    /// INTEGER LITERAL, e.g. <c>static let camera: Int32 = 4</c> or
    /// <c>const val CAMERA = 4</c> (Phase 16.1).
    ///
    /// <para>NARROWER THAN THE NAME ALONE, on purpose. The op names are also the natural
    /// names of the capability objects: <c>let camera = BnCamera()</c> in
    /// AppleShellBridge.swift is a property, not an op, and a name-only pattern flags it.
    /// What makes a declaration a second copy of the WIRE value is the literal integer, so
    /// that is what this matches.</para></summary>
    private static string OpConstantDeclarationPattern(string symbol)
        => $@"\b(?:static\s+let|let|val|var)\s+{Regex.Escape(symbol)}\s*(?::\s*\w+)?\s*=\s*-?(?:0[xX][0-9A-Fa-f]+|\d+)\s*$";

    /// <summary>THE FORWARDING WINDOW — the pin's SUPPRESSION BRANCH, and therefore the
    /// branch that can go quiet by accident (pin standard Rule 7). Scans the declaration
    /// line plus the two that follow for a qualified reference to the generated symbol.
    /// C definitions never forward — `BnWireVocabulary.kNodeTypes` cannot occur in an
    /// `#include`d header — so the window is never offered to them. Nor is it offered to op
    /// constants, which match only an integer literal and so cannot forward.</summary>
    private static bool Forwards(string[] lines, int index, string symbol)
    {
        for (int j = index; j < Math.Min(index + 3, lines.Length); j++)
            if (Regex.IsMatch(lines[j], $@"BnWireVocabulary\.{Regex.Escape(symbol)}\b"))
                return true;
        return false;
    }

    /// <summary>THE DETECTOR, over one file's already-stripped lines: every declaration
    /// of <paramref name="symbol"/>, one-based, each tagged with whether the forwarding
    /// window exempted it.
    ///
    /// <para>One implementation, driven by the pin, by <see cref="DeclarationSites"/>
    /// and by the positive control's C fixture alike (pin standard Rule 8). Taking
    /// LINES rather than a path is what lets the control splice a real declaration into
    /// real source and run the production detector over the result, instead of
    /// controlling a copy of it.</para>
    ///
    /// <para>The forwarding window is offered ONLY to a non-op Swift or Kotlin declaration. An op
    /// constant matches only when bound to an integer LITERAL, so it can never be a forwarder: a
    /// real forward, `const val CAMERA = HostCallOp.CAMERA`, does not match the pattern and is
    /// never a site at all (16.6 fix round 1: the window was dead for ops).</para></summary>
    private static List<(int Line, bool Forwards)> DeclarationSitesIn(string[] lines, string symbol, bool c, bool opConstant = false)
    {
        var sites = new List<(int, bool)>();
        for (int i = 0; i < lines.Length; i++)
            if (Regex.IsMatch(lines[i], DeclarationPattern(symbol, c, opConstant)))
                sites.Add((i + 1, !c && !opConstant && Forwards(lines, i, symbol)));
        return sites;
    }

    private readonly record struct DeclarationSite(string Source, string Symbol, int Line, bool Forwards);

    /// <summary>Every hand-written declaration of a generated symbol anywhere in the two
    /// shell trees, forwarding or not. The pin below reports the non-forwarding ones;
    /// its control asserts the forwarding ones are still SEEN and still EXEMPTED.</summary>
    private static List<DeclarationSite> DeclarationSites()
    {
        var sites = new List<DeclarationSite>();
        foreach ((string file, string symbol, _, string? opTable) in GeneratedSymbols())
        {
            bool c = IsCHeader(file);
            foreach (string source in ShellSources(file))
                foreach ((int line, bool forwards) in DeclarationSitesIn(CodeLines(source), symbol, c, opTable is not null))
                    sites.Add(new DeclarationSite(source, symbol, line, forwards));
        }
        return sites;
    }

    /// <summary>THE PIN. A hand-written declaration of a generated symbol's name
    /// shadows it: the generated value goes dead and the manifest stops governing
    /// that vocabulary, silently.
    ///
    /// BUT A DECLARATION IS NOT AUTOMATICALLY A SHADOW. Forwarding the generated
    /// value under the same name is the INTENDED consumption pattern this codebase
    /// already uses — `static let nodeTypes = BnWireVocabulary.nodeTypes` in
    /// BnFrameAdapter.swift, `private val YOGA_STYLES = BnWireVocabulary.YOGA_STYLES`
    /// in YogaLayout.kt — and it is also the shape Task 2's fix for #279 took.
    /// A pin that flagged every declaration, forwarding or not, could never go green
    /// with its own prescribed remedy. So a declaration only counts as an offender
    /// if its right-hand side does NOT derive from the generated symbol: no
    /// `BnWireVocabulary.&lt;symbol&gt;` appears on the declaration line or the two
    /// lines after it.
    ///
    /// THREE LINES, NOT ONE. Swift routinely splits the type annotation from the
    /// initializer:
    ///   private static let measuredNodeTypes: Set&lt;String&gt; =
    ///       Set(BnWireVocabulary.measuredNodeTypes)
    /// A one-line check would see only `private static let measuredNodeTypes:
    /// Set&lt;String&gt; =`, find no qualified reference, and misreport a correct
    /// forward as a shadow. The window has to reach past the line break — and it is
    /// read as CODE, never raw text, so a comment inside the window cannot buy the
    /// exemption (see <see cref="CodeLines"/>).
    ///
    /// C HAS NO QUALIFIED FORM. `BnWireVocabulary.g.h` is `#include`d, so its arrays
    /// are named bare — `BnWireVocabulary.kNodeTypes` cannot occur and the Swift rule
    /// would flag every declaration. The one C shape that DERIVES from the generated
    /// symbol rather than replacing it is an `extern` re-declaration, which names the
    /// same entity and has no initializer. So in C the offender is specifically a
    /// DEFINITION: `NAME[] = {`, its own literal, a second copy of the truth.
    ///
    /// <para>THIS IS AN ABSENCE CLAIM, so it is only worth what its detector is worth:
    /// see <see cref="TheShadowDetector_StillMatchesRealDeclarations_AndTheForwardingWindowStillFires"/>,
    /// which is the fixed point both the declaration pattern and the forwarding window
    /// are required to keep hitting.</para></summary>
    [Fact]
    public void NoGeneratedSymbol_IsShadowedByAHandWrittenDeclaration()
    {
        var offenders = DeclarationSites()
            .Where(s => !s.Forwards)
            .Select(s => $"{Path.GetRelativePath(BnRepo.Root(), s.Source)}:{s.Line} declares '{s.Symbol}', which WireGen generates")
            .ToList();

        Assert.True(offenders.Count == 0,
            "A generated symbol is shadowed by a hand-written declaration. The generated value goes "
            + "dead, the hand-written copy wins every use, and src/wire-vocabulary.json silently stops "
            + "governing that vocabulary — while the codegen tests stay green, because they compare "
            + "generated files to the manifest and never look at a hand-written twin. Consume the "
            + "generated symbol instead.\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>The two live forwarding declarations this detector must still SEE and
    /// still EXEMPT — one per language that has a qualified form. Both are the
    /// intended consumption pattern, both are hand-written production code, and both
    /// are exactly the shape #279's fix took, so neither can be rewritten away without
    /// somebody noticing.</summary>
    private static readonly (string Source, string Symbol)[] ForwardingFixedPoints =
    [
        ("BnFrameAdapter.swift", "nodeTypes"),   // static let nodeTypes = BnWireVocabulary.nodeTypes
        ("YogaLayout.kt", "YOGA_STYLES"),        // private val YOGA_STYLES = BnWireVocabulary.YOGA_STYLES
    ];

    /// <summary>THE POSITIVE CONTROL for the shadow-declaration detector (pin standard
    /// Rule 3; phase 15.0 census item 4 and §5.4).
    ///
    /// <para>WHAT WAS UNGUARDED. <see cref="NoGeneratedSymbol_IsShadowedByAHandWrittenDeclaration"/>
    /// is an ABSENCE claim standing on two moving parts: the declaration pattern, and
    /// the forwarding window that suppresses a match. <see cref="GeneratedSymbolFloor"/>
    /// proves the generated symbols were PARSED; nothing proved either of those two
    /// still fires over real shell source. Reword <see cref="DeclarationPattern"/> past
    /// its subject — a Swift or Kotlin syntax change, an over-eager escape — and the pin
    /// reports zero offenders forever while a hand-written twin sits in the tree. That is
    /// <b>#279 reopening silently</b>, which is the specific failure this file exists to
    /// prevent.</para>
    ///
    /// <para>SWIFT AND KOTLIN HAVE HONEST TREE ANCHORS, and they buy both halves at once.
    /// <see cref="ForwardingFixedPoints"/> names a live forwarding declaration in each
    /// language. Each must be MATCHED as a declaration — that controls the pattern — and
    /// then EXEMPTED by the window — that proves the suppression branch is entered at all,
    /// which the census flags as a Rule 7 concern in its own right, since no assertion
    /// previously proved anything ever reached it.</para>
    ///
    /// <para>C HAS NO TREE ANCHOR AND GETS A FIXTURE, for a structural reason rather than
    /// a shortage of searching. The C detector matches a DEFINITION, and a hand-written
    /// definition of a generated array is precisely what the pin forbids: the scanned tree
    /// is required to be empty of the shape, by construction. That is the difference from
    /// <c>NSLogDriftTests</c>, whose exempt bundle must still hold what the shipped tree
    /// must not. So the fixture is built from real parts: the REAL definition line WireGen
    /// emitted into the header, spliced into the REAL <c>BnYogaLayout.mm</c> immediately
    /// above its `#include` of that header — where a hand-written twin would actually be
    /// written — and run through <see cref="DeclarationSitesIn"/>, the pin's own detector.
    /// It requires exactly one site, AT THE SPLICED LINE, so the failure message's line
    /// fidelity is exercised too. The unspliced file is asserted to yield none, which is
    /// the negative: `sizeof(kYogaStyles[0])` and `kYogaStyles[i]` index with something and
    /// must keep being refused.</para>
    ///
    /// <para>WHAT THIS DOES NOT COVER (Rule 5). It controls the UNDER-matching direction
    /// for the pattern and the ENTERED direction for the window. It cannot catch a window
    /// that has become too BROAD — one that exempts a genuine shadow — because a widened
    /// window still exempts these two fixed points. That failure hands out a green, so it
    /// is a defect rather than a footnote; what stands against it today is that the window
    /// is three lines of already-stripped code and that a change to it is a change to
    /// <see cref="Forwards"/>, which no other pin shares.</para></summary>
    [Fact]
    public void TheShadowDetector_StillMatchesRealDeclarations_AndTheForwardingWindowStillFires()
    {
        // ── Swift and Kotlin: live forwarding declarations, matched then exempted ──
        var sites = DeclarationSites();

        foreach ((string source, string symbol) in ForwardingFixedPoints)
        {
            Assert.True(
                sites.Any(s => Path.GetFileName(s.Source) == source && s.Symbol == symbol && s.Forwards),
                $"the shadow detector no longer sees '{symbol}' declared-and-forwarded in {source}, "
                + "which is a live forwarding declaration and the fixed point this detector is "
                + $"required to hit (pattern: {DeclarationPattern(symbol, c: false)}). EITHER the "
                + "declaration pattern stopped matching real shell source — in which case "
                + "NoGeneratedSymbol_IsShadowedByAHandWrittenDeclaration is green over nothing and "
                + "#279 can reopen unseen — OR the forwarding window stopped firing, in which case "
                + "its suppression branch is now unexercised. If the forward itself was genuinely "
                + "moved or renamed, re-point this fixed point deliberately at the new one; do not "
                + "delete the row, because a detector with no fixed point is what this assertion "
                + "exists to forbid.\n  sites seen: "
                + string.Join(", ", sites.Select(s => $"{Path.GetFileName(s.Source)}:{s.Line} {s.Symbol} forwards={s.Forwards}")));
        }

        // ── C: a fixture, built from the real definition and the real consumer ──
        (string header, string cSymbol, bool _, string? _) = GeneratedSymbols().First(s => IsCHeader(s.File));
        string pattern = DeclarationPattern(cSymbol, c: true);

        string[] generated = File.ReadAllLines(header);
        int declaredAt = Array.FindIndex(generated, l => Regex.IsMatch(l, pattern));
        Assert.True(declaredAt >= 0,
            $"'{cSymbol}' has no definition matching {pattern} in {Path.GetFileName(header)} — the "
            + "file it was parsed OUT of. WireGen changed the shape it emits for a C array, so the "
            + "pin's C branch is now looking for a shape that no longer exists anywhere. Re-point "
            + "CArrayDeclaration and this pattern together.");

        string mm = Path.Combine(Path.GetDirectoryName(header)!, "BnYogaLayout.mm");
        Assert.True(File.Exists(mm),
            $"BnYogaLayout.mm is missing from {Path.GetDirectoryName(header)} — it is the one "
            + "Objective-C++ consumer of the generated header and the realistic home this fixture "
            + "splices into. Re-point the fixture at the new consumer rather than falling back to a "
            + "synthetic file, which would stop exercising real source.");

        string[] real = CodeLines(mm);
        Assert.Empty(DeclarationSitesIn(real, cSymbol, c: true));   // the negative: uses are not definitions

        int at = Array.FindIndex(real, l => l.Contains($"#include \"{Path.GetFileName(header)}\"", StringComparison.Ordinal));
        Assert.True(at >= 0,
            $"BnYogaLayout.mm no longer includes {Path.GetFileName(header)} — the fixture has no "
            + "realistic place to splice a twin, and more importantly the C half of the wire "
            + "vocabulary may have stopped being consumed at all. Check that before re-pointing.");

        var spliced = real.ToList();
        spliced.Insert(at, generated[declaredAt]);

        var found = DeclarationSitesIn([.. spliced], cSymbol, c: true);

        Assert.True(found.Count == 1 && found[0].Line == at + 1 && !found[0].Forwards,
            $"the C shadow detector did not report exactly one non-forwarding site at line {at + 1} "
            + $"after splicing the header's own definition of '{cSymbol}' into BnYogaLayout.mm.\n"
            + $"  spliced line: {generated[declaredAt].Trim()}\n"
            + $"  pattern:      {pattern}\n"
            + $"  found:        {(found.Count == 0 ? "(nothing)" : string.Join(", ", found.Select(f => $"line {f.Line} forwards={f.Forwards}")))}\n"
            + "Nothing found means a hand-written C twin would no longer be detected and the pin is "
            + "green over a blind spot. A wrong line number means the offender message would send a "
            + "reader to the wrong place. An exemption means the forwarding window is being offered "
            + "to C, which it must never be — C has no qualified form to forward through.");
    }

    /// <summary>THE POSITIVE CONTROL for the op-constant shadow shape (Rule 3, Phase 16.1).
    /// The tree must hold no hand-written op twin, so the anchor is a FIXTURE built from
    /// real parts, as the C half above does: the generated Swift and Kotlin constant lines
    /// themselves, spliced into the real files that route on them. Each must be reported
    /// at the spliced line; the unspliced files must report none, which is the negative
    /// for <c>let camera = BnCamera()</c>, a property that shares an op's name.
    ///
    /// <para>A HEX twin, <c>static let camera: Int32 = 0x4</c>, is matched too: the control
    /// splices the generated line rewritten in hex as a second fixture.</para>
    ///
    /// <para>DOES NOT COVER (Rule 5):
    /// <list type="bullet">
    /// <item>an op twin written with a non-literal initializer, such as
    /// <c>let camera: Int32 = 2 + 2</c>;</item>
    /// <item>an op twin written as an enum CASE, <c>case camera = 4</c> inside an
    /// <c>enum …: Int32</c>. The pattern matches only <c>let</c>/<c>val</c>/<c>var</c>
    /// bindings, and a case is not one.</item>
    /// </list>
    /// Both are left to review; the consumption pin still reds if the generated constant
    /// goes dead.</para></summary>
    [Fact]
    public void TheOpConstantShadowDetector_MatchesAnIntegerTwin_AndNotACapabilityProperty()
    {
        var ops = GeneratedSymbols().Where(s => s.OpTable is not null).ToList();
        Assert.True(ops.Count >= 12, $"parsed {ops.Count} host-call op constants, expected at least 12 (6 per language)");

        (string Consumer, string Symbol)[] fixtures =
        [
            (Path.Combine("BlazorNative.Apple", "BnHost", "AppleShellBridge.swift"), "camera"),
            (Path.Combine("BlazorNative.Jni", "src", "main", "kotlin", "io", "blazornative", "jni", "ShellBridge.kt"), "CAMERA"),
        ];
        foreach ((string consumer, string symbol) in fixtures)
        {
            var op = ops.Single(o => o.Symbol == symbol);
            string? twin = File.ReadAllLines(op.File).SingleOrDefault(l => Regex.IsMatch(l, OpConstantDeclarationPattern(symbol)));
            Assert.True(twin is not null,
                $"the op-constant pattern no longer matches the generated declaration of '{symbol}' in "
                + $"{Path.GetFileName(op.File)}, the very shape a hand-written twin would copy. The "
                + "detector is blind, and the shadow pin is green over nothing for op constants.");

            string[] real = CodeLines(Path.Combine(BnRepo.Root(), "src", consumer));
            var unspliced = DeclarationSitesIn(real, symbol, c: false, opConstant: true);
            Assert.True(unspliced.Count == 0,
                $"the op-constant pattern matches '{symbol}' in the unspliced {consumer} at line(s) "
                + $"{string.Join(", ", unspliced.Select(u => u.Line))}. It has widened past the integer "
                + "literal and now flags a capability property that merely shares the op's name.");

            // The generated line as written, then the same binding with its id in hex.
            string hexTwin = Regex.Replace(twin, @"=\s*(\d+)\s*$",
                m => "= 0x" + int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture).ToString("X", CultureInfo.InvariantCulture));
            Assert.NotEqual(twin, hexTwin);
            foreach (string candidate in new[] { twin, hexTwin })
            {
                var spliced = real.ToList();
                spliced.Insert(1, candidate);
                var found = DeclarationSitesIn([.. spliced], symbol, c: false, opConstant: true);
                Assert.True(found.Count == 1 && found[0].Line == 2 && !found[0].Forwards,
                    $"the op-constant shadow detector did not report the spliced twin '{candidate.Trim()}' "
                    + $"at line 2 of {consumer}; found {found.Count}. A hand-written op constant would no "
                    + "longer be detected.");
            }
        }
    }

    /// <summary>Generated symbols that nothing consumes, each with a written reason.
    ///
    /// <para>Being on this list is not an accusation — a generated symbol with no
    /// consumer and no hand-written twin is harmless. It is here so that ADDING one
    /// is a decision somebody wrote down, rather than a file quietly growing a dead
    /// symbol that a future hand-written twin can then shadow. That progression is
    /// exactly how #279 happened.</para>
    ///
    /// <para>KEYED BY LANGUAGE AND SYMBOL, not by symbol alone (Phase 16.2). An entry
    /// is a reason about ONE generated file's copy of a name: Swift's
    /// <c>yogaStyles</c> being test-only says nothing about a Kotlin or C symbol that
    /// happens to share the spelling. Keyed by name alone, one entry would silently
    /// exempt every language's copy. <see cref="UnconsumedByDesign_IsKeyedByLanguage"/>
    /// pins the keying.</para></summary>
    private static readonly Dictionary<(string Language, string Symbol), string> UnconsumedByDesign = new()
    {
        [("Swift", "visualStyles")] =
            "Swift has no visual-style routing of its own — BnWidgetMapper switches on style names "
            + "directly. Emitted for symmetry with Kotlin and byte-pinned by the codegen tests.",
        [("Swift", "scrollIgnoredContainerStyles")] =
            "Same: the Swift scroll path checks the names inline. Emitted for symmetry, byte-pinned.",
        [("Swift", "yogaStyles")] =
            "Swift's layout path never names it: the Yoga style question is answered in "
            + "Objective-C++ by bn_yoga_is_layout_style over the C header's kYogaStyles, which IS "
            + "consumed. Only BnHostTests reads the Swift copy, and tests are not consumers (Phase "
            + "16.2: ShippedShellSources). Emitted for symmetry, byte-pinned by the codegen tests.",
        [("Kotlin", "VISUAL_STYLES")] =
            "Kotlin's WidgetMapper.kt switches on style-name literals directly (\"backgroundColor\" ->, "
            + "\"color\" ->, \"fontSize\" ->) rather than checking membership in this set — the same "
            + "pattern as Swift's visualStyles. Verified no hand-written twin exists (not a #279 shadow). "
            + "Emitted for symmetry, byte-pinned by the codegen tests.",
        [("C", "kNodeTypes")] =
            "The Objective-C++ layer is a YOGA seam and nothing else: BnYogaLayout.mm and BnYogaProbe.mm "
            + "answer style questions (bn_yoga_is_layout_style, bn_yoga_is_scroll_ignored_container_style) "
            + "and never see a node type — the wire byte is decoded and routed entirely in Swift, where "
            + "BnFrameAdapter forwards BnWireVocabulary.nodeTypes. So the header's other two arrays are "
            + "consumed and this one is emitted for symmetry with them, byte-pinned by the codegen tests. "
            + "Verified no hand-written twin exists in the Apple tree (not a #279 shadow): `kNodeTypes` "
            + "occurs exactly once, in its own generated declaration.",
        // A generated op that lands before its shell arms goes here as a TEMPORARY entry with
        // the reason PendingShellArm; the stale-entry check in the pin below reds the moment an
        // arm consumes the symbol, so it cannot outlive the arm. Phase 16.2 used it for ops 6
        // and 7, and removed both once the Android and iOS arms landed.
    };

    private const string PendingShellArm =
        "Generated before the shells route it. Until the arm lands the op takes the shell's "
        + "unknown-op branch, which completes with Error. REMOVE this entry when the arm lands; "
        + "the stale-entry check forces it.";

    /// <summary>The language a generated shell file is written in, which is the first half of
    /// an <see cref="UnconsumedByDesign"/> key. Throws for a file it does not know, so a new
    /// generated file is keyed deliberately rather than falling into another language.</summary>
    private static string LanguageOf(string generatedFile) =>
        generatedFile.EndsWith(".swift", StringComparison.Ordinal) ? "Swift"
        : generatedFile.EndsWith(".kt", StringComparison.Ordinal) ? "Kotlin"
        : IsCHeader(generatedFile) ? "C"
        : throw new InvalidOperationException($"no language for generated file {generatedFile}");

    /// <summary>The written reason <paramref name="symbol"/>, as generated into
    /// <paramref name="generatedFile"/>, has no consumer, or null if it has none.</summary>
    private static string? UnconsumedReason(string generatedFile, string symbol) =>
        UnconsumedByDesign.TryGetValue((LanguageOf(generatedFile), symbol), out string? reason) ? reason : null;

    /// <summary>The shell source that SHIPS: <see cref="ShellSources"/> minus the test trees,
    /// `BnHostTests/`, `src/test/` and `src/androidTest/`.
    ///
    /// <para>Phase 16.2: a test naming a generated symbol is not a consumer. With the tests
    /// scanned, deleting the Swift `BnHostCallOp.backState` arm left the consumption pin
    /// green, because `BnBackOffMainTests.swift` names the op, so the pin could not see its
    /// subject leave (pin standard Rule 4). Only the consumption pin uses this. The shadow
    /// pin keeps scanning the tests: a hand-written twin there is still worth flagging.</para></summary>
    private static string[] ShippedShellSources(string generatedFile)
    {
        char s = Path.DirectorySeparatorChar;
        string[] testTrees = [$"{s}BnHostTests{s}", $"{s}src{s}test{s}", $"{s}src{s}androidTest{s}"];
        return [.. ShellSources(generatedFile)
            .Where(f => !testTrees.Any(t => f.Contains(t, StringComparison.Ordinal)))];
    }

    /// <summary>A host-call op is CONSUMED only by a routing arm: Swift's
    /// <c>case BnHostCallOp.backState:</c> or Kotlin's <c>HostCallOp.BACK_STATE -&gt;</c>, alone
    /// or among other ops in one arm. A bare reference is not enough.
    ///
    /// <para>Phase 16.2: Kotlin's <c>deliverBackState</c> ends in
    /// <c>completeNotice(HostCallOp.BACK_STATE, requestId)</c>. With the name-only check,
    /// deleting the <c>HostCallOp.BACK_STATE -&gt;</c> arm left that call naming the op, and the
    /// pin stayed green while BackState took the unknown-op branch. The arm, not the name, is
    /// what an op's consumption means.</para></summary>
    private static string OpArmPattern(string opTable, string symbol)
    {
        string t = Regex.Escape(opTable);
        string op = $@"{t}\.{Regex.Escape(symbol)}\b";
        string other = $@"{t}\.\w+";
        return opTable == "BnHostCallOp"
            ? $@"^\s*case\s+(?:{other}\s*,\s*)*{op}\s*(?:,\s*{other}\s*)*:"
            : $@"^\s*(?:{other}\s*,\s*)*{op}\s*(?:,\s*{other}\s*)*->";
    }

    /// <summary>Rule 3 control for <see cref="OpArmPattern"/>: it matches an arm, alone and
    /// among other ops, in both languages, and does NOT match a call that merely names the op,
    /// which is the Kotlin shape that hid a deleted arm.
    ///
    /// <para>DOES NOT COVER: an arm that routes through a `let` or a range, or a Swift
    /// `case` split over several lines. None exists today; each would read as unconsumed and
    /// fail RED, not green.</para></summary>
    [Fact]
    public void OpArmPattern_MatchesAnArm_AndNotABareReference()
    {
        string swift = OpArmPattern("BnHostCallOp", "backState");
        Assert.Matches(swift, "        case BnHostCallOp.backState:");
        Assert.Matches(swift, "        case BnHostCallOp.faultNotice, BnHostCallOp.backState:");
        Assert.DoesNotMatch(swift, "        let rc = bridge.hostCallBegin(60, BnHostCallOp.backState, \"{}\")");
        Assert.DoesNotMatch(swift, "        case BnHostCallOp.backStateX:");

        string kotlin = OpArmPattern("HostCallOp", "BACK_STATE");
        Assert.Matches(kotlin, "                HostCallOp.BACK_STATE -> deliverBackState(requestId, argsJson)");
        Assert.Matches(kotlin, "                HostCallOp.FAULT_NOTICE, HostCallOp.BACK_STATE -> x()");
        Assert.DoesNotMatch(kotlin, "            completeNotice(HostCallOp.BACK_STATE, requestId)");
        Assert.DoesNotMatch(kotlin, "                HostCallOp.BACK_STATE_X -> x()");
    }

    /// <summary>Rule 3 control for the keying and the test-tree exclusion together. An
    /// entry exempts one language's copy only, so the Swift <c>yogaStyles</c> entry must not
    /// exempt the same spelling generated into Kotlin or C. And the exclusion must still
    /// be doing work: the Swift tests DO name <c>yogaStyles</c>, so it is dead only because
    /// they are no longer scanned, and each test tree must still exist and hold source.
    ///
    /// <para>DOES NOT COVER: a NEW test tree under a path the exclusion does not name, such
    /// as a future `src/iosTest/`. That fails GREEN: the consumption pin would count its
    /// references as consumers again. A RENAMED tree is covered, because the existence
    /// checks below red when one of the three named trees disappears.</para></summary>
    [Fact]
    public void UnconsumedByDesign_IsKeyedByLanguage()
    {
        string root = BnRepo.Root();
        string swift = Path.Combine(root, "src", "BlazorNative.Apple", "BnHost", "BnWireVocabulary.g.swift");
        string kotlin = Path.Combine(root, "src", "BlazorNative.Jni", "src", "main", "kotlin", "io", "blazornative", "jni", "BnWireVocabulary.g.kt");
        string c = Path.Combine(root, "src", "BlazorNative.Apple", "BnHost", "BnWireVocabulary.g.h");

        Assert.NotNull(UnconsumedReason(swift, "yogaStyles"));
        Assert.Null(UnconsumedReason(kotlin, "yogaStyles"));
        Assert.Null(UnconsumedReason(c, "yogaStyles"));

        foreach (string tree in new[]
        {
            Path.Combine(root, "src", "BlazorNative.Apple", "BnHostTests"),
            Path.Combine(root, "src", "BlazorNative.Jni", "src", "test"),
            Path.Combine(root, "src", "BlazorNative.Jni", "src", "androidTest"),
        })
        {
            Assert.True(Directory.Exists(tree),
                $"the test tree {tree} is gone. ShippedShellSources excludes it by path; re-point the "
                + "exclusion deliberately, or test references count as consumers again.");
        }

        string[] all = ShellSources(swift);
        string[] shipped = ShippedShellSources(swift);
        string reference = @"BnWireVocabulary\.yogaStyles\b";
        Assert.True(all.Any(f => CodeLines(f).Any(l => Regex.IsMatch(l, reference))),
            "no Swift source names BnWireVocabulary.yogaStyles any more, so this control no longer "
            + "shows the exclusion doing work. Re-point it at another test-only reference.");
        Assert.False(shipped.Any(f => CodeLines(f).Any(l => Regex.IsMatch(l, reference))),
            "shipped Swift now names BnWireVocabulary.yogaStyles: remove its UnconsumedByDesign entry "
            + "and re-point this control, or the exclusion has stopped excluding BnHostTests.");
    }

    /// <summary>Advisory pin: a generated symbol is consumed, or it is on the list above
    /// with a reason. Catches the state that PRECEDES a shadow — a dead generated symbol
    /// is what a hand-written twin later shadows without anyone noticing.
    ///
    /// <para>Consumption is read from CODE, not raw text: a symbol named only in a
    /// comment is not a consumer, and this file's own shells discuss these names at
    /// length (`BnYogaLayout.h` explains both C arrays in prose).</para>
    ///
    /// <para>THREE REFERENCE SHAPES, NOT TWO. `BnWireVocabulary` members are named
    /// through that object (`BnWireVocabulary.nodeTypes`); C's `#include`d arrays are
    /// named bare. A `BnHostEvent` enum member — `wireName` today — is neither: it is
    /// a per-case property, referenced by property access on an enum INSTANCE
    /// (`event.wireName`), and no `BnWireVocabulary.wireName` will ever exist for it
    /// to match. Without this third shape the detector reports every enum property
    /// dead regardless of real use, which is what happened here: `wireName` is
    /// consumed as `event.wireName` in `BlazorNativeRuntime.kt`. <see cref="ShippedShellSources"/>
    /// itself only walks `src/BlazorNative.Jni`, minus its test trees — the `templates/` mirror is NOT
    /// independently scanned here; it is held byte-identical to that file by
    /// TemplateDriftTests instead, which is how consumption in the template copy
    /// is actually guaranteed, not by this test reaching it directly. The old
    /// two-shape check could not see even the `src/BlazorNative.Jni` copy. The
    /// new pattern is scoped to symbols <see cref="GeneratedSymbols"/>
    /// tagged as enum members — it does not widen matching for
    /// `BnWireVocabulary`-object symbols, which would accept a bare name occurring
    /// anywhere and defeat the guard's purpose.</para></summary>
    [Fact]
    public void EveryGeneratedSymbol_IsConsumed_OrAllowlistedWithAReason()
    {
        var dead = new List<string>();
        var stale = new List<string>();

        foreach ((string file, string symbol, bool inHostEventEnum, string? opTable) in GeneratedSymbols())
        {
            // BnWireVocabulary members are named through that object; a BnHostEvent
            // enum member is named through property access on an enum instance
            // instead; a host-call op constant is named through its own table type;
            // C `#include`s the header and names its arrays bare.
            string reference = IsCHeader(file)
                ? $@"\b{Regex.Escape(symbol)}\b"
                : opTable is not null
                    ? OpArmPattern(opTable, symbol)
                    : inHostEventEnum
                        ? $@"\.{Regex.Escape(symbol)}\b"
                        : $@"BnWireVocabulary\.{Regex.Escape(symbol)}\b";

            // Shipped source only: a test naming the symbol is not a consumer (Phase 16.2).
            bool referenced = ShippedShellSources(file)
                .Any(src => CodeLines(src).Any(line => Regex.IsMatch(line, reference)));

            string? reason = UnconsumedReason(file, symbol);
            if (!referenced && reason is null)
                dead.Add($"{symbol} (generated into {Path.GetFileName(file)})");
            else if (referenced && reason == PendingShellArm)
                stale.Add($"{symbol} (generated into {Path.GetFileName(file)})");
        }

        // A temporary entry whose symbol is now consumed is a reason that has become false.
        // Scoped to PendingShellArm entries: the older entries are permanent by design.
        Assert.True(stale.Count == 0,
            "A generated symbol is consumed but still allowlisted as waiting for its shell arm. "
            + "The arm has landed, so the reason is false: remove the entry from UnconsumedByDesign.\n  "
            + string.Join("\n  ", stale));

        Assert.True(dead.Count == 0,
            "A generated symbol has no consumer and no written reason. It is harmless TODAY — but a "
            + "dead generated symbol is what a hand-written twin later shadows, which is how #279 "
            + "happened. Either consume it, or add it to UnconsumedByDesign with a reason.\n  "
            + string.Join("\n  ", dead));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NoProductionShellSource_CallsTheHostEventSeamsDirectly — Phase 14.0 final
    // review, item 1(a) (PR #341).
    //
    // BlazorNativeRuntime.kt's KDoc on dispatchHostEventUnchecked claimed the
    // BnHostEvent enum overload "makes that rc unreachable from production code
    // by construction" — false in the strong sense. dispatchHostEventUnchecked
    // (name: String) and dispatchHostEventBlocking(name: String) are both
    // `internal`, both take a bare String, and nothing in Kotlin stops a
    // production file in the SAME module from calling either with an arbitrary
    // literal. Zero production call sites exist today (verified by grep across
    // src/, templates/ and samples/ before this pin existed) — but "nothing does
    // today" is not a mechanism, and an unenforced safety claim in a comment is
    // exactly the bug class this repo treats as most dangerous (three separate
    // incidents in one week were each a comment asserting a safety property
    // nothing enforced). This pin IS the mechanism the KDoc claims.
    //
    // It reuses this file's own CodeLines (comment-stripped source) rather than
    // a second comment-stripper, so a KDoc cross-reference like
    // `[dispatchHostEventUnchecked]` can never count as an offending call.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>The two Kotlin seam names this pin forbids outside their own home
    /// file and outside test source sets. (Swift has no equivalent named seam —
    /// its bypass door is the imported C symbol `blazornative_host_event` called
    /// bare, which is a different shape this pin does not cover.)
    ///
    /// <para>NAMED "UNFLOORED" ON PURPOSE. Every consumer must come through
    /// <see cref="HostEventSeams"/>, which is where the floor lives; reading this field directly
    /// bypasses it. The name is the signal, because a third consumer added later would otherwise
    /// reach the bare array with nothing saying not to — and the floor exists precisely because the
    /// two consumers that already existed both did.</para></summary>
    private static readonly string[] HostEventSeamNamesUnfloored =
        ["dispatchHostEventUnchecked", "dispatchHostEventBlocking"];

    /// <summary>THE FLOOR ON THE ITERATED SET — one implementation at the single point BOTH
    /// consumers reach it (pin standard Rule 2, and Rule 8 for why it is not two copies).
    ///
    /// <para>WHAT WAS UNGUARDED, and it is this milestone's own headline shape. The pin below
    /// floors its WALK (<c>sources.Length &gt; 0</c>) and the control below floors its walk too,
    /// but the set they both ITERATE was a bare literal array that nothing floored. Replacing
    /// <see cref="HostEventSeamNamesUnfloored"/> with <c>[]</c> left BOTH facts GREEN — measured by the
    /// 15.1 branch review, not reasoned about. A floor on the walk with the iterated set bare is
    /// the exact defect phase 15.1 exists to remove, and it was shipping inside the phase.</para>
    ///
    /// <para>WHY THE NUMBER IS 2. One entry per raw-String door that exists: BlazorNativeRuntime.kt
    /// declares exactly two, <c>dispatchHostEventUnchecked</c> and <c>dispatchHostEventBlocking</c>,
    /// and that count is not this comment's word for it — the width coupling at the end of
    /// <see cref="TheSeamCallDetector_StillMatchesTheCallsTheTestSourceSetExistsToMake"/> reads the
    /// declarations out of the home file and requires this list to name exactly them. So the floor
    /// cannot drift out of step with the tree the way a hand-chosen number would.</para>
    ///
    /// <para>A ONE-ELEMENT LIST MUST RED, and that is the deliberate answer rather than a side
    /// effect of picking a round number. Dropping a seam from this list while it still compiles in
    /// Kotlin does not narrow the pin loudly — it narrows it silently, leaving a live raw-String
    /// door with nothing watching it, which is the precise failure this pin was written to prevent.
    /// A seam legitimately going away is a deliberate re-point: delete it in Kotlin first, and the
    /// coupling will tell you to lower this floor with it.</para></summary>
    private static string[] HostEventSeams()
    {
        Assert.True(HostEventSeamNamesUnfloored.Length >= 2,
            $"HostEventSeamNamesUnfloored names only {HostEventSeamNamesUnfloored.Length} seam(s) — "
            + $"[{string.Join(", ", HostEventSeamNamesUnfloored)}]. This list is the set BOTH "
            + "NoProductionShellSource_CallsTheHostEventSeamsDirectly and its positive control "
            + "iterate, so shrinking it does not make either fact fail: it makes them run fewer "
            + "iterations, and at zero it makes them pass over nothing at all. There are two "
            + "raw-String host-event doors in BlazorNativeRuntime.kt and there must be one entry "
            + "here per door. If a door was genuinely removed from Kotlin, re-point this "
            + "deliberately — lower the floor in the same commit that deletes the declaration, and "
            + "say which door went.");

        return HostEventSeamNamesUnfloored;
    }

    /// <summary>A CALL SITE — a word boundary plus an open paren — not a bare mention.
    /// One implementation, driven by the pin and by its positive control alike (pin
    /// standard Rule 8).</summary>
    private static string SeamCallPattern(string seam) => $@"\b{seam}\s*\(";

    /// <summary>The Kotlin unit-test source set: excluded from the pin's scan BY DESIGN,
    /// and therefore the one tree that must still contain direct seam calls — which is
    /// what makes it the fixed point in
    /// <see cref="TheSeamCallDetector_StillMatchesTheCallsTheTestSourceSetExistsToMake"/>.</summary>
    private const string SeamTestSourceSet = "src/BlazorNative.Jni/src/test/kotlin";

    /// <summary>The seams' own home file — the one production file that DECLARES them, which is
    /// why <see cref="ProductionHostEventSources"/> excludes it by name. It is also the anchor for
    /// the width coupling in
    /// <see cref="TheSeamCallDetector_StillMatchesTheCallsTheTestSourceSetExistsToMake"/>. Only the
    /// repo copy is read: TemplateDriftTests pins the template's mirror byte-identical to it.</summary>
    private const string SeamHomeFile =
        "src/BlazorNative.Jni/src/main/kotlin/io/blazornative/jni/BlazorNativeRuntime.kt";

    /// <summary>A RAW-STRING DOOR: a host-event function in the home file that another file in the
    /// module can call with an arbitrary name. Two halves carry the meaning. <c>internal</c> is
    /// load-bearing — <c>hostEventCore(name: String, …)</c> has the same parameter shape and is
    /// <c>private</c>, so no other file can reach it and it is not a door. The bare
    /// <c>name: String</c> first parameter is the other half: <c>dispatchHostEvent</c> and
    /// <c>dispatchHostEventAndWait</c> are <c>internal</c> too, but they take
    /// <c>event: BnHostEvent</c>, which is the enum overload the manifest governs — the sanctioned
    /// entry point rather than a bypass of it.</summary>
    private const string SeamDeclarationPattern =
        @"\binternal\s+fun\s+(\w*[Hh]ost[Ee]vent\w*)\s*\(\s*name\s*:\s*String";

    /// <summary>PRODUCTION Kotlin only: the `main` + `androidMain` source sets
    /// (see build.gradle.kts: <c>java.srcDirs("src/main/kotlin",
    /// "src/androidMain/kotlin")</c>) — never `test` or `androidTest`, which
    /// legitimately call these seams directly (that is what they are FOR: the
    /// rc 3 malformed-name path is only reachable through a bare String, and the
    /// lane/onError routing has to be provable with an arbitrary name).
    ///
    /// <para>Scanned in BOTH `src/BlazorNative.Jni` and its `templates/`
    /// mirror: TemplateDriftTests pins the template's copy of
    /// BlazorNativeRuntime.kt byte-identical to the repo's, but a hypothetical
    /// OTHER template-only file calling the seam directly would be exactly as
    /// reachable from a consumer's compiled app as one in the repo's own shell,
    /// and nothing else scans the template tree for this.</para>
    ///
    /// <para><c>BlazorNativeRuntime.kt</c> itself (and its template mirror) is
    /// EXCLUDED: it is the seam's own home file, declares both functions, and
    /// legitimately calls <c>dispatchHostEventUnchecked</c> once — from
    /// <c>dispatchHostEvent(event: BnHostEvent, ...)</c>, passing
    /// <c>event.wireName</c>. That is the enum overload's own implementation,
    /// not a bypass of it.</para></summary>
    private static string[] ProductionHostEventSources()
    {
        string root = BnRepo.Root();
        string[] roots =
        [
            Path.Combine(root, "src", "BlazorNative.Jni", "src", "main", "kotlin"),
            Path.Combine(root, "src", "BlazorNative.Jni", "src", "androidMain", "kotlin"),
            Path.Combine(root, "templates", "BlazorNative.Templates", "content", "BlazorNative.App", "android", "src", "main", "kotlin"),
            Path.Combine(root, "templates", "BlazorNative.Templates", "content", "BlazorNative.App", "android", "src", "androidMain", "kotlin"),
        ];

        return [.. roots
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.kt", SearchOption.AllDirectories))
            .Where(f => !f.Contains(".g.", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}build{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => Path.GetFileName(f) != "BlazorNativeRuntime.kt")];
    }

    /// <summary>THE PIN. See the section header above for the false claim this
    /// closes. A production call to either seam is caught as a call SITE
    /// (`name(` — a word boundary plus an open paren), not a bare mention, so a
    /// KDoc `[dispatchHostEventBlocking]` cross-reference elsewhere in a
    /// production file — of which this codebase has several, forwarding to it
    /// from doc comments on `dispatchHostEvent` and `dispatchHostEventAndWait`
    /// — does not false-positive; CodeLines already strips those, but the
    /// call-site shape is the real reason a bare identifier mention is safe.
    ///
    /// <para>THIS IS AN ABSENCE CLAIM, and `sources.Length > 0` floors only the WALK.
    /// The detector's own fixed point is
    /// <see cref="TheSeamCallDetector_StillMatchesTheCallsTheTestSourceSetExistsToMake"/>.</para></summary>
    [Fact]
    public void NoProductionShellSource_CallsTheHostEventSeamsDirectly()
    {
        // THE ITERATED SET, floored before the walk is even attempted: an empty seam list makes
        // the offender loop below run zero iterations and report clean forever, and no assertion
        // about the file walk can notice that. See HostEventSeams for the measurement.
        string[] seams = HostEventSeams();

        string[] sources = ProductionHostEventSources();
        Assert.True(sources.Length > 0,
            "ProductionHostEventSources() found zero Kotlin files — the Android source-set layout "
            + "moved and this pin can no longer see its subject, which would leave it green while "
            + "checking nothing. Fix the scan.");

        var offenders = new List<string>();
        foreach (string source in sources)
        {
            string[] lines = CodeLines(source);
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (string seam in seams)
                {
                    if (Regex.IsMatch(lines[i], SeamCallPattern(seam)))
                        offenders.Add($"{Path.GetFileName(source)}:{i + 1} calls {seam}(...)");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Production shell code calls a host-event seam that exists for tests only. Dispatch "
            + "through the BnHostEvent overload instead (dispatchHostEvent for fire-and-forget, "
            + "dispatchHostEventAndWait for the blocking rc) — that is the only sanctioned "
            + "production entry point, and it is how the manifest actually governs what a shell "
            + "can send. If a genuinely new raw-string production need exists, that is a design "
            + "decision requiring a recorded reason, not a quiet call to dispatchHostEventUnchecked "
            + "or dispatchHostEventBlocking.\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>THE POSITIVE CONTROL for the seam call-site detector (pin standard Rule 3;
    /// phase 15.0 census item 5 and §5.4).
    ///
    /// <para>WHAT WAS UNGUARDED. The pin above floors its WALK — `sources.Length > 0` — and
    /// nothing floored its DETECTOR. Reword <see cref="SeamCallPattern"/> past its subject,
    /// or rename a seam in <see cref="HostEventSeamNamesUnfloored"/> without renaming it in Kotlin,
    /// and the pin reports zero offenders forever while a production file calls the raw-String
    /// door directly. The KDoc this pin exists to make true would go back to being an
    /// unenforced safety claim in a comment, which is the class the section header above
    /// calls this repo's most dangerous.</para>
    ///
    /// <para>THE FIXED POINT IS THE EXCLUSION ITSELF, which is <c>NSLogDriftTests</c>' design
    /// applied to a different exclusion. <see cref="ProductionHostEventSources"/> deliberately
    /// scans only the production source sets, because the unit tests legitimately call both
    /// seams directly — that is what the seams are FOR: rc 3 is only reachable through a bare
    /// String. So the excluded tree is required to keep containing exactly what the scanned
    /// tree must not, and the detector is required to keep finding it there. The census
    /// recorded this as needing a fixture; it does not — the anchor is real code in the tree,
    /// and a real anchor beats a fixture whenever one exists, because it also notices when the
    /// seam stops being exercised at all.</para>
    ///
    /// <para>THE WIDTH IS COUPLED, at the end, and that is a separate claim from the one
    /// above. The loop proves the detector still matches the seams we listed; it cannot notice
    /// a THIRD raw-String door being added to the home file and never listed, which would be
    /// unpinned from birth. So the last assertion reads the doors back out of
    /// <see cref="SeamHomeFile"/> — <c>internal</c> functions taking a bare
    /// <c>name: String</c>, see <see cref="SeamDeclarationPattern"/> — and requires
    /// <see cref="HostEventSeamNamesUnfloored"/> to name exactly them. It also makes the floor in
    /// <see cref="HostEventSeams"/> answerable to the tree rather than to a comment: the number
    /// 2 is not a judgement, it is how many doors the file declares.</para>
    ///
    /// <para>WHAT THIS DOES NOT COVER (Rule 5). It controls the UNDER-matching direction
    /// only: the pattern still recognises a real call. It says nothing about Swift, whose
    /// bypass door is the imported C symbol <c>blazornative_host_event</c> called bare — a
    /// different shape that the pin does not cover either, stated on
    /// <see cref="HostEventSeamNamesUnfloored"/>. The width coupling reaches the home file ONLY: a
    /// raw-String door declared in some OTHER production Kotlin file would be a door this list
    /// never hears about, and nothing here would say so. That is a narrower residual than the
    /// "guarded by review" this paragraph used to claim, but it is still a residual.</para></summary>
    [Fact]
    public void TheSeamCallDetector_StillMatchesTheCallsTheTestSourceSetExistsToMake()
    {
        // THE ITERATED SET — the same floor the pin runs, from the same implementation, because
        // this control iterates the same list and emptying it left THIS fact green too.
        string[] seams = HostEventSeams();

        string dir = Path.Combine(BnRepo.Root(), SeamTestSourceSet.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(Directory.Exists(dir),
            $"{SeamTestSourceSet} is missing, so the exclusion the pin above relies on protects "
            + "nothing and this control has no fixed point. Either the Kotlin unit-test source set "
            + "moved — then re-point BOTH this control and ProductionHostEventSources deliberately "
            + "— or it is gone, in which case the seams have no sanctioned caller left and that is "
            + "the thing to look at.");

        string[] files = [.. Directory.EnumerateFiles(dir, "*.kt", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}build{Path.DirectorySeparatorChar}", StringComparison.Ordinal))];

        Assert.True(files.Length > 0,
            $"no Kotlin files under {SeamTestSourceSet} — the walk this control depends on found "
            + "nothing, so the assertion below would be checking nothing.");

        foreach (string seam in seams)
        {
            var hits = files
                .SelectMany(f => CodeLines(f).Select((line, i) => (File: f, Line: i + 1, Text: line)))
                .Where(x => Regex.IsMatch(x.Text, SeamCallPattern(seam)))
                .ToList();

            Assert.True(hits.Count > 0,
                $"the seam call-site pattern matched NO call to {seam} anywhere under "
                + $"{SeamTestSourceSet} (pattern: {SeamCallPattern(seam)}), which is the one tree "
                + "that MUST still contain direct calls to it — the pin above excludes that tree "
                + "precisely because these calls are legitimate there. EITHER the pattern no longer "
                + "matches a real Kotlin call, in which case "
                + "NoProductionShellSource_CallsTheHostEventSeamsDirectly is holding NOTHING and a "
                + "production bypass would pass unseen, OR the tests stopped calling the seam, in "
                + "which case the seam's rc paths are now unexercised and this fixed point must be "
                + "re-pointed deliberately rather than deleted.");
        }

        // ── THE WIDTH COUPLING — read the doors out of the home file, last ──────────
        // Ordered after the loop on purpose: a seam reworded past its subject should fail with
        // the detector's message above, which names the excluded tree and the two ways it can
        // break, not with this one.
        string home = Path.Combine(BnRepo.Root(), SeamHomeFile.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(home),
            $"{SeamHomeFile} is missing, so the seams' declarations cannot be read and the width "
            + "coupling below has nothing to compare against. The file moved — re-point this AND "
            + "ProductionHostEventSources' by-name exclusion of it together, because that exclusion "
            + "is keyed on the same file and would otherwise start excluding nothing.");

        string[] declared = [.. CodeLines(home)
            .Select(line => Regex.Match(line, SeamDeclarationPattern))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)];

        string[] listed = [.. seams.OrderBy(n => n, StringComparer.Ordinal)];

        Assert.True(declared.SequenceEqual(listed, StringComparer.Ordinal),
            "The seam list and the raw-String doors actually declared in "
            + $"{SeamHomeFile} have diverged.\n"
            + $"  declared there : [{string.Join(", ", declared)}]\n"
            + $"  named here     : [{string.Join(", ", listed)}]\n"
            + "A door declared there but NOT named here is UNPINNED — production code can call it "
            + "with an arbitrary name and NoProductionShellSource_CallsTheHostEventSeamsDirectly "
            + "will never look for it. A name here that is NOT declared there is a pin pointed at "
            + "nothing, and it also makes HostEventSeams' floor of 2 a number with no subject "
            + "behind it. An EMPTY declared set means this scan stopped recognising a Kotlin "
            + "declaration — pattern: " + SeamDeclarationPattern + " — which is the same failure "
            + "wearing a different hat: re-point it rather than editing the list to match.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Phase 16.2 Task 5 fix round (final whole-branch review, Important #1) —
    // dispatchHostEventAndWait BECOMES A DOOR NOTHING CAN OPEN, NOT JUST A KEYWORD
    // NOBODY CHECKS.
    //
    // Task 5 made the method `internal` in both shells and pinned the KEYWORD:
    // DispatchSurfaceDriftTests.MethodsWithADeclaredVisibility_MatchBothShells and the
    // JVM DispatchHostEventAndWaitVisibilityTest. Neither stops a CALLER. Kotlin's
    // `internal` is module-wide and MainActivity shares BlazorNativeRuntime's module;
    // Swift's BnHost is an app target where a class with no modifier is already
    // `internal`, so the new keyword changed nothing a same-target caller could not
    // already do. The review measured it directly: reverting onNewIntent's call back
    // to the blocking one, in the repo copy only, left Runtime at 1102/1102 and the
    // JVM suite green — the #346 shape back on main with every existing gate still
    // green.
    //
    // THE FIX IS A SOURCE SCAN, not a stronger keyword — there is no stronger
    // Kotlin/Swift visibility available that still reaches same-module/`@testable`
    // tests. So the guard is "nothing SHIPPED calls it", read from source, the same
    // shape NoProductionShellSource_CallsTheHostEventSeamsDirectly already uses for
    // the raw-String doors — except this one must also see the method's OWN home
    // file, because the exact regression (a navigator closure inside BnRuntime.swift,
    // or MainActivity's onNewIntent) can sit right next to the declaration it must
    // not call. Excluding the home file by name, the way ProductionHostEventSources
    // excludes BlazorNativeRuntime.kt for the seam pin, would have hidden precisely
    // the bug this pin exists to catch. So the detector excludes only the
    // DECLARATION LINE itself (`fun`/`func dispatchHostEventAndWait(`), never the file.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Every production Kotlin file under `src/main/kotlin` and
    /// `src/androidMain/kotlin`, repo AND template mirror — deliberately NOT filtered by
    /// filename the way <see cref="ProductionHostEventSources"/> excludes
    /// `BlazorNativeRuntime.kt`. That exclusion is right for the raw-String-seam pin,
    /// whose only legitimate callers live outside the seam's home file; it would be
    /// WRONG here, because the exact regression this pin exists to catch — a caller
    /// reaching back for the blocking overload — can sit in the SAME file as the
    /// declaration. <see cref="IsOffendingCallLine"/> excludes only the declaration
    /// line, not the file, so a call anywhere else in it is still caught.</summary>
    private static string[] AllProductionKotlinSources()
    {
        string root = BnRepo.Root();
        string[] roots =
        [
            Path.Combine(root, "src", "BlazorNative.Jni", "src", "main", "kotlin"),
            Path.Combine(root, "src", "BlazorNative.Jni", "src", "androidMain", "kotlin"),
            Path.Combine(root, "templates", "BlazorNative.Templates", "content", "BlazorNative.App", "android", "src", "main", "kotlin"),
            Path.Combine(root, "templates", "BlazorNative.Templates", "content", "BlazorNative.App", "android", "src", "androidMain", "kotlin"),
        ];

        return [.. roots
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.kt", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}build{Path.DirectorySeparatorChar}", StringComparison.Ordinal))];
    }

    /// <summary>Every Swift file in `BnHost/` — the production Apple shell target.
    /// `BnHostTests/` is a SIBLING directory, not a subdirectory, so walking `BnHost/`
    /// alone already excludes the test tree without a separate filter.</summary>
    private static string[] BnHostProductionSwiftSources()
    {
        string dir = Path.Combine(BnRepo.Root(), "src", "BlazorNative.Apple", "BnHost");
        if (!Directory.Exists(dir))
            return [];

        return [.. Directory.EnumerateFiles(dir, "*.swift", SearchOption.AllDirectories)];
    }

    /// <summary>A MENTION of `dispatchHostEventAndWait` immediately followed by `(` — matches
    /// both a real call and the method's own declaration line, which is why
    /// <see cref="IsOffendingCallLine"/> exists.</summary>
    private static readonly Regex DispatchHostEventAndWaitMention = new(@"\bdispatchHostEventAndWait\s*\(");

    /// <summary>The method's own declaration, in either language — the one shape
    /// <see cref="DispatchHostEventAndWaitMention"/> must NOT be allowed to count as a
    /// call.</summary>
    private static readonly Regex DispatchHostEventAndWaitDeclaration =
        new(@"\b(?:internal\s+)?(?:fun|func)\s+dispatchHostEventAndWait\s*\(");

    /// <summary>A CALL to `dispatchHostEventAndWait` on this line. The method's own
    /// `fun`/`func` declaration TOKEN is removed first, and only then is the rest of the line
    /// searched for a mention followed by `(`. Removing the token, not skipping the line, is
    /// what keeps an expression-bodied forwarder written on the declaration line
    /// (`internal fun dispatchHostEventAndWait(e: BnHostEvent) = dispatchHostEventAndWait(e, null)`)
    /// visible as the call it is. Skipping the whole line hid it (16.6, defect 1).</summary>
    private static bool IsOffendingCallLine(string line) =>
        DispatchHostEventAndWaitMention.IsMatch(DispatchHostEventAndWaitDeclaration.Replace(line, "", 1));

    /// <summary>THE POSITIVE CONTROL (pin standard Rule 3), fed synthetic lines rather than a
    /// tree walk: proves the detector recognises a real call in BOTH languages, and does not
    /// mistake either language's declaration for one — the exact confusion that would leave
    /// the pin below blind to a call sitting right next to the method it must not reach.</summary>
    [Fact]
    public void OffendingCallDetector_MatchesACall_AndNotTheDeclaration()
    {
        Assert.True(IsOffendingCallLine("        runtime.dispatchHostEventAndWait(BnHostEvent.Back)"));
        Assert.True(IsOffendingCallLine("            self?.dispatchHostEventAndWait(.navigate, payload: route)"));
        Assert.True(IsOffendingCallLine("val rc = dispatchHostEventAndWait(event, payload)"));

        // Rule 4 (16.6, defect 2): the declarations are READ from the tree, not hand-copied,
        // so a change to either real signature is what this control checks.
        string root = BnRepo.Root();
        (string File, string Needle)[] declarations =
        [
            (Path.Combine(root, "src", "BlazorNative.Jni", "src", "main", "kotlin", "io", "blazornative",
                "jni", "BlazorNativeRuntime.kt"), "fun dispatchHostEventAndWait("),
            (Path.Combine(root, "src", "BlazorNative.Apple", "BnHost", "BnRuntime.swift"),
                "func dispatchHostEventAndWait("),
        ];
        foreach ((string file, string needle) in declarations)
        {
            string[] found = [.. CodeLines(file).Where(l => l.Contains(needle, StringComparison.Ordinal))];
            Assert.True(found.Length == 1,
                $"expected exactly one declaration line containing '{needle}' in {file}, found {found.Length}: "
                + "the control would otherwise check nothing, or check a copy.");
            Assert.False(IsOffendingCallLine(found[0]), $"the real declaration was counted as a call: {found[0]}");
        }

        // Defect 1 (16.6): a declaration line that ALSO calls the method — an expression-
        // bodied forwarder — is a call, not merely a declaration.
        Assert.True(IsOffendingCallLine(
            "    internal fun dispatchHostEventAndWait(event: BnHostEvent) = dispatchHostEventAndWait(event, null)"));
    }

    /// <summary>THE PIN (final whole-branch review, Important #1). No shipped shell source may
    /// call the blocking `dispatchHostEventAndWait` — Task 5 made it internal and test-only by
    /// owner decision, but `internal` alone enforces nothing a caller in the SAME
    /// module/target cannot ignore, and the review proved it: reverting `onNewIntent`'s call
    /// in the repo copy left the whole Runtime suite and the JVM suite green. This scan is the
    /// actual enforcement — the third of the three mechanisms this phase ends with, alongside
    /// the keyword and the manifest.
    ///
    /// <para>ANCHORS (Rule 2), NAMED rather than counted: both `MainActivity.kt` copies and
    /// `BnRuntime.swift` must be among the scanned files, because those three are exactly
    /// where the regression the review measured, and #346 before it, lived.</para>
    ///
    /// <para>DOES NOT COVER (Rule 5): `dispatchEventAndWait`, the Inspector's JVM-host-only
    /// blocking seam — a different, allowed method (`src/dispatch-surface.json` scopes it
    /// `platforms: ["kotlin"]` for exactly that reason) — which this fact does not touch at
    /// all, by name, not by tree exclusion; `src/jvmHost/kotlin`, which this scan's roots
    /// never include; a call reached through reflection or a stored function reference rather
    /// than a literal name; and Objective-C++, which cannot call a Swift member across the
    /// language boundary in the first place.</para></summary>
    [Fact]
    public void NoShippedShellSource_CallsTheBlockingHostEventDispatch()
    {
        string root = BnRepo.Root();
        string[] kotlinSources = AllProductionKotlinSources();
        string[] swiftSources = BnHostProductionSwiftSources();

        string repoMainActivity = Path.Combine(root, "src", "BlazorNative.Jni", "src", "androidMain",
            "kotlin", "io", "blazornative", "shell", "MainActivity.kt");
        string templateMainActivity = Path.Combine(root, "templates", "BlazorNative.Templates", "content",
            "BlazorNative.App", "android", "src", "androidMain", "kotlin", "io", "blazornative", "shell",
            "MainActivity.kt");
        string bnRuntimeSwift = Path.Combine(root, "src", "BlazorNative.Apple", "BnHost", "BnRuntime.swift");

        Assert.True(kotlinSources.Contains(repoMainActivity),
            $"the Kotlin scan did not see {repoMainActivity} — either the source-set layout moved "
            + "or the walk found nothing, and this pin would then be checking an empty set.");
        Assert.True(kotlinSources.Contains(templateMainActivity),
            $"the Kotlin scan did not see the template mirror {templateMainActivity} — the template "
            + "copy is exactly where the #346 shape could regress unnoticed if only the repo copy "
            + "were scanned.");
        Assert.True(swiftSources.Contains(bnRuntimeSwift),
            $"the Swift scan did not see {bnRuntimeSwift} — the method's own home file, and the "
            + "file the review's Swift-navigator mutation lives in.");

        var offenders = new List<string>();
        foreach (string file in kotlinSources.Concat(swiftSources))
        {
            string[] lines = CodeLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (IsOffendingCallLine(lines[i]))
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}: {lines[i].Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Shipped shell code calls dispatchHostEventAndWait, the blocking host-event dispatch "
            + "Phase 16.2 Task 5 made internal and test-only (#346). A caller blocked here waits "
            + "on .NET; the sanctioned production entry point is the fire-and-forget "
            + "dispatchHostEvent. See this fact's DOES NOT COVER note for dispatchEventAndWait, a "
            + "different method this scan does not touch.\n  " + string.Join("\n  ", offenders));
    }
}
