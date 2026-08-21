using System.Text.RegularExpressions;

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
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BlazorNative.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>How many symbols the three generated shell files hold TODAY (5 Swift,
    /// 5 Kotlin, 3 C). The floor below is measured, not guessed; if the manifest
    /// legitimately loses a name, lower it in the same commit.</summary>
    private const int GeneratedSymbolFloor = 13;

    /// <summary>Swift `static let NAME` / Kotlin `val NAME` / `@JvmField val NAME`.</summary>
    private const string SwiftKotlinDeclaration = @"\b(?:static\s+let|val)\s+([A-Za-z_][A-Za-z0-9_]*)\b";

    /// <summary>C `static const char* const NAME[] = {`. Empty brackets are what makes
    /// this a DEFINITION rather than a use — every use in the shell indexes with
    /// something (`kYogaStyles[i]`, `sizeof(kYogaStyles[0])`).</summary>
    private const string CArrayDeclaration = @"\b([A-Za-z_][A-Za-z0-9_]*)\s*\[\s*\]\s*=\s*\{";

    /// <summary>Every symbol WireGen emits into a shell, by generated-file path.
    ///
    /// <para>MATERIALISED, AND FLOORED, DELIBERATELY. Both pins below iterate this
    /// one helper, so a parse that silently stopped matching would green BOTH of
    /// them over an empty set — the exact silent-degradation shape this phase
    /// exists to remove, sitting inside its own flagship guard. `File.Exists`
    /// guards the file MOVING; only a count guards the parse FAILING.</para></summary>
    private static IReadOnlyList<(string File, string Symbol)> GeneratedSymbols()
    {
        string root = RepoRoot();
        (string Path, string Pattern)[] generated =
        [
            (Path.Combine(root, "src", "BlazorNative.Apple", "BnHost", "BnWireVocabulary.g.swift"), SwiftKotlinDeclaration),
            (Path.Combine(root, "src", "BlazorNative.Jni", "src", "main", "kotlin", "io", "blazornative", "jni", "BnWireVocabulary.g.kt"), SwiftKotlinDeclaration),
            (Path.Combine(root, "src", "BlazorNative.Apple", "BnHost", "BnWireVocabulary.g.h"), CArrayDeclaration),
        ];

        var symbols = new List<(string File, string Symbol)>();
        foreach ((string path, string pattern) in generated)
        {
            Assert.True(File.Exists(path), $"generated file missing: {path}");
            foreach (string line in File.ReadAllLines(path))
            {
                Match m = Regex.Match(line, pattern);
                if (m.Success)
                    symbols.Add((path, m.Groups[1].Value));
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
        string root = RepoRoot();
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
    /// <para>The same shape as <c>ConsoleErrorDriftTests.CodeLines</c> and
    /// <c>NSLogDriftTests.CodeLines</c>, and it exists here for the mirror-image
    /// reason: those scans must not count PROSE as an offence, this one must not
    /// count prose as an EXEMPTION. The forwarding window is three lines of source;
    /// scanned raw, a shadowing declaration followed within two lines by a comment
    /// that merely MENTIONS `BnWireVocabulary.&lt;symbol&gt;` would be waved through —
    /// the exemption-too-broad direction, which leaves the pin green while the class
    /// stays open. `///` and `//!` are covered by the `//` rule.</para></summary>
    private static string[] CodeLines(string file)
    {
        var code = new List<string>();
        bool inBlockComment = false;

        foreach (string raw in File.ReadLines(file))
        {
            string line = raw;

            if (inBlockComment)
            {
                int close = line.IndexOf("*/", StringComparison.Ordinal);
                if (close < 0) { code.Add(string.Empty); continue; }
                inBlockComment = false;
                line = line[(close + 2)..];
            }

            int open = line.IndexOf("/*", StringComparison.Ordinal);
            if (open >= 0)
            {
                inBlockComment = line.IndexOf("*/", open, StringComparison.Ordinal) < 0;
                line = line[..open];
            }

            int slashes = line.IndexOf("//", StringComparison.Ordinal);
            if (slashes >= 0) line = line[..slashes];

            code.Add(line);
        }

        return [.. code];
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
    /// DEFINITION: `NAME[] = {`, its own literal, a second copy of the truth.</summary>
    [Fact]
    public void NoGeneratedSymbol_IsShadowedByAHandWrittenDeclaration()
    {
        var offenders = new List<string>();

        foreach ((string file, string symbol) in GeneratedSymbols())
        {
            bool c = IsCHeader(file);

            // A DECLARATION of the same name — not a use of it. In C, specifically a
            // definition with its own initializer; `kYogaStyles[i]` and
            // `sizeof(kYogaStyles[0])` index with something and do not match.
            string declaration = c
                ? $@"\b{Regex.Escape(symbol)}\s*\[\s*\]\s*=\s*\{{"
                : $@"\b(?:static\s+let|let|val|var)\s+{Regex.Escape(symbol)}\b";

            foreach (string source in ShellSources(file))
            {
                string[] lines = CodeLines(source);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!Regex.IsMatch(lines[i], declaration))
                        continue;

                    // FORWARDING, not shadowing: scan the declaration line plus the two
                    // that follow for a qualified reference to the generated symbol.
                    // (C definitions never forward — see the note above — so the window
                    // can never exempt one.)
                    bool forwards = false;
                    if (!c)
                    {
                        for (int j = i; j < Math.Min(i + 3, lines.Length); j++)
                        {
                            if (Regex.IsMatch(lines[j], $@"BnWireVocabulary\.{Regex.Escape(symbol)}\b"))
                            {
                                forwards = true;
                                break;
                            }
                        }
                    }
                    if (forwards)
                        continue;

                    offenders.Add($"{Path.GetFileName(source)}:{i + 1} declares '{symbol}', which WireGen generates");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "A generated symbol is shadowed by a hand-written declaration. The generated value goes "
            + "dead, the hand-written copy wins every use, and src/wire-vocabulary.json silently stops "
            + "governing that vocabulary — while the codegen tests stay green, because they compare "
            + "generated files to the manifest and never look at a hand-written twin. Consume the "
            + "generated symbol instead.\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>Generated symbols that nothing consumes, each with a written reason.
    ///
    /// <para>Being on this list is not an accusation — a generated symbol with no
    /// consumer and no hand-written twin is harmless. It is here so that ADDING one
    /// is a decision somebody wrote down, rather than a file quietly growing a dead
    /// symbol that a future hand-written twin can then shadow. That progression is
    /// exactly how #279 happened.</para></summary>
    private static readonly Dictionary<string, string> UnconsumedByDesign = new(StringComparer.Ordinal)
    {
        ["visualStyles"] =
            "Swift has no visual-style routing of its own — BnWidgetMapper switches on style names "
            + "directly. Emitted for symmetry with Kotlin and byte-pinned by the codegen tests.",
        ["scrollIgnoredContainerStyles"] =
            "Same: the Swift scroll path checks the names inline. Emitted for symmetry, byte-pinned.",
        ["VISUAL_STYLES"] =
            "Kotlin's WidgetMapper.kt switches on style-name literals directly (\"backgroundColor\" ->, "
            + "\"color\" ->, \"fontSize\" ->) rather than checking membership in this set — the same "
            + "pattern as Swift's visualStyles. Verified no hand-written twin exists (not a #279 shadow). "
            + "Emitted for symmetry, byte-pinned by the codegen tests.",
        ["kNodeTypes"] =
            "The Objective-C++ layer is a YOGA seam and nothing else: BnYogaLayout.mm and BnYogaProbe.mm "
            + "answer style questions (bn_yoga_is_layout_style, bn_yoga_is_scroll_ignored_container_style) "
            + "and never see a node type — the wire byte is decoded and routed entirely in Swift, where "
            + "BnFrameAdapter forwards BnWireVocabulary.nodeTypes. So the header's other two arrays are "
            + "consumed and this one is emitted for symmetry with them, byte-pinned by the codegen tests. "
            + "Verified no hand-written twin exists in the Apple tree (not a #279 shadow): `kNodeTypes` "
            + "occurs exactly once, in its own generated declaration.",
    };

    /// <summary>Advisory pin: a generated symbol is consumed, or it is on the list above
    /// with a reason. Catches the state that PRECEDES a shadow — a dead generated symbol
    /// is what a hand-written twin later shadows without anyone noticing.
    ///
    /// <para>Consumption is read from CODE, not raw text: a symbol named only in a
    /// comment is not a consumer, and this file's own shells discuss these names at
    /// length (`BnYogaLayout.h` explains both C arrays in prose).</para></summary>
    [Fact]
    public void EveryGeneratedSymbol_IsConsumed_OrAllowlistedWithAReason()
    {
        var dead = new List<string>();

        foreach ((string file, string symbol) in GeneratedSymbols())
        {
            if (UnconsumedByDesign.ContainsKey(symbol))
                continue;

            // Swift/Kotlin name the symbol through its enum/object; C `#include`s the
            // header and names it bare.
            string reference = IsCHeader(file)
                ? $@"\b{Regex.Escape(symbol)}\b"
                : $@"BnWireVocabulary\.{Regex.Escape(symbol)}\b";

            bool referenced = ShellSources(file)
                .Any(src => CodeLines(src).Any(line => Regex.IsMatch(line, reference)));

            if (!referenced)
                dead.Add($"{symbol} (generated into {Path.GetFileName(file)})");
        }

        Assert.True(dead.Count == 0,
            "A generated symbol has no consumer and no written reason. It is harmless TODAY — but a "
            + "dead generated symbol is what a hand-written twin later shadows, which is how #279 "
            + "happened. Either consume it, or add it to UnconsumedByDesign with a reason.\n  "
            + string.Join("\n  ", dead));
    }
}
