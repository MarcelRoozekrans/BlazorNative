using System.Text.RegularExpressions;
using BlazorNative.Tests.Shared;
using Xunit;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// PerfMarkSiteDriftTests — 19.2. BnPerfMark's line is written with BnPrivacy.SAFE, so
// what may reach it must be fixed at the call sites. Every BnPerfMark.mark call and every
// perfMark hook call in the Kotlin shell, in both copies, must pass a string literal that is
// one of the seven point names. BnPerfMark.kt's own boot marks go through private write calls, which this check does not see.
//
// DOES NOT COVER: the id argument, a Long, which cannot carry text; and a call through an
// alias of BnPerfMark or of the hook, which the call regex does not name.
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

    private static IEnumerable<(string Rel, string Code)> KotlinSources()
    {
        foreach (var root in Roots)
            foreach (var f in Directory.EnumerateFiles(Path.Combine(BnRepo.Root(), root), "*.kt", SearchOption.AllDirectories))
                yield return (Path.GetRelativePath(BnRepo.Root(), f), CommentStrippedSource.Strip(File.ReadAllText(f)));
    }

    [Fact]
    public void EveryMarkCallSite_PassesALiteralOfTheSevenPointNames()
    {
        var offenders = new List<string>();
        int sites = 0;
        var seen = new HashSet<string>();
        foreach (var (rel, code) in KotlinSources())
        {
            offenders.AddRange(Offenders(code).Select(o => rel + ": " + o));
            foreach (Match m in Regex.Matches(code, "(?:BnPerfMark\\s*\\.\\s*mark|perfMark\\s*\\?\\s*\\.\\s*invoke)\\s*\\(\\s*\"([a-z.]+)\""))
            { sites++; seen.Add(m.Groups[1].Value); }
        }

        Assert.True(offenders.Count == 0,
            "A perf mark call that is not a literal of the seven point names:\n  " + string.Join("\n  ", offenders));
        // Non-vacuity: dispatch.post, the hook's frame.recv twice and frame.applied, in each of two copies.
        Assert.True(sites >= 8, $"found {sites} mark call sites, expected at least 8; the call regex has stopped seeing them");
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
}
