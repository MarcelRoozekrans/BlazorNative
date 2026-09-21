namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// CommentStrippedSource — Phase 14.1 fix round 1, Important #1.
//
// A SINGLE truth for "strip Kotlin/Swift line and block comments", extracted
// because it had already drifted once inside a pin that exists to catch
// exactly that shape of drift. GeneratedSymbolShadowTests.CodeLines carried
// the correct logic (checking whether a same-line block comment's closing
// `*/` was already present before deciding to stay in block-comment mode);
// DispatchSurfaceDriftTests.CodeText was modelled on it but dropped that
// check, so a single-line KDoc like:
//
//     /** Caller-allocated NUL-terminated UTF-8 cstring for input pointers. */
//
// set inBlock = true and swallowed everything after it until another literal
// `*/` turned up — silently, in a pin whose entire job is to notice silent
// divergence between two copies of one truth. Both call sites now share this
// one implementation instead of maintaining separate, driftable copies.
// ─────────────────────────────────────────────────────────────────────────────

internal static class CommentStrippedSource
{
    /// <summary>Every line of <paramref name="file"/> with `//` and `/* … */` comment
    /// text removed, one entry per source line so line numbers and any forwarding-window
    /// scan still line up. A comment-only line becomes an empty string, not a dropped
    /// entry — callers that need line indices to survive (GeneratedSymbolShadowTests)
    /// rely on that; callers that only want code text (DispatchSurfaceDriftTests) filter
    /// blanks themselves.</summary>
    public static string[] Lines(string file)
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

            // A block comment that OPENS on this line may also CLOSE on this line
            // (`/** … */`, both delimiters present) — check for that closing `*/`
            // before deciding the rest of the file is inside the comment. Missing
            // this check is exactly the bug this type was extracted to fix once,
            // not twice.
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
}
