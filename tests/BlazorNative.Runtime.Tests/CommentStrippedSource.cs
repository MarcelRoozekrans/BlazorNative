using System.Text;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// CommentStrippedSource — THE single "remove comments before scanning" for every
// pin in this repo. Swift, Kotlin and C# share both comment forms, so one
// implementation serves all eight callers: GeneratedSymbolShadowTests,
// DispatchSurfaceDriftTests, AuthSemanticsDriftTests, PinPopulationTests,
// NSLogDriftTests, ConsoleErrorDriftTests, AndroidLogDriftTests and
// DeepLinkSeedDriftTests.
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
    /// REMAINING BOUNDED LIMITS, not claims. A `"""` multiline or raw string toggles three
    /// times and lands inside-string; a C# `'"'` char literal toggles once. Only the
    /// newline reset clears either. The reset is the point — it confines any mis-parse to
    /// a single line rather than letting one stray quote blind the rest of the file.</summary>
    public static string Strip(string source)
    {
        var sb = new StringBuilder(source.Length);
        int i = 0;
        bool inString = false;

        while (i < source.Length)
        {
            // STRING LITERALS (14.4's F2, and the reason this type absorbed the
            // AuthSemantics copy in 15.0). A `//` inside a string is not a comment —
            // the shells already contain `://` in literals, at BnDeepLink.swift and
            // BnCamera.swift. Tracking is deliberately simple: toggle on an unescaped
            // `"`, and RESET AT EVERY NEWLINE. No ordinary string in Swift, Kotlin or
            // C# spans lines, and the reset is what BOUNDS a mis-parse to one line
            // rather than letting one stray quote blind the rest of the file.
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
