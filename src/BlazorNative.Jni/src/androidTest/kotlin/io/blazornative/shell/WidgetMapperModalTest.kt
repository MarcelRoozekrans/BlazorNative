package io.blazornative.shell

import android.graphics.Color
import android.graphics.drawable.ColorDrawable
import android.view.ViewGroup
import androidx.test.ext.junit.runners.AndroidJUnit4
import io.blazornative.jni.RenderPatch
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

/**
 * Phase 7.4 Gate 2 — **the anchor + overlay model, at the mechanism**
 * (design decision 1: the 6.2 synthetic-node machinery, pointed at the root).
 *
 * Synthetic frames against a [SyntheticHost] (the detached-root discipline:
 * these pin STRUCTURE and CREATION SHAPE — the design says full-root numbers
 * are a DEVICE assertion, and `BnModalDemoAndroidTest` is where the real
 * `widget_root`'s own bounds are read at assert time; the host here has
 * definite 400 × 800 bounds, so the centering arithmetic is still a number).
 *
 * The model under test, stated once (the design's words):
 *  - the ANCHOR — a 0-sized `position:absolute` view at the modal's WIRE slot,
 *    out of the flex flow entirely. THE THIRD INDEX-MAPPING RULE (normative):
 *    it occupies the modal's slot in its wire parent, in the view tree AND the
 *    Yoga tree, so sibling insert indices never skew; the modal's wire child
 *    at index i is the OVERLAY's child at index i.
 *  - the OVERLAY — full-root, attached LAST at the host root, never
 *    re-ordered; children redirect into it; the scrim paints on it; the
 *    dismissal-request `click` listens on it.
 *  - `RemoveNode` purges **BOTH subtrees** (NOT the scroll shape: the overlay
 *    is not a descendant of the anchor — the `modalOverlays` entry names it).
 *
 * The RemoveNode shapes here are the PURE one-remove shape, deliberately:
 * these trees have inline-only content, and the unit-pinned .NET shape for
 * that is exactly one RemoveNodePatch. The COMPOSITE shape — /modal's FIVE
 * removes (the four nested components' disposal removes riding ahead of the
 * modal's own, the 7.2 removes-first order) — is the real renderer's, and
 * `BnModalDemoAndroidTest` asserts the purge under it.
 */
@RunWith(AndroidJUnit4::class)
class WidgetMapperModalTest {

    // ── The anchor: the third index-mapping rule ─────────────────────────────

    @Test fun the_anchor_is_zero_footprint_and_the_siblings_hold_their_exact_frames() {
        val host = SyntheticHost()
        // The demo's shape in miniature: a column with two declared-size boxes.
        host.render(listOf(
            create(1, "view", null),
            create(10, "view", 1), style(10, "width", "220"), style(10, "height", "48"),
            create(11, "view", 1), style(11, "width", "220"), style(11, "height", "48"),
        ))
        host.read {
            val page = host.root.getChildAt(0) as ViewGroup
            assertFrame("box A before the show", page.getChildAt(0), 0f, 0f, 220f, 48f)
            assertFrame("box B before the show", page.getChildAt(1), 0f, 48f, 220f, 48f)
        }

        // THE SHOW: the modal lands BETWEEN the boxes (insertIndex 1 — the
        // anchor's slot), with its declared-size content box inside.
        host.render(listOf(
            create(2, "modal", 1, insertIndex = 1),
            create(20, "view", 2),
            style(20, "width", "280"), style(20, "height", "180"),
        ))

        host.read {
            val page = host.root.getChildAt(0) as ViewGroup
            assertEquals("the anchor OCCUPIES the wire slot — three children now", 3, page.childCount)
            val anchor = page.getChildAt(1)
            assertEquals("the anchor is 0 wide", 0, anchor.width)
            assertEquals("the anchor is 0 tall", 0, anchor.height)
            assertFalse("the anchor is a plain View — it can host NOTHING (children " +
                "redirect to the overlay; a container here would let a redirection bug " +
                "parent into the flex flow silently)", anchor is ViewGroup)
            // THE PIN: the siblings hold the EXACT frames they held with the modal
            // closed — the anchor is absolute and 0-sized, so it contributes
            // nothing to any sibling's frame. Break the anchor's fixed styles and
            // box B moves down by the anchor's height.
            assertFrame("box A with the modal open", page.getChildAt(0), 0f, 0f, 220f, 48f)
            assertFrame("box B with the modal open — the zero-footprint rule",
                page.getChildAt(2), 0f, 48f, 220f, 48f)
        }
    }

    // ── The overlay: last at the root, full-root, children redirected ────────

    @Test fun modal_children_redirect_into_the_overlay_and_the_content_box_centers() {
        val host = SyntheticHost() // 400 × 800 dp
        host.render(listOf(
            create(1, "view", null),
            create(2, "modal", 1),
            create(20, "view", 2), style(20, "width", "280"), style(20, "height", "180"),
        ))
        host.read {
            assertEquals("the host root holds the page AND the overlay", 2, host.root.childCount)
            val overlay = host.root.getChildAt(1) as ViewGroup
            assertFrame("the overlay is the ROOT's own bounds (100%/100% against the " +
                "host — the scrim IS the root)", overlay, 0f, 0f, 400f, 800f)
            assertEquals("the modal's wire child parents into the OVERLAY (the third " +
                "index-mapping rule's second half)", 1, overlay.childCount)
            // The design's frame arithmetic: (W − 280)/2 × (H − 180)/2 — the
            // centering is the SHELL's (the wire carries no layout for it).
            assertFrame("the content box centers against the root",
                overlay.getChildAt(0), (400f - 280f) / 2, (800f - 180f) / 2, 280f, 180f)
        }
    }

    @Test fun a_top_level_wire_append_lands_BEFORE_the_live_overlays() {
        val host = SyntheticHost()
        host.render(listOf(
            create(1, "view", null), style(1, "height", "40"),
            create(2, "modal", null), // a TOP-LEVEL modal: anchor at root, overlay after it
        ))
        // A top-level wire APPEND while the overlay is live: it must slot in
        // AHEAD of the overlay — "the overlay is LAST, always" — or the new
        // node draws over the scrim and the root's 1:1 index arithmetic skews.
        host.render(listOf(
            create(3, "view", null), style(3, "height", "80"),
        ))
        host.read {
            assertEquals(4, host.root.childCount)
            // [0] view 1, [1] anchor 2, [2] view 3 — the append, BEFORE the overlay —
            // [3] the overlay, still last.
            assertFrame("the appended top-level node flows after view 1 in BOTH trees",
                host.root.getChildAt(2), 0f, 40f, 400f, 80f)
            assertFrame("the overlay is STILL the last child, still full-root",
                host.root.getChildAt(3), 0f, 0f, 400f, 800f)
        }
    }

    @Test fun a_top_level_INDEXED_insert_lands_at_its_index_and_the_overlay_stays_last() {
        val host = SyntheticHost()
        host.render(listOf(
            create(1, "view", null), style(1, "height", "40"),
            create(2, "view", null), style(2, "height", "60"),
            create(3, "modal", null), // a TOP-LEVEL modal: anchor at root, overlay after it
        ))
        // Phase 7.6 (H4, the 7.4 G2 review ledger): the APPEND arm above is
        // pinned; the INDEXED insert at root while an overlay is live was
        // correct by construction (ONE resolved index feeds both trees) but
        // unpinned — 7.4 called the asymmetry "a decision, not an oversight".
        // Now it is neither: an indexed CreateNode between existing root
        // anchors must land at its WIRE index — the overlay is not a wire
        // child, so it must not shift the arithmetic — and the overlay stays
        // LAST in both trees.
        host.render(listOf(
            create(4, "view", null, insertIndex = 1), style(4, "height", "80"),
        ))
        host.read {
            assertEquals(5, host.root.childCount)
            // [0] view 1, [1] view 4 — the indexed insert, at its wire index —
            // [2] view 2 (pushed down), [3] anchor 3, [4] the overlay, still last.
            assertFrame("the indexed insert lands between the anchors in BOTH trees " +
                "(flows after view 1)", host.root.getChildAt(1), 0f, 40f, 400f, 80f)
            assertFrame("view 2 flows AFTER the insert — the index resolved against " +
                "the wire order, not the overlay-padded view order",
                host.root.getChildAt(2), 0f, 120f, 400f, 60f)
            assertFrame("the overlay is STILL the last child, still full-root",
                host.root.getChildAt(4), 0f, 0f, 400f, 800f)
        }
    }

    @Test fun a_reshown_modal_is_a_recreate_and_lands_on_top() {
        val host = SyntheticHost()
        host.render(listOf(
            create(1, "view", null),
            create(2, "modal", 1),                    // modal A
            create(20, "view", 2), style(20, "height", "100"),
            create(3, "modal", 1, insertIndex = 1),   // modal B — created after A: on top
            create(30, "view", 3), style(30, "height", "50"),
        ))
        val overlayOfB = host.read {
            assertEquals("page + two overlays, creation order", 3, host.root.childCount)
            // B's overlay is last (its content is the 50-tall box).
            val last = host.root.getChildAt(2) as ViewGroup
            assertEquals(50f, last.getChildAt(0).height / density(), 0.5f)
            last
        }

        // Hide A (unmount — one RemoveNodePatch, the inline-content shape) and
        // RE-SHOW it: a re-created modal (fresh wire id — hide is unmount, the
        // decision-2 posture) lands ON TOP of B. Stacking is creation order;
        // the shell never re-orders.
        host.render(listOf(RenderPatch.RemoveNode(nodeId = 2)))
        host.render(listOf(
            create(4, "modal", 1, insertIndex = 1),
            create(40, "view", 4), style(40, "height", "100"),
        ))
        host.read {
            assertEquals(3, host.root.childCount)
            assertTrue("B's overlay kept its place", host.root.getChildAt(1) === overlayOfB)
            val top = host.root.getChildAt(2) as ViewGroup
            assertEquals("the RE-SHOWN modal's overlay is on top (re-show = re-create = " +
                "attached last)", 100f, top.getChildAt(0).height / density(), 0.5f)
        }
    }

    // ── The scrim: paint by PROP, dismissal by click-on-scrim ────────────────

    @Test fun scrimColor_is_a_prop_and_paints_the_overlay() {
        val host = SyntheticHost()
        host.render(listOf(
            create(1, "view", null),
            create(2, "modal", 1),
            prop(2, "scrimColor", "#80000000"), // the component default, off the wire
        ))
        host.read {
            val overlay = host.root.getChildAt(1)
            assertEquals("the scrim paints the OVERLAY — the one paintable thing the " +
                "author owns on a modal node, and it rides the PROP wire by design",
                Color.parseColor("#80000000"), (overlay.background as ColorDrawable).color)
        }
        // Garbage is logged and ignored (the backgroundColor arm's posture) —
        // the scrim keeps its paint.
        host.render(listOf(prop(2, "scrimColor", "not-a-color")))
        host.read {
            val overlay = host.root.getChildAt(1)
            assertEquals(Color.parseColor("#80000000"), (overlay.background as ColorDrawable).color)
        }
    }

    @Test fun the_dismissal_click_listens_on_the_scrim_never_the_anchor() {
        val dispatches = mutableListOf<Pair<Int, String>>()
        val host = SyntheticHost(onUiEvent = { h, n, _ -> dispatches.add(h to n) })
        host.render(listOf(
            create(1, "view", null),
            create(2, "modal", 1),
            create(20, "view", 2), style(20, "width", "280"), style(20, "height", "180"),
            RenderPatch.AttachEvent(nodeId = 2, eventName = "click", handlerId = 77),   // the modal's dismissal wire
            RenderPatch.AttachEvent(nodeId = 20, eventName = "click", handlerId = 88),  // the content-box swallow
        ))
        host.read {
            val page = host.root.getChildAt(0) as ViewGroup
            val overlay = host.root.getChildAt(1) as ViewGroup
            val anchor = page.getChildAt(0)
            val box = overlay.getChildAt(0)

            // A scrim tap dispatches the modal's click — the dismissal REQUEST.
            assertTrue("the OVERLAY holds the click listener", overlay.performClick())
            assertEquals(listOf(77 to "click"), dispatches)

            // A content-box tap dispatches the BOX's own attach (the swallow — a
            // real dispatch the counters account for), never the modal's: the
            // clickable box consumes the tap before it reaches the scrim
            // (decision 4's Android fall-through half).
            assertTrue(box.performClick())
            assertEquals(listOf(77 to "click", 88 to "click"), dispatches)

            // The anchor holds NO listener — the wire's view is the scrim.
            assertFalse("the anchor must not be clickable", anchor.performClick())
            assertEquals(2, dispatches.size)
            assertEquals("every click above rode the counted dispatch path",
                2, host.mapper.clickDispatchesSent)
        }
    }

    // ── The back consult (decision 3's mapper half) ──────────────────────────

    @Test fun back_consults_the_TOPMOST_modal_and_only_when_one_is_live() {
        val dispatches = mutableListOf<Pair<Int, String>>()
        val host = SyntheticHost(onUiEvent = { h, n, _ -> dispatches.add(h to n) })
        host.render(listOf(create(1, "view", null)))

        // No live overlay: back is NOT consumed — navigation-back's turn.
        host.read {
            assertFalse("no overlay ⇒ the consult declines and navigation-back runs",
                host.mapper.requestTopmostModalDismissal())
            assertTrue(dispatches.isEmpty())
        }

        // Two live modals: back consumes and dispatch-requests the TOPMOST
        // (the last-created — stacking is creation order).
        host.render(listOf(
            create(2, "modal", 1),
            RenderPatch.AttachEvent(nodeId = 2, eventName = "click", handlerId = 70),
            create(3, "modal", 1, insertIndex = 1),
            RenderPatch.AttachEvent(nodeId = 3, eventName = "click", handlerId = 71),
        ))
        host.read {
            assertTrue("a live overlay ⇒ back is CONSUMED", host.mapper.requestTopmostModalDismissal())
            assertEquals("…and the dismissal request went to the TOPMOST modal's wire — " +
                "a REQUEST: .NET flips Visible, the shell closed nothing itself",
                listOf(71 to "click"), dispatches)
        }

        // The topmost gone (one RemoveNodePatch — inline content, the pure
        // shape), the consult falls to the one beneath.
        host.render(listOf(RenderPatch.RemoveNode(nodeId = 3)))
        host.read {
            assertTrue(host.mapper.requestTopmostModalDismissal())
            assertEquals(listOf(71 to "click", 70 to "click"), dispatches)
        }
    }

    // ── The style-ignore rule (decision 1, live code) ────────────────────────

    @Test fun setStyle_on_a_modal_node_is_diagnosed_and_ignored() {
        val host = SyntheticHost()
        host.render(listOf(
            create(1, "view", null),
            create(2, "modal", 1),
            prop(2, "scrimColor", "#80000000"),
            create(20, "view", 2), style(20, "width", "280"), style(20, "height", "180"),
        ))
        // The hand-rolled-wire hatch (the .NET side pins it open): a LAYOUT name
        // and a VISUAL name, both against the modal node. Every style would land
        // on the anchor or the overlay, neither of which the author owns.
        host.render(listOf(
            style(2, "width", "100"),
            style(2, "backgroundColor", "#FF0000"),
        ))
        host.read {
            val page = host.root.getChildAt(0) as ViewGroup
            val overlay = host.root.getChildAt(1) as ViewGroup
            assertEquals("width did NOT size the anchor back into the flex flow",
                0, page.getChildAt(0).width)
            assertFrame("the overlay kept its shell-fixed full-root frame",
                overlay, 0f, 0f, 400f, 800f)
            assertEquals("backgroundColor did NOT repaint the scrim (the scrim's paint " +
                "is the scrimColor PROP)", Color.parseColor("#80000000"),
                (overlay.background as ColorDrawable).color)
            val diags = host.mapper.diagnostics.filter { it.contains("`modal` node") }
            assertEquals("both drops are RECORDED — logcat is not an assertion surface, " +
                "and the failure this rule prevents is silent on every frame table",
                2, diags.size)
            assertTrue(diags[0].contains("width") && diags[1].contains("backgroundColor"))
        }
    }

    // ── The two-subtree purge (the overlay-count lifecycle pin) ──────────────

    @Test fun removeNode_on_the_modal_purges_BOTH_subtrees() {
        val host = SyntheticHost()
        host.render(listOf(create(1, "view", null)))
        val baseline = host.read { counts(host) }

        host.render(listOf(
            create(2, "modal", 1),
            prop(2, "scrimColor", "#80000000"),
            RenderPatch.AttachEvent(nodeId = 2, eventName = "click", handlerId = 77),
            create(20, "view", 2), style(20, "width", "280"), style(20, "height", "180"),
            create(21, "text", 20), text(21, "inside the overlay"),
        ))
        val mounted = host.read { counts(host) }
        assertEquals("the modal mounts THREE wire nodes (anchor id + box + text)",
            baseline.nodes + 3, mounted.nodes)
        assertEquals("ONE live overlay", 1, mounted.overlays)
        assertEquals("…in the Yoga tree too — both trees or neither", 1, mounted.yogaOverlays)

        // ONE RemoveNodePatch (inline-only content — the unit-pinned pure shape;
        // /modal's composite five-remove shape is BnModalDemoAndroidTest's).
        // It stands for the ANCHOR's subtree AND the OVERLAY's — the overlay is
        // not a descendant of the anchor, and the design names the difference.
        host.render(listOf(RenderPatch.RemoveNode(nodeId = 2)))

        val after = host.read { counts(host) }
        assertEquals("the wire nodes are gone", baseline.nodes, after.nodes)
        assertEquals("THE PIN: the overlay count is back to 0. Purge only the anchor's " +
            "subtree and the overlay, the content box and everything under it leak once " +
            "per dismissal — and on iOS the same miss is a dangling YGNodeRef.",
            0, after.overlays)
        assertEquals("…and the Yoga overlay node with it", 0, after.yogaOverlays)
        assertEquals("…and its view mappings (what pins the Activity Context)",
            baseline.yogaViews, after.yogaViews)
        assertEquals("…and the Yoga wire nodes", baseline.yogaNodes, after.yogaNodes)
        assertEquals("the overlay view is DETACHED from the host root",
            1, host.read { host.root.childCount })
    }

    @Test fun removing_a_WRAPPER_purges_the_modal_and_its_overlay_transitively() {
        val host = SyntheticHost()
        val baseline = host.read { counts(host) }

        // THE PATCH NAVIGATION ACTUALLY EMITS: away from /modal with the modal
        // OPEN, the RemoveNode names the PAGE root — nothing names the modal,
        // and nothing could ever name the overlay. The anchor is found
        // transitively; the fixpoint is what takes the overlay with it.
        host.render(listOf(
            create(1, "view", null),
            create(10, "view", 1), style(10, "height", "48"),
            create(2, "modal", 1, insertIndex = 1),
            create(20, "view", 2), style(20, "width", "280"), style(20, "height", "180"),
        ))
        assertEquals(1, host.read { counts(host) }.overlays)

        host.render(listOf(RenderPatch.RemoveNode(nodeId = 1)))

        val after = host.read { counts(host) }
        assertEquals(baseline.nodes, after.nodes)
        assertEquals("THE PIN: the overlay is freed by a patch that names the modal's " +
            "GRANDPARENT — the path every navigation-away takes", 0, after.overlays)
        assertEquals(0, after.yogaOverlays)
        assertEquals(baseline.yogaViews, after.yogaViews)
        assertEquals(baseline.yogaNodes, after.yogaNodes)
        assertEquals("nothing is left under the host root", 0, host.read { host.root.childCount })
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private data class Counts(
        val nodes: Int,
        val yogaNodes: Int,
        val yogaViews: Int,
        val overlays: Int,
        val yogaOverlays: Int,
    )

    private fun counts(host: SyntheticHost) = Counts(
        host.mapper.nodeCount, host.mapper.yogaNodeCount, host.mapper.yogaViewCount,
        host.mapper.modalOverlayCount, host.mapper.yogaOverlayNodeCount)
}
