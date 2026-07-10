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
import android.widget.LinearLayout
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
 * DetachEvent (click listener + re-entrancy-guarded change TextWatcher; see
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
     * Phase 3.2 re-entrancy guard: true while [applyBatch] runs. A programmatic
     * `setText` during patch application (ReplaceText/UpdateProp on an EditText)
     * fires its TextWatcher synchronously; the watcher checks this flag and
     * skips the dispatch — otherwise a change dispatch → re-render → setText
     * loop would spin. Plain field (no volatile/atomic): both applyBatch and
     * every watcher callback run on the main looper thread only.
     */
    private var applyingBatch = false

    /**
     * Live change-watchers keyed by handlerId so DetachEvent can remove them
     * (view tags would need res-ids; a map is simpler). Main-thread only.
     */
    private val watchers = mutableMapOf<Int, Pair<EditText, TextWatcher>>()

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
                is RenderPatch.CommitFrame -> { /* boundary marker; no-op here */ }
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
     * only ever target ViewGroup containers — do NOT "fix" the collapse when
     * wiring addView(view, index).
     *
     * Detach vs re-attach (Phase 3.3): a GENUINE on* attribute removal now
     * emits DetachEventPatch (with eventName) — [handleDetachEvent] handles
     * it. Re-attach for the same (node, event) is STILL last-wins with NO
     * preceding DetachEvent (see RenderFrame.kt's mapping notes): click
     * re-attach overwrites via setOnClickListener; change re-attach replaces
     * the [watchers] map entry but leaves the previous watcher registered on
     * the EditText — a stale watcher dispatches a dead handlerId, absorbed by
     * the rc-0 at-most-once stale contract. That re-attach caveat remains
     * real until watchers are swapped keyed by (node, event), not handlerId.
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
                watchers[p.handlerId] = view to watcher
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
                val removed = watchers.remove(p.handlerId)?.also { (editText, watcher) ->
                    editText.removeTextChangedListener(watcher)
                }
                if (removed == null) {
                    // Legal under the last-wins re-attach caveat (see
                    // [handleAttachEvent]): the map entry may have been
                    // replaced by a re-attach before this detach arrived.
                    Log.w(TAG, "DetachEvent 'change' handlerId ${p.handlerId} has no live watcher: ignored")
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
        if (p.nodeType == "text") {
            val rawParent = p.parentId?.let { nodes[it] }
            if (rawParent is TextView && rawParent !is android.view.ViewGroup) {
                nodes[p.nodeId] = rawParent
                return  // no separate view; subsequent ReplaceText sets parent's text
            }
        }

        val view: View = when (p.nodeType) {
            "view"   -> LinearLayout(context).apply { orientation = LinearLayout.VERTICAL }
            "text"   -> TextView(context)
            "button" -> Button(context)
            "input"  -> EditText(context)
            "image"  -> ImageView(context)
            "scroll" -> ScrollView(context)
            "picker" -> Spinner(context)
            else     -> {
                Log.w(TAG, "Unknown nodeType ${p.nodeType} — falling back to TextView")
                TextView(context)
            }
        }
        nodes[p.nodeId] = view
        val parent = p.parentId?.let { nodes[it] as? ViewGroup } ?: root
        // Phase 3.3 Task 9 (DoD #10): honor the renderer-computed placement.
        // insertIndex counts HOST views in the target container — and a
        // WidgetMapper ViewGroup's children ARE exactly those host views 1:1
        // (the only nodes that never materialize a child view are collapsed
        // text nodes, and those alias onto non-ViewGroup parents — see the
        // collapse block above — so they can't skew a container's indices).
        // No translation needed; mirrors the JVM twin's arithmetic
        // (CompositionProbeTest: "the list container's children are
        // EXCLUSIVELY the keyed ItemComponent views"). −1 = append,
        // explicitly encoded (0 is a valid front index).
        if (p.insertIndex >= 0) parent.addView(view, p.insertIndex)
        else parent.addView(view)
    }

    private fun handleReplaceText(p: RenderPatch.ReplaceText) {
        (nodes[p.nodeId] as? TextView)?.text = p.text
    }

    private fun handleRemove(p: RenderPatch.RemoveNode) {
        val v = nodes.remove(p.nodeId) ?: return
        // Purge change watchers registered on the removed view — the detached
        // EditText would otherwise pin itself (and its watcher) in [watchers]
        // forever. Identity match: the collapse can alias several nodeIds to
        // one view, but watchers are keyed by handlerId, not nodeId.
        watchers.entries.removeAll { it.value.first === v }
        (v.parent as? ViewGroup)?.removeView(v)
    }

    private fun handleUpdateProp(p: RenderPatch.UpdateProp) {
        val view = nodes[p.nodeId] ?: run {
            Log.w(TAG, "UpdateProp for unknown nodeId ${p.nodeId}: ignored")
            return
        }
        when (p.name) {
            "placeholder" -> {
                if (view is EditText) view.hint = p.value
                else Log.w(TAG, "UpdateProp placeholder ignored: $view is not EditText")
            }
            "enabled" -> {
                view.isEnabled = p.value?.toBoolean() ?: true
            }
            else -> Log.w(TAG, "UpdateProp '${p.name}' not yet supported (Phase 3+ extends)")
        }
    }

    private fun handleSetStyle(p: RenderPatch.SetStyle) {
        val view = nodes[p.nodeId] ?: run {
            Log.w(TAG, "SetStyle for unknown nodeId ${p.nodeId}: ignored")
            return
        }
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
            }
            "padding" -> {
                val dp = p.value?.let { parseFloatOrNull(it) }
                    ?: return logIgnore("padding", p.value)
                val px = TypedValue.applyDimension(
                    TypedValue.COMPLEX_UNIT_DIP, dp, context.resources.displayMetrics
                ).toInt()
                view.setPadding(px, px, px, px)
            }
            else -> Log.w(TAG, "SetStyle '${p.property}' not yet supported (Phase 3+ extends)")
        }
    }

    private fun parseColorOrNull(s: String): Int? =
        try { Color.parseColor(s) } catch (_: IllegalArgumentException) { null }

    private fun parseFloatOrNull(s: String): Float? =
        s.removeSuffix("sp").removeSuffix("dp").removeSuffix("px").toFloatOrNull()

    private fun logIgnore(prop: String, detail: String?) {
        Log.w(TAG, "SetStyle $prop ignored: $detail")
    }

    private companion object { const val TAG = "BlazorNative.WidgetMapper" }
}
