using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace BlazorNative.Analyzers.Tests;

/// <summary>
/// THE PIN THIS PACKAGE ACTUALLY NEEDS — Phase 11.3 Gate B, criterion A2.
///
/// The other six shipped packages got a <c>PublicAPI.Shipped.txt</c> baseline at
/// Gate B. BlazorNative.Analyzers deliberately did NOT, and the reason is the
/// whole point of this file:
///
///   * A consumer CANNOT REFERENCE THIS ASSEMBLY. It ships as an analyzer asset
///     under <c>analyzers/dotnet/cs</c>, targets netstandard2.0, and its Roslyn
///     dependency is <c>PrivateAssets="all"</c>
///     (BlazorNative.Analyzers.csproj:48-:49). A consuming project receives
///     DIAGNOSTICS, never a compile-time reference to MobilePolicyAnalyzer.
///   * ITS REAL CONTRACT IS THE DIAGNOSTIC IDs. <c>BN0011</c> is what a consumer
///     types into a NoWarn, an .editorconfig severity or a <c>#pragma warning
///     disable</c>. Renaming the ID breaks their build; renaming the C# class it
///     lives in breaks nothing.
///   * A <c>PublicAPI.Shipped.txt</c> CANNOT EXPRESS THAT. It would faithfully
///     record three class names and some DiagnosticDescriptor fields — none of
///     which is the thing that must not change. A pin that guards the wrong noun
///     is worse than no pin: it manufactures confidence.
///
/// So the substitute pin guards the IDs, in BOTH directions: an ID that appears
/// without being declared in the release ledger reds, and a ledger line whose ID
/// no longer ships reds. Neither side is a hand-copied roster — one is reflected
/// out of the assembly, the other is parsed out of AnalyzerReleases.Shipped.md,
/// which the RS2xxx release-tracking rules already force to be accurate at build
/// time. The only literal in this file is the COUNT, and it is deliberate: see
/// <see cref="TheShippedRoster_IsExactlySeven_AndReusesNoRetiredId"/>.
/// </summary>
public sealed class AnalyzerDiagnosticRosterTests
{
    /// <summary>The seven IDs Phase 11.3 Gate A recorded as this package's real
    /// contract. Written down ONCE, as a count and a printed list in a failure
    /// message — never as the expected set of an assertion, which is what would
    /// turn this file into the roster it exists to replace.</summary>
    private const int ExpectedShippedRuleCount = 7;

    // ── side A: what the assembly actually ships ────────────────────────────

    /// <summary>Every rule this package really ships, reflected: instantiate each
    /// DiagnosticAnalyzer in the assembly and read SupportedDiagnostics. A new
    /// analyzer class or a new rule is picked up with no edit here.</summary>
    private static SortedSet<string> ReflectedIds()
        => new(
            typeof(MobilePolicyAnalyzer).Assembly
                .GetTypes()
                .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t))
                .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
                .SelectMany(a => a.SupportedDiagnostics)
                .Select(d => d.Id),
            StringComparer.Ordinal);

    // ── side B: what the release ledger says ships ──────────────────────────

    private const string ShippedLedgerPath = "src/BlazorNative.Analyzers/AnalyzerReleases.Shipped.md";
    private const string UnshippedLedgerPath = "src/BlazorNative.Analyzers/AnalyzerReleases.Unshipped.md";

    /// <summary>
    /// The live rule set according to the release ledger: every ID ever announced
    /// under a "New Rules" table, minus every ID retired under a "Removed Rules"
    /// table, plus anything queued in the Unshipped ledger. "Changed Rules" moves
    /// a rule's category or severity and is deliberately ignored — it neither
    /// adds nor removes an ID.
    /// </summary>
    private static (SortedSet<string> Live, int NewCount, int RemovedCount) LedgerIds()
    {
        var added = new SortedSet<string>(StringComparer.Ordinal);
        var removed = new SortedSet<string>(StringComparer.Ordinal);

        foreach (string path in new[] { ShippedLedgerPath, UnshippedLedgerPath })
        {
            string? section = null;
            foreach (string raw in ReadCheckoutFile(path).Split('\n'))
            {
                string line = raw.TrimEnd('\r').Trim();

                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    // "### New Rules" / "### Removed Rules" / "### Changed Rules",
                    // and "## Release x.y.z" which resets to no section.
                    string heading = line.TrimStart('#').Trim();
                    section = heading switch
                    {
                        "New Rules"     => "new",
                        "Removed Rules" => "removed",
                        "Changed Rules" => "changed",
                        _               => null,
                    };
                    continue;
                }

                // A rule row is `BN0011 | Category | Severity | Notes`. The header
                // row (`Rule ID | ...`) and its dashes do not match.
                Match m = Regex.Match(line, @"^(BN\d{4})\s*\|");
                if (!m.Success) continue;

                if (section == "new") added.Add(m.Groups[1].Value);
                else if (section == "removed") removed.Add(m.Groups[1].Value);
            }
        }

        var live = new SortedSet<string>(added, StringComparer.Ordinal);
        live.ExceptWith(removed);
        return (live, added.Count, removed.Count);
    }

    // ── the pin ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The shipped diagnostic IDs are exactly the ledger's live set — BOTH
    /// DIRECTIONS. An ID added to the assembly without a ledger line reds; a
    /// ledger line whose ID no longer ships reds. Neither side is transcribed.
    /// </summary>
    [Fact]
    public void ShippedDiagnosticIds_MatchTheReleaseLedger_InBothDirections()
    {
        var reflected = ReflectedIds();
        (SortedSet<string> ledger, int newCount, int removedCount) = LedgerIds();

        // NON-VACUITY, THREE WAYS. A reflection call that returns nothing, a
        // parser that ate the file, or a parser that never found the "Removed
        // Rules" section would each make the compare below pass over a fiction —
        // the exact shape of the defect this pin exists to prevent.
        Assert.True(reflected.Count > 0,
            "reflected ZERO DiagnosticDescriptors out of BlazorNative.Analyzers — the pin has no "
            + "subject and must never pass vacuously.");
        Assert.True(newCount > 0,
            $"parsed ZERO `New Rules` rows out of {ShippedLedgerPath} — the ledger parser ate the "
            + "file, and every assertion below would compare against an empty set.");
        Assert.True(removedCount > 0,
            $"parsed ZERO `Removed Rules` rows out of {ShippedLedgerPath} — Release 4.1.0 retired "
            + "six WASI-era IDs, so a parser that finds none is not reading the section it must "
            + "subtract, and the ledger side would over-count by exactly those six.");

        var shippedNotLedgered = new SortedSet<string>(reflected, StringComparer.Ordinal);
        shippedNotLedgered.ExceptWith(ledger);
        var ledgeredNotShipped = new SortedSet<string>(ledger, StringComparer.Ordinal);
        ledgeredNotShipped.ExceptWith(reflected);

        Assert.True(shippedNotLedgered.Count == 0 && ledgeredNotShipped.Count == 0,
            "ANALYZER DIAGNOSTIC-ID ROSTER DRIFT (Phase 11.3 Gate B, criterion A2).\n\n"
            + $"  shipped (reflected off the assembly): {string.Join(", ", reflected)}\n"
            + $"  live per {ShippedLedgerPath}: {string.Join(", ", ledger)}\n\n"
            + (shippedNotLedgered.Count > 0
                ? $"  ADDED WITHOUT A LEDGER LINE: {string.Join(", ", shippedNotLedgered)}\n"
                  + "    A new BN rule is new public contract — a consumer will see it in their\n"
                  + "    build and need somewhere to look it up. Announce it under a `### New\n"
                  + "    Rules` table in AnalyzerReleases.Unshipped.md.\n"
                : "")
            + (ledgeredNotShipped.Count > 0
                ? $"  LEDGERED BUT NO LONGER SHIPPED: {string.Join(", ", ledgeredNotShipped)}\n"
                  + "    Deleting a rule silently un-breaks every consumer's `#pragma warning\n"
                  + "    disable` and NoWarn for it — their suppression now names an ID nothing\n"
                  + "    emits, so the suppression is dead and the code it guarded is unguarded.\n"
                  + "    Retire it under a `### Removed Rules` table; IDs are never reused.\n"
                : "")
            + "\nTHE BN00xx IDs ARE THIS PACKAGE'S PUBLIC API. It is the one shipped package with\n"
            + "no PublicAPI.Shipped.txt, because a consumer cannot reference the assembly — they\n"
            + "only ever type the IDs. This test is that baseline.");
    }

    /// <summary>
    /// The roster is seven rules, and no retired ID has been reused.
    ///
    /// The test above is fully derived, which leaves one gap it cannot see: an
    /// author who adds a rule AND its ledger line in the same commit satisfies
    /// both sides and moves the contract silently. This is the deliberate
    /// acknowledgement line for that — the count is the only literal in the file,
    /// and changing it is the author saying out loud that the consumer-facing
    /// roster grew.
    ///
    /// The second half is not a formality. AnalyzerReleases.Shipped.md retired
    /// six IDs at Release 4.1.0 with "ID retired, never reused" written against
    /// each. Reusing one would point a consumer's existing suppression at a rule
    /// that means something entirely different.
    /// </summary>
    [Fact]
    public void TheShippedRoster_IsExactlySeven_AndReusesNoRetiredId()
    {
        var reflected = ReflectedIds();

        Assert.True(reflected.Count == ExpectedShippedRuleCount,
            $"BlazorNative.Analyzers ships {reflected.Count} diagnostic ID(s); the recorded roster "
            + $"is {ExpectedShippedRuleCount}.\n\n"
            + $"  shipped: {string.Join(", ", reflected)}\n\n"
            + "  Phase 11.3 Gate A recorded seven — BN0004, BN0010, BN0011, BN0013, BN0014,\n"
            + "  BN0020, BN0021 — as this package's entire consumer contract (the tier table,\n"
            + "  docs/plans/2026-07-21-phase-11.3-api-tiers.md §3.7). If the roster genuinely\n"
            + "  grew or shrank, update this count IN THE SAME COMMIT as the rule and say so in\n"
            + "  the changelog. That edit is the point: it is the acknowledgement a fully\n"
            + "  derived pin cannot demand.");

        // Every ID the ledger has ever retired. Derived, not listed: a seventh
        // retirement tomorrow is covered with no edit here.
        var retired = new SortedSet<string>(StringComparer.Ordinal);
        string? section = null;
        foreach (string raw in ReadCheckoutFile(ShippedLedgerPath).Split('\n'))
        {
            string line = raw.TrimEnd('\r').Trim();
            if (line.StartsWith("#", StringComparison.Ordinal))
            {
                section = line.TrimStart('#').Trim() == "Removed Rules" ? "removed" : null;
                continue;
            }
            if (section != "removed") continue;
            Match m = Regex.Match(line, @"^(BN\d{4})\s*\|");
            if (m.Success) retired.Add(m.Groups[1].Value);
        }

        Assert.True(retired.Count > 0,
            $"parsed ZERO retired IDs out of {ShippedLedgerPath} — Release 4.1.0 retired six, so "
            + "this half of the pin is looking at nothing.");

        var reused = new SortedSet<string>(reflected, StringComparer.Ordinal);
        reused.IntersectWith(retired);

        Assert.True(reused.Count == 0,
            $"RETIRED ANALYZER ID REUSED: {string.Join(", ", reused)}.\n\n"
            + $"  retired per {ShippedLedgerPath}: {string.Join(", ", retired)}\n\n"
            + "  AnalyzerReleases.Shipped.md writes \"ID retired, never reused\" against each of\n"
            + "  these. A consumer who suppressed the old rule still carries that suppression;\n"
            + "  reusing the ID silently applies it to a rule that means something else.");
    }

    // ── the checkout ────────────────────────────────────────────────────────

    private static string ReadCheckoutFile(string relativePath)
    {
        string file = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(file), $"checkout file not found: {file}");
        return File.ReadAllText(file);
    }

    /// <summary>The repo root — the nearest ancestor holding BlazorNative.sln
    /// (the drift-test house rule: build-test is the one required lane where the
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
