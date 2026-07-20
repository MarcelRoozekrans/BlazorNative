using BlazorNative.RouteGen;

// BlazorNative.RouteGen <app-assembly-path> <output-json-path>
//
// Loads the built app assembly, reads the framework's routed-page registry, and
// writes the shells' deep-link map to <output-json-path>. Invoked by the
// GenerateBlazorNativeRoutes target in build/BionicNativeAot.targets. Exit 0 on
// success; non-zero with a diagnostic on any failure (the build must fail loudly
// rather than ship a stale or empty map).

if (args.Length != 2)
{
    Console.Error.WriteLine(
        "usage: BlazorNative.RouteGen <app-assembly-path> <output-json-path>");
    return 2;
}

string appAssemblyPath = args[0];
string outputPath = args[1];

if (!File.Exists(appAssemblyPath))
{
    Console.Error.WriteLine($"BlazorNative.RouteGen: app assembly not found: {appAssemblyPath}");
    return 3;
}

try
{
    IReadOnlyList<RoutedPage> routed = RouteManifest.Extract(appAssemblyPath);
    string json = RouteManifest.ToJson(routed);

    string? dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

    // Only rewrite when the content actually changed — keeps the file's timestamp
    // stable so a no-op rebuild does not needlessly re-trigger downstream tooling.
    if (!File.Exists(outputPath) || File.ReadAllText(outputPath) != json)
        File.WriteAllText(outputPath, json);

    Console.WriteLine(
        $"BlazorNative.RouteGen: wrote {routed.Count} routed rows to {outputPath}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"BlazorNative.RouteGen: {ex.Message}");
    return 1;
}
