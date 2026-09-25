using System.Text.RegularExpressions;

namespace BlazorNative.DocSamples;

/// <summary>A fenced code block on a hand-written docs page.</summary>
public sealed record Fence(int Index, int Line, string Language, string? Kind, string? SkipReason, string Body);

/// <summary>The ONE parser of docs code fences. The CI sample build and the .NET pins
/// both call it, so the two cannot disagree about what a fence is (pin standard Rule 8).</summary>
public static class DocSampleParser
{
    public static readonly IReadOnlySet<string> Kinds = new HashSet<string>(StringComparer.Ordinal)
        { "component", "file", "statements", "skip" };

    /// <summary>Controller ruling (2026-09-25, amending the original task brief): the
    /// classify-and-compile obligation applies ONLY to a fence written in one of these
    /// languages. Measured: 14 razor + 14 csharp/cs fences vs. 26 in bash, xml, swift,
    /// yaml, sh, powershell and diff — a shell transcript or a Kotlin/Swift excerpt is
    /// not a .NET compilation unit, and giving it a bn-sample marker would say nothing
    /// true. <see cref="Fences"/> still finds every fence of every language; the caller
    /// decides which ones the compiled-sample obligation binds.</summary>
    public static readonly IReadOnlySet<string> CompiledLanguages = new HashSet<string>(StringComparer.Ordinal)
        { "razor", "csharp", "cs" };

    private static readonly Regex Open = new(@"^```(?<lang>[A-Za-z0-9_+-]*)(?<meta>[^\r\n]*)$", RegexOptions.CultureInvariant);
    private static readonly Regex Marker = new(@"(?:^|\s)bn-sample=(?<kind>[a-z]+)(?::(?<reason>[^\s].*))?\s*$", RegexOptions.CultureInvariant);

    /// <summary>Every *.md / *.mdx under website/docs except the directories
    /// website/.gitignore lists under docs/ — the generated reference. Derived from
    /// that file, never hard-coded, so a new generated tree cannot be audited as prose
    /// and a new hand-written tree cannot be skipped.</summary>
    public static IReadOnlyList<string> HandWrittenPages(string repoRoot)
    {
        string website = Path.Combine(repoRoot, "website");
        string[] generated = [.. File.ReadAllLines(Path.Combine(website, ".gitignore"))
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("docs/", StringComparison.Ordinal))
            .Select(l => l.TrimEnd('/') + "/")];
        if (generated.Length == 0)
            throw new InvalidOperationException("website/.gitignore lists no docs/ directory — the generated reference is no longer excluded, so the page set is wrong.");

        string docs = Path.Combine(website, "docs");
        return [.. Directory.EnumerateFiles(docs, "*.*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".md", StringComparison.Ordinal) || p.EndsWith(".mdx", StringComparison.Ordinal))
            .Select(p => Path.GetRelativePath(website, p).Replace('\\', '/'))
            .Where(rel => !generated.Any(g => rel.StartsWith(g, StringComparison.Ordinal)))
            .Select(rel => "website/" + rel)
            .OrderBy(p => p, StringComparer.Ordinal)];
    }

    public static IReadOnlyList<Fence> Fences(string markdown)
    {
        var fences = new List<Fence>();
        string[] lines = markdown.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            Match open = Open.Match(lines[i]);
            if (!open.Success || open.Groups["lang"].Value.Length == 0) continue;
            int start = i;
            var body = new List<string>();
            for (i++; i < lines.Length && !lines[i].StartsWith("```", StringComparison.Ordinal); i++) body.Add(lines[i]);
            (string? kind, string? reason) = ParseMarker(open.Groups["meta"].Value);
            fences.Add(new Fence(fences.Count + 1, start + 1, open.Groups["lang"].Value, kind, reason, string.Join('\n', body)));
        }
        return fences;
    }

    internal static (string? Kind, string? Reason) ParseMarker(string meta)
    {
        Match m = Marker.Match(meta);
        if (!m.Success) return (null, null);
        string? reason = m.Groups["reason"].Success ? m.Groups["reason"].Value.Trim() : null;
        return (m.Groups["kind"].Value, reason);
    }
}
