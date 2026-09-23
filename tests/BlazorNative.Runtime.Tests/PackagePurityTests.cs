using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BlazorNative.Tests.Shared;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// PackagePurityTests — Phase 8.0 (design decision 5, M8 DoD #1: "the demo app
// is a consumer, not a tenant" — the CI pin that keeps it true).
//
// THE NORMATIVE RULE: shipped assemblies carry no app types. Three teeth:
//
//   1. THE ROSTER, BOTH DIRECTIONS — the 16 moved types as a literal list,
//      each asserted PRESENT in BlazorNative.SampleApp.dll and ABSENT (by
//      full enumeration) from every shipped assembly. One direction alone is
//      gameable: deleting a type outright would green an absence-only pin;
//      the presence side catches it.
//   2. THE PATTERN NET — zero types matching `.*Demo$ | .*Probe$ |
//      ^SpikeRazor` in any shipped assembly: catches the NEXT demo page
//      someone parks in the library, which the frozen roster cannot.
//   3. THE SHIPPED SET IS PINNED EVERYWHERE IT APPEARS — ShippedAssemblies
//      below (Core, Renderer, Http, Components, Runtime, Analyzers) is the ONE
//      deliberate declaration: a new shipped assembly must join it on purpose,
//      not drift in unexamined. Every other appearance of the set is measured
//      against the checkout's src/ csproj enumeration, so no two copies can
//      disagree:
//        · this literal                          — TheShippedSet_IsExactlyTheSrcCsprojs
//        · consumer-smoke.ps1's $packages        — TheConsumerSmokeScript_...
//        · ConsumerSmoke.csproj's references     — TheConsumerSmokeProject_...
//        · PackageVersionPinTests' walk          — enumerates src/ itself (no copy)
//      Phase 8.1's Gate 1 review (I-2) is why: the set had FOUR copies and only
//      this one was pinned. The failure was concrete — add src/BlazorNative.Seven,
//      tooth 3 reds, a dev adds the name here to green it, and Seven now packs
//      into nothing, smokes in nothing, and has an unguarded version. Three of
//      the four copies are foreign files, parsed out of the checkout: build-test
//      is the one required lane where every file is visible (the drift-test
//      house rule, RouteTableDriftTests' precedent).
//
// Types are enumerated with System.Reflection.Metadata (names off the PE,
// no loading): the Analyzers assembly targets netstandard2.0 and references
// Roslyn, so reflection-loading it here would need its dependency closure
// for nothing — the pin is about NAMES. Nupkg-level purity interrogation is
// 8.1's job by construction (8.1 owns pack; there is no nupkg in 8.0).
// ─────────────────────────────────────────────────────────────────────────────

public sealed class PackagePurityTests
{
    /// <summary>The 16 moved types (Phase 8.0's file-fate table): 9 demo
    /// pages + BnThemedPanel + SpikeRazor + HelloComponent + 4 probes.</summary>
    private static readonly string[] MovedTypeRoster =
    [
        "BnDemo", "BnSettingsPage", "BnLayoutDemo", "BnScrollDemo", "BnImageDemo",
        "BnListDemo", "BnFormDemo", "BnModalDemo", "BnImagePolishDemo",
        "BnThemedPanel", "SpikeRazor",
        "HelloComponent", "CompositionProbe", "FocusProbe", "HostEventProbe", "ClipboardProbe",
    ];

    /// <summary>The shipped set, pinned. These are the assemblies 8.1 packs;
    /// nothing else under src/ may grow a csproj without joining this pin.</summary>
    private static readonly string[] ShippedAssemblies =
    [
        "BlazorNative.Core", "BlazorNative.Renderer", "BlazorNative.Http",
        "BlazorNative.Components", "BlazorNative.Runtime", "BlazorNative.Analyzers",
        // Phase 9.0 (M9 DoD #1): the 7th shipped package — the device-API facades
        // (IGeolocation now; notifications/biometrics/camera in 9.1-9.3).
        "BlazorNative.Device",
        // #25: the 8th, and the first added AGAINST the "no 8th package"
        // precedent — deliberately, and joining this pin is how that is made
        // deliberate rather than accidental. The consumer test harness is a
        // DEV-TIME-ONLY dependency: an app references it from its test project and
        // it must never enter the app's own graph, which is exactly why it is not
        // folded into Core beside DevHostBridge. See the csproj's own note and
        // docs/plans/2026-08-18-consumer-test-harness-design.md §3.
        "BlazorNative.Testing",
    ];

    private const string SampleAppAssembly = "BlazorNative.SampleApp";

    /// <summary>The pattern net: app-shaped names that must never appear in
    /// a shipped assembly, whatever the roster knows about.
    ///
    /// ITS FIXED POINT IS <see cref="TheAppShapedNet_StillCatchesTheSampleApp"/>
    /// (pin standard Rule 3; census item 9). This regex is the detector in an
    /// ABSENCE assertion, so rewording it past its subject — `Demo$` to `Demos$`,
    /// a lost `^`, a stray `\b` of the kind that cost
    /// ComponentReferenceDriftTests a pattern — produces no offenders forever
    /// while claiming the shipped assemblies are clean.</summary>
    private static readonly Regex AppShapedTypeName =
        new("(Demo$)|(Probe$)|(^SpikeRazor)", RegexOptions.CultureInvariant);

    // ── 1. The roster, both directions ───────────────────────────────────────

    [Fact]
    public void TheMovedRoster_IsPresentInTheSampleApp_EveryType()
    {
        HashSet<string> sampleTypes = TypeNamesOf(SampleAppAssembly);
        var missing = MovedTypeRoster.Where(t => !sampleTypes.Contains(t)).ToList();

        Assert.True(missing.Count == 0,
            "The moved-type roster must be PRESENT in BlazorNative.SampleApp.dll — the presence "
            + "side is what stops an absence-only pin from being greened by deleting a type "
            + $"outright. Missing: {string.Join(", ", missing)}");
    }

    [Fact]
    public void TheMovedRoster_IsAbsentFromEveryShippedAssembly()
    {
        var offenders = new List<string>();
        foreach (string assembly in ShippedAssemblies)
        {
            HashSet<string> types = TypeNamesOf(assembly);
            offenders.AddRange(MovedTypeRoster
                .Where(types.Contains)
                .Select(t => $"{t} (in {assembly})"));
        }

        Assert.True(offenders.Count == 0,
            "Shipped assemblies carry no app types (Phase 8.0's normative rule) — these moved to "
            + $"samples/BlazorNative.SampleApp and must stay there: {string.Join(", ", offenders)}");
    }

    // ── 2. The pattern net ───────────────────────────────────────────────────

    [Fact]
    public void NoAppShapedTypeName_InAnyShippedAssembly()
    {
        var offenders = new List<string>();
        foreach (string assembly in ShippedAssemblies)
        {
            offenders.AddRange(TypeNamesOf(assembly)
                .Where(t => AppShapedTypeName.IsMatch(t))
                .Select(t => $"{t} (in {assembly})"));
        }

        Assert.True(offenders.Count == 0,
            "The pattern net (*Demo / *Probe / SpikeRazor*) caught an app-shaped type in a "
            + $"shipped assembly: {string.Join(", ", offenders)}. Demo pages and probes live in "
            + "samples/BlazorNative.SampleApp — the library ships no app types.");
    }

    /// <summary>
    /// THE POSITIVE CONTROL FOR <see cref="AppShapedTypeName"/> (pin standard Rule 3;
    /// census item 9, one of the two uncontrolled detectors that sat OUTSIDE the pin
    /// population — this is a purity fact over PE metadata, not a drift pin, and it is
    /// closed anyway because an uncontrolled detector is a liability in either
    /// population).
    ///
    /// THE ANCHOR IS THE EXEMPTION, which is the cheapest model available and the one
    /// NSLogDriftTests uses: the assembly the net deliberately does NOT scan is the
    /// assembly that must still be full of what the net looks for.
    /// `BlazorNative.SampleApp.dll` is where all sixteen moved types live, by the
    /// normative rule this file exists to enforce — so it is guaranteed, not merely
    /// likely, to hold a match for every alternation.
    ///
    /// ONE NAME PER ALTERNATION, each asserted to be a REAL type in that assembly
    /// first. Naming a string literal and matching it would prove only that the regex
    /// matches a string someone typed beside it; requiring the name to exist in the PE
    /// metadata makes it a tree anchor. Reword any single alternation and exactly one
    /// row here reds, naming which.
    ///
    /// THE NEGATIVE HALF guards the other direction. The net's failure mode is not
    /// only under-matching: dropping the `$` anchors would flag `DemoRecorder`, and
    /// dropping `^` would flag `NotSpikeRazor`. A net widened that way reds on
    /// innocent shipped types, which is a false red — cheap, but it is also how an
    /// author is taught to weaken the net. The near-misses below are the shapes the
    /// anchors exist to refuse.
    ///
    /// THE COUPLING closes the gap between the anchor rows and the net they anchor.
    /// The rows below are a hand-written list; nothing used to tie it to the pattern,
    /// so a FOURTH alternation shipped with no fixed point and no red — the control
    /// kept passing while covering three of four, which is the exact defect class
    /// phase 15.1 closed everywhere else. The alternations are now derived from the
    /// LIVE regex object by splitting its source at top-level `|` (see
    /// <see cref="TopLevelAlternationsOf"/>) and asserted set-equal to the rows.
    /// Never a second hand-written copy of the pattern string: that would be a new
    /// divergence inside the milestone about divergence.
    ///
    /// WHAT THE COUPLING BUYS, precisely: COMPLETENESS OVER THE PATTERN AS WRITTEN.
    /// Every alternation that exists has an anchor, and every anchor names a live
    /// alternation. It is a different property from the per-row assertions, which
    /// prove each anchored alternation still hits its subject, and from the WIDTH
    /// limit below, which it does not touch. It is deliberately sensitive to the
    /// pattern's SPELLING — the rows quote each alternation verbatim, capture parens
    /// included — so a cosmetic rewrite reds too. That is a false red, which costs a
    /// re-quote; the alternative direction costs a false green, which is the thing
    /// this phase exists to remove.
    ///
    /// WHAT THIS DOES NOT BUY: the net's WIDTH. `*Demo`, `*Probe` and `SpikeRazor*`
    /// are the three shapes someone thought of in phase 8.0. A demo page called
    /// `BnSandbox` parked in a shipped assembly matches nothing here and nothing in
    /// the roster either, and no control over a fixed pattern list can see it. The
    /// coupling above cannot reach this either: it proves every alternation that
    /// EXISTS is anchored, never that the alternation you need exists at all. That
    /// gap FAILS GREEN and is stated rather than implied.
    /// </summary>
    [Fact]
    public void TheAppShapedNet_StillCatchesTheSampleApp()
    {
        HashSet<string> sampleTypes = TypeNamesOf(SampleAppAssembly);

        (string Name, string Alternation)[] anchors =
        [
            ("BnDemo", "(Demo$)"),
            ("CompositionProbe", "(Probe$)"),
            ("SpikeRazor", "(^SpikeRazor)"),
        ];

        foreach (var (name, alternation) in anchors)
        {
            Assert.True(sampleTypes.Contains(name),
                $"'{name}' is no longer a type in {SampleAppAssembly}.dll, so it cannot anchor the "
                + $"`{alternation}` alternation of the app-shaped net. Either the type was renamed "
                + "— then re-point this control at whatever now plays its part, and check "
                + "MovedTypeRoster in the same pass — or it was deleted, in which case this "
                + "alternation may have no live subject left and the question is whether the "
                + "alternation should survive it. Do not drop the row to make this green.");

            Assert.True(AppShapedTypeName.IsMatch(name),
                $"AppShapedTypeName no longer matches '{name}', a real type in "
                + $"{SampleAppAssembly}.dll and this control's anchor for the `{alternation}` "
                + "alternation. The net has been reworded past its subject, so "
                + "NoAppShapedTypeName_InAnyShippedAssembly is now reporting zero offenders "
                + "because it can no longer SEE one — not because the shipped assemblies are "
                + $"clean. Current pattern: /{AppShapedTypeName}/");
        }

        // THE NEGATIVE: the anchors are load-bearing. Each of these contains an
        // alternation's text in a position the anchor must refuse; a net widened by
        // dropping `$` or `^` flags all three and reds on innocent shipped names.
        foreach (string nearMiss in new[] { "DemoRecorder", "ProbeStore", "NotSpikeRazor", "BnView" })
            Assert.False(AppShapedTypeName.IsMatch(nearMiss),
                $"AppShapedTypeName now matches '{nearMiss}', which is NOT an app-shaped name — "
                + "`Demo` and `Probe` are anchored at the END and `SpikeRazor` at the START on "
                + "purpose. A net that matches anywhere reds on ordinary library types, and the "
                + $"next author will weaken it rather than fix it. Current pattern: /{AppShapedTypeName}/");

        // THE COUPLING: set equality between the rows above and the net's own
        // alternations. Runs LAST on purpose — a reworded alternation must red in the
        // per-row loop, where the message names the subject it stopped matching.
        List<string> alternations = TopLevelAlternationsOf(AppShapedTypeName);
        List<string> unanchored = alternations.Except(anchors.Select(a => a.Alternation)).ToList();
        List<string> orphaned = anchors.Select(a => a.Alternation).Except(alternations).ToList();

        Assert.True(unanchored.Count == 0 && orphaned.Count == 0,
            "The anchor rows are no longer set-equal to the alternations of "
            + $"AppShapedTypeName.\n    pattern:    /{AppShapedTypeName}/\n"
            + $"    unanchored: {Describe(unanchored)}  <- alternations with NO anchor row\n"
            + $"    orphaned:   {Describe(orphaned)}  <- anchor rows naming no live alternation\n"
            + "An UNANCHORED alternation is what this assertion exists for: it ships with no "
            + "fixed point, so the loop above keeps passing while covering only the alternations "
            + "someone remembered, and NoAppShapedTypeName_InAnyShippedAssembly reports zero "
            + "offenders for a shape nothing has ever proved the net can SEE. Add a row naming a "
            + $"REAL type in {SampleAppAssembly}.dll that the new alternation matches; if no such "
            + "type exists then the alternation has no live subject, and that is the finding "
            + "rather than a reason to skip the row.\n"
            + "An ORPHANED row means the pattern lost an alternation or was respelled. The rows "
            + "quote each alternation VERBATIM, capture parens included, so even a cosmetic "
            + "rewrite lands here — re-quote the row to match the pattern's new spelling. "
            + "Deleting the row, or deleting this assertion, puts the net back to being "
            + "uncontrolled, which is the state census item 9 recorded.");
    }

    private static string Describe(IReadOnlyCollection<string> parts)
        => parts.Count == 0 ? "(none)" : string.Join(" ", parts.Select(p => $"/{p}/"));

    /// <summary>The alternations of a regex, split at its TOP-LEVEL `|` and returned
    /// VERBATIM — parens, anchors and all. The source comes off the LIVE
    /// <see cref="Regex"/> object, so no copy of the pattern string exists for this to
    /// drift from. Depth- and class-aware: a `|` inside a group or a character class
    /// belongs to one alternation and is not a separator.</summary>
    private static List<string> TopLevelAlternationsOf(Regex regex)
    {
        string source = regex.ToString();
        var parts = new List<string>();
        int depth = 0, start = 0;
        bool inClass = false;

        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '\\') { i++; continue; }            // escaped — consume the pair
            if (inClass) { inClass = c != ']'; continue; }

            switch (c)
            {
                case '[': inClass = true; break;
                case '(': depth++; break;
                case ')': depth--; break;
                case '|' when depth == 0:
                    parts.Add(source[start..i]);
                    start = i + 1;
                    break;
            }
        }

        parts.Add(source[start..]);

        Assert.True(parts.All(p => p.Length > 0),
            $"splitting /{source}/ at its top-level `|` produced an EMPTY alternation, so the "
            + "split no longer describes the pattern it was handed and the set equality built on "
            + "it would be comparing against nonsense. Either the pattern grew a shape this "
            + "splitter does not model — a leading, trailing or doubled `|` — or the splitter is "
            + "wrong. Fix whichever it is; do not let the coupling pass over a bad parse.");
        return parts;
    }

    // ── 3. The shipped set is pinned EVERYWHERE it appears ───────────────────

    [Fact]
    public void TheShippedSet_IsExactlyTheSrcCsprojs()
    {
        Assert.Equal(
            ShippedAssemblies.OrderBy(n => n, StringComparer.Ordinal),
            SrcCsprojNames());
    }

    /// <summary>THE SCRIPT'S COPY. consumer-smoke.ps1's `$packages` is the list
    /// the smoke PACKS, interrogates, and asserts provenance for — a seventh
    /// shipped project missing from it is simply never packed and never smoked,
    /// and nothing else notices. Parsed out of the checkout as text (the
    /// RouteTableDriftTests rule: build-test is the one required lane where
    /// every file is checkout-visible; the script is not a build input of any
    /// project, so text is the only handle). The declaration is anchored at line
    /// start so the comment ABOVE it — which discusses `$packages` by name —
    /// cannot be mistaken for the list itself.</summary>
    [Fact]
    public void TheConsumerSmokeScript_PacksExactlyTheShippedSet()
    {
        const string script = "scripts/consumer-smoke.ps1";
        string source = ReadCheckoutFile(script);

        Match match = Regex.Match(source, @"(?m)^\$packages\s*=\s*@\((?<body>[^)]*)\)");
        Assert.True(match.Success,
            $"could not find the `$packages = @(...)` declaration in {script}. It moved or was "
            + "rewritten — this pin IS the contract that the smoke packs the shipped set, so "
            + "re-point it deliberately rather than deleting it.");

        var packages = Regex.Matches(match.Groups["body"].Value, @"""(?<name>[^""]+)""")
            .Select(m => "BlazorNative." + m.Groups["name"].Value)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        Assert.True(packages.Count > 0,
            $"parsed ZERO package names out of {script}'s `$packages` declaration — a pin that "
            + "cannot see its subject must never pass vacuously.");

        Assert.Equal(ShippedAssemblies.OrderBy(n => n, StringComparer.Ordinal), packages);
    }

    /// <summary>THE CONSUMER'S COPY. ConsumerSmoke.csproj's PackageReferences
    /// are what the blank out-of-repo consumer actually restores from the six
    /// packages — a shipped package missing from it is packed but never proven
    /// consumable, which is the whole point of the smoke. Read from the checkout
    /// (the project is deliberately outside the solution, so it is a FILE here,
    /// never a reference) and parsed as XML rather than by regex: it is
    /// structured, and XDocument is what PackageVersionPinTests already uses on
    /// csprojs — the "as text" of the drift-test rule is about the LANE, not
    /// about refusing a real parser.</summary>
    [Fact]
    public void TheConsumerSmokeProject_ReferencesExactlyTheShippedSet()
    {
        const string project = "samples/ConsumerSmoke/ConsumerSmoke.csproj";
        string file = CheckoutPath(project);
        Assert.True(File.Exists(file), $"consumer smoke project not found: {file}");

        var references = XDocument.Load(file).Root!
            .Elements("ItemGroup").Elements("PackageReference")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(id => id is not null && id.StartsWith("BlazorNative.", StringComparison.Ordinal))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        Assert.True(references.Count > 0,
            $"parsed ZERO BlazorNative.* PackageReferences out of {project} — a pin that cannot "
            + "see its subject must never pass vacuously.");

        Assert.Equal(ShippedAssemblies.OrderBy(n => n, StringComparer.Ordinal), references!);
    }

    /// <summary>The shipped set as the CHECKOUT declares it — src/'s csproj
    /// names, the one enumeration all four copies are measured against.
    /// Non-vacuity asserted: an enumeration that finds nothing would green every
    /// caller above, which is TypeNamesOf's rule applied to the filesystem.</summary>
    private static List<string> SrcCsprojNames()
    {
        string src = Path.Combine(BnRepo.Root(), "src");
        var names = Directory.EnumerateFiles(src, "*.csproj", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(names.Count > 0,
            $"enumerated ZERO csprojs under {src} — the shipped-set pins would all pass over an "
            + "empty set. Fix the enumeration; do not let it green vacuously.");
        return names!;
    }

    private static string CheckoutPath(string relativePath)
        => Path.Combine(BnRepo.Root(), relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string ReadCheckoutFile(string relativePath)
    {
        string file = CheckoutPath(relativePath);
        Assert.True(File.Exists(file), $"checkout file not found: {file}");
        return File.ReadAllText(file);
    }

    // ── PE type enumeration (names off the metadata, no loading) ─────────────

    /// <summary>Every type definition's simple name in the assembly —
    /// including nested types, so a demo cannot hide inside a helper. Fails
    /// loudly when the dll cannot be found: a pin that cannot see its subject
    /// must never pass vacuously.</summary>
    private static HashSet<string> TypeNamesOf(string assemblyName)
    {
        string path = ResolveAssemblyPath(assemblyName);
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader metadata = pe.GetMetadataReader();

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
        {
            names.Add(metadata.GetString(metadata.GetTypeDefinition(handle).Name));
        }

        // The walk's own floor. Every caller above is an assertion of the form "for
        // every type name, assert X" — the shape that passes trivially over an empty
        // set (pin standard Rule 2). An assembly always defines <Module>, so zero is
        // never a legitimate answer here; it means the PE was read but yielded
        // nothing, and the absence facts would go green over it.
        Assert.True(names.Count > 0,
            $"enumerated ZERO type definitions out of {path} — the purity facts would all pass "
            + "vacuously over an empty set. The PE was opened but yielded no metadata; fix the "
            + "read, do not let it green.");
        return names;
    }

    /// <summary>Referenced assemblies (the five runtime-shaped shipped ones +
    /// the sample app) sit in the test output directory; the analyzer is not
    /// a runtime reference, so it is read from its own build output in the
    /// checkout, same configuration as this test build. Phase 8.1 (the 8.0
    /// review's M-3): the TFM segment is READ FROM THE CSPROJ it already
    /// knows how to find — the old hardcoded "netstandard2.0" meant an
    /// Analyzers TFM move would red as a path miss, not as the right test.</summary>
    private static string ResolveAssemblyPath(string assemblyName)
    {
        string baseDirectory = BnRepo.TestBinaryDirectory();
        string local = Path.Combine(baseDirectory, assemblyName + ".dll");
        if (File.Exists(local))
            return local;

        string configuration = baseDirectory.Contains(
            Path.DirectorySeparatorChar + "Debug" + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase) ? "Debug" : "Release";
        string csproj = Path.Combine(BnRepo.Root(), "src", assemblyName, assemblyName + ".csproj");
        string? tfm = XDocument.Load(csproj).Root!
            .Elements("PropertyGroup").Elements("TargetFramework")
            .Select(e => e.Value)
            .SingleOrDefault();
        Assert.False(string.IsNullOrEmpty(tfm),
            $"could not read a single <TargetFramework> from {csproj} — the purity pin "
            + "resolves checkout build output by the csproj's OWN TFM (8.0 review M-3).");
        string built = Path.Combine(
            BnRepo.Root(), "src", assemblyName, "bin", configuration, tfm!,
            assemblyName + ".dll");
        Assert.True(File.Exists(built),
            $"could not resolve {assemblyName}.dll — looked in the test output "
            + $"({local}) and the checkout build output ({built}). The purity pin must "
            + "SEE every shipped assembly; fix the path, do not skip the assembly.");
        return built;
    }
}
