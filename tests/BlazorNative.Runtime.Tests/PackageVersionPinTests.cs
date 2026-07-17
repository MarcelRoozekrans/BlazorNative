using System.Text.Json;
using System.Xml.Linq;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// PackageVersionPinTests — Phase 8.1 (design decision 4, M8 DoD #2: the
// versioning scheme decided and recorded), extended in Phase 8.6 (decision 3).
//
// THE NORMATIVE RULE, AS 8.6 RESTATES IT: the version has ONE AUTHOR and N
// PINNED MIRRORS. `.release-please-manifest.json` is the AUTHOR — the only file
// into which a version is ever DECIDED. src/Directory.Build.props' <Version> is
// its FIRST MIRROR and remains the build's single source of truth: nothing in
// the build, the pack, the smoke or the classifier ever reads the manifest.
// Every other version literal mirrors the props. N is 6.
//
// (8.1's rule was "ONE literal". That was always shorthand for "one literal in
// src/" — 8.3 already added the template's twin. This is the real rule.)
//
// The 4.5 shape (a phase-stamped literal repeated per-csproj, 7 files of churn
// per bump) is dead; drift BACK to per-csproj literals must be a red, not a
// review catch. Three teeth:
//
//   1. THE PROPS CARRIES EXACTLY ONE <Version> — zero means the source of
//      truth vanished (and consumer-smoke.ps1, which PARSES this file for the
//      version it packs and restores at, would fail loudly for its own
//      reasons); two means ambiguity nobody should ever have to resolve.
//   2. NO SHIPPED CSPROJ OVERRIDES IT — a <Version> in any PropertyGroup of
//      any shipped csproj would silently win over the shared props (csproj
//      evaluates after Directory.Build.props). The smoke's filename-vs-props
//      drift check reds on the same sin at the packaging layer; this pin reds
//      it earlier, in the required build-test lane. The set it walks is
//      ENUMERATED from src/, not rostered (8.1 Gate 1 review, I-2): a seventh
//      shipped project is version-guarded the day it appears, without anyone
//      remembering to tell this file about it.
//   3. THE MANIFEST AGREES WITH THE PROPS (8.6) — the arrow that arrived WITH
//      the automation, and the one no pin watched. See its own docstring.
//
// Enumerated from the checkout (build-test is the one required lane where
// every file is checkout-visible — the drift-test house rule; RepoRoot is
// PackagePurityTests' rule verbatim).
// ─────────────────────────────────────────────────────────────────────────────

public sealed class PackageVersionPinTests
{
    /// <summary>The shipped set, ENUMERATED from the checkout's src/ csprojs —
    /// deliberately not a roster. Until Phase 8.1's Gate 1 review (I-2) this was
    /// a second literal copy of PackagePurityTests.ShippedAssemblies whose
    /// docstring CLAIMED "purity tooth 3 keeps this equal to the src/
    /// enumeration" — it did not, and could not: tooth 3 pins ITS OWN literal
    /// against src/ and has never read this file. The two copies could drift
    /// silently, and the drift had a live shape: add src/BlazorNative.Seven,
    /// tooth 3 reds, a dev adds "BlazorNative.Seven" to ShippedAssemblies to
    /// green it — and Seven's version is now guarded by nothing, because this
    /// roster never learned the name. Enumerating removes the copy rather than
    /// pinning it: there is nothing left to drift.
    ///
    /// Full PATHS, not names: a name list would have to be re-joined back into
    /// src/{name}/{name}.csproj, which both assumes the flat layout and makes
    /// the File.Exists check below a tautology. Non-vacuity is asserted here —
    /// an enumeration that finds nothing would green every caller (the house
    /// rule at PackagePurityTests.TypeNamesOf; the 8.1 Gate 1 review's I-3 is
    /// what that rule looks like when it is violated).</summary>
    private static List<string> ShippedCsprojs()
    {
        var csprojs = Directory.EnumerateFiles(
                Path.Combine(RepoRoot(), "src"), "*.csproj", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.True(csprojs.Count > 0,
            $"enumerated ZERO csprojs under {Path.Combine(RepoRoot(), "src")} — the version pin "
            + "would pass over an empty set, which is a pin that cannot see its subject. Fix the "
            + "enumeration; do not let it green vacuously.");
        return csprojs;
    }

    [Fact]
    public void TheSharedProps_CarriesExactlyOneVersionLiteral()
    {
        string props = Path.Combine(RepoRoot(), "src", "Directory.Build.props");
        Assert.True(File.Exists(props),
            "src/Directory.Build.props is the ONE version source (8.1 design decision 4) — "
            + "it must exist; consumer-smoke.ps1 parses it for the pack/restore version.");

        var versions = XDocument.Load(props).Root!
            .Elements("PropertyGroup").Elements("Version")
            .Select(v => v.Value)
            .ToList();

        Assert.True(versions.Count == 1,
            $"src/Directory.Build.props must carry exactly ONE <Version> element (the single "
            + $"source of version truth), found {versions.Count}"
            + (versions.Count > 0 ? $": {string.Join(", ", versions)}" : "") + ".");
        Assert.False(string.IsNullOrWhiteSpace(versions[0]),
            "the <Version> literal must not be empty.");
    }

    [Fact]
    public void NoShippedCsproj_OverridesTheSharedVersion()
    {
        var offenders = new List<string>();
        foreach (string csproj in ShippedCsprojs())
        {
            var overrides = XDocument.Load(csproj).Root!
                .Elements("PropertyGroup").Elements("Version")
                .Select(v => $"{Path.GetFileNameWithoutExtension(csproj)}: <Version>{v.Value}</Version>")
                .ToList();
            offenders.AddRange(overrides);
        }

        Assert.True(offenders.Count == 0,
            "ONE version truth (8.1 design decision 4): no shipped csproj may carry a "
            + "<Version> — it would silently win over src/Directory.Build.props. Offenders: "
            + string.Join("; ", offenders));
    }

    /// <summary>THE MANIFEST↔PROPS PIN (8.6 decision 3) — the arrow that
    /// arrived WITH the automation and that nothing else watches.
    ///
    /// WHY IT IS NEW, AND WHY THE EXISTING PINS CANNOT COVER IT. Every other
    /// version pin in this repo compares a mirror TO THE PROPS — TemplateDrift's
    /// six literals, the smoke's nupkg filename, the classifier's tag. So if
    /// release-please's `extra-files` ever stopped naming src/Directory.Build.props,
    /// the manifest would bump, the props would stay behind, and EVERY ONE OF
    /// THOSE PINS WOULD STAY GREEN — because the mirrors would still agree with
    /// the props they mirror. The props would simply be one release behind its
    /// own author, and the packages would publish at the old version forever.
    /// That is a real gap, it is invisible to the whole existing suite, and it
    /// did not exist before a machine started writing these files.
    ///
    /// WHY IT IS SOUND AS AN ALWAYS-TRUE INVARIANT ON MAIN — the question worth
    /// asking of any pin that compares two files a tool writes. release-please
    /// writes the manifest AND the props IN THE SAME RELEASE PR, so they agree
    /// at every commit on main: between releases both sit at the last released
    /// version. There is no window in which they legitimately differ.
    ///
    /// AND THIS IS THE PART THAT MATTERS: THIS PIN AUDITS A MACHINE. It runs in
    /// `build-test` — a REQUIRED check — so it reds release-please's OWN pull
    /// request before it can merge. The release PR is branch-protected like any
    /// other and cannot merge red. That is the guard: not a human remembering,
    /// but the machine's own output being refused.
    ///
    /// ⚠ THE CHECKS DO NOT START BY THEMSELVES — AND THIS PIN'S DELIVERY DEPENDS
    /// ON A CLICK. release-please opens the release PR with `GITHUB_TOKEN`, and
    /// when a workflow using `GITHUB_TOKEN` creates or updates a pull request,
    /// the resulting `pull_request` event creates workflow runs in an
    /// APPROVAL-REQUIRED state: a banner appears and someone with write access
    /// must click "Approve workflows to run". release-please-action's own README
    /// says the same thing from the other side ("configure a PAT if you want
    /// GitHub Actions CI checks to run on Release Please PRs"). So the honest
    /// sequence is: the machine opens the PR → a human clicks Approve → THIS PIN
    /// RUNS → it reds a bad machine BEFORE the merge. The pin is sound; its
    /// delivery needs the click. It is a step, not a surprise, and it is named in
    /// docs/GITHUB-SETUP.md's ritual and in release-please.yml's header.
    ///
    /// UNTIL THAT CLICK, THE PR SITS WITH NO CHECKS — and a required check that
    /// never ran cannot be satisfied, so branch protection will not let the PR
    /// merge. Nothing merges, nothing tags, nothing publishes. The failure
    /// direction is SAFE (design UNPROVEN row U8).
    ///
    /// THE REJECTED ALTERNATIVE, recorded because it is the obvious one: a PAT
    /// (or a GitHub App token) restores automatic CI on the release PR. The owner
    /// chose `GITHUB_TOKEN` + the click instead — a PAT is a SECOND repo-scoped
    /// secret that expires and needs rotation, against the one-secret law, and
    /// the click is itself a human gate of exactly the shape this phase already
    /// trusts for the draft-publish go.
    ///
    /// The classifier's tag↔props assertion (8.2 decision 2) is the second,
    /// independent guard — the Release-time backstop if this pin is ever removed
    /// or the PR is force-merged. 8.2's assertion did not lose its subject when
    /// the owner stopped hand-bumping; it was RE-POINTED. Its old job was "the
    /// owner forgot to bump the props". Its new job is "release-please's config
    /// is wrong". The subject is better, not gone.
    ///
    /// ⚠ WHAT THIS PIN CANNOT SEE, named because a guard's limits belong next to
    /// the guard: the rung-4 graduation trap (8.6 decision 2). Reaching 1.0.0
    /// without dropping `versioning`/`prerelease`/`prerelease-type` from
    /// release-please-config.json makes the NEXT commit compute `1.0.1-preview` —
    /// and the manifest and the props would AGREE on that, so this pin is
    /// perfectly happy. So is the classifier. The only guard there is prose, and
    /// it lives in docs/GITHUB-SETUP.md where the person doing the graduating is
    /// standing.</summary>
    [Fact]
    public void TheManifest_AgreesWithTheProps()
    {
        string manifestPath = Path.Combine(RepoRoot(), ".release-please-manifest.json");
        Assert.True(File.Exists(manifestPath),
            $".release-please-manifest.json not found at {manifestPath} — it is the version's AUTHOR "
            + "(8.6 normative rule 2). Its absence must be a RED, not a vacuous pass over a missing "
            + "read: this pin is an equality claim, and an equality claim over a file that is not "
            + "there proves nothing at all.");

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));

        Assert.True(manifest.RootElement.TryGetProperty(".", out JsonElement rootPackage),
            ".release-please-manifest.json has no \".\" key — that is the root package's entry, and "
            + "it is the key release-please-config.json's `packages` declares. Either the manifest "
            + "was rewritten by hand (nobody should ever do that) or the config's package path "
            + "changed and this pin is now reading for a key that no longer exists. Re-point it "
            + "deliberately rather than deleting it.");

        string manifestVersion = rootPackage.GetString() ?? "(null)";
        Assert.False(string.IsNullOrWhiteSpace(manifestVersion),
            ".release-please-manifest.json's \".\" is empty — the version's author decided nothing.");

        string propsVersion = PropsVersion();

        Assert.True(manifestVersion == propsVersion,
            "THE MANIFEST AND THE PROPS DISAGREE (8.6 decision 3). The version has ONE AUTHOR and N "
            + "PINNED MIRRORS: .release-please-manifest.json DECIDES, and src/Directory.Build.props "
            + "is its FIRST mirror and the build's only source of truth.\n"
            + $"  .release-please-manifest.json  \".\"        is \"{manifestVersion}\"\n"
            + $"  src/Directory.Build.props      <Version>  is \"{propsVersion}\"\n\n"
            + "release-please writes BOTH in the same release PR, so on main they agree at every "
            + "commit. A disagreement means one of three things, and all three are the same fix:\n"
            + "  · release-please-config.json's `extra-files` no longer names src/Directory.Build.props;\n"
            + "  · the `<!-- x-release-please-version -->` annotation was removed from, or moved off, "
            + "the <Version> line (the Generic updater is LINE-scoped — it rewrites the annotated "
            + "line and nothing else, so an annotation on the wrong line is an annotation on no line);\n"
            + "  · someone hand-edited a version literal. Nobody does that any more.\n\n"
            + "NOTHING ELSE IN THIS REPO CATCHES THIS. Every other version pin compares a MIRROR to "
            + "the props — so a props left behind by its own author is a props all six mirrors still "
            + "agree with, and the whole suite goes green while the packages publish one release "
            + "behind forever. Do not edit the manifest to make this pass: it is the author, and "
            + "editing it by hand is the thing this rule exists to stop.");
    }

    /// <summary>The props' single <Version>, read the same way
    /// TheSharedProps_CarriesExactlyOneVersionLiteral reads it — that test owns
    /// the "exactly one" claim, so this reader takes it as given and says so
    /// loudly if it is not.</summary>
    private static string PropsVersion()
    {
        string props = Path.Combine(RepoRoot(), "src", "Directory.Build.props");
        Assert.True(File.Exists(props),
            "src/Directory.Build.props is the version's FIRST MIRROR and the build's source of "
            + "truth — it must exist.");

        var versions = XDocument.Load(props).Root!
            .Elements("PropertyGroup").Elements("Version")
            .Select(v => v.Value)
            .ToList();

        Assert.True(versions.Count == 1,
            $"expected exactly ONE <Version> in src/Directory.Build.props, found {versions.Count} — "
            + "TheSharedProps_CarriesExactlyOneVersionLiteral owns that claim and is redding too. "
            + "This pin cannot compare against an ambiguous props.");
        return versions[0];
    }

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
