using BlazorNative.WireGen;

// ─────────────────────────────────────────────────────────────────────────────
// BlazorNative.WireGen — reads src/wire-vocabulary.json, writes every generated
// table. Run from anywhere in the repo:
//
//     dotnet run --project tools/BlazorNative.WireGen
//     dotnet run --project tools/BlazorNative.WireGen -- --check
//
// --check writes nothing and exits 1 if any output is stale. That is what a
// developer runs to answer "did I forget to regenerate?" without dirtying the
// tree; CI asks the same question through WireVocabularyCodegenTests instead,
// because the required lane already runs the .NET tests and adding a second
// mechanism would mean two things to keep in step.
// ─────────────────────────────────────────────────────────────────────────────

bool check = args.Contains("--check");

string repoRoot = FindRepoRoot();
string manifestPath = Path.Combine(repoRoot, Emitters.ManifestPath);

if (!File.Exists(manifestPath))
{
    Console.Error.WriteLine($"wire-vocabulary: manifest not found at {manifestPath}");
    return 2;
}

WireVocabulary vocabulary;
try
{
    vocabulary = WireVocabulary.Load(File.ReadAllText(manifestPath));
}
catch (Exception ex)
{
    // The manifest is the ONLY place these names live now, so a bad one would
    // propagate into four languages at once. Fail with the reason, never emit.
    Console.Error.WriteLine($"wire-vocabulary: {Emitters.ManifestPath} is invalid — {ex.Message}");
    return 2;
}

string vectorsPath = Path.Combine(repoRoot, Emitters.VectorsManifestPath.Replace('/', Path.DirectorySeparatorChar));

if (!File.Exists(vectorsPath))
{
    Console.Error.WriteLine($"deeplink-vectors: manifest not found at {vectorsPath}");
    return 2;
}

DeepLinkVectors vectors;
try
{
    vectors = System.Text.Json.JsonSerializer.Deserialize<DeepLinkVectors>(
        File.ReadAllText(vectorsPath),
        new System.Text.Json.JsonSerializerOptions
        {
            ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
        })
        ?? throw new InvalidOperationException("manifest deserialized to null");
}
catch (Exception ex)
{
    // Same reasoning as the vocabulary above: this table is the ONLY place these
    // cases live, and a bad one would propagate into three test suites at once.
    Console.Error.WriteLine($"deeplink-vectors: {Emitters.VectorsManifestPath} is invalid — {ex.Message}");
    return 2;
}

var stale = new List<string>();
foreach ((string relative, string content) in Emitters.EmitAll(vocabulary))
{
    string path = Path.Combine(repoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
    string? existing = File.Exists(path) ? File.ReadAllText(path) : null;

    // Compare through the SAME normalization the test uses: git checks these out
    // with CRLF on Windows and LF elsewhere (`* text=auto`), so a raw byte
    // comparison would report every file stale on one OS and none on the other.
    if (existing is not null && Normalize(existing) == Normalize(content))
        continue;

    stale.Add(relative);
    if (check) continue;

    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content);
    Console.WriteLine($"wire-vocabulary: wrote {relative}");
}

// The vector tables, through the SAME staleness rule and the SAME normalization
// — one `stale` list, so --check and the summary below already cover both.
foreach ((string relative, string content) in Emitters.EmitAllVectors(vectors))
{
    string path = Path.Combine(repoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
    string? existing = File.Exists(path) ? File.ReadAllText(path) : null;

    if (existing is not null && Normalize(existing) == Normalize(content))
        continue;

    stale.Add(relative);
    if (check) continue;

    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content);
    Console.WriteLine($"deeplink-vectors: wrote {relative}");
}

if (check)
{
    if (stale.Count == 0)
    {
        Console.WriteLine("wire-vocabulary: all generated files are up to date.");
        return 0;
    }
    Console.Error.WriteLine(
        "wire-vocabulary: STALE — regenerate with `dotnet run --project tools/BlazorNative.WireGen`:\n  "
        + string.Join("\n  ", stale));
    return 1;
}

Console.WriteLine(stale.Count == 0
    ? "wire-vocabulary: nothing to do (already up to date)."
    : $"wire-vocabulary: regenerated {stale.Count} file(s).");
return 0;

static string Normalize(string s) => s.Replace("\r\n", "\n");

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BlazorNative.sln")))
        dir = dir.Parent;
    return dir?.FullName
        ?? throw new InvalidOperationException(
            "could not find BlazorNative.sln above the current directory — run this from inside the repo");
}
