using System.Text.Json;
using BlazorNative.Tests.Shared;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// PerfCollectorTests — Phase 19.2 final review, I3. The collectors' assertion
// logic decides whether a perf run's q1 passes and whether a click log is
// complete, and it had no test. Each fact dot-sources one of scripts/perf's
// scripts through TestData/perf-collector-probe.ps1, the way GeneratorRunTests
// shells out, through BoundedScript, and feeds one detector a passing input and
// planted violations. No device and no simulator: the functions read only their
// input. Each fact is one pwsh start-up.
//
// The planted lines are shaped like the real logs: `logcat -v threadtime` lines
// on Android, `log show --style ndjson` records on iOS.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class PerfCollectorTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    private static string Probe => Path.Combine(
        BnRepo.Root(), "tests", "BlazorNative.Runtime.Tests", "TestData", "perf-collector-probe.ps1");

    private sealed record Call(string Function, params object[] Args);

    /// <summary>Runs <paramref name="calls"/> in one probe and returns each call's value.</summary>
    private static async Task<JsonElement[]> RunAsync(string script, params Call[] calls)
    {
        string file = Path.Combine(Path.GetTempPath(), "bn-perf-probe-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(file, JsonSerializer.Serialize(
                calls.Select(c => new { function = c.Function, args = c.Args })));
            GeneratorRun run = await BoundedScript.RunBoundedAsync(Probe, ["-Script", script, "-CallsFile", file], Bound);
            Assert.True(run.ExitCode == 0, $"the probe exited with {run.ExitCode}:\n{run.Log}");
            string? line = run.Log.Split('\n').LastOrDefault(l => l.StartsWith("PROBE-RESULT: ", StringComparison.Ordinal));
            Assert.True(line is not null, $"the probe printed no PROBE-RESULT line:\n{run.Log}");
            using JsonDocument doc = JsonDocument.Parse(line!["PROBE-RESULT: ".Length..]);
            JsonElement[] values = [.. doc.RootElement.EnumerateArray().Select(e => e.GetProperty("value").Clone())];
            Assert.Equal(calls.Length, values.Length);
            return values;
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>A failure list, or any value PowerShell may unroll: null is empty, a string is one.</summary>
    private static string[] Strings(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Null => [],
        JsonValueKind.String => [e.GetString()!],
        JsonValueKind.Array => [.. e.EnumerateArray().Select(x => x.GetString()!)],
        _ => throw new InvalidOperationException($"not a string list: {e}"),
    };

    private static string Logcat(string priority, string tag, string message) =>
        $"11-23 10:00:00.123  4321  4350 {priority} {tag}: {message}";

    private const string FullyDrawn = "11-23 10:00:01.000   600   650 I ActivityTaskManager: Fully drawn io.blazornative.shell/.MainActivity: +812ms";
    private const string Displayed = "11-23 10:00:01.000   600   650 I ActivityTaskManager: Displayed io.blazornative.shell/.MainActivity for user 0: +640ms";

    private static string OsLog(string type, string subsystem, int pid, string message) =>
        JsonSerializer.Serialize(new { messageType = type, subsystem, processID = pid, eventMessage = message });

    [Fact]
    public async Task AndroidQ1Default_PassesAQuietLaunch_AndRedsMarksGatedTagsAndAJoinedRead()
    {
        string[] quiet = [Logcat("W", "BlazorNative", "[BN|W|boot] slow start"), Logcat("I", "ActivityManager", "Start proc"), FullyDrawn];
        string[] mark = [.. quiet, Logcat("V", "BlazorNative/Perf", "BNPERF boot.start seq=1 t=5")];
        string[] gated = [.. quiet, Logcat("I", "BlazorNative", "[BN|I|BnRuntime] mounted")];
        string[] joined = [string.Join(" ", quiet)];

        JsonElement[] r = await RunAsync("android",
            new Call("Test-Q1Default", [quiet]), new Call("Test-Q1Default", [mark]),
            new Call("Test-Q1Default", [gated]), new Call("Test-Q1Default", [joined]));

        Assert.Empty(Strings(r[0]));
        Assert.Contains("BNPERF line at the default level", Assert.Single(Strings(r[1])));
        Assert.Contains("I line from tag BlazorNative at the default level", Assert.Single(Strings(r[2])));
        Assert.Contains("control failed: no Fully drawn line", Assert.Single(Strings(r[3])));
    }

    [Fact]
    public async Task AndroidQ1DefaultClick_PassesAQuietRun_AndRedsMarksGatedTagsAndAMissingLaunch()
    {
        // The instrumentation's own lines: its runner's tags, and the androidTest sources' BlazorNative* tag.
        string[] quiet =
        [
            Logcat("I", "TestRunner", "started: testSerialClicksOnPerfProbe"), Displayed,
            Logcat("I", "BlazorNative.Test", "[7.2-throughput] burst"), Logcat("W", "BlazorNative", "[BN|W|boot] slow"),
        ];
        string[] mark = [.. quiet, Logcat("V", "BlazorNative/Perf", "BNPERF dispatch.post seq=1 t=5 id=3")];
        string[] gated = [.. quiet, Logcat("D", "BlazorNative.WidgetMapper", "applied batch")];
        string[] noLaunch = [.. quiet.Where(l => l != Displayed)];

        JsonElement[] r = await RunAsync("android",
            new Call("Test-Q1DefaultClick", [quiet, "io.blazornative.shell"]),
            new Call("Test-Q1DefaultClick", [mark, "io.blazornative.shell"]),
            new Call("Test-Q1DefaultClick", [gated, "io.blazornative.shell"]),
            new Call("Test-Q1DefaultClick", [noLaunch, "io.blazornative.shell"]));

        Assert.Empty(Strings(r[0]));
        Assert.Contains("BNPERF line in a default-level click run", Assert.Single(Strings(r[1])));
        Assert.Contains("D line from tag BlazorNative.WidgetMapper in a default-level click run", Assert.Single(Strings(r[2])));
        Assert.Contains("control failed: no 'Displayed io.blazornative.shell/...' line", Assert.Single(Strings(r[3])));
    }

    [Fact]
    public async Task IosQ1Default_PassesAQuietLaunch_AndRedsMarksSubsystemDebugAndGatedDotNetLines()
    {
        string[] quiet =
        [
            "Timestamp               Thread     Type        Activity             PID    TTL",
            OsLog("Default", "io.blazornative", 812, "booted"),
            OsLog("Default", "", 812, "[BN|W|boot] slow start"),
            OsLog("Info", "com.apple.UIKit", 812, "scene connected"),
        ];
        string[] mark = [.. quiet, OsLog("Debug", "io.blazornative", 812, "BNPERF boot.start seq=1 t=5")];
        string[] debug = [.. quiet, OsLog("Debug", "io.blazornative", 812, "mapper ready")];
        // NSLog's own copy of a .NET line: the app's Default record outside the subsystem.
        string[] nsLog = [.. quiet, OsLog("Default", "", 812, "[BN|I|BnRuntime] mounted")];
        // The stderr pump's relay of the same line: a subsystem record of category native, also Default.
        string[] relayed = [.. quiet, OsLog("Default", "io.blazornative", 812, "[BN|V|dispatch] lane idle")];

        JsonElement[] r = await RunAsync("ios",
            new Call("Test-Q1Default", [quiet]), new Call("Test-Q1Default", [mark]), new Call("Test-Q1Default", [debug]),
            new Call("Test-Q1Default", [nsLog]), new Call("Test-Q1Default", [relayed]));

        Assert.Empty(Strings(r[0]));
        Assert.Contains("BNPERF line at the default level", Assert.Single(Strings(r[1])));
        Assert.Contains("Debug line from subsystem io.blazornative", Assert.Single(Strings(r[2])));
        Assert.Contains(".NET line below Warn at the default level", Assert.Single(Strings(r[3])));
        Assert.Contains(".NET line below Warn at the default level", Assert.Single(Strings(r[4])));
    }

    [Fact]
    public async Task IosQ1DefaultClick_PassesAQuietRun_AndRedsMarksGatedDotNetLinesAndAnUnreadWindow()
    {
        string[] host = [OsLog("Default", "", 900, "Test Suite 'Selected tests' started"), OsLog("Default", "", 900, "[BN|E|render] patch failed")];
        string[] noMarks = ["Timestamp               Ty Process[PID:TID]"];
        string[] withMark = [.. noMarks, "2026-11-23 10:00:00.1 Df BnHost[900:1] BNPERF dispatch.post seq=1 t=5 id=3"];
        string[] gatedHost = [.. host, OsLog("Default", "", 900, "[BN|D|dispatch] lane idle")];

        JsonElement[] r = await RunAsync("ios",
            new Call("Test-Q1DefaultClick", [noMarks, host]),
            new Call("Test-Q1DefaultClick", [withMark, host]),
            new Call("Test-Q1DefaultClick", [noMarks, gatedHost]),
            new Call("Test-Q1DefaultClick", [noMarks, Array.Empty<string>()]));

        Assert.Empty(Strings(r[0]));
        Assert.Contains("BNPERF line in a default-level click run", Assert.Single(Strings(r[1])));
        Assert.Contains(".NET line below Warn in a default-level click run", Assert.Single(Strings(r[2])));
        Assert.Contains("control failed: the default-level click window holds no record", Assert.Single(Strings(r[3])));
    }

    [Fact]
    public async Task Q1Verbose_PassesEveryBootAndFramePoint_AndRedsTheOneMissing()
    {
        string[] all = ["BNPERF boot.start seq=1 t=1", "BNPERF boot.content seq=1 t=2", "BNPERF frame.recv seq=1 t=3", "BNPERF frame.applied seq=1 t=4"];

        JsonElement[] r = await RunAsync("common",
            new Call("Test-Q1Verbose", [all]), new Call("Test-Q1Verbose", [all[..3]]));

        Assert.Empty(Strings(r[0]));
        Assert.Equal("no BNPERF frame.applied at Verbose", Assert.Single(Strings(r[1])));
    }

    [Fact]
    public async Task SideId_PassesMatchingIds_AndRedsACrossedOrEmptyId()
    {
        JsonElement[] r = await RunAsync("common",
            new Call("Test-SideId", "head", "io.blazornative.shell", "Package"),
            new Call("Test-SideId", "v015", "io.blazornative.bnhost.v015", "Bundle"),
            new Call("Test-SideId", "head", "io.blazornative.shell.v015", "Package"),
            new Call("Test-SideId", "v015", "io.blazornative.bnhost", "Bundle"),
            new Call("Test-SideId", "head", "", "Bundle"));

        Assert.Equal(JsonValueKind.Null, r[0].ValueKind);
        Assert.Equal(JsonValueKind.Null, r[1].ValueKind);
        Assert.Equal("-Side head needs HEAD's package id, not the backport's 'io.blazornative.shell.v015'", r[2].GetString());
        Assert.Equal("-Side v015 needs the backport's bundle id ending in .v015, got 'io.blazornative.bnhost'", r[3].GetString());
        Assert.Equal("-Bundle must not be empty", r[4].GetString());
    }

    // Not a detector but the env a launch carries: BN_LOG_LEVEL must be the NAME Verbose, because
    // BnLogLevel.fromName does not read digits, and must be absent at the default level.
    [Fact]
    public async Task LaunchEnv_CarriesTheComponent_AndTheVerboseNameOnlyAtVerbose()
    {
        JsonElement[] r = await RunAsync("ios",
            new Call("Get-LaunchEnv", true, "PerfProbe"), new Call("Get-LaunchEnv", false, "PerfProbe"));

        Dictionary<string, string> verbose = r[0].Deserialize<Dictionary<string, string>>()!;
        Dictionary<string, string> quiet = r[1].Deserialize<Dictionary<string, string>>()!;
        Assert.Equal(new Dictionary<string, string>
        {
            ["SIMCTL_CHILD_BN_LAUNCH_COMPONENT"] = "PerfProbe",
            ["SIMCTL_CHILD_BN_LOG_LEVEL"] = "Verbose",
        }, verbose);
        Assert.Equal(new Dictionary<string, string> { ["SIMCTL_CHILD_BN_LAUNCH_COMPONENT"] = "PerfProbe" }, quiet);
    }

    [Fact]
    public async Task XcodebuildPassed_NeedsTheMarkerAndOneExecutedTest_AndRedsAnEmptyOrFailedRun()
    {
        string[] passed = ["Test Suite 'BnPerfMeasurementTests' passed", "Executed 1 test, with 0 failures (0 unexpected) in 4.2 seconds", "** TEST EXECUTE SUCCEEDED **"];
        string[] none = ["Executed 0 tests, with 0 failures (0 unexpected) in 0.0 seconds", "** TEST EXECUTE SUCCEEDED **"];
        string[] failed = ["Executed 1 test, with 1 failure (0 unexpected) in 4.2 seconds", "** TEST EXECUTE FAILED **"];
        string[] markerOnly = ["** TEST SUCCEEDED **"];

        JsonElement[] r = await RunAsync("ios",
            new Call("Test-XcodebuildPassed", [passed]), new Call("Test-XcodebuildPassed", [none]),
            new Call("Test-XcodebuildPassed", [failed]), new Call("Test-XcodebuildPassed", [markerOnly]));

        Assert.True(r[0].GetBoolean());
        Assert.False(r[1].GetBoolean());
        Assert.False(r[2].GetBoolean());
        Assert.False(r[3].GetBoolean());
    }

    [Fact]
    public async Task SelectRunningAppJobs_PicksTheAppsRunningJob_NotTheBackportsOrAStoppedOne()
    {
        string[] list =
        [
            "PID\tStatus\tLabel",
            "812\t0\tUIKitApplication:io.blazornative.bnhost[1a2b][rb-legacy]",
            "-\t0\tUIKitApplication:io.blazornative.bnhost[3c4d][rb-legacy]",
            "915\t0\tUIKitApplication:io.blazornative.bnhost.v015[5e6f][rb-legacy]",
        ];

        JsonElement[] r = await RunAsync("common",
            new Call("Select-RunningAppJobs", list, "io.blazornative.bnhost"),
            new Call("Select-RunningAppJobs", list, "io.blazornative.bnhost.v015"),
            new Call("Select-RunningAppJobs", list[..3], "io.blazornative.bnhost.v015"));

        Assert.Equal([list[1]], Strings(r[0]));
        Assert.Equal([list[3]], Strings(r[1]));
        Assert.Empty(Strings(r[2]));
    }

    [Fact]
    public async Task ClickLogComplete_NeedsEveryPostAndHandler_AndRedsAShortOrEmptyLog()
    {
        string Line(string point, int seq) => $"BNPERF {point} seq={seq} t={seq} id=3";
        string[] three = [.. Enumerable.Range(1, 3).SelectMany(n => new[] { Line("dispatch.post", n), Line("dispatch.handler", n) })];
        string[] shortPost = [.. three.Where(l => l != Line("dispatch.post", 3))];
        string[] shortHandler = [.. three.Where(l => l != Line("dispatch.handler", 3))];

        JsonElement[] r = await RunAsync("common",
            new Call("Test-ClickLogComplete", three, 3), new Call("Test-ClickLogComplete", shortPost, 3),
            new Call("Test-ClickLogComplete", shortHandler, 3), new Call("Test-ClickLogComplete", three, 0));

        Assert.True(r[0].GetBoolean());
        Assert.False(r[1].GetBoolean());
        Assert.False(r[2].GetBoolean());
        Assert.False(r[3].GetBoolean());
    }
}
