using System.Xml.Linq;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// PackageVersionPinTests — Phase 8.1 (design decision 4, M8 DoD #2: the
// versioning scheme decided and recorded).
//
// THE NORMATIVE RULE: ONE version truth — the single <Version> element in
// src/Directory.Build.props. The 4.5 shape (a phase-stamped literal repeated
// per-csproj, 7 files of churn per bump) is dead; drift BACK to per-csproj
// literals must be a red, not a review catch. Two teeth:
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
