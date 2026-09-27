using System.Diagnostics;
using System.Text.RegularExpressions;
using BlazorNative.Core;
using BlazorNative.Renderer;
using BlazorNative.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Xunit;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 16.3 (#9) — the slow-handler warning.
//
// Since 16.1 and 16.2 an async handler frees the dispatch lane when it yields,
// and the main thread never waits on .NET. What can still starve the lane is a
// handler whose SYNCHRONOUS part is slow: the render thread runs it while the
// shell's dispatch lane waits, and every later event queues behind it. The
// phase record measured the cost as one-for-one: an event posted behind a
// handler whose synchronous part takes N ms waits about N ms.
//
// The renderer times that part — from opening the dispatch's DispatchScope to
// closing it — and the host-event arms, which run outside any scope, are timed
// in Exports around their render-thread work. Over
// NativeRenderer.SlowHandlerBudget it logs ONE BnLog.Warn per handler id, or per
// event name for a host-event arm, per session. BnLog.DefaultLevel is Warn in
// every build, so that once-per-key rule is what keeps Release quiet.
//
// Every pin runs in PRODUCTION mode (StrictErrors = false) and captures BnLog at
// BnLog.DefaultLevel, so it sees exactly what a Release app logs. "Slow" is a
// fake clock the handler advances, set on the renderer under test through
// NativeRenderer.TimestampForTests: no pin waits for the budget, and every slow
// case sits at five times the budget, every quick one at a tenth of it, so none
// asserts near the boundary. One pin leaves the clock alone and really sleeps,
// at five times the budget, to prove the Stopwatch path.
//
// DOES NOT COVER:
//   - a host event with NO session: its arm runs on the calling thread, there is
//     no render thread to starve and no session to key the warning to, so it is
//     not timed. Fails safe: no warning, nothing blocked on the render thread;
//   - the time a post waits in the render-thread queue BEFORE its synchronous
//     part starts. That wait is the symptom, charged to the handler ahead of it,
//     never to the one waiting;
//   - the async remainder of a handler after its first await: it holds nothing;
//   - a handler whose delegate changes on every render. Blazor gives it a new
//     handler id each render, so it can warn once per id rather than once in all;
//   - the navigate and safeAreaChanged arms by name. They share the timed helper
//     with back and the lifecycle multicast, which are pinned;
//   - the shells: this is the .NET side only.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class SlowHandlerWarningTests
{
    /// <summary>A "slow" synchronous part: five times the budget.</summary>
    private static readonly int SlowMs = 5 * NativeRenderer.SlowHandlerBudget;

    /// <summary>A "quick" synchronous part: a tenth of the budget.</summary>
    private static readonly int QuickMs = NativeRenderer.SlowHandlerBudget / 10;

    private const string PayloadMarker = "PAYLOAD-MARKER-16-3-c4e1";

    private static string Click => /*lang=json*/ """{"name":"click"}""";

    private static string Change(string payload) => $$"""{"name":"change","payload":"{{payload}}"}""";

    // ── Session and capture ─────────────────────────────────────────────────

    private sealed class Session : IDisposable
    {
        private readonly Func<NativeRenderer, int> _demo;
        private readonly Action<BnLogLevel, string, string>? _sink = BnLog.Sink;
        private readonly BnLogLevel _level = BnLog.Level;
        private readonly List<RenderFrame> _frames = new();
        private readonly List<(BnLogLevel Level, string Line)> _log = new();

        public NativeRenderer Renderer { get; private set; } = null!;

        public Session(bool fakeClock = true)
        {
            FakeShellHost.Reset();
            NativeShellBridge.Register(FakeShellHost.BuildCallbacks());
            HostSession.ResetForTests();
            SlowProbe.ResetStatics();
            FakeClock.Reset();
            _demo = HostSession.ReplaceRegistryEntryForTests("BnDemo", r => r.Mount<SlowProbe>());
            Start(fakeClock);
            // What a Release app logs: the default level, every line.
            BnLog.Level = BnLog.DefaultLevel;
            BnLog.Sink = (level, category, message) => { lock (_log) _log.Add((level, $"{category}: {message}")); };
        }

        /// <summary>Builds a session and mounts the probe, in production mode.</summary>
        public void Start(bool fakeClock = true)
        {
            Renderer = HostSession.EnsureSession();
            Renderer.StrictErrors = false;
            if (fakeClock)
                Renderer.TimestampForTests = FakeClock.Now;
            lock (_frames) _frames.Clear();
            Renderer.Frames += (f, _) => { lock (_frames) _frames.Add(f); return ValueTask.CompletedTask; };
            Assert.Equal(0, HostSession.TryMount("BnDemo"));
        }

        public string[] SlowLines()
        {
            lock (_log)
                return _log.Where(e => e.Line.Contains(NativeRenderer.SlowHandlerLogLabel)).Select(e => e.Line).ToArray();
        }

        public (BnLogLevel Level, string Line)[] Log() { lock (_log) return _log.ToArray(); }

        /// <summary>The handler id of the element carrying <paramref name="eventName"/>
        /// whose text child is <paramref name="label"/>, from the latest frame that
        /// created it; an input has no text child, so it is found by its event alone.</summary>
        public int Handler(string label, string eventName = "click")
        {
            List<RenderFrame> all;
            lock (_frames) all = _frames.ToList();
            for (int i = all.Count - 1; i >= 0; i--)
            {
                RenderFrame f = all[i];
                if (eventName == "change")
                {
                    AttachEventPatch? change = f.Patches.OfType<AttachEventPatch>().FirstOrDefault(p => p.EventName == "change");
                    if (change is not null)
                        return change.HandlerId;
                    continue;
                }
                ReplaceTextPatch? text = f.Patches.OfType<ReplaceTextPatch>().FirstOrDefault(p => p.Text == label);
                if (text is null)
                    continue;
                CreateNodePatch? create = f.Patches.OfType<CreateNodePatch>().FirstOrDefault(p => p.NodeId == text.NodeId);
                if (create?.ParentId is not int button)
                    continue;
                AttachEventPatch? attach = f.Patches.OfType<AttachEventPatch>()
                    .FirstOrDefault(p => p.NodeId == button && p.EventName == eventName);
                if (attach is not null)
                    return attach.HandlerId;
            }
            throw new Xunit.Sdk.XunitException(
                $"no frame created a '{eventName}' handler for '{label}'. SlowProbe moved or was "
                + "relabelled; re-point this pin deliberately rather than deleting it.");
        }

        public void Dispose()
        {
            BnLog.Sink = _sink;
            BnLog.Level = _level;
            Exports.HostBackWorkForTests = null;
            HostSession.ReplaceRegistryEntryForTests("BnDemo", _demo);
            HostSession.ResetForTests();
            NativeShellBridge.ResetForTests();
            SlowProbe.ResetStatics();
        }
    }

    /// <summary>A clock only the probe's handlers move, in Stopwatch ticks.</summary>
    private static class FakeClock
    {
        private static long s_ticks;
        private static int s_reads;

        public static long Now() { Interlocked.Increment(ref s_reads); return Interlocked.Read(ref s_ticks); }

        public static void Advance(int ms) => Interlocked.Add(ref s_ticks, ms * Stopwatch.Frequency / 1000);

        public static int Reads => Volatile.Read(ref s_reads);

        public static void Reset() { Interlocked.Exchange(ref s_ticks, 1_000_000); Interlocked.Exchange(ref s_reads, 0); }
    }

    private static int Dispatch(int handlerId, string args) => Exports.DispatchEventCore((ulong)handlerId, args);

    // ── Rule 3: the capture sees a Warn at the production level ──────────────

    [Fact]
    public void TheLogCapture_SeesAWarn_AtTheDefaultLevel_PositiveControl()
    {
        using var s = new Session();
        Assert.Equal(BnLogLevel.Warn, BnLog.DefaultLevel);

        BnLog.Warn("probe", $"{NativeRenderer.SlowHandlerLogLabel}: capture control");
        BnLog.Debug("probe", $"{NativeRenderer.SlowHandlerLogLabel}: below the default level");

        var line = Assert.Single(s.SlowLines());
        Assert.Contains("capture control", line);
        Assert.Contains(s.Log(), e => e.Level == BnLogLevel.Warn && e.Line.Contains("capture control"));
    }

    // ── The warning ─────────────────────────────────────────────────────────

    [Fact]
    public void AHandlerOverBudget_LogsExactlyOneWarn_NamingItsId_ItsEvent_AndTheMilliseconds()
    {
        using var s = new Session();
        int slowA = s.Handler("slow-a");

        Assert.Equal(0, Dispatch(slowA, Click));

        Assert.Equal(1, SlowProbe.RunsOf("slow-a")); // anchor: the handler ran
        var line = Assert.Single(s.SlowLines());
        Assert.Contains(s.Log(), e => e.Level == BnLogLevel.Warn && e.Line == line);
        Assert.Contains($"handler {slowA}", line);
        Assert.Contains("'click'", line);
        Assert.Contains($"{SlowMs} ms", line);
        Assert.Contains($"{NativeRenderer.SlowHandlerBudget} ms budget", line);
    }

    [Fact]
    public void AHandlerUnderBudget_LogsNoWarn()
    {
        using var s = new Session();
        int quick = s.Handler("quick");
        int readsBefore = FakeClock.Reads;

        Assert.Equal(0, Dispatch(quick, Click));

        // Anchors: the handler ran, and the dispatch read the renderer's clock, so
        // the timer was on the path and simply found nothing to report.
        Assert.Equal(1, SlowProbe.RunsOf("quick"));
        Assert.True(FakeClock.Reads - readsBefore >= 2,
            $"the dispatch read the fake clock {FakeClock.Reads - readsBefore} times; a timed "
            + "synchronous part reads it at least twice. The timing moved off DispatchSyncPart, "
            + "or TimestampForTests is no longer its clock, so this pin checked nothing.");
        Assert.Empty(s.SlowLines());
    }

    [Fact]
    public void TheSameHandlerSlowTwice_WarnsOnce_AndADifferentSlowHandler_WarnsItsOwn()
    {
        using var s = new Session();
        int slowA = s.Handler("slow-a");
        Assert.Equal(0, Dispatch(slowA, Click));
        Assert.True(s.Handler("slow-a") == slowA,
            "slow-a's handler id changed across its own re-render, so the second dispatch below "
            + "would not be the same handler. Re-point this pin deliberately.");
        Assert.Equal(0, Dispatch(slowA, Click));

        int slowB = s.Handler("slow-b");
        Assert.NotEqual(slowA, slowB);
        Assert.Equal(0, Dispatch(slowB, Click));

        Assert.Equal(2, SlowProbe.RunsOf("slow-a")); // anchor: both slow runs happened
        Assert.Equal(1, SlowProbe.RunsOf("slow-b"));
        string[] lines = s.SlowLines();
        Assert.True(lines.Length == 2,
            $"expected one warning for slow-a and one for slow-b, got {lines.Length}:\n{string.Join("\n", lines)}");
        Assert.Single(lines, l => l.Contains($"handler {slowA} "));
        Assert.Single(lines, l => l.Contains($"handler {slowB} "));
    }

    [Fact]
    public void TheWarn_NeverCarriesThePayload()
    {
        using var s = new Session();
        int change = s.Handler("", "change");

        Assert.Equal(0, Dispatch(change, Change(PayloadMarker)));

        // Anchor: the payload reached the handler, so it was there to leak.
        Assert.Equal(PayloadMarker, SlowProbe.LastPayload);
        var line = Assert.Single(s.SlowLines());
        Assert.Contains("'change'", line);
        Assert.DoesNotContain(PayloadMarker, line);
        Assert.DoesNotContain(s.Log(), e => e.Line.Contains(PayloadMarker));
    }

    [Fact]
    public void ASlowBackArm_IsWarned_KeyedByEventName_Once()
    {
        using var s = new Session();
        int backs = 0;
        Exports.HostBackWorkForTests = () =>
        {
            backs++;
            FakeClock.Advance(SlowMs);
            return Task.FromResult(true);
        };

        Assert.Equal(0, Exports.DispatchHostEventCore(BnHostEvents.Back, null));
        Assert.Equal(0, Exports.DispatchHostEventCore(BnHostEvents.Back, null));

        Assert.Equal(2, backs); // anchor: the arm's work ran, twice
        var line = Assert.Single(s.SlowLines());
        Assert.Contains($"host event '{BnHostEvents.Back}'", line);
        Assert.Contains($"{SlowMs} ms", line);
    }

    [Fact]
    public void ASlowLifecycleArm_IsWarned_KeyedByEventName_AndEachNameWarnsItsOwn()
    {
        using var s = new Session();
        SlowProbe.SlowLifecycle = true;

        Assert.Equal(0, Exports.DispatchHostEventCore("onPause", null));
        Assert.Equal(0, Exports.DispatchHostEventCore("onPause", null));
        Assert.Equal(0, Exports.DispatchHostEventCore("onResume", null));

        Assert.Equal(3, SlowProbe.RunsOf("lifecycle")); // anchor: the subscriber ran
        string[] lines = s.SlowLines();
        Assert.True(lines.Length == 2,
            $"expected one warning for onPause and one for onResume, got {lines.Length}:\n{string.Join("\n", lines)}");
        Assert.Single(lines, l => l.Contains("host event 'onPause'"));
        Assert.Single(lines, l => l.Contains("host event 'onResume'"));
    }

    [Fact]
    public void TheWarnedSet_ResetsWithTheSession()
    {
        using var s = new Session();
        Assert.Equal(0, Dispatch(s.Handler("slow-a"), Click));
        Assert.Single(s.SlowLines());

        HostSession.ResetForTests();
        s.Start();
        Assert.Equal(0, Dispatch(s.Handler("slow-a"), Click));

        Assert.Equal(2, SlowProbe.RunsOf("slow-a"));
        Assert.Equal(2, s.SlowLines().Length);
    }

    [Fact]
    public void TheRealTimer_WarnsForASynchronousPartFiveTimesTheBudget()
    {
        using var s = new Session(fakeClock: false);
        Assert.Null(s.Renderer.TimestampForTests); // the real Stopwatch path
        SlowProbe.RealSleep = true;

        Assert.Equal(0, Dispatch(s.Handler("slow-a"), Click));

        Assert.Equal(1, SlowProbe.RunsOf("slow-a"));
        var line = Assert.Single(s.SlowLines());
        Match ms = Regex.Match(line, @"for (\d+) ms");
        Assert.True(ms.Success, $"the warning no longer states its milliseconds as 'for N ms': {line}");
        Assert.True(int.Parse(ms.Groups[1].Value) >= SlowMs,
            $"a {SlowMs} ms sleep was reported as {ms.Groups[1].Value} ms: {line}");
    }

    /// <summary>Three buttons, an input and a lifecycle subscriber. The slow ones
    /// advance <see cref="FakeClock"/> by <see cref="SlowMs"/>, or really sleep that
    /// long when <see cref="RealSleep"/> is set. Static slots are safe under the
    /// "host-session" collection.</summary>
    private sealed class SlowProbe : ComponentBase, IDisposable
    {
        private static readonly Dictionary<string, int> s_runs = new();
        public static volatile bool RealSleep;
        public static volatile bool SlowLifecycle;
        public static volatile string? LastPayload;

        public static int RunsOf(string tag) { lock (s_runs) return s_runs.GetValueOrDefault(tag); }

        public static void ResetStatics()
        {
            lock (s_runs) s_runs.Clear();
            RealSleep = false;
            SlowLifecycle = false;
            LastPayload = null;
        }

        [Inject] public IMobileBridge Bridge { get; set; } = default!;

        private static void Run(string tag, int ms)
        {
            lock (s_runs) s_runs[tag] = s_runs.GetValueOrDefault(tag) + 1;
            if (RealSleep)
                Thread.Sleep(ms);
            else
                FakeClock.Advance(ms);
        }

        protected override void OnInitialized() => Bridge.NativeEvents += OnNativeEvent;

        private void OnNativeEvent(NativeEvent _) { if (SlowLifecycle) Run("lifecycle", SlowMs); }

        private void SlowA() => Run("slow-a", SlowMs);
        private void SlowB() => Run("slow-b", SlowMs);
        private void Quick() => Run("quick", QuickMs);
        private void OnChange(ChangeEventArgs e) { LastPayload = e.Value as string; Run("change", SlowMs); }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "button");
            b.AddAttribute(1, "onclick", EventCallback.Factory.Create(this, SlowA));
            b.AddContent(2, "slow-a");
            b.CloseElement();
            b.OpenElement(3, "button");
            b.AddAttribute(4, "onclick", EventCallback.Factory.Create(this, SlowB));
            b.AddContent(5, "slow-b");
            b.CloseElement();
            b.OpenElement(6, "button");
            b.AddAttribute(7, "onclick", EventCallback.Factory.Create(this, Quick));
            b.AddContent(8, "quick");
            b.CloseElement();
            b.OpenElement(9, "input");
            b.AddAttribute(10, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, OnChange));
            b.CloseElement();
        }

        public void Dispose() => Bridge.NativeEvents -= OnNativeEvent;
    }
}
