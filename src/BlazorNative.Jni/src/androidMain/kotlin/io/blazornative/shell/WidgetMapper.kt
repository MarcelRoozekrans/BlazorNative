package io.blazornative.shell

import android.content.Context
import android.graphics.Color
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
            else -> Log.w(TAG, "UpdateProp '${p.name}' not yet supported (Phase 3+ extends)")
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
