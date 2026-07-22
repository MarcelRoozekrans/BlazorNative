using BlazorNative.Core;
using BlazorNative.Renderer;
using Microsoft.Extensions.Logging;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// BnLogTests — Phase 11.4 Gate A (M11 DoD #6, #155).
//
// The design's §8.1 pins 1–3: level gating, the init-input ordinal → level map,
// and the strengthened struct pin (the SizeOf/OffsetOf pair lives in
// NativeShellBridgeTests, next to the M10 assertion it strengthens). Plus §5.5's
// line format, which is a THREE-LANGUAGE stringly-typed contract and therefore
// the highest-risk thing in the phase (§11 R1): a one-character drift silently
// downgrades every framework line to the Gate B/C pump's unprefixed fallback and
// NOTHING LOOKS BROKEN.
//
// WHY [Collection("host-session")]: BnLog.Level and BnLog.Sink are PROCESS-WIDE
// singletons, exactly like the host-session state that collection exists to
// serialize. A test here that raised the level while DispatchEventTests was
// asserting on captured stderr would be a real race, not a theoretical one —
// and every test below restores what it changed in a finally.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class BnLogTests
{
    // ── 1. The default, and why it is a RUNTIME default ──────────────────────

    /// <summary>THE DEFAULT IS Warn — errors and warnings ship in Release,
    /// everything else does not.
    ///
    /// And it is a runtime `const`, NOT `#if DEBUG`. That is the design decision
    /// this assertion exists to hold: a build-configuration switch cannot be opened
    /// by a consumer shipping a Release build who needs one verbose session, and it
    /// makes the Debug and Release code paths differ — which is exactly how a
    /// logging bug survives to production. This test compiles and runs identically
    /// in both configurations BECAUSE there is no `#if`; if one is ever introduced,
    /// the Release lane and a local Debug run would disagree about this line.</summary>
    [Fact]
    public void DefaultLevel_IsWarn()
    {
        Assert.Equal(BnLogLevel.Warn, BnLog.DefaultLevel);
    }

    // ── 2. The gate ──────────────────────────────────────────────────────────

    /// <summary>Every level against every threshold — a message is emitted when it
    /// is at the threshold or MORE SEVERE (numerically lower).</summary>
    [Theory]
    // threshold Error: only errors
    [InlineData(BnLogLevel.Error, BnLogLevel.Error, true)]
    [InlineData(BnLogLevel.Error, BnLogLevel.Warn, false)]
    [InlineData(BnLogLevel.Error, BnLogLevel.Info, false)]
    [InlineData(BnLogLevel.Error, BnLogLevel.Debug, false)]
    [InlineData(BnLogLevel.Error, BnLogLevel.Verbose, false)]
    // threshold Warn: THE RELEASE DEFAULT — errors and warnings, nothing else
    [InlineData(BnLogLevel.Warn, BnLogLevel.Error, true)]
    [InlineData(BnLogLevel.Warn, BnLogLevel.Warn, true)]
    [InlineData(BnLogLevel.Warn, BnLogLevel.Info, false)]
    [InlineData(BnLogLevel.Warn, BnLogLevel.Debug, false)]
    [InlineData(BnLogLevel.Warn, BnLogLevel.Verbose, false)]
    // threshold Info
    [InlineData(BnLogLevel.Info, BnLogLevel.Info, true)]
    [InlineData(BnLogLevel.Info, BnLogLevel.Debug, false)]
    // threshold Debug
    [InlineData(BnLogLevel.Debug, BnLogLevel.Debug, true)]
    [InlineData(BnLogLevel.Debug, BnLogLevel.Verbose, false)]
    // threshold Verbose: everything
    [InlineData(BnLogLevel.Verbose, BnLogLevel.Verbose, true)]
    [InlineData(BnLogLevel.Verbose, BnLogLevel.Error, true)]
    public void IsEnabled_GatesByThreshold(BnLogLevel threshold, BnLogLevel level, bool expected)
    {
        WithLevel(threshold, () => Assert.Equal(expected, BnLog.IsEnabled(level)));
    }

    /// <summary>Ordinal 0 is a THRESHOLD marker, never a message level. Even at the
    /// most permissive threshold it is rejected — which is what lets
    /// `NativeRendererLoggerFactory` map `LogLevel.None` onto it and get the one
    /// MEL level that must never be emitted for free.</summary>
    [Theory]
    [InlineData(BnLogLevel.Error)]
    [InlineData(BnLogLevel.Warn)]
    [InlineData(BnLogLevel.Verbose)]
    public void IsEnabled_NeverAcceptsUnset(BnLogLevel threshold)
    {
        WithLevel(threshold, () => Assert.False(BnLog.IsEnabled(BnLogLevel.Unset)));
    }

    // ── 3. The init-input ordinal → level map ────────────────────────────────

    /// <summary>THE ORDINAL CONTRACT the shells encode against, and the exact twin
    /// of <c>ToPlatformKind_MapsTheInitOptionsOrdinal_ByEnumValue</c> — the
    /// neighbouring field in the same struct, mapped by the same rule.
    ///
    /// Ordinal 0 is the load-bearing row: a shell that predates this field leaves
    /// the init struct's tail padding zero, so 0 MUST resolve to the quiet default
    /// rather than to "Error" (which would silence every warning) or to a throw.
    /// Out-of-range and negative junk resolve there too — a safe non-lying default
    /// for a field filled by a hand-written mirror in two other languages.</summary>
    [Theory]
    [InlineData(0, BnLogLevel.Warn)]      // unset → the default. The back-compat row.
    [InlineData(1, BnLogLevel.Error)]
    [InlineData(2, BnLogLevel.Warn)]
    [InlineData(3, BnLogLevel.Info)]
    [InlineData(4, BnLogLevel.Debug)]
    [InlineData(5, BnLogLevel.Verbose)]
    [InlineData(6, BnLogLevel.Warn)]      // out of range → the default, not silence
    [InlineData(-1, BnLogLevel.Warn)]     // negative junk → the default
    [InlineData(int.MaxValue, BnLogLevel.Warn)]
    public void SetLevelFromOrdinal_MapsTheInitOptionsOrdinal_ByEnumValue(int ordinal, BnLogLevel expected)
    {
        BnLogLevel original = BnLog.Level;
        try
        {
            BnLog.SetLevelFromOrdinal(ordinal);
            Assert.Equal(expected, BnLog.Level);
        }
        finally { BnLog.Level = original; }
    }

    /// <summary>The managed setter applies the SAME normalisation — one rule, not
    /// two. Assigning <c>Unset</c> (or a cast-in junk value) resolves to the
    /// default rather than disabling the seam entirely.</summary>
    [Theory]
    [InlineData(BnLogLevel.Unset, BnLogLevel.Warn)]
    [InlineData((BnLogLevel)99, BnLogLevel.Warn)]
    [InlineData(BnLogLevel.Debug, BnLogLevel.Debug)]
    public void LevelSetter_NormalisesLikeTheOrdinalMap(BnLogLevel assigned, BnLogLevel expected)
    {
        BnLogLevel original = BnLog.Level;
        try
        {
            BnLog.Level = assigned;
            Assert.Equal(expected, BnLog.Level);
        }
        finally { BnLog.Level = original; }
    }

    // ── 4. The sink ──────────────────────────────────────────────────────────

    /// <summary>A SUPPRESSED LEVEL INVOKES NO SINK — the gate is applied BEFORE the
    /// sink, not inside it. This is what makes a suppressed call cost one integer
    /// compare instead of a delegate invocation on a NativeAOT error path.</summary>
    [Fact]
    public void SuppressedLevel_InvokesNoSink()
    {
        var seen = new List<(BnLogLevel, string, string)>();
        WithSink(seen, () => WithLevel(BnLogLevel.Warn, () =>
        {
            BnLog.Info("cat", "narration");
            BnLog.Debug("cat", "detail");
            BnLog.Verbose("cat", "trace");

            Assert.Empty(seen);
        }));
    }

    /// <summary>…and an enabled level reaches it with the level, category and
    /// message intact — the three-part shape §12 deliberately froze as the
    /// narrowest extension point that works.</summary>
    [Fact]
    public void EnabledLevel_ReachesTheSink_WithLevelCategoryAndMessage()
    {
        var seen = new List<(BnLogLevel, string, string)>();
        WithSink(seen, () => WithLevel(BnLogLevel.Verbose, () =>
        {
            BnLog.Error("Exports", "e");
            BnLog.Warn("NativeRenderer", "w");
            BnLog.Info("BnRuntime", "i");
            BnLog.Debug("mapper", "d");
            BnLog.Verbose("mapper", "v");

            Assert.Equal(
                new[]
                {
                    (BnLogLevel.Error, "Exports", "e"),
                    (BnLogLevel.Warn, "NativeRenderer", "w"),
                    (BnLogLevel.Info, "BnRuntime", "i"),
                    (BnLogLevel.Debug, "mapper", "d"),
                    (BnLogLevel.Verbose, "mapper", "v"),
                },
                seen);
        }));
    }

    /// <summary>A SINK THAT THROWS DOES NOT FAULT ITS CALLER. Every one of the 31
    /// migrated sites sits inside a `catch` whose entire job is that nothing escapes
    /// across the C-ABI (BN0020's boundary shape). A logger that throws would turn
    /// the seam into a new way to violate it.</summary>
    [Fact]
    public void AThrowingSink_IsSwallowed()
    {
        Action<BnLogLevel, string, string>? original = BnLog.Sink;
        try
        {
            BnLog.Sink = (_, _, _) => throw new InvalidOperationException("sink is broken");
            BnLog.Error("cat", "message");   // must not throw
        }
        finally { BnLog.Sink = original; }
    }

    /// <summary>With no sink installed, the default writer puts the FORMATTED line
    /// on `Console.Error` — the behaviour every existing stderr-capturing test in
    /// this suite depends on, and the byte stream Gate B/C's stdio pump will read.</summary>
    [Fact]
    public void TheDefaultSink_WritesTheFormattedLineToStderr()
    {
        Action<BnLogLevel, string, string>? originalSink = BnLog.Sink;
        TextWriter originalErr = Console.Error;
        var capture = new StringWriter();
        try
        {
            BnLog.Sink = null;
            Console.SetError(capture);
            WithLevel(BnLogLevel.Warn, () => BnLog.Error("HostSession", "mount 'X' failed"));
        }
        finally
        {
            Console.SetError(originalErr);
            BnLog.Sink = originalSink;
        }

        Assert.Contains("[BN|E|HostSession] mount 'X' failed", capture.ToString());
    }

    // ── 5. The line format — §5.5, and §11's R1 ──────────────────────────────

    /// <summary>THE FORMAT, STATED LITERALLY. Written by C# and parsed by Kotlin AND
    /// Swift in Gates B and C, so it is spelled out here rather than derived from
    /// the constant it is testing — a test that builds the expectation from
    /// `BnLog.LinePrefix` would pass through any drift of that constant, which is
    /// precisely the failure it must catch.</summary>
    [Theory]
    [InlineData(BnLogLevel.Error, "Exports", "shutdown failed", "[BN|E|Exports] shutdown failed")]
    [InlineData(BnLogLevel.Warn, "NativeRenderer", "stale handler 7", "[BN|W|NativeRenderer] stale handler 7")]
    [InlineData(BnLogLevel.Info, "BnRuntime", "native init ok", "[BN|I|BnRuntime] native init ok")]
    [InlineData(BnLogLevel.Debug, "mapper", "node 3 skipped", "[BN|D|mapper] node 3 skipped")]
    [InlineData(BnLogLevel.Verbose, "mapper", "patch 12", "[BN|V|mapper] patch 12")]
    public void FormatLine_IsTheDocumentedThreeLanguageContract(
        BnLogLevel level, string category, string message, string expected)
    {
        Assert.Equal(expected, BnLog.FormatLine(level, category, message));
    }

    /// <summary>The formatted line ROUND-TRIPS: a naive parser of the shape Gate B/C
    /// will write recovers exactly the triple that went in, including a message that
    /// itself contains brackets and pipes (a stack frame or a JSON payload will).
    /// The pin is the round trip, because the pump's real failure mode is not
    /// "crashes" — it is "silently classifies everything as unprefixed".</summary>
    [Theory]
    [InlineData(BnLogLevel.Error, "Exports", "host_event 'x|y' faulted [inner]")]
    [InlineData(BnLogLevel.Warn, "NativeShellBridge", "fetch_complete for unknown/completed id 4")]
    [InlineData(BnLogLevel.Verbose, "mapper", "")]
    public void FormatLine_RoundTrips(BnLogLevel level, string category, string message)
    {
        string line = BnLog.FormatLine(level, category, message);

        Assert.StartsWith(BnLog.LinePrefix, line, StringComparison.Ordinal);
        Assert.Equal(BnLog.Tag(level), line[BnLog.LinePrefix.Length]);

        int close = line.IndexOf(']');
        Assert.True(close > 0, "the prefix must terminate with ']'");
        Assert.Equal(category, line[(BnLog.LinePrefix.Length + 2)..close]);
        Assert.Equal(message, line[(close + 2)..]);
    }

    /// <summary>The five tags, and the defensive mapping for a level that can never
    /// reach a line.</summary>
    [Theory]
    [InlineData(BnLogLevel.Error, 'E')]
    [InlineData(BnLogLevel.Warn, 'W')]
    [InlineData(BnLogLevel.Info, 'I')]
    [InlineData(BnLogLevel.Debug, 'D')]
    [InlineData(BnLogLevel.Verbose, 'V')]
    [InlineData(BnLogLevel.Unset, 'W')]
    public void Tag_MapsEveryLevel(BnLogLevel level, char expected)
    {
        Assert.Equal(expected, BnLog.Tag(level));
    }

    // ── 6. §7 — redaction, which level gating alone does NOT deliver ─────────

    /// <summary>#155 ends with "no internal exception detail / paths leaked at
    /// default Release verbosity", and an Error SHIPS in Release by design — so
    /// gating changes which messages appear, not what is inside the ones that do.
    /// At the Release default the exception is reduced to type + message (+ the top
    /// frame): enough to identify the fault and the component, not a map of the
    /// assembly.</summary>
    [Theory]
    [InlineData(BnLogLevel.Error)]
    [InlineData(BnLogLevel.Warn)]
    [InlineData(BnLogLevel.Info)]
    public void FormatException_BelowDebug_EmitsTypeAndMessage_ButNotTheWholeToString(BnLogLevel verbosity)
    {
        Exception ex = Caught();
        string rendered = BnLog.FormatException(ex, verbosity);

        Assert.Contains(nameof(InvalidOperationException), rendered);
        Assert.Contains("outer-boom", rendered);
        Assert.DoesNotContain("inner-boom", rendered);          // the chain requires Debug
        Assert.NotEqual(ex.ToString(), rendered);
        Assert.True(rendered.Length < ex.ToString().Length,
            "the redacted form must be SHORTER than ToString() — if it is not, nothing was redacted.");
    }

    /// <summary>…and the full <c>ToString()</c> — inner chain and stack — is one
    /// level away, which is the whole point of a runtime threshold rather than a
    /// `#if`: the detail is reachable on the binary that is actually failing.</summary>
    [Fact]
    public void FormatException_AtDebug_EmitsTheFullToString()
    {
        Exception ex = Caught();
        Assert.Equal(ex.ToString(), BnLog.FormatException(ex, BnLogLevel.Debug));
        Assert.Equal(ex.ToString(), BnLog.FormatException(ex, BnLogLevel.Verbose));
    }

    /// <summary>The exception overloads route through the redaction, at the CURRENT
    /// verbosity — so the 31 migrated sites inherit §7 without each one repeating
    /// the rule.</summary>
    [Fact]
    public void TheExceptionOverload_AppliesTheCurrentVerbositysRedaction()
    {
        Exception ex = Caught();
        var seen = new List<(BnLogLevel, string, string)>();

        WithSink(seen, () =>
        {
            WithLevel(BnLogLevel.Warn, () => BnLog.Error("HostSession", "mount failed", ex));
            WithLevel(BnLogLevel.Debug, () => BnLog.Error("HostSession", "mount failed", ex));
        });

        Assert.Equal(2, seen.Count);
        Assert.DoesNotContain("inner-boom", seen[0].Item3);   // Release verbosity
        Assert.Contains("inner-boom", seen[1].Item3);         // Debug verbosity
    }

    // ── 7. The seam replaced RendererServices' hard-coded literal ────────────

    /// <summary>`RendererServices.cs:39` was the framework's ENTIRE level concept —
    /// nine characters, `logLevel &gt;= LogLevel.Warning`, not configurable and not
    /// build-gated. It now delegates, and this is the assertion that says so: raise
    /// `BnLog.Level` and Blazor's own `ILogger` starts emitting `Information`.
    ///
    /// If it still read the literal, the second block below would be false — which
    /// makes this a pin on the DELEGATION, not merely on the mapping.</summary>
    [Fact]
    public void TheRenderersILogger_HonoursBnLogsThreshold_NotAHardCodedLiteral()
    {
        ILogger logger = new NativeRendererLoggerFactory().CreateLogger("cat");

        WithLevel(BnLogLevel.Warn, () =>
        {
            Assert.True(logger.IsEnabled(LogLevel.Critical));
            Assert.True(logger.IsEnabled(LogLevel.Error));
            Assert.True(logger.IsEnabled(LogLevel.Warning));
            Assert.False(logger.IsEnabled(LogLevel.Information));
            Assert.False(logger.IsEnabled(LogLevel.Debug));
            Assert.False(logger.IsEnabled(LogLevel.Trace));
        });

        WithLevel(BnLogLevel.Verbose, () =>
        {
            Assert.True(logger.IsEnabled(LogLevel.Information));
            Assert.True(logger.IsEnabled(LogLevel.Debug));
            Assert.True(logger.IsEnabled(LogLevel.Trace));

            // LogLevel.None is the one MEL level that must NEVER be emitted, at any
            // threshold — it maps onto the reserved Unset ordinal, which the gate
            // rejects unconditionally.
            Assert.False(logger.IsEnabled(LogLevel.None));
        });
    }

    /// <summary>…and the emitted line goes through the SAME sink, so a consumer who
    /// installs one receives Blazor's diagnostics and the framework's own through
    /// one channel. That is the "one seam" DoD #6 asks for, asserted rather than
    /// claimed.</summary>
    [Fact]
    public void TheRenderersILogger_EmitsThroughTheSameSink()
    {
        ILogger logger = new NativeRendererLoggerFactory().CreateLogger("Blazor.Cat");
        var seen = new List<(BnLogLevel, string, string)>();

        WithSink(seen, () => WithLevel(BnLogLevel.Warn, () =>
        {
            logger.LogInformation("suppressed narration");
            logger.LogWarning("a real warning");
        }));

        (BnLogLevel level, string category, string message) = Assert.Single(seen);
        Assert.Equal(BnLogLevel.Warn, level);
        Assert.Equal("BlazorNative.Renderer/Blazor.Cat", category);
        Assert.Contains("a real warning", message);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static Exception Caught()
    {
        try
        {
            try { throw new FormatException("inner-boom"); }
            catch (Exception inner) { throw new InvalidOperationException("outer-boom", inner); }
        }
        catch (Exception ex) { return ex; }
    }

    private static void WithLevel(BnLogLevel level, Action body)
    {
        BnLogLevel original = BnLog.Level;
        try
        {
            BnLog.Level = level;
            body();
        }
        finally { BnLog.Level = original; }
    }

    private static void WithSink(List<(BnLogLevel, string, string)> seen, Action body)
    {
        Action<BnLogLevel, string, string>? original = BnLog.Sink;
        try
        {
            BnLog.Sink = (l, c, m) => seen.Add((l, c, m));
            body();
        }
        finally { BnLog.Sink = original; }
    }
}
