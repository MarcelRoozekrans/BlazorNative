namespace BlazorNative.Core;

// ─────────────────────────────────────────────────────────────────────────────
// BnLog — Phase 11.4 Gate A: the ONE level-gated logging seam (M11 DoD #6,
// issue #155).
//
// WHAT IT REPLACES: 31 bare `Console.Error.WriteLine` sites across Runtime,
// Renderer and Core, plus `RendererServices`' nine-character throttle
// (`logLevel >= LogLevel.Warning`) — the only level concept the framework had.
// After Gate A there is exactly one threshold, one sink, and one line format.
//
// WHY IT LIVES IN CORE: it must be reachable from Core, Renderer AND Runtime.
// Those are three assemblies, so `internal` is not expressible without an
// InternalsVisibleTo web — it is PUBLIC, and that is a deliberate second
// benefit: raising the level is a thing a CONSUMER needs to do (see `Level`).
//
// THE DEFAULT IS Warn, AND IT IS NOT `#if DEBUG`. A build-configuration switch
// cannot be opened by a consumer shipping a Release build who needs one
// verbose session, and it makes the two configurations' code paths differ —
// which is exactly how a logging bug survives to production. A runtime level
// with a quiet default gives the same production quietness AND a way in.
//
// ALLOCATION POSTURE: this sits on error paths inside a NativeAOT runtime. The
// gate is `volatile int` compare — a suppressed level costs one branch and no
// allocation. The message is still built by the CALLER's interpolation, so a
// site below the default threshold must guard itself with `IsEnabled` rather
// than pay for a string nobody reads.
//
// THE LINE FORMAT IS A CROSS-LANGUAGE CONTRACT (design §5.5). The default sink
// tags the level INTO the text — `[BN|E|category] message` — because Gate B/C's
// stderr pump sees bytes, not levels, and must map the line back onto
// `android.util.Log` / `os.Logger`. `FormatLine` is a pure function so the pin
// asserts behaviour rather than a claim.
//
// See docs/plans/2026-07-21-phase-11.4-design.md §3 (the seam), §5.4 (the level
// rides the init input at zero byte cost), §5.5 (the format) and §7 (redaction).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The five framework log levels, plus the reserved <see cref="Unset"/>
/// ordinal 0.</summary>
/// <remarks>The ordinals are a WIRE CONTRACT: the shell passes one of them in
/// <c>BlazorNativeInitOptions.LogLevel</c> (offset 28), exactly as it passes a
/// <see cref="PlatformKind"/> ordinal at offset 24. Ordinal 0 means "the shell
/// said nothing" and resolves to <see cref="BnLog.DefaultLevel"/> — which is what
/// keeps a shell that predates the field working unchanged, since it leaves the
/// field zero. Never renumber.</remarks>
public enum BnLogLevel
{
    /// <summary>Reserved: "unset". Never a threshold and never a message level —
    /// it resolves to <see cref="BnLog.DefaultLevel"/>.</summary>
    Unset = 0,

    /// <summary>A fault. Ships in Release.</summary>
    Error = 1,

    /// <summary>Something the author asked for was dropped, or a host contract was
    /// bent. Ships in Release — see the design's §4.3 finding.</summary>
    Warn = 2,

    /// <summary>Success narration (boot lines). Suppressed in Release.</summary>
    Info = 3,

    /// <summary>Developer detail, including full exception stacks. Suppressed in
    /// Release.</summary>
    Debug = 4,

    /// <summary>Per-frame / per-patch tracing. Suppressed in Release.</summary>
    Verbose = 5,
}

/// <summary>The framework's one logging seam: a level threshold, a sink, and five
/// level methods.</summary>
public static class BnLog
{
    /// <summary>The threshold applied when nothing sets one — deliberately a
    /// RUNTIME default and deliberately not <c>#if DEBUG</c> (see the file
    /// header). Errors and warnings ship in Release; everything else does not.</summary>
    public const BnLogLevel DefaultLevel = BnLogLevel.Warn;

    /// <summary>The line prefix marker the Gate B/C stderr pumps parse. One
    /// constant per language; the drift pin holds them equal.</summary>
    public const string LinePrefix = "[BN|";

    // volatile: read on EVERY call from whatever thread is faulting, written once
    // at init (and possibly once more from ConfigureServices). An int compare is
    // the whole gate.
    private static volatile int s_level = (int)DefaultLevel;

    /// <summary>The current threshold: a message at this level or MORE SEVERE
    /// (numerically lower) is emitted.</summary>
    /// <remarks>THREE PLACES SET THIS, and the last write wins:
    /// <list type="number">
    /// <item>the shell's <c>BlazorNativeInitOptions.LogLevel</c> at boot — the one
    /// that matters, because it is set before the first managed line;</item>
    /// <item>this setter, from an app's <c>BlazorNativeApp.ConfigureServices</c>
    /// callback, for a consumer who wants verbosity for one session without
    /// touching the shell;</item>
    /// <item>a test, which must restore it in a <c>finally</c>.</item>
    /// </list>
    /// Assigning <see cref="BnLogLevel.Unset"/> or an out-of-range value resolves
    /// to <see cref="DefaultLevel"/> — the same safe non-lying rule
    /// <c>Exports.ToPlatformKind</c> applies to the neighbouring ordinal.</remarks>
    public static BnLogLevel Level
    {
        get => (BnLogLevel)s_level;
        set => s_level = (int)Normalize((int)value);
    }

    /// <summary>The one sink every framework line goes through. <see langword="null"/>
    /// (the default) means the built-in level-tagged <c>Console.Error</c> writer of
    /// <see cref="FormatLine"/>.</summary>
    /// <remarks>Deliberately the NARROWEST extension point that works — level,
    /// category, message. A consumer wanting structured sinks (Serilog,
    /// OpenTelemetry, JSON-per-line) builds on this; the framework does not grow an
    /// <c>ILoggerProvider</c> ecosystem to host them (design §12).
    /// A sink that THROWS is swallowed: a logger that faults its caller is worse
    /// than a logger that is quiet.</remarks>
    public static Action<BnLogLevel, string, string>? Sink { get; set; }

    /// <summary>Maps a raw ordinal — the value a shell put in the init-input
    /// struct — onto the threshold.</summary>
    /// <remarks>Ordinal 0 (unset, i.e. a shell that predates the field and left the
    /// tail padding zero) and any out-of-range value resolve to
    /// <see cref="DefaultLevel"/>. This is the exact shape of
    /// <c>Exports.ToPlatformKind</c>, and for the exact same reason: the field is
    /// filled by a hand-written mirror in two other languages.</remarks>
    public static void SetLevelFromOrdinal(int ordinal) => s_level = (int)Normalize(ordinal);

    /// <summary>Would a message at <paramref name="level"/> be emitted?</summary>
    /// <remarks>Public because it is how a call site AVOIDS BUILDING a message it
    /// will not emit. Interpolation happens at the call site, before the call, so a
    /// site below the default threshold — anything on the frame/patch path — must
    /// guard itself: <c>if (BnLog.IsEnabled(BnLogLevel.Debug)) BnLog.Debug(...)</c>.</remarks>
    public static bool IsEnabled(BnLogLevel level)
        => level != BnLogLevel.Unset && (int)level <= s_level;

    /// <summary>A fault.</summary>
    public static void Error(string category, string message) => Write(BnLogLevel.Error, category, message);

    /// <summary>A fault, with the exception rendered per the current verbosity
    /// (see <see cref="FormatException"/>).</summary>
    public static void Error(string category, string message, Exception exception)
        => Write(BnLogLevel.Error, category, Append(message, exception));

    /// <summary>A dropped wire, or a bent host contract.</summary>
    public static void Warn(string category, string message) => Write(BnLogLevel.Warn, category, message);

    /// <inheritdoc cref="Warn(string, string)"/>
    public static void Warn(string category, string message, Exception exception)
        => Write(BnLogLevel.Warn, category, Append(message, exception));

    /// <summary>Success narration. Suppressed in Release.</summary>
    public static void Info(string category, string message) => Write(BnLogLevel.Info, category, message);

    /// <summary>Developer detail. Suppressed in Release.</summary>
    public static void Debug(string category, string message) => Write(BnLogLevel.Debug, category, message);

    /// <summary>Per-frame tracing. Suppressed in Release; guard with
    /// <see cref="IsEnabled"/> before building the message.</summary>
    public static void Verbose(string category, string message) => Write(BnLogLevel.Verbose, category, message);

    /// <summary>Emits at an arbitrary level — the entry point
    /// <c>NativeRendererLoggerFactory</c> uses to funnel Blazor's own
    /// <c>ILogger</c> calls through this one threshold.</summary>
    public static void Write(BnLogLevel level, string category, string message)
    {
        if (!IsEnabled(level)) return;

        Action<BnLogLevel, string, string>? sink = Sink;
        try
        {
            if (sink is null) Console.Error.WriteLine(FormatLine(level, category, message));
            else sink(level, category, message);
        }
        catch
        {
            // A logger that throws is worse than a logger that is quiet — and this
            // sits inside `catch` blocks whose whole job is that nothing escapes
            // across the C-ABI (BN0020's boundary shape).
        }
    }

    /// <summary>THE LINE FORMAT (design §5.5) — <c>[BN|E|category] message</c>.</summary>
    /// <remarks>A pure function, and it has to be: Gate B/C's stderr pump sees
    /// formatted BYTES on fd 2, not levels, so it recovers the level by parsing this
    /// prefix back. A one-character drift silently downgrades every framework line to
    /// the pump's unprefixed fallback and NOTHING LOOKS BROKEN — which is why the
    /// round-trip is pinned in both directions rather than asserted in prose.</remarks>
    public static string FormatLine(BnLogLevel level, string category, string message)
        => $"{LinePrefix}{Tag(level)}|{category}] {message}";

    /// <summary>The single-character level tag inside <see cref="FormatLine"/>'s
    /// prefix. <see cref="BnLogLevel.Unset"/> can never reach a line (the gate
    /// rejects it) and maps to <c>W</c> defensively.</summary>
    public static char Tag(BnLogLevel level) => level switch
    {
        BnLogLevel.Error => 'E',
        BnLogLevel.Warn => 'W',
        BnLogLevel.Info => 'I',
        BnLogLevel.Debug => 'D',
        BnLogLevel.Verbose => 'V',
        _ => 'W',
    };

    /// <summary>Renders an exception at the given verbosity — design §7's
    /// information-disclosure rule, which LEVEL GATING ALONE DOES NOT ACHIEVE.</summary>
    /// <remarks>#155 ends with "no internal exception detail / paths leaked at default
    /// Release verbosity", and an Error ships in Release BY DESIGN — so gating changes
    /// which messages appear, not what is inside the ones that do. At the Release
    /// default this emits the exception TYPE, its MESSAGE and the TOP managed frame:
    /// enough to identify the fault and the component, not a map of the assembly. The
    /// full <c>ToString()</c> — inner chain and stack — requires
    /// <see cref="BnLogLevel.Debug"/>.
    /// <para>One documented exception stays verbatim and is NOT routed here:
    /// <c>blazornative_init</c>'s failure path deliberately returns
    /// <c>ex.ToString()</c> to the shell, because for the real NativeAOT trim failure
    /// modes (<c>TypeLoadException</c>, <c>MissingMethodException</c>) the message
    /// alone hides the offending type. That is a one-shot boot failure on a binary
    /// that is already not going to run.</para></remarks>
    public static string FormatException(Exception exception, BnLogLevel verbosity)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (verbosity >= BnLogLevel.Debug) return exception.ToString();

        string head = $"{exception.GetType().Name}: {exception.Message}";
        string? frame = TopFrame(exception);
        return frame is null ? head : $"{head} @ {frame}";
    }

    // ── internals ────────────────────────────────────────────────────────────

    private static string Append(string message, Exception exception)
        => exception is null ? message : $"{message} — {FormatException(exception, Level)}";

    /// <summary>The first line of the stack, trimmed — "the top managed frame".
    /// NativeAOT without PDBs may yield no stack at all, which is why the caller
    /// tolerates null rather than indexing blindly.</summary>
    private static string? TopFrame(Exception exception)
    {
        string? stack = exception.StackTrace;
        if (string.IsNullOrEmpty(stack)) return null;

        int end = stack.IndexOf('\n');
        string first = (end < 0 ? stack : stack[..end]).Trim();
        return first.Length == 0 ? null : first;
    }

    private static BnLogLevel Normalize(int ordinal)
        => ordinal >= (int)BnLogLevel.Error && ordinal <= (int)BnLogLevel.Verbose
            ? (BnLogLevel)ordinal
            : DefaultLevel;
}
