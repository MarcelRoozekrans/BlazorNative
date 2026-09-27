using BlazorNative.Components;
using BlazorNative.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorNative.SampleApp;

// ─────────────────────────────────────────────────────────────────────────────
// SlowHandlerProbe — Phase 16.3 (#9): the page that proves the slow-handler
// warning names the APP's handler in the published NativeAOT build.
// SCAFFOLDING, like HostEventProbe/BackHoldProbe: a Named mount-registry entry,
// not a routed page, so it has no menu row and no deep link.
//
// Shape:
//   root div
//     ├─ BnButton "Slow one" → SlowOne, a synchronous 500 ms sleep
//     ├─ BnButton "Slow two" → SlowTwo, the same, a different method
//     ├─ BnButton "Report"   → restores the previous BnLog sink, and re-renders the echo
//     └─ BnText echo: "slow-warnings:<n>" then one "\n<line>" per warning
//
// WHY. The warning keys a dispatch by the method its delegate runs. BnButton
// forwards every app's OnClick from one line, so a key built from the tree that
// holds the attribute would merge SlowOne and SlowTwo into one warning naming
// BnButton. Whether Delegate.Method resolves under NativeAOT is a property of the
// published dll, so JVM SlowHandlerProbeTest drives this page through it and
// reads the echo.
//
// WHY THE ECHO READS A SINK. The warning goes to BnLog, which the JVM lane cannot
// read from the native process's stderr. The page installs a BnLog.Sink when it
// mounts, keeps every slow-handler line, and forwards every line to the sink it
// replaced (or to stderr in BnLog's own format). "Report" puts the previous sink
// back once the warnings it needs are captured: a session retired without
// disposing its components never runs Dispose, so the sink would otherwise stay
// installed for the rest of the process. Dispose restores it too, for a probe
// torn down before Report. The warning is logged after the dispatch's own
// re-render, so "Report" shows it.
// 500 ms is five times the renderer's 100 ms budget, never near the boundary.
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class SlowHandlerProbe : ComponentBase, IDisposable
{
    /// <summary>The echo's prefix, followed by the number of captured warnings.</summary>
    internal const string EchoPrefix = "slow-warnings:";

    /// <summary>The synchronous part of each slow handler: five times the budget.</summary>
    internal const int SlowMs = 500;

    private readonly List<string> _warnings = new();
    private Action<BnLogLevel, string, string>? _previousSink;

    protected override void OnInitialized()
    {
        _previousSink = BnLog.Sink;
        BnLog.Sink = Capture;
    }

    private void Capture(BnLogLevel level, string category, string message)
    {
        if (message.StartsWith("slow handler", StringComparison.Ordinal))
        {
            lock (_warnings)
                _warnings.Add(message);
        }
        if (_previousSink is { } previous)
            previous(level, category, message);
        else
            Console.Error.WriteLine(BnLog.FormatLine(level, category, message));
    }

    // BN0004 is right about these two lines, and they are the point of the page: each is
    // the blocking synchronous handler the slow-handler warning exists to report. They run
    // only when a test clicks them.
#pragma warning disable BN0004 // justification: a deliberate slow handler, the warning's subject
    private void SlowOne() => Thread.Sleep(SlowMs);

    private void SlowTwo() => Thread.Sleep(SlowMs);
#pragma warning restore BN0004

    private void Report() => RestoreSink();

    /// <summary>Puts the replaced sink back, unless something else has replaced ours
    /// since.</summary>
    private void RestoreSink()
    {
        if (BnLog.Sink == (Action<BnLogLevel, string, string>)Capture)
            BnLog.Sink = _previousSink;
    }

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "div");

        b.OpenComponent<BnButton>(10);
        b.AddComponentParameter(11, nameof(BnButton.Label), "Slow one");
        b.AddComponentParameter(12, nameof(BnButton.OnClick),
            EventCallback.Factory.Create<MouseEventArgs>(this, SlowOne));
        b.CloseComponent();

        b.OpenComponent<BnButton>(20);
        b.AddComponentParameter(21, nameof(BnButton.Label), "Slow two");
        b.AddComponentParameter(22, nameof(BnButton.OnClick),
            EventCallback.Factory.Create<MouseEventArgs>(this, SlowTwo));
        b.CloseComponent();

        b.OpenComponent<BnButton>(30);
        b.AddComponentParameter(31, nameof(BnButton.Label), "Report");
        b.AddComponentParameter(32, nameof(BnButton.OnClick),
            EventCallback.Factory.Create<MouseEventArgs>(this, Report));
        b.CloseComponent();

        string echo;
        lock (_warnings)
            echo = EchoPrefix + _warnings.Count + string.Concat(_warnings.Select(w => "\n" + w));
        b.OpenComponent<BnText>(40);
        b.AddComponentParameter(41, nameof(BnText.Text), echo);
        b.CloseComponent();

        b.CloseElement();
    }

    public void Dispose() => RestoreSink();
}
