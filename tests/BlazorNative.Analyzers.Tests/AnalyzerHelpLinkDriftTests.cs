using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace BlazorNative.Analyzers.Tests;

/// <summary>
/// THE ONE LINK CLASS NO BUILD CAN SEE — Phase 8.4 Gate 4 (review S2-2).
///
/// Every DiagnosticDescriptor here carries a `helpLinkUri` into the nupkg, and a
/// consumer's IDE turns it into the "BN0004" hyperlink in the error list. That
/// link is:
///
///   * ABSOLUTE and EXTERNAL — https://marcelroozekrans.github.io/BlazorNative/...
///     so `onBrokenLinks: 'throw'` and `onBrokenAnchors: 'throw'` cannot reach
///     it. Docusaurus checks the links the SITE contains, not the links pointed
///     AT it from somewhere else.
///   * SHIPPED, not served — it is baked into a released package. A wrong one
///     cannot be fixed by redeploying the site; it is a permanent 404 for
///     everyone who installed that version.
///   * INVISIBLE to every other gate — no test read these strings before this
///     file existed (`grep -rln helpLinkUri tests/` returned nothing), and the
///     site does not know they exist.
///
/// Gate 1 caught one of these by hand and prevented exactly that permanent 404.
/// Nothing kept it prevented. This does.
///
/// Both sides are derived: the subjects are reflected out of the analyzer
/// assembly, the anchors are parsed out of the page that must answer them, and
/// the base is read from the site's own `baseUrl`. There is no roster.
/// </summary>
public sealed class AnalyzerHelpLinkDriftTests
{
    /// <summary>Every rule this package actually ships, reflected — instantiate
    /// each DiagnosticAnalyzer in the assembly and read SupportedDiagnostics. A
    /// new analyzer or a new rule is picked up with no edit here, which is what
    /// keeps this from becoming the roster it is meant to replace.</summary>
    private static List<DiagnosticDescriptor> ShippedDescriptors()
        => typeof(MobilePolicyAnalyzer).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t))
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .SelectMany(a => a.SupportedDiagnostics)
            .GroupBy(d => d.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(d => d.Id, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Every shipped rule's `helpLinkUri` anchor resolves to a real heading on
    /// the analyzers page.
    ///
    /// This is the half that rots quietly: the URL stays 200 forever because the
    /// PAGE exists — the browser simply lands at the top of a long document
    /// instead of at the reader's rule. A consumer clicking "BN0013" gets
    /// "BlazorNative Analyzer Rules" and a scroll bar.
    /// </summary>
    [Fact]
    public void EveryHelpLink_ResolvesToARealHeading_OnTheAnalyzersPage()
    {
        var descriptors = ShippedDescriptors();

        // NON-VACUITY, BOTH SIDES. Reflection returning nothing, or a heading
        // parser that silently matches nothing, would make every loop below
        // green over an empty set — the exact shape of the defect this pin is
        // for.
        Assert.True(descriptors.Count > 0,
            "reflected ZERO DiagnosticDescriptors out of BlazorNative.Analyzers — the pin has no "
            + "subject. A pin that cannot see its subject must never pass vacuously.");

        var anchors = AnchorsOnTheAnalyzersPage();
        Assert.True(anchors.Count > 5,
            $"parsed only {anchors.Count} headings out of {AnalyzersPageRelativePath} — the parser "
            + "ate the page, and every assertion below would pass over nothing.");

        var offenders = new List<string>();
        foreach (var d in descriptors)
        {
            string uri = d.HelpLinkUri ?? "";

            // A rule with NO help link is a finding, not an exemption: the IDE
            // shows an un-clickable ID and the reader has nowhere to go.
            if (string.IsNullOrWhiteSpace(uri))
            {
                offenders.Add($"    {d.Id} — ships NO helpLinkUri at all (the IDE renders a dead ID)");
                continue;
            }

            int hash = uri.IndexOf('#');
            if (hash < 0)
            {
                offenders.Add($"    {d.Id} — helpLinkUri has no #anchor: {uri}");
                continue;
            }

            string anchor = uri[(hash + 1)..];
            if (!anchors.Contains(anchor))
                offenders.Add($"    {d.Id} — #{anchor} matches no heading on the page: {uri}");
        }

        Assert.True(offenders.Count == 0,
            $"{offenders.Count} of {descriptors.Count} shipped analyzer help link(s) point at an "
            + "anchor that does not exist.\n\n"
            + string.Join("\n", offenders)
            + $"\n\nHeadings the page actually offers ({anchors.Count}):\n"
            + string.Join("\n", anchors.OrderBy(a => a, StringComparer.Ordinal).Select(a => "    #" + a))
            + "\n\nTHESE LINKS SHIP INSIDE THE NUPKG. `onBrokenAnchors: 'throw'` cannot see them — it "
            + "checks the links the site CONTAINS, and these are absolute URLs pointed at it from a "
            + "released package. Fix the cref or add the heading; a published version's help link is "
            + "not fixable by redeploying the site.");
    }

    /// <summary>
    /// Every shipped `helpLinkUri` is built on the site's own `url` + `baseUrl`.
    ///
    /// U2's blast radius, from the other side. `baseUrl` is one string in
    /// docusaurus.config.js; these seven URLs are copies of it inside a package.
    /// Move the site and nothing local reds — the pages 301 or 404 depending on
    /// how it moved, and the only witness is a consumer clicking a link in an
    /// IDE months later.
    /// </summary>
    [Fact]
    public void EveryHelpLink_IsBuiltOnTheSitesOwnBaseUrl()
    {
        var descriptors = ShippedDescriptors();
        Assert.True(descriptors.Count > 0, "reflected ZERO DiagnosticDescriptors — vacuous.");

        string config = ReadCheckoutFile("website/docusaurus.config.js");
        string url = Single(config, @"^\s*url:\s*'([^']+)'", "url");
        string baseUrl = Single(config, @"^\s*baseUrl:\s*'([^']+)'", "baseUrl");
        string routeBasePath = Single(config, @"routeBasePath:\s*'([^']+)'", "routeBasePath");

        // The page's own route, and EVERY PART OF IT IS DERIVED. url + baseUrl +
        // routeBasePath come from the config; the last segment is the markdown
        // file's own name, and reading the file is what proves it is there. Not
        // one segment of the address these links must match is transcribed here —
        // if any of the four moves, this pin moves with it and the seven packaged
        // strings are what red.
        string page = Path.GetFileNameWithoutExtension(AnalyzersPageRelativePath);
        ReadCheckoutFile(AnalyzersPageRelativePath);
        string expectedBase =
            $"{url.TrimEnd('/')}/{baseUrl.Trim('/')}/{routeBasePath.Trim('/')}/{page}";

        var offenders = descriptors
            .Where(d => !(d.HelpLinkUri ?? "").StartsWith(expectedBase + "#", StringComparison.Ordinal))
            .Select(d => $"    {d.Id} — {d.HelpLinkUri}")
            .ToList();

        Assert.True(offenders.Count == 0,
            $"{offenders.Count} of {descriptors.Count} shipped analyzer help link(s) are NOT built on "
            + $"this site's url + baseUrl.\n\n  Expected every link to start with:\n    {expectedBase}#\n\n"
            + string.Join("\n", offenders)
            + "\n\n  (url and baseUrl read from website/docusaurus.config.js — the one home. If the "
            + "site MOVED, these seven strings are copies of the old address that ship inside a "
            + "nupkg, and no site build can see them.)");
    }

    // ── the page ─────────────────────────────────────────────────────────────

    /// <summary>The page these links must answer to. Its route is not written
    /// down anywhere here — it is composed from the site's `routeBasePath` and
    /// this file's own name.</summary>
    private const string AnalyzersPageRelativePath = "website/docs/analyzers.md";

    /// <summary>The anchors Docusaurus will actually mint for this page: one per
    /// heading, github-slugger's rules (lowercase, punctuation dropped, spaces to
    /// dashes), with an explicit `{#id}` winning when present.</summary>
    private static HashSet<string> AnchorsOnTheAnalyzersPage()
    {
        var anchors = new HashSet<string>(StringComparer.Ordinal);
        bool inFence = false;

        foreach (string raw in ReadCheckoutFile(AnalyzersPageRelativePath).Split('\n'))
        {
            string line = raw.TrimEnd('\r');

            // FENCES MATTER, and this is not hypothetical: this very page opens
            // with `#pragma warning disable BN0004` inside a code block. A naive
            // /^#/ parser reads that as a heading and mints an anchor for a line
            // of C#.
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence) continue;

            Match m = Regex.Match(line, @"^(#{1,6})\s+(.+?)\s*$");
            if (m.Success) anchors.Add(Slug(m.Groups[2].Value));
        }

        return anchors;
    }

    private static string Slug(string heading)
    {
        Match explicitId = Regex.Match(heading, @"\{#([^}]+)\}\s*$");
        if (explicitId.Success) return explicitId.Groups[1].Value;

        string s = heading.ToLowerInvariant();
        s = Regex.Replace(s, @"[`*_\[\]()]", "");      // inline markdown
        s = Regex.Replace(s, @"[^a-z0-9 \-]", "");     // punctuation
        return s.Trim().Replace(' ', '-');
    }

    private static string Single(string text, string pattern, string what)
    {
        Match m = Regex.Match(text, pattern, RegexOptions.Multiline);
        Assert.True(m.Success, $"could not read `{what}` out of website/docusaurus.config.js");
        return m.Groups[1].Value;
    }

    private static string ReadCheckoutFile(string relativePath)
    {
        string file = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(file), $"checkout file not found: {file}");
        return File.ReadAllText(file);
    }

    /// <summary>The repo root — the nearest ancestor holding BlazorNative.sln
    /// (RouteTableDriftTests' rule: build-test is the one required lane where the
    /// whole checkout is visible).</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BlazorNative.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, "BlazorNative.sln not found above " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
