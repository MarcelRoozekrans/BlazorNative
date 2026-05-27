package io.blazornative.shell

import android.content.Context
import android.os.Handler
import android.os.Looper
import android.util.Log
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
 * Threading: `apply(frame)` is called on the WasiHost boot thread. The mapper
 * collects patches until [RenderPatch.CommitFrame], then posts the batch to the
 * main looper for atomic application. Caller-thread-agnostic.
 *
 * Patch coverage (Phase 2.5 scope): CreateNode (all 7 NodeTypes wired),
 * ReplaceText, RemoveNode, CommitFrame. UpdateProp / SetStyle / AttachEvent /
 * DetachEvent / AppendChild are stubbed with Log.w and a TODO marker — Phase 3+.
 *
 * Wire contract: src/BlazorNative.Renderer/PatchProtocol.cs.
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
            is RenderPatch.CommitFrame -> { /* boundary marker; no-op here */ }
            is RenderPatch.UpdateProp,
            is RenderPatch.SetStyle,
            is RenderPatch.AttachEvent,
            is RenderPatch.DetachEvent,
            is RenderPatch.AppendChild -> Log.w(TAG, "TODO Phase 3+: $patch")
        }
    }

    private fun handleCreate(p: RenderPatch.CreateNode) {
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

    private companion object { const val TAG = "BlazorNative.WidgetMapper" }
}
