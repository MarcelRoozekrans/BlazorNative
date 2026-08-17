package io.blazornative.shell

import android.widget.EditText
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.filters.SmallTest
import io.blazornative.jni.RenderPatch
import org.junit.Assert.assertEquals
import org.junit.Test
import org.junit.runner.RunWith

// ─────────────────────────────────────────────────────────────────────────────
// WidgetMapperStaleEchoTest — the M3 ledger's "stale-echo/IME artifact under fast
// typing", finally pinned.
//
// THE BUG. Every keystroke dispatches its text to .NET, and .NET echoes each one
// back as UpdateProp("value"). The write-back used to compare only against the
// EditText's CURRENT text — so an echo carrying an OLDER value that lands after
// the user has typed further FAILS the inequality skip, overwrites the box, and
// jumps the caret to the end. Type "abc" quickly and you can land back on "ab".
//
// It sat in the ledger for eight milestones partly because it was filed as
// "sequence-stamping territory (frame/sequence ids on the wire)" — i.e. an
// ABI-shaped change nobody wanted to start. It is not. The shell already knows
// every value it dispatched, so it can distinguish:
//
//   · AN ECHO OF THE USER'S OWN TYPING — a value this shell sent. The box already
//     holds it or something newer, so applying it can only undo a later keystroke
//     or move the caret. DROP IT.
//   · A GENUINE PROGRAMMATIC SET — a value this shell never sent. "Blazor state is
//     truth": APPLY IT, even mid-word.
//
// iOS needs none of this and gets none: UIKit does not fire `.editingChanged` for
// a programmatic set, so the echo never re-enters the loop there. This is one of
// the few places the two shells legitimately differ, and the reason is written
// down rather than left as an asymmetry someone later "fixes".
//
// Written RED-FIRST: with the reconciliation removed, `fast_typing…` fails with
// the box reading "ab" instead of "abc" — the user's own keystroke undone.
// ─────────────────────────────────────────────────────────────────────────────

@RunWith(AndroidJUnit4::class)
@SmallTest
class WidgetMapperStaleEchoTest {

    private companion object {
        const val INPUT = 1
        const val HANDLER = 42
    }

    /** An input node with the `change` event wired, plus a recorder for what the
     * shell dispatches — the same shape the form-control loop-guard tests use. */
    private fun host(sent: MutableList<String>) = SyntheticHost(
        onUiEvent = { _, eventName, payload ->
            if (eventName == "change") sent.add(payload ?: "")
        },
    ).also {
        it.render(listOf(
            create(INPUT, "input", null),
            style(INPUT, "width", "200"),
            style(INPUT, "height", "40"),
            RenderPatch.AttachEvent(nodeId = INPUT, eventName = "change", handlerId = HANDLER),
        ))
    }

    /** Resolve the view ONCE, in one `read`. `SyntheticHost.read` is
     * `runOnMainSync`, and Android throws "This method can not be called from the
     * main application thread" if one is nested inside another — so every helper
     * below takes the already-resolved view rather than looking it up again. */
    private fun editTextOf(h: SyntheticHost): EditText =
        h.read { h.root.getChildAt(0) as EditText }

    /** One keystroke, through the REAL TextWatcher — `setText` from the main thread
     * is what a key event reduces to, and it is how the other input tests type. */
    private fun type(h: SyntheticHost, et: EditText, text: String) {
        h.read { et.setText(text) }
    }

    private fun textOf(h: SyntheticHost, et: EditText): String = h.read { et.text.toString() }

    @Test
    fun fast_typing_survives_a_stale_echo_landing_late() {
        val sent = mutableListOf<String>()
        val h = host(sent)

        // The user types three characters faster than .NET answers.
        val et = editTextOf(h)
        type(h, et, "a")
        type(h, et, "ab")
        type(h, et, "abc")
        assertEquals("every keystroke dispatched", listOf("a", "ab", "abc"), sent)

        // .NET's answer to the FIRST keystroke arrives now — two keystrokes late.
        h.render(listOf(prop(INPUT, "value", "a")))

        assertEquals(
            "THE PIN: a stale echo must not undo later keystrokes. The box holds 'abc'; " +
                "the echo carries 'a', which this shell dispatched, so it is the user's own " +
                "typing coming back — dropping it is safe by construction because the box " +
                "already contains that text or something newer. Applying it instead sets the " +
                "box back to 'a' and jumps the caret, silently, under the user's fingers.",
            "abc", textOf(h, et))
    }

    @Test
    fun the_echo_of_the_LATEST_keystroke_is_also_a_no_op() {
        val sent = mutableListOf<String>()
        val h = host(sent)

        val et = editTextOf(h)
        type(h, et, "hello")
        h.render(listOf(prop(INPUT, "value", "hello")))

        // Nothing to do — the box already agrees. The old code reached the same
        // outcome via the inequality skip; this asserts the NEW path keeps it, so
        // the fix cannot regress the common case into a needless setText (which
        // would move the caret on every single keystroke).
        assertEquals("hello", textOf(h, et))
        assertEquals("the echo was reconciled, not applied", 1, h.read { h.mapper.staleEchoesDropped })
    }

    @Test
    fun a_genuine_programmatic_set_still_wins_even_mid_typing() {
        val sent = mutableListOf<String>()
        val h = host(sent)

        val et = editTextOf(h)
        type(h, et, "dra")

        // The app sets Value itself — a value the user never typed and this shell
        // therefore never dispatched. "Blazor state is truth" is unchanged by the
        // fix: this must land, mid-word or not.
        h.render(listOf(prop(INPUT, "value", "draft-42")))

        assertEquals("draft-42", textOf(h, et))
    }

    @Test
    fun the_registry_drains_and_does_not_leak() {
        val sent = mutableListOf<String>()
        val h = host(sent)

        val et = editTextOf(h)
        type(h, et, "a")
        type(h, et, "ab")
        type(h, et, "abc")
        assertEquals("three keystrokes are outstanding", 3, h.read { h.mapper.pendingEchoCount })

        // One echo for the LAST value reconciles all three: the earlier two are
        // keystrokes it superseded, and keeping them would let a much later genuine
        // set that happens to equal an ancient keystroke be mistaken for an echo.
        h.render(listOf(prop(INPUT, "value", "abc")))

        assertEquals("the queue drained to empty", 0, h.read { h.mapper.pendingEchoCount })
    }

    @Test
    fun a_detached_node_drops_its_outstanding_echoes() {
        val sent = mutableListOf<String>()
        val h = host(sent)

        type(h, editTextOf(h), "abc")
        h.render(listOf(RenderPatch.DetachEvent(nodeId = INPUT, handlerId = HANDLER, eventName = "change")))

        // No watcher means no echoes are coming: holding them would be a leak that
        // grows with every mount/unmount cycle of a page with an input on it.
        assertEquals(0, h.read { h.mapper.pendingEchoCount })
    }
}
