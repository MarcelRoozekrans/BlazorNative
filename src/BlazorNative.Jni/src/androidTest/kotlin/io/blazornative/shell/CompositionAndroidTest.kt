package io.blazornative.shell

import android.content.Intent
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.TextView
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.util.concurrent.atomic.AtomicReference

/**
 * Phase 3.3 Gate 3 — the composition composite, live on the AVD: the
 * on-device third of CompositionProbeTest (JVM, through the dll) and
 * CompositionProbeTests.cs (.NET, in-process). Launches MainActivity with the
 * [MainActivity.EXTRA_COMPONENT] Intent extra so the NativeAOT boot mounts
 * "CompositionProbe" instead of the Hello demo, then asserts what Tasks 1-5
 * only proved at the patch level: the view TREE — child ORDER inside real
 * LinearLayouts after WidgetMapper's addView(view, index).
 *
 * Expected mount tree (CompositionProbe.cs shape × WidgetMapper's NodeType
 * table; divs → vertical LinearLayouts, buttons → Button with the Phase 2.8
 * text collapse, ItemComponent divs each hold one TextView "<Label> (taps: N)"):
 *   widget_root: FrameLayout
 *     └── root LinearLayout (root div), 7 children IN THIS ORDER:
 *           [0] header  LinearLayout ("CompositionProbe")
 *           [1] badge   LinearLayout ("badge (taps: 0)")   ← the InsertIndex=1
 *           [2] label   LinearLayout ("list:")               mount proof: the
 *           [3] list    LinearLayout (item-1, item-2)        badge's create runs
 *           [4] Button "Add"                                 AFTER the parent
 *           [5] Button "Insert"                              walk appended
 *           [6] Button "Remove"                              children 2..6
 *
 * Buttons are found by TEXT ("Add"/"Insert"/"Remove"), items by their
 * "item-N (taps: 0)" TextViews — never by nodeId (process-global counters)
 * or patch order (JVM-twin convention).
 *
 * STRICT MODE (DoD #9): strict is guaranteed by BlazorNativeTestRunner —
 * the runner sets BLAZORNATIVE_STRICT=1 before any test class loads
 * (Phase 3.5 Gate 0; the per-class setenv pattern is gone).
 *
 * Polling: boot deadline 60s, post-tap re-render deadline 10s — the
 * EventRoundTripAndroidTest precedent (dispatch is async from the UI thread).
 */
@RunWith(AndroidJUnit4::class)
class CompositionAndroidTest {

    private fun launchProbe(): ActivityScenario<MainActivity> {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val intent = Intent(ctx, MainActivity::class.java)
            .putExtra(MainActivity.EXTRA_COMPONENT, "CompositionProbe")
        return ActivityScenario.launch(intent)
    }

    // ── The composite on-screen: root child ORDER is the assertion ──────────

    @Test
    fun composite_renders_with_interleaved_badge() {
        launchProbe().use { scenario ->
            val root = pollForRootContainer(scenario)
            assertNotNull("CompositionProbe never rendered within 60s — boot/mapper failed", root)

            scenario.onActivity { act ->
                val container = rootContainer(act)!!
                assertEquals(
                    "root container must hold exactly 7 children " +
                        "(header, badge, label, list, 3 buttons); got ${describeChildren(container)}",
                    7, container.childCount
                )

                // [0] header div — parent-walk child #1.
                assertEquals("child 0 must be the header",
                    "CompositionProbe", firstTextIn(container.getChildAt(0)))

                // [1] the interleaved badge — its create carried InsertIndex 1
                // (pinned at the patch level by the JVM twin); addView(view, 1)
                // must have landed it BETWEEN header and label even though the
                // ItemComponent's own diff ran after the parent's full walk.
                val badge = container.getChildAt(1)
                assertTrue("child 1 (badge ItemComponent root) must be a LinearLayout, " +
                    "got ${badge::class.simpleName}", badge is LinearLayout)
                assertEquals("child 1 must be the interleaved badge (InsertIndex=1 proof)",
                    "badge (taps: 0)", firstTextIn(badge))

                // [2] label, [3] list with the two seed items IN ORDER.
                assertEquals("child 2 must be the list label",
                    "list:", firstTextIn(container.getChildAt(2)))
                val list = container.getChildAt(3)
                assertTrue("child 3 (list container) must be a LinearLayout",
                    list is LinearLayout)
                list as LinearLayout
                assertEquals("list must hold the two seed items; got ${describeChildren(list)}",
                    2, list.childCount)
                assertEquals("item-1 (taps: 0)", firstTextIn(list.getChildAt(0)))
                assertEquals("item-2 (taps: 0)", firstTextIn(list.getChildAt(1)))

                // [4..6] the three buttons, in source order, text-collapsed.
                for ((i, label) in listOf(4 to "Add", 5 to "Insert", 6 to "Remove")) {
                    val v = container.getChildAt(i)
                    assertTrue("child $i must be a Button, got ${v::class.simpleName}",
                        v is Button)
                    assertEquals("child $i must be the $label button",
                        label, (v as Button).text.toString())
                }
            }
        }
    }

    // ── Insert-at-front: InsertIndex 0 lands the new item FIRST on screen ───

    @Test
    fun insert_at_front_lands_first_in_list() {
        launchProbe().use { scenario ->
            assertNotNull("boot failed", pollForRootContainer(scenario))

            tapButton(scenario, "Insert")

            // InsertAtFront() does _items.Insert(0, "item-3") → the create
            // carries InsertIndex 0 → addView(view, 0): the NEW item must be
            // the list's FIRST child, with the old first item shifted to [1].
            assertTrue(
                "list never showed item-3 first within 10s of the Insert tap",
                pollListState(scenario, deadlineMs = 10_000) { texts ->
                    texts == listOf("item-3 (taps: 0)", "item-1 (taps: 0)", "item-2 (taps: 0)")
                }
            )
        }
    }

    // ── Remove-first: the first item's view leaves the screen ───────────────

    @Test
    fun remove_first_removes_from_screen() {
        launchProbe().use { scenario ->
            assertNotNull("boot failed", pollForRootContainer(scenario))

            tapButton(scenario, "Remove")

            // RemoveFirst() drops item-1 → one RemoveNode (JVM twin) → the
            // list shrinks to a single child and item-1's text is gone from
            // position 0 (item-2 promoted).
            assertTrue(
                "list never shrank to [item-2] within 10s of the Remove tap",
                pollListState(scenario, deadlineMs = 10_000) { texts ->
                    texts == listOf("item-2 (taps: 0)")
                }
            )
        }
    }

    // ── Child ItemComponent's own handler: its own state, its own text ──────

    @Test
    fun child_item_button_fires_own_handler() {
        launchProbe().use { scenario ->
            assertNotNull("boot failed", pollForRootContainer(scenario))

            // The badge ItemComponent's clickable view is its own root div
            // (ItemComponent attaches @onclick to the div, so AttachEvent
            // lands on the badge LinearLayout — root child [1]).
            scenario.onActivity { act ->
                rootContainer(act)!!.getChildAt(1).performClick()
            }

            // The child's OWN handler increments its OWN taps counter and its
            // re-render updates the badge text in place.
            assertTrue(
                "badge never showed 'badge (taps: 1)' within 10s of its own tap",
                pollForText(scenario, "badge (taps: 1)", deadlineMs = 10_000)
            )
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /** The CompositionProbe root container: widget_root's single child. */
    private fun rootContainer(act: MainActivity): LinearLayout? =
        act.findViewById<FrameLayout>(R.id.widget_root)
            ?.takeIf { it.childCount > 0 }
            ?.getChildAt(0) as? LinearLayout

    /** Polls until the mount frame has been applied: the root container
     * exists AND already carries the full 7-child composite (the batch is
     * atomic, so children appear all-or-nothing per frame — but the badge's
     * frame could in principle trail the parent's; poll to the full shape). */
    private fun pollForRootContainer(
        scenario: ActivityScenario<MainActivity>,
        deadlineMs: Long = 60_000,
    ): LinearLayout? {
        val deadline = System.currentTimeMillis() + deadlineMs
        val found = AtomicReference<LinearLayout?>(null)
        while (System.currentTimeMillis() < deadline) {
            scenario.onActivity { act ->
                found.set(rootContainer(act)?.takeIf { it.childCount >= 7 })
            }
            if (found.get() != null) break
            Thread.sleep(250)
        }
        return found.get()
    }

    /** Finds the Button whose text equals [label] and performClicks it on the
     * UI thread. Fails the test if the button is not on screen. */
    private fun tapButton(scenario: ActivityScenario<MainActivity>, label: String) {
        val clicked = AtomicReference(false)
        scenario.onActivity { act ->
            val root = act.findViewById<FrameLayout>(R.id.widget_root)
            val button = root?.let {
                firstMatch(it) { v -> v is Button && v.text.toString() == label }
            } as? Button
            if (button != null) {
                button.performClick()
                clicked.set(true)
            }
        }
        assertTrue("Button '$label' not found on screen", clicked.get())
    }

    /** Polls the list container (root child [3]) until its children's texts,
     * IN CHILD ORDER, satisfy [predicate]. */
    private fun pollListState(
        scenario: ActivityScenario<MainActivity>,
        deadlineMs: Long,
        predicate: (List<String>) -> Boolean,
    ): Boolean {
        val deadline = System.currentTimeMillis() + deadlineMs
        while (System.currentTimeMillis() < deadline) {
            val texts = AtomicReference<List<String>>(emptyList())
            scenario.onActivity { act ->
                val container = rootContainer(act) ?: return@onActivity
                if (container.childCount < 4) return@onActivity
                val list = container.getChildAt(3) as? ViewGroup ?: return@onActivity
                texts.set((0 until list.childCount).map { firstTextIn(list.getChildAt(it)) ?: "<no text>" })
            }
            if (predicate(texts.get())) return true
            Thread.sleep(250)
        }
        return false
    }

    /** Polls until any TextView in widget_root shows exactly [expected]. */
    private fun pollForText(
        scenario: ActivityScenario<MainActivity>,
        expected: String,
        deadlineMs: Long,
    ): Boolean {
        val deadline = System.currentTimeMillis() + deadlineMs
        while (System.currentTimeMillis() < deadline) {
            val found = AtomicReference(false)
            scenario.onActivity { act ->
                val root = act.findViewById<FrameLayout>(R.id.widget_root) ?: return@onActivity
                found.set(firstMatch(root) { v ->
                    v is TextView && v.text.toString() == expected
                } != null)
            }
            if (found.get()) return true
            Thread.sleep(250)
        }
        return false
    }

    /** Text of the first TextView in [view]'s subtree (depth-first), or null. */
    private fun firstTextIn(view: View): String? =
        (firstMatch(view) { it is TextView } as? TextView)?.text?.toString()

    /** One-line child summary for assertion messages. */
    private fun describeChildren(group: ViewGroup): String =
        (0 until group.childCount).joinToString(prefix = "[", postfix = "]") { i ->
            val v = group.getChildAt(i)
            "${v::class.simpleName}(${firstTextIn(v) ?: ""})"
        }

    /** Depth-first search of a view subtree (includes [view] itself). */
    private fun firstMatch(view: View, predicate: (View) -> Boolean): View? {
        if (predicate(view)) return view
        if (view is ViewGroup) {
            for (i in 0 until view.childCount) {
                firstMatch(view.getChildAt(i), predicate)?.let { return it }
            }
        }
        return null
    }
}
