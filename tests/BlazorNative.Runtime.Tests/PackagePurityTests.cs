using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using System.Xml.Linq;

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
    ];

    private const string SampleAppAssembly = "BlazorNative.SampleApp";

    /// <summary>The pattern net: app-shaped names that must never appear in
    /// a shipped assembly, whatever the roster knows about.</summary>
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
        string src = Path.Combine(RepoRoot(), "src");
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
        => Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

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
        string local = Path.Combine(AppContext.BaseDirectory, assemblyName + ".dll");
        if (File.Exists(local))
            return local;

        string configuration = AppContext.BaseDirectory.Contains(
            Path.DirectorySeparatorChar + "Debug" + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase) ? "Debug" : "Release";
        string csproj = Path.Combine(RepoRoot(), "src", assemblyName, assemblyName + ".csproj");
        string? tfm = XDocument.Load(csproj).Root!
            .Elements("PropertyGroup").Elements("TargetFramework")
            .Select(e => e.Value)
            .SingleOrDefault();
        Assert.False(string.IsNullOrEmpty(tfm),
            $"could not read a single <TargetFramework> from {csproj} — the purity pin "
            + "resolves checkout build output by the csproj's OWN TFM (8.0 review M-3).");
        string built = Path.Combine(
            RepoRoot(), "src", assemblyName, "bin", configuration, tfm!,
            assemblyName + ".dll");
        Assert.True(File.Exists(built),
            $"could not resolve {assemblyName}.dll — looked in the test output "
            + $"({local}) and the checkout build output ({built}). The purity pin must "
            + "SEE every shipped assembly; fix the path, do not skip the assembly.");
        return built;
    }

    /// <summary>The repo root — the nearest ancestor holding BlazorNative.sln
    /// (RouteTableDriftTests' rule: build-test is the one required lane where
    /// the whole checkout is visible).</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BlazorNative.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, "BlazorNative.sln not found above " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
