package io.blazornative.jni

/**
 * Phase 4.3 Gate 1 — the PreviewHost's testable core: consumes decoded
 * [RenderFrame]s (the same [NativeFrameAdapter] read path every host rides)
 * and maintains a minimal node model whose [render] is an indented console
 * tree. Pure JVM — deliberately NO Android imports; this is WidgetMapper's
 * placement semantics, console-shaped:
 *
 *  - CreateNode honors [RenderPatch.CreateNode.insertIndex] BUCKET-LOCALLY
 *    (the parent's ordered children list, exactly `addView(view, index)`);
 *    -1 = append, explicitly encoded — 0 is a valid front index. An unknown
 *    parentId falls back to the root bucket (WidgetMapper's `?: root`).
 *  - Text-child collapse (Phase 2.8 parity): a "text" create whose parent is
 *    a text-bearing LEAF (anything that maps to a TextView-but-not-ViewGroup:
 *    text/button/input, plus the unknown-type TextView fallback) allocates no
 *    node — the child id aliases onto the parent, so the subsequent
 *    ReplaceText lands as the parent's own label. Console consequence: a text
 *    child under a button/input just shows as the widget's text.
 *  - AttachEvent/DetachEvent render as `events={name=#handlerId}` annotations;
 *    re-attach is last-wins replace (never stacks), detach clears by
 *    eventName — mirroring WidgetMapper's Phase 4.2 watcher semantics.
 *  - RemoveNode drops the whole subtree and purges every id mapping onto it
 *    (identity match — collapsed text ids alias several ids to one node);
 *    later patches against removed/unknown ids are skipped (log+ignore
 *    parity, minus the log — the console tree IS the diagnostic).
 *  - CommitFrame is a boundary marker; the snapshot is single-threaded, so
 *    there is no batching to the main looper to mirror.
 *
 * Line format (one node per line, two-space indent per depth):
 *
 *   type#id "text" props={k=v, ...} styles={k=v, ...} events={name=#id, ...}
 *
 * with every segment omitted when empty; props/styles keep first-seen order
 * and overwrite in place, so re-render diffs read stably across cycles.
 */
class TreeSnapshot {

    private class Node(val id: Int, val type: String) {
        var text: String? = null
        val props = LinkedHashMap<String, String?>()
        val styles = LinkedHashMap<String, String?>()
        val events = LinkedHashMap<String, Int>()
        val children = mutableListOf<Node>()
        var parent: Node? = null
    }

    private val nodes = mutableMapOf<Int, Node>()
    private val roots = mutableListOf<Node>()

    /** Number of [apply] calls — PreviewHost's frame-count summary line. */
    var framesApplied = 0
        private set

    fun apply(frame: RenderFrame) {
        framesApplied++
        for (patch in frame.patches) when (patch) {
            is RenderPatch.CreateNode -> handleCreate(patch)
            is RenderPatch.ReplaceText -> nodes[patch.nodeId]?.text = patch.text
            is RenderPatch.RemoveNode -> handleRemove(patch)
            is RenderPatch.UpdateProp -> nodes[patch.nodeId]?.props?.put(patch.name, patch.value)
            is RenderPatch.SetStyle -> nodes[patch.nodeId]?.styles?.put(patch.property, patch.value)
            is RenderPatch.AttachEvent -> nodes[patch.nodeId]?.events?.put(patch.eventName, patch.handlerId)
            is RenderPatch.DetachEvent -> nodes[patch.nodeId]?.events?.remove(patch.eventName)
            is RenderPatch.CommitFrame -> { /* boundary marker; no-op */ }
        }
    }

    private fun handleCreate(p: RenderPatch.CreateNode) {
        // Text-child collapse (see class KDoc): alias the id onto the parent.
        if (p.nodeType == "text") {
            val rawParent = p.parentId?.let { nodes[it] }
            if (rawParent != null && isTextBearingLeaf(rawParent.type)) {
                nodes[p.nodeId] = rawParent
                return
            }
        }

        val node = Node(p.nodeId, p.nodeType)
        nodes[p.nodeId] = node
        // Container parents keep their own bucket; a missing (or non-container)
        // parent falls back to the root bucket — WidgetMapper's
        // `as? ViewGroup ?: root`.
        val parent = p.parentId?.let { nodes[it] }?.takeIf { it.type in CONTAINER_TYPES }
        node.parent = parent
        val bucket = parent?.children ?: roots
        if (p.insertIndex >= 0) bucket.add(p.insertIndex, node) else bucket.add(node)
    }

    private fun handleRemove(p: RenderPatch.RemoveNode) {
        val node = nodes[p.nodeId] ?: return
        // Identity purge over the whole subtree: collapsed text ids alias
        // several map keys onto one node, and the entry may sit under a
        // different id than the one being removed (WidgetMapper's rule).
        val doomed = HashSet<Node>()
        fun collect(n: Node) {
            doomed.add(n)
            n.children.forEach(::collect)
        }
        collect(node)
        nodes.entries.removeAll { it.value in doomed }
        (node.parent?.children ?: roots).remove(node)
    }

    /** The indented text tree ("" while empty — nothing mounted yet). */
    fun render(): String {
        val sb = StringBuilder()
        roots.forEach { renderNode(it, 0, sb) }
        return sb.toString().trimEnd('\n')
    }

    private fun renderNode(n: Node, depth: Int, sb: StringBuilder) {
        sb.append("  ".repeat(depth)).append(n.type).append('#').append(n.id)
        n.text?.let { sb.append(" \"").append(it).append('"') }
        if (n.props.isNotEmpty()) {
            sb.append(n.props.entries.joinToString(", ", " props={", "}") { "${it.key}=${it.value}" })
        }
        if (n.styles.isNotEmpty()) {
            sb.append(n.styles.entries.joinToString(", ", " styles={", "}") { "${it.key}=${it.value}" })
        }
        if (n.events.isNotEmpty()) {
            sb.append(n.events.entries.joinToString(", ", " events={", "}") { "${it.key}=#${it.value}" })
        }
        sb.append('\n')
        n.children.forEach { renderNode(it, depth + 1, sb) }
    }

    private companion object {
        /** NodeTypes WidgetMapper maps to ViewGroups (LinearLayout /
         * ScrollView / Spinner) — the only valid insert targets. */
        val CONTAINER_TYPES = setOf("view", "scroll", "picker")

        /** TextView-but-not-ViewGroup widgets — the collapse targets. Unknown
         * types fall back to TextView in WidgetMapper, so they count too;
         * "image" (ImageView) is the one non-container that doesn't. */
        fun isTextBearingLeaf(type: String) = type !in CONTAINER_TYPES && type != "image"
    }
}
