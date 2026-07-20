using BlazorNative.RouteGen;

// BlazorNative.RouteGen <sources-list-file> <output-json-path>
//
// Phase 11.0 Gate A: PIVOTED from loading the built app assembly to Roslyn SOURCE
// analysis (arch/RID-independent — see RouteManifest.cs). <sources-list-file> is a
// newline-delimited list of the app's C# source paths (the build target writes
// @(Compile) to it); RouteGen parses them for BlazorNativePage.Routed<T> calls and
// writes the shells' deep-link map to <output-json-path>. Exit 0 on success;
// non-zero with a diagnostic on any failure (the build must fail loudly rather
// than ship a stale or empty map).

if (args.Length != 2)
{
    Console.Error.WriteLine(
        "usage: BlazorNative.RouteGen <sources-list-file> <output-json-path>");
    return 2;
}

string sourcesListPath = args[0];
string outputPath = args[1];

if (!File.Exists(sourcesListPath))
{
    Console.Error.WriteLine(
        $"BlazorNative.RouteGen: sources-list file not found: {sourcesListPath}");
    return 3;
}

try
{
    string[] sourceFiles = File.ReadAllLines(sourcesListPath)
        .Select(l => l.Trim())
        .Where(l => l.Length > 0)
        .ToArray();

    IReadOnlyList<RoutedPage> routed = RouteManifest.Extract(sourceFiles);
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
