using System.Diagnostics;
using System.Runtime.InteropServices;
using BlazorNative.Core;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// BnLogExitDrainTests — Phase 18.1, #564's final review, I1.
//
// THE CLAIM. Every BnLog level is queued, Error included, and the owner accepted
// that on one condition: the queue drains at the exits the framework controls.
// There are three, each bounded by BnLogQueue.ExitDrainBudget:
//   - blazornative_shutdown, in a `finally` after HostSession.Shutdown. On NativeAOT
//     a library may never see ProcessExit, so this is the exit that matters most;
//   - AppDomain.ProcessExit;
//   - AppDomain.UnhandledException.
// Before these facts, a comment stated each drain and nothing redded if one was
// deleted.
//
// HOW. The shutdown fact runs the real export, through its own function pointer,
// with Console.Error replaced by GatedWriter, so BnLog's drain thread is parked
// inside a write that cannot finish. The two hook facts cannot end their own test
// host, so they run BlazorNative.LogExitProbe, a child process that logs one line
// behind a slow stderr and then ends, and read the child's real stderr.
//
// DOES NOT COVER: whether ProcessExit is raised under NativeAOT on a device. These
// facts run on CoreCLR, where it is. The device lanes are the evidence for that.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>blazornative_shutdown drains BnLog's queue before it returns, bounded.</summary>
[Collection("host-session")]
public sealed unsafe class BnLogShutdownDrainTests
{
    /// <summary>18.1, #564: SHUTDOWN WAITS FOR THE LOG QUEUE TO DRAIN, WITHIN ITS BUDGET, AND
    /// THE LINE ARRIVES ONCE STDERR OPENS. Three claims, one per clause of the name. The
    /// export does not return while a line is still queued behind a blocked stderr; it gives
    /// up after <see cref="BnLogQueue.ExitDrainBudget"/> rather than hold the shell's call;
    /// and giving up loses nothing still queued, so the line is written once stderr drains.
    /// Two lines are logged: the drain thread is parked writing the first, so the second is
    /// still in the queue when the export gives up, and it is the one the last claim reads.
    ///
    /// DOES NOT COVER: a session that is running at shutdown. None is mounted here, so
    /// <c>HostSession.Shutdown</c> returns at once and the time the export takes is the
    /// drain's.</summary>
    [Fact]
    public Task Shutdown_WaitsForTheLogQueueToDrain_WithinItsBudget_AndTheLineArrivesOnceStderrOpens()
        => OffPool.Run(() =>
        {
            const string Line = "[BN|E|BnLogExitDrainTests] logged-before-shutdown";
            TimeSpan budget = BnLogQueue.ExitDrainBudget;
            TimeSpan waitedAtLeast = budget - TimeSpan.FromMilliseconds(100);
            TimeSpan bound = TimeSpan.FromSeconds(2);

            Action<BnLogLevel, string, string>? originalSink = BnLog.Sink;
            TextWriter originalErr = Console.Error;
            var stderr = new GatedWriter();
            using var returned = new ManualResetEventSlim(false);
            long elapsedTicks = -1;
            bool entered;
            bool returnedInBound;
            Thread? worker = null;
            try
            {
                BnLog.Sink = null;
                Console.SetError(stderr);
                BnLog.Error("BnLogExitDrainTests", "held-in-the-writer");
                entered = stderr.WaitEntered(TimeSpan.FromSeconds(10));
                BnLog.Error("BnLogExitDrainTests", "logged-before-shutdown");

                var clock = Stopwatch.StartNew();
                worker = new Thread(() =>
                {
                    ShutdownExport();
                    Interlocked.Exchange(ref elapsedTicks, clock.Elapsed.Ticks);
                    returned.Set();
                })
                { IsBackground = true, Name = "bnlog-shutdown-drain" };
                worker.Start();
                returnedInBound = returned.Wait(bound);
            }
            finally
            {
                Console.SetError(originalErr);
                stderr.Open();
                BnLog.Sink = originalSink;
            }
            // Let the export leave before `returned` is disposed, whichever way it went.
            worker?.Join(TimeSpan.FromSeconds(10));
            long ticks = Interlocked.Read(ref elapsedTicks);
            string took = ticks < 0 ? $"had not returned after {bound}" : $"returned after {TimeSpan.FromTicks(ticks).TotalMilliseconds:F0} ms";

            Assert.True(entered, "anchor: BnLog's drain thread never reached the blocked stderr, so the line is not queued behind a held write.");
            Assert.True(!returnedInBound || TimeSpan.FromTicks(ticks) >= waitedAtLeast,
                $"blazornative_shutdown {took} while BnLog's line was still queued behind a blocked stderr; it should "
                + $"wait about ExitDrainBudget, {budget.TotalMilliseconds:F0} ms. It did not drain the queue, so on NativeAOT, "
                + "where ProcessExit may never be raised, the lines still queued at shutdown are lost (#564).");
            Assert.True(returnedInBound,
                $"blazornative_shutdown {took} while stderr stayed blocked: its drain of BnLog's queue is not bounded "
                + $"by ExitDrainBudget, {budget.TotalMilliseconds:F0} ms, so a stderr that never drains holds the shell's "
                + "shutdown call (#564).");
            Assert.True(WaitUntil(() => stderr.Lines.Contains(Line), TimeSpan.FromSeconds(10)),
                "the line still queued when blazornative_shutdown gave up never reached stderr once it opened: the "
                + $"shutdown drain discarded it; lines seen: [{string.Join(" | ", stderr.Lines)}].");
        });

    /// <summary>blazornative_shutdown, called through its own function pointer.</summary>
    private static void ShutdownExport()
    {
        delegate* unmanaged[Cdecl]<void> shutdown = &Exports.Shutdown;
        shutdown();
    }

    private static bool WaitUntil(Func<bool> condition, TimeSpan budget)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > budget) return false;
            Thread.Sleep(10);
        }
        return true;
    }
}

/// <summary>BnLog's default queue drains when its process exits, and on an unhandled
/// exception. Each fact runs BlazorNative.LogExitProbe as a child process.</summary>
public sealed class BnLogProcessExitDrainTests
{
    private const string Line = "[BN|E|LogExitProbe] logged-before-exit";
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    /// <summary>18.1, #564: AT PROCESS EXIT, THE QUEUED LINE IS WRITTEN BEFORE THE PROCESS
    /// ENDS. The probe's Main returns while BnLog's drain thread is inside a 200 ms write.
    /// The drain is a background thread, so only the ProcessExit hook keeps the line.
    ///
    /// DOES NOT COVER: NativeAOT, where ProcessExit may never be raised; that is why
    /// blazornative_shutdown drains too.</summary>
    [Fact]
    public Task AtProcessExit_TheQueuedLineIsWrittenBeforeTheProcessEnds()
        => OffPool.Run(() =>
        {
            ProbeRun run = RunProbe("exit");

            Assert.True(run.DrainInWrite && run.ExitCode == 0,
                $"anchor: the probe did not return from Main normally with BnLog's drain inside the slow write. {run}");
            Assert.True(run.Stderr.Contains(Line, StringComparison.Ordinal),
                "the line BnLog queued before the process exited never reached stderr: nothing drained the queue at "
                + $"AppDomain.ProcessExit, so a process that ends normally loses its last lines (#564). {run}");
        });

    /// <summary>18.1, #564: ON AN UNHANDLED EXCEPTION, THE QUEUED LINE IS WRITTEN BEFORE THE
    /// PROCESS ENDS. The probe's Main throws while BnLog's drain thread is inside a 200 ms
    /// write. The runtime raises UnhandledException and then aborts the process, so only that
    /// hook keeps the line.</summary>
    [Fact]
    public Task OnAnUnhandledException_TheQueuedLineIsWrittenBeforeTheProcessEnds()
        => OffPool.Run(() =>
        {
            ProbeRun run = RunProbe("unhandled");

            Assert.True(run.DrainInWrite && run.ExitCode != 0
                    && run.Stderr.Contains("an unhandled exception, on purpose", StringComparison.Ordinal),
                $"anchor: the probe's exception did not go unhandled with BnLog's drain inside the slow write. {run}");
            Assert.True(run.Stderr.Contains(Line, StringComparison.Ordinal),
                "the line BnLog queued before an unhandled exception never reached stderr: nothing drained the queue at "
                + $"AppDomain.UnhandledException, so a crash loses the lines that led up to it (#564). {run}");
        });

    private sealed record ProbeRun(int ExitCode, bool DrainInWrite, string Stdout, string Stderr)
    {
        public override string ToString()
            => $"Exit code {ExitCode}. stdout: [{Stdout.Trim()}]. stderr: [{Stderr.Trim()}].";
    }

    /// <summary>Runs the probe to its end, bounded. Never on a pool thread: callers run
    /// it through <see cref="OffPool"/> (#551).</summary>
    private static ProbeRun RunProbe(string mode)
    {
        string probe = Path.Combine(BlazorNative.Tests.Shared.BnRepo.TestBinaryDirectory(), "BlazorNative.LogExitProbe.dll");
        Assert.True(File.Exists(probe), $"the probe is not built next to the tests: {probe}. The test project references it.");

        string host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } h && File.Exists(h)
            ? h
            : RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "dotnet.exe" : "dotnet";
        var psi = new ProcessStartInfo(host)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(probe);
        psi.ArgumentList.Add(mode);
        // The crash is on purpose: no dump.
        psi.Environment["DOTNET_DbgEnableMiniDump"] = "0";

        using var p = Process.Start(psi)!;
        Task<string> stdout = p.StandardOutput.ReadToEndAsync();
        Task<string> stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(Bound) || !Task.WaitAll([stdout, stderr], Bound))
        {
            try { p.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* it exited */ }
            Assert.Fail($"the probe '{mode}' did not finish within {Bound}, and was killed.");
        }
        p.WaitForExit();
        return new ProbeRun(p.ExitCode, stdout.Result.Contains("drain-in-write", StringComparison.Ordinal),
            stdout.Result, stderr.Result);
    }
}
