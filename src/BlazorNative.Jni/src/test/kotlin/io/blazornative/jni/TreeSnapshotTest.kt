package io.blazornative.jni

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.assertThrows

/**
 * Phase 4.3 Gate 1 — TreeSnapshot, the PreviewHost's console-shaped mirror of
 * WidgetMapper's placement semantics, driven by synthetic [RenderFrame]s (no
 * JNA, no dll — pure model-level TDD).
 *
 * Every placement pin transliterates a WidgetMapper rule:
 *  - CreateNode.insertIndex is BUCKET-LOCAL (counts the parent's host
 *    children, exactly `addView(view, index)`); -1 = append, explicitly
 *    encoded — 0 is a valid front index.
 *  - Unknown parentId falls back to the root bucket (`?: root`).
 *  - Text-child-of-a-text-bearing-leaf collapses onto the parent (the
 *    Phase 2.8 collapse): a text child under button/input/text renders as the
 *    parent's own text, never as a separate line.
 *  - Re-attach for the same (node, event) is last-wins with NO preceding
 *    DetachEvent; detach clears the annotation.
 *  - RemoveNode drops the whole subtree; later patches against removed ids
 *    are ignored (log+skip parity), never resurrected.
 */
class TreeSnapshotTest {

    private var nextFrameId = 0

    /** Wraps patches in a [RenderFrame], appending the CommitFrame boundary
     * the encoder always emits at the end of a real frame. */
    private fun frame(vararg patches: RenderPatch): RenderFrame {
        val id = ++nextFrameId
        return RenderFrame(
            frameId = id,
            timestampMs = id * 100L,
            patches = patches.toList() + RenderPatch.CommitFrame(frameId = id, timestampMs = id * 100L),
        )
    }

    private fun create(id: Int, type: String, parent: Int? = null, at: Int = -1) =
        RenderPatch.CreateNode(nodeId = id, nodeType = type, parentId = parent, insertIndex = at)

    // ── Creation + placement ─────────────────────────────────────────────────

    @Test
    fun empty_snapshot_renders_empty() {
        assertEquals("", TreeSnapshot().render())
    }

    @Test
    fun create_renders_parent_child_in_append_order() {
        val snap = TreeSnapshot()
        snap.apply(
            frame(
                create(1, "view"),
                create(2, "text", parent = 1),
                create(3, "input", parent = 1),
                create(4, "view", parent = 1),
                create(5, "image", parent = 4),
            )
        )
        assertEquals(
            """
            view#1
              text#2
              input#3
              view#4
                image#5
            """.trimIndent(),
            snap.render()
        )
    }

    @Test
    fun insert_index_places_mid_list_bucket_local() {
        val snap = TreeSnapshot()
        snap.apply(
            frame(
                create(1, "view"),
                create(2, "text", parent = 1),          // explicit -1 append
                create(3, "button", parent = 1),        // append
                create(4, "button", parent = 1),        // append
                // The BnDemo chain-fix shape: a late create carrying a MID-LIST
                // index into its parent's bucket (after 2, before the buttons).
                create(5, "view", parent = 1, at = 1),
            )
        )
        assertEquals(
            """
            view#1
              text#2
              view#5
              button#3
              button#4
            """.trimIndent(),
            snap.render()
        )
    }

    @Test
    fun insert_index_zero_is_a_valid_front_index() {
        val snap = TreeSnapshot()
        snap.apply(
            frame(
                create(1, "view"),
                create(2, "text", parent = 1),
                create(3, "text", parent = 1, at = 0),
            )
        )
        assertEquals(
            """
            view#1
              text#3
              text#2
            """.trimIndent(),
            snap.render()
        )
    }

    @Test
    fun out_of_range_insert_index_throws_strict_placement_parity() {
        // Gate 1 review N3 pin: WidgetMapper's addView(view, index) throws on
        // an out-of-range index — "inherently strict placement". The snapshot
        // must not silently clamp what Android would crash on.
        val snap = TreeSnapshot()
        assertThrows<IndexOutOfBoundsException> {
            snap.apply(frame(create(1, "view"), create(2, "text", parent = 1, at = 5)))
        }
    }

    @Test
    fun unknown_parent_falls_back_to_the_root_bucket() {
        val snap = TreeSnapshot()
        snap.apply(frame(create(1, "view"), create(9, "text", parent = 42)))
        assertEquals(
            """
            view#1
            text#9
            """.trimIndent(),
            snap.render()
        )
    }

    // ── Text ─────────────────────────────────────────────────────────────────

    @Test
    fun replace_text_sets_then_replaces() {
        val snap = TreeSnapshot()
        snap.apply(frame(create(1, "view"), create(2, "text", parent = 1), RenderPatch.ReplaceText(2, "first")))
        assertEquals("view#1\n  text#2 \"first\"", snap.render())

        snap.apply(frame(RenderPatch.ReplaceText(2, "second")))
        assertEquals("view#1\n  text#2 \"second\"", snap.render())
    }

    @Test
    fun text_child_of_a_text_bearing_leaf_collapses_into_the_parent() {
        val snap = TreeSnapshot()
        snap.apply(
            frame(
                create(1, "view"),
                create(2, "button", parent = 1),
                create(3, "text", parent = 2),          // collapse: no separate node
                RenderPatch.ReplaceText(3, "Clear"),
                create(4, "input", parent = 1),
                create(5, "text", parent = 4),          // same collapse on input
                RenderPatch.ReplaceText(5, "typed"),
            )
        )
        assertEquals(
            """
            view#1
              button#2 "Clear"
              input#4 "typed"
            """.trimIndent(),
            snap.render()
        )
    }

    // ── Props / styles ───────────────────────────────────────────────────────

    @Test
    fun props_render_in_first_seen_order_and_overwrite_in_place() {
        val snap = TreeSnapshot()
        snap.apply(
            frame(
                create(1, "input"),
                RenderPatch.UpdateProp(1, "value", ""),
                RenderPatch.UpdateProp(1, "placeholder", "Type here..."),
                RenderPatch.UpdateProp(1, "enabled", "false"),
            )
        )
        assertEquals("input#1 props={value=, placeholder=Type here..., enabled=false}", snap.render())

        snap.apply(frame(RenderPatch.UpdateProp(1, "value", "hello")))
        assertEquals("input#1 props={value=hello, placeholder=Type here..., enabled=false}", snap.render())
    }

    @Test
    fun styles_render_and_overwrite_in_place() {
        val snap = TreeSnapshot()
        snap.apply(
            frame(
                create(1, "view"),
                RenderPatch.SetStyle(1, "backgroundColor", "#FFEEAA"),
                RenderPatch.SetStyle(1, "padding", "16"),
            )
        )
        assertEquals("view#1 styles={backgroundColor=#FFEEAA, padding=16}", snap.render())

        // The theme toggle shape: overwrite keeps its slot, order stays stable.
        snap.apply(frame(RenderPatch.SetStyle(1, "backgroundColor", "#334455")))
        assertEquals("view#1 styles={backgroundColor=#334455, padding=16}", snap.render())
    }

    // ── Events ───────────────────────────────────────────────────────────────

    @Test
    fun attach_annotates_reattach_replaces_last_wins_detach_clears() {
        val snap = TreeSnapshot()
        snap.apply(frame(create(1, "button"), RenderPatch.AttachEvent(1, "click", handlerId = 7)))
        assertEquals("button#1 events={click=#7}", snap.render())

        // Re-attach: same (node, event), new handlerId, NO preceding detach —
        // the annotation REPLACES (WidgetMapper's last-wins swap), never stacks.
        snap.apply(frame(RenderPatch.AttachEvent(1, "click", handlerId = 9)))
        assertEquals("button#1 events={click=#9}", snap.render())

        snap.apply(frame(RenderPatch.DetachEvent(1, handlerId = 9, eventName = "click")))
        assertEquals("button#1", snap.render())
    }

    // ── Removal ──────────────────────────────────────────────────────────────

    @Test
    fun remove_node_drops_the_whole_subtree() {
        val snap = TreeSnapshot()
        snap.apply(
            frame(
                create(1, "view"),
                create(2, "view", parent = 1),
                create(3, "text", parent = 2),
                create(4, "text", parent = 1),
                RenderPatch.ReplaceText(3, "doomed"),
                RenderPatch.ReplaceText(4, "survivor"),
            )
        )
        snap.apply(frame(RenderPatch.RemoveNode(2)))
        assertEquals(
            """
            view#1
              text#4 "survivor"
            """.trimIndent(),
            snap.render()
        )

        // Patches against removed ids are ignored (WidgetMapper log+skip
        // parity) — the subtree must not resurrect.
        snap.apply(frame(RenderPatch.ReplaceText(3, "zombie")))
        assertEquals(
            """
            view#1
              text#4 "survivor"
            """.trimIndent(),
            snap.render()
        )
    }

    // ── Multi-frame accumulation ─────────────────────────────────────────────

    @Test
    fun multi_frame_accumulation_merges_state_and_counts_frames() {
        val snap = TreeSnapshot()
        // Frame 1 — a BnDemo-shaped mount: form with title, bound input, echo
        // panel created LAST but placed mid-list at index 2 (the chain-fix pin).
        snap.apply(
            frame(
                create(1, "view"),
                RenderPatch.SetStyle(1, "backgroundColor", "#FFEEAA"),
                create(2, "text", parent = 1),
                RenderPatch.ReplaceText(2, "Title"),
                create(3, "input", parent = 1),
                RenderPatch.UpdateProp(3, "value", ""),
                RenderPatch.AttachEvent(3, "change", handlerId = 11),
                create(4, "button", parent = 1),
                create(5, "text", parent = 4),
                RenderPatch.ReplaceText(5, "Clear"),
                RenderPatch.AttachEvent(4, "click", handlerId = 12),
                create(6, "view", parent = 1, at = 2),
                create(7, "text", parent = 6),
                RenderPatch.ReplaceText(7, ""),
            )
        )
        // Frame 2 — a re-render: the typed text echoes + value write-back.
        snap.apply(
            frame(
                RenderPatch.ReplaceText(7, "typed"),
                RenderPatch.UpdateProp(3, "value", "typed"),
            )
        )

        assertEquals(2, snap.framesApplied)
        assertEquals(
            """
            view#1 styles={backgroundColor=#FFEEAA}
              text#2 "Title"
              input#3 props={value=typed} events={change=#11}
              view#6
                text#7 "typed"
              button#4 "Clear" events={click=#12}
            """.trimIndent(),
            snap.render()
        )
    }
}
