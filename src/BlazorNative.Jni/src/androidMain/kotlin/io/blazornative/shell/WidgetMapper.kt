package io.blazornative.shell

import android.content.Context
import android.graphics.Color
import android.graphics.drawable.BitmapDrawable
import android.graphics.drawable.Drawable
import android.os.Handler
import android.os.Looper
import android.text.Editable
import android.text.TextWatcher
import android.util.Log
import android.util.TypedValue
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.EditText
import android.widget.ImageView
import android.widget.ScrollView
import android.widget.Spinner
import android.widget.TextView
import coil.imageLoader
import coil.request.Disposable
import coil.request.ImageRequest
import coil.size.Size
import io.blazornative.jni.RenderFrame
import io.blazornative.jni.RenderPatch

/**
 * Phase 2.5: maps parsed [RenderFrame] patches to real Android [View] mutations.
 *
 * Threading: `apply(frame)` is called on the native frame-callback thread. The
 * mapper collects patches until [RenderPatch.CommitFrame], then posts the batch
 * to the main looper for atomic application. Caller-thread-agnostic.
 *
 * Patch coverage: CreateNode (all 7 NodeTypes wired, placement via
 * insertIndex — see [handleCreate]), ReplaceText, RemoveNode, UpdateProp,
 * SetStyle, CommitFrame, and — live since Phase 3.2 — AttachEvent /
 * DetachEvent (click listener + re-entrancy-guarded change TextWatcher +
 * focus/blur via a single per-view OnFocusChangeListener since Phase 4.2; see
 * [handleAttachEvent]; detach dispatches on the patch's eventName since
 * Phase 3.3 Task 9). AppendChild was DELETED in Phase 3.3 (DoD #10):
 * CreateNode.insertIndex carries placement instead — its wire kind (2) stays
 * reserved-dormant and never reaches this class.
 *
 * Events: [onUiEvent] is invoked from UI listeners with (handlerId, eventName,
 * payload) — production wires it to BlazorNativeRuntime.dispatchEvent, which is
 * safe to call from the UI thread (non-blocking submit to the
 * BlazorNative-Dispatch lane). The default no-op keeps event-agnostic tests
 * compiling unchanged.
 *
 * Patch model: src/BlazorNative.Renderer/PatchProtocol.cs (the wire itself is
 * the typed-struct C ABI decoded by [io.blazornative.jni.NativeFrameAdapter]).
 * Source of truth for the NodeType → widget table: docs/planning/MILESTONE.md DoD #6.
 *
 * ── PHASE 6.1: YOGA OWNS PLACEMENT ───────────────────────────────────────────
 * This class no longer places anything. Every node gets a [YogaLayout] node
 * mirrored against the view tree; `view` containers are [BnYogaFrameLayout]s
 * (children absolutely placed, nothing stacked); CommitFrame runs ONE
 * `calculateLayout` and applies every computed frame. An un-styled tree still
 * renders as a vertical stack — that is Yoga's default `flexDirection: column`,
 * not a LinearLayout, and it is the regression signal that the ENGINE changed
 * and the BEHAVIOUR did not.
 *
 * ── PHASE 6.3: THE SHELL FETCHES THE BYTES ───────────────────────────────────
 * `image` was the last stubbed leaf — it made a widget and measured ZERO, because
 * no shell had a source-loading path. `UpdateProp("src", …)` now drives one
 * ([handleSrc]): Coil fetches and decodes off the main thread, and on the main
 * thread the shell sets the drawable, records the NATURAL size, `markDirty`s the
 * Yoga node (the 6.1 path) and re-solves (the 6.2 path). ONE reflow, never two.
 * There is no binary path on the wire and there does not need to be — .NET names
 * the source. The normative contract (and the rows this class owes it) is
 * docs/plans/2026-07-14-phase-6.3-design.md §"The parity contract"; iOS mirrors it
 * with Kingfisher, and asserts THE SAME FRAMES.
 *
 * THREE THINGS IN THAT PATH ARE TWO-SHELL LANDMINES, and Kingfisher steps on two of
 * them the same way Coil does — so they are stated in the contract, not just here:
 *   1. THE UNIT — one FILE PIXEL is one dp/pt. Read the DECODED BITMAP's own pixel
 *      count ([naturalSizeOf]), never `intrinsicWidth` (density-scaled, device- and
 *      version-dependent). iOS gets it free from `UIImage(data:).scale == 1` and must
 *      therefore NOT set Kingfisher's `scaleFactor` or any downsampling processor.
 *   2. A MEMORY-CACHE HIT COMPLETES SYNCHRONOUSLY, inside `enqueue`, inside applyBatch
 *      (Coil dispatches on `Dispatchers.Main.immediate`; Kingfisher's `setImage` calls
 *      its completionHandler synchronously on a memory hit). See [resolveLayout] and
 *      the tail of [handleSrc] — the re-entrancy guard and the in-flight bookkeeping
 *      both depend on knowing it.
 *   3. THE STALE-CALLBACK GUARD IS GENERATION *AND* IDENTITY, in EVERY callback — not
 *      just the painting one — and it is ONE FUNCTION asked at BOTH call sites
 *      ([isLiveImageRequest], from [isLive] and from [clearIfMine]), so the one unit test
 *      that pins the conjunction defends both.
 *   4. EVERY `src` WRITE BUMPS THE GENERATION — **INCLUDING A CLEAR** ([handleSrc]).
 *      `cancelImageRequest` is best-effort BY DEFINITION; the generation is what makes the
 *      callback it failed to prevent harmless. A `Src` → null whose dispose lost the race
 *      would otherwise RE-INFLATE the node the author just cleared.
 *
 * ── PHASE 7.2: THE onScroll WIRE — THE FIRST 60Hz PRODUCER ───────────────────
 * A scroll node with the `scroll` event attached reports its content offset to
 * .NET over the EXISTING dispatch wire — CONFLATED. The shell keeps ONE pending
 * offset per scroll node ([ScrollWire]); a new native sample REPLACES it (never
 * queue — scroll position is idempotent state, not an event log) and at most one
 * dispatch is in flight per node at a time: submit when the lane is free, conflate
 * while it is not, dispatch the freshest value on the completion signal
 * ([maybeDispatchScroll]). Offsets cross in dp (px ÷ density at the source — the
 * 6.1 one-conversion-site rule), as an invariant float payload, exactly what
 * `NativeRenderer.ParseScrollOffset` parses. The contract is NORMATIVE
 * (docs/plans/2026-07-15-phase-7.2-design.md §"The wire contract") and iOS
 * mirrors the CONFLATION in Gate 3 — not the Android mechanics (the listener
 * API, the px÷density, and the mid-batch re-clamp echo are this shell's own;
 * see the section comment above [onScrollSample]).
 *
 * [handleSetStyle] is now a ROUTER over the partitioned allow-list
 * (`NativeRenderer.YogaStyleAttributes` / `VisualStyleAttributes`): a LAYOUT name
 * goes to the Yoga node and NOWHERE else — in particular `padding` no longer
 * reaches `view.setPadding(...)`, because Yoga already lays a container's
 * children out inside its padding box and the surviving call would apply the
 * inset a second time.
 */
class WidgetMapper(
    private val context: Context,
    private val root: ViewGroup,
    private val onUiEvent: (handlerId: Int, eventName: String, payload: String?) -> Unit = { _, _, _ -> },
    /**
     * Phase 7.2 — the scroll wire's dispatch, WITH a completion signal: the
     * conflation ([ScrollWire]) may not submit the next scroll dispatch until
     * the previous one has LEFT the lane, and fire-and-forget [onUiEvent]
     * cannot say when that is. Production wires it to
     * `BlazorNativeRuntime.dispatchEvent(h, "scroll", payload, onComplete)`
     * (MainActivity); `onComplete` may arrive on ANY thread — the mapper
     * marshals to the main handler itself. The default completes synchronously
     * through [onUiEvent], which keeps every event-agnostic test compiling
     * unchanged (the 3.2 posture) — and is still a correct conflation, just
     * one whose lane is never busy longer than a looper turn.
     */
    private val onScrollEvent: (handlerId: Int, offsetPayload: String, onComplete: () -> Unit) -> Unit =
        { handlerId, offsetPayload, onComplete -> onUiEvent(handlerId, "scroll", offsetPayload); onComplete() },
) {
    private val nodes = mutableMapOf<Int, View>()
    private val mainHandler = Handler(Looper.getMainLooper())
    private val pending = mutableListOf<RenderPatch>()

    /**
     * The nodeIds that are ALIASES, not owners: a `text` node COLLAPSED onto its
     * text-bearing parent (Button/EditText/TextView) by [handleCreate]. Its
     * `nodes` entry points at the PARENT's view, which it does not own.
     *
     * Tracked explicitly because [handleRemove] cannot tell the two apart from the
     * map alone, and guessing wrong is expensive in both directions: removing an
     * alias must NOT detach the parent Button (pre-6.1 it did — `removeView(nodes[textId])`
     * removes the BUTTON) and must NOT remove a Yoga node (the alias has none, so
     * Yoga would keep laying out and reserving space for a Button that is no longer
     * in the view hierarchy, offsetting every sibling after it by a ghost).
     */
    private val collapsedAliases = mutableSetOf<Int>()

    /**
     * Phase 6.2 — **the SYNTHETIC content views**: a `scroll` node's id → the
     * [BnYogaFrameLayout] inside its `ScrollView` that holds its wire children.
     *
     * A `ScrollView` hosts exactly ONE direct child (it throws on a second), and that
     * child is the thing whose height becomes the scroll range. So the scroll node's
     * wire children go **into the content view**, at the index the patch names — and
     * the Yoga tree does the same, into the synthetic content NODE, or the two trees'
     * child indices skew and every frame after the first row is wrong. The synthetic
     * node is in BOTH trees or NEITHER: this map's entry and [YogaLayout]'s are made
     * in the same breath and purged in the same breath.
     *
     * It is also the answer to "what container does a child of node N parent into?" —
     * see [containerFor]. Never on the wire; the renderer knows nothing about it.
     */
    private val scrollContents = mutableMapOf<Int, ViewGroup>()

    /**
     * Phase 6.3 — **one in-flight image request per `image` node**, and everything a callback
     * needs to know whether the entry it is looking at **is its own**.
     *
     * Cancellation is **memory safety, not hygiene** (6.3 non-negotiable #4): a completion
     * firing into a removed node would paint into a detached widget here and touch a
     * **freed `YGNodeRef`** on iOS. So the entry is disposed on a `Src` change
     * ([handleSrc]), on node removal ([handleRemove] — as part of the SUBTREE purge, which
     * is the shape navigation actually emits) and on teardown ([destroy]).
     *
     * ── WHY THE ENTRY CARRIES THE GENERATION *AND* THE VIEW (Gate 2 review, C1) ─────────
     * It used to be a bare `Disposable`, and the two terminal callbacks that do not paint
     * ([onImageFailed], [onImageCancelled]) evicted it on a **generation match alone** —
     * which is precisely the case a generation cannot decide. `/image` → back → `/image`
     * re-uses this mapper and **node ids restart at 1**, so the OLD node 2's `onCancel`
     * (generation 1) lands as a later main-thread message, matches the **NEW** node 2's
     * generation 1, and **evicts the live new request's `Disposable`**. That request is then
     * un-cancellable: the next [handleRemove] finds nothing to cancel, [inFlightImageCount]
     * under-reports, and on iOS the un-cancelled completion runs against a freed node.
     *
     * So every callback goes through [clearIfMine], which drops the entry **only if the
     * entry IS the one this callback owns** — same generation AND the same `ImageView`
     * instance. See [isLiveImageRequest] (a pure function, unit-tested on the JVM lane) for
     * the same conjunction on the PAINT side.
     */
    private data class InFlight(
        val generation: Int,
        val view: ImageView,
        val disposable: Disposable,
    )
    private val imageRequests = mutableMapOf<Int, InFlight>()

    /**
     * The node's CURRENT generation — bumped by every `src` write, and **deliberately not
     * folded into [imageRequests]**, because the two answer different questions and one of
     * them has to survive the entry.
     *
     *  - [imageRequests] answers *"which request does this node have in flight, and whose is
     *    it?"* — and there may be **none** even while a request is completing: a Coil
     *    **memory-cache hit dispatches on `Dispatchers.Main.immediate`**, so `onSuccess` runs
     *    to completion **inside** `enqueue()`, before [handleSrc] has had a chance to record
     *    anything (Gate 2 review, C2 — the same is true of Kingfisher's `setImage` on a
     *    memory hit, so Gate 3 inherits it verbatim).
     *  - THIS answers *"has this node's `src` been written since that request was issued?"* —
     *    the question [isLiveImageRequest] must be able to ask on a node with no live entry
     *    at all. It is written BEFORE the enqueue, which is what makes the synchronous
     *    completion above still see itself as live and still paint.
     *
     * Purged with the node ([handleRemove]) for 6.2's reason: ids are reused.
     */
    private val imageGenerations = mutableMapOf<Int, Int>()

    /**
     * Test-only: every image request that has TERMINATED, in order — Coil's own per-node
     * `ImageRequest.Listener` verdict.
     *
     * It is the shell's half of **the synchronization gate** (6.3 non-negotiable #6, design
     * §"The synchronization gate"), which is normative and is the phase's most dangerous
     * failure mode: THE WIRE CARRIES NO COMPLETION SIGNAL, and two of `/image`'s three cases
     * assert that **nothing moved** — so a suite that reads the "after" frames straight
     * after mount passes both of them having fetched nothing, and a suite whose HTTP is
     * BLOCKED passes both of them too (a blocked load is indistinguishable from the 404 the
     * failure case expects). The tests therefore await THIS, counted, with a timeout that
     * fails — never a poll on a frame.
     *
     * **BOUNDED — a RING of the last [MAX_IMAGE_RESULTS]** (Gate 2 review, I3). This list is
     * appended by every terminal callback and it lives in PRODUCTION code, so unbounded it grows
     * for as long as the app runs: one entry per image, per navigation, forever, evicted only by
     * [destroy]. [YogaLayout.diagnosed] is evicted on node removal for exactly this reason, one
     * file away, citing the 6.2 lesson by name — but eviction-on-removal is not available here
     * (`removing_the_node_cancels_the_request_IN_FLIGHT` reads this log *after* the purge that is
     * the whole subject of the test). So it is CAPPED instead. The tests read a handful of entries
     * and never notice; the bound exists for the app, not for the suite.
     */
    private val imageResultLog = ArrayDeque<ImageResult>()

    /** Appends to the bounded [imageResultLog], evicting the oldest. Main-thread only — every
     * Coil listener callback is. */
    private fun recordImageResult(result: ImageResult) {
        imageTerminalCount++
        imageResultLog.addLast(result)
        while (imageResultLog.size > MAX_IMAGE_RESULTS) imageResultLog.removeFirst()
    }

    /** What Coil's listener said about one request. `CANCELLED` is a normal outcome of a
     * `Src` change or a node removal — and a SETUP FAILURE on `/image`, where nothing
     * cancels anything. */
    internal enum class ImageOutcome { SUCCESS, ERROR, CANCELLED }

    /** One terminated image request. The [url] is the one the WIRE carried, which is what
     * lets a test assert the demo pages point at the fixture server without transcribing a
     * URL out of a `.cs` file it cannot read. */
    internal data class ImageResult(val nodeId: Int, val url: String, val outcome: ImageOutcome)

    /** The layout engine (Phase 6.1). Mirrors this class's view tree; runs one
     * layout pass per committed frame and on every host resize. */
    private val yoga = YogaLayout(context, root)

    /**
     * Phase 3.2 re-entrancy guard: true while [applyBatch] runs. A programmatic
     * `setText` during patch application (ReplaceText/UpdateProp on an EditText)
     * fires its TextWatcher synchronously; the watcher checks this flag and
     * skips the dispatch — otherwise a change dispatch → re-render → setText
     * loop would spin. Plain field (no volatile/atomic): both applyBatch and
     * every watcher callback run on the main looper thread only.
     */
    private var applyingBatch = false

    /**
     * Live change-watchers keyed by NODEID so DetachEvent can remove them
     * (view tags would need res-ids; a map is simpler). Main-thread only.
     *
     * Phase 4.2 (stale-watcher fix): keyed by nodeId, not handlerId — a
     * last-wins re-attach (same node, new handlerId, NO preceding detach)
     * now REPLACES the node's watcher instead of stacking a second one on
     * the EditText. See [handleAttachEvent]'s change arm.
     */
    private val watchers = mutableMapOf<Int, Pair<EditText, TextWatcher>>()

    /**
     * Phase 4.2 (M4 DoD #4) — per-view focus/blur handler pair, keyed by
     * nodeId like [watchers]. Android has ONE focus listener slot per view
     * ([View.setOnFocusChangeListener]) and it fires BOTH directions
     * (hasFocus true/false), while the renderer attaches "focus" and "blur"
     * as two independent events — so the mapper keeps this pair and installs
     * a SINGLE listener that dispatches whichever side is currently attached
     * (see [handleAttachEvent]). Detach clears one side; the listener is
     * removed when both sides are gone. The view is stored so [handleRemove]
     * can purge by identity (the text collapse can alias nodeIds onto one
     * view). Main-thread only.
     */
    private class FocusEntry(val view: View) {
        var focusHandlerId: Int? = null
        var blurHandlerId: Int? = null
        val isEmpty: Boolean get() = focusHandlerId == null && blurHandlerId == null
    }
    private val focusEntries = mutableMapOf<Int, FocusEntry>()

    /**
     * Phase 7.2 — **THE CONFLATION SLOT** (the wire contract, design
     * §"The wire contract" — NORMATIVE, mirrored by iOS in Gate 3): one per
     * scroll node with the `scroll` event attached, keyed by nodeId like
     * [watchers].
     *
     *  - [pendingOffsetDp] is the ONE pending offset. A new native sample
     *    **REPLACES** it — never a queue: scroll position is idempotent STATE,
     *    not an event log, so only the freshest value is worth a dispatch and
     *    a slow consumer sees FEWER, FRESHER events, never a backlog.
     *  - [inFlight] is true from the moment a dispatch is SUBMITTED to the
     *    lane until its completion signal comes back ([maybeDispatchScroll]).
     *    While it is true — or while [applyingBatch] is — new samples conflate
     *    into the slot; the freshest value goes out when the lane frees.
     *  - [handlerId] is MUTABLE for the [watchers] reason (Phase 4.2): a
     *    last-wins re-attach (same node, new handlerId, no preceding detach)
     *    swaps the handler on the LIVE wire instead of stacking a second one —
     *    and deliberately KEEPS the pending offset and the in-flight flag,
     *    because they describe the NODE's scroll state, which a handler swap
     *    does not reset.
     *
     * Detach/purge is the 6.3 stale-callback discipline: DetachEvent and
     * [handleRemove] delete the wire, and the pending offset **dies with it,
     * never dispatched**. A completion that lands afterwards resets a flag on
     * an unreachable object and re-consults the LIVE map ([maybeDispatchScroll]
     * starts with a map lookup), so it is a no-op by construction.
     */
    private class ScrollWire(var handlerId: Int) {
        var pendingOffsetDp: Float? = null
        var inFlight = false
    }
    private val scrollWires = mutableMapOf<Int, ScrollWire>()

    /** Test-only (Phase 7.2): native scroll samples the listener delivered to a live
     * wire — the numerator of the throughput evidence (samples-seen vs
     * events-dispatched, the contract's "Throughput evidence" row). Main-thread only. */
    internal var scrollSamplesSeen: Int = 0
        private set

    /** Test-only (Phase 7.2): scroll dispatches actually SUBMITTED to the lane — the
     * denominator of the conflation ratio. By construction ≤ [scrollSamplesSeen], and
     * ≤ (completions + live wires): at most one in flight per node, ever. */
    internal var scrollDispatchesSent: Int = 0
        private set

    /** Test-only (Phase 7.2): the offset (dp) the LAST submitted dispatch carried —
     * how a test asserts "the FINAL offset always arrives" without parsing logcat. */
    internal var lastScrollDispatchDp: Float? = null
        private set

    /** Test-only (Phase 7.2): live conflation slots — must return to 0 after
     * detach/purge, or a detached node's pending offset is one looper turn from
     * being dispatched into a stale handler. */
    internal val scrollWireCount: Int get() = scrollWires.size

    /** Test-only (Phase 7.2): a node's pending (conflated, not yet dispatched)
     * offset in dp — null when the slot is empty or the node has no wire. */
    internal fun scrollPendingOffsetDp(nodeId: Int): Float? = scrollWires[nodeId]?.pendingOffsetDp

    /** Test-only (Phase 7.2): wires with work outstanding — a dispatch in flight or
     * a conflated offset waiting for the lane. 0 = the scroll wire is QUIESCENT
     * (the freshest sample has been dispatched AND completed) — the device tests'
     * settle gate, because "the FINAL offset always arrives" is only assertable
     * about a wire that has finished arriving. */
    internal val scrollBusyWireCount: Int
        get() = scrollWires.values.count { it.inFlight || it.pendingOffsetDp != null }

    fun apply(frame: RenderFrame) {
        for (patch in frame.patches) {
            pending.add(patch)
            if (patch is RenderPatch.CommitFrame) {
                val batch = pending.toList()
                pending.clear()
                mainHandler.post { applyBatch(batch) }
            }
        }
    }

    private fun applyBatch(patches: List<RenderPatch>) {
        applyingBatch = true
        try {
            for (patch in patches) when (patch) {
                is RenderPatch.CreateNode  -> handleCreate(patch)
                is RenderPatch.ReplaceText -> handleReplaceText(patch)
                is RenderPatch.RemoveNode  -> handleRemove(patch)
                is RenderPatch.UpdateProp  -> handleUpdateProp(patch)
                is RenderPatch.SetStyle    -> handleSetStyle(patch)
                // Phase 6.1: the frame boundary IS the layout trigger — ONE
                // calculateLayout over the whole tree, then every computed frame
                // applied. Inside the applyingBatch guard on purpose: the pass
                // measures real widgets, and a measure that moved focus or text
                // must not dispatch back into .NET.
                is RenderPatch.CommitFrame -> yoga.calculateAndApply()
                is RenderPatch.AttachEvent -> handleAttachEvent(patch)
                is RenderPatch.DetachEvent -> handleDetachEvent(patch)
            }
        } finally {
            applyingBatch = false
        }
        // Phase 7.2: scroll samples that arrived DURING the batch (ScrollView's
        // per-layout offset re-clamp inside calculateAndApply fires the scroll
        // listener SYNCHRONOUSLY — the 6.2 Android-specific mechanism) were
        // CONFLATED into their slots, per the wire contract's backpressure row.
        // The batch end is a lane-availability: flush the freshest values now,
        // AFTER the guard dropped — a dispatch from inside the guard would be
        // swallowed, and the re-clamped offset would never reach .NET.
        flushScrollWires()
    }

    /**
     * Phase 3.2: wires a native listener that forwards to [onUiEvent].
     *
     * NodeId resolution rides the text-collapse (see [handleCreate]): the
     * renderer emits AttachEvent against the interactive element's OWN nodeId
     * (Hello: nodeId 4 = the Button view itself), so `nodes[p.nodeId]` is the
     * real widget even when its text child shares the mapping. Task-9
     * invariant: the collapse aliases text nodeIds onto non-ViewGroup parents,
     * which is safe for indexed inserts (CreateNode.insertIndex) because those
     * only ever target ViewGroup containers — the collapse must stay as-is;
     * indexed inserts depend on it.
     *
     * Detach vs re-attach (Phase 3.3): a GENUINE on* attribute removal now
     * emits DetachEventPatch (with eventName) — [handleDetachEvent] handles
     * it. Re-attach for the same (node, event) is STILL last-wins with NO
     * preceding DetachEvent (see RenderFrame.kt's mapping notes): click
     * re-attach overwrites via setOnClickListener; change re-attach — FIXED
     * in Phase 4.2 — removes the node's prior watcher before adding the new
     * one ([watchers] is keyed by nodeId now, not handlerId), so a re-attach
     * can no longer STACK watchers: a text change dispatches exactly once,
     * always with the live handlerId. The rc-0 at-most-once stale contract
     * itself is unchanged — a dispatch that races a handler swap can still
     * carry a just-retired id and is still absorbed downstream.
     *
     * Focus/blur (Phase 4.2, M4 DoD #4): Android exposes ONE focus listener
     * slot per view, firing both directions — the renderer's independent
     * "focus"/"blur" attaches land in the per-view [FocusEntry] pair and a
     * single [View.setOnFocusChangeListener] dispatches whichever side is
     * attached at fire time (the listener reads the pair LIVE, so attach /
     * detach of either side never re-installs it). Installing the listener
     * on every focus/blur attach is idempotent last-wins, same as click.
     */
    private fun handleAttachEvent(p: RenderPatch.AttachEvent) {
        val view = nodes[p.nodeId] ?: run {
            Log.w(TAG, "AttachEvent '${p.eventName}' for unknown nodeId ${p.nodeId}: ignored")
            return
        }
        when (p.eventName) {
            "click" -> view.setOnClickListener { onUiEvent(p.handlerId, "click", null) }
            "change" -> {
                if (view !is EditText) {
                    Log.w(TAG, "AttachEvent 'change' ignored: node ${p.nodeId} is ${view::class.simpleName}, not EditText")
                    return
                }
                // Phase 4.2 stale-watcher fix: a re-attach (same node, new
                // handlerId, no preceding detach) must REPLACE the watcher,
                // not stack a second one — remove the node's prior watcher
                // from the EditText before adding the new one.
                watchers.remove(p.nodeId)?.let { (priorView, priorWatcher) ->
                    priorView.removeTextChangedListener(priorWatcher)
                }
                val watcher = object : TextWatcher {
                    override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
                    override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {}
                    override fun afterTextChanged(s: Editable?) {
                        // Re-entrancy guard: programmatic setText during patch
                        // application must not dispatch (see [applyingBatch]).
                        if (applyingBatch) return
                        // s is nullable — a null Editable must not stringify
                        // into the literal "null" payload.
                        onUiEvent(p.handlerId, "change", s?.toString() ?: "")
                    }
                }
                view.addTextChangedListener(watcher)
                watchers[p.nodeId] = view to watcher
            }
            "focus", "blur" -> {
                // Single-listener semantics (see the method KDoc): both event
                // names share one FocusEntry and one OnFocusChangeListener per
                // view. The listener resolves the handlerId from the LIVE map
                // entry at fire time, so a side that was never attached (or
                // was detached) simply doesn't dispatch.
                val entry = focusEntries.getOrPut(p.nodeId) { FocusEntry(view) }
                if (p.eventName == "focus") entry.focusHandlerId = p.handlerId
                else entry.blurHandlerId = p.handlerId
                view.setOnFocusChangeListener { _, hasFocus ->
                    // Re-entrancy guard, mirroring the change TextWatcher: a
                    // focus shift caused by patch application itself (e.g. a
                    // focused view removed mid-batch) must not dispatch.
                    if (applyingBatch) return@setOnFocusChangeListener
                    val live = focusEntries[p.nodeId] ?: return@setOnFocusChangeListener
                    val handlerId = if (hasFocus) live.focusHandlerId else live.blurHandlerId
                    if (handlerId != null) {
                        onUiEvent(handlerId, if (hasFocus) "focus" else "blur", null)
                    }
                }
            }
            "scroll" -> {
                // Phase 7.2 — the onScroll wire's Android half (the wire
                // contract is NORMATIVE; iOS mirrors the CONFLATION, not this
                // listener). Only a viewport can scroll:
                if (view !is ScrollView) {
                    Log.w(TAG, "AttachEvent 'scroll' ignored: node ${p.nodeId} is " +
                        "${view::class.simpleName}, not ScrollView")
                    return
                }
                // Last-wins re-attach, the 4.2 watcher discipline: swap the
                // handler on the LIVE wire (keeping its pending offset and
                // in-flight flag — they describe the NODE, not the handler)
                // instead of stacking a second slot.
                scrollWires.getOrPut(p.nodeId) { ScrollWire(p.handlerId) }.handlerId = p.handlerId
                // View.setOnScrollChangeListener is a SINGLE slot (last-wins,
                // like setOnClickListener) and fires on the main thread — for
                // finger drags, flings, AND programmatic scrollTo/re-clamps
                // (px, converted below at the ONE source site). The listener
                // resolves the wire from the LIVE map at fire time, so a
                // detached node's late sample no-ops (the 6.3 stale-callback
                // discipline).
                view.setOnScrollChangeListener { _, _, scrollY, _, _ ->
                    onScrollSample(p.nodeId, scrollY)
                }
            }
            else -> Log.w(TAG, "AttachEvent '${p.eventName}' not supported (forward compat): skipped")
        }
    }

    /** Phase 3.3 Task 9: dispatch on the patch's [RenderPatch.DetachEvent.eventName]
     * (carried on the wire since Task 8), retiring the 3.2 map-membership
     * inference ("handlerId in [watchers] ⇒ change") — the eventName mirrors
     * [handleAttachEvent]'s switch, so a future event type extends both arms
     * symmetrically instead of overloading the fallthrough. handlerId is the
     * ORIGINAL attach's id (renderer registry contract, RenderFrame.kt). */
    private fun handleDetachEvent(p: RenderPatch.DetachEvent) {
        when (p.eventName) {
            "click" -> {
                val view = nodes[p.nodeId] ?: run {
                    Log.w(TAG, "DetachEvent 'click' for unknown nodeId ${p.nodeId}: ignored")
                    return
                }
                view.setOnClickListener(null)
                // setOnClickListener(non-null) flips the view clickable; detaching
                // must restore the pre-listener state or the view keeps consuming
                // taps as a focusable/clickable no-op.
                view.isClickable = false
            }
            "change" -> {
                // Phase 4.2: keyed by nodeId (the stale-watcher fix) — a
                // genuine detach removes whatever watcher is live on the node.
                val removed = watchers.remove(p.nodeId)?.also { (editText, watcher) ->
                    editText.removeTextChangedListener(watcher)
                }
                if (removed == null) {
                    Log.w(TAG, "DetachEvent 'change' for node ${p.nodeId} has no live watcher: ignored")
                }
            }
            "focus", "blur" -> {
                // Symmetric to the attach arm's single-listener semantics:
                // clear ONE side of the pair; drop the entry AND the view's
                // focus listener only when both sides are gone.
                val entry = focusEntries[p.nodeId] ?: run {
                    Log.w(TAG, "DetachEvent '${p.eventName}' for node ${p.nodeId} has no live focus entry: ignored")
                    return
                }
                if (p.eventName == "focus") entry.focusHandlerId = null
                else entry.blurHandlerId = null
                if (entry.isEmpty) {
                    entry.view.onFocusChangeListener = null
                    focusEntries.remove(p.nodeId)
                }
            }
            "scroll" -> {
                // Phase 7.2 — the 6.3 stale-callback discipline, for scroll: the
                // wire dies HERE, and its pending offset dies WITH it, never
                // dispatched (the contract's detach row). An in-flight dispatch
                // already on the lane is beyond recall — its completion resets a
                // flag on this now-unreachable wire and finds no map entry to
                // dispatch from; a stale handlerId is absorbed downstream (the
                // rc-0 at-most-once contract, same as click).
                val removed = scrollWires.remove(p.nodeId)
                if (removed == null) {
                    Log.w(TAG, "DetachEvent 'scroll' for node ${p.nodeId} has no live wire: ignored")
                    return
                }
                (nodes[p.nodeId] as? ScrollView)?.setOnScrollChangeListener(null)
            }
            else -> Log.w(TAG, "DetachEvent '${p.eventName}' not supported (forward compat): skipped")
        }
    }

    // ── Phase 7.2: THE onScroll WIRE (the conflation — NORMATIVE, iOS mirrors it) ──
    //
    // The contract (docs/plans/2026-07-15-phase-7.2-design.md §"The wire contract"):
    //
    //   sample (px, main thread)                    ← ScrollView.setOnScrollChangeListener
    //     → dp = px / density                       ← the ONE conversion site (6.1 rule)
    //     → REPLACES the node's pending offset      ← never queue: scroll position is
    //                                                 idempotent STATE, not an event log
    //     → dispatch IF the lane is free            ← at most ONE in flight per node;
    //       (not in flight, not mid-batch)            payload = the offset as an
    //                                                 invariant float string, exactly what
    //                                                 NativeRenderer.ParseScrollOffset parses
    //     → completion → flush the freshest         ← a slow consumer sees FEWER, FRESHER
    //                                                 events — the backlog is impossible
    //                                                 by construction
    //
    // ORDERING IS FREE: the dispatch rides BlazorNativeRuntime's single FIFO lane —
    // the same queue tail as every tap and change event — so a conflated scroll
    // dispatch can never overtake a user-input event that was queued before it.
    // (iOS note: this property must be PRESERVED, not assumed — whatever path Gate 3
    // dispatches on must keep scroll behind already-queued input for the same node.)
    //
    // ANDROID-SPECIFIC, iOS MUST NOT COPY: the listener API (UIScrollView observes
    // contentOffset via its delegate), the px→dp division (points ARE pt), and the
    // mid-batch re-clamp echo (ScrollView.onLayout's scrollTo — the 6.2 mechanism;
    // UIScrollView does no such re-clamp, iOS has its own explicit shrink clamp).

    /**
     * A native scroll sample landed (main thread — the listener fires there).
     * Converts AT THE SOURCE (px → dp, the 6.1 one-conversion-site rule: Yoga
     * dp times density is what [YogaLayout.applyFrame] painted, so px divided
     * by the same density is the exact inverse) and conflates: the slot holds
     * ONE offset, and this sample REPLACES whatever was there.
     */
    private fun onScrollSample(nodeId: Int, scrollYpx: Int) {
        val wire = scrollWires[nodeId] ?: return // detached/purged: stale sample, no-op
        scrollSamplesSeen++
        wire.pendingOffsetDp = scrollYpx / context.resources.displayMetrics.density
        maybeDispatchScroll(nodeId)
    }

    /**
     * Dispatches the node's pending offset IF the lane is available — not
     * mid-batch, and no dispatch of this node's already in flight. Called from
     * three places, which are exactly the three lane-availability edges: a new
     * sample ([onScrollSample]), a completion (below), and the end of a patch
     * batch ([flushScrollWires]).
     *
     * The completion marshals to the main handler (all conflation state is
     * main-thread-only, like every other map in this class) and re-consults
     * the LIVE map: the wire it captured may have been detached/purged, or the
     * nodeId may already belong to a NEW node (ids restart — the 6.2/6.3
     * lesson). Resetting the captured wire's flag and then looking the nodeId
     * up fresh is what makes both cases harmless without a generation counter:
     * a dead wire is unreachable from the map, and a new node's wire has its
     * own independent flag.
     */
    private fun maybeDispatchScroll(nodeId: Int) {
        if (applyingBatch) return // conflate; applyBatch's tail flushes
        val wire = scrollWires[nodeId] ?: return
        if (wire.inFlight) return // conflate; the completion flushes
        val offsetDp = wire.pendingOffsetDp ?: return
        wire.pendingOffsetDp = null
        wire.inFlight = true
        scrollDispatchesSent++
        lastScrollDispatchDp = offsetDp
        // The payload is the offset as an INVARIANT float string — mirroring
        // NativeRenderer.ParseScrollOffset (NumberStyles.Float, invariant
        // culture) exactly: Float.toString never localizes ("1,5" from a Dutch
        // device would be a loud rc-2 fault, by design).
        onScrollEvent(wire.handlerId, offsetDp.toString()) {
            mainHandler.post {
                wire.inFlight = false
                maybeDispatchScroll(nodeId)
            }
        }
    }

    /** The batch-end / layout-pass lane-availability: give every wire that
     * conflated during the guard its dispatch chance. Snapshot the keys — a
     * dispatcher completing synchronously (the default test dispatcher) can
     * re-enter the map. */
    private fun flushScrollWires() {
        if (scrollWires.isEmpty()) return
        for (nodeId in scrollWires.keys.toList()) maybeDispatchScroll(nodeId)
    }

    private fun handleCreate(p: RenderPatch.CreateNode) {
        // Phase 2.8 Task 3b — text-child-of-TextView collapse: when a CreateNode
        // for a text frame lands with a parent that's a TextView-but-not-ViewGroup
        // (Button, EditText, plain TextView, etc.), don't allocate a separate
        // View; instead map this nodeId to the parent itself so the subsequent
        // ReplaceText on this nodeId routes through the parent's setText.
        // Matches React Native's text-content collapse pattern.
        //
        // Without this, the renderer's child text frames orphan to widget_root
        // because `as? ViewGroup` returns null for Button/EditText/etc.
        //
        // Phase 6.1: a collapsed text node gets no view AND THEREFORE NO YOGA
        // NODE. The Yoga tree mirrors the VIEW tree, not the patch tree — give
        // this node one and the two trees' child indices skew from here on, and
        // every frame after it is wrong.
        if (p.nodeType == "text") {
            val rawParent = p.parentId?.let { nodes[it] }
            if (rawParent is TextView && rawParent !is android.view.ViewGroup) {
                nodes[p.nodeId] = rawParent
                collapsedAliases.add(p.nodeId) // it ALIASES the parent; it owns nothing
                return  // no separate view; subsequent ReplaceText sets parent's text
            }
        }

        val view: View = when (p.nodeType) {
            // Phase 6.1: a plain FrameLayout — children are absolutely placed by
            // Yoga, nothing is stacked. (An un-styled tree still LOOKS stacked:
            // Yoga's default flexDirection is column.)
            "view"   -> BnYogaFrameLayout(context)
            "text"   -> TextView(context)
            "button" -> Button(context)
            "input"  -> EditText(context)
            // Phase 6.3 — **THE CONTENT MODE IS SET EXPLICITLY, AND IT IS A TWO-SHELL
            // CONTRACT** (Gate 2 review, F4; design §"The parity contract").
            //
            // Nothing here used to set it, so each shell took its FRAMEWORK default — and
            // the two defaults DISAGREE: Android's `ImageView` is `FIT_CENTER`
            // (aspect-preserving) and `UIImageView`'s is `.scaleToFill` (STRETCH). For
            // `/image`'s case [0] — a 64 × 48 fixture inside a declared 200 × 120 frame —
            // Android letterboxes and iOS would distort. It is FRAME-NEUTRAL, so the parity
            // contract's numbers survive it and no test on either side could see it; it is
            // "renders identically" that breaks, silently, on one platform. 6.1 set the
            // precedent (`clipChildren = false`: "it costs one line to align the two
            // shells"), and deferring the `ContentMode` *API* (design decision 3) does not
            // defer the *default*.
            //
            // ASPECT-FIT is the one picked, and the reason is that it cannot LIE: a stretched
            // image misrepresents its own pixels, and this phase's whole subject is an image
            // reporting its true size. It is also free on the intrinsic path (there the frame
            // IS the natural size, so fit and fill are pixel-identical — the choice only bites
            // on a DECLARED frame of a different aspect), it is already Android's default, and
            // it is the value an M7 `ContentMode` would default to. Gate 3 owes
            // `contentMode = .scaleAspectFit` — one line, and it is normative.
            "image"  -> ImageView(context).apply { scaleType = ImageView.ScaleType.FIT_CENTER }
            // Phase 6.2: a VIEWPORT — vertical (Android's ScrollView is vertical-only;
            // horizontal is a different widget class and is ledgered). Its single
            // child, the synthetic content view, is created just below.
            SCROLL   -> ScrollView(context)
            "picker" -> Spinner(context)
            else     -> {
                Log.w(TAG, "Unknown nodeType ${p.nodeType} — falling back to TextView")
                TextView(context)
            }
        }
        nodes[p.nodeId] = view

        // Phase 6.2 — THE SYNTHETIC CONTENT VIEW. A BnYogaFrameLayout, not a stock
        // one, for the 6.1 reason (a stock FrameLayout's onLayout would re-place every
        // row by gravity on the framework's next pass) AND for a new one: ScrollView
        // measures its single child with an UNSPECIFIED height spec, and
        // BnYogaFrameLayout.onMeasure answers with the last size Yoga applied — which
        // is what keeps ScrollView's per-layout offset re-clamp from snapping a
        // scrolled page back to the top (see BnYogaFrameLayout.onMeasure's KDoc; the
        // mechanism is ANDROID-SPECIFIC and iOS must NOT go looking for it).
        //
        // Keyed on the NODETYPE, not on `view is ScrollView`: the nodeType is the
        // CONTRACT ("a `scroll` node is a viewport"), and the widget class is a row in
        // a table that could change (a horizontal `scroll` would be a
        // HorizontalScrollView and would still owe a content node). Two ways of asking
        // one question is how the two trees end up disagreeing about which nodes are
        // scroll nodes.
        if (p.nodeType == SCROLL) {
            val scroll = view as ScrollView
            // ── isFillViewport: EXPLICIT, and its value is LOAD-BEARING ────────────
            // `false` IS the framework default — and it is the ONLY reason ScrollView
            // does not re-measure the content child with EXACTLY(viewportHeight) when
            // the content is SHORTER than the viewport. Set it to `true` (an entirely
            // ordinary thing to reach for — "make my content fill the empty space")
            // and Android stretches the content behind Yoga's back: the 6.1
            // FrameLayout lesson in a new costume. Worse, that EXACTLY spec is written
            // back into BnYogaFrameLayout.yogaHeight, so the framework's stretched
            // number then answers every later UNSPECIFIED measure AS IF Yoga had
            // computed it. iOS has NO equivalent knob, so the divergence would be
            // Android-only and invisible in a diff. Written out so the default is a
            // DECISION rather than an accident.
            scroll.isFillViewport = false
            // ── clipChildren: the viewport KEEPS the framework default (true) ──────
            // Every BnYogaFrameLayout turns clipping OFF to match iOS's
            // `UIView.clipsToBounds == NO`. A ScrollView is NOT one of those and must
            // NOT be made one: a viewport that does not clip DRAWS ITS 800dp OF
            // CONTENT OVER THE WHOLE SCREEN. `true` here is what matches
            // `UIScrollView.clipsToBounds == YES` — so this is the one container in
            // the shell where "our containers don't clip" is the WRONG rule to mirror.
            val content = BnYogaFrameLayout(context)
            scroll.addView(content)
            scrollContents[p.nodeId] = content
        }

        // The container this node's view parents INTO: for a child of a scroll node
        // that is the CONTENT view, never the ScrollView (non-negotiable #2).
        val parent = containerFor(p.parentId) ?: root
        // Phase 3.3 Task 9 (DoD #10): honor the renderer-computed placement.
        // insertIndex counts HOST views in the target container — and a
        // WidgetMapper ViewGroup's children ARE exactly those host views 1:1
        // (the only nodes that never materialize a child view are collapsed
        // text nodes, and those alias onto non-ViewGroup parents — see the
        // collapse block above — so they can't skew a container's indices).
        // No translation needed; mirrors the JVM twin's arithmetic
        // (CompositionProbeTest: "the list container's children are
        // EXCLUSIVELY the keyed ItemComponent views"). −1 = append,
        // explicitly encoded (0 is a valid front index). An out-of-range
        // index throws on the main thread — inherently strict placement.
        if (p.insertIndex >= 0) parent.addView(view, p.insertIndex)
        else parent.addView(view)

        // Phase 6.1: the Yoga twin, inserted at the SAME index in the SAME
        // parent. `parent === root` (an unknown / non-ViewGroup parentId fell
        // back to the host root) must fall back to the Yoga host root too, or the
        // trees diverge — hence the parentId is re-derived from the view we
        // actually parented to, not from the patch.
        val yogaParentId = if (parent === root) null else p.parentId
        yoga.createNode(
            p.nodeId, p.nodeType, view, yogaParentId, p.insertIndex,
            // …and the Yoga tree gets its synthetic node in the same breath as the view
            // tree got its synthetic view. BOTH TREES OR NEITHER.
            contentView = scrollContents[p.nodeId],
        )
    }

    /**
     * The ViewGroup a child of [parentId] parents into — **the second index-mapping
     * rule** (non-negotiable #2), stated once.
     *
     * A `scroll` node's children go into its CONTENT view, not into the `ScrollView`
     * itself, whose only child *is* the content view. `insertIndex` then counts the
     * content view's children, and `-1` (append) appends to them — exactly mirroring
     * [YogaLayout.createNode], which redirects to the synthetic content NODE by the
     * same rule. The two trees mirror each other; neither mirrors the patch tree.
     *
     * Null when [parentId] is null or names a non-container (an unknown id, or a text
     * node's Button parent) — the caller falls back to the host root, and re-derives
     * the Yoga parent from the view it actually parented to.
     */
    private fun containerFor(parentId: Int?): ViewGroup? {
        if (parentId == null) return null
        scrollContents[parentId]?.let { return it }
        return nodes[parentId] as? ViewGroup
    }

    private fun handleReplaceText(p: RenderPatch.ReplaceText) {
        val view = nodes[p.nodeId] as? TextView ?: return
        view.text = p.text
        // Phase 6.1: new text = new intrinsic size. Yoga caches a measure
        // function's result and will not re-run it unless the node is dirtied —
        // without this the label keeps the frame its OLD text measured to.
        // markDirty resolves by VIEW, which is what makes the collapse work: this
        // nodeId may be an alias for the parent Button.
        yoga.markDirty(view)
    }

    /**
     * ONE RemoveNodePatch arrives for a WHOLE SUBTREE — the renderer does not emit
     * one per node (`NativeRenderer.PurgeNodeSubtree` is .NET-side bookkeeping; the
     * host contract on `ProcessDisposedComponent` spells it out). So the host must
     * purge the subtree itself, in BOTH trees: here (views/watchers/focus entries/
     * aliases) and in [YogaLayout.removeNode] (Yoga nodes + their measure funcs).
     * Purging only the named node leaks every descendant — each entry pinning a
     * View, hence the Activity Context, hence a native Yoga peer that can never be
     * reclaimed — once per navigation, forever.
     *
     * The subtree is read off the VIEW hierarchy (the mapper's own tree) and matched
     * by IDENTITY, never by key: the text collapse aliases nodeIds onto a view they
     * do not own, so a map entry may sit under a different id than the one removed.
     */
    private fun handleRemove(p: RenderPatch.RemoveNode) {
        // An ALIAS (collapsed text node) owns NO view and NO Yoga node: drop the
        // map entry and stop. Removing its view would detach the parent BUTTON, and
        // the Yoga node it would leave behind is the Button's — Yoga would keep
        // reserving space for a widget no longer on screen.
        if (collapsedAliases.remove(p.nodeId)) {
            nodes.remove(p.nodeId)
            return
        }
        val v = nodes[p.nodeId] ?: return
        val doomed = subtreeOf(v)
        val removedIds = nodes.entries.filter { it.value in doomed }.map { it.key }
        for (id in removedIds) {
            nodes.remove(id)
            collapsedAliases.remove(id) // an aliased text child of a doomed Button
            // Phase 6.3 — **CANCEL, and it is MEMORY SAFETY rather than hygiene**
            // (non-negotiable #4). A completion that fires after this patch would paint
            // into a detached ImageView here and would touch a FREED YGNodeRef on iOS —
            // 6.2's dangling-pointer lesson in a new costume. It rides the SUBTREE purge
            // because that is the shape the renderer actually emits: navigating away from
            // /image or /scroll names the PAGE, never the image inside it.
            cancelImageRequest(id)
            imageGenerations.remove(id)
            // Phase 7.2 — the purge half of the stale-callback discipline: a
            // removed scroll node's conflation slot dies here, pending offset
            // and all, NEVER dispatched (the wire contract's detach/purge row).
            // Rides the SUBTREE purge for the 6.3 reason: navigation names the
            // page, never the scroll inside it.
            scrollWires.remove(id)
        }
        // The detached EditText would otherwise pin itself (and its watcher/focus
        // pair) in these maps forever.
        watchers.entries.removeAll { it.value.first in doomed }
        focusEntries.entries.removeAll { it.value.view in doomed }
        // Phase 6.2: a doomed ScrollView takes its SYNTHETIC content view with it —
        // the view is in `doomed` (it is a child of the ScrollView, and subtreeOf walks
        // the view hierarchy), so this entry is the only thing that would survive, and
        // it would pin the content view, the rows under it and the Activity Context.
        // The synthetic node is part of the subtree ONE RemoveNodePatch stands for.
        scrollContents.entries.removeAll { it.value in doomed }
        (v.parent as? ViewGroup)?.removeView(v)
        // Phase 6.1: and the Yoga twin, or the detached view keeps consuming space
        // in its parent's layout. Purges ITS subtree too, for the same reason.
        yoga.removeNode(p.nodeId)
    }

    /** [root] and every descendant view. A `HashSet<View>` IS an identity set —
     * `View` does not override `equals`. */
    private fun subtreeOf(root: View): Set<View> {
        val out = mutableSetOf<View>()
        fun walk(v: View) {
            out.add(v)
            if (v is ViewGroup) for (i in 0 until v.childCount) walk(v.getChildAt(i))
        }
        walk(root)
        return out
    }

    /**
     * Activity teardown (`MainActivity.onDestroy`): drop every map entry and the
     * whole Yoga tree. Without it the mapper — reachable from the runtime's frame
     * callback, which outlives the Activity (the native session is process-global)
     * — keeps a dead Activity's entire view hierarchy and its native Yoga peers
     * alive across every recreation.
     */
    fun destroy() {
        nodes.clear()
        collapsedAliases.clear()
        scrollContents.clear()
        watchers.clear()
        focusEntries.clear()
        // Phase 7.2: pending scroll offsets die with the Activity — a dispatch
        // after teardown would enter a retired lane for a dead view hierarchy.
        scrollWires.clear()
        // Phase 6.3: every in-flight fetch dies with the Activity. A completion landing on
        // a destroyed mapper would paint into a dead view hierarchy and re-solve a torn-down
        // Yoga tree.
        for (request in imageRequests.values) request.disposable.dispose()
        imageRequests.clear()
        imageGenerations.clear()
        imageResultLog.clear()
        yoga.destroy()
    }

    /** Test-only: the live node count, the pin for the subtree purge above
     * (`YogaNodeLifecycleAndroidTest` asserts it returns to baseline). */
    internal val nodeCount: Int get() = nodes.size

    /** Test-only: the Yoga tree's live node count — must track the view tree's. */
    internal val yogaNodeCount: Int get() = yoga.nodeCount

    /** Test-only: the Yoga tree's live view mappings — must track [yogaNodeCount]
     * plus [yogaContentNodeCount] (a synthetic content node places a view too). */
    internal val yogaViewCount: Int get() = yoga.viewCount

    /** Test-only: the live SYNTHETIC content nodes (Phase 6.2) — no patch ever names
     * one, so only this can witness that removing a scroll node freed it. */
    internal val yogaContentNodeCount: Int get() = yoga.contentNodeCount

    /** Test-only: the live SYNTHETIC content VIEWS — the view-tree half of the same. */
    internal val scrollContentCount: Int get() = scrollContents.size

    /** The scroll node's two runtime diagnostics (Phase 6.2): the container-style
     * ignore-and-log rule, and the definite-height warning. Exposed because logcat
     * is not an assertion surface and both failures are SILENT on the device — a
     * page that simply does not move. `WidgetMapperScrollTest` asserts them. */
    internal val scrollDiagnostics: List<String> get() = yoga.diagnostics

    /** Test-only (Phase 6.3) — **the synchronization gate's observation surface**: the last
     * [MAX_IMAGE_RESULTS] image requests that TERMINATED, with Coil's own verdict. See
     * [imageResultLog]; the AFTER frames of `/image` may only be asserted once this holds all
     * three. */
    internal val imageResults: List<ImageResult> get() = imageResultLog.toList()

    /** Test-only (Phase 6.3, Gate 2 review I3): how many requests have terminated ALTOGETHER —
     * which [imageResults] deliberately cannot say, because it is a bounded ring. A test that
     * wants to overflow that ring has to be able to wait for the overflow. */
    internal var imageTerminalCount: Int = 0
        private set

    /** Test-only: the cap on [imageResultLog], so the bound is asserted against the shell's own
     * number rather than a constant a test invented. */
    internal val imageResultCap: Int get() = MAX_IMAGE_RESULTS

    /** Test-only (Phase 6.3): the requests still in flight. Returns to 0 after every
     * completion, cancellation and purge — a non-zero count after a removal is the leak
     * that, on iOS, is a dangling `YGNodeRef`. */
    internal val inFlightImageCount: Int get() = imageRequests.size

    /**
     * Test-only (Phase 6.3 Gate 3 review, C1): a node's CURRENT generation (`null` = it has
     * none — it was purged, or never carried a `src`). The twin of Swift's
     * `BnWidgetMapper.imageGeneration(of:)`.
     *
     * It is exposed for ONE reason: **the rule it pins is invisible in every frame.** *Every*
     * `src` write bumps the generation, **including a CLEAR** — because a clear cancels, a
     * cancel races its own callback, and the generation is the only thing that stops the loser
     * painting ([handleSrc]). A shell that bumped only on a real URL is GREEN on every frame
     * table in this repo: the dispose wins that race in every ordering a device test can
     * produce (the main looper is FIFO, so a callback already posted runs BEFORE any batch a
     * test posts after it — a device test can only ever stage the clear WINNING). The bump is
     * therefore asserted as the number it is, and [isLiveImageRequest]'s superseded-generation
     * row is what that number then BUYS.
     */
    internal fun imageGeneration(nodeId: Int): Int? = imageGenerations[nodeId]

    private fun handleUpdateProp(p: RenderPatch.UpdateProp) {
        val view = nodes[p.nodeId] ?: run {
            Log.w(TAG, "UpdateProp for unknown nodeId ${p.nodeId}: ignored")
            return
        }
        when (p.name) {
            "placeholder" -> {
                if (view is EditText) {
                    view.hint = p.value
                    yoga.markDirty(view) // the hint sizes an empty EditText
                } else Log.w(TAG, "UpdateProp placeholder ignored: $view is not EditText")
            }
            // Phase 3.4 (DoD #5): the bound input's write-back half of the bind
            // loop. Runs inside [applyBatch], so the [applyingBatch] guard
            // already suppresses the TextWatcher's dispatch — a value echo can
            // never re-enter the change → re-render → setText loop. The
            // inequality check skips redundant setText when the echo merely
            // confirms what the user just typed (the common bind case), which
            // also preserves the user's cursor position mid-edit. When the
            // runtime DOES push a different value (e.g. Clear), the write wins
            // over whatever the IME holds (last-write-wins: Blazor state is
            // the source of truth) and the cursor moves to the end —
            // setText resets the selection to 0, so setSelection(length) is
            // the least-surprising placement for a programmatic overwrite.
            "value" -> {
                if (view is EditText) {
                    if (view.text.toString() != (p.value ?: "")) {
                        view.setText(p.value ?: "")
                        view.setSelection(view.text.length)
                        yoga.markDirty(view) // new content = new intrinsic size
                    }
                } else Log.w(TAG, "UpdateProp value ignored: $view is not EditText")
            }
            "enabled" -> {
                view.isEnabled = p.value?.toBoolean() ?: true
            }
            // Phase 6.3 (M6 DoD #5): the LAST stubbed leaf stops being one. `src` is a
            // PROP, not a style — a URL is neither layout nor paint, so it rides this wire
            // and not the partitioned SetStyle routing table (BnImage.cs's header).
            "src" -> {
                if (view is ImageView) handleSrc(p.nodeId, view, p.value)
                else Log.w(TAG, "UpdateProp src ignored: node ${p.nodeId} is " +
                    "${view::class.simpleName}, not ImageView")
            }
            else -> Log.w(TAG, "UpdateProp '${p.name}' not yet supported (Phase 3+ extends)")
        }
    }

    // ── Phase 6.3: IMAGES ────────────────────────────────────────────────────────
    //
    // The model (design §"The model") — and there is no binary path on the wire, by
    // design: .NET names the source, THE SHELL FETCHES THE BYTES (React Native's model).
    //
    //   UpdateProp(nodeId, "src", url)
    //         → cancel any in-flight request for this node
    //         → clear the bytes it already holds  ← back to 0 × 0 until the new ones land
    //         → Coil fetches + decodes (off the main thread)
    //         → on the MAIN thread: set the drawable
    //                               record the NATURAL size (YogaLayout.setImageNaturalSize)
    //                               markDirty            ← the 6.1 path
    //                               re-solve + apply     ← the 6.2 path
    //
    // ONE reflow, never two. That is why there is no placeholder (design decision 3): a
    // placeholder that MEASURED would reflow the page twice.

    /**
     * `src` arrived — with a URL, or with **null**.
     *
     * Both are the same code path, and that is the point: the renderer emits
     * `UpdateProp(nodeId, "src", null)` when an author sets `Src` back to null (a
     * `RemoveAttribute` on a non-style name — `BnButton.Enabled`'s precedent), and the
     * contract for it is *cancel, CLEAR, markDirty, re-solve* — an intrinsic node collapses
     * back to 0 × 0 and **its siblings move back UP**. Which is exactly the first half of
     * what a `src` CHANGE owes as well ("back to 0 × 0 until the new bytes land"). Two rows
     * of the parity contract, one path; a shell that split them is a shell where one of them
     * rots.
     *
     * No re-solve here: this runs inside [applyBatch], whose `CommitFrame` re-solves the
     * whole tree at the end of the batch. Only the ASYNCHRONOUS completion — which arrives
     * with no patch behind it — has to trigger its own ([resolveLayout]).
     */
    private fun handleSrc(nodeId: Int, view: ImageView, url: String?) {
        // ── THE GENERATION IS BUMPED FIRST, AND IT IS BUMPED BY *EVERY* `src` WRITE —
        //    INCLUDING A CLEAR. It sits above the early returns on purpose ──────────────
        //
        // **A clear cancels; a cancel RACES ITS OWN COMPLETION; and the generation is the only
        // thing that stops the loser painting.** [cancelImageRequest] is best-effort *by
        // definition* — that is the whole reason this counter exists. When `Src` goes to null
        // (or `""`) while a request is in flight **whose work has already finished and whose
        // callback is already on its way to the main thread**, the `dispose()` below arrives
        // too late: that callback reaches [onImageLoaded] with generation *N*. If the clear had
        // not bumped, it would find `imageGenerations[nodeId]` still *N* and the very same view
        // — [isLiveImageRequest] would say **LIVE** — and it would paint the stale bytes,
        // record their natural size, `markDirty` and re-solve. **The node the author just
        // cleared would RE-INFLATE, and its sibling would move back down** — defeating the
        // contract's `Src` → `null` row ("cancel, CLEAR, collapse to 0 × 0, siblings move UP")
        // *and* "one reflow, never two", on this phase's own home ground.
        //
        // Pinned by `WidgetMapperImageTest.every_src_write_bumps_the_generation_INCLUDING_a_clear`
        // (the bump itself, as the number it is — no frame can see it) composed with
        // `ImageRequestGuardTest.a_superseded_generation_is_not_live` (what the bump then BUYS).
        val generation = (imageGenerations[nodeId] ?: 0) + 1
        imageGenerations[nodeId] = generation

        cancelImageRequest(nodeId)
        // The bytes the node already holds go NOW, not when (or if) the new ones arrive:
        // "on a Src change the node measures 0 × 0 again until the new bytes land".
        view.setImageDrawable(null)
        yoga.setImageNaturalSize(view, null)
        yoga.markDirty(view)

        // An EMPTY string is the null/clear contract, not a fetch of "" (which would be an
        // immediate, pointless ERROR on Android and — on iOS — a `URL(string:)` that returns
        // nil, i.e. an NPE-shaped crash if the shell force-unwraps it). It is a SHELL
        // decision, so it is written into the shared contract rather than left for the two
        // shells to make differently (design §"The parity contract", the `Src` → `null` row).
        if (url.isNullOrEmpty()) return

        // …and the generation the enqueue below is issued under is the one taken above — which
        // is also load-bearing in the OTHER direction: a Coil memory-cache hit completes
        // SYNCHRONOUSLY (see below), inside `enqueue`, and its completion asks
        // [isLiveImageRequest] for this very number.

        val request = ImageRequest.Builder(context)
            .data(url)
            // ── NO DOWNSAMPLING THAT CHANGES THE REPORTED SIZE ──────────────────────
            // The contract's last row, and the one a library gets to break for free.
            // Coil's default sizes a request to its TARGET; with no target it would use
            // the display. Size.ORIGINAL is what makes the decoded bitmap the FILE's own
            // pixels — so the size we report to Yoga is the image's NATURAL size and not
            // a decoder's chosen sample size. The tests assert the measured frame against
            // the decoded fixture's own pixel count, so a sampled decode reddens them.
            .size(Size.ORIGINAL)
            .listener(
                onSuccess = { _, result ->
                    onImageLoaded(nodeId, generation, view, url, result.drawable)
                },
                onError = { _, result -> onImageFailed(nodeId, generation, view, url, result.throwable) },
                onCancel = { _ -> onImageCancelled(nodeId, generation, view, url) },
            )
            .build()

        // ── A MEMORY-CACHE HIT COMPLETES *INSIDE* THIS CALL (Gate 2 review, C2) ──────────
        // Coil 2 dispatches on `Dispatchers.Main.immediate`, and [handleSrc] runs on the main
        // thread (inside [applyBatch]). So an `enqueue` that hits the memory cache runs the
        // whole request — including `onSuccess`, including [resolveLayout] — TO COMPLETION
        // BEFORE IT RETURNS. That is the ordinary case on the SECOND mount of any page whose
        // images the process has already fetched (Coil's cache is process-wide), and it means
        // the disposable this line receives can already be DISPOSED.
        //
        // Recording it unconditionally is a permanent leak: the entry is never removed (the
        // completion's [clearIfMine] ran before it existed), [inFlightImageCount] never
        // returns to 0 — an invariant three tests assert — and [handleRemove] would later
        // "cancel" a request that finished long ago. So: record only what is STILL LIVE.
        //
        // **Kingfisher's `setImage` calls its completionHandler synchronously on a memory
        // hit too**, so Gate 3 inherits this verbatim; the design says so, and
        // `the_second_mount_with_a_WARM_cache_completes_inside_applyBatch` is the test.
        val disposable = context.imageLoader.enqueue(request)
        if (!disposable.isDisposed) imageRequests[nodeId] = InFlight(generation, view, disposable)
    }

    /**
     * The bytes landed. On the MAIN thread (Coil dispatches its listener there), which is
     * what makes the three calls below safe to make back-to-back.
     */
    private fun onImageLoaded(
        nodeId: Int,
        generation: Int,
        view: ImageView,
        url: String,
        drawable: Drawable,
    ) {
        recordImageResult(ImageResult(nodeId, url, ImageOutcome.SUCCESS))
        if (!isLive(nodeId, generation, view, url, "completion")) return
        clearIfMine(nodeId, generation, view)

        view.setImageDrawable(drawable)
        // The NATURAL size (pixels, read as dp — YogaLayout.setImageNaturalSize states the
        // rule and why it is the only reading that agrees with iOS). Taken from the decoded
        // BITMAP where there is one: `Bitmap.width` is the raw pixel count and is immune to
        // the density metadata a `BitmapDrawable`'s intrinsicWidth is scaled by.
        yoga.setImageNaturalSize(view, naturalSizeOf(nodeId, url, drawable))
        // …and the 6.1 path, WITHOUT WHICH THE IMAGE PAINTS AND THE PAGE NEVER MOVES: Yoga
        // caches a measure function's result and will not re-run it on a clean node.
        yoga.markDirty(view)
        // …and the 6.2 path. No patch is behind this frame — the wire carries no completion
        // signal — so the re-solve is the shell's to trigger.
        resolveLayout()
    }

    /**
     * The load failed — a 404, a refused connection, a blocked cleartext fetch. The node
     * **keeps measuring 0 × 0** (it was cleared when the request was issued), it **reserves
     * nothing**, and it **does not retry**. There is nothing to markDirty and nothing to
     * re-solve: no frame changed, which is the whole content of the contract's failure row.
     */
    private fun onImageFailed(
        nodeId: Int,
        generation: Int,
        view: ImageView,
        url: String,
        error: Throwable,
    ) {
        recordImageResult(ImageResult(nodeId, url, ImageOutcome.ERROR))
        clearIfMine(nodeId, generation, view)
        Log.w(TAG, "image load failed for node $nodeId ($url): ${error.javaClass.simpleName}: " +
            "${error.message} — the node stays 0 × 0 and reserves nothing")
    }

    /** We cancelled it: a `Src` change, a node removal, or teardown. Nothing is painted and
     * nothing is re-solved — that is what "cancelled" means. */
    private fun onImageCancelled(nodeId: Int, generation: Int, view: ImageView, url: String) {
        recordImageResult(ImageResult(nodeId, url, ImageOutcome.CANCELLED))
        clearIfMine(nodeId, generation, view)
    }

    /**
     * **DROP THE IN-FLIGHT ENTRY ONLY IF IT IS THIS CALLBACK'S OWN** (Gate 2 review, C1).
     *
     * Every terminal callback ends here, and every one of them asks BOTH questions — which is
     * the whole of the fix. `onError`/`onCancel` used to evict on a **generation match alone**,
     * and that is exactly the case a generation cannot decide: `/image` → back → `/image`
     * re-uses this mapper, **node ids restart at 1**, and the OLD node 2's `onCancel`
     * (generation 1) arrives as a later main-thread message to find the NEW node 2 also on
     * generation 1. It matched, and it evicted the LIVE request's `Disposable` — leaving a
     * request nothing could cancel any more. On Android [isLive] still stopped the paint, so
     * the symptom was invisible; **on iOS the Kingfisher task is not cancelled and its
     * completion runs against a freed `YGNodeRef`.**
     *
     * Comparing against the ENTRY (its generation, its view) rather than against `nodes`/
     * `imageGenerations` is deliberate: it is a question about the *request*, and it answers
     * correctly even for a node that has since been purged from both maps.
     *
     * **AND THE DECISION IS [isLiveImageRequest]'s — the SAME pure function [isLive] asks, not
     * a second inline copy of the conjunction.** It used to be a copy, and the copy was
     * UNPINNED: `ImageRequestGuardTest` tests the FUNCTION, so dropping `&& entry.view === view`
     * from HERE ALONE left the whole suite green — the mutation had to be applied in two places
     * to redden one test, which is the definition of a second site nothing defends. One
     * decision, one function, one unit test, two call sites. (What differs between the two call
     * sites is only WHERE the "current" facts come from — the ENTRY here, the LIVE maps there —
     * and that distinction is deliberate and is preserved.)
     */
    private fun clearIfMine(nodeId: Int, generation: Int, view: ImageView) {
        val entry = imageRequests[nodeId] ?: return
        if (isLiveImageRequest(entry.generation, generation, entry.view, view)) {
            imageRequests.remove(nodeId)
        }
    }

    /**
     * **THE PURGED-NODE GUARD** — the defence behind the cancel, and it is not belt-and-
     * braces theatre: [cancelImageRequest] is what *prevents* a completion, and this is what
     * makes one HARMLESS if it ever arrives anyway (a completion already in its main-thread
     * continuation when `dispose()` ran).
     *
     * The DECISION is [isLiveImageRequest] — a pure function, in its own file, **unit-tested
     * on the JVM lane** (`ImageRequestGuardTest`), including the reset collision that no
     * single-mount instrumented test can stage. This method is the lookup and the log around
     * it; the reasoning lives with the function.
     */
    private fun isLive(
        nodeId: Int,
        generation: Int,
        view: ImageView,
        url: String,
        what: String,
    ): Boolean {
        if (!isLiveImageRequest(imageGenerations[nodeId], generation, nodes[nodeId], view)) {
            Log.w(TAG, "stale image $what for node $nodeId ($url) dropped: the node was " +
                "removed, or its src was written again. Nothing painted.")
            return false
        }
        return true
    }

    /**
     * An image's natural size in PIXELS — **the decoded bitmap's own**, never a density-scaled
     * `intrinsicWidth` (a `BitmapDrawable` scales that by `targetDensity / bitmap.density`, so
     * it is the wrong number on any device whose density is not 1, and it can never agree with
     * iOS). One file pixel is one dp/pt: the parity contract's UNIT row, stated at length in
     * [YogaLayout.setImageNaturalSize].
     *
     * **A drawable with no bitmap reports NO NATURAL SIZE — 0 × 0 — and says so** (Gate 2
     * review, F3). The fallback used to return `intrinsicWidth`, which is a SECOND, CONTRADICTORY
     * unit rule sitting in the same file as the first: nothing reaches it today (Coil decodes a
     * PNG to a `BitmapDrawable`), and the thing that WOULD reach it is animated GIF / SVG — both
     * ledgered, both arriving in some later phase, and both landing on a number that is wrong by
     * the device's density with no test anywhere to notice. A capability we have not designed
     * measures ZERO and logs; it does not silently get a made-up frame.
     */
    private fun naturalSizeOf(nodeId: Int, url: String, drawable: Drawable): YogaLayout.NaturalSize {
        val bitmap = (drawable as? BitmapDrawable)?.bitmap
        if (bitmap != null) return YogaLayout.NaturalSize(bitmap.width, bitmap.height)
        Log.w(TAG, "image for node $nodeId ($url) decoded to a ${drawable::class.simpleName}, " +
            "not a BitmapDrawable: it has NO natural size this shell knows how to read in the " +
            "contract's unit (one FILE PIXEL is one dp/pt), so it measures 0 × 0 and reserves " +
            "nothing. Animated/vector formats are ledgered — they need a design, not a guess.")
        return YogaLayout.NaturalSize(0, 0)
    }

    /** Cancels [nodeId]'s in-flight request, if any. Idempotent; safe for a node that never
     * had one. */
    private fun cancelImageRequest(nodeId: Int) {
        imageRequests.remove(nodeId)?.disposable?.dispose()
    }

    /**
     * A layout pass triggered by something that is NOT a patch — an image completion.
     *
     * **IT MUST NOT RUN INSIDE A BATCH** (Gate 2 review, C2). A Coil memory-cache hit completes
     * SYNCHRONOUSLY, on the main thread, inside the `enqueue` that [handleSrc] issues from
     * within [applyBatch] — so this method has exactly one RE-ENTRANT caller, and the
     * [applyingBatch] guard is a plain boolean that is not re-entrant: its `finally` would set
     * the flag back to **false FOR THE REST OF THE BATCH**, and every subsequent `setText` /
     * `value` / focus change in that batch would dispatch back into .NET — the change →
     * re-render → setText loop the 3.2/4.2 guard exists to prevent. It would also re-solve Yoga
     * against a HALF-APPLIED tree, and then again at `CommitFrame`: **two reflows, where the
     * contract says ONE.**
     *
     * So inside a batch this is a NO-OP, and it loses nothing: the batch's own `CommitFrame`
     * re-solves the whole tree at the end, which is where the synchronously-set natural size and
     * `markDirty` are picked up. Only a completion that arrives with NO patch behind it — the
     * asynchronous case, the one the wire carries no signal for — has a layout pass to trigger.
     *
     * When it DOES run, it runs inside the guard for the reason `CommitFrame`'s pass does: the
     * pass MEASURES REAL WIDGETS, and a measure that moved focus or text must not dispatch back
     * into .NET.
     */
    private fun resolveLayout() {
        if (applyingBatch) return
        applyingBatch = true
        try {
            yoga.calculateAndApply()
        } finally {
            applyingBatch = false
        }
        // Phase 7.2: this pass can re-clamp a scrolled ScrollView (a completed
        // image grew/shrank the content) — the sample it fires mid-guard
        // conflated, and this is its lane-availability, same as applyBatch's tail.
        flushScrollWires()
    }

    /**
     * Phase 6.1 — THE ROUTER. The SetStyle allow-list is PARTITIONED
     * (`NativeRenderer.YogaStyleAttributes` / `VisualStyleAttributes`, design
     * §"The allow-list is a routing table"), and every style name goes to exactly
     * ONE destination:
     *
     *  - a **LAYOUT** name ([YogaLayout.owns] — flexDirection, width, height,
     *    padding, margin, …) → the node's **Yoga node**, and nowhere else.
     *  - a **VISUAL** name (backgroundColor, fontSize, …) → the **View** (paint).
     *
     * `padding`'s old `view.setPadding(...)` arm is GONE, deliberately: Yoga lays
     * a container's children out inside its padding box, so a surviving setPadding
     * would apply the inset a SECOND time and put every child in that container
     * off by it. Same reasoning retires any view-level width/height/margin.
     *
     * Matching is ordinal/case-sensitive — the same discipline .NET's ordinal
     * allow-list keeps, so a mis-cased name falls onto the prop wire (visible)
     * rather than being silently swallowed here.
     */
    private fun handleSetStyle(p: RenderPatch.SetStyle) {
        if (nodes[p.nodeId] == null) {
            Log.w(TAG, "SetStyle for unknown nodeId ${p.nodeId}: ignored")
            return
        }
        if (yoga.owns(p.property)) {
            yoga.setStyle(p.nodeId, p.property, p.value)
            return
        }
        val view = nodes.getValue(p.nodeId)
        when (p.property) {
            "backgroundColor" -> {
                val color = p.value?.let { parseColorOrNull(it) }
                    ?: return logIgnore("backgroundColor", p.value)
                view.setBackgroundColor(color)
            }
            "fontSize" -> {
                val tv = view as? TextView
                    ?: return logIgnore("fontSize", "${view::class.simpleName} is not TextView")
                val sp = p.value?.let { parseFloatOrNull(it) }
                    ?: return logIgnore("fontSize", p.value)
                tv.setTextSize(TypedValue.COMPLEX_UNIT_SP, sp)
                yoga.markDirty(tv) // a bigger font is a bigger intrinsic size
            }
            else -> Log.w(TAG, "SetStyle '${p.property}' not yet supported (Phase 3+ extends)")
        }
    }

    private fun parseColorOrNull(s: String): Int? =
        try { Color.parseColor(s) } catch (_: IllegalArgumentException) { null }

    /** The LEGACY VISUAL props' tolerant number parser (fontSize). The layout
     * grammar has NO unit suffixes (design §"Style value grammar"; `px` is not
     * dp, `sp` is font-scaled, neither exists on iOS) and [YogaLayout] does not
     * strip them — this defensive strip survives only for the visual props that
     * shipped before the grammar existed. */
    private fun parseFloatOrNull(s: String): Float? =
        s.removeSuffix("sp").removeSuffix("dp").removeSuffix("px").toFloatOrNull()

    private fun logIgnore(prop: String, detail: String?) {
        Log.w(TAG, "SetStyle $prop ignored: $detail")
    }

    private companion object {
        const val TAG = "BlazorNative.WidgetMapper"

        /** The one nodeType that is a VIEWPORT and owns a synthetic content view.
         * The same constant [YogaLayout] keys its half of the pair on — one contract,
         * one spelling. */
        const val SCROLL = "scroll"

        /** Phase 6.3 (Gate 2 review, I3) — the bound on [imageResultLog]. It is a DIAGNOSTIC
         * ring, not a ledger: the tests read the last handful of entries and an app that runs
         * for a day must not accumulate one entry per image per navigation, forever. Large
         * enough that no realistic test can notice the eviction; small enough that it is a
         * bound. */
        const val MAX_IMAGE_RESULTS = 64
    }
}
