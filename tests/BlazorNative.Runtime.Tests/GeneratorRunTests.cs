using System.Diagnostics;
using System.Text.RegularExpressions;
using BlazorNative.Tests.Shared;
using Xunit.Sdk;

namespace BlazorNative.Runtime.Tests;

/// <summary>
/// PIN for <see cref="BoundedScript.RunBounded"/>, #454: a script whose output pipe is
/// held open by a descendant FAILS, NAMED, within the bound. It does not hang.
///
/// <para>The stub, TestData/hold-stdout.ps1, has the 2026-10-15 dumps' shape. The
/// stub's pwsh runs a native command piped to Out-Host, the way generate-reference.ps1
/// runs <c>dotnet publish</c>. That command, the launcher, starts a sleeper which
/// inherits its stdout, and then exits. The stub is left waiting for an EOF that never
/// comes, exactly the pipeline thread the dump shows.</para>
///
/// <para>ANCHOR, in the same fact: before the run returns, the launcher has exited and
/// both the stub and the sleeper are alive. So the pipe really is held when the bound
/// fires, and the named failure is the bound's work, not a crash.</para>
///
/// <para>DOES NOT COVER:
/// <list type="bullet">
/// <item>The sleeper. It was re-parented when the launcher exited, so no tree kill
/// can reach it, on Linux or Windows. This fact kills it in <c>finally</c>, and only
/// it: by pid, without its tree, and only if the process holding that pid started
/// after the fact did, so it never kills a process older than the fact. The same limit applies to a re-parented MSBuild
/// node, and the failure message says how to clear one.</item>
/// <item>The tree kill, where <c>pwsh</c> is the real executable. The stub's pwsh dies
/// under a tree kill but not a plain one only where <c>pwsh</c> is the dotnet global-tool
/// shim, with the real pwsh as its child, as on the dev host. Where <c>pwsh</c> is the
/// real executable, as on Windows or a native pwsh install such as GitHub's ubuntu
/// runners, a plain kill passes too, so there the tree kill is effectively
/// unpinned.</item>
/// <item>What held the pipe in the real hangs. A reused MSBuild node is the leading
/// suspect, not proven; #530 tracks it. This pins only that any such hang is
/// bounded and named.</item>
/// </list></para>
/// </summary>
public sealed class GeneratorRunTests
{
    /// <summary>Read from the repo tree through <see cref="BnRepo"/>, as PinPopulationTests
    /// requires of every test that reaches it, so the stub needs no copy step.</summary>
    private static string Stub => Path.Combine(
        BnRepo.Root(), "tests", "BlazorNative.Runtime.Tests", "TestData", "hold-stdout.ps1");

    /// <summary>The bound given to <see cref="BoundedScript.RunBounded"/>, and the outer
    /// window the fact waits for the run in. The held-pipe shape needs three pwsh
    /// start-ups in sequence, stub, launcher and sleeper, before the anchor can hold;
    /// these leave room for that on a loaded runner. Nothing here measures a runner's
    /// start-up time.</summary>
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan Outer = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task AChildThatLeavesItsPipeHeld_FailsNamedWithinTheBound_AndLeavesNoProcess()
    {
        Assert.True(File.Exists(Stub), $"the stub is missing: {Stub}");
        string pidFile = Path.Combine(
            Path.GetTempPath(), "bn-454-stub-" + Guid.NewGuid().ToString("N") + ".pids");

        DateTime factStart = DateTime.Now;
        try
        {
            var clock = Stopwatch.StartNew();
            Task<GeneratorRun> run = Task.Run(() =>
                BoundedScript.RunBounded(Stub, ["-PidFile", pidFile], Bound));

            // ANCHOR: the launcher is gone, the stub and the sleeper are alive, and the
            // run has not returned. Polled, because each role is a pwsh start-up.
            Dictionary<string, int> pids = new();
            while (!run.IsCompleted && clock.Elapsed < Outer)
            {
                pids = ReadPids(pidFile);
                if (pids.Count == 3 && !IsAlive(pids["launcher"])) break;
                await Task.Delay(100);
            }
            Assert.True(pids.Count == 3,
                $"the stub recorded {pids.Count} of its 3 roles before the run returned; "
                + "the held-pipe shape never formed");
            Assert.False(IsAlive(pids["launcher"]), "the launcher never exited");
            Assert.False(run.IsCompleted,
                "the run returned before the anchor held, so the bound was not what ended it");
            Assert.True(IsAlive(pids["stub"]), "the stub's pwsh is not alive: nothing is waiting on the pipe");
            Assert.True(IsAlive(pids["sleeper"]), "the sleeper is not alive: nothing holds the pipe");

            // THE BOUND: a named failure, within the outer window of the start.
            TimeSpan left = Outer - clock.Elapsed;
            Task first = await Task.WhenAny(run, Task.Delay(left > TimeSpan.Zero ? left : TimeSpan.Zero));
            Assert.True(first == run,
                $"RunBounded did not return within {Outer.TotalSeconds} s of the start ({clock.Elapsed}): a held pipe hangs it");
            var ex = await Assert.ThrowsAsync<XunitException>(() => run);
            Assert.Contains("did not finish within", ex.Message);
            Assert.Contains("#454", ex.Message);

            // THE TREE: the stub's own pwsh is dead. Where `pwsh` is the dotnet global-tool
            // shim, as on the dev host, the stub is the shim's child, so this needs the tree
            // kill. A native pwsh install, such as GitHub's ubuntu runners, behaves like
            // Windows here.
            Assert.True(await DiesWithin(pids["stub"], TimeSpan.FromSeconds(15)),
                $"the stub's pwsh (pid {pids["stub"]}) survived the bound's kill");
        }
        finally
        {
            if (ReadPids(pidFile).TryGetValue("sleeper", out int sleeper)) KillOwnSleeper(sleeper, factStart);
            try { File.Delete(pidFile); } catch (IOException) { /* temp file */ }
        }
    }

    // ── node reuse off in the generator: #454's likely fix ──────────────────────

    /// <summary>An MSBuild call in command position: at the start of a statement, or
    /// after <c>;</c>, <c>|</c>, a brace, a paren or <c>=</c>, or after <c>&amp;</c>
    /// with the command optionally quoted. <c>dotnet</c> and <c>msbuild</c> may carry
    /// <c>.exe</c>. A string that only names one, such as a
    /// <c>throw "dotnet publish failed"</c>, is not a call.</summary>
    private static readonly Regex MsBuildCall = new(
        @"(?:(?:^|[;|{}(=])\s*(?<q>)|&\s*(?<q>['""]?))"
        + @"(?:dotnet(?:\.exe)?\k<q>\s+(?:publish|build|msbuild|pack|test|restore|clean|run)|msbuild(?:\.exe)?\k<q>)"
        + @"(?![\w.-])",
        RegexOptions.IgnoreCase);

    private static readonly Regex NodeReuseEnv = new(
        @"^\s*\$env:MSBUILDDISABLENODEREUSE\s*=\s*'1'\s*$", RegexOptions.IgnoreCase);

    /// <summary>The script's code lines, numbered: the comment-help block and
    /// whole-line comments are dropped, so prose that names a command is not a call.</summary>
    private static List<(int Line, string Text)> CodeLines(string script)
    {
        var code = new List<(int, string)>();
        bool inBlock = false;
        string[] lines = script.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string t = lines[i].Trim();
            if (inBlock) { if (t.Contains("#>", StringComparison.Ordinal)) inBlock = false; continue; }
            if (t.StartsWith("<#", StringComparison.Ordinal))
            {
                inBlock = !t.Contains("#>", StringComparison.Ordinal);
                continue;
            }
            if (t.Length == 0 || t.StartsWith('#')) continue;
            code.Add((i + 1, lines[i].TrimEnd('\r')));
        }
        return code;
    }

    /// <summary>Every MSBuild call line, and the subset that lacks -nodeReuse:false.</summary>
    private static (List<string> Calls, List<string> WithReuse) MsBuildCalls(string script)
    {
        var calls = CodeLines(script).Where(l => MsBuildCall.IsMatch(l.Text))
            .Select(l => $"{l.Line}: {l.Text.Trim()}").ToList();
        var withReuse = calls
            .Where(c => !c.Contains("-nodeReuse:false", StringComparison.OrdinalIgnoreCase)).ToList();
        return (calls, withReuse);
    }

    private static string GeneratorScript()
        => File.ReadAllText(Path.Combine(BnRepo.Root(), "scripts", "generate-reference.ps1"));

    /// <summary>
    /// PIN, #454's likely fix: every MSBuild call in generate-reference.ps1 passes
    /// -nodeReuse:false, and the script sets MSBUILDDISABLENODEREUSE=1 before the
    /// first one, so no MSBuild worker node the publish starts outlives it. The
    /// 2026-10-15 Linux hang ran with node reuse on, and an outliving node is the
    /// leading suspect for the held pipe. Whatever the cause, the bound in
    /// <see cref="BoundedScript"/> turns a stall into a named failure.
    ///
    /// <para>DOES NOT COVER: whether node reuse caused the hangs; it is not proven,
    /// and #530 tracks it. Calls the recogniser does not see: one hidden behind a
    /// variable, such as <c>&amp; $tool publish</c>; one built as a string, such as
    /// <c>Invoke-Expression "dotnet publish …"</c>; and one started through
    /// <c>Start-Process dotnet -ArgumentList publish</c>. For all of them the environment
    /// variable still covers it.</para>
    /// </summary>
    [Fact]
    public void GenerateReference_RunsEveryMSBuildCallWithNodeReuseOff()
    {
        string script = GeneratorScript();
        var (calls, withReuse) = MsBuildCalls(script);

        Assert.True(calls.Count > 0,
            "found no MSBuild call in scripts/generate-reference.ps1; the script's publish is "
            + "its whole point, so the recogniser is broken, not the script clean");
        Assert.True(withReuse.Count == 0,
            "an MSBuild call in scripts/generate-reference.ps1 leaves node reuse on (#454): a reused "
            + "worker node can outlive the publish and is the leading suspect for a held pipe. "
            + "Add -nodeReuse:false:\n  "
            + string.Join("\n  ", withReuse));

        var code = CodeLines(script);
        int env = code.FindIndex(l => NodeReuseEnv.IsMatch(l.Text));
        int first = code.FindIndex(l => MsBuildCall.IsMatch(l.Text));
        Assert.True(env >= 0,
            "scripts/generate-reference.ps1 no longer sets $env:MSBUILDDISABLENODEREUSE = '1' (#454)");
        Assert.True(env < first,
            $"scripts/generate-reference.ps1 sets MSBUILDDISABLENODEREUSE at line {code[env].Line}, "
            + $"after its first MSBuild call at line {code[first].Line}");
    }

    /// <summary>NON-VACUITY for the pin above: a planted call without the switch is
    /// caught, and so is one after <c>&amp;</c>, quoted, assigned or spelled
    /// <c>dotnet.exe</c>. The same call with the switch is not caught, and a comment
    /// or a string that names one is ignored.</summary>
    [Fact]
    public void GenerateReference_NodeReusePin_CatchesAPlantedCall()
    {
        string script = GeneratorScript();

        var planted = MsBuildCalls(script + "\ndotnet build $project -c Release | Out-Host\n");
        Assert.Contains(planted.WithReuse, c => c.Contains("dotnet build $project", StringComparison.Ordinal));

        var switched = MsBuildCalls(script + "\ndotnet build $project -c Release -nodeReuse:false | Out-Host\n");
        Assert.Empty(switched.WithReuse);

        var commented = MsBuildCalls(script + "\n# dotnet build $project\n");
        Assert.Empty(commented.WithReuse);

        var named = MsBuildCalls(script + "\nthrow \"dotnet build failed\"\n");
        Assert.Empty(named.WithReuse);

        var piped = MsBuildCalls(script + "\nGet-Item x; & msbuild app.proj\n");
        Assert.Contains(piped.WithReuse, c => c.Contains("msbuild app.proj", StringComparison.Ordinal));

        var assigned = MsBuildCalls(script + "\n$out = dotnet publish $project -c Release\n");
        Assert.Contains(assigned.WithReuse, c => c.Contains("$out = dotnet publish", StringComparison.Ordinal));

        var exe = MsBuildCalls(script + "\ndotnet.exe build $project\n");
        Assert.Contains(exe.WithReuse, c => c.Contains("dotnet.exe build", StringComparison.Ordinal));

        var quoted = MsBuildCalls(script + "\n& 'dotnet' publish $project\n");
        Assert.Contains(quoted.WithReuse, c => c.Contains("& 'dotnet' publish", StringComparison.Ordinal));

        var assignedString = MsBuildCalls(script + "\n$msg = \"dotnet build failed\"\n");
        Assert.Empty(assignedString.WithReuse);
    }

    private static Dictionary<string, int> ReadPids(string pidFile)
    {
        var pids = new Dictionary<string, int>(StringComparer.Ordinal);
        string[] lines;
        try { lines = File.ReadAllLines(pidFile); }
        catch (IOException) { return pids; }   // absent, or mid-write
        foreach (string line in lines)
        {
            string[] parts = line.Trim().Split(' ');
            if (parts.Length == 2 && int.TryParse(parts[1], out int pid)) pids[parts[0]] = pid;
        }
        return pids;
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private static async Task<bool> DiesWithin(int pid, TimeSpan limit)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < limit)
        {
            if (!IsAlive(pid)) return true;
            await Task.Delay(100);
        }
        return !IsAlive(pid);
    }

    /// <summary>Kills the orphaned sleeper, and nothing else. The launcher has exited,
    /// and the stub is normally dead from the bound's kill; on a failure path, such as
    /// a kill that threw or did not reach the stub, it may still be alive, and it then
    /// exits on its own once the sleeper's pipe closes. Either pid may be recycled,
    /// within seconds on Windows, so neither is touched. The sleeper is killed alone,
    /// without a tree, and only if the process now holding its pid started after this
    /// fact did, so it never kills a process older than the fact.</summary>
    private static void KillOwnSleeper(int pid, DateTime factStart)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (!p.HasExited && p.StartTime >= factStart) p.Kill();
        }
        catch (ArgumentException) { /* already gone */ }
        catch (InvalidOperationException) { /* exited meanwhile */ }
        catch (System.ComponentModel.Win32Exception) { /* not ours to read, so not ours to kill */ }
    }
}
