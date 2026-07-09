package io.blazornative.shell

import android.content.Context
import android.graphics.Color
import android.os.Handler
import android.os.Looper
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
 * Patch coverage (Phase 2.5 scope): CreateNode (all 7 NodeTypes wired),
 * ReplaceText, RemoveNode, CommitFrame. UpdateProp / SetStyle / AttachEvent /
 * DetachEvent / AppendChild are stubbed with Log.w and a TODO marker — Phase 3+.
 *
 * Patch model: src/BlazorNative.Renderer/PatchProtocol.cs (the wire itself is
 * the typed-struct C ABI decoded by [io.blazornative.jni.NativeFrameAdapter]).
 * Source of truth for the NodeType → widget table: docs/planning/MILESTONE.md DoD #6.
 */
class WidgetMapper(private val context: Context, private val root: ViewGroup) {
    private val nodes = mutableMapOf<Int, View>()
    private val mainHandler = Handler(Looper.getMainLooper())
    private val pending = mutableListOf<RenderPatch>()

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
        for (patch in patches) when (patch) {
            is RenderPatch.CreateNode  -> handleCreate(patch)
            is RenderPatch.ReplaceText -> handleReplaceText(patch)
            is RenderPatch.RemoveNode  -> handleRemove(patch)
            is RenderPatch.UpdateProp  -> handleUpdateProp(patch)
            is RenderPatch.SetStyle    -> handleSetStyle(patch)
            is RenderPatch.CommitFrame -> { /* boundary marker; no-op here */ }
            is RenderPatch.AttachEvent,
            is RenderPatch.DetachEvent,
            is RenderPatch.AppendChild -> Log.w(TAG, "TODO Phase 3+: $patch")
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
        parent.addView(view)
    }

    private fun handleReplaceText(p: RenderPatch.ReplaceText) {
        (nodes[p.nodeId] as? TextView)?.text = p.text
    }

    private fun handleRemove(p: RenderPatch.RemoveNode) {
        val v = nodes.remove(p.nodeId) ?: return
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
