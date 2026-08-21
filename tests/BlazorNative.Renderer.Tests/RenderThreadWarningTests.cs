using BlazorNative.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorNative.Renderer.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// RenderThreadWarningTests — Phase 13.2.
//
// WHAT THIS GUARDS, AND WHY IT IS A WARNING RATHER THAN AN ASSERTION.
//
// The renderer's InlineDispatcher answers CheckAccess() with an unconditional
// `true`. That looks like a lie worth fixing, and Phase 13.2 tried: the spike
// made it honest (owner-thread comparison) and the TEST HOST PROCESS DIED with a
// stack overflow, reproducibly. The captured loop:
//
//     Renderer.Dispose()
//       -> CheckAccess()                        false
//       -> Dispatcher.InvokeAsync(() => Dispose())   marshal to "the right thread"
//       -> InlineDispatcher.InvokeAsync runs it INLINE, on the same thread
//       -> Renderer.Dispose()  -> CheckAccess() false -> ... until the stack ends
//
// Blazor uses CheckAccess() for MARSHALLING, not merely for assertions. An
// inline dispatcher runs work on the CALLING thread and therefore has nowhere to
// marshal to, so it MUST claim every thread is the right one. The unconditional
// `true` is load-bearing for the dispatcher's own coherence, not an oversight.
//
// Making it honest requires giving InvokeAsync a real queue and a real owner
// thread, and that breaks the sync-mount contract the C-ABI depends on (the first
// render must complete synchronously inside the native callback window — see
// Exports.cs and MountSyncTests).
//
// So the real risk — a render batch driven from a thread that is not the one the
// sync-mount contract assumes — is DETECTED here and reported. It is never
// thrown. A throw would be a behaviour change on a path that currently works, in
// the exact area the spike proved is delicate.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Serializes every class that touches BnLog's PROCESS-WIDE Level/Sink.
/// Runtime.Tests' BnLogTests documents the same hazard and solves it the same way.</summary>
[CollectionDefinition("bnlog-global")]
public sealed class BnLogGlobalCollection { }

// WHY [Collection("bnlog-global")]: BnLog.Level and BnLog.Sink are process-wide
// statics. xUnit runs test CLASSES in parallel by default, so a capture installed
// here is installed for every class running beside it — and this suite has classes
// (StrictModeTests, DevHostBridgeEventTests) that assert on log output. Without
// this attribute the capture swallows their lines and they fail for reasons that
// have nothing to do with them. That is not hypothetical: it is what happened.
[Collection("bnlog-global")]
public sealed class RenderThreadWarningTests
{
    private const string Category = "BlazorNative.Renderer";

    private sealed class Probe : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.CloseElement();
        }
    }

    private static NativeRenderer BuildRenderer(bool strict)
    {
        var services = new ServiceCollection().AddBlazorNativeRenderer();
        var renderer = services.BuildServiceProvider().GetRequiredService<NativeRenderer>();
        renderer.StrictErrors = strict;
        return renderer;
    }

    /// <summary>Runs <paramref name="work"/> on a DEDICATED thread and waits for it.
    ///
    /// <para>NOT <c>Task.Run</c>, and the difference is not stylistic — it is two CI
    /// failures. First, the pool may INLINE the work onto the calling thread when cores
    /// are scarce, so `Task.Run` does not guarantee a different thread and the
    /// cross-thread assertion failed on a 2-core runner with both ids equal to 12.
    /// Second, pool continuations land on arbitrary pool threads — including the one
    /// running <c>RendererSpike.RenderWalk_IsAllocationFree_OnSteadyState</c>, whose
    /// <c>GC.GetAllocatedBytesForCurrentThread()</c> then measures THIS test's
    /// allocations and reports a phantom regression. A dedicated thread has neither
    /// problem: it is provably distinct and it never borrows anyone else's.</para></summary>
    private static void OnAnotherThread(Action work)
    {
        Exception? escaped = null;
        var t = new Thread(() => { try { work(); } catch (Exception ex) { escaped = ex; } })
        {
            IsBackground = true,
            Name = "bn-test-non-owner",
        };
        t.Start();
        Assert.True(t.Join(TimeSpan.FromSeconds(30)), "the non-owner thread did not finish");
        if (escaped is not null)
            throw escaped;
    }

    /// <summary>Captures BnLog lines for the duration of <paramref name="body"/>.
    /// Synchronous throughout: no await, so no continuation can escape onto a pool
    /// thread and outlive the capture.</summary>
    private static List<(BnLogLevel Level, string Message)> Capture(Action body)
    {
        var lines = new List<(BnLogLevel, string)>();
        Action<BnLogLevel, string, string>? originalSink = BnLog.Sink;
        BnLogLevel originalLevel = BnLog.Level;
        try
        {
            BnLog.Level = BnLogLevel.Verbose;   // so Debug-level lines are not filtered out
            // FORWARDS to whatever was installed. A sink that only captures is a sink that
            // SWALLOWS, and anything relying on the default stderr writer while this is
            // installed would silently see nothing.
            BnLog.Sink = (level, category, message) =>
            {
                if (category == Category)
                    lock (lines) { lines.Add((level, message)); }
                originalSink?.Invoke(level, category, message);
            };
            body();
        }
        finally
        {
            BnLog.Sink = originalSink;
            BnLog.Level = originalLevel;
        }

        lock (lines) { return [.. lines]; }
    }

    /// <summary>The reports THIS test's renderer produced, identified by its owner thread.
    ///
    /// <para>Filtering on the log category alone is not enough, and that is not a theoretical
    /// worry: BnLog is process-wide, xUnit runs classes in parallel, and any other class driving
    /// a render batch off its own thread emits the same category. `Assert.Single` then sees two
    /// and the test fails for something another class did. Every report names its owner thread,
    /// so that is the discriminator.</para></summary>
    private static List<(BnLogLevel Level, string Message)> Reports(
        List<(BnLogLevel Level, string Message)> lines, int ownerThreadId)
        => [.. lines.Where(l =>
               l.Message.Contains("render batch", StringComparison.Ordinal)
               && l.Message.Contains($"owned by thread {ownerThreadId}", StringComparison.Ordinal))];

    /// <summary>A batch driven from a thread that did not drive the first one is a WARNING
    /// under StrictErrors, and it must name BOTH threads — a report that says only "wrong
    /// thread" cannot be acted on.</summary>
    [Fact]
    public void ARenderFromANonOwnerThread_WarnsUnderStrictErrors_AndNamesBothThreads()
    {
        using var renderer = BuildRenderer(strict: true);
        int ownerThreadId = Environment.CurrentManagedThreadId;
        int otherThreadId = 0;

        var lines = Capture(() =>
        {
            int rootId = renderer.Mount<Probe>();          // this thread becomes the owner
            OnAnotherThread(() =>
            {
                otherThreadId = Environment.CurrentManagedThreadId;
                renderer.TriggerRootRenderForTests(rootId);
            });
        });

        Assert.NotEqual(ownerThreadId, otherThreadId);

        (BnLogLevel Level, string Message) warning = Assert.Single(Reports(lines, ownerThreadId));
        Assert.Equal(BnLogLevel.Warn, warning.Level);
        Assert.Contains(otherThreadId.ToString(), warning.Message, StringComparison.Ordinal);
        Assert.Contains(ownerThreadId.ToString(), warning.Message, StringComparison.Ordinal);
    }

    /// <summary>The ordinary single-threaded path must be SILENT. A guard that fires on every
    /// batch is noise, and noise is not read — which would make this worse than no guard,
    /// because it would look like coverage. It would also allocate a message per frame, which
    /// the renderer's allocation budget pins against.</summary>
    [Fact]
    public void TheOrdinarySingleThreadedPath_IsSilent()
    {
        using var renderer = BuildRenderer(strict: true);
        int ownerThreadId = Environment.CurrentManagedThreadId;

        var lines = Capture(() =>
        {
            int rootId = renderer.Mount<Probe>();
            renderer.TriggerRootRenderForTests(rootId);
            renderer.TriggerRootRenderForTests(rootId);
        });

        Assert.Empty(Reports(lines, ownerThreadId));
    }

    /// <summary>Without StrictErrors the same condition still reports, at Debug level — it ships
    /// either way, so a consumer running ordinary trace logging gets it without having to
    /// discover StrictErrors first.</summary>
    [Fact]
    public void WithoutStrictErrors_TheSameConditionReportsAtDebugLevel()
    {
        using var renderer = BuildRenderer(strict: false);
        int ownerThreadId = Environment.CurrentManagedThreadId;

        var lines = Capture(() =>
        {
            int rootId = renderer.Mount<Probe>();
            OnAnotherThread(() => renderer.TriggerRootRenderForTests(rootId));
        });

        Assert.Equal(BnLogLevel.Debug, Assert.Single(Reports(lines, ownerThreadId)).Level);
    }
}
