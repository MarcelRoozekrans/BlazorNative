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
// DEAD IS COMMON; DEAD-AND-SHADOWED IS THE DEFECT. Three generated symbols are
// unreferenced and harmless because nothing competes with them. A blunt "every
// generated symbol must be used" guard would red on those and force three
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

    /// <summary>Every symbol WireGen emits into a shell, by generated-file path.</summary>
    private static IEnumerable<(string File, string Symbol)> GeneratedSymbols()
    {
        string root = RepoRoot();
        string[] generated =
        [
            Path.Combine(root, "src", "BlazorNative.Apple", "BnHost", "BnWireVocabulary.g.swift"),
            Path.Combine(root, "src", "BlazorNative.Jni", "src", "main", "kotlin", "io", "blazornative", "jni", "BnWireVocabulary.g.kt"),
        ];

        foreach (string path in generated)
        {
            Assert.True(File.Exists(path), $"generated file missing: {path}");
            foreach (string line in File.ReadAllLines(path))
            {
                // Swift `static let NAME` / Kotlin `val NAME` / `@JvmField val NAME`.
                Match m = Regex.Match(line, @"\b(?:static\s+let|val)\s+([A-Za-z_][A-Za-z0-9_]*)\b");
                if (m.Success)
                    yield return (path, m.Groups[1].Value);
            }
        }
    }

    /// <summary>The shell source a generated file's consumers live in.</summary>
    private static string[] ShellSources(string generatedFile)
    {
        string root = RepoRoot();
        string dir = generatedFile.EndsWith(".swift", StringComparison.Ordinal)
            ? Path.Combine(root, "src", "BlazorNative.Apple")
            : Path.Combine(root, "src", "BlazorNative.Jni");

        return [.. Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".swift", StringComparison.Ordinal)
                     || f.EndsWith(".kt", StringComparison.Ordinal)
                     || f.EndsWith(".mm", StringComparison.Ordinal))
            .Where(f => !f.Contains(".g.", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}build{Path.DirectorySeparatorChar}", StringComparison.Ordinal))];
    }

    /// <summary>THE PIN. A hand-written declaration of a generated symbol's name
    /// shadows it: the generated value goes dead and the manifest stops governing
    /// that vocabulary, silently.
    ///
    /// BUT A DECLARATION IS NOT AUTOMATICALLY A SHADOW. Forwarding the generated
    /// value under the same name is the INTENDED consumption pattern this codebase
    /// already uses — `static let nodeTypes = BnWireVocabulary.nodeTypes` in
    /// BnFrameAdapter.swift, `private val YOGA_STYLES = BnWireVocabulary.YOGA_STYLES`
    /// in YogaLayout.kt — and it is also the shape Task 2's fix for #279 will take.
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
    /// forward as a shadow. The window has to reach past the line break.</summary>
    [Fact]
    public void NoGeneratedSymbol_IsShadowedByAHandWrittenDeclaration()
    {
        var offenders = new List<string>();

        foreach ((string file, string symbol) in GeneratedSymbols())
        {
            foreach (string source in ShellSources(file))
            {
                string[] lines = File.ReadAllLines(source);
                for (int i = 0; i < lines.Length; i++)
                {
                    // A DECLARATION of the same name — not a use of it.
                    if (!Regex.IsMatch(lines[i], $@"\b(?:static\s+let|let|val|var)\s+{Regex.Escape(symbol)}\b"))
                        continue;

                    // FORWARDING, not shadowing: scan the declaration line plus the two
                    // that follow for a qualified reference to the generated symbol.
                    bool forwards = false;
                    for (int j = i; j < Math.Min(i + 3, lines.Length); j++)
                    {
                        if (Regex.IsMatch(lines[j], $@"BnWireVocabulary\.{Regex.Escape(symbol)}\b"))
                        {
                            forwards = true;
                            break;
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
}
