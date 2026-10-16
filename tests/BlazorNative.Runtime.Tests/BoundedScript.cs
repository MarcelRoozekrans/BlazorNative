using System.Diagnostics;
using BlazorNative.Tests.Shared;
using Xunit.Sdk;

namespace BlazorNative.Runtime.Tests;

/// <summary>One finished <see cref="BoundedScript.RunBounded"/> run: the exit code, and
/// stdout and stderr interleaved in arrival order, one line per entry.</summary>
internal sealed record GeneratorRun(int ExitCode, string Log);

/// <summary>
/// THE ONE WAY A TEST SHELLS OUT TO A <c>.ps1</c> (#454). The reference fixtures used
/// to read stdout and stderr to the end and then wait for exit, with no bound. In the
/// 2026-10-01 Windows and 2026-10-15 Linux hangs, a process somewhere below the
/// script kept an output pipe open, the end never came, and the suite sat silent
/// until CI's job timeout.
///
/// <para>A run FINISHES when the process has exited AND both of its streams have
/// reached EOF, all within one deadline. Either one missing at the deadline is a
/// failure that names the script, its arguments, the bound and the last 20 lines of
/// output. It is never a wait. GeneratorRunTests pins this against a stub with the
/// dumps' shape.</para>
///
/// <para>On expiry the process tree is killed. A tree kill reaches only processes
/// still linked to the launched one: a descendant re-parented after its own parent
/// exited, such as an MSBuild node whose publish is done, is out of its reach. The
/// failure message says so and how to clear one. EOF is not awaited after the kill,
/// because such a descendant may still hold the pipe.</para>
/// </summary>
internal static class BoundedScript
{
    internal static GeneratorRun RunBounded(string script, IReadOnlyList<string> args, TimeSpan bound)
    {
        var psi = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = BnRepo.Root(),
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(script);
        foreach (string a in args) psi.ArgumentList.Add(a);

        // One locked buffer for both streams. The EOF signals are TaskCompletionSources,
        // not disposable events: a handler can still fire after this method returns,
        // and it must not touch a disposed object on a pool thread.
        var lines = new List<string>();
        var outEof = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errEof = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var p = new Process { StartInfo = psi };
        p.OutputDataReceived += (_, e) => Collect(e.Data, lines, outEof);
        p.ErrorDataReceived += (_, e) => Collect(e.Data, lines, errEof);

        var clock = Stopwatch.StartNew();
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        bool finished = p.WaitForExit(bound)
            && outEof.Task.Wait(Remaining(bound, clock))
            && errEof.Task.Wait(Remaining(bound, clock));

        if (!finished)
        {
            try { p.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* it exited; only the pipe was held */ }

            string tail;
            lock (lines) tail = string.Join("\n", lines.TakeLast(20));
            throw new XunitException(
                $"{script} {string.Join(' ', args)} did not finish within {bound}; the process tree "
                + "was killed. A child may have left a descendant holding its output pipe, as #454 "
                + "records. A descendant re-parented after its parent exited, such as an MSBuild "
                + "node, is out of a tree kill's reach and may linger.\n"
                + $"Last output:\n{tail}\n"
                + "A stray MSBuild node can be cleared with `dotnet build-server shutdown`.");
        }

        // Both EOFs are in, so this cannot wait on a held pipe; it only lets the
        // async handlers drain before the buffer is read.
        p.WaitForExit();
        lock (lines) return new GeneratorRun(p.ExitCode, string.Join("\n", lines));
    }

    private static void Collect(string? line, List<string> lines, TaskCompletionSource eof)
    {
        if (line is null) { eof.TrySetResult(); return; }
        lock (lines) lines.Add(line);
    }

    private static TimeSpan Remaining(TimeSpan bound, Stopwatch clock)
    {
        TimeSpan left = bound - clock.Elapsed;
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }
}
