using BlazorNative.Core;
using BlazorNative.Renderer;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// TraceLoggingTests — #201: developer trace logging on the SUCCESS paths.
//
// Before #201 every BnLog call site sat on a failure path (a catch, a contract
// violation, a dropped patch), so raising the level revealed nothing for a
// working app — "I pressed a button" produced no output at any level. #201 adds
// level-gated trace on the paths a developer actually debugs: event dispatch and
// mount (Debug), and frame volume (Verbose).
//
// This file pins the GATING CONTRACT the issue names, driven through the REAL
// paths (HostSession.TryMount + Exports.DispatchEventCore + FrameEncoder.Encode):
//   · the default (Warn) stays SILENT on a working mount+click — #200/Q1 must not
//     regress into a chatty default;
//   · Debug reveals the per-interaction trace;
//   · Verbose (and not Debug) reveals the per-frame volume count.
// The mount/nav/bridge traces all use the identical `if (IsEnabled) Debug(...)`
// shape; dispatch+mount here exercise two of them and the frame test covers the
// Verbose level. The paired DEVICE observation the issue asks for (silent at Warn,
// trace at Verbose on a real handset) stays the owner's final step, like #200's.
//
// [Collection("host-session")]: BnLog.Level/Sink and the host session are all
// process-wide singletons — a test raising the level while another asserts on
// captured output would be a real race. Every test restores what it changed.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class TraceLoggingTests
{
    /// <summary>Runs <paramref name="body"/> with BnLog at <paramref name="level"/>,
    /// capturing every line the gate lets through (the sink only sees emitted lines).
    /// Restores the process-wide level and sink in a finally.</summary>
    private static List<(BnLogLevel Level, string Category, string Message)> CaptureAt(
        BnLogLevel level, Action body)
    {
        var captured = new List<(BnLogLevel, string, string)>();
        BnLogLevel savedLevel = BnLog.Level;
        Action<BnLogLevel, string, string>? savedSink = BnLog.Sink;
        try
        {
            BnLog.Level = level;
            BnLog.Sink = (lvl, cat, msg) => captured.Add((lvl, cat, msg));
            body();
        }
        finally
        {
            BnLog.Sink = savedSink;
            BnLog.Level = savedLevel;
        }
        return captured;
    }

    /// <summary>The "I pressed a button" loop through the real paths: mount BnDemo
    /// (HostSession.TryMount) and dispatch a click to its first click handler
    /// (Exports.DispatchEventCore).</summary>
    private static void MountAndClick()
    {
        HostSession.ResetForTests();
        NativeRenderer renderer = HostSession.EnsureSession();
        var frames = new List<RenderFrame>();
        renderer.Frames += (f, _) => { frames.Add(f); return ValueTask.CompletedTask; };

        Assert.Equal(0, HostSession.TryMount("BnDemo"));

        int handlerId = frames.SelectMany(f => f.Patches).OfType<AttachEventPatch>()
            .First(p => p.EventName == "click").HandlerId;
        Assert.Equal(0, Exports.DispatchEventCore((ulong)handlerId, /*lang=json*/ """{"name":"click"}"""));
    }

    [Fact]
    public void SuccessPaths_AreSilentAtTheDefaultWarnLevel()
    {
        var lines = CaptureAt(BnLogLevel.Warn, MountAndClick);

        // The whole point of #201: a working mount+click narrates NOTHING at the
        // Release default. If either appears here, the default turned chatty.
        Assert.DoesNotContain(lines, l => l.Message.Contains("mounted 'BnDemo'"));
        Assert.DoesNotContain(lines, l => l.Message.Contains("dispatch_event handler"));
        // Belt and braces: the gate never handed the sink anything less severe than
        // Warn, so no trace-level line leaked through at the default.
        Assert.All(lines, l => Assert.True(
            l.Level == BnLogLevel.Error || l.Level == BnLogLevel.Warn,
            $"unexpected {l.Level} line at the Warn default: {l.Category} — {l.Message}"));
    }

    [Fact]
    public void MountAndDispatch_TraceAtDebug()
    {
        var lines = CaptureAt(BnLogLevel.Debug, MountAndClick);

        // Mount trace — HostSession, Debug, names the resolved component.
        Assert.Contains(lines, l =>
            l.Level == BnLogLevel.Debug && l.Category == "HostSession"
            && l.Message.Contains("mounted 'BnDemo'") && l.Message.Contains("rc 0"));

        // Dispatch trace — the headline: handler id + event name + rc, at Debug.
        // Never the payload (a text field's value), so only the name is asserted.
        Assert.Contains(lines, l =>
            l.Level == BnLogLevel.Debug && l.Category == "Exports"
            && l.Message.Contains("dispatch_event handler")
            && l.Message.Contains("'click'") && l.Message.Contains("rc 0"));
    }

    [Fact]
    public unsafe void FrameVolume_TracesAtVerbose_NotAtDebug()
    {
        // The count line fires before the patch loop, so an empty frame is enough to
        // exercise it without building encodable patches.
        var frame = new RenderFrame(FrameId: 42, TimestampMs: 0L, Patches: Array.Empty<RenderPatch>());

        using var arena = FrameArena.Rent();

        // Debug is ABOVE Verbose in severity, so the per-frame volume line is gated OUT
        // (it belongs at Verbose — per-frame at Debug would drown the interaction trace).
        var atDebug = CaptureAt(BnLogLevel.Debug, () => { FrameEncoder.Encode(frame, arena); });
        Assert.DoesNotContain(atDebug, l => l.Message.Contains("frame 42"));

        // Verbose reveals exactly one line per frame, with the patch count.
        var atVerbose = CaptureAt(BnLogLevel.Verbose, () => { FrameEncoder.Encode(frame, arena); });
        Assert.Contains(atVerbose, l =>
            l.Level == BnLogLevel.Verbose && l.Category == "FrameEncoder"
            && l.Message.Contains("frame 42: 0 patches"));
    }
}
