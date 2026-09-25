using System.Text;
using System.Text.RegularExpressions;
using BlazorNative.DocSamples;

// ─────────────────────────────────────────────────────────────────────────────
// BlazorNative.DocSamples — the generator half of the tool (see DocSampleParser
// for the parser half, which is the piece DocsSamplesDriftTests also references).
//
// Reads every hand-written docs page (DocSampleParser.HandWrittenPages), takes
// every fence whose language is in DocSampleParser.CompiledLanguages, and for
// every one marked component/file/statements writes a source file into --out.
// `skip` fences and unclassified fences write nothing: an unclassified fence is
// DocsSamplesDriftTests' job to catch, not this generator's — the generator
// only ever compiles what Step 4 already decided is a compilable unit.
// ─────────────────────────────────────────────────────────────────────────────

string? outArg = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--out" && i + 1 < args.Length) { outArg = args[i + 1]; break; }
}
if (outArg is null)
{
    Console.Error.WriteLine("usage: dotnet run --project tools/BlazorNative.DocSamples -- --out <dir>");
    return 1;
}

string repoRoot = FindRepoRoot();
string outDir = Path.GetFullPath(outArg, Directory.GetCurrentDirectory());

if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
Directory.CreateDirectory(outDir);
string samplesDir = Path.Combine(outDir, "Samples");
Directory.CreateDirectory(samplesDir);

// The prelude ships next to the built tool (BlazorNative.DocSamples.csproj
// copies prelude/** to the output directory) so this works under `dotnet run`
// from any working directory.
string preludeDir = Path.Combine(AppContext.BaseDirectory, "prelude");
foreach (string file in Directory.EnumerateFiles(preludeDir))
    File.Copy(file, Path.Combine(outDir, Path.GetFileName(file)), overwrite: true);

WriteProjectFile(outDir, repoRoot);

var awaitProbe = new Regex(@"\bawait\b", RegexOptions.CultureInvariant);
int totalCompiled = 0, totalSkipped = 0;

foreach (string page in DocSampleParser.HandWrittenPages(repoRoot))
{
    string markdown = File.ReadAllText(Path.Combine(repoRoot, page));
    var fences = DocSampleParser.Fences(markdown)
        .Where(f => DocSampleParser.CompiledLanguages.Contains(f.Language))
        .ToList();

    int compiled = 0, skipped = 0;
    string slug = PageSlug(page);

    foreach (Fence fence in fences)
    {
        string name = $"{slug}_{fence.Index}";
        switch (fence.Kind)
        {
            case "component":
                File.WriteAllText(Path.Combine(samplesDir, $"{name}.razor"), fence.Body + Environment.NewLine);
                compiled++;
                break;
            case "file":
                File.WriteAllText(Path.Combine(samplesDir, $"{name}.cs"), fence.Body + Environment.NewLine);
                compiled++;
                break;
            case "statements":
                bool isAsync = awaitProbe.IsMatch(fence.Body);
                string signature = isAsync
                    ? "internal static async System.Threading.Tasks.Task Run()"
                    : "internal static void Run()";
                // These three usings are the generator's wrapper boilerplate, not the
                // prelude (spec risk 2 only constrains prelude/_Imports.razor and
                // prelude/GlobalUsings.cs) — a "statements" fence is, by definition, a
                // fragment meant to be pasted inside a method that already has whatever
                // usings its own file needs, so the fence body itself never shows one.
                string wrapped = $$"""
                    using BlazorNative.Core;
                    using BlazorNative.Device;
                    using BlazorNative.Http;
                    using BlazorNative.Runtime;

                    namespace DocSamples;
                    internal static class {{name}}
                    {
                        {{signature}} { {{fence.Body}} }
                    }
                    """;
                File.WriteAllText(Path.Combine(samplesDir, $"{name}.cs"), wrapped + Environment.NewLine);
                compiled++;
                break;
            case "skip":
                skipped++;
                break;
            // A null or unrecognised kind writes nothing — DocsSamplesDriftTests is
            // what holds every fence to declaring one of the four known kinds.
        }
    }

    Console.WriteLine($"{page}: {compiled} compiled, {skipped} skipped");
    totalCompiled += compiled;
    totalSkipped += skipped;
}

Console.WriteLine($"total: {totalCompiled} compiled, {totalSkipped} skipped");
return 0;

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BlazorNative.sln")))
        dir = dir.Parent;
    if (dir is null)
        throw new InvalidOperationException("could not find BlazorNative.sln above the tool binary.");
    return dir.FullName;
}

// The page path with '/', '-' and the '.md'/'.mdx' extension turned into '_', so
// it stays a valid C# identifier and names the page in any compiler error. The
// leading character is upper-cased because a generated .razor file's class name
// IS this slug, and Razor rejects a component name starting lowercase (RZ10011).
static string PageSlug(string page)
{
    string s = page.EndsWith(".mdx", StringComparison.Ordinal) ? page[..^4] : page[..^3];
    s = s.Replace('/', '_').Replace('-', '_');
    return char.ToUpperInvariant(s[0]) + s[1..];
}

static void WriteProjectFile(string outDir, string repoRoot)
{
    string[] packages = ["Components", "Core", "Device", "Http", "Renderer", "Runtime", "Testing"];
    var refs = new StringBuilder();
    foreach (string p in packages)
    {
        string path = Path.Combine(repoRoot, "src", $"BlazorNative.{p}", $"BlazorNative.{p}.csproj");
        refs.AppendLine($"""    <ProjectReference Include="{path}" />""");
    }

    string csproj = $"""
        <Project Sdk="Microsoft.NET.Sdk.Razor">

          <!-- Generated by tools/BlazorNative.DocSamples — DO NOT EDIT. Every hand-written
               docs page's component/file/statements fence, compiled together so a sample
               that no longer compiles fails here, naming the page and fence in its
               <PageSlug>_<Index> file name. -->

          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <RootNamespace>DocSamples</RootNamespace>
            <AssemblyName>DocSamples</AssemblyName>
            <IsPackable>false</IsPackable>
            <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
            <StaticWebAssetsEnabled>false</StaticWebAssetsEnabled>
            <NoWarn>NETSDK1206</NoWarn>
            <!-- A "file" fence is a complete, standalone .cs file verbatim — one docs
                 sample (logging.md) is written as top-level statements, which C#
                 requires to be an executable's entry point (CS8805). -->
            <OutputType>Exe</OutputType>
          </PropertyGroup>

          <ItemGroup>
        {refs}    </ItemGroup>

        </Project>
        """;
    File.WriteAllText(Path.Combine(outDir, "DocSamples.csproj"), csproj);
}
