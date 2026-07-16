// ─────────────────────────────────────────────────────────────────────────────
// BnImageErrorDispatch — Phase 7.5 (design decision 2): **THE ERROR-DISPATCH
// DECISION, AS A PURE FUNCTION.** The Swift twin of Kotlin's `ImageErrorDispatch.kt`,
// row for row.
//
// On Android the file lives in the shared source set so the JVM lane can pin the
// DEFER row before any emulator boots — because **Android has no deterministic
// synchronous-failure path** (Coil's memory cache proves synchronous SUCCESS; a
// 404 is never cached). iOS DOES have one, **by construction**: `URL(string:)` →
// nil fails synchronously inside `UpdateProp("src")`, inside the batch — so this
// shell is where the defer row is staged LIVE, end to end
// (`BnImagePolishMapperTests`' nil-URL-in-batch test), and this table is what
// guarantees the two shells decided the same thing (the design's risk-table row:
// "staged deterministically on iOS (nil URL), decision-table-tested on the JVM").
//
// The three rows, each normative (design §decision 2, "Dispatch discipline") —
// AND THE ROW ORDER IS NORMATIVE TOO (Gate 3 review, I-1): liveness, THEN
// applyingBatch, THEN handlerAttached. The batch question must be asked BEFORE
// the handler question, because mid-batch the handler state is not a fact yet —
// it is a race with the rest of the batch. At mount the wire order puts `src`
// (seq 24) BEFORE `attachEvent "error"` (seq 27) in ONE batch, so this shell's
// synchronous nil-URL failure asks its question three patches early; a table
// that consulted `handlerAttached` first answered DROP — permanently — for a
// handler the SAME batch was about to attach, and `OnError` never fired for
// `<BnImage Src="<unparseable>" OnError="...">` at mount (Android's failure
// lands post-batch and dispatched: a parity break, not a shared rule). DEFER
// re-asks at fire time (`decideAndDispatchError` posts ITSELF), when the
// handler state has SETTLED: attached-by-batch-end dispatches exactly once;
// never-attached still drops — at fire time, where the answer is true.
//
//  - **[.drop] — only a LIVE request's failure dispatches, and only into a
//    handler that is attached WHEN THE QUESTION CAN BE ANSWERED (outside a
//    batch).** The liveness gate is `bnIsLiveImageRequest` — THE SAME
//    pure function the paint asks, composed by name, not a re-implementation: one
//    guard, two consumers (paint + dispatch), so the unit test that pins the
//    conjunction defends both. A superseded request's error is a stale callback
//    and dispatches nothing, exactly as it paints nothing; an unbound `OnError`
//    never attached, so there is no wire to ride (attach-iff-HasDelegate — the
//    .NET half already pinned it).
//  - **[.deferToFreshTurn] — a dispatch may never run inside a patch batch, and
//    no PERMANENT verdict but liveness-drop may be reached there either.**
//    Terminal callbacks CAN complete synchronously inside `UpdateProp("src")`
//    (6.3's most surprising finding — Kingfisher's `.mainCurrentOrAsync` memory
//    hit; and the nil-URL failure above is synchronous ALWAYS), and a dispatch
//    from inside `applyBatch` is re-entrant dispatch under a non-re-entrant guard
//    (the 6.3 re-solve lesson's evil twin). **DEFERRED, NEVER DROPPED** — unlike
//    the re-solve, which the batch's own CommitFrame subsumes, a swallowed event
//    never happened.
//  - **[.dispatchNow]** — the ordinary asynchronous failure: main thread, no
//    batch in flight, a live request, a bound handler.
//
// CANCELLED is not an error and never reaches this decision at all — the cancel
// callback has no dispatch site (`BnWidgetMapper.onImageCancelled`), which is
// structural rather than a row here on purpose: a row would imply the question
// gets asked, and it must not be.
// ─────────────────────────────────────────────────────────────────────────────

import Foundation

/// What a terminal image FAILURE may do about the `error` wire.
enum BnImageErrorDispatchAction {
    case dispatchNow
    case deferToFreshTurn
    case drop
}

/// The decision. The first four parameters are `bnIsLiveImageRequest`'s own,
/// passed through verbatim — the liveness question is that function's and
/// nobody else's.
func bnImageErrorDispatchAction(currentGeneration: Int?,
                                requestGeneration: Int,
                                currentView: AnyObject?,
                                requestView: AnyObject?,
                                handlerAttached: Bool,
                                applyingBatch: Bool) -> BnImageErrorDispatchAction {
    if !bnIsLiveImageRequest(currentGeneration: currentGeneration,
                             requestGeneration: requestGeneration,
                             currentView: currentView,
                             requestView: requestView) {
        return .drop
    }
    if applyingBatch { return .deferToFreshTurn }
    if !handlerAttached { return .drop }
    return .dispatchNow
}
