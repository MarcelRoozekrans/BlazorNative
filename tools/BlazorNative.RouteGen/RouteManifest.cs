using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;

namespace BlazorNative.RouteGen;

// ─────────────────────────────────────────────────────────────────────────────
// RouteManifest — Phase 11.0 (M11 DoD #1). The extraction + serialization behind
// the deep-link route codegen. Reads the app's ACTUAL registered routed pages by
// loading its assembly and consulting the FRAMEWORK registry — general by
// construction (it never names SampleAppPages).
//
// SINGLE-INSTANCE HAZARD (the reason for the isolated ALC): the app's
// [ModuleInitializer] calls BlazorNativeApp.RegisterPages, which writes the
// process-wide PageManifest store and throws on a second call. The drift test
// already has SampleApp registered in its own default context, and a build node
// is reused across builds — so Extract loads the app into a PRIVATE, collectible
// AssemblyLoadContext (AppLoadContext) and reads THAT context's PageManifest via
// reflection. Its statics are isolated from any other registration, and the
// context unloads when Extract returns.
//
// WHY Load() IS OVERRIDDEN, not just Resolving. A custom ALC that only handles the
// Resolving EVENT does NOT isolate: the event fires only AFTER the runtime's
// default-context fallback, so any assembly the DEFAULT context already has (the
// test host references BlazorNative.Runtime + Microsoft.AspNetCore.Components) is
// UNIFIED into this context — and the app's module initializer then re-registers
// into the DEFAULT PageManifest, which throws "registered twice". Overriding
// Load() runs BEFORE the default fallback, so the app + its package assemblies are
// force-loaded into THIS context (a private BlazorNative.Runtime with fresh
// statics). Only true framework assemblies (System.*, the shared framework) fall
// through to Load()==null → the default context, where sharing them is correct.
//
// DEPENDENCY RESOLUTION (no FrameworkReference): the consumer app is a Library
// (OutputType=Library) with no runtimeconfig.json, so AssemblyDependencyResolver
// cannot locate the NuGet root. We resolve deps ourselves, in order:
//   1) the app's own bin dir (BlazorNative.* project outputs, and — in a
//      copy-local host like the test output — every package asset too),
//   2) the app's .deps.json → the NuGet global-packages folder (package assets a
//      Library build leaves in the cache rather than copying to bin).
// Proven in the Gate A spike against both shapes.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One routed page: the deep-link route and the mount-registry
/// component name it resolves to.</summary>
public readonly record struct RoutedPage(string Route, string Name);

public static class RouteManifest
{
    /// <summary>Loads the built app assembly at <paramref name="appAssemblyPath"/>
    /// into an isolated, collectible context, triggers its module initializer
    /// (RegisterPages), and returns the framework registry's routed rows — every
    /// page with a non-null Route, in manifest order (the "/" default row
    /// included). Throws if the app registered no routed rows, or if the
    /// framework registry could not be read (a shape the generator must never
    /// emit silently).</summary>
    public static IReadOnlyList<RoutedPage> Extract(string appAssemblyPath)
    {
        appAssemblyPath = Path.GetFullPath(appAssemblyPath);
        var alc = new AppLoadContext(appAssemblyPath);

        try
        {
            Assembly app = alc.LoadFromAssemblyPath(appAssemblyPath);

            // Force the app's [ModuleInitializer] (RegisterPages). CoreCLR runs
            // module initializers lazily; a build-time host must trigger it.
            foreach (Module m in app.GetModules())
                RuntimeHelpers.RunModuleConstructor(m.ModuleHandle);

            Assembly runtime = alc.Assemblies.FirstOrDefault(a =>
                a.GetName().Name == "BlazorNative.Runtime")
                ?? throw new InvalidOperationException(
                    "BlazorNative.Runtime was not loaded by the app assembly — the app at "
                    + $"'{appAssemblyPath}' does not reference the BlazorNative framework, so it has "
                    + "no route registry to read.");

            Type pageManifest = runtime.GetType("BlazorNative.Runtime.PageManifest")
                ?? throw new InvalidOperationException(
                    "BlazorNative.Runtime.PageManifest not found — the framework's route registry "
                    + "type moved or was renamed; re-point RouteManifest.Extract deliberately.");
            var pages = (Array?)pageManifest
                .GetProperty("Pages", BindingFlags.NonPublic | BindingFlags.Static)?
                .GetValue(null)
                ?? throw new InvalidOperationException(
                    "PageManifest.Pages could not be read — the registry accessor changed shape.");

            var routed = new List<RoutedPage>();
            foreach (object? page in pages)
            {
                if (page is null) continue;
                Type t = page.GetType();
                var route = (string?)t.GetProperty("Route")!.GetValue(page);
                var name = (string)t.GetProperty("Name")!.GetValue(page)!;
                if (route is not null)
                    routed.Add(new RoutedPage(route, name));
            }

            if (routed.Count == 0)
                throw new InvalidOperationException(
                    $"the app at '{appAssemblyPath}' registered no ROUTED pages — there is no "
                    + "deep-link map to generate. A routed app declares at least the \"/\" row.");

            return routed;
        }
        finally
        {
            alc.Unload();
        }
    }

    /// <summary>Serializes the routed rows to the flat JSON the shells read at
    /// Intent-parse time: <c>{ "/route": "ComponentName", ... }</c>, the "/"
    /// default row included. Stable, 2-space indented, keys in manifest order.</summary>
    public static string ToJson(IReadOnlyList<RoutedPage> routed)
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        for (int i = 0; i < routed.Count; i++)
        {
            sb.Append("  ").Append(JsonEncode(routed[i].Route))
              .Append(": ").Append(JsonEncode(routed[i].Name));
            sb.Append(i == routed.Count - 1 ? "\n" : ",\n");
        }
        sb.Append("}\n");
        return sb.ToString();
    }

    private static string JsonEncode(string s) => JsonSerializer.Serialize(s);
}

/// <summary>The isolated, collectible context the app is loaded into. Overrides
/// Load() (not merely Resolving) so the app + its package assemblies are pulled
/// into THIS context BEFORE the runtime's default-context fallback can unify them
/// with the host's already-loaded copies — the isolation the double-registration
/// hazard demands (see the RouteManifest header). Framework assemblies (Load
/// returns null for anything not in bin/deps) fall through to the default context,
/// where sharing them is correct.</summary>
internal sealed class AppLoadContext : AssemblyLoadContext
{
    private readonly string _binDir;
    private readonly Dictionary<string, string> _depMap;

    public AppLoadContext(string appAssemblyPath)
        : base("blazornative-routegen", isCollectible: true)
    {
        _binDir = Path.GetDirectoryName(appAssemblyPath)!;
        _depMap = BuildDepMap(appAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName name)
    {
        string local = Path.Combine(_binDir, name.Name + ".dll");
        if (File.Exists(local)) return LoadFromAssemblyPath(local);
        if (_depMap.TryGetValue(name.Name + ".dll", out string? p) && File.Exists(p))
            return LoadFromAssemblyPath(p);
        return null; // System.* / shared framework → the default context
    }

    /// <summary>filename → absolute path, from the app's .deps.json runtime assets
    /// mapped onto the NuGet global-packages folder. Empty when there is no
    /// .deps.json beside the assembly (a copy-local host resolves from bin
    /// instead).</summary>
    private static Dictionary<string, string> BuildDepMap(string appAssemblyPath)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string depsPath = Path.ChangeExtension(appAssemblyPath, ".deps.json");
        if (!File.Exists(depsPath)) return map;

        string nuget = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget", "packages");

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(depsPath));
        if (!doc.RootElement.TryGetProperty("targets", out JsonElement targets)) return map;
        doc.RootElement.TryGetProperty("libraries", out JsonElement libraries);

        foreach (JsonProperty target in targets.EnumerateObject())
        {
            foreach (JsonProperty lib in target.Value.EnumerateObject()) // "id/version"
            {
                if (!lib.Value.TryGetProperty("runtime", out JsonElement runtimeAssets)) continue;

                string idVer = lib.Name;
                string type = libraries.ValueKind == JsonValueKind.Object
                    && libraries.TryGetProperty(idVer, out JsonElement le)
                    && le.TryGetProperty("type", out JsonElement te)
                        ? te.GetString() ?? "package" : "package";
                if (type != "package") continue; // project outputs live in the bin dir

                int slash = idVer.IndexOf('/');
                if (slash < 0) continue;
                string id = idVer[..slash], ver = idVer[(slash + 1)..];
                string pkgRoot = Path.Combine(nuget, id.ToLowerInvariant(), ver);

                foreach (JsonProperty asset in runtimeAssets.EnumerateObject())
                {
                    string rel = asset.Name.Replace('/', Path.DirectorySeparatorChar);
                    map[Path.GetFileName(rel)] = Path.Combine(pkgRoot, rel);
                }
            }
        }
        return map;
    }
}
