using System.Text.Json;
using System.Text.RegularExpressions;
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
// Every other version literal mirrors the props. N is 7 (Phase 10.1 added the
// runtime's Exports.VersionNumber as a mirror — see
// TheRuntimeVersionExport_AgreesWithTheProps below).
//
// (8.1's rule was "ONE literal". That was always shorthand for "one literal in
// src/" — 8.3 already added the template's twin. This is the real rule.)
//
// The 4.5 shape (a phase-stamped literal repeated per-csproj, 7 files of churn
// per bump) is dead; drift BACK to per-csproj literals must be a red, not a
// review catch. Four teeth:
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
//   4. EVERY EXTRA-FILE NAMES ITS UPDATER (8.6 Gate 2) — teeth 1-3 all assume
//      release-please can actually WRITE the mirrors. It could not: a bare
//      path string picks the updater by FILE EXTENSION, and template.json's
//      `.json` bought a strict `JSON.parse` that threw on the very `//`
//      annotations decision 3 put there. No release PR would ever have opened.
//      Found by a real --dry-run at Gate 2, not by reading. See its docstring.
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
    /// the guard: it is an EQUALITY claim, so anything that moves the manifest and
    /// the props TOGETHER is invisible to it. Two such moves are live at 0.x, and
    /// Phase 8.7 measured both:
    ///
    ///   · AN ACCIDENTAL GRADUATION. `bump-minor-pre-major: true` is what keeps a
    ///     `feat!:` at 0.x on the MINOR (0.1.0 -> 0.2.0). Flip it to false — which
    ///     is what "mirror AdoNet.Async" invites, and the reference really does set
    ///     it — and the very next `feat!:` computes 1.0.0. That is a graduation
    ///     nobody decided, and nuget.org has NO hard delete, so it is permanent.
    ///     The manifest and the props would agree on 1.0.0 perfectly.
    ///   · A `release-as` FOOTER IN A COMMIT BODY. It is read FIRST, before any
    ///     counting (versioning-strategies/default.js), and it sets the version
    ///     outright. Manifest and props agree on whatever it named.
    ///
    /// So does the classifier, in both cases: tag and props agree. scripts/
    /// footer-check.ps1 is the guard for the second one; the first one's guard is
    /// prose, and it lives in docs/GITHUB-SETUP.md where the person graduating is
    /// standing.
    ///
    /// (Phase 8.6's version of this note named the `-preview` re-entry trap —
    /// reaching 1.0.0 without dropping `versioning`/`prerelease`/`prerelease-type`
    /// re-glued the suffix on. Phase 8.7 deleted those three keys with the
    /// prerelease scheme itself, so that trap no longer exists to be documented.)</summary>
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

    /// <summary>THE UPDATER PIN (Phase 8.6 Gate 2, P7). Every `extra-files`
    /// entry must name its updater OUTRIGHT — the object form
    /// `{"type":"generic","path":…}` — and never the bare path string.
    ///
    /// IT EXISTS BECAUSE THE BARE STRING SHIPPED A CRASH, and only a real
    /// `--dry-run` could see it. 8.6 decision 3 chose ONE updater kind for all
    /// four files — the Generic (annotation) updater, a line-scoped regex
    /// replace — and rejected `json`+jsonpath and `xml`+xpath explicitly,
    /// because both round-trip the WHOLE document through a parser/serializer
    /// and reformat files that are this repo's version truth. Gate 1 then wrote
    /// the four paths as bare strings, which LOOKS like it says that.
    ///
    /// It does not. A bare string dispatches ON FILE EXTENSION
    /// (release-please strategies/base.js, an `else if` ladder). Only the final
    /// `else` yields the Generic updater. `.json` yields
    /// CompositeUpdater(GenericJson("$.version"), Generic), and
    /// GenericJson.updateContent opens with a bare `JSON.parse(content)`.
    /// `.template.config/template.json` carries the two `//` annotations
    /// decision 3 deliberately added, so that parse threw:
    ///
    ///   SyntaxError: Expected double-quoted property name in JSON at position
    ///   1699 (line 48 column 42)
    ///
    /// — column 42 being exactly where the `//` starts. release-please would
    /// have crashed on every run and NO release PR would ever have opened. The
    /// other three end `.props` and `.csproj`, match no arm, and reached the
    /// Generic updater BY ACCIDENT OF THEIR EXTENSIONS. So decision 3's "one
    /// updater kind everywhere" was true for three files by luck and false for
    /// the fourth — and nothing in the tree could tell the difference, because
    /// the dispatch is invisible at the config.
    ///
    /// THE FAILURE DIRECTION WAS SAFE (design row U1: "no PR appears" — loud,
    /// and nothing publishes), and that is exactly why it needed a pin rather
    /// than a shrug: a machine that never runs is a machine nobody notices is
    /// broken. The automation would simply have never worked, quietly, and the
    /// first person to look would have been reading a stack trace from a
    /// third-party updater about a file they did not know it read.
    ///
    /// WHAT THIS PIN IS NOT: it is not a copy of release-please's dispatch
    /// ladder. It does not know which extensions are special, and it must not —
    /// that list is a third-party's internal table and it drifts. It asserts
    /// ONE invariant with no extension knowledge at all: we never use the form
    /// whose updater depends on the extension. A `.json` file added tomorrow,
    /// or a `.props` renamed to `.xml` next year, is covered without this pin
    /// learning anything new.
    ///
    /// `{"type":"generic"}` is code-supported (base.js `case 'generic'`) AND
    /// schema-valid (the `extra-files` items' fifth anyOf arm, typeEnum
    /// ["generic"], required [type, path]) — both checked against the shipped
    /// artifacts rather than assumed, which is the Q2-1 rule: this repo ships a
    /// `$schema` reference, so a key the schema rejects is a red squiggle in
    /// every editor that honours it, and that trade was already refused once
    /// this phase.</summary>
    [Fact]
    public void EveryExtraFile_NamesTheGenericUpdaterOutright()
    {
        string configPath = Path.Combine(RepoRoot(), "release-please-config.json");
        Assert.True(File.Exists(configPath),
            $"release-please-config.json not found at {configPath} — it is where the version's "
            + "AUTHOR is told which files to write. Its absence must be a RED, not a vacuous pass: "
            + "a pin that cannot see its subject must never pass vacuously.");

        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(configPath));

        JsonElement extraFiles = default;
        bool found = config.RootElement.TryGetProperty("packages", out JsonElement packages)
            && packages.TryGetProperty(".", out JsonElement rootPackage)
            && rootPackage.TryGetProperty("extra-files", out extraFiles);
        Assert.True(found,
            "could not read packages[\".\"][\"extra-files\"] out of release-please-config.json — "
            + "either the root package path changed or `extra-files` is gone. If it is gone, the seven "
            + "mirrors are no longer written by anything and every bump leaves them behind: "
            + "TheManifest_AgreesWithTheProps reds on the props and TheRuntimeVersionExport_AgreesWithTheProps "
            + "reds on Exports.cs, but the SIX template literals would drift silently. Re-point this pin "
            + "deliberately rather than deleting it.");

        var bareStrings = new List<string>();
        var wrongType = new List<string>();
        int inspected = 0;

        foreach (JsonElement entry in extraFiles.EnumerateArray())
        {
            inspected++;

            if (entry.ValueKind == JsonValueKind.String)
            {
                bareStrings.Add(entry.GetString() ?? "(null)");
                continue;
            }

            string path = entry.TryGetProperty("path", out JsonElement p)
                ? p.GetString() ?? "(null path)"
                : "(no `path` key)";
            string type = entry.TryGetProperty("type", out JsonElement t)
                ? t.GetString() ?? "(null type)"
                : "(no `type` key)";

            if (type != "generic")
                wrongType.Add($"    {path}\n      declares type \"{type}\", must be \"generic\"");
        }

        // NON-VACUITY: an empty `extra-files` would make every claim below
        // trivially true while the six mirrors go unwritten forever.
        Assert.True(inspected > 0,
            "release-please-config.json's `extra-files` is EMPTY — so release-please writes the "
            + "manifest and CHANGELOG.md and NOTHING ELSE, and all six version mirrors (the props "
            + "included) sit at their old values after every release. This pin would have passed "
            + "over an empty read. A pin that cannot see its subject must never pass vacuously.");

        Assert.True(bareStrings.Count == 0 && wrongType.Count == 0,
            "AN EXTRA-FILE DOES NOT NAME ITS UPDATER (8.6 decision 3; Gate 2 P7).\n\n"
            + $"  BARE PATH STRINGS ({bareStrings.Count}) — the updater is chosen by FILE EXTENSION:\n"
            + (bareStrings.Count == 0
                ? "    (none)\n"
                : string.Join("\n", bareStrings.Select(f => $"    {f}")) + "\n")
            + $"  OBJECT FORM, WRONG TYPE ({wrongType.Count}):\n"
            + (wrongType.Count == 0 ? "    (none)\n" : string.Join("\n", wrongType) + "\n")
            + $"\n({inspected} extra-file entr(ies) inspected.)\n\n"
            + "Decision 3 chose ONE updater for all four files: the Generic (annotation) updater, a "
            + "LINE-scoped regex replace that touches the annotated line and nothing else. It "
            + "rejected `json`+jsonpath and `xml`+xpath outright, because both round-trip the whole "
            + "document through a parser and re-emit it — a formatting risk taken, on this repo's "
            + "version truth, for no gain.\n\n"
            + "A BARE STRING DOES NOT ASK FOR THAT UPDATER. It asks release-please to guess from "
            + "the extension (strategies/base.js): `.json` -> GenericJson + Generic, `.yaml`/`.yml` "
            + "-> GenericYaml + Generic, `.toml` -> GenericToml + Generic, `.xml` -> GenericXml + "
            + "Generic, and ONLY the final `else` -> Generic alone. GenericJson opens with "
            + "`JSON.parse(content)`, which is strict — and it threw on template.json's `//` "
            + "annotations at line 48 column 42, which is where the `//` starts. Gate 2's dry-run "
            + "found that; no amount of reading the config did, because the config looks correct.\n\n"
            + "Write the object form and say which updater you mean:\n"
            + "    { \"type\": \"generic\", \"path\": \"path/to/file\" }\n\n"
            + "If you genuinely want a round-trip updater for some new file, that is a decision-3 "
            + "change: make it deliberately, in the design, and expect the reformat. Do not reach "
            + "it by writing a string and letting an extension decide.");
    }

    /// <summary>THE RUNTIME-VERSION MIRROR PIN (Phase 10.1, #120). The runtime's
    /// consumer-visible version literal — BlazorNative.Runtime.Exports.VersionNumber,
    /// the string the C-ABI `version` export returns and the value
    /// NativeShellBridge.PlatformInfo / GetPlatformInfoAsync report as AppVersion —
    /// must equal the props <Version>, the same manifest↔props equality every other
    /// mirror gets.
    ///
    /// WHY IT WAS NEEDED. Until 10.1 this literal was an EIGHTH, ungoverned version
    /// string: it sat at "1.4.0-phase-5.4", frozen ~4 milestones back, in NO test
    /// and NOT in release-please's extra-files. The package was 0.1.0, so any
    /// consumer reading the runtime version got a number four milestones stale. It
    /// is now a release-please Generic mirror (annotated `// x-release-please-version`
    /// in Exports.cs and named in release-please-config.json's extra-files), so a
    /// release bumps it in lockstep with the props — and this pin reds the day the
    /// two disagree, in the required build-test lane, exactly like the manifest pin.
    ///
    /// SOUND AS AN ALWAYS-TRUE INVARIANT ON MAIN: release-please rewrites BOTH the
    /// props and Exports.cs in the same release PR, so they agree at every commit on
    /// main between releases. There is no window in which they legitimately differ.</summary>
    [Fact]
    public void TheRuntimeVersionExport_AgreesWithTheProps()
    {
        string propsVersion = PropsVersion();

        Assert.True(
            BlazorNative.Runtime.Exports.VersionNumber == propsVersion,
            "THE RUNTIME VERSION EXPORT AND THE PROPS DISAGREE (Phase 10.1, #120). "
            + "BlazorNative.Runtime.Exports.VersionNumber is the consumer-visible runtime version — "
            + "it is what the C-ABI `version` export returns (as \"BlazorNative.Runtime <VersionNumber>\") "
            + "and what NativeShellBridge reports as the PlatformInfo version and GetPlatformInfoAsync's "
            + "AppVersion. It is a release-please Generic MIRROR of the props <Version> and must equal "
            + "it at every commit on main.\n"
            + $"  Exports.VersionNumber      is \"{BlazorNative.Runtime.Exports.VersionNumber}\"\n"
            + $"  src/Directory.Build.props  <Version>  is \"{propsVersion}\"\n\n"
            + "release-please writes BOTH in the same release PR (Exports.cs is named in "
            + "release-please-config.json's extra-files, and its literal carries the "
            + "`// x-release-please-version` annotation the Generic updater rewrites). A disagreement "
            + "means one of:\n"
            + "  · release-please-config.json's `extra-files` no longer names src/BlazorNative.Runtime/Exports.cs;\n"
            + "  · the `// x-release-please-version` annotation was removed from, or moved off, the "
            + "VersionNumber line (the Generic updater is LINE-scoped);\n"
            + "  · someone hand-edited the literal. Do not — it is a mirror, not a source.");
    }

    /// <summary>THE RUNTIME-FRAMEWORK-VERSION DRIFT PIN (Phase 10.1, #122). The
    /// ONE load-bearing version this repo did not guard.
    ///
    /// `RuntimeFrameworkVersion` pins the EXACT NativeAOT runtime pack the bionic
    /// (Android) and iOS publishes compile against — its own csproj comment says it
    /// exists "so a servicing release can't silently change the toolchain under us".
    /// It is the version that makes those NativeAOT builds compile at all, yet it
    /// was hardcoded in THREE csproj spots (the sample's bionic + iOS PropertyGroups
    /// and the template's bionic one) with NO test linking them — while every
    /// cosmetic literal in the tree was pinned.
    ///
    /// This pin PARSES the value out of every occurrence and asserts they AGREE. It
    /// pattern-derives the canonical value from occurrence #1 rather than hardcoding
    /// a second copy of the literal (which would be the very duplication the finding
    /// warns against): a deliberate bump is a one-line edit per file that this test
    /// makes visible, and a bump that misses a file reds here in the required
    /// build-test lane — before a generated or sample app compiles against a runtime
    /// pack the others do not use.
    ///
    /// The occurrences currently AGREE, so this is a GUARD, not a bug-fix; its bite
    /// was proven by mutation at authoring (Phase 10.1 Gate B).</summary>
    [Fact]
    public void EveryRuntimeFrameworkVersion_AgreesAcrossSampleAndTemplate()
    {
        string[] files =
        [
            Path.Combine(RepoRoot(), "samples", "BlazorNative.SampleApp", "BlazorNative.SampleApp.csproj"),
            Path.Combine(RepoRoot(), "templates", "BlazorNative.Templates", "content",
                "BlazorNative.App", "MyBlazorNativeApp.csproj"),
        ];

        var occurrences = new List<(string Where, string Value)>();
        foreach (string file in files)
        {
            Assert.True(File.Exists(file),
                $"{file} not found — this drift pin links the RuntimeFrameworkVersion literals across "
                + "the sample and the template, and it cannot compare a file that is not there. A pin "
                + "that cannot see its subject must never pass vacuously.");

            string text = File.ReadAllText(file);
            var matches = Regex.Matches(text, @"<RuntimeFrameworkVersion>([^<]+)</RuntimeFrameworkVersion>");
            foreach (Match m in matches)
                occurrences.Add(($"{Path.GetFileName(file)} → <RuntimeFrameworkVersion>", m.Groups[1].Value.Trim()));
        }

        // NON-VACUITY: fewer than the three known occurrences means the literal
        // moved or was renamed and this pin stopped seeing its subject — a pin that
        // reads nothing agrees with nothing. Three is the current count (sample ×2,
        // template ×1); a NEW occurrence is welcome (it is one more thing held equal)
        // so the floor is >=3, not ==3.
        Assert.True(occurrences.Count >= 3,
            $"expected at least 3 RuntimeFrameworkVersion occurrences across the sample (×2) and "
            + $"template (×1) csproj, found {occurrences.Count}. The literal moved, was renamed, or a "
            + "PropertyGroup was dropped — re-point this pin deliberately rather than letting it pass "
            + "over a set it can no longer see.\n"
            + (occurrences.Count == 0 ? "(none)" : string.Join("\n", occurrences.Select(o => $"  {o.Where} = \"{o.Value}\""))));

        // Pattern-derive the canonical value from occurrence #1 — never a hardcoded
        // second copy of "10.0.9" (the duplication #122 is about). Everything else
        // must equal it.
        string canonical = occurrences[0].Value;
        var offenders = occurrences.Where(o => o.Value != canonical).ToList();

        Assert.True(offenders.Count == 0,
            "RUNTIMEFRAMEWORKVERSION DRIFT (Phase 10.1, #122). This is the version that pins the exact "
            + "NativeAOT runtime pack the bionic and iOS publishes compile against — the one the "
            + "toolchain-stability comment guards, and the one that makes those builds compile at all. "
            + "Every occurrence across the sample and template csproj MUST be identical, or a generated "
            + "app (or the sample) builds against a runtime pack the others do not use, silently.\n"
            + $"  canonical (occurrence #1): \"{canonical}\"\n"
            + "  offenders:\n"
            + string.Join("\n", offenders.Select(o => $"    {o.Where} = \"{o.Value}\" (must be \"{canonical}\")"))
            + $"\n(Checked {occurrences.Count} occurrences in total. To bump the runtime pack, change "
            + "EVERY occurrence in the same commit.)");
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
