using BlazorNative.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorNative.Renderer.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// RenderThreadWarningTests — Phase 13.2, flipped in Phase 16.1.
//
// WHAT THIS GUARDS.
//
// Phase 13.2 could only DETECT a render driven from the wrong thread: the inline
// dispatcher answered CheckAccess() with an unconditional `true`, because an honest
// answer on a dispatcher with nowhere to marshal to recursed
//
//     Renderer.Dispose() -> CheckAccess() false -> Dispatcher.InvokeAsync(Dispose)
//       -> run INLINE on the same thread -> Renderer.Dispose() -> ... stack overflow
//
// Phase 16.1 gave the renderer its own render thread, so CheckAccess() is honest and
// Blazor's own AssertAccess() rejects an off-thread StateHasChanged before any batch
// starts. The renderer's check is now a second, independent line: it compares the
// thread that reaches UpdateDisplayAsync against THIS renderer's RenderThreadId, not
// against CheckAccess(). So it still fires if CheckAccess() ever lies again — which is
// exactly 13.2's failure mode, and exactly how these tests drive it: they make the
// dispatcher claim every thread (RenderThreadDispatcher.ClaimEveryThreadForTests), the
// one condition under which a batch can reach the tree off its thread.
//
// Under StrictErrors the check THROWS, naming both threads; without it, it warns.
//
// DOES NOT COVER (pin standard Rule 5): Blazor's own AssertAccess rejection of an
// off-thread StateHasChanged — that is Blazor's pin, not ours, and it names no thread.
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

    /// <summary>A batch that reaches the tree off the render thread THROWS under StrictErrors,
    /// and the exception must name BOTH threads — a report that says only "wrong thread" cannot
    /// be acted on.</summary>
    [Fact]
    public void ARenderFromANonOwnerThread_ThrowsUnderStrictErrors_AndNamesBothThreads()
    {
        // Flipped in 16.1: was ARenderFromANonOwnerThread_WarnsUnderStrictErrors_AndNamesBothThreads, asserting a single Warn-level report and no throw.
        using var renderer = BuildRenderer(strict: true);
        var dispatcher = Assert.IsType<RenderThreadDispatcher>(renderer.Dispatcher);
        int ownerThreadId = renderer.RenderThreadId;
        int rootId = renderer.Mount<Probe>();

        // Positive control: the same off-thread call WITHOUT the lie is marshalled onto the
        // render thread and is fine, so the throw below comes from the bypass, not the call.
        OnAnotherThread(() => renderer.TriggerRootRenderForTests(rootId));

        int otherThreadId = 0;
        InvalidOperationException thrown;
        dispatcher.ClaimEveryThreadForTests = true;
        try
        {
            thrown = Assert.Throws<InvalidOperationException>(() => OnAnotherThread(() =>
            {
                otherThreadId = Environment.CurrentManagedThreadId;
                renderer.TriggerRootRenderForTests(rootId);
            }));
        }
        finally
        {
            dispatcher.ClaimEveryThreadForTests = false;
        }

        Assert.NotEqual(ownerThreadId, otherThreadId);
        Assert.Contains("render batch", thrown.Message, StringComparison.Ordinal);
        Assert.Contains($"driven from thread {otherThreadId}", thrown.Message, StringComparison.Ordinal);
        Assert.Contains($"owned by thread {ownerThreadId}", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>The ordinary single-threaded path must be SILENT. A guard that fires on every
    /// batch is noise, and noise is not read — which would make this worse than no guard,
    /// because it would look like coverage. It would also allocate a message per frame, which
    /// the renderer's allocation budget pins against.</summary>
    [Fact]
    public void TheOrdinarySingleThreadedPath_IsSilent()
    {
        using var renderer = BuildRenderer(strict: true);
        // 16.1: the owner is the renderer's render thread, no longer the thread that drove the
        // first batch — compared against the renderer under test, never the test thread.
        int ownerThreadId = renderer.RenderThreadId;

        var lines = Capture(() =>
        {
            int rootId = renderer.Mount<Probe>();
            renderer.TriggerRootRenderForTests(rootId);
            renderer.TriggerRootRenderForTests(rootId);
        });

        Assert.Empty(Reports(lines, ownerThreadId));
    }

    /// <summary>Without StrictErrors the same condition still reports, as a WARNING — it ships
    /// either way, so a consumer running ordinary logging gets it without having to discover
    /// StrictErrors first.</summary>
    [Fact]
    public void WithoutStrictErrors_TheSameConditionWarns()
    {
        // Flipped in 16.1: was WithoutStrictErrors_TheSameConditionReportsAtDebugLevel, asserting a single Debug-level report.
        using var renderer = BuildRenderer(strict: false);
        var dispatcher = Assert.IsType<RenderThreadDispatcher>(renderer.Dispatcher);
        int ownerThreadId = renderer.RenderThreadId;

        var lines = Capture(() =>
        {
            int rootId = renderer.Mount<Probe>();
            dispatcher.ClaimEveryThreadForTests = true;
            try
            {
                OnAnotherThread(() => renderer.TriggerRootRenderForTests(rootId));
            }
            finally
            {
                dispatcher.ClaimEveryThreadForTests = false;
            }
        });

        Assert.Equal(BnLogLevel.Warn, Assert.Single(Reports(lines, ownerThreadId)).Level);
    }
}
