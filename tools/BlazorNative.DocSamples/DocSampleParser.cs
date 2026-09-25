using System.Text.RegularExpressions;

namespace BlazorNative.DocSamples;

/// <summary>A fenced code block on a hand-written docs page. <paramref name="SkipReason"/>
/// (despite the name, kept for interface stability — Task 3 and CI already consume this
/// shape) carries the text after the marker's `:` for BOTH forms that use one: a `skip`'s
/// reason, and a `component`'s explicit name (`bn-sample=component:SettingsPage`) — the
/// latter used when another fence on the same page needs to reference the generated file
/// by name instead of by its page/index slug.</summary>
public sealed record Fence(int Index, int Line, string Language, string? Kind, string? SkipReason, string Body);

/// <summary>The ONE parser of docs code fences. The CI sample build and the .NET pins
/// both call it, so the two cannot disagree about what a fence is (pin standard Rule 8).</summary>
public static class DocSampleParser
{
    public static readonly IReadOnlySet<string> Kinds = new HashSet<string>(StringComparer.Ordinal)
        { "component", "file", "statements", "skip" };

    /// <summary>Controller ruling (2026-09-25, amending the original task brief): the
    /// classify-and-compile obligation applies ONLY to a fence written in one of these
    /// languages. Measured 2026-09-25 (fix round 1, after indented fences were found):
    /// 20 razor + 15 csharp/cs fences vs. 26 in bash, xml, swift, yaml, sh, powershell and
    /// diff — a shell transcript or a Kotlin/Swift excerpt is not a .NET compilation unit,
    /// and giving it a bn-sample marker would say nothing true. <see cref="Fences"/> still
    /// finds every fence of every language; the caller decides which ones the
    /// compiled-sample obligation binds.</summary>
    public static readonly IReadOnlySet<string> CompiledLanguages = new HashSet<string>(StringComparer.Ordinal)
        { "razor", "csharp", "cs" };

    /// <summary>CommonMark allows 0–3 leading spaces on a fence's opening (and closing)
    /// line — an indented fence inside a list item, exactly like the six `analyzers.md`
    /// "Compliant shape" samples fix round 1 found invisible to the un-indented anchor this
    /// used to be. The captured indent is stripped from every body line in <see
    /// cref="Fences"/> so the emitted sample is not itself mis-indented C#/Razor.
    ///
    /// NOT HANDLED (Rule 5 — see DocsSamplesDriftTests' header): `~~~`-fenced blocks,
    /// four-or-more-backtick fences, and MDX's `&lt;CodeBlock&gt;` component. None exist on
    /// a hand-written page today; a page that starts using one is invisible to this parser
    /// exactly the way an unlabeled fence is.
    ///
    /// Close REQUIRES an end anchor (fix round 3): CommonMark's closing fence is 0–3
    /// leading spaces, three-or-more backticks, THEN ONLY WHITESPACE to the end of the
    /// line. Without `[ \t]*$`, a body line that merely STARTS with "```csharp" (a fence
    /// shown INSIDE another fence, to document the marker syntax itself) would close the
    /// fence early — exactly the case TheDetectors_SeeAPlantedDefect now plants.</summary>
    private static readonly Regex Open = new(@"^(?<indent> {0,3})```(?<lang>[A-Za-z0-9_+-]*)(?<meta>[^\r\n]*)$", RegexOptions.CultureInvariant);
    private static readonly Regex Close = new(@"^ {0,3}`{3,}[ \t]*$", RegexOptions.CultureInvariant);
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
            int indent = open.Groups["indent"].Length;
            var body = new List<string>();
            for (i++; i < lines.Length && !Close.IsMatch(lines[i]); i++) body.Add(Unindent(lines[i], indent));
            (string? kind, string? reason) = ParseMarker(open.Groups["meta"].Value);
            fences.Add(new Fence(fences.Count + 1, start + 1, open.Groups["lang"].Value, kind, reason, string.Join('\n', body)));
        }
        return fences;
    }

    /// <summary>Strips up to <paramref name="indent"/> leading space characters — never
    /// more than the line actually has, so a body line indented less than its fence's
    /// opener (legal Markdown; rare in practice) loses only what is really there.</summary>
    private static string Unindent(string line, int indent)
    {
        int i = 0;
        while (i < indent && i < line.Length && line[i] == ' ') i++;
        return line[i..];
    }

    internal static (string? Kind, string? Reason) ParseMarker(string meta)
    {
        Match m = Marker.Match(meta);
        if (!m.Success) return (null, null);
        string? reason = m.Groups["reason"].Success ? m.Groups["reason"].Value.Trim() : null;
        return (m.Groups["kind"].Value, reason);
    }
}
