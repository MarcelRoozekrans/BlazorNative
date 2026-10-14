using System.Text.RegularExpressions;
using BlazorNative.Tests.Shared;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// JdkVersionDriftTests — CI and local setup install one JDK (#404).
//
// Renovate moved CI's java-version from 21 to 25 in #326. The step kept the
// name "Setup JDK 21", and setup.ps1 kept installing 21, so local and CI
// builds ran different JDKs and nothing compared them. The home is CI's
// java-version, because Renovate maintains it. Every other site must equal it:
//   - each `java-version:` in .github/workflows;
//   - each `Setup JDK <n>` step name there;
//   - setup.ps1's `$jdkMajor = <n>`;
//   - setup.sh's `jdk_major=<n>`.
//
// WHAT THIS DOES NOT COVER (Rule 5):
//   - Whether the scripts USE the variable. A script that installs a literal
//     version elsewhere passes here; the single variable is a convention.
//   - scripts/devloop.ps1 and scripts/test-android.ps1, which look for the
//     newest Temurin 25 or 21 on disk rather than install one.
//   - A java-version written as a list or an expression, not a quoted or bare
//     number. It is not matched, so the floor reds if none is left.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Every JDK version CI provisions, names or tells local setup to install is the same one.</summary>
public sealed class JdkVersionDriftTests
{
    private static readonly Regex JavaVersion =
        new(@"^\s*java-version:\s*'?(\d+)'?\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex SetupStepName =
        new(@"^\s*-\s*name:\s*Setup JDK (\d+)\b", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex Ps1Major =
        new(@"^\$jdkMajor\s*=\s*(\d+)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex ShMajor =
        new(@"^jdk_major=(\d+)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>The workflows' java-version values, the Setup JDK step names, and both setup scripts agree.</summary>
    [Fact]
    public void CiAndBothSetupScripts_NameOneJdk()
    {
        var root = BnRepo.Root();
        var sites = new List<(string Site, string Version)>();

        foreach (var wf in Directory.EnumerateFiles(Path.Combine(root, ".github", "workflows"), "*.yml").Order(StringComparer.Ordinal))
        {
            var text = File.ReadAllText(wf);
            var name = ".github/workflows/" + Path.GetFileName(wf);
            sites.AddRange(JavaVersion.Matches(text).Select(m => ($"{name} java-version", m.Groups[1].Value)));
            sites.AddRange(SetupStepName.Matches(text).Select(m => ($"{name} step name", m.Groups[1].Value)));
        }

        var ciVersions = sites.Where(s => s.Site.EndsWith(" java-version", StringComparison.Ordinal)).ToList();
        Assert.True(ciVersions.Count > 0,
            "no `java-version:` found in .github/workflows. The pin's home is gone, so it would compare nothing.");

        foreach (var (file, regex) in new[] { ("setup.ps1", Ps1Major), ("setup.sh", ShMajor) })
        {
            var found = regex.Matches(File.ReadAllText(Path.Combine(root, file)));
            Assert.True(found.Count == 1,
                $"{file} declares the JDK major {found.Count} times, expected exactly once as {regex}.");
            sites.Add((file, found[0].Groups[1].Value));
        }

        var home = ciVersions[0].Version;
        var disagreeing = sites.Where(s => s.Version != home).ToList();
        Assert.True(disagreeing.Count == 0,
            $"{ciVersions[0].Site} provisions JDK {home}, but {string.Join(", ", disagreeing.Select(s => $"{s.Site} says {s.Version}"))}. "
            + "Local setup installs the JDK CI builds on (#404); move every site together.");
    }
}
