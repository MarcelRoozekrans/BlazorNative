using System.Text.RegularExpressions;
using BlazorNative.Tests.Shared;

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
//
// AND THAT IS TWO PROPERTIES, NOT ONE — the correction phase 15.1 made to this
// file, because until then it said "one" and the comment on fact 2 said so in
// capitals. Proving the SCAN found its subject and proving the DETECTOR still
// recognises its shape are different claims, and this file had only the first:
// `dotnet nuget push` being present says nothing about whether `VersionOverride`
// would still match `-p:PackageVersion=`. A pattern reworded past its subject
// leaves both the file read and the absence claim perfectly healthy and lets a
// real override through. The containment pin (fact 1) was always controlled for
// free — `total == 1` reds at zero matches, which is a reworded regex — and the
// no-override pin was not. TheOverrideDetector_StillMatchesTheShapeItWasWritten
// For is the control it was missing; the pin standard calls this Rule 3.
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

        // THE SUBJECT-MOVED GUARD, first (pin standard Rule 4). The assertion
        // below is an ABSENCE claim, and this file could be emptied, gutted or
        // restructured while the absence stayed perfectly true. So: prove the
        // subject is still the thing being claimed about. `dotnet nuget push` IS
        // the release path — if it is gone, this pin is guarding a file that no
        // longer publishes anything, and it must say so rather than pass.
        //
        // ⚠ THIS WAS LABELLED "THE POSITIVE CONTROL" UNTIL PHASE 15.1, AND IT IS
        // NOT ONE. It proves the SUBJECT is still the release path; it proves
        // nothing whatever about `VersionOverride`, which is the DETECTOR. The
        // two properties are different — a walk that finds its file versus a
        // pattern that still matches its shape — and the pin standard's Rule 3
        // exists because they are routinely confused. The mislabel was worse than
        // the gap it hid: the phase-15.0 census records that any sweep grepping
        // for the phrase "positive control" would have scored this fact as done
        // and moved on. The real control is a separate fact —
        // TheOverrideDetector_StillMatchesTheShapeItWasWrittenFor, below.
        Assert.True(
            source.Contains("dotnet nuget push", StringComparison.Ordinal),
            $"could not find `dotnet nuget push` in {PublishWorkflow} — the release path moved or "
            + "was rewritten. The no-override assertion below would then be an absence claim over "
            + "a file that publishes nothing: TRUE, and worthless. Re-point this pin deliberately "
            + "rather than letting it pass over a subject it can no longer see.");

        var offenders = Offenders(source);

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

    /// <summary>THE OFFENDER PROJECTION — one implementation, shared by the pin and by
    /// its positive control (pin standard Rule 8). A control that reruns its own copy of
    /// this projection controls the copy, not the pin; the whole point is that the
    /// control drives the SAME regex through the SAME line-numbering the failure message
    /// prints.</summary>
    private static List<string> Offenders(string source) =>
        [.. VersionOverride.Matches(source)
            .Select(m => $"line {source.Take(m.Index).Count(c => c == '\n') + 1}: {m.Value}")];

    /// <summary>THE POSITIVE CONTROL FOR `VersionOverride` (pin standard Rule 3; census
    /// item 7, and §5.5's mislabel). The name is used here in its narrow sense — a fixed
    /// point the DETECTOR is required to hit — and nowhere else in this file.
    ///
    /// <para>NO HONEST TREE ANCHOR EXISTS FOR THIS DETECTOR, and that conclusion is
    /// recorded rather than worked around, because a forced control reads as coverage
    /// while providing none. The anchors considered, and why each fails:</para>
    /// <list type="bullet">
    /// <item><description>A live `-p:Version=` anywhere in a build or publish path —
    /// there is none, <b>by construction</b>. This pin's entire claim is that the repo
    /// never overrides the version, so the subject tree is required to be empty of the
    /// pattern. That is the structural difference from <c>NSLogDriftTests</c>, whose
    /// exempt test bundle is a real tree that MUST still hold the thing the shipped tree
    /// must not.</description></item>
    /// <item><description><c>scripts/release-preflight.ps1</c>'s comment and
    /// <c>docs/GITHUB-SETUP.md</c>'s prose both spell `-p:Version=` while describing this
    /// rule. Prose is reworded freely and legitimately; anchoring a detector to a
    /// sentence buys a false red on a docs edit, and a pin that reds on docs edits gets
    /// weakened rather than investigated (pin standard, Rule 2's corollary on floors that
    /// move for irrelevant reasons — the same failure, applied to a control).</description></item>
    /// <item><description><c>docs/plans/2026-07-16-phase-8.2-*.md</c> spell it several
    /// times, but those are archived milestone docs; the standard says so itself, which
    /// is why it lives in <c>docs/</c> rather than beside them.</description></item>
    /// <item><description><c>ci.yml</c>'s own test-count narration quotes the shape while
    /// explaining this pin. It is prose about a baseline that every counting phase
    /// rewrites — including the one adding this fact.</description></item>
    /// </list>
    ///
    /// <para>SO THE CONTROL IS A FIXTURE, which Rule 3 lists as its third form, and it is
    /// built to be as close to the real thing as a fixture can get: it takes the REAL
    /// release workflow's text, splices in the EXACT shape the reference implementation
    /// would import — <c>pack -p:PackageVersion=$VERSION</c>, the AdoNet.Async line the
    /// class header names as the predictable drift — immediately above the real
    /// <c>dotnet nuget push</c>, and runs the pin's own <see cref="Offenders"/> over the
    /// result. It asserts the detector finds exactly one, AT THE LINE THE SPLICE LANDED
    /// ON, so the failure message's line:line fidelity is exercised too and not merely
    /// the regex (Rule 7's note that line numbers through a projection break silently).
    /// Reword <see cref="VersionOverride"/> past its subject and this reds.</para>
    ///
    /// <para>WHAT A FIXTURE CANNOT BUY, said plainly: it proves the detector still
    /// recognises the shape, never that the detector is pointed at anything real. That
    /// second property is what the subject-moved guard inside
    /// <see cref="TheReleaseWorkflow_NeverOverridesTheVersion"/> buys, and the two are
    /// only worth anything read together — which is the distinction the old "THE POSITIVE
    /// CONTROL" label collapsed.</para></summary>
    [Fact]
    public void TheOverrideDetector_StillMatchesTheShapeItWasWrittenFor()
    {
        // ── the reference implementation's line, spliced into the real workflow ──
        string[] lines = ReadPublishWorkflow().Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        // The COMMAND, not the `- name:` above it and not the prose around it: this
        // workflow says "dotnet nuget push" in four comments and two step names, and a
        // splice above a comment would be testing the detector against a line the
        // release path never executes.
        int at = Array.FindIndex(lines, l => l.TrimStart().StartsWith("dotnet nuget push", StringComparison.Ordinal));
        Assert.True(at >= 0,
            $"no `dotnet nuget push` COMMAND line in {PublishWorkflow} to splice above — the "
            + "workflow still mentions the phrase in prose, but nothing runs it. The release path "
            + "moved, and this control has nowhere realistic to put the override it is testing "
            + "for. Re-point it at the new pack/push step rather than falling back to a bare "
            + "string, which would stop exercising the real file altogether.");

        var spliced = lines.ToList();
        spliced.Insert(at, "          dotnet pack -p:PackageVersion=$VERSION -o ./artifacts");
        var found = Offenders(string.Join("\n", spliced));

        Assert.True(found.Count == 1 && found[0] == $"line {at + 1}: -p:PackageVersion=",
            "THE VERSION-OVERRIDE DETECTOR NO LONGER DETECTS. With the reference "
            + "implementation's own `pack -p:PackageVersion=$VERSION` spliced into "
            + $"{PublishWorkflow} at line {at + 1}, the pin's offender projection reported: "
            + (found.Count == 0 ? "(nothing)" : string.Join("; ", found)) + $" — expected exactly "
            + $"one, reading `line {at + 1}: -p:PackageVersion=`.\n"
            + "  Zero matches means TheReleaseWorkflow_NeverOverridesTheVersion is an absence "
            + "claim over a pattern that can no longer see its subject: a real override in the "
            + "release path would pass, the packages on nuget.org would stop being reproducible "
            + "from the commit they name, and NOTHING would red. A match at the wrong line means "
            + "the offender projection's line numbering drifted, so the failure message would "
            + "send a reader to the wrong line of a file they publish from once per release.\n"
            + "  Fix the pattern or the projection. Do not green this by editing the expectation.");

        // ── every spelling the pattern CLAIMS, as a fixture ──────────────────────
        // The doc on VersionOverride promises `-p:` and `/p:`, both property names, any
        // casing, and tolerated whitespace before the `=`. A promise in a doc comment
        // with nothing enforcing it is this repo's most expensive bug class.
        var spellings = Offenders(
            "dotnet pack -p:Version=1.2.3\n"
            + "dotnet pack /p:PackageVersion=$VERSION\n"
            + "dotnet pack -p:packageversion =$VERSION\n");
        Assert.True(spellings.Count == 3,
            "VersionOverride claims to cover `-p:` and `/p:`, `Version` and `PackageVersion`, any "
            + $"casing, and whitespace before the `=` — it matched {spellings.Count} of those 3 "
            + "spellings: " + (spellings.Count == 0 ? "(none)" : string.Join("; ", spellings))
            + ". A narrowed pattern leaves a spelling MSBuild still honours unguarded, which is "
            + "the override arriving through the one door nobody is watching.");

        // ── the negative: near-misses that are NOT overrides ─────────────────────
        // `VersionPrefix`/`VersionSuffix` are legitimate and would be a different
        // decision to make; this pin must not claim them. A control that only ever
        // widens a detector is not a control.
        var nearMisses = Offenders(
            "dotnet pack -p:VersionPrefix=1.2.3\n"
            + "dotnet pack -p:VersionSuffix=rc1\n"
            + "dotnet pack -p:ContinuousIntegrationBuild=true\n");
        Assert.True(nearMisses.Count == 0,
            "VersionOverride matched a near-miss it must not claim: "
            + string.Join("; ", nearMisses) + ". `-p:VersionPrefix=` and `-p:VersionSuffix=` are "
            + "different properties and forbidding them is a different decision, not this one. A "
            + "detector widened until it reds on ordinary pack flags is a detector the next "
            + "contributor deletes.");
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
        => Path.GetRelativePath(BnRepo.Root(), absolutePath).Replace(Path.DirectorySeparatorChar, '/');

    private static string CheckoutPath(string relativePath)
        => Path.Combine(BnRepo.Root(), relativePath.Replace('/', Path.DirectorySeparatorChar));
}
