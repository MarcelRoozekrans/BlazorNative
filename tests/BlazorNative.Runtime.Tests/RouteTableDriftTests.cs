using System.Text.RegularExpressions;
using BlazorNative.RouteGen;
using BlazorNative.SampleApp;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// RouteTableDriftTests — Phase 7.6 (M7 DoD #8: the route-registry unification's
// PIN), retargeted by Phase 8.0 to the app's manifest, and RESHAPED by Phase 11.0
// (M11 DoD #1: deep-link route codegen).
//
// WHAT CHANGED AT 11.0. Android's `MainActivity.DEEP_LINK_COMPONENTS` used to be a
// HAND-WRITTEN mapOf(route → component), consulted at Intent-parse time before the
// .so loads — the one surviving PINNED MIRROR of the manifest's routed rows, and a
// consumer FOOTGUN: an app author who added a routed page and forgot to hand-edit
// the map got a deep link that silently opened the wrong screen. 11.0 replaced the
// hand-written map with a BUILD-TIME-GENERATED Android resource
// (res/raw/blazornative_routes.json): BlazorNative.RouteGen parses the app's SOURCE
// for BlazorNativePage.Routed<T>(route, name) registrations and emits them;
// MainActivity parses THAT at Intent-parse time. There is no longer any
// hand-written Kotlin map to drift, so the pair-for-pair Kotlin-text pin is RETIRED
// — drift is now impossible by construction (one generator, one resource, both
// shells read it). Its replacement guards the GENERATOR instead:
//
//   · GeneratedRoutesJson_ReproducesTheManifestsRoutedRows_PairForPair runs the
//     REAL RouteGen extractor against the REAL SampleApp SOURCE files, serializes
//     the result exactly as the build target does, parses it back the way
//     MainActivity does, and holds it PAIR-FOR-PAIR against SampleAppPages.All's
//     routed rows (the "/" default row included). It is NOT vacuous: the extractor
//     reads the manifest by PARSING the app's .cs source (Roslyn) — loading no
//     assembly — while the expected side reads the in-process SampleAppPages.All
//     (the compiled objects) directly. Two independent derivations of the one
//     source: a generator bug (a dropped row, a mangled name, a lost default)
//     reddens it.
//
//   GATE-A PIVOT: the extractor was assembly-loading (reflect the framework
//   registry off the built dll); that could not survive CI, because a per-RID
//   linux-bionic-arm64 managed dll cannot load into the x64 build host. It now
//   reads SOURCE — arch/RID-independent — so this test drives it against the
//   SampleApp source tree, not typeof(SampleAppPages).Assembly.Location.
//
// KEPT VERBATIM:
//   · AndroidDefaultFallbackLiteral_… — the resource carries the "/" default now,
//     but MainActivity keeps a hard-coded `?: "…"` ULTIMATE fallback for a missing
//     or malformed resource. That literal must still name the manifest's default
//     component, so a renamed default page cannot boot a resource-less app into the
//     wrong screen. Pinned from checkout TEXT, the 7.6 way.
//   · ManifestRoutedRows_MatchTheThirteenPageBaseline — the content pin. A drift
//     test comparing two surfaces is blind to a row deleted from BOTH; this literal
//     catches a routed page silently vanishing from the SOURCE.
//
// iOS has NO route surface at all — BnRuntime.start(component:) mounts by NAME
// (the 7.5 Gate 3 record). The day it grows a deep-link story it gains its own
// resource + a [Fact] here — a decision, not an accident.
//
// SERIALIZED in the "host-session" collection, and it MUST be. AndroidDefaultFallback…
// reads the LIVE global PageManifest.DefaultComponent; RegistrationTests (also
// "host-session") transiently registers a probe as the "/" default and restores
// "BnDemo" in a finally — without the shared collection this test would race that
// mutation and read the probe name, a nondeterministic red the strict count gate
// turns into a failed release PR. (GeneratedRoutesJson… reads its own isolated ALC,
// untouched by that mutation, but the collection costs nothing and keeps the file's
// rule uniform.)
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class RouteTableDriftTests
{
    private const string KotlinMainActivity =
        "src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/MainActivity.kt";

    /// <summary>The Kotlin ULTIMATE fallback literal — anchored to the elvis chain
    /// that consumes the generated map (`?: routes["/"]` followed by `?: "…"`), so
    /// no other string literal in the file can match. This is the last-ditch
    /// default the shell mounts when the generated resource is missing/malformed.</summary>
    private const string KotlinDefaultFallbackDeclaration =
        @"\?\:\s*routes\[""/""\]\s*\r?\n\s*\?\:\s*""(?<name>[^""]+)""";

    /// <summary>The manifest's routed rows — every page with a Route (the "/"
    /// default INCLUDED, because the generated resource carries it too). This is
    /// exactly what the generated JSON is supposed to be.</summary>
    private static Dictionary<string, string> ExpectedRoutedRows()
        => SampleAppPages.All
            .Where(p => p.Route is not null)
            .ToDictionary(p => p.Route!, p => p.Name, StringComparer.Ordinal);

    /// <summary>THE GENERATOR PIN (Phase 11.0). The build-time codegen's output must
    /// reproduce the manifest's routed rows PAIR-FOR-PAIR — a route pointing at the
    /// WRONG page is the drift that actually hurts (a deep link opens the wrong
    /// screen), and set-equality cannot see it. This runs the REAL extractor against
    /// the REAL SampleApp SOURCE files (Roslyn, no assembly load — the Gate-A pivot),
    /// round-trips the JSON exactly as the build target + MainActivity do, and
    /// compares to SampleAppPages.All independently.</summary>
    [Fact]
    public void GeneratedRoutesJson_ReproducesTheManifestsRoutedRows_PairForPair()
    {
        // 1) run the actual generator against the actual SampleApp SOURCE tree
        //    (the same @(Compile) the build target feeds it) — no assembly loaded.
        IReadOnlyList<RoutedPage> routed = RouteManifest.Extract(SampleAppSourceFiles());

        // 2) serialize the way the build target does, and parse it back the way
        //    MainActivity does (a flat JSON object) — so this exercises the emitted
        //    RESOURCE FORMAT, not just the in-memory list.
        string json = RouteManifest.ToJson(routed);
        Dictionary<string, string> generated = ParseFlatJsonObject(json);

        Dictionary<string, string> expected = ExpectedRoutedRows();

        var onlyInManifest = expected.Where(e => !generated.ContainsKey(e.Key)).ToList();
        var onlyInGenerated = generated.Where(g => !expected.ContainsKey(g.Key)).ToList();
        var wrongComponent = expected
            .Where(e => generated.TryGetValue(e.Key, out string? actual) && actual != e.Value)
            .Select(e => (Route: e.Key, Manifest: e.Value, Generated: generated[e.Key]))
            .ToList();

        Assert.True(
            onlyInManifest.Count == 0 && onlyInGenerated.Count == 0 && wrongComponent.Count == 0,
            "The GENERATED deep-link map (res/raw/blazornative_routes.json, produced by "
            + "BlazorNative.RouteGen) must reproduce SampleAppPages' routed rows (the \"/\" default "
            + "included) PAIR-FOR-PAIR — this is the pin that the codegen replaced the hand-written "
            + "Kotlin mirror with.\n"
            + $"  only in the manifest (route missing from the generated JSON): {JoinPairs(onlyInManifest)}\n"
            + $"  only in the generated JSON (route the manifest does not know): {JoinPairs(onlyInGenerated)}\n"
            + "  route mapped to the WRONG page: "
            + (wrongComponent.Count == 0
                ? "(none)"
                : string.Join(", ", wrongComponent.Select(w =>
                    $"\"{w.Route}\" → manifest says \"{w.Manifest}\", generated says \"{w.Generated}\"")))
            + "\nThe generator reads the framework registry directly, so a mismatch here is a bug in "
            + "RouteManifest.Extract/ToJson — the map is no longer hand-maintained, so fix the "
            + "generator, not a copy.");

        // NON-VACUITY: a generator that emitted nothing would compare two empty
        // reads (expected is never empty) — but guard the round-trip explicitly.
        Assert.NotEmpty(generated);
    }

    /// <summary>THE DEFAULT-FALLBACK PIN. The generated resource now carries the "/"
    /// row, but MainActivity keeps a hard-coded `?: "…"` ULTIMATE fallback for a
    /// missing/malformed resource. That literal must be the manifest's default
    /// component — otherwise a resource-less boot (or a renamed default) mounts a
    /// name nothing registers. Pinned from checkout text, as before.</summary>
    [Fact]
    public void AndroidDefaultFallbackLiteral_IsTheManifestsDefaultComponent()
    {
        string fallback = ParseDefaultFallback(ReadShellSource(KotlinMainActivity));

        Assert.True(
            fallback == PageManifest.DefaultComponent,
            $"MainActivity.kt's ultimate deep-link fallback literal (?: \"{fallback}\") must be the "
            + $"manifest's default component (\"{PageManifest.DefaultComponent}\" — the \"/\" row). "
            + "It is the last-ditch default the shell mounts when the generated "
            + "res/raw/blazornative_routes.json is missing or malformed; if it named an unregistered "
            + "page, a resource-less boot would fail with rc 1 at first mount. Change both in the "
            + "same commit.");
    }

    /// <summary>THE CONTENT PIN — the routed-page ordered baseline. A drift test
    /// comparing two surfaces is blind to a row deleted from BOTH: this literal
    /// catches a routed page silently vanishing from the SOURCE. (+BnListDemo,
    /// Phase 7.2 — "/list"; +BnFormDemo, Phase 7.3 — "/form"; +BnModalDemo,
    /// Phase 7.4 — "/modal"; +BnImagePolishDemo, Phase 7.5 — "/imagepolish";
    /// +BnGeolocationDemo, Phase 9.0 — "/geolocation";
    /// +BnNotificationsDemo, Phase 9.1 — "/notifications";
    /// +BnSecureDemo, Phase 9.2 — "/secure";
    /// +BnCameraDemo, Phase 9.3 — "/camera".)</summary>
    [Fact]
    public void ManifestRoutedRows_MatchTheThirteenPageBaseline()
    {
        Assert.Equal(
            ["BnCameraDemo", "BnDemo", "BnFormDemo", "BnGeolocationDemo", "BnImageDemo",
             "BnImagePolishDemo", "BnLayoutDemo", "BnListDemo", "BnModalDemo",
             "BnNotificationsDemo", "BnScrollDemo", "BnSecureDemo", "BnSettingsPage"],
            SampleAppPages.All
                .Where(p => p.Route is not null)
                .Select(p => p.Name)
                .OrderBy(n => n, StringComparer.Ordinal));
    }

    // ── Parsers ────────────────────────────────────────────────────────────────

    /// <summary>Parses a flat one-level JSON object (`{ "k": "v", ... }`) the way
    /// MainActivity's org.json.JSONObject read does. Fails loudly on an empty parse:
    /// a generator that emitted an empty object must red, never pass vacuously.</summary>
    private static Dictionary<string, string> ParseFlatJsonObject(string json)
    {
        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(json, @"""(?<k>(?:[^""\\]|\\.)*)""\s*:\s*""(?<v>(?:[^""\\]|\\.)*)"""))
            pairs[Regex.Unescape(m.Groups["k"].Value)] = Regex.Unescape(m.Groups["v"].Value);
        return pairs;
    }

    /// <summary>The `?: "…"` literal at the end of the map-consuming elvis chain.
    /// A rewritten resolution chain must break this test, not skip it.</summary>
    private static string ParseDefaultFallback(string source)
    {
        Match match = Regex.Match(source, KotlinDefaultFallbackDeclaration, RegexOptions.Singleline);
        Assert.True(match.Success,
            $"could not find the ultimate deep-link fallback (?: routes[\"/\"] ?: \"…\") in "
            + $"{KotlinMainActivity} (pattern: {KotlinDefaultFallbackDeclaration}). The componentName "
            + "resolution chain moved or was rewritten — re-point this pin deliberately rather than "
            + "deleting it.");
        return match.Groups["name"].Value;
    }

    /// <summary>The SampleApp's C# source files — the app's manifest lives in
    /// SampleAppPages.cs, but the whole tree is passed (excluding obj/bin) to
    /// mirror the build target's @(Compile) input. This is the INDEPENDENT read:
    /// the generator parses these files, while the expected side reads the
    /// compiled SampleAppPages.All in-process.</summary>
    private static string[] SampleAppSourceFiles()
    {
        string projectDir = Path.Combine(RepoRoot(), "samples", "BlazorNative.SampleApp");
        Assert.True(Directory.Exists(projectDir), $"SampleApp project dir not found: {projectDir}");
        return Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsUnderIntermediateDir(f))
            .ToArray();
    }

    /// <summary>Excludes obj/ and bin/ so the generator sees the hand-written
    /// source, never a build-intermediate copy.</summary>
    private static bool IsUnderIntermediateDir(string path)
    {
        string p = path.Replace('\\', '/');
        return p.Contains("/obj/", StringComparison.Ordinal)
            || p.Contains("/bin/", StringComparison.Ordinal);
    }

    private static string ReadShellSource(string relativePath)
    {
        string file = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(file), $"shell source not found: {file}");
        return File.ReadAllText(file);
    }

    /// <summary>The repo root — the nearest ancestor of the test binary holding
    /// BlazorNative.sln.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BlazorNative.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, "BlazorNative.sln not found above " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    // ── #212: a duplicate route is refused at BUILD time ─────────────────────

    /// <summary>
    /// Two rows sharing a route must fail the GENERATOR, not the device.
    ///
    /// Before this, the duplicate was appended and ToJson emitted both, producing a JSON
    /// object with duplicate keys. Android's <c>loadDeepLinkRoutes</c> parses with
    /// <c>org.json.JSONObject</c>, which throws on duplicate keys, so the catch returned
    /// an empty map and EVERY deep link fell back to the default component — while
    /// RouteGen exited 0 and the build stayed green.
    ///
    /// <para><c>PageManifest.Validate</c> also rejects duplicate routes at startup, which
    /// is why this is not severe. It is not the answer either: the point of a build-time
    /// generator is to fail with the file in front of you, not on a device with a stack
    /// trace that names JSON parsing.</para>
    ///
    /// <para>Written against a TEMP source file rather than the sample app's manifest,
    /// because the sample must stay valid — a fixture that made the real app illegal would
    /// red every other test in this file.</para>
    /// </summary>
    [Fact]
    public void TwoPagesSharingARoute_AreRefusedByTheGenerator_NamingBoth()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bn-routegen-dupe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "Pages.cs");
        try
        {
            File.WriteAllText(file, """
                public static class Pages
                {
                    public static readonly object[] All =
                    [
                        BlazorNativePage.Routed<Home>("/", "Home"),
                        BlazorNativePage.Routed<Settings>("/settings", "Settings"),
                        BlazorNativePage.Routed<Imposter>("/settings", "Imposter"),
                    ];
                }
                """);

            var ex = Assert.Throws<DuplicateRouteException>(
                () => RouteManifest.Extract([file]));

            // The message must name the ROUTE and BOTH pages: a refusal that says only
            // "duplicate route" sends you hunting through a manifest for the pair.
            Assert.Contains("/settings", ex.Message, StringComparison.Ordinal);
            Assert.Contains("Settings", ex.Message, StringComparison.Ordinal);
            Assert.Contains("Imposter", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { /* temp dir */ }
        }
    }

    /// <summary>
    /// NON-VACUITY: the same fixture WITHOUT the collision extracts cleanly. Without this,
    /// the test above would pass if <c>Extract</c> threw on every input — the fixture being
    /// rejected for some unrelated parse reason would read as success.
    /// </summary>
    [Fact]
    public void TheDuplicateRouteFixture_IsOtherwiseValid()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bn-routegen-ok-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "Pages.cs");
        try
        {
            File.WriteAllText(file, """
                public static class Pages
                {
                    public static readonly object[] All =
                    [
                        BlazorNativePage.Routed<Home>("/", "Home"),
                        BlazorNativePage.Routed<Settings>("/settings", "Settings"),
                        BlazorNativePage.Routed<Imposter>("/other", "Imposter"),
                    ];
                }
                """);

            IReadOnlyList<RoutedPage> routed = RouteManifest.Extract([file]);

            Assert.Equal(3, routed.Count);
            Assert.Equal(["/", "/settings", "/other"], routed.Select(r => r.Route));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { /* temp dir */ }
        }
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
