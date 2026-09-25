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

// Fix round 3: every file this tool writes gets a consistent LF line ending,
// including its trailing newline. `Environment.NewLine` is CRLF on Windows, and a
// raw string literal's embedded newlines follow whatever the SOURCE file itself
// uses — mixing the two produced files that were LF throughout except for a
// trailing CRLF. Normalising unconditionally (not just appending "\n") means this
// holds regardless of how Program.cs itself is checked out.
static void WriteLf(string path, string content) =>
    File.WriteAllText(path, content.Replace("\r\n", "\n").TrimEnd('\n') + "\n");

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
// A leading `using Namespace;` DIRECTIVE line (not a `using var x = ...;` DECLARATION,
// which is legal statement syntax and must stay in the body) — fix round 1: a
// "statements" fence occasionally needs its own using (logging.md's BnLog.Level sample),
// and a using directive cannot appear inside the wrapped method body, so it is hoisted
// above the generated class instead.
var usingDirective = new Regex(@"^using\s+[\w.]+\s*;\s*$", RegexOptions.CultureInvariant);
int totalCompiled = 0, totalSkipped = 0;
var usedComponentNames = new Dictionary<string, string>(StringComparer.Ordinal); // name -> page:line, generator-side courtesy check

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
                // bn-sample=component:<Name> — DocsSamplesDriftTests validates <Name> is a
                // valid C# identifier and globally unique; this is a courtesy check so the
                // generator fails loudly too, rather than one fence silently overwriting
                // another's file.
                string componentName = fence.SkipReason ?? name;
                if (fence.SkipReason is not null)
                {
                    string where = $"{page}:{fence.Line}";
                    if (usedComponentNames.TryGetValue(componentName, out string? first))
                        throw new InvalidOperationException($"bn-sample=component:{componentName} claimed twice: {first} and {where}");
                    usedComponentNames[componentName] = where;
                }
                WriteLf(Path.Combine(samplesDir, $"{componentName}.razor"), fence.Body);
                compiled++;
                break;
            case "file":
                WriteLf(Path.Combine(samplesDir, $"{name}.cs"), fence.Body);
                compiled++;
                break;
            case "statements":
                // These usings are the generator's wrapper boilerplate, not the prelude
                // (spec risk 2 only constrains prelude/_Imports.razor and
                // prelude/GlobalUsings.cs) — a "statements" fence is, by definition, a
                // fragment meant to be pasted inside a method that already has whatever
                // usings its own file needs, so the fence body itself never shows one,
                // except the rare case hoisted below (logging.md).
                string[] defaultUsings =
                [
                    "using BlazorNative.Components;",
                    "using BlazorNative.Core;",
                    "using BlazorNative.Device;",
                    "using BlazorNative.Http;",
                    "using BlazorNative.Runtime;",
                    "using BlazorNative.Testing;",
                    "using DocSamples.Samples;",
                    "using Microsoft.AspNetCore.Components;",
                    "using Xunit;",
                ];

                string[] bodyLines = fence.Body.Replace("\r\n", "\n").Split('\n');
                int hoistCount = bodyLines.TakeWhile(l => usingDirective.IsMatch(l) || l.Length == 0).Count();
                // Trim only the usings themselves, not blank lines that separate them from
                // the rest of the body — hoisting fewer than all the leading blanks keeps
                // the body's own formatting close to what the page shows.
                while (hoistCount > 0 && bodyLines[hoistCount - 1].Length == 0) hoistCount--;
                // A hoisted using already covered by a default (e.g. logging.md's own
                // `using BlazorNative.Core;`) is dropped here, not emitted twice (CS0105).
                string[] hoistedLines = [.. bodyLines.Take(hoistCount)
                    .Where(l => l.Length == 0 || !defaultUsings.Contains(l.Trim()))];
                string hoisted = string.Join('\n', hoistedLines);
                string remainder = string.Join('\n', bodyLines.Skip(hoistCount));

                bool isAsync = awaitProbe.IsMatch(remainder);
                string signature = isAsync
                    ? "internal static async System.Threading.Tasks.Task Run()"
                    : "internal static void Run()";
                string wrapped = $$"""
                    {{string.Join('\n', defaultUsings)}}
                    {{(hoistCount > 0 ? hoisted + "\n" : "")}}
                    namespace DocSamples;
                    internal static class {{name}}
                    {
                        {{signature}}
                        {
                    {{remainder}}
                        }
                    }
                    """;
                WriteLf(Path.Combine(samplesDir, $"{name}.cs"), wrapped);
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

// Final review finding I1: this generator used to always return 0, and nothing checked
// how many sample files it actually wrote — the CI "Docs samples compile" step could
// pass while compiling almost nothing, as long as the (possibly empty) project it
// handed to `dotnet build` had no errors. DocSampleParser.MinimumCompiledSamples is the
// SAME floor DocsSamplesDriftTests.TheCompiledSampleCount_MeetsItsFloor measures from the
// parsed fences; failing loudly here, from the count of files this generator itself
// wrote, closes the gap between "the pin is green" and "the step compiled anything".
if (totalCompiled < DocSampleParser.MinimumCompiledSamples)
{
    Console.Error.WriteLine($"only {totalCompiled} sample files were written, fewer than DocSampleParser.MinimumCompiledSamples ({DocSampleParser.MinimumCompiledSamples}) — the docs-sample build step would pass while compiling too little. Fix the page scan or lower the floor deliberately, in one place (DocSampleParser.MinimumCompiledSamples), never here.");
    return 1;
}

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
    // Final review finding I2: OutputItemType="Analyzer" on a src csproj's own reference
    // to BlazorNative.Analyzers does not flow to a project that merely references that
    // src csproj — analyzers are per-project, not transitive. Without this, every sample
    // in DocSamples.csproj built clean of BN diagnostics regardless of whether it was
    // actually compliant. Same attributes as every src csproj's own reference, e.g.
    // src/BlazorNative.Components/BlazorNative.Components.csproj.
    string analyzersPath = Path.Combine(repoRoot, "src", "BlazorNative.Analyzers", "BlazorNative.Analyzers.csproj");
    refs.AppendLine($"""    <ProjectReference Include="{analyzersPath}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />""");

    string xunitVersion = ReadPackageVersion(repoRoot, "xunit");

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
          </PropertyGroup>

          <ItemGroup>
        {refs}
            <!-- testing-harness.md's compiled sample calls Assert directly (the fence is
                 written the way a consumer's own xUnit test would be) — version READ, at
                 generation time, from the same `xunit` PackageReference
                 tests/BlazorNative.Runtime.Tests.csproj pins, so the two cannot drift apart
                 the way a second hard-coded copy could. -->
            <PackageReference Include="xunit.assert" Version="{xunitVersion}" />
          </ItemGroup>

        </Project>
        """;
    WriteLf(Path.Combine(outDir, "DocSamples.csproj"), csproj);
}

/// <summary>Reads the version of <paramref name="packageId"/> the test suite itself pins,
/// so the generated project's own reference cannot silently drift from it (fix round 3 —
/// this used to be a hard-coded "2.9.3"). Checks the ordinary
/// `&lt;PackageReference Include="..." Version="..." /&gt;` form first; falls back to
/// `Directory.Packages.props` for a repo using central package management. Throws rather
/// than falling back to a guessed version — a docs-sample project silently pinning the
/// wrong test framework version is worse than a loud generation failure.</summary>
static string ReadPackageVersion(string repoRoot, string packageId)
{
    string testCsproj = Path.Combine(repoRoot, "tests", "BlazorNative.Runtime.Tests", "BlazorNative.Runtime.Tests.csproj");
    var direct = new Regex($"""<PackageReference\s+Include="{Regex.Escape(packageId)}"\s+Version="(?<version>[^"]+)"\s*/>""", RegexOptions.CultureInvariant);
    if (File.Exists(testCsproj))
    {
        Match m = direct.Match(File.ReadAllText(testCsproj));
        if (m.Success) return m.Groups["version"].Value;
    }

    string centralProps = Path.Combine(repoRoot, "Directory.Packages.props");
    if (File.Exists(centralProps))
    {
        var central = new Regex($"""<PackageVersion\s+Include="{Regex.Escape(packageId)}"\s+Version="(?<version>[^"]+)"\s*/>""", RegexOptions.CultureInvariant);
        Match m = central.Match(File.ReadAllText(centralProps));
        if (m.Success) return m.Groups["version"].Value;
    }

    throw new InvalidOperationException(
        $"could not find a Version for PackageReference '{packageId}' in {testCsproj} " +
        $"or in {centralProps} — the generator refuses to guess a version for xunit.assert " +
        "rather than risk it silently drifting from what the test suite actually pins.");
}
