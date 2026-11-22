using System.Text.RegularExpressions;
using BlazorNative.SampleApp;
using BlazorNative.Tests.Shared;
using Xunit;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// IosRouteComponentsDriftTests — 19.2. BnDeepLink.routeComponents is hand-written Swift: it
// answers which component a cold deep link mounts. A name in it that SampleAppPages does not
// register mounts nothing, and the shell falls back without saying so. These facts parse the
// literal and compare it to the registered pages, and pin /perf to PerfProbe, which the perf
// collector's boot kind depends on.
//
// DOES NOT COVER: whether the route key matches the page's own route (only the component name is
// compared, plus the /perf pair), or the Android generated map.
// ─────────────────────────────────────────────────────────────────────────────
public sealed class IosRouteComponentsDriftTests
{
    private const string Swift = "src/BlazorNative.Apple/BnHost/BnDeepLink.swift";

    /// <summary>The `"route": "Component"` pairs of the routeComponents literal, comments stripped.</summary>
    private static Dictionary<string, string> ParseMap(string source)
    {
        string code = CommentStrippedSource.Strip(source);
        Match block = Regex.Match(code, @"routeComponents\s*:\s*\[String:\s*String\]\s*=\s*\[(?<body>.*?)\n\s*\]", RegexOptions.Singleline);
        Assert.True(block.Success, "the routeComponents literal was not found in BnDeepLink.swift");
        var map = new Dictionary<string, string>();
        foreach (Match m in Regex.Matches(block.Groups["body"].Value, "\"(?<k>[^\"]+)\"\\s*:\\s*\"(?<v>[^\"]+)\""))
            map[m.Groups["k"].Value] = m.Groups["v"].Value;
        return map;
    }

    private static string[] Unregistered(Dictionary<string, string> map, IEnumerable<string> registered)
    {
        var names = new HashSet<string>(registered);
        return [.. map.Values.Where(v => !names.Contains(v)).OrderBy(v => v, StringComparer.Ordinal)];
    }

    [Fact]
    public void EveryIosRouteComponent_IsARegisteredPage_AndPerfMountsPerfProbe()
    {
        var map = ParseMap(File.ReadAllText(Path.Combine(BnRepo.Root(), Swift)));

        Assert.True(map.Count >= 3, $"the map must hold at least /notifications, /geolocation and /perf, got {map.Count}");
        Assert.Empty(Unregistered(map, SampleAppPages.All.Select(p => p.Name)));
        Assert.Equal("PerfProbe", map["/perf"]);
        Assert.Contains(SampleAppPages.All, p => p.Name == "PerfProbe" && p.Route == "/perf");
    }

    [Fact]
    public void TheDetector_ReportsAComponentNoPageRegisters()
    {
        const string planted = "static let routeComponents: [String: String] = [\n    \"/a\": \"BnDemo\",\n    \"/ghost\": \"NoSuchPage\", // a trailing comment\n]\n";
        var map = ParseMap(planted);

        Assert.Equal(2, map.Count);
        Assert.Equal(["NoSuchPage"], Unregistered(map, ["BnDemo"]));
    }
}
