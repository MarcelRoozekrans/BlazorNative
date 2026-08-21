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

    /// <summary>Captures every BnLog line for the duration of the delegate.</summary>
    private static async Task<List<(BnLogLevel Level, string Message)>> Capture(Func<Task> body)
    {
        var lines = new List<(BnLogLevel, string)>();
        Action<BnLogLevel, string, string>? originalSink = BnLog.Sink;
        BnLogLevel originalLevel = BnLog.Level;
        try
        {
            BnLog.Level = BnLogLevel.Verbose;   // so Debug-level lines are not filtered out
            // FORWARDS to whatever was installed. A sink that only captures is a sink
            // that SWALLOWS, and anything relying on the default stderr writer while
            // this is installed would silently see nothing.
            BnLog.Sink = (level, category, message) =>
            {
                if (category == Category)
                    lock (lines) { lines.Add((level, message)); }
                originalSink?.Invoke(level, category, message);
            };
            await body();
        }
        finally
        {
            BnLog.Sink = originalSink;
            BnLog.Level = originalLevel;
        }

        lock (lines) { return [.. lines]; }
    }

    /// <summary>Drives a render from a thread that did not drive the first one. Under
    /// StrictErrors that is a WARNING, and it must name both threads — a report that
    /// says only "wrong thread" cannot be acted on.</summary>
    [Fact]
    public async Task ARenderFromANonOwnerThread_WarnsUnderStrictErrors_AndNamesBothThreads()
    {
        using var renderer = BuildRenderer(strict: true);
        int ownerThreadId = Environment.CurrentManagedThreadId;
        int otherThreadId = 0;

        var lines = await Capture(async () =>
        {
            // The first batch establishes the owner: this thread.
            int rootId = await renderer.MountAsync<Probe>(ParameterView.Empty);

            // A second batch, driven from somewhere else entirely.
            await Task.Run(() =>
            {
                otherThreadId = Environment.CurrentManagedThreadId;
                renderer.TriggerRootRenderForTests(rootId);
            });
        });

        Assert.NotEqual(ownerThreadId, otherThreadId);

        (BnLogLevel Level, string Message) warning = Assert.Single(
            lines.Where(l => l.Message.Contains("render batch", StringComparison.Ordinal)));

        Assert.Equal(BnLogLevel.Warn, warning.Level);
        Assert.Contains(otherThreadId.ToString(), warning.Message, StringComparison.Ordinal);
        Assert.Contains(ownerThreadId.ToString(), warning.Message, StringComparison.Ordinal);
    }

    /// <summary>The ordinary single-threaded path must be SILENT. A guard that fires on
    /// every batch is noise, and noise is not read — which would make this worse than
    /// having no guard, because it would look like coverage.</summary>
    [Fact]
    public async Task TheOrdinarySingleThreadedPath_IsSilent()
    {
        using var renderer = BuildRenderer(strict: true);

        var lines = await Capture(async () =>
        {
            int rootId = await renderer.MountAsync<Probe>(ParameterView.Empty);
            renderer.TriggerRootRenderForTests(rootId);
            renderer.TriggerRootRenderForTests(rootId);
        });

        Assert.DoesNotContain(lines, l => l.Message.Contains("render batch", StringComparison.Ordinal));
    }

    /// <summary>Without StrictErrors the same condition still reports, at Debug level.
    /// It ships either way — a consumer who turns Debug logging on gets it without
    /// having to discover StrictErrors first.</summary>
    [Fact]
    public async Task WithoutStrictErrors_TheSameConditionReportsAtDebugLevel()
    {
        using var renderer = BuildRenderer(strict: false);

        var lines = await Capture(async () =>
        {
            int rootId = await renderer.MountAsync<Probe>(ParameterView.Empty);
            await Task.Run(() => renderer.TriggerRootRenderForTests(rootId));
        });

        (BnLogLevel Level, string Message) report = Assert.Single(
            lines.Where(l => l.Message.Contains("render batch", StringComparison.Ordinal)));

        Assert.Equal(BnLogLevel.Debug, report.Level);
    }
}
