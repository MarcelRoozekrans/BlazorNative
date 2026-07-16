package io.blazornative.shell

/**
 * Phase 7.5 (design decision 2) — **THE ERROR-DISPATCH DECISION, AS A PURE FUNCTION.**
 *
 * Shared source set, JVM-tested ([isLiveImageRequest]'s file says why at length): the two
 * rules this table encodes are exactly the ones **no device test can reliably stage on
 * Android** — the liveness half needs the reset collision (same id, same generation,
 * different view), and the defer half needs a terminal failure that completes SYNCHRONOUSLY
 * inside `applyBatch`, which Coil has no deterministic path for (its memory cache proves the
 * synchronous-SUCCESS case; a 404 is never cached). iOS DOES have one — `URL(string:) → nil`
 * fails synchronously by construction — so Gate 3 stages the defer live and this table is
 * what guarantees the two shells decided the same thing. Decision-table-tested on the JVM:
 * the design's own risk-table mitigation, by name.
 *
 * The three rows, each normative (design §decision 2, "Dispatch discipline") — AND THE ROW
 * ORDER IS NORMATIVE TOO (Gate 3 review, I-1): liveness, THEN applyingBatch, THEN
 * handlerAttached. The batch question must be asked BEFORE the handler question, because
 * mid-batch the handler state is not a fact yet — it is a race with the rest of the batch.
 * At mount the wire order puts `src` (seq 24) BEFORE `attachEvent "error"` (seq 27) in ONE
 * batch, so iOS's synchronous nil-URL failure asks its question three patches early; a table
 * that consulted `handlerAttached` first answered DROP — permanently — for a handler the
 * SAME batch was about to attach, and `OnError` never fired for
 * `<BnImage Src="<unparseable>" OnError="...">` at mount (this shell's failure lands
 * post-batch and dispatched: a parity break, not a shared rule). DEFER re-asks at fire time
 * (`WidgetMapper.decideAndDispatchError` posts ITSELF), when the handler state has SETTLED:
 * attached-by-batch-end dispatches exactly once; never-attached still drops — at fire time,
 * where the answer is true.
 *
 *  - **[ImageErrorDispatchAction.DROP] — only a LIVE request's failure dispatches, and only
 *    into a handler that is attached WHEN THE QUESTION CAN BE ANSWERED (outside a batch).**
 *    The liveness gate is [isLiveImageRequest] — THE SAME pure
 *    function the paint asks, composed by name, not a re-implementation: one guard, two
 *    consumers (paint + dispatch), so the unit test that pins the conjunction defends both.
 *    A superseded request's error is a stale callback and dispatches nothing, exactly as it
 *    paints nothing; an unbound `OnError` never attached, so there is no wire to ride
 *    (attach-iff-HasDelegate — the .NET half already pinned it).
 *  - **[ImageErrorDispatchAction.DEFER] — a dispatch may never run inside a patch batch,
 *    and no PERMANENT verdict but liveness-drop may be reached there either.**
 *    Terminal callbacks CAN complete synchronously inside `UpdateProp("src")` (6.3's most
 *    surprising finding — `Dispatchers.Main.immediate` / Kingfisher's memory hit), and a
 *    dispatch from inside `applyBatch` is re-entrant dispatch under a non-re-entrant guard
 *    (the 6.3 re-solve lesson's evil twin). **DEFERRED, NEVER DROPPED** — unlike the
 *    re-solve, which the batch's own CommitFrame subsumes, a swallowed event never happened.
 *  - **[ImageErrorDispatchAction.DISPATCH_NOW]** — the ordinary asynchronous failure: main
 *    thread, no batch in flight, a live request, a bound handler.
 *
 * CANCELLED is not an error and never reaches this decision at all — the cancel callback has
 * no dispatch site (`WidgetMapper.onImageCancelled`), which is structural rather than a row
 * here on purpose: a row would imply the question gets asked, and it must not be.
 */
internal enum class ImageErrorDispatchAction { DISPATCH_NOW, DEFER, DROP }

/**
 * What a terminal image FAILURE may do about the `error` wire. The first four parameters are
 * [isLiveImageRequest]'s own, passed through verbatim — the liveness question is that
 * function's and nobody else's.
 */
internal fun imageErrorDispatchAction(
    currentGeneration: Int?,
    requestGeneration: Int,
    currentView: Any?,
    requestView: Any?,
    handlerAttached: Boolean,
    applyingBatch: Boolean,
): ImageErrorDispatchAction = when {
    !isLiveImageRequest(currentGeneration, requestGeneration, currentView, requestView) ->
        ImageErrorDispatchAction.DROP
    applyingBatch -> ImageErrorDispatchAction.DEFER
    !handlerAttached -> ImageErrorDispatchAction.DROP
    else -> ImageErrorDispatchAction.DISPATCH_NOW
}
