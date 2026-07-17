using System.Text.RegularExpressions;
using BlazorNative.SampleApp;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// RouteTableDriftTests — Phase 7.6 (design decision 1, M7 DoD #8: the
// route-registry unification's PIN). Phase 8.0 retargeted the manifest
// expressions to the app's `SampleAppPages.All` — the registration inversion
// moved the ONE declaration to the app, and the drift-test discipline follows
// the manifest's new owner (DoD #1's own demand). Everything else in this
// file survives verbatim: same lane, same pair pin, same default-fallback
// pin, same nine-page content baseline.
//
// A page is declared ONCE — one row in the app's manifest. `HostSession`'s
// mount registry and `NativeNavigationManager`'s route table are DERIVED
// VIEWS of that array (same object graph — they cannot drift). Android's
// `MainActivity.DEEP_LINK_COMPONENTS` is the one surviving PINNED MIRROR: it
// is consulted at Intent-parse time, BEFORE the .so is loaded (the 5.1
// structural record), so it must be a hand-written copy — and a hand-written
// copy that drifts (`"/form"` → the wrong page) fails no compile, no test, no
// lane: a deep link quietly opens the wrong screen on ONE platform. That is
// the exact silent-cross-shell-drift class the style tables closed in 6.1,
// and this file is the same fix: the mirror is parsed out of the checkout as
// TEXT and compared in `build-test`, the one required lane where every file
// is checkout-visible (ShellStyleTableDriftTests' own rationale — neither
// shell's lane can see the other's source; build-test sees them all). Drift
// fails the required lane in the commit that causes it.
//
// PAIRS, NOT NAME SETS: for routes the drift that matters is a route mapped
// to the WRONG page, which set-equality cannot see — the pin compares
// route → component pair-for-pair, and its failure message names the
// offending pairs in both directions.
//
// iOS has NO route surface at all — `BnRuntime.start(component:)` mounts by
// NAME (verified in the 7.5 Gate 3 report). The day it grows a deep-link
// story, it gains a mirror AND a [Fact] here — a decision, not an accident.
//
// Parser rules (the style-table parser's, reused): the declaration is
// anchored at line start so a comment that MENTIONS the map cannot match,
// and a parse that finds zero pairs FAILS the test — never vacuously green.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class RouteTableDriftTests
{
    private const string KotlinMainActivity =
        "src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/MainActivity.kt";

    /// <summary>The Kotlin declaration — anchored at line start, so the KDoc
    /// above it (which discusses the map at length) cannot be mistaken for
    /// the table itself.</summary>
    private const string KotlinDeepLinkDeclaration =
        @"(?m)^\s*private val DEEP_LINK_COMPONENTS = mapOf\((?<body>[^)]*)\)";

    /// <summary>The Kotlin default-fallback literal — anchored to the elvis
    /// chain that CONSUMES the map (`?: deepLinkRoute?.let { DEEP_LINK_COMPONENTS[it] }`
    /// followed by `?: "…"`), so no other string literal in the file can
    /// match. This is the one row the map deliberately does not carry ("/" is
    /// the no-deep-link default), pinned where it actually lives.</summary>
    private const string KotlinDefaultFallbackDeclaration =
        @"DEEP_LINK_COMPONENTS\[it\] \}\s*\?\:\s*""(?<name>[^""]+)""";

    /// <summary>The manifest's routed rows, minus the default route — exactly
    /// what the Kotlin map is supposed to mirror ("/" rides the separate
    /// `?: "BnDemo"` fallback, pinned below).</summary>
    private static Dictionary<string, string> ExpectedDeepLinkPairs()
        => SampleAppPages.All
            .Where(p => p.Route is not null && p.Route != BlazorNativeApp.DefaultRoute)
            .ToDictionary(p => p.Route!, p => p.Name, StringComparer.Ordinal);

    /// <summary>THE PAIR PIN. Android's deep-link map must be exactly the
    /// manifest's routed rows minus "/", PAIR-FOR-PAIR — a route pointing at
    /// the WRONG page is drift set-equality cannot see, and it is the drift
    /// that actually hurts: the deep link opens the wrong screen, on one
    /// platform, silently.</summary>
    [Fact]
    public void AndroidDeepLinkMap_IsTheManifestsRoutedRowsMinusDefault_PairForPair()
    {
        Dictionary<string, string> kotlin =
            ParsePairTable(ReadShellSource(KotlinMainActivity), KotlinDeepLinkDeclaration);
        Dictionary<string, string> expected = ExpectedDeepLinkPairs();

        var onlyInManifest = expected.Where(e => !kotlin.ContainsKey(e.Key)).ToList();
        var onlyInKotlin = kotlin.Where(k => !expected.ContainsKey(k.Key)).ToList();
        var wrongComponent = expected
            .Where(e => kotlin.TryGetValue(e.Key, out string? actual) && actual != e.Value)
            .Select(e => (Route: e.Key, Manifest: e.Value, Kotlin: kotlin[e.Key]))
            .ToList();

        Assert.True(
            onlyInManifest.Count == 0 && onlyInKotlin.Count == 0 && wrongComponent.Count == 0,
            "MainActivity.kt's DEEP_LINK_COMPONENTS must mirror SampleAppPages' routed rows "
            + "(minus \"/\", which rides the ?: fallback) PAIR-FOR-PAIR.\n"
            + $"  only in the manifest (route missing from Kotlin): {JoinPairs(onlyInManifest)}\n"
            + $"  only in Kotlin (route the manifest does not know): {JoinPairs(onlyInKotlin)}\n"
            + "  route mapped to the WRONG page: "
            + (wrongComponent.Count == 0
                ? "(none)"
                : string.Join(", ", wrongComponent.Select(w =>
                    $"\"{w.Route}\" → manifest says \"{w.Manifest}\", Kotlin says \"{w.Kotlin}\"")))
            + "\nThe map is consulted at Intent-parse time, before the .so loads — it MUST be a "
            + "hand-written copy, and a copy that drifts opens the WRONG SCREEN from a deep link "
            + "on Android alone, silently. Fix the pair in the same commit.");
    }

    /// <summary>THE DEFAULT-FALLBACK PIN. The one routed row the Kotlin map
    /// does not carry: no deep link (or an unknown one) mounts the `?:`
    /// literal, which must be the manifest's default row's component. If the
    /// default page is ever renamed on one side only, Android boots into the
    /// wrong app — this is that drift, caught in the commit that causes it.</summary>
    [Fact]
    public void AndroidDefaultFallbackLiteral_IsTheManifestsDefaultComponent()
    {
        string fallback = ParseDefaultFallback(ReadShellSource(KotlinMainActivity));

        Assert.True(
            fallback == PageManifest.DefaultComponent,
            $"MainActivity.kt's deep-link fallback literal (?: \"{fallback}\") must be the "
            + $"manifest's default component (\"{PageManifest.DefaultComponent}\" — the \"/\" "
            + "row). It is the one pair DEEP_LINK_COMPONENTS deliberately omits, so the pair "
            + "pin above cannot see it drift; this pin can. Change both in the same commit.");
    }

    /// <summary>THE CONTENT PIN — the routed-page ordered baseline, retargeted
    /// from the retired NavigationTests tautology to the manifest itself
    /// (Phase 7.6). A drift test comparing two surfaces is blind to a row
    /// deleted from BOTH: this literal catches a routed page silently
    /// vanishing from the SOURCE. (+BnListDemo, Phase 7.2 — "/list";
    /// +BnFormDemo, Phase 7.3 — "/form"; +BnModalDemo, Phase 7.4 — "/modal";
    /// +BnImagePolishDemo, Phase 7.5 — "/imagepolish";
    /// +BnGeolocationDemo, Phase 9.0 — "/geolocation";
    /// +BnNotificationsDemo, Phase 9.1 — "/notifications".)</summary>
    [Fact]
    public void ManifestRoutedRows_MatchTheElevenPageBaseline()
    {
        Assert.Equal(
            ["BnDemo", "BnFormDemo", "BnGeolocationDemo", "BnImageDemo", "BnImagePolishDemo",
             "BnLayoutDemo", "BnListDemo", "BnModalDemo", "BnNotificationsDemo", "BnScrollDemo",
             "BnSettingsPage"],
            SampleAppPages.All
                .Where(p => p.Route is not null)
                .Select(p => p.Name)
                .OrderBy(n => n, StringComparer.Ordinal));
    }

    // ── The parser ───────────────────────────────────────────────────────────

    /// <summary>Every `"route" to "name"` pair inside the declaration
    /// <paramref name="pattern"/>'s body. Fails loudly when the declaration
    /// cannot be found OR parses to zero pairs: a moved or emptied table must
    /// break this test, never silently pass it.</summary>
    private static Dictionary<string, string> ParsePairTable(string source, string pattern)
    {
        Match match = Regex.Match(source, pattern, RegexOptions.Singleline);
        Assert.True(match.Success,
            $"could not find DEEP_LINK_COMPONENTS in {KotlinMainActivity} (pattern: {pattern}). "
            + "It moved or was renamed — this drift test IS the contract, so re-point it "
            + "deliberately rather than deleting it.");

        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match pair in Regex.Matches(
            match.Groups["body"].Value, @"""(?<route>[^""]+)""\s+to\s+""(?<name>[^""]+)"""))
        {
            pairs[pair.Groups["route"].Value] = pair.Groups["name"].Value;
        }

        Assert.NotEmpty(pairs);
        return pairs;
    }

    /// <summary>The `?: "…"` literal at the end of the map-consuming elvis
    /// chain. Same loud-failure rule: a rewritten resolution chain must
    /// break this test, not skip it.</summary>
    private static string ParseDefaultFallback(string source)
    {
        Match match = Regex.Match(source, KotlinDefaultFallbackDeclaration, RegexOptions.Singleline);
        Assert.True(match.Success,
            $"could not find the deep-link default fallback (?: \"…\") in {KotlinMainActivity} "
            + $"(pattern: {KotlinDefaultFallbackDeclaration}). The componentName resolution chain "
            + "moved or was rewritten — re-point this pin deliberately rather than deleting it.");
        return match.Groups["name"].Value;
    }

    private static string ReadShellSource(string relativePath)
    {
        string file = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(file), $"shell source not found: {file}");
        return File.ReadAllText(file);
    }

    /// <summary>The repo root — the nearest ancestor of the test binary holding
    /// BlazorNative.sln. The shell's source is not a build input of this project,
    /// so it is read from the checkout (which is what makes `build-test` the only
    /// lane that can host this test — ShellStyleTableDriftTests' rule).</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BlazorNative.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, "BlazorNative.sln not found above " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string JoinPairs(IEnumerable<KeyValuePair<string, string>> pairs)
    {
        var list = pairs
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"\"{p.Key}\" → \"{p.Value}\"")
            .ToList();
        return list.Count == 0 ? "(none)" : string.Join(", ", list);
    }
}
