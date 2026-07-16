using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

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
//   3. THE SHIPPED SET IS ITSELF A PINNED LITERAL — Core, Renderer, Http,
//      Components, Runtime, Analyzers — so a new shipped assembly must join
//      the pin deliberately, not drift in unexamined. Enumerated from the
//      checkout's src/ csprojs (build-test is the one required lane where
//      every file is checkout-visible — the drift-test house rule).
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

    // ── 3. The shipped set is itself pinned ──────────────────────────────────

    [Fact]
    public void TheShippedSet_IsExactlyTheSrcCsprojs()
    {
        string src = Path.Combine(RepoRoot(), "src");
        var actual = Directory.EnumerateFiles(src, "*.csproj", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            ShippedAssemblies.OrderBy(n => n, StringComparer.Ordinal),
            actual!);
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
    /// the sample app) sit in the test output directory; the netstandard2.0
    /// analyzer is not a runtime reference, so it is read from its own build
    /// output in the checkout, same configuration as this test build.</summary>
    private static string ResolveAssemblyPath(string assemblyName)
    {
        string local = Path.Combine(AppContext.BaseDirectory, assemblyName + ".dll");
        if (File.Exists(local))
            return local;

        string configuration = AppContext.BaseDirectory.Contains(
            Path.DirectorySeparatorChar + "Debug" + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase) ? "Debug" : "Release";
        string built = Path.Combine(
            RepoRoot(), "src", assemblyName, "bin", configuration, "netstandard2.0",
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
