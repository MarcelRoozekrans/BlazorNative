namespace BlazorNative.Core;

// ─────────────────────────────────────────────────────────────────────────────
// BnScrollEventArgs — Phase 7.2 Task 1.3 (design §"The wire contract").
//
// The typed args a `scroll` dispatch carries into an OnScroll handler. It
// lives in CORE deliberately: the renderer CONSTRUCTS it (BuildEventArgs's
// "scroll" arm parses the flat-JSON payload) and the component library
// CONSUMES it (BnScroll.OnScroll is an EventCallback<BnScrollEventArgs>) —
// and Components does not reference Renderer, so the shared contract sits in
// the assembly both already depend on, next to INavigationManager.
//
// ONE number, deliberately. The wire contract says the payload is "the
// content offset in dp/pt" — scrolling is VERTICAL-ONLY (6.2 decision 2), so
// there is no X to carry, and contentSize/viewport are things .NET already
// knows (it authored them). A richer args (velocity, edges) is a design
// conversation with the conflation contract, not a field addition.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Event arguments for the <c>scroll</c> event: the shell-conflated
/// vertical content offset of a scroll viewport.</summary>
/// <remarks>The offset arrives in density-independent units (dp on Android —
/// divided by density at the source, the 6.1 one-conversion-site rule — and
/// points on iOS) and is the LATEST sample the shell's conflation slot held:
/// at most one <c>scroll</c> dispatch per committed frame, older samples
/// dropped (scroll position is idempotent state, not an event log). It is
/// raw from the shell — iOS rubber-banding can make it negative or push it
/// past the scroll range; consumers clamp (BnList's window function
/// does).</remarks>
public sealed class BnScrollEventArgs : EventArgs
{
    /// <summary>Vertical content offset in dp/pt. 0 = top of the content.</summary>
    public float OffsetY { get; init; }
}
