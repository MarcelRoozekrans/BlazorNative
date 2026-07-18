using System.Text.RegularExpressions;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// ReleaseWorkflowPinTests — Phase 8.2 (design decision 6, M8 DoD #3: the
// release pipeline's two normative rules, pinned rather than reviewed), re-cut
// in Phase 9.x when the publish MOVED from release.yml into release-please.yml.
//
// THE MOVE, AND WHY THE PINS MOVED WITH IT. release.yml used to own the push,
// gated on `release: types: [published]`. That design never fired: release-please
// cuts the Release with GITHUB_TOKEN, and GitHub does not fire workflow triggers
// for GITHUB_TOKEN-created events (anti-recursion), so the push workflow never ran
// and nothing reached nuget.org. The push therefore moved INTO release-please.yml,
// gated on that action's own `release_created` output, publishing in the same run
// the tag is cut. So the two facts these pins guard now live in release-please.yml,
// and the pins point there. release.yml is now the keyless PR-time validation lane.
//
// THE LANE: build-test — the one required lane where every file is
// checkout-visible (the drift-test house rule; RouteTableDriftTests' and
// PackagePurityTests' precedent). `.github/workflows/*.yml` is not a build
// input of anything, so it is read from the checkout as text.
//
// WHY THESE TWO FACTS ARE TESTS AND NOT COMMENTS. The publish job fires ONCE PER
// RELEASE — it is the least-exercised code in the repository, and both of these
// rules fail SILENTLY and EXPENSIVELY:
//
//   1. SECRET CONTAINMENT — nothing publishes except the merge of a release PR,
//      and the mechanism of that rule is that `secrets.NUGET_API_KEY` is
//      reachable from EXACTLY ONE job in EXACTLY ONE workflow. A second
//      reference — a push step bolted into ci.yml, a "convenience" key in a
//      validate job, a stale one left behind in release.yml — would move the
//      door without moving the sign on it, and no lane would notice. There is no
//      review that catches this reliably; there is a test.
//
//   2. NO VERSION OVERRIDE — the props is the version and the tag is a CLAIM
//      the workflow ASSERTS (8.2 decision 2). `-p:Version=` / `-p:PackageVersion=`
//      in the release path would make the props literal LIE: the packages on
//      nuget.org would not be reproducible from the commit they name (pack at
//      that SHA yields one version, nuget.org serves another) and the nuspec's
//      own repository@commit would point at a tree that disagrees with the
//      package it is stamped into. It would also silently defeat BOTH of 8.1's
//      version pins by routing around the property they guard.
//
//      This is not a hypothetical drift. It is the EXACT shape a contributor
//      imports by copying the owner's own reference implementation
//      (AdoNet.Async's release-please.yml publish job: `VERSION="${TAG#v}"` ->
//      `pack -p:PackageVersion=$VERSION`), which can afford it because GitVersion
//      computes its version anyway — there is no literal to contradict. THIS
//      REPO HAS A LITERAL, ON PURPOSE. So the predictable drift gets the tooth.
//
// BOTH ARE NON-VACUITY-ASSERTED (8.1's I-2/I-3 lesson applied at design time
// rather than at review). Fact 2 especially: it is an ABSENCE assertion over a
// file read, and an absence assertion over a blind scanner is green for the
// wrong reason — a deleted, renamed, moved or emptied release-please.yml would
// "contain no -p:Version=" perfectly. So each pin proves it can SEE its subject
// before it reports on it. That is the house rule at
// PackagePurityTests.TypeNamesOf: *a pin that cannot see its subject must never
// pass vacuously.*
// ─────────────────────────────────────────────────────────────────────────────

public sealed class ReleaseWorkflowPinTests
{
    private const string WorkflowDir = ".github/workflows";

    /// <summary>The workflow that publishes — where the single NUGET_API_KEY
    /// reference and the `dotnet nuget push` now live. The Phase 9.x auto-publish
    /// re-cut moved them out of release.yml (now the keyless PR-time validation
    /// lane) and into release-please.yml's `push` job, gated on the action's
    /// `release_created` output.</summary>
    private const string PublishWorkflow = ".github/workflows/release-please.yml";

    /// <summary>The key's expression — whitespace-tolerant inside the braces,
    /// because a pin that only sees one spelling of the same reference is a pin
    /// with a hole.
    ///
    /// IT COUNTS COMMENTS TOO, DELIBERATELY — and this is the one judgment call
    /// in this file, so it is recorded rather than left to be rediscovered. A
    /// YAML comment is inert and cannot leak a secret, so counting it is
    /// technically a false positive; the pin reds anyway, for two reasons.
    /// (1) It makes the claim GREPPABLE: `grep -r "secrets.NUGET_API_KEY"
    /// .github/` returns exactly one hit, and that hit is the door — an owner
    /// auditing "what can reach my key?" gets a complete answer from one
    /// command, with no "…except the comments" footnote. (2) The alternative is
    /// a comment-stripping parser, and stripping `#`-to-EOL from YAML is wrong
    /// in the presence of block scalars (the publish workflow's `run: |` steps
    /// contain literal `#` characters that are CONTENT, not comments) — so the
    /// parser would either be fooled or, worse, eat its own subject and go
    /// green. A pin that occasionally reds on prose fails LOUD and takes ten
    /// seconds to fix; a pin with a parser that can blind itself fails SILENT.
    ///
    /// This is not theoretical: the pin's first run reddened on release.yml's
    /// own `env:` comment, which quoted the expression it was describing. Every
    /// comment that names the secret spells it without its braces.</summary>
    private static readonly Regex NugetApiKeyReference =
        new(@"\$\{\{\s*secrets\.NUGET_API_KEY\s*\}\}", RegexOptions.CultureInvariant);

    /// <summary>The version-override shapes, in every spelling MSBuild accepts
    /// on a CLI: `-p:` and `/p:`, `Version` and `PackageVersion`, any casing
    /// (MSBuild property names are case-insensitive).</summary>
    private static readonly Regex VersionOverride =
        new(@"[-/]p:(Version|PackageVersion)\s*=", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>THE CONTAINMENT PIN (8.2 normative rule 1). The key is
    /// referenced exactly once, across every workflow, and that once is in
    /// release-please.yml. Mutation: add the reference to ci.yml — or leave a
    /// stale one in release.yml — -> red NAMING the file.</summary>
    [Fact]
    public void TheNugetApiKey_IsReferencedExactlyOnce_AndOnlyInReleasePleaseYml()
    {
        var workflows = WorkflowFiles();

        var referencesByFile = workflows
            .Select(f => (
                File: Relative(f),
                Count: NugetApiKeyReference.Matches(File.ReadAllText(f)).Count))
            .Where(x => x.Count > 0)
            .OrderBy(x => x.File, StringComparer.Ordinal)
            .ToList();

        int total = referencesByFile.Sum(x => x.Count);

        Assert.True(
            total == 1 && referencesByFile.Count == 1 && referencesByFile[0].File == PublishWorkflow,
            "NOTHING PUBLISHES EXCEPT THE MERGE OF A RELEASE PR (8.2 normative rule 1), and the "
            + "mechanism of that rule is that ${{ secrets.NUGET_API_KEY }} is reachable from "
            + $"EXACTLY ONE job in EXACTLY ONE workflow — `{PublishWorkflow}`'s `push` job, which "
            + "is guarded on `needs.release-please.outputs.release_created == 'true'`.\n"
            + $"  expected: 1 reference, in {PublishWorkflow}\n"
            + $"  found:    {total} reference(s) across {referencesByFile.Count} file(s): "
            + (referencesByFile.Count == 0
                ? "(none — see below)"
                : string.Join(", ", referencesByFile.Select(x => $"{x.File} ×{x.Count}")))
            + $"\n  (scanned {workflows.Count} workflow file(s) under {WorkflowDir})\n"
            + "A SECOND reference moves the door without moving the sign on it: a push step in "
            + "ci.yml would publish from a MERGE that is not a release-PR merge, which is verbatim "
            + "what DoD #3 forbids; a stale reference left in release.yml would resurrect the old "
            + "door. ZERO references means the push job can no longer authenticate — or that this "
            + "pin has gone blind. Neither is a thing to green by editing this test.");
    }

    /// <summary>THE NO-OVERRIDE PIN (8.2 normative rule 3 / decision 2). The
    /// release path never overrides the props version. Mutation: add
    /// `-p:Version=1.2.3` to release-please.yml's push job -> red.</summary>
    [Fact]
    public void TheReleaseWorkflow_NeverOverridesTheVersion()
    {
        string source = ReadPublishWorkflow();

        // THE POSITIVE CONTROL, first. The assertion below is an ABSENCE claim,
        // and this file could be emptied, gutted or restructured while the
        // absence stayed perfectly true. So: prove the subject is still the
        // thing being claimed about. `dotnet nuget push` IS the release path —
        // if it is gone, this pin is guarding a file that no longer publishes
        // anything, and it must say so rather than pass.
        Assert.True(
            source.Contains("dotnet nuget push", StringComparison.Ordinal),
            $"could not find `dotnet nuget push` in {PublishWorkflow} — the release path moved or "
            + "was rewritten. The no-override assertion below would then be an absence claim over "
            + "a file that publishes nothing: TRUE, and worthless. Re-point this pin deliberately "
            + "rather than letting it pass over a subject it can no longer see.");

        var offenders = VersionOverride.Matches(source)
            .Select(m =>
            {
                int line = source.Take(m.Index).Count(c => c == '\n') + 1;
                return $"line {line}: {m.Value}";
            })
            .ToList();

        Assert.True(offenders.Count == 0,
            "THE PROPS IS THE VERSION; THE TAG IS A CLAIM (8.2 decision 2). No `-p:Version=` or "
            + $"`-p:PackageVersion=` may appear in {PublishWorkflow}: overriding the version there "
            + "makes src/Directory.Build.props LIE — the packages on nuget.org stop being "
            + "reproducible from the commit they name, the nuspec's own repository@commit points "
            + "at a tree that disagrees with the package it is stamped into, and BOTH of 8.1's "
            + "version pins are silently defeated by routing around the property they guard.\n"
            + $"  offenders: {string.Join("; ", offenders)}\n"
            + "This is the exact shape copied from the reference implementation "
            + "(`VERSION=\"${TAG#v}\"` -> `pack -p:PackageVersion=$VERSION`), which can afford it "
            + "because GitVersion computes its version anyway. This repo has a version LITERAL, on "
            + "purpose. The release flow ASSERTS the tag against it (scripts/release-preflight.ps1) "
            + "and never overrides it. Bump the props in a PR, then let release-please tag it.");
    }

    // ── Readers (non-vacuity asserted) ───────────────────────────────────────

    /// <summary>Every workflow file, enumerated — never rostered, so a NEW
    /// workflow is inside the containment pin the day it appears rather than
    /// the day someone remembers to add it here (8.1's I-2 lesson: the copy you
    /// forget to pin is the one that drifts). An enumeration that finds nothing
    /// would green the containment pin over an empty set, so it is asserted
    /// non-empty.</summary>
    private static List<string> WorkflowFiles()
    {
        string dir = CheckoutPath(WorkflowDir);
        Assert.True(Directory.Exists(dir),
            $"{WorkflowDir} not found at {dir} — the containment pin would scan NOTHING and pass. "
            + "A pin that cannot see its subject must never pass vacuously.");

        var files = Directory.EnumerateFiles(dir, "*.yml", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.True(files.Count > 0,
            $"enumerated ZERO *.yml under {WorkflowDir} — the containment pin would find zero "
            + "references and could not tell 'the key is contained' from 'the scanner is blind'.");
        return files;
    }

    /// <summary>release-please.yml's text, with existence AND non-emptiness
    /// asserted. An absent or empty file satisfies every absence claim in this
    /// class perfectly — which is why it is a RED here, not a pass.</summary>
    private static string ReadPublishWorkflow()
    {
        string file = CheckoutPath(PublishWorkflow);
        Assert.True(File.Exists(file),
            $"{PublishWorkflow} not found at {file} — it is THE ONE DOOR since the auto-publish "
            + "re-cut. Its absence must be a RED, not a vacuous pass over a missing read.");

        string source = File.ReadAllText(file);
        Assert.False(string.IsNullOrWhiteSpace(source),
            $"{PublishWorkflow} is EMPTY — every absence assertion in this class would pass over "
            + "it, loudly claiming nothing is wrong with a workflow that does nothing at all.");
        return source;
    }

    private static string Relative(string absolutePath)
        => Path.GetRelativePath(RepoRoot(), absolutePath).Replace(Path.DirectorySeparatorChar, '/');

    private static string CheckoutPath(string relativePath)
        => Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>The repo root — PackagePurityTests' rule: the nearest ancestor
    /// holding BlazorNative.sln.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BlazorNative.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, "BlazorNative.sln not found above " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
