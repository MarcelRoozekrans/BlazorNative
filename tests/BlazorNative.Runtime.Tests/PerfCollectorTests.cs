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

    /// <summary>Runs <paramref name="calls"/> in one probe and returns each call's value; a call that threw fails the fact.</summary>
    private static async Task<JsonElement[]> RunAsync(string script, params Call[] calls)
    {
        (JsonElement Value, string? Error)[] results = await RunCatchingAsync(script, calls);
        for (int i = 0; i < results.Length; i++)
            Assert.True(results[i].Error is null, $"call {i}, {calls[i].Function}, threw: {results[i].Error}");
        return [.. results.Select(r => r.Value)];
    }

    /// <summary>Runs <paramref name="calls"/> in one probe and returns each call's value, or the message it threw.</summary>
    private static async Task<(JsonElement Value, string? Error)[]> RunCatchingAsync(string script, params Call[] calls)
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
            (JsonElement, string?)[] results = [.. doc.RootElement.EnumerateArray().Select(e =>
                (e.GetProperty("value").Clone(), e.GetProperty("error").ValueKind == JsonValueKind.Null ? null : e.GetProperty("error").GetString()))];
            Assert.Equal(calls.Length, results.Length);
            return results;
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>A probe arg the probe turns into a scriptblock.</summary>
    private static object Block(string text) => new { scriptblock = text };

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
    private const string FullyDrawnV015 = "11-23 10:00:01.000   600   650 I ActivityTaskManager: Fully drawn io.blazornative.shell.v015/io.blazornative.shell.MainActivity: +1s282ms";
    private const string Shell = "io.blazornative.shell";
    private const string ShellV015 = "io.blazornative.shell.v015";
    private const string ShellActivity = "io.blazornative.shell.MainActivity";
    private const string Displayed ="11-23 10:00:01.000   600   650 I ActivityTaskManager: Displayed io.blazornative.shell/.MainActivity for user 0: +640ms";

    private static string OsLog(string type, string subsystem, int pid, string message) =>
        JsonSerializer.Serialize(new { messageType = type, subsystem, processID = pid, eventMessage = message });

    [Fact]
    public async Task AndroidQ1Default_PassesAQuietLaunch_AndRedsMarksGatedTagsAndAJoinedRead()
    {
        string[] quiet = [Logcat("W", "BlazorNative", "[BN|W|boot] slow start"), Logcat("I", "ActivityManager", "Start proc"), FullyDrawn];
        string[] mark = [.. quiet, Logcat("V", "BlazorNative/Perf", "BNPERF boot.start seq=1 t=5")];
        string[] gated = [.. quiet, Logcat("I", "BlazorNative", "[BN|I|BnRuntime] mounted")];
        string[] joined = [string.Join(" ", quiet)];

        // The control must be the selected app's own record: the backport's, when head was launched, is not.
        string[] otherApp = [.. quiet.Where(l => l != FullyDrawn), FullyDrawnV015];

        JsonElement[] r = await RunAsync("android",
            new Call("Test-Q1Default", quiet, Shell, ShellActivity), new Call("Test-Q1Default", mark, Shell, ShellActivity),
            new Call("Test-Q1Default", gated, Shell, ShellActivity), new Call("Test-Q1Default", joined, Shell, ShellActivity),
            new Call("Test-Q1Default", otherApp, Shell, ShellActivity));

        Assert.Empty(Strings(r[0]));
        Assert.Contains("BNPERF line at the default level", Assert.Single(Strings(r[1])));
        Assert.Contains("I line from tag BlazorNative at the default level", Assert.Single(Strings(r[2])));
        Assert.Contains("control failed: no Fully drawn line", Assert.Single(Strings(r[3])));
        Assert.Contains("control failed: no Fully drawn line for io.blazornative.shell", Assert.Single(Strings(r[4])));
    }

    // ActivityTaskManager prints head's component short, because its class starts with its package, and
    // the backport's in full, because its package carries the .v015 suffix and its class does not.
    [Fact]
    public async Task AndroidFullyDrawn_AcceptsTheSelectedAppInEitherForm_AndRedsAnotherAppOrABadTiming()
    {
        string Drawn(string component, string time) => $"11-23 10:00:01.000   600   650 I ActivityTaskManager: Fully drawn {component}: {time}";
        string headShort = Drawn("io.blazornative.shell/.MainActivity", "+1s180ms");
        string headFull = Drawn("io.blazornative.shell/io.blazornative.shell.MainActivity", "+812ms");
        string launcher = Drawn("com.google.android.apps.nexuslauncher/.NexusLauncherActivity", "+640ms");
        string badTime = Drawn("io.blazornative.shell/.MainActivity", "+?");

        JsonElement[] r = await RunAsync("android",
            new Call("Test-LogHasFullyDrawn", new[] { headShort }, Shell, ShellActivity),
            new Call("Test-LogHasFullyDrawn", new[] { headFull }, Shell, ShellActivity),
            new Call("Test-LogHasFullyDrawn", new[] { FullyDrawnV015 }, ShellV015, ShellActivity),
            new Call("Test-LogHasFullyDrawn", new[] { FullyDrawnV015, launcher }, Shell, ShellActivity),
            new Call("Test-LogHasFullyDrawn", new[] { headShort, launcher }, ShellV015, ShellActivity),
            new Call("Test-LogHasFullyDrawn", new[] { badTime }, Shell, ShellActivity),
            new Call("Test-LogHasFullyDrawn", new[] { headShort.Replace(".MainActivity", ".OtherActivity") }, Shell, ShellActivity));

        Assert.True(r[0].GetBoolean());
        Assert.True(r[1].GetBoolean());
        Assert.True(r[2].GetBoolean());
        Assert.False(r[3].GetBoolean());
        Assert.False(r[4].GetBoolean());
        Assert.False(r[5].GetBoolean());
        Assert.False(r[6].GetBoolean());
    }

    // PerfMeasurementTest is the one test its annotation selects, so only "OK (1 test)" is a pass.
    [Fact]
    public async Task AndroidInstrumentPassed_NeedsExactlyOneTest_AndRedsZeroManyOrAFailure()
    {
        string[] one = ["INSTRUMENTATION_STATUS_CODE: 0", "OK (1 test)"];
        string[] none = ["OK (0 tests)"];
        string[] two = ["OK (2 tests)"];
        string[] failures = ["FAILURES!!!", "OK (1 test)"];
        string[] crashed = ["OK (1 test)", "INSTRUMENTATION_FAILED: io.blazornative.shell.test/io.blazornative.shell.BlazorNativeTestRunner"];

        JsonElement[] r = await RunAsync("android",
            new Call("Test-InstrumentPassed", [one]), new Call("Test-InstrumentPassed", [none]),
            new Call("Test-InstrumentPassed", [two]), new Call("Test-InstrumentPassed", [failures]),
            new Call("Test-InstrumentPassed", [crashed]));

        Assert.True(r[0].GetBoolean());
        Assert.False(r[1].GetBoolean());
        Assert.False(r[2].GetBoolean());
        Assert.False(r[3].GetBoolean());
        Assert.False(r[4].GetBoolean());
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
        // A subsystem mark outside category Perf, which the click predicate does not read: only the host window holds it.
        string[] markedHost = [.. host, OsLog("Debug", "io.blazornative", 900, "BNPERF dispatch.post seq=1 t=5 id=3")];

        JsonElement[] r = await RunAsync("ios",
            new Call("Test-Q1DefaultClick", [noMarks, host]),
            new Call("Test-Q1DefaultClick", [withMark, host]),
            new Call("Test-Q1DefaultClick", [noMarks, gatedHost]),
            new Call("Test-Q1DefaultClick", [noMarks, Array.Empty<string>()]),
            new Call("Test-Q1DefaultClick", [noMarks, markedHost]));

        Assert.Empty(Strings(r[0]));
        Assert.Contains("BNPERF line in a default-level click run", Assert.Single(Strings(r[1])));
        Assert.Contains(".NET line below Warn in a default-level click run", Assert.Single(Strings(r[2])));
        Assert.Contains("control failed: the default-level click window holds no record", Assert.Single(Strings(r[3])));
        Assert.Contains("BNPERF line in the test host's window of a default-level click run", Assert.Single(Strings(r[4])));
    }

    // q1's default-level reads must include Info records, which Test-Q1Default flags; the parsed boot and
    // click reads keep their arguments. A launch must replace a process Stop-App failed to end.
    [Fact]
    public async Task IosSimctlArgs_DefaultLevelReadsTakeInfo_AndALaunchTerminatesARunningProcess()
    {
        JsonElement[] r = await RunAsync("ios",
            new Call("Get-OsLogArgs", "UDID", "2026-11-23 10:00:00", "ndjson", "processID == 812", true),
            new Call("Get-OsLogArgs", "UDID", "2026-11-23 10:00:00", "compact", "subsystem == \"io.blazornative\"", false),
            new Call("Get-LaunchArgs", "UDID", "io.blazornative.bnhost"));

        Assert.Equal(["spawn", "UDID", "log", "show", "--debug", "--info", "--style", "ndjson", "--start", "2026-11-23 10:00:00", "--predicate", "processID == 812"], Strings(r[0]));
        Assert.Equal(["spawn", "UDID", "log", "show", "--debug", "--style", "compact", "--start", "2026-11-23 10:00:00", "--predicate", "subsystem == \"io.blazornative\""], Strings(r[1]));
        Assert.Equal(["launch", "--terminate-running-process", "UDID", "io.blazornative.bnhost"], Strings(r[2]));
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
    // BnLogLevel.fromName does not read digits. At the default level it is null, which
    // Invoke-BoundedProcess removes, so the collector's own environment cannot set the level.
    [Fact]
    public async Task LaunchEnv_CarriesTheComponent_AndTheVerboseNameOnlyAtVerbose()
    {
        JsonElement[] r = await RunAsync("ios",
            new Call("Get-LaunchEnv", true, "PerfProbe"), new Call("Get-LaunchEnv", false, "PerfProbe"));

        Dictionary<string, string?> verbose = r[0].Deserialize<Dictionary<string, string?>>()!;
        Dictionary<string, string?> quiet = r[1].Deserialize<Dictionary<string, string?>>()!;
        Assert.Equal(new Dictionary<string, string?>
        {
            ["SIMCTL_CHILD_BN_LAUNCH_COMPONENT"] = "PerfProbe",
            ["SIMCTL_CHILD_BN_LOG_LEVEL"] = "Verbose",
        }, verbose);
        Assert.Equal(new Dictionary<string, string?>
        {
            ["SIMCTL_CHILD_BN_LAUNCH_COMPONENT"] = "PerfProbe",
            ["SIMCTL_CHILD_BN_LOG_LEVEL"] = null,
        }, quiet);
    }

    // The xcodebuild env: both levels name the level the test asserts, and the default level removes
    // BN_LOG_LEVEL rather than leave it to the collector's environment.
    [Fact]
    public async Task MeasurementEnv_SetsThePerfLevelAtBothLevels_AndRemovesTheLogLevelAtTheDefault()
    {
        JsonElement[] r = await RunAsync("ios",
            new Call("Get-MeasurementEnv", 25, false), new Call("Get-MeasurementEnv", 3, true));

        Assert.Equal(new Dictionary<string, string?>
        {
            ["TEST_RUNNER_PERF_CLICKS"] = "25",
            ["TEST_RUNNER_PERF_LEVEL"] = "Verbose",
            ["TEST_RUNNER_BN_LOG_LEVEL"] = "Verbose",
        }, r[0].Deserialize<Dictionary<string, string?>>()!);
        Assert.Equal(new Dictionary<string, string?>
        {
            ["TEST_RUNNER_PERF_CLICKS"] = "3",
            ["TEST_RUNNER_PERF_LEVEL"] = "default",
            ["TEST_RUNNER_BN_LOG_LEVEL"] = null,
        }, r[1].Deserialize<Dictionary<string, string?>>()!);
    }

    // The launch reports a pid, and the boot wait accepts only that process's marks: a mark of another
    // BnHost process in the window, or of a pid that merely starts with the same digits, does not count.
    [Fact]
    public async Task BootMarksOfPid_NeedsBothMarksFromTheLaunchedProcess()
    {
        string Mark(int pid, string point) => $"2026-11-23 10:00:00.123 Db BnHost[{pid}:1a2b] [io.blazornative:Perf] BNPERF {point} seq=1 t=5";
        string[] both = ["Timestamp               Ty Process[PID:TID]", Mark(900, "boot.start"), Mark(900, "boot.content")];
        string[] split = [Mark(900, "boot.start"), Mark(901, "boot.content")];
        string[] longer = [Mark(9001, "boot.start"), Mark(9001, "boot.content")];

        JsonElement[] r = await RunAsync("ios",
            new Call("Test-BootMarksOfPid", both, 900), new Call("Test-BootMarksOfPid", split, 900),
            new Call("Test-BootMarksOfPid", split, 901), new Call("Test-BootMarksOfPid", longer, 900));

        Assert.True(r[0].GetBoolean());
        Assert.False(r[1].GetBoolean());
        Assert.False(r[2].GetBoolean());
        Assert.False(r[3].GetBoolean());
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
    public async Task ClickLogComplete_NeedsEachMarkOncePerClick_AndRedsAShortLongOrEmptyLog()
    {
        string Line(string point, int seq) => $"BNPERF {point} seq={seq} t={seq} id=3";
        string[] points = ["dispatch.post", "dispatch.enter", "dispatch.handler"];
        string[] three = [.. Enumerable.Range(1, 3).SelectMany(n => points.Select(p => Line(p, n)))];

        var calls = new List<Call> { new("Test-ClickLogComplete", three, 3), new("Test-ClickLogComplete", three, 0) };
        foreach (string p in points)
        {
            calls.Add(new("Test-ClickLogComplete", three.Where(l => l != Line(p, 3)).ToArray(), 3));
            calls.Add(new("Test-ClickLogComplete", three.Append(Line(p, 4)).ToArray(), 3));
        }

        JsonElement[] r = await RunAsync("common", [.. calls]);

        Assert.True(r[0].GetBoolean());
        Assert.False(r[1].GetBoolean());
        for (int i = 2; i < r.Length; i++)
            Assert.False(r[i].GetBoolean(), $"case {i}: a short or long {points[(i - 2) / 2]} count passed");
    }

    // ── Get-SideWriteError: meta.json may not relabel logs it does not describe ──

    [Fact]
    public async Task SideWriteError_RefusesAnExistingRunOrARelabel_AndAllowsInterleavedRunsAndAFullReplace()
    {
        string root = Path.Combine(Path.GetTempPath(), "bn-perf-side-" + Guid.NewGuid().ToString("N"));
        string Dir(string name, string? meta, params string[] logs)
        {
            string d = Path.Combine(root, name);
            Directory.CreateDirectory(d);
            if (meta is not null) File.WriteAllText(Path.Combine(d, "meta.json"), meta);
            foreach (string l in logs) File.WriteAllText(Path.Combine(d, l), "BNPERF boot.start seq=1 t=1");
            return d;
        }
        const string Meta = """{ "sdk": "10.0.100", "commit": "aaaa" }""";
        try
        {
            string missing = Path.Combine(root, "missing");
            string interleaved = Dir("interleaved", Meta, "boot-0.log", "click-0.log");
            string third = Dir("third", Meta, "boot-2.log");
            string onlyRun = Dir("onlyRun", Meta, "boot-0.log");
            string bootAndClick = Dir("bootAndClick", Meta, "boot-0.log", "click-0.log");
            string noMeta = Dir("noMeta", null, "boot-0.log");

            (JsonElement Value, string? Error)[] r = await RunCatchingAsync("common",
                // A fresh side directory.
                new Call("Get-SideWriteError", missing, "boot", new[] { 0, 1 }, false, true, "10.0.100", "aaaa"),
                // Interleaved: the next run of either kind under the same build.
                new Call("Get-SideWriteError", interleaved, "boot", new[] { 1 }, false, false, "10.0.100", "aaaa"),
                // Every requested run is checked, not only the first.
                new Call("Get-SideWriteError", third, "boot", new[] { 0, 1, 2 }, false, true, "10.0.100", "aaaa"),
                // The next run under another build would relabel the runs already there.
                new Call("Get-SideWriteError", interleaved, "boot", new[] { 1 }, false, false, "10.0.101", "aaaa"),
                new Call("Get-SideWriteError", interleaved, "boot", new[] { 1 }, false, false, "10.0.100", "bbbb"),
                // -Force -Run 0 replaces boot-0 and nothing else: the only log, so a new label is safe...
                new Call("Get-SideWriteError", onlyRun, "boot", new[] { 0 }, true, false, "10.0.101", "bbbb"),
                // ...but click-0 would keep the new label.
                new Call("Get-SideWriteError", bootAndClick, "boot", new[] { 0 }, true, false, "10.0.101", "bbbb"),
                // -Force without -Run removes the kind's logs, but not the other kind's.
                new Call("Get-SideWriteError", onlyRun, "boot", new[] { 0, 1 }, true, true, "10.0.101", "bbbb"),
                new Call("Get-SideWriteError", bootAndClick, "boot", new[] { 0, 1 }, true, true, "10.0.101", "bbbb"),
                // Logs with no meta.json cannot be shown to match.
                new Call("Get-SideWriteError", noMeta, "boot", new[] { 1 }, false, false, "10.0.100", "aaaa"));

            Assert.All(r, x => Assert.Null(x.Error));
            Assert.Equal(JsonValueKind.Null, r[0].Value.ValueKind);
            Assert.Equal(JsonValueKind.Null, r[1].Value.ValueKind);
            Assert.StartsWith($"refusing to overwrite {Path.Combine(third, "boot-2.log")}", r[2].Value.GetString());
            Assert.Contains("records sdk '10.0.100' and commit 'aaaa'", r[3].Value.GetString());
            Assert.Contains("records sdk '10.0.100' and commit 'aaaa'", r[4].Value.GetString());
            Assert.Equal(JsonValueKind.Null, r[5].Value.ValueKind);
            Assert.Contains("such as click-0.log would keep that label", r[6].Value.GetString());
            Assert.Equal(JsonValueKind.Null, r[7].Value.ValueKind);
            Assert.Contains("such as click-0.log would keep that label", r[8].Value.GetString());
            Assert.Contains("but no meta.json", r[9].Value.GetString());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // ── Initialize-SideDir: a relabel never outlives the log it replaces ─────────

    [Fact]
    public async Task InitializeSideDir_RemovesTheReplacedRunBeforeTheRelabel_AndKeepsInterleavedRuns()
    {
        string root = Path.Combine(Path.GetTempPath(), "bn-perf-init-" + Guid.NewGuid().ToString("N"));
        const string Meta = """{ "sdk": "10.0.100", "commit": "aaaa" }""";
        string Dir(string name, params string[] logs)
        {
            string d = Path.Combine(root, name);
            Directory.CreateDirectory(d);
            File.WriteAllText(Path.Combine(d, "meta.json"), Meta);
            foreach (string l in logs) File.WriteAllText(Path.Combine(d, l), "BNPERF boot.start seq=1 t=1");
            return d;
        }
        try
        {
            string relabel = Dir("relabel", "boot-0.log");
            string interleaved = Dir("interleaved", "boot-0.log", "click-0.log");
            string refused = Dir("refused", "boot-0.log", "click-0.log");

            (JsonElement Value, string? Error)[] r = await RunCatchingAsync("common",
                // -Force -Run 0 under another build: boot-0 is the only log, so the relabel is allowed, and
                // the old build's boot-0 must be gone before meta.json names the new one.
                new Call("Initialize-SideDir", relabel, "boot", new[] { 0 }, true, false, "10.0.101", "bbbb"),
                // The next interleaved run under the same build touches no log.
                new Call("Initialize-SideDir", interleaved, "boot", new[] { 1 }, false, false, "10.0.100", "aaaa"),
                // A relabel click-0 would keep is refused before anything is written.
                new Call("Initialize-SideDir", refused, "boot", new[] { 0 }, true, false, "10.0.101", "bbbb"));

            Assert.Null(r[0].Error);
            Assert.Equal(relabel, r[0].Value.GetString());
            Assert.False(File.Exists(Path.Combine(relabel, "boot-0.log")), "the old build's boot-0.log survived the relabel");
            Assert.Contains("\"bbbb\"", File.ReadAllText(Path.Combine(relabel, "meta.json")));

            Assert.Null(r[1].Error);
            Assert.True(File.Exists(Path.Combine(interleaved, "boot-0.log")));
            Assert.True(File.Exists(Path.Combine(interleaved, "click-0.log")));
            Assert.Contains("\"aaaa\"", File.ReadAllText(Path.Combine(interleaved, "meta.json")));

            Assert.Contains("such as click-0.log would keep that label", r[2].Error);
            Assert.True(File.Exists(Path.Combine(refused, "boot-0.log")));
            Assert.Equal(Meta, File.ReadAllText(Path.Combine(refused, "meta.json")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // A relative -Out names a directory under PowerShell's location, which Set-Location moves and .NET's
    // current directory does not follow: meta.json must land beside the logs, without a BOM.
    [Fact]
    public async Task InitializeSideDir_ResolvesARelativePathFromThePowerShellLocation()
    {
        string root = Path.Combine(Path.GetTempPath(), "bn-perf-loc-" + Guid.NewGuid().ToString("N"));
        string rel = "rel-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(root);
        try
        {
            JsonElement[] r = await RunAsync("common",
                new Call("Invoke-Command", Block("Set-Location -LiteralPath '" + root.Replace("'", "''") + "'")),
                new Call("Initialize-SideDir", rel + "/head", "boot", new[] { 0 }, false, true, "10.0.100", "aaaa"));

            string side = Path.Combine(root, rel, "head");
            Assert.Equal(Path.GetFullPath(side), Path.GetFullPath(r[1].GetString()!));
            byte[] bytes = File.ReadAllBytes(Path.Combine(side, "meta.json"));
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "meta.json has a BOM");
            Assert.Contains("\"aaaa\"", System.Text.Encoding.UTF8.GetString(bytes));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MetaValue_RefusesEmptyCommaPipeQuoteAndNewline()
    {
        JsonElement[] r = await RunAsync("common",
            new Call("Test-MetaValue", "Sdk", "10.0.100"), new Call("Test-MetaValue", "Sdk", " "),
            new Call("Test-MetaValue", "Sdk", "10,0"), new Call("Test-MetaValue", "Commit", "a|b"),
            new Call("Test-MetaValue", "Commit", "a\"b"), new Call("Test-MetaValue", "Commit", "a\nb"));

        Assert.Equal(JsonValueKind.Null, r[0].ValueKind);
        Assert.Equal("-Sdk is required for the boot and click kinds", r[1].GetString());
        for (int i = 2; i < r.Length; i++)
            Assert.True(r[i].ValueKind == JsonValueKind.String && r[i].GetString()!.Contains("must not contain commas, pipes, double quotes or newlines"),
                $"case {i} passed: {r[i]}");
    }

    // ── The boot log's records ──────────────────────────────────────────────────

    // The parser takes any `Fully drawn` line, and refuses a second: another component's record must not
    // reach it, and every record of the selected activity must.
    [Fact]
    public async Task SelectBootRecords_DropsAnotherComponentsFullyDrawn_AndKeepsEveryOneOfTheSelected()
    {
        string launcher = "11-23 10:00:01.000   600   650 I ActivityTaskManager: Fully drawn com.google.android.apps.nexuslauncher/.NexusLauncherActivity: +640ms";
        string mark = Logcat("V", "BlazorNative/Perf", "BNPERF boot.start seq=1 t=5");
        string[] lines = [mark, launcher, FullyDrawn, FullyDrawnV015, FullyDrawn];

        JsonElement[] r = await RunAsync("android",
            new Call("Select-BootRecords", lines, Shell, ShellActivity),
            new Call("Select-BootRecords", lines, ShellV015, ShellActivity));

        Assert.Equal([mark, FullyDrawn, FullyDrawn], Strings(r[0]));
        Assert.Equal([mark, FullyDrawnV015], Strings(r[1]));
    }

    // ── Wait-ForLog: each read gets only the budget left ────────────────────────

    // The read is stubbed to take 1.2 s and record the timeout it was handed. A 3 s wait must hand the first
    // read 3 s and each later one less, and give up near 3 s; a fixed per-read timeout would let one read
    // carry the wait far past it.
    [Theory]
    [InlineData("android", "function script:Get-Logcat { param([int]$TimeoutSeconds = 60) $global:Budgets.Add($TimeoutSeconds); Start-Sleep -Milliseconds 1200; return , @('no mark') }", new object[0])]
    [InlineData("ios", "function script:Get-OsLog { param([string]$Start, [string]$Style = 'compact', [string]$Predicate = '', [switch]$Info, [int]$TimeoutSeconds = 90) $global:Budgets.Add($TimeoutSeconds); Start-Sleep -Milliseconds 1200; return , @('no mark') }", new object[] { "2026-11-23 10:00:00" })]
    public async Task WaitForLog_HandsEachReadTheBudgetLeft(string script, string stub, object[] leading)
    {
        const string Reset = "$global:Budgets = New-Object System.Collections.Generic.List[int]";
        var clock = System.Diagnostics.Stopwatch.StartNew();
        (JsonElement Value, string? Error)[] r = await RunCatchingAsync(script,
            new Call("Invoke-Command", Block(Reset + "; " + stub)),
            new Call("Wait-ForLog", [.. leading, Block("param($l) $false"), "the mark", 3]),
            new Call("Invoke-Command", Block("$global:Budgets.ToArray()")),
            new Call("Invoke-Command", Block(Reset)),
            new Call("Wait-ForLog", [.. leading, Block("param($l) $global:Budgets.Count -ge 2"), "the mark", 30]));

        Assert.Equal("timed out after 3s waiting for the mark", r[1].Error);
        int[] budgets = [.. r[2].Value.EnumerateArray().Select(e => e.GetInt32())];
        Assert.True(budgets.Length >= 2, $"only {budgets.Length} read(s) in 3 s");
        Assert.Equal(3, budgets[0]);
        for (int i = 1; i < budgets.Length; i++)
            Assert.True(budgets[i] < budgets[i - 1] && budgets[i] >= 1, $"budgets {string.Join(",", budgets)}");
        // A match is returned as the read's lines.
        Assert.Null(r[4].Error);
        Assert.Equal(["no mark"], Strings(r[4].Value));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(45), $"the probe took {clock.Elapsed}");
    }

    // ── interleave.ps1's Stop-Side: bounded calls, and the state decides ────────

    private const string StopSideStub = """
        $global:Calls = New-Object System.Collections.Generic.List[string]
        function script:Invoke-BoundedProcess {
            param([string]$File, [string[]]$ArgumentList, [int]$TimeoutSeconds = 30, [hashtable]$EnvVars = @{})
            $global:Calls.Add("$File $($ArgumentList -join ' ') @$TimeoutSeconds")
            if ($ArgumentList -contains 'terminate') {
                if ($global:TerminateThrows) { throw 'xcrun simctl terminate timed out after 15s' }
                return [pscustomobject]@{ ExitCode = 1; Stdout = ''; Stderr = 'found nothing to terminate' }
            }
            if ($ArgumentList -contains 'launchctl') {
                $global:Lists++
                $col = if ($global:Lists -le $global:RunningFor) { '812' } else { '-' }
                return [pscustomobject]@{ ExitCode = 0; Stdout = "PID`tStatus`tLabel`n$col`t0`tUIKitApplication:io.x[1a2b][rb-legacy]"; Stderr = '' }
            }
            return [pscustomobject]@{ ExitCode = $global:AdbExit; Stdout = ''; Stderr = 'adb said no' }
        }
        """;

    private static object StopSideCase(string platform, int adbExit = 0, int runningFor = 0, bool terminateThrows = false) => Block(
        $"$global:Calls.Clear(); $global:Lists = 0; $global:AdbExit = {adbExit}; $global:RunningFor = {runningFor}; "
        + $"$global:TerminateThrows = ${terminateThrows.ToString().ToLowerInvariant()}; "
        + $"$script:Platform = '{platform}'; $script:Serial = 'SERIAL'; $script:Udid = 'UDID'");

    [Fact]
    public async Task StopSide_BoundsEveryToolCall_AndJudgesTheSimulatorByItsState()
    {
        object side = new { Id = "io.x" };
        const string Calls = "$global:Calls.ToArray()";
        (JsonElement Value, string? Error)[] r = await RunCatchingAsync("interleave",
            new Call("Invoke-Command", Block(StopSideStub)),
            // 1-2: Android stops the app and its test package; a failed force-stop throws.
            new Call("Invoke-Command", StopSideCase("android")),
            new Call("Stop-Side", side),
            new Call("Invoke-Command", Block(Calls)),
            new Call("Invoke-Command", StopSideCase("android", adbExit: 1)),
            new Call("Stop-Side", side),
            // 6-7: terminate exits 1, the app is reaped two polls later: stopped.
            new Call("Invoke-Command", StopSideCase("ios", runningFor: 2)),
            new Call("Stop-Side", side),
            new Call("Invoke-Command", Block(Calls)),
            // 9-10: terminate timed out, but the app is not running: stopped.
            new Call("Invoke-Command", StopSideCase("ios", terminateThrows: true)),
            new Call("Stop-Side", side),
            new Call("Invoke-Command", Block(Calls)),
            // 12-13: still running past the grace: throws.
            new Call("Invoke-Command", StopSideCase("ios", runningFor: 1000)),
            new Call("Stop-Side", side, 1));

        Assert.All(r[..5], x => Assert.Null(x.Error));
        Assert.Equal(["adb -s SERIAL shell am force-stop io.x @30", "adb -s SERIAL shell am force-stop io.x.test @30"], Strings(r[3].Value));
        Assert.StartsWith("adb am force-stop io.x failed with exit 1", r[5].Error);

        Assert.Null(r[7].Error);
        Assert.Equal(
            ["xcrun simctl terminate UDID io.x @15", .. Enumerable.Repeat("xcrun simctl spawn UDID launchctl list @30", 3)],
            Strings(r[8].Value));

        Assert.Null(r[10].Error);
        Assert.Equal(["xcrun simctl terminate UDID io.x @15", "xcrun simctl spawn UDID launchctl list @30"], Strings(r[11].Value));

        Assert.StartsWith("io.x is still running after simctl terminate (exit 1 found nothing to terminate)", r[13].Error);
    }

    // ── Invoke-BoundedProcess: one deadline for the exit and both pipes ─────────

    // A null -EnvVars entry removes the variable from the child, so the collector's own environment cannot
    // set a level the default runs must not have.
    [Fact]
    public async Task BoundedProcess_RemovesANullEnvVar_FromTheChild()
    {
        const string Child = "'-NoProfile', '-Command', '[Console]::Out.Write([Environment]::GetEnvironmentVariables().Contains(''BN_PROBE_LEAK''))'";
        JsonElement[] r = await RunAsync("common",
            new Call("Invoke-Command", Block("$env:BN_PROBE_LEAK = 'Verbose'; (Invoke-BoundedProcess 'pwsh' @(" + Child + ") 30 @{ BN_PROBE_LEAK = $null }).Stdout")),
            new Call("Invoke-Command", Block("(Invoke-BoundedProcess 'pwsh' @(" + Child + ") 30 @{}).Stdout")));

        Assert.Equal("False", r[0].GetString());
        Assert.Equal("True", r[1].GetString());
    }

    private static string HoldStdout => Path.Combine(
        BnRepo.Root(), "tests", "BlazorNative.Runtime.Tests", "TestData", "hold-stdout.ps1");

    [Fact]
    public async Task BoundedProcess_KeepsBothStreamsOfANormalExit()
    {
        (JsonElement Value, string? Error)[] r = await RunCatchingAsync("common",
            new Call("Invoke-BoundedProcess", "pwsh",
                new[] { "-NoProfile", "-Command", "[Console]::Out.Write('out'); [Console]::Error.Write('err'); exit 3" }, 30));

        Assert.Null(r[0].Error);
        Assert.Equal(3, r[0].Value.GetProperty("ExitCode").GetInt32());
        Assert.Equal("out", r[0].Value.GetProperty("Stdout").GetString());
        Assert.Equal("err", r[0].Value.GetProperty("Stderr").GetString());
    }

    // hold-stdout.ps1's launcher role starts a sleeper that inherits its stdout, and exits at once: the
    // process is gone, its pipe is not. Without the shared deadline the read waits for the sleeper's 600 s,
    // and the probe outlives its bound.
    [Fact]
    public async Task BoundedProcess_ThrowsAtTheDeadline_WhenAProcessItLeftHoldsThePipe()
    {
        string pidFile = Path.Combine(Path.GetTempPath(), "bn-perf-hold-" + Guid.NewGuid().ToString("N") + ".pids");
        DateTime factStart = DateTime.Now;
        try
        {
            // The probe kills the sleeper itself before it exits: on Windows the sleeper also inherits the
            // probe's own stdout, and the probe's run would not end while it lives.
            string killSleeper = "foreach ($l in @(Get-Content -LiteralPath '" + pidFile.Replace("'", "''") + "')) "
                + "{ $p = $l.Trim() -split ' '; if ($p[0] -eq 'sleeper') { Stop-Process -Id ([int]$p[1]) -Force -ErrorAction SilentlyContinue } }";
            (JsonElement Value, string? Error)[] r = await RunCatchingAsync("common",
                new Call("Invoke-BoundedProcess", "pwsh",
                    new[] { "-NoProfile", "-File", HoldStdout, "-PidFile", pidFile, "-Role", "launcher" }, 15),
                new Call("Invoke-Command", Block(killSleeper)));

            Assert.Null(r[1].Error);
            Assert.NotNull(r[0].Error);
            Assert.Contains("timed out after 15s: it exited with 0, but a process it left running held its output open", r[0].Error);
        }
        finally
        {
            foreach (string line in File.Exists(pidFile) ? File.ReadAllLines(pidFile) : [])
            {
                string[] parts = line.Trim().Split(' ');
                if (parts.Length != 2 || parts[0] != "sleeper" || !int.TryParse(parts[1], out int pid)) continue;
                try
                {
                    using var p = System.Diagnostics.Process.GetProcessById(pid);
                    if (!p.HasExited && p.StartTime >= factStart) p.Kill();
                }
                catch (ArgumentException) { /* already gone */ }
                catch (InvalidOperationException) { /* exited meanwhile */ }
                catch (System.ComponentModel.Win32Exception) { /* not ours to read, so not ours to kill */ }
            }
            try { File.Delete(pidFile); } catch (IOException) { /* temp file */ }
        }
    }

    // ── interleave.ps1's Invoke-WithSideCleanup ──────────────────────────────────

    [Fact]
    public async Task SideCleanup_StopsBothSidesAfterAFailure_AndKeepsTheCollectionError()
    {
        const string Reset = "$global:PerfStops = New-Object System.Collections.Generic.List[string]";
        const string Read = "$global:PerfStops.ToArray()";
        object stopFailingHead = Block("param($s) $global:PerfStops.Add($s); if ($s -eq 'head') { throw 'stopping head failed' }");
        object stop = Block("param($s) $global:PerfStops.Add($s)");
        string[] sides = ["head", "v015"];

        (JsonElement Value, string? Error)[] r = await RunCatchingAsync("interleave",
            new Call("Invoke-Command", Block(Reset)),
            new Call("Invoke-WithSideCleanup", sides, Block("throw 'click run 3 failed'"), stopFailingHead),
            new Call("Invoke-Command", Block(Read)),
            new Call("Invoke-Command", Block(Reset)),
            new Call("Invoke-WithSideCleanup", sides, Block("$null"), stopFailingHead),
            new Call("Invoke-Command", Block(Read)),
            new Call("Invoke-Command", Block(Reset)),
            new Call("Invoke-WithSideCleanup", sides, Block("$null"), stop),
            new Call("Invoke-Command", Block(Read)));

        // The collection failed and stopping head failed: v015 is still stopped, and the collection's error survives.
        Assert.Equal("click run 3 failed", r[1].Error);
        Assert.Equal(["head", "v015"], Strings(r[2].Value));
        // The collection passed and stopping head failed: v015 is still stopped, and the stop error is thrown.
        Assert.Equal("stopping head failed", r[4].Error);
        Assert.Equal(["head", "v015"], Strings(r[5].Value));
        // Both pass.
        Assert.Null(r[7].Error);
        Assert.Equal(["head", "v015"], Strings(r[8].Value));
    }
}
