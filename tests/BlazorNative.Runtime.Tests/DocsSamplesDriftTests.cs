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
// DocSampleParser.CompiledLanguages (razor, csharp, cs). AllFences below (and
// the positive control, through the same InScope helper) filters to that set
// before anything else runs — ONE filter, so deleting it reds the control
// instead of leaving it checking nothing (fix round 1: a duplicated filter in
// the control used to mean the control tested only itself).
//
// FIX ROUND 1 (2026-09-25): the original `Open` anchor required a fence's
// opening backticks at column 0. CommonMark allows 0–3 leading spaces, and six
// `analyzers.md` "Compliant shape" samples — indented two spaces inside a list
// item — were invisible to this whole pin: the generator silently compiled
// nothing for them and EveryFenceOnAHandWrittenPage_DeclaresAKnownKind stayed
// green over six unclassified fences. DocSampleParser.Fences now accepts and
// strips that indent; TheDetectors_SeeAPlantedDefect plants an indented fence
// to keep this from regressing invisibly a second time.
//
// bn-sample=component:<Name> (fix round 1) lets one fence reference another's
// generated file by name — `<Name>.razor` instead of the page/index slug —
// for the rare case where a SECOND fence on the page needs to call into the
// FIRST one's generated component (testing-harness.md's SettingsPage). The
// name must be a valid C# identifier and globally unique (two pages could
// otherwise collide in one Samples/ directory); NamedComponents_…below is the
// fact and its planted controls are in TheDetectors_SeeAPlantedDefect.
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
//   - `~~~`-fenced code blocks, four-or-more-backtick fences, and MDX's
//     `<CodeBlock>` component. DocSampleParser.Fences only recognises a
//     triple-backtick fence (with 0–3 leading spaces); none of these three
//     forms exist on a hand-written page today, and a page that starts using
//     one is invisible here exactly the way an unlabeled fence is.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class DocsSamplesDriftTests
{
    /// <summary>Re-measured 2026-09-25 (Task 4 fix round 1): 20 hand-written pages,
    /// 36 razor/csharp fences (16 + 20) out of 63 fences total on those pages — the
    /// other 27 are bash, xml, swift, yaml, sh, powershell and diff, outside
    /// DocSampleParser.CompiledLanguages. History: 28 before the parser saw
    /// 0–3-space-indented fences, 35 once it did (six indented `analyzers.md`
    /// samples), 36 after the Task 4 audit removed one sample and added two. Floors,
    /// not counts — adding a page or a sample passes; losing the scan does not.</summary>
    private const int MinimumPages = 20;
    private const int MinimumFences = 36;
    /// <summary>Re-measured 2026-09-25 (Task 4 fix round 1): 33 of the 36
    /// compiled-language fences are component/file/statements; the other 3 are skip —
    /// the signature listing (testing-harness.md) and the two genuine render-and-throw
    /// ✗ counterexamples (layout-and-yoga.md, typed-lengths.md). History: 31/4 after
    /// Task 2's fix round 1; 32/3 after fix round 2 made state.md's abridged consumer
    /// fence the real definition (bn-sample=component:BnThemedPanel); 33/3 after the
    /// Task 4 audit, which removed a redundant rest-backends.md sample and added the
    /// scroll and testing samples.</summary>
    private const int MinimumCompiledSamples = 33;
    /// <summary>Fix round 2: at least one bn-sample=component:<Name> must exist — a
    /// floor, not a count, so the fact this file adds for it
    /// (NoNamedSampleComponent_CollidesWithAShippedType) is provably scanning something.
    /// Measured 2026-09-25: two, SettingsPage (migrating/testing-harness.md) and
    /// BnThemedPanel (guides/state.md).</summary>
    private const int MinimumNamedComponents = 1;

    private const string MigratingDir = "website/docs/migrating/";
    /// <summary>An `X.Y.Z` version, with an optional leading `v` that is part of the
    /// match — `0.12.0` and `v0.12.0` both hit. The lookarounds keep it from matching
    /// inside a longer token (`abc1.2.3`, `xv1.2.3`, `1.2.3.4`) or an IP address, and a
    /// two-part name such as `.NET 10` never has three numeric parts.</summary>
    private static readonly Regex Version = new(@"(?<![\w.])v?\d+\.\d+\.\d+(?![\w.])", RegexOptions.CultureInvariant);
    private static readonly Regex ComponentName = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    /// <summary>One verified-public anchor type per shipped package DocSamples.csproj
    /// references — <see cref="Type.Assembly"/> off each reaches every OTHER public type
    /// in that assembly, so the collision set is measured, not hand-copied from a
    /// reference page that could itself drift.</summary>
    private static readonly Type[] ShippedAssemblyAnchors =
    [
        typeof(BlazorNative.Components.BnView),
        typeof(BlazorNative.Core.BnLog),
        typeof(BlazorNative.Device.ICamera),
        typeof(BlazorNative.Http.BridgeHttpHandler),
        typeof(BlazorNative.Renderer.NativeRenderer),
        typeof(BlazorNative.Runtime.BlazorNativeApp),
        typeof(BlazorNative.Testing.BnTestHost),
    ];

    internal static HashSet<string> ShippedTypeNames() =>
        [.. ShippedAssemblyAnchors.Select(t => t.Assembly).Distinct()
            .SelectMany(a => a.GetTypes()).Where(t => t.IsPublic).Select(t => t.Name)];

    /// <summary>The ONE place the CompiledLanguages filter is applied — AllFences and the
    /// positive control (TheDetectors_SeeAPlantedDefect) both call this, so deleting the
    /// filter reds the control instead of leaving a second copy that tests nothing (fix
    /// round 1: the control used to re-implement this inline).</summary>
    internal static IEnumerable<(string Page, Fence Fence)> InScope(IEnumerable<(string Page, Fence Fence)> fences) =>
        fences.Where(x => DocSampleParser.CompiledLanguages.Contains(x.Fence.Language));

    internal static IEnumerable<(string Page, Fence Fence)> AllFences(string root) =>
        InScope(DocSampleParser.HandWrittenPages(root).SelectMany(p =>
            DocSampleParser.Fences(File.ReadAllText(Path.Combine(root, p))).Select(f => (p, f))));

    internal static List<string> Unclassified(IEnumerable<(string Page, Fence Fence)> fences) =>
        [.. fences.Where(x => x.Fence.Kind is null || !DocSampleParser.Kinds.Contains(x.Fence.Kind))
                  .Select(x => $"{x.Page}:{x.Fence.Line} fence #{x.Fence.Index} ({x.Fence.Language}) has kind '{x.Fence.Kind ?? "<none>"}'")];

    internal static List<string> VersionMentions(string page, string markdown) =>
        page.StartsWith(MigratingDir, StringComparison.Ordinal) ? [] :
        [.. markdown.Replace("\r\n", "\n").Split('\n').Select((l, i) => (l, i))
            .Where(x => Version.IsMatch(x.l)).Select(x => $"{page}:{x.i + 1}: {Version.Match(x.l).Value}")];

    /// <summary>Named fences only (`bn-sample=component:<Name>`) — the plain `component`
    /// form (SkipReason null) has no name to validate.</summary>
    private static IEnumerable<(string Page, Fence Fence)> NamedComponentFences(IEnumerable<(string Page, Fence Fence)> fences) =>
        fences.Where(x => x.Fence.Kind == "component" && x.Fence.SkipReason is not null);

    internal static List<string> BadComponentNames(IEnumerable<(string Page, Fence Fence)> fences) =>
        [.. NamedComponentFences(fences).Where(x => !ComponentName.IsMatch(x.Fence.SkipReason!))
                  .Select(x => $"{x.Page}:{x.Fence.Line} bn-sample=component:{x.Fence.SkipReason} is not a valid C# identifier")];

    internal static List<string> DuplicateComponentNames(IEnumerable<(string Page, Fence Fence)> fences) =>
        [.. NamedComponentFences(fences).GroupBy(x => x.Fence.SkipReason, StringComparer.Ordinal)
                  .Where(g => g.Count() > 1)
                  .Select(g => $"bn-sample=component:{g.Key} claimed by " + string.Join(" and ", g.Select(x => $"{x.Page}:{x.Fence.Line}")))];

    /// <summary>A named sample sharing a simple name with a public shipped type would
    /// shadow it or resolve ambiguously wherever both are in scope — never a name a
    /// docs author should be free to pick.</summary>
    internal static List<string> ShippedTypeCollisions(IEnumerable<(string Page, Fence Fence)> fences)
    {
        HashSet<string> shipped = ShippedTypeNames();
        return [.. NamedComponentFences(fences).Where(x => shipped.Contains(x.Fence.SkipReason!))
                  .Select(x => $"{x.Page}:{x.Fence.Line} bn-sample=component:{x.Fence.SkipReason} collides with a public shipped type of the same name")];
    }

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
    public void NamedComponents_HaveValidUniqueIdentifiers()
    {
        var fences = AllFences(BnRepo.Root()).ToList();
        List<string> bad = BadComponentNames(fences);
        Assert.True(bad.Count == 0, "bn-sample=component:<Name> must be a valid C# identifier: " + string.Join(" | ", bad));
        List<string> dup = DuplicateComponentNames(fences);
        Assert.True(dup.Count == 0, "two fences may not claim the same component name — they would overwrite one another's generated file: " + string.Join(" | ", dup));
    }

    [Fact]
    public void NoNamedSampleComponent_CollidesWithAShippedType()
    {
        var fences = AllFences(BnRepo.Root()).ToList();
        var named = NamedComponentFences(fences).ToList();
        Assert.True(named.Count >= MinimumNamedComponents,
            $"found {named.Count} named (bn-sample=component:<Name>) samples, fewer than the measured {MinimumNamedComponents} — this fact would be scanning nothing (Rule 2).");
        List<string> collisions = ShippedTypeCollisions(fences);
        Assert.True(collisions.Count == 0,
            "a named doc sample must not share a simple name with a public shipped type — it would shadow it, or resolve ambiguously wherever both are in scope: " + string.Join(" | ", collisions));
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
        var plantedFences = InScope(DocSampleParser.Fences(planted).Select(f => ("website/docs/planted.md", f)));
        Assert.Single(Unclassified(plantedFences));
        Assert.Single(VersionMentions("website/docs/planted.md", planted));
        Assert.Empty(VersionMentions(MigratingDir + "planted.md", planted));
        // A v-prefixed version is a version too — the gap Task 4 found live twice.
        Assert.Single(VersionMentions("website/docs/planted.md", "since v0.12.0 it is generated\n"));
        // …but not inside a longer token, an address, or a two-part platform name.
        Assert.Empty(VersionMentions("website/docs/planted.md", "xv1.2.3 abc1.2.3 10.0.0.1 on .NET 10\n"));

        // The CompiledLanguages filter itself, through the SAME InScope helper AllFences
        // uses: an unmarked bash fence is a fence (DocSampleParser.Fences sees it) but
        // never a compiled-language one, so it must never be COUNTED as unclassified — it
        // is simply out of scope, the same way the other 26 non-razor/csharp fences on
        // real pages are.
        const string plantedBash = "text\n```bash\necho hi\n```\n";
        var bashFences = DocSampleParser.Fences(plantedBash);
        Assert.Single(bashFences); // the splice landed, or this control proves nothing
        var compiledLanguageBashFences = InScope(bashFences.Select(f => ("website/docs/planted.md", f)));
        Assert.Empty(compiledLanguageBashFences);
        Assert.Empty(Unclassified(compiledLanguageBashFences));

        // Fix round 1: an indented fence (0–3 leading spaces, legal CommonMark inside a
        // list item) must be SEEN at all — this is exactly the shape that was invisible
        // before analyzers.md's six "Compliant shape" samples were found.
        const string plantedIndented = "text\n\n  ```csharp\n  var x = 1;\n  ```\n";
        var indentedFences = InScope(DocSampleParser.Fences(plantedIndented).Select(f => ("website/docs/planted.md", f))).ToList();
        Assert.Single(indentedFences); // the splice landed, or this control proves nothing
        Assert.Single(Unclassified(indentedFences));

        // Fix round 1: bn-sample=component:<Name> — a bad identifier and a duplicate name
        // must both be caught, never silently accepted or silently overwritten.
        const string plantedBadName = "```razor bn-sample=component:123bad\n<BnView />\n```\n";
        var badNameFences = DocSampleParser.Fences(plantedBadName).Select(f => ("website/docs/planted.md", f)).ToList();
        Assert.Single(BadComponentNames(badNameFences));

        const string plantedDuplicateName = "```razor bn-sample=component:Dup\n<BnView />\n```\n```razor bn-sample=component:Dup\n<BnView />\n```\n";
        var duplicateNameFences = DocSampleParser.Fences(plantedDuplicateName).Select(f => ("website/docs/planted.md", f)).ToList();
        Assert.Equal(2, duplicateNameFences.Count); // the splice landed, or this control proves nothing
        Assert.Single(DuplicateComponentNames(duplicateNameFences));

        // Fix round 2: a named sample claiming a real shipped type's simple name must be
        // caught by the SAME helper the real fact calls.
        const string plantedShippedCollision = "```razor bn-sample=component:BnView\n<BnText />\n```\n";
        var collisionFences = DocSampleParser.Fences(plantedShippedCollision).Select(f => ("website/docs/planted.md", f)).ToList();
        Assert.Single(collisionFences); // the splice landed, or this control proves nothing
        Assert.Single(ShippedTypeCollisions(collisionFences));

        // Fix round 3: Close requires an end anchor. A body line that merely STARTS with
        // "```csharp" — documenting the fence marker syntax itself, inside another fence —
        // must NOT close the fence early; only a line that is 0–3 spaces, three-or-more
        // backticks, then nothing but whitespace closes it.
        const string plantedFakeClose = "```csharp\nline one\n```csharp\nline two\n```\n";
        var fakeCloseFences = DocSampleParser.Fences(plantedFakeClose);
        Assert.Single(fakeCloseFences); // one fence, not two — the mid-body "```csharp" must not close it
        Assert.Equal("line one\n```csharp\nline two", fakeCloseFences[0].Body);
    }
}
