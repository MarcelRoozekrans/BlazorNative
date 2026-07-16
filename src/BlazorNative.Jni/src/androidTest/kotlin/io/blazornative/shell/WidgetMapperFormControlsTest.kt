package io.blazornative.shell

import android.view.ViewGroup
import android.widget.CheckBox
import android.widget.SeekBar
import android.widget.Spinner
import android.widget.Switch
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import io.blazornative.jni.RenderFrame
import io.blazornative.jni.RenderPatch
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

/**
 * Phase 7.3 Gate 2 Task 2.2 — the form controls' props, wires and, above all,
 * **THE PER-CONTROL LOOP GUARD**, tested per control because the design says
 * "verify each, never assume" — and the verification found the platform's
 * controls differ AMONG THEMSELVES:
 *
 *  - `CompoundButton.setChecked` and `ProgressBar.setProgress` fire their
 *    listeners SYNCHRONOUSLY → the `applyingBatch` guard (the 4.2 TextWatcher
 *    pattern) catches a patch-applied value echo. Each control's test drives
 *    the SAME programmatic set twice — inside a batch (must dispatch NOTHING)
 *    and outside one (must dispatch, with the exact wire payload) — so the
 *    guard is pinned as the ONLY thing separating the two.
 *  - `Spinner.setSelection` does NOT fire synchronously: AdapterView POSTS its
 *    SelectionNotifier (setSelectionInt runs its layout under
 *    mBlockLayoutRequests, so selectionChanged() always takes the post path),
 *    and the callback lands on a LATER main-loop turn, when `applyingBatch` is
 *    long false — the 4.2 flag CANNOT guard this control. The picker's guard
 *    is the EXPECTED-SELECTION comparison (recorded before every shell-side
 *    set), pinned by [a_selectedIndex_patch_fires_nothing_the_pickers_loop_guard].
 *
 * THE HOSTING SPLIT IS THE SAME FINDING, MECHANICALLY ENFORCED: a posted
 * SelectionNotifier on a DETACHED view goes to the run-queue that only drains
 * on window attach — i.e. NEVER, in a detached-root test, which would pass the
 * picker guard tests VACUOUSLY (no callback, no dispatch, nothing verified).
 * So the sync controls use the detached [SyntheticHost] (fast, the 2.6
 * pattern) and every picker wire test runs against a mapper whose root is
 * ATTACHED to a real Activity window ([withAttachedMapper]).
 *
 * The picker's NORMATIVE CLAMP RULE (BnPicker.razor's header) is asserted ON
 * THE WIRE (the dispatched payload IS the clamped index): .NET's benign
 * inbound clamp makes a non-clamping shell invisible from the .NET side, so
 * these dispatches are the only place the rule is provable.
 *
 * The REQUIRED MUTATIONS redden here: drop a control's `applyingBatch` line →
 * its fires-nothing test; drop the picker's expected-selection compare → its
 * fires-nothing test.
 */
@RunWith(AndroidJUnit4::class)
class WidgetMapperFormControlsTest {

    private companion object {
        const val NODE = 1
        const val HANDLER = 42
        /** BnFormDemoTests.ItemsJson — THE wire literal, transcribed exactly. */
        const val ITEMS = """["Alpha","Bravo","Charlie"]"""
    }

    private val instr = InstrumentationRegistry.getInstrumentation()

    /** Records every (handlerId, eventName, payload) the mapper dispatches. */
    private class Recorder {
        val events = mutableListOf<Triple<Int, String, String?>>()
        val dispatcher: (Int, String, String?) -> Unit = { h, n, p -> events += Triple(h, n, p) }
        val payloads: List<String?> get() = events.map { it.third }
    }

    private fun host(recorder: Recorder) = SyntheticHost(onUiEvent = recorder.dispatcher)

    /** One control with its change wire attached and [props] applied — ONE mount
     * batch, the exact shape the renderer emits for `/form`. */
    private fun mountPatches(nodeType: String, vararg props: Pair<String, String?>) = buildList {
        add(create(NODE, nodeType, null))
        add(RenderPatch.AttachEvent(NODE, "change", HANDLER))
        for ((name, value) in props) add(prop(NODE, name, value))
    }

    private fun <T> onMain(block: () -> T): T {
        var out: T? = null
        instr.runOnMainSync { out = block() }
        @Suppress("UNCHECKED_CAST")
        return out as T
    }

    // ── The ATTACHED host (see the class KDoc: pickers post their callbacks) ──

    /** A WidgetMapper whose root is attached to a real window — inside a live
     * MainActivity (whatever it boots is its own business; this mapper and its
     * recorder are separate instances rendering into a separate root). */
    private fun withAttachedMapper(
        rec: Recorder,
        block: (render: (List<RenderPatch>) -> Unit, root: ViewGroup) -> Unit,
    ) {
        ActivityScenario.launch(MainActivity::class.java).use { scenario ->
            lateinit var root: BnYogaFrameLayout
            lateinit var mapper: WidgetMapper
            scenario.onActivity { act ->
                val d = act.resources.displayMetrics.density
                root = BnYogaFrameLayout(act)
                mapper = WidgetMapper(act, root, onUiEvent = rec.dispatcher)
                act.findViewById<ViewGroup>(android.R.id.content).addView(
                    root,
                    ViewGroup.LayoutParams((400 * d).toInt(), (800 * d).toInt()))
                // Give the mapper's Yoga a sized root NOW — the framework's own
                // traversal re-lays it to the same box a frame later.
                root.layout(0, 0, (400 * d).toInt(), (800 * d).toInt())
            }
            instr.waitForIdleSync()
            var frameId = 0
            val render: (List<RenderPatch>) -> Unit = { patches ->
                instr.runOnMainSync {
                    frameId++
                    mapper.apply(RenderFrame(
                        frameId = frameId, timestampMs = 0L,
                        patches = patches + RenderPatch.CommitFrame(frameId, 0L)))
                }
                // Two waves to drain: the mapper's posted batch, then the
                // Spinner's posted SelectionNotifier.
                instr.waitForIdleSync()
                instr.waitForIdleSync()
            }
            block(render, root)
        }
    }

    private fun spinnerOf(root: ViewGroup): Spinner = onMain { root.getChildAt(0) as Spinner }

    // ── The value prop, per control ───────────────────────────────────────────

    @Test
    fun checkbox_value_prop_drives_the_checked_state_and_garbage_is_ignored() {
        val rec = Recorder()
        val h = host(rec)
        h.render(mountPatches("checkbox", "value" to "true"))
        val cb = h.read { h.root.getChildAt(0) as CheckBox }
        assertTrue("value \"true\" must check the box", h.read { cb.isChecked })

        h.render(listOf(prop(NODE, "value", "false")))
        assertFalse("value \"false\" must uncheck it", h.read { cb.isChecked })

        // The wire grammar is EXACTLY "true"/"false" (ordinal — BnCheckbox's
        // header): "True" is garbage, logged and ignored, state unchanged.
        h.render(listOf(prop(NODE, "value", "True")))
        assertFalse("\"True\" is not the wire grammar — the state must not move",
            h.read { cb.isChecked })
    }

    @Test
    fun slider_props_map_by_the_precision_contract_stepped() {
        val h = host(Recorder())
        // The demo's bound slider: 25 / 0..100 step 5 → ONE progress unit IS
        // ONE step: max = 20, progress = 5.
        h.render(mountPatches("slider",
            "value" to "25", "min" to "0", "max" to "100", "step" to "5"))
        val bar = h.read { h.root.getChildAt(0) as SeekBar }
        assertEquals("stepped: max = round((100-0)/5)", 20, h.read { bar.max })
        assertEquals("stepped: progress = round((25-0)/5)", 5, h.read { bar.progress })
    }

    @Test
    fun slider_without_step_is_continuous_range_over_1000() {
        val h = host(Recorder())
        // The demo's disabled slider shape (continuous): the range is quantized
        // into 1000 units — precision (max-min)/1000 = 0.1 here.
        h.render(mountPatches("slider", "value" to "62.5", "min" to "0", "max" to "100"))
        val bar = h.read { h.root.getChildAt(0) as SeekBar }
        assertEquals("continuous: max = 1000", 1000, h.read { bar.max })
        assertEquals("continuous: progress = round(62.5/0.1)", 625, h.read { bar.progress })
    }

    @Test
    fun slider_range_and_step_rewrites_rederive_the_geometry() {
        val rec = Recorder()
        val h = host(rec)
        h.render(mountPatches("slider",
            "value" to "25", "min" to "0", "max" to "100", "step" to "5"))
        val bar = h.read { h.root.getChildAt(0) as SeekBar }

        // Shrink the range: the state holds the RAW wire floats, so the
        // geometry re-derives from the whole state (order-independent).
        h.render(listOf(prop(NODE, "max", "50")))
        assertEquals("max 50 step 5 → 10 units", 10, h.read { bar.max })
        assertEquals("value 25 → progress 5", 5, h.read { bar.progress })

        // step → null resets to continuous (the un-styled invariant).
        h.render(listOf(prop(NODE, "step", null)))
        assertEquals("continuous again: 1000 units", 1000, h.read { bar.max })
        assertEquals("value 25 of 0..50 → 500", 500, h.read { bar.progress })

        // And none of those programmatic moves dispatched anything.
        assertEquals("prop-driven geometry changes are not user input",
            emptyList<String?>(), rec.payloads)
    }

    // ── The loop guard: checkbox / switch / slider (the applyingBatch pattern) ──

    @Test
    fun checkbox_programmatic_set_fires_nothing_in_a_batch_and_fires_outside_one() {
        val rec = Recorder()
        val h = host(rec)
        // The mount batch itself sets value "true" AFTER the change attach —
        // setChecked fires the listener SYNCHRONOUSLY (verified: this half is
        // RED without the guard), inside the batch → swallowed.
        h.render(mountPatches("checkbox", "value" to "true"))
        assertEquals("a patch-applied value echo must dispatch NOTHING (the applyingBatch " +
            "guard — the 4.2 lesson, per control)", emptyList<String?>(), rec.payloads)

        // The SAME programmatic set outside a batch is the user-input stand-in
        // (a tap ends at setChecked too) — it must dispatch, with the wire
        // grammar's payload.
        val cb = h.read { h.root.getChildAt(0) as CheckBox }
        onMain { cb.isChecked = false }
        instr.waitForIdleSync()
        assertEquals("the listener DOES fire on programmatic setChecked — the guard was " +
            "the only thing standing between", listOf<String?>("false"), rec.payloads)
        assertEquals(HANDLER, rec.events.single().first)
        assertEquals("change", rec.events.single().second)
    }

    @Test
    fun switch_programmatic_set_fires_nothing_in_a_batch_and_fires_outside_one() {
        val rec = Recorder()
        val h = host(rec)
        h.render(mountPatches("switch", "value" to "true"))
        assertEquals("same guard, same finding, verified per control (never assumed)",
            emptyList<String?>(), rec.payloads)

        val sw = h.read { h.root.getChildAt(0) as Switch }
        onMain { sw.isChecked = false }
        instr.waitForIdleSync()
        assertEquals(listOf<String?>("false"), rec.payloads)
    }

    @Test
    fun slider_programmatic_set_fires_nothing_in_a_batch_and_fires_outside_one() {
        val rec = Recorder()
        val h = host(rec)
        h.render(mountPatches("slider",
            "value" to "25", "min" to "0", "max" to "100", "step" to "5"))
        // setMax + setProgress both fired the listener inside the batch
        // (ProgressBar refreshes synchronously on the main thread — verified);
        // the guard swallowed every one.
        assertEquals(emptyList<String?>(), rec.payloads)

        // Outside a batch: progress 12 → value = 0 + 12×5 = 60, and the payload
        // is the INVARIANT float BnSlider's strict parse expects (Float.toString
        // never localizes — "60.0", never "60,0", on a Dutch device).
        val bar = h.read { h.root.getChildAt(0) as SeekBar }
        onMain { bar.progress = 12 }
        instr.waitForIdleSync()
        assertEquals("the payload is min + progress×step, invariant",
            listOf<String?>("60.0"), rec.payloads)
    }

    // ── The picker: bind, ITS loop guard, and THE CLAMP RULE (attached host) ──

    @Test
    fun picker_mount_binds_the_adapter_selects_and_dispatches_nothing() {
        val rec = Recorder()
        withAttachedMapper(rec) { render, root ->
            render(mountPatches("picker", "items" to ITEMS, "selectedIndex" to "1"))
            val spinner = spinnerOf(root)

            assertEquals("the items literal round-trips the STRICT parser into the adapter",
                3, onMain { spinner.adapter.count })
            assertEquals("Alpha", onMain { spinner.adapter.getItem(0) })
            assertEquals("Charlie", onMain { spinner.adapter.getItem(2) })
            assertEquals("the declared selection", 1, onMain { spinner.selectedItemPosition })

            // The mount positioned the selection (adapter bind + setSelection)
            // and the POSTED onItemSelected echo of the shell's own set has run
            // by now (attached root — see the class KDoc). The expected-selection
            // comparison is the only thing that dropped it.
            assertEquals("mount dispatches NOTHING — including the async selection echo",
                emptyList<String?>(), rec.payloads)
        }
    }

    @Test
    fun a_selectedIndex_patch_fires_nothing_the_pickers_loop_guard() {
        val rec = Recorder()
        withAttachedMapper(rec) { render, root ->
            render(mountPatches("picker", "items" to ITEMS, "selectedIndex" to "0"))
            val spinner = spinnerOf(root)

            // A later batch moves the selection programmatically (the bound-state
            // echo shape). Spinner's callback for it fires on a LATER main-loop
            // turn — applyingBatch is false there; the expected-selection guard
            // is what drops it. THE REQUIRED MUTATION reddens here: remove the
            // comparison in the Spinner arm and this dispatches "2".
            render(listOf(prop(NODE, "selectedIndex", "2")))
            assertEquals(2, onMain { spinner.selectedItemPosition })
            assertEquals("a programmatic selection set must re-fire NOTHING",
                emptyList<String?>(), rec.payloads)
        }
    }

    @Test
    fun a_user_selection_dispatches_the_new_index_exactly_once() {
        val rec = Recorder()
        withAttachedMapper(rec) { render, root ->
            render(mountPatches("picker", "items" to ITEMS, "selectedIndex" to "0"))
            val spinner = spinnerOf(root)

            // A user pick lands on setSelection through the dropdown's item
            // click — this IS the user-input path for a Spinner, driven outside
            // any batch.
            onMain { spinner.setSelection(2) }
            instr.waitForIdleSync()
            instr.waitForIdleSync()
            assertEquals("the payload is the new index as an invariant int",
                listOf<String?>("2"), rec.payloads)
            assertEquals(HANDLER, rec.events.single().first)
            assertEquals("change", rec.events.single().second)

            // Re-selecting the SAME position is not a change — nothing is added.
            onMain { spinner.setSelection(2) }
            instr.waitForIdleSync()
            assertEquals(listOf<String?>("2"), rec.payloads)
        }
    }

    @Test
    fun an_item_shrink_below_the_selection_clamps_to_the_LAST_item_and_notifies_the_wire() {
        val rec = Recorder()
        withAttachedMapper(rec) { render, root ->
            render(mountPatches("picker", "items" to ITEMS, "selectedIndex" to "2"))
            val spinner = spinnerOf(root)
            assertEquals(2, onMain { spinner.selectedItemPosition })

            // Items shrink 3 → 2 with the selection on index 2: THE NORMATIVE
            // CLAMP (BnPicker.razor's header) — clamp TO THE LAST item, and
            // NOTIFY the CLAMPED index on the change wire. Asserted ON THE WIRE
            // deliberately: .NET's benign inbound clamp makes a non-clamping
            // shell invisible from the .NET side; this payload is the only
            // place the rule is provable.
            render(listOf(prop(NODE, "items", """["Alpha","Bravo"]""")))
            assertEquals("the adapter re-bound", 2, onMain { spinner.adapter.count })
            assertEquals("the selection clamped to the LAST item", 1,
                onMain { spinner.selectedItemPosition })
            assertEquals("…and the shell NOTIFIED the clamped index — the bound .NET " +
                "state re-syncs to what the native widget actually shows",
                listOf<String?>("1"), rec.payloads)
        }
    }

    @Test
    fun items_emptied_clamps_to_minus_one_and_notifies() {
        val rec = Recorder()
        withAttachedMapper(rec) { render, root ->
            render(mountPatches("picker", "items" to ITEMS, "selectedIndex" to "1"))

            // Empty items → −1 (the only state an empty picker has), notified:
            // the live selection was displaced (the clamp rule's empty arm).
            render(listOf(prop(NODE, "items", "[]")))
            val spinner = spinnerOf(root)
            assertEquals(0, onMain { spinner.adapter.count })
            assertEquals(listOf<String?>("-1"), rec.payloads)
        }
    }

    @Test
    fun an_in_range_selection_is_PRESERVED_across_an_items_change_no_notify() {
        val rec = Recorder()
        withAttachedMapper(rec) { render, root ->
            render(mountPatches("picker", "items" to ITEMS, "selectedIndex" to "1"))
            val spinner = spinnerOf(root)

            // Same size, new content: re-bind the adapter, PRESERVE the
            // selection (the rule's other half) — the clamp did not move it,
            // so no notify (the loop guard is untouched).
            render(listOf(prop(NODE, "items", """["Delta","Echo","Foxtrot"]""")))
            assertEquals("Echo", onMain { spinner.adapter.getItem(1) })
            assertEquals(1, onMain { spinner.selectedItemPosition })
            assertEquals("an unmoved selection notifies NOTHING",
                emptyList<String?>(), rec.payloads)
        }
    }

    @Test
    fun malformed_items_render_an_EMPTY_picker_never_a_wrong_one() {
        val rec = Recorder()
        withAttachedMapper(rec) { render, root ->
            render(mountPatches("picker", "items" to ITEMS, "selectedIndex" to "1"))
            val spinner = spinnerOf(root)
            assertEquals(3, onMain { spinner.adapter.count })

            // A normative malformed vector (whitespace between tokens — the
            // strict grammar has none; ItemsJsonTest owns the full rejection
            // matrix on the JVM lane). The shell logs LOUDLY and renders EMPTY
            // — malformed data never becomes a plausible-looking wrong picker.
            render(listOf(prop(NODE, "items", """[ "a"]""")))
            assertEquals("malformed → EMPTY", 0, onMain { spinner.adapter.count })
            // …and the displaced live selection followed the empty-items clamp arm.
            assertEquals(listOf<String?>("-1"), rec.payloads)
        }
    }

    // ── Detach + enabled ─────────────────────────────────────────────────────

    @Test
    fun detach_change_silences_every_control() {
        val rec = Recorder()
        withAttachedMapper(rec) { render, root ->
            render(buildList {
                add(create(1, "checkbox", null))
                add(create(2, "switch", null))
                add(create(3, "slider", null))
                add(create(4, "picker", null))
                add(RenderPatch.AttachEvent(1, "change", 11))
                add(RenderPatch.AttachEvent(2, "change", 12))
                add(RenderPatch.AttachEvent(3, "change", 13))
                add(RenderPatch.AttachEvent(4, "change", 14))
                add(prop(3, "min", "0")); add(prop(3, "max", "100"))
                add(prop(4, "items", ITEMS)); add(prop(4, "selectedIndex", "0"))
            })
            render(listOf(
                RenderPatch.DetachEvent(1, 11, "change"),
                RenderPatch.DetachEvent(2, 12, "change"),
                RenderPatch.DetachEvent(3, 13, "change"),
                RenderPatch.DetachEvent(4, 14, "change"),
            ))
            onMain {
                (root.getChildAt(0) as CheckBox).isChecked = true
                (root.getChildAt(1) as Switch).isChecked = true
                (root.getChildAt(2) as SeekBar).progress = 50
                (root.getChildAt(3) as Spinner).setSelection(2)
            }
            instr.waitForIdleSync()
            instr.waitForIdleSync()
            assertEquals("a detached wire dispatches nothing — the detach arm mirrors " +
                "the attach arm's switch (the 3.3 symmetric-arms rule)",
                emptyList<String?>(), rec.payloads)
        }
    }

    @Test
    fun enabled_false_disables_each_form_control() {
        val h = host(Recorder())
        h.render(buildList {
            add(create(1, "checkbox", null)); add(prop(1, "enabled", "false"))
            add(create(2, "switch", null)); add(prop(2, "enabled", "false"))
            add(create(3, "slider", null)); add(prop(3, "enabled", "false"))
            add(create(4, "picker", null)); add(prop(4, "enabled", "false"))
        })
        for (i in 0 until 4) {
            assertFalse("child $i must render disabled (the device half — a disabled " +
                "widget's touch path dispatches nothing — is BnFormDemoAndroidTest's)",
                h.read { h.root.getChildAt(i).isEnabled })
        }
    }
}
