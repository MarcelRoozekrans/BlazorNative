using BlazorNative.Core;
using BlazorNative.Runtime;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// HostSafeAreaTests — Phase 14.2 Task 2: the reserved "safeAreaChanged" host-event
// name. Shells report the area of the display obscured by system UI (notches,
// status bars, home indicators) as a flat JSON payload of string-valued numbers —
// {"top":"47","right":"0","bottom":"34","left":"0"} — over the EXISTING
// blazornative_host_event export (wire vocabulary + a .NET branch, NOT an ABI
// change, the same shape "back"/9.1's "navigate" used).
//
// The rc contract (mirrors "back"/"navigate", but the malformed case is new):
//   0 = stored AND a live session was told to re-render
//   1 = stored, but nothing mounted to re-render (no session) OR no payload sent
//       at all (NULL/empty — "nothing to act on", the same convention
//       DispatchHostNavigate uses for a missing route; also what makes 14.0's
//       EveryReservedHostEvent_IsRoutedRatherThanFallingThrough pin — which drives
//       every reserved arm with payload: null — read this arm as routed)
//   2 = inherited from DispatchHostEventCore's shared preamble (a NULL/empty
//       event NAME never reaches this arm)
//   3 = malformed: invalid JSON, OR valid JSON missing one of the four edge keys,
//       OR an edge value that is not a parseable number. A missing/bad edge is
//       the shell disagreeing with the contract; defaulting it to zero would hide
//       the disagreement, exactly the silent-divergence class this milestone
//       exists to close. The insets are NEVER stored on rc 3.
//
// BnSafeAreaInsets.Current is stored UNCONDITIONALLY whenever the payload parses
// (rc 0 AND rc 1) — so a later mount starts with the right values instead of
// rendering at zero a second time — but never on rc 3.
// ─────────────────────────────────────────────────────────────────────────────

[Collection("host-session")]
public sealed class HostSafeAreaTests
{
    [Fact]
    public void SafeAreaChanged_WithNoSessionMounted_StoresTheInsets_AndReportsNothingToRerender()
    {
        HostSession.ResetForTests();
        BnSafeAreaInsets.ResetForTests();

        // rc 1 = "routed, but nothing to re-render" — the same shape `back` and
        // `navigate` report when no nav manager is mounted. The rc says whether a
        // RE-RENDER happened, not whether the value was kept.
        Assert.Equal(1, Exports.DispatchHostEventCore(
            BnHostEvents.SafeAreaChanged,
            """{"top":"47","right":"0","bottom":"34","left":"0"}"""));

        // Stored anyway, so the NEXT mount starts with the right values rather
        // than rendering at zero a second time.
        Assert.Equal(new BnSafeAreaInsets(47, 0, 34, 0), BnSafeAreaInsets.Current);
    }

    [Fact]
    public void SafeAreaChanged_WithAMalformedPayload_ReportsMalformed_AndDoesNotFaultTheLane()
    {
        HostSession.ResetForTests();
        BnSafeAreaInsets.ResetForTests();

        // rc 3 = malformed input, the same code dispatch_event returns for bad args.
        // A shell bug must never fault the dispatch lane.
        Assert.Equal(3, Exports.DispatchHostEventCore(BnHostEvents.SafeAreaChanged, "not json"));
        Assert.Equal(BnSafeAreaInsets.Zero, BnSafeAreaInsets.Current);
    }

    [Fact]
    public void SafeAreaChanged_MissingAKey_IsMalformed_RatherThanSilentlyZero()
    {
        HostSession.ResetForTests();
        BnSafeAreaInsets.ResetForTests();

        // A missing edge is a shell that does not agree with the contract. Defaulting
        // it to zero would hide the disagreement — exactly the silent-divergence class
        // this milestone exists to close.
        Assert.Equal(3, Exports.DispatchHostEventCore(
            BnHostEvents.SafeAreaChanged, """{"top":"47","bottom":"34"}"""));
        Assert.Equal(BnSafeAreaInsets.Zero, BnSafeAreaInsets.Current);
    }

    [Fact]
    public void SafeAreaChanged_ANonNumericEdge_IsMalformed()
    {
        // The "string-valued numbers" contract's other half: a key can be present
        // and still disagree with the contract by carrying a value that is not a
        // parseable number. Same rc as a missing key — both are the shell not
        // honouring the wire shape, not a value the renderer can act on.
        HostSession.ResetForTests();
        BnSafeAreaInsets.ResetForTests();

        Assert.Equal(3, Exports.DispatchHostEventCore(
            BnHostEvents.SafeAreaChanged, """{"top":"47","right":"NaNope","bottom":"34","left":"0"}"""));
        Assert.Equal(BnSafeAreaInsets.Zero, BnSafeAreaInsets.Current);
    }

    [Fact]
    public void SafeAreaChanged_NullPayload_IsNotMalformed_ReportsNothingToRerender()
    {
        // No payload at all (as opposed to a payload missing a key) is "nothing
        // sent" rather than "sent something that disagrees with the contract" —
        // the same convention DispatchHostNavigate uses for a missing route. This
        // is also exactly what 14.0's EveryReservedHostEvent_IsRoutedRatherThanFallingThrough
        // pin drives every reserved arm with (payload: null), so this arm must
        // report rc 1 here, not rc 3, to read as routed rather than fallen-through.
        HostSession.ResetForTests();
        BnSafeAreaInsets.ResetForTests();

        Assert.Equal(1, Exports.DispatchHostEventCore(BnHostEvents.SafeAreaChanged, null));
        Assert.Equal(BnSafeAreaInsets.Zero, BnSafeAreaInsets.Current);
    }

    [Fact]
    public void SafeAreaChanged_WithALiveSession_StoresAndReportsReRendered()
    {
        HostSession.ResetForTests();
        BnSafeAreaInsets.ResetForTests();
        try
        {
            HostSession.EnsureSession();

            // rc 0 = stored AND a live session was told to re-render (the renderer
            // exists — CurrentRenderer is non-null — regardless of what, if
            // anything, is subscribed to BnSafeAreaInsets.Changed today; the
            // consuming component is a later task).
            Assert.Equal(0, Exports.DispatchHostEventCore(
                BnHostEvents.SafeAreaChanged,
                """{"top":"20","right":"0","bottom":"10","left":"0"}"""));
            Assert.Equal(new BnSafeAreaInsets(20, 0, 10, 0), BnSafeAreaInsets.Current);
        }
        finally
        {
            HostSession.ResetForTests();
            BnSafeAreaInsets.ResetForTests();
        }
    }
}
