package io.blazornative.shell

import android.content.Intent
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.FrameLayout
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.util.concurrent.atomic.AtomicReference

/**
 * Phase 6.1 — **ONE RemoveNodePatch STANDS FOR A WHOLE SUBTREE.**
 *
 * The renderer does NOT emit one `RemoveNode` per node: it emits one for the
 * subtree's root and purges the descendants in its own bookkeeping
 * (`NativeRenderer.PurgeNodeSubtree`; the host contract on
 * `ProcessDisposedComponent` says so in as many words). A host that drops only the
 * named node's map entry therefore leaks EVERY DESCENDANT — and the leak is not
 * abstract: the entry pins the `View`, the View pins the Activity `Context`, and
 * the Java `YogaNode`'s native peer can never be reclaimed while anything
 * references it. Every navigation would leak a complete view hierarchy plus a
 * complete native Yoga tree.
 *
 * A leaked node lays out nothing and shows nothing, so **no frame assertion can see
 * this** — the only honest witness is the mapper's own bookkeeping, against the
 * REAL renderer's patch stream (a synthetic RemoveNode would be assuming the very
 * thing under test: that one patch arrives for a subtree).
 *
 * So: mount `CompositionProbe`, whose `ItemComponent` is a two-node subtree (a div
 * + its text child) with an Add and a Remove button, and cycle. Every add-then-
 * remove returns the tree to the shape it had, so **the node counts must return to
 * their baseline**. With the purge missing they grow by one node per cycle, forever.
 */
@RunWith(AndroidJUnit4::class)
class YogaNodeLifecycleAndroidTest {

    @Test fun removing_a_subtree_purges_every_descendant_from_both_trees() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val intent = Intent(ctx, MainActivity::class.java)
            .putExtra(MainActivity.EXTRA_COMPONENT, "CompositionProbe")

        ActivityScenario.launch<MainActivity>(intent).use { scenario ->
            assertTrue("CompositionProbe never rendered within 60s", pollForProbe(scenario))

            val baseline = readCounts(scenario)
            assertTrue("the mount must have created nodes at all", baseline.nodes > 0)
            assertEquals("every view node must have a Yoga node except the COLLAPSED text " +
                "children (the three buttons') — the Yoga tree mirrors the VIEW tree",
                baseline.yogaNodes, baseline.yogaViews)

            // Three add→remove cycles. Add appends item-N (a div + its text child);
            // Remove drops the FIRST item (the same two nodes). Net zero, every time.
            repeat(3) { cycle ->
                tapButton(scenario, "Add")
                assertTrue("the list never grew to 3 items after Add (cycle $cycle)",
                    pollForItemCount(scenario, 3))
                tapButton(scenario, "Remove")
                assertTrue("the list never shrank back to 2 items after Remove (cycle $cycle)",
                    pollForItemCount(scenario, 2))
            }

            val after = readCounts(scenario)
            assertEquals(
                "THE PIN: an add→remove cycle is net-zero, so the mapper's node count must be " +
                    "back at its baseline. One RemoveNodePatch arrives for the item's whole " +
                    "SUBTREE — purge only the named node and its text child stays in the map " +
                    "forever, pinning a View, an Activity Context and a native Yoga peer, once " +
                    "per cycle (3 cycles ⇒ +3 here, and one per navigation in the real app)",
                baseline.nodes, after.nodes,
            )
            assertEquals("…and the Yoga tree's nodes with it (its purge is the one that frees " +
                "native memory)", baseline.yogaNodes, after.yogaNodes)
            assertEquals("…and the Yoga node → View mappings, which are what pin the Context",
                baseline.yogaViews, after.yogaViews)
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private data class Counts(val nodes: Int, val yogaNodes: Int, val yogaViews: Int)

    /** The mapper's live bookkeeping, read on the main thread. */
    private fun readCounts(scenario: ActivityScenario<MainActivity>): Counts {
        val out = AtomicReference(Counts(0, 0, 0))
        scenario.onActivity { act ->
            out.set(Counts(act.mapper.nodeCount, act.mapper.yogaNodeCount, act.mapper.yogaViewCount))
        }
        return out.get()
    }

    /** The probe's root container: widget_root's single child, once it holds the
     * full 7-child composite (CompositionAndroidTest's shape). */
    private fun rootContainer(act: MainActivity): ViewGroup? =
        act.findViewById<FrameLayout>(R.id.widget_root)
            ?.takeIf { it.childCount > 0 }
            ?.getChildAt(0) as? ViewGroup

    private fun pollForProbe(scenario: ActivityScenario<MainActivity>): Boolean {
        val deadline = System.currentTimeMillis() + 60_000
        val ready = AtomicReference(false)
        while (System.currentTimeMillis() < deadline) {
            scenario.onActivity { act ->
                ready.set((rootContainer(act)?.childCount ?: 0) >= 7)
            }
            if (ready.get()) break
            Thread.sleep(250)
        }
        return ready.get()
    }

    /** Polls until the list container (root child [3]) holds [expected] items. */
    private fun pollForItemCount(scenario: ActivityScenario<MainActivity>, expected: Int): Boolean {
        val deadline = System.currentTimeMillis() + 10_000
        val ok = AtomicReference(false)
        while (System.currentTimeMillis() < deadline) {
            scenario.onActivity { act ->
                val list = rootContainer(act)?.takeIf { it.childCount >= 4 }?.getChildAt(3) as? ViewGroup
                ok.set(list != null && list.childCount == expected)
            }
            if (ok.get()) break
            Thread.sleep(250)
        }
        return ok.get()
    }

    private fun tapButton(scenario: ActivityScenario<MainActivity>, label: String) {
        val clicked = AtomicReference(false)
        scenario.onActivity { act ->
            val root = act.findViewById<FrameLayout>(R.id.widget_root)
            val button = root?.let { firstMatch(it) { v -> v is Button && v.text.toString() == label } }
            if (button != null) {
                button.performClick()
                clicked.set(true)
            }
        }
        assertTrue("Button '$label' not found on screen", clicked.get())
    }

    private fun firstMatch(view: View, predicate: (View) -> Boolean): View? {
        if (predicate(view)) return view
        if (view is ViewGroup) {
            for (i in 0 until view.childCount) firstMatch(view.getChildAt(i), predicate)?.let { return it }
        }
        return null
    }
}
