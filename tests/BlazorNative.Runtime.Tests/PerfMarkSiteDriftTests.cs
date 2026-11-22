using System.Text.RegularExpressions;
using BlazorNative.Tests.Shared;
using Xunit;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// PerfMarkSiteDriftTests — 19.2. BnPerfMark's line is written with BnPrivacy.SAFE on Android and
// with .safe on iOS, so what may reach it must be fixed at the call sites. Every BnPerfMark.mark
// call and every perfMark hook call in the Kotlin shell, in both copies, and every BnPerfMark.mark
// call in the Swift shell under src/BlazorNative.Apple/BnHost, must pass a string literal that is
// one of the seven point names. The BnPerfMark files' own boot marks go through private write
// calls, which this check does not see. The Swift tests under BnHostTests are not scanned: they
// plant their own point name on purpose.
//
// DOES NOT COVER: the id argument, a number, which cannot carry text; a call through an
// alias of BnPerfMark or of the hook, which the call regex does not name; and the Swift
// template copy, which does not exist yet.
// ─────────────────────────────────────────────────────────────────────────────
public sealed class PerfMarkSiteDriftTests
{
    private static readonly string[] Points =
    [
        "boot.start", "boot.content", "dispatch.post", "dispatch.enter", "dispatch.handler", "frame.recv", "frame.applied",
    ];

    private static readonly string[] Roots =
    [
        "src/BlazorNative.Jni/src/androidMain",
        "src/BlazorNative.Jni/src/main",
        "templates/BlazorNative.Templates/content/BlazorNative.App/android/src",
    ];

    private static readonly Regex Call = new(
        @"\bBnPerfMark\s*\.\s*mark\s*\(|\bperfMark\s*\?\s*\.\s*invoke\s*\(",
        RegexOptions.Compiled);

    /// <summary>Every call in <paramref name="code"/> whose first argument is not a literal point name.</summary>
    private static List<string> Offenders(string code)
    {
        var bad = new List<string>();
        foreach (Match m in Call.Matches(code))
        {
            var rest = code[(m.Index + m.Length)..];
            var lit = Regex.Match(rest, "^\\s*\"([a-z.]+)\"\\s*[,)]");
            if (!lit.Success || !Points.Contains(lit.Groups[1].Value))
                bad.Add(m.Value + rest[..Math.Min(40, rest.Length)].Split('\n')[0]);
        }
        return bad;
    }

    private const string SwiftRoot = "src/BlazorNative.Apple/BnHost";

    private static IEnumerable<(string Rel, string Code)> KotlinSources()
    {
        foreach (var root in Roots)
            foreach (var f in Directory.EnumerateFiles(Path.Combine(BnRepo.Root(), root), "*.kt", SearchOption.AllDirectories))
                yield return (Path.GetRelativePath(BnRepo.Root(), f), CommentStrippedSource.Strip(File.ReadAllText(f)));
    }

    private static IEnumerable<(string Rel, string Code)> SwiftSources()
    {
        foreach (var f in Directory.EnumerateFiles(Path.Combine(BnRepo.Root(), SwiftRoot), "*.swift", SearchOption.AllDirectories))
            yield return (Path.GetRelativePath(BnRepo.Root(), f), CommentStrippedSource.Strip(File.ReadAllText(f)));
    }

    [Fact]
    public void EveryMarkCallSite_PassesALiteralOfTheSevenPointNames()
    {
        var offenders = new List<string>();
        int sites = 0;
        var seen = new HashSet<string>();
        int swiftSites = 0;
        var swiftSeen = new HashSet<string>();
        foreach (var (rel, code) in KotlinSources().Concat(SwiftSources()))
        {
            offenders.AddRange(Offenders(code).Select(o => rel + ": " + o));
            foreach (Match m in Regex.Matches(code, "(?:BnPerfMark\\s*\\.\\s*mark|perfMark\\s*\\?\\s*\\.\\s*invoke)\\s*\\(\\s*\"([a-z.]+)\""))
            { sites++; seen.Add(m.Groups[1].Value); if (rel.EndsWith(".swift", StringComparison.Ordinal)) { swiftSites++; swiftSeen.Add(m.Groups[1].Value); } }
        }

        Assert.True(offenders.Count == 0,
            "A perf mark call that is not a literal of the seven point names:\n  " + string.Join("\n  ", offenders));
        // Non-vacuity: dispatch.post, the hook's frame.recv twice and frame.applied, in each of two copies.
        Assert.True(sites >= 8, $"found {sites} mark call sites, expected at least 8; the call regex has stopped seeing them");
        // The Swift shell marks dispatch.post, frame.recv and frame.applied; boot.start and boot.content go through BnPerfMark's own entry points, bootStart and bootContentOnce, which are not mark calls.
        Assert.True(swiftSites >= 3, $"found {swiftSites} Swift mark call sites, expected at least 3; the call regex has stopped seeing them");
        foreach (var p in new[] { "dispatch.post", "frame.recv", "frame.applied" })
            Assert.True(swiftSeen.Contains(p), $"the Swift shell has no literal BnPerfMark.mark call for {p}");
        Assert.Contains("dispatch.post", seen);
        Assert.Contains("frame.recv", seen);
        Assert.Contains("frame.applied", seen);
    }

    [Fact]
    public void TheCallCheck_FlagsAPlantedNonLiteralAndAnUnknownName()
    {
        Assert.Empty(Offenders("BnPerfMark.mark(\"frame.applied\")\nperfMark?.invoke(\"frame.recv\")"));
        Assert.Single(Offenders("BnPerfMark.mark(name)"));
        Assert.Single(Offenders("BnPerfMark.mark(\"user.text\", 1)"));
        Assert.Single(Offenders("BnPerfMark.mark(\"frame.\" + x)"));
        Assert.Single(Offenders("perfMark?.invoke(message)"));
        // The Swift call shape: a labelled id argument after the literal.
        Assert.Empty(Offenders("BnPerfMark.mark(\"dispatch.post\", id: UInt64(h))"));
        Assert.Single(Offenders("BnPerfMark.mark(point, id: 1)"));
        Assert.Single(Offenders("BnPerfMark.mark(\"user.text\", id: 1)"));
    }

    [Fact]
    public void BnPerfMarkKt_ExistsInExactlyTheShellAndTheTemplate()
    {
        var found = new List<string>();
        foreach (var top in new[] { "src", "templates" })
            foreach (var f in Directory.EnumerateFiles(Path.Combine(BnRepo.Root(), top), "BnPerfMark.kt", SearchOption.AllDirectories))
                found.Add(Path.GetRelativePath(BnRepo.Root(), f).Replace('\\', '/'));
        found.Sort(StringComparer.Ordinal);

        Assert.Equal(
        [
            "src/BlazorNative.Jni/src/androidMain/kotlin/io/blazornative/shell/BnPerfMark.kt",
            "templates/BlazorNative.Templates/content/BlazorNative.App/android/src/androidMain/kotlin/io/blazornative/shell/BnPerfMark.kt",
        ], found);
    }

    [Fact]
    public void BnPerfMarkSwift_ExistsExactlyOnce()
    {
        var found = Directory.EnumerateFiles(Path.Combine(BnRepo.Root(), "src"), "BnPerfMark.swift", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(BnRepo.Root(), "templates"), "BnPerfMark.swift", SearchOption.AllDirectories))
            .Select(f => Path.GetRelativePath(BnRepo.Root(), f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["src/BlazorNative.Apple/BnHost/BnPerfMark.swift"], found);
    }
}
