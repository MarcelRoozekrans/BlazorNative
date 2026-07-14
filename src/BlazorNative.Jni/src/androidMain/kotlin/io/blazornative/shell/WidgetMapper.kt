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
 * the source. The normative contract (and the SEVEN rows this class owes it) is
 * docs/plans/2026-07-14-phase-6.3-design.md §"The parity contract"; iOS mirrors it
 * with Kingfisher, and asserts THE SAME FRAMES.
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
     * Phase 6.3 — **the in-flight image request per `image` node**, and the handle that
     * CANCELS it.
     *
     * Cancellation is **memory safety, not hygiene** (6.3 non-negotiable #4): a completion
     * firing into a removed node would paint into a detached widget here and touch a
     * **freed `YGNodeRef`** on iOS. So the entry is disposed on a `Src` change
     * ([handleSrc]), on node removal ([handleRemove] — as part of the SUBTREE purge, which
     * is the shape navigation actually emits) and on teardown ([destroy]).
     */
    private val imageRequests = mutableMapOf<Int, Disposable>()

    /**
     * The GENERATION of the request a node's image callbacks may act on — bumped by every
     * `src` write. It is the second half of the "a late completion paints nothing" guard,
     * and it exists because [imageRequests] alone cannot express it: a superseded request's
     * `onCancel` would otherwise `remove(nodeId)` the disposable of the request that
     * REPLACED it, and a completion that races its own `dispose()` would paint stale bytes
     * over fresh ones.
     *
     * The other half is an IDENTITY check (`nodes[nodeId] === view`) in the callbacks, and
     * it is not redundant: **node ids restart at 1 after a reset** (6.2's lesson, learned on
     * the warn-once keys), so a stale callback carrying generation 1 can meet a brand-new
     * node that is also on generation 1. Only identity separates them.
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
     */
    private val imageResultLog = mutableListOf<ImageResult>()

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
            else -> Log.w(TAG, "DetachEvent '${p.eventName}' not supported (forward compat): skipped")
        }
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
            "image"  -> ImageView(context)
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
        // Phase 6.3: every in-flight fetch dies with the Activity. A completion landing on
        // a destroyed mapper would paint into a dead view hierarchy and re-solve a torn-down
        // Yoga tree.
        for (request in imageRequests.values) request.dispose()
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

    /** Test-only (Phase 6.3) — **the synchronization gate's observation surface**: every
     * image request that has TERMINATED, with Coil's own verdict. See [imageResultLog];
     * the AFTER frames of `/image` may only be asserted once this holds all three. */
    internal val imageResults: List<ImageResult> get() = imageResultLog.toList()

    /** Test-only (Phase 6.3): the requests still in flight. Returns to 0 after every
     * completion, cancellation and purge — a non-zero count after a removal is the leak
     * that, on iOS, is a dangling `YGNodeRef`. */
    internal val inFlightImageCount: Int get() = imageRequests.size

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
        cancelImageRequest(nodeId)
        // The bytes the node already holds go NOW, not when (or if) the new ones arrive:
        // "on a Src change the node measures 0 × 0 again until the new bytes land".
        view.setImageDrawable(null)
        yoga.setImageNaturalSize(view, null)
        yoga.markDirty(view)

        if (url.isNullOrEmpty()) return

        val generation = (imageGenerations[nodeId] ?: 0) + 1
        imageGenerations[nodeId] = generation

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
                onSuccess = { _, result -> onImageLoaded(nodeId, generation, view, url, result.drawable) },
                onError = { _, result -> onImageFailed(nodeId, generation, url, result.throwable) },
                onCancel = { _ -> onImageCancelled(nodeId, generation, url) },
            )
            .build()
        imageRequests[nodeId] = context.imageLoader.enqueue(request)
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
        imageResultLog.add(ImageResult(nodeId, url, ImageOutcome.SUCCESS))
        if (!isLiveImageRequest(nodeId, generation, view, url, "completion")) return
        imageRequests.remove(nodeId)

        view.setImageDrawable(drawable)
        // The NATURAL size (pixels, read as dp — YogaLayout.setImageNaturalSize states the
        // rule and why it is the only reading that agrees with iOS). Taken from the decoded
        // BITMAP where there is one: `Bitmap.width` is the raw pixel count and is immune to
        // the density metadata a `BitmapDrawable`'s intrinsicWidth is scaled by.
        yoga.setImageNaturalSize(view, naturalSizeOf(drawable))
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
    private fun onImageFailed(nodeId: Int, generation: Int, url: String, error: Throwable) {
        imageResultLog.add(ImageResult(nodeId, url, ImageOutcome.ERROR))
        if (imageGenerations[nodeId] == generation) imageRequests.remove(nodeId)
        Log.w(TAG, "image load failed for node $nodeId ($url): ${error.javaClass.simpleName}: " +
            "${error.message} — the node stays 0 × 0 and reserves nothing")
    }

    /** We cancelled it: a `Src` change, a node removal, or teardown. Nothing is painted and
     * nothing is re-solved — that is what "cancelled" means. */
    private fun onImageCancelled(nodeId: Int, generation: Int, url: String) {
        imageResultLog.add(ImageResult(nodeId, url, ImageOutcome.CANCELLED))
        if (imageGenerations[nodeId] == generation) imageRequests.remove(nodeId)
    }

    /**
     * **THE PURGED-NODE GUARD** — the defence behind the cancel, and it is not belt-and-
     * braces theatre: [cancelImageRequest] is what *prevents* a completion, and this is what
     * makes one HARMLESS if it ever arrives anyway (a completion already in its main-thread
     * continuation when `dispose()` ran).
     *
     * Two questions, because one does not answer it:
     *  - the GENERATION — has this node's `src` been written since this request was issued?
     *  - the IDENTITY — is the view this callback captured still the view that node id
     *    names? **Node ids restart at 1 after a reset** (6.2's warn-once lesson), so a stale
     *    callback can meet a *different* node wearing its id — and possibly on the same
     *    generation.
     */
    private fun isLiveImageRequest(
        nodeId: Int,
        generation: Int,
        view: ImageView,
        url: String,
        what: String,
    ): Boolean {
        if (imageGenerations[nodeId] != generation || nodes[nodeId] !== view) {
            Log.w(TAG, "stale image $what for node $nodeId ($url) dropped: the node was " +
                "removed, or its src was written again. Nothing painted.")
            return false
        }
        return true
    }

    /** An image's natural size in PIXELS — the decoded bitmap's own, never a density-scaled
     * `intrinsicWidth` (a `BitmapDrawable` scales that by `targetDensity / bitmap.density`,
     * so it is the wrong number on any device whose density is not 1). */
    private fun naturalSizeOf(drawable: Drawable): YogaLayout.NaturalSize {
        val bitmap = (drawable as? BitmapDrawable)?.bitmap
        return if (bitmap != null) YogaLayout.NaturalSize(bitmap.width, bitmap.height)
        else YogaLayout.NaturalSize(
            drawable.intrinsicWidth.coerceAtLeast(0), drawable.intrinsicHeight.coerceAtLeast(0))
    }

    /** Cancels [nodeId]'s in-flight request, if any. Idempotent; safe for a node that never
     * had one. */
    private fun cancelImageRequest(nodeId: Int) {
        imageRequests.remove(nodeId)?.dispose()
    }

    /**
     * A layout pass triggered by something that is NOT a patch — an image completion.
     *
     * Inside the [applyingBatch] guard for the reason `CommitFrame`'s pass is: the pass
     * MEASURES REAL WIDGETS, and a measure that moved focus or text must not dispatch back
     * into .NET.
     */
    private fun resolveLayout() {
        applyingBatch = true
        try {
            yoga.calculateAndApply()
        } finally {
            applyingBatch = false
        }
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
    }
}
