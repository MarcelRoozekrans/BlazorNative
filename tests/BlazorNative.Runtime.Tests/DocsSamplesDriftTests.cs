using System.Text.RegularExpressions;
using BlazorNative.DocSamples;
using BlazorNative.Tests.Shared;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// DocsSamplesDriftTests — every code fence on a hand-written docs page, one
// known kind (#291).
//
// A docs page can show C#/Razor that used to compile, or never did: nothing
// short of actually compiling it says otherwise, and nothing was compiling it.
// tools/BlazorNative.DocSamples is the ONE parser (DocSampleParser) and the
// generator that turns every fence marked component/file/statements into a
// buildable project; the CI "Docs samples compile" step builds it. This pin
// is the OTHER half: it holds every fence on a hand-written page to declaring
// a known kind at all, so a fence cannot dodge the compile step by omission —
// a fence with no bn-sample= marker reds here, not silently drops out of the
// generator's count.
//
// Everything here is DERIVED, never restated:
//   · the page set comes from DocSampleParser.HandWrittenPages, itself derived
//     from website/.gitignore's docs/ entries — never a hard-coded list;
//   · every fence comes from DocSampleParser.Fences, the same parser the
//     generator calls, so the pin and the build cannot disagree about what a
//     fence is (pin standard Rule 8);
//   · the compiled-sample floor (Step 7) is a measured count, not a guess.
//
// CONTROLLER RULING (2026-09-25, amending the original task brief): the
// classify-and-compile obligation binds ONLY a fence whose language is in
// DocSampleParser.CompiledLanguages (razor, csharp, cs). Measured: 14 razor +
// 14 csharp/cs fences vs. 26 in bash, xml, swift, yaml, sh, powershell and
// diff — AllFences below filters to that set before anything else runs.
//
// WHAT THIS DOES NOT COVER (Rule 5):
//   - Whether a sample MEANS what the prose around it says. A fence can
//     compile and still illustrate the wrong thing; nothing here reads prose.
//   - `skip` samples are not compiled here or anywhere — they are held only
//     to having a reason after `skip:`, never to compiling.
//   - The build itself. This suite never invokes the compiler; the CI "Docs
//     samples compile" step does that, using the SAME parser and generator.
//     A fence can pass every fact below and still fail that step if its
//     content, once genuinely compiled, is wrong — that failure is real and
//     is exactly what the CI step is for.
//   - A C# or Razor sample labeled with another language, or left unlabeled
//     entirely. DocSampleParser.Fences requires an info-string language to
//     recognise a fence at all (`open.Groups["lang"].Value.Length == 0`
//     short-circuits it), so a ```text block holding real C#, or a bare
//     ``` with no language, is invisible to this whole mechanism — not
//     classified, not compiled, not reported missing. Nothing here reads a
//     fence's CONTENT to guess what language it actually is.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class DocsSamplesDriftTests
{
    /// <summary>Measured 2026-09-25: 20 hand-written pages, 28 razor/csharp fences
    /// (14 + 14) out of 54 fences total on those pages — the other 26 are bash, xml,
    /// swift, yaml, sh, powershell and diff, outside DocSampleParser.CompiledLanguages.
    /// Floors, not counts — adding a page or a sample passes; losing the scan does
    /// not.</summary>
    private const int MinimumPages = 20;
    private const int MinimumFences = 28;
    /// <summary>Measured 2026-09-25: 20 of the 28 compiled-language fences are
    /// component/file/statements; the other 9 are skip (a signature listing, three
    /// render-and-throw counterexamples, and five fences that are, by nature, not a
    /// standalone unit — see docs/plans/2026-09-25-phase-15.5-docs-audit.md).</summary>
    private const int MinimumCompiledSamples = 20;

    private const string MigratingDir = "website/docs/migrating/";
    private static readonly Regex Version = new(@"(?<![\w.])\d+\.\d+\.\d+(?![\w.])", RegexOptions.CultureInvariant);

    internal static IEnumerable<(string Page, Fence Fence)> AllFences(string root) =>
        DocSampleParser.HandWrittenPages(root).SelectMany(p =>
            DocSampleParser.Fences(File.ReadAllText(Path.Combine(root, p)))
                .Where(f => DocSampleParser.CompiledLanguages.Contains(f.Language))
                .Select(f => (p, f)));

    internal static List<string> Unclassified(IEnumerable<(string Page, Fence Fence)> fences) =>
        [.. fences.Where(x => x.Fence.Kind is null || !DocSampleParser.Kinds.Contains(x.Fence.Kind))
                  .Select(x => $"{x.Page}:{x.Fence.Line} fence #{x.Fence.Index} ({x.Fence.Language}) has kind '{x.Fence.Kind ?? "<none>"}'")];

    internal static List<string> VersionMentions(string page, string markdown) =>
        page.StartsWith(MigratingDir, StringComparison.Ordinal) ? [] :
        [.. markdown.Replace("\r\n", "\n").Split('\n').Select((l, i) => (l, i))
            .Where(x => Version.IsMatch(x.l)).Select(x => $"{page}:{x.i + 1}: {Version.Match(x.l).Value}")];

    [Fact]
    public void EveryFenceOnAHandWrittenPage_DeclaresAKnownKind()
    {
        string root = BnRepo.Root();
        IReadOnlyList<string> pages = DocSampleParser.HandWrittenPages(root);
        Assert.True(pages.Count >= MinimumPages, $"found {pages.Count} hand-written pages, fewer than the measured {MinimumPages} — the page scan has stopped seeing the site (Rule 2).");
        var fences = AllFences(root).ToList();
        Assert.True(fences.Count >= MinimumFences, $"found {fences.Count} code fences, fewer than the measured {MinimumFences} (Rule 2).");
        List<string> bad = Unclassified(fences);
        Assert.True(bad.Count == 0, "every docs code fence must declare bn-sample=component|file|statements|skip:<reason> so it is compiled or explicitly excused: " + string.Join(" | ", bad));
    }

    [Fact]
    public void EverySkippedSample_SaysWhy()
    {
        var bare = AllFences(BnRepo.Root()).Where(x => x.Fence.Kind == "skip" && string.IsNullOrWhiteSpace(x.Fence.SkipReason))
            .Select(x => $"{x.Page}:{x.Fence.Line}").ToList();
        Assert.True(bare.Count == 0, "a skipped sample needs a reason after 'skip:' — an unexplained skip is how samples stop being compiled: " + string.Join(", ", bare));
    }

    [Fact]
    public void TheCompiledSampleCount_MeetsItsFloor()
    {
        int compiled = AllFences(BnRepo.Root()).Count(x => x.Fence.Kind is "component" or "file" or "statements");
        Assert.True(compiled >= MinimumCompiledSamples, $"{compiled} samples are compiled, fewer than the measured {MinimumCompiledSamples} — samples are drifting into skip.");
    }

    [Fact]
    public void NoNarrativePage_NamesAVersion()
    {
        string root = BnRepo.Root();
        var hits = DocSampleParser.HandWrittenPages(root)
            .SelectMany(p => VersionMentions(p, File.ReadAllText(Path.Combine(root, p)))).ToList();
        Assert.True(hits.Count == 0, "narrative pages name no version — it is stale the day a release ships (CONTRIBUTING.md). migrating/* is exempt by name: " + string.Join(" | ", hits));
    }

    [Fact]
    public void TheDetectors_SeeAPlantedDefect()
    {
        const string planted = "text\n```razor\n<BnView />\n```\nsee 0.12.0\n";
        Assert.Single(Unclassified(DocSampleParser.Fences(planted)
            .Where(f => DocSampleParser.CompiledLanguages.Contains(f.Language))
            .Select(f => ("website/docs/planted.md", f))));
        Assert.Single(VersionMentions("website/docs/planted.md", planted));
        Assert.Empty(VersionMentions(MigratingDir + "planted.md", planted));

        // The CompiledLanguages filter itself: an unmarked bash fence is a fence
        // (DocSampleParser.Fences sees it) but never a compiled-language one, so it
        // must never be COUNTED as unclassified — it is simply out of scope, the same
        // way the other 26 non-razor/csharp fences on real pages are.
        const string plantedBash = "text\n```bash\necho hi\n```\n";
        var bashFences = DocSampleParser.Fences(plantedBash);
        Assert.Single(bashFences); // the splice landed, or this control proves nothing
        var compiledLanguageBashFences = bashFences
            .Where(f => DocSampleParser.CompiledLanguages.Contains(f.Language))
            .Select(f => ("website/docs/planted.md", f));
        Assert.Empty(compiledLanguageBashFences);
        Assert.Empty(Unclassified(compiledLanguageBashFences));
    }
}
