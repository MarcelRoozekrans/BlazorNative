namespace BlazorNative.Core;

// ─────────────────────────────────────────────────────────────────────────────
// BnImageErrorEventArgs — Phase 7.5 (design decision 2).
//
// The typed args an `error` dispatch carries into an OnError handler. It lives
// in CORE for BnScrollEventArgs's exact reason (the file above it in this
// directory): the renderer CONSTRUCTS it (BuildEventArgs's "error" arm parses
// the flat-JSON envelope's payload string — strictly: a missing/empty payload
// is a shell contract violation → FormatException → the loud rc-2 fault) and
// the component library CONSUMES it (BnImage.OnError is an
// EventCallback<BnImageErrorEventArgs>) — and Components does not reference
// Renderer, so the shared contract sits in the assembly both depend on.
//
// ONE string, deliberately, and it is the URL — not a message, not JSON. A
// platform error message is the one thing two loaders (Coil, Kingfisher) will
// never agree on; parity demands the two shells dispatch IDENTICAL payloads
// for the same failure, and the wire `src` is the only fact they share. It is
// also exactly what the handler needs (WHICH image failed) — everything else
// is .NET state the author already holds. The scroll grammar, not the items
// grammar: strict, single-value, invariant, nothing structured to parse.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Event arguments for the <c>error</c> event: a shell's image load
/// for this node terminated in failure.</summary>
/// <remarks>Dispatched AT MOST ONCE per terminated request, and only for a
/// LIVE one — the shells' dispatch site sits behind the same
/// generation-and-identity guard the paint does, so a superseded request's
/// error is a stale callback and dispatches nothing. CANCELLED is not an
/// error: a <c>Src</c> change, <c>src → null</c> and node removal all cancel,
/// and none dispatches. Failure never changes measurement — a declared box
/// holds, an intrinsic node stays 0 × 0 (the 6.3 failure row, unchanged);
/// fallback UI is the app's re-render on this event, never the shell's.</remarks>
public sealed class BnImageErrorEventArgs : EventArgs
{
    /// <summary>The failed source — the <c>src</c> the wire carried for the
    /// node, verbatim. Never null or empty: a dispatch without it is a shell
    /// contract violation and faults loudly at the ingress (rc 2).</summary>
    public required string Src { get; init; }
}
