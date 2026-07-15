package io.blazornative.shell

import android.content.Context
import android.view.View
import android.widget.Spinner

/**
 * Phase 7.3 — the `picker` widget: a [Spinner] that ANSWERS ITS OWN LAYOUT
 * REQUEST, because in this shell nobody else will.
 *
 * A stock Spinner's selection mechanics assume the framework responds to
 * `requestLayout()`: `AbsSpinner.setSelection(int)` only records
 * `mNextSelectedPosition` and requests layout — the selection is APPLIED (and
 * `onItemSelected` fired, via `Spinner.layout` → `checkSelectionChanged`)
 * inside the NEXT LAYOUT PASS. The dropdown's own item click takes exactly
 * this path, so it is not an exotic corner: it is how EVERY user pick lands.
 *
 * In this shell the framework never lays widgets out: every container is a
 * [BnYogaFrameLayout] whose `onLayout` is deliberately EMPTY (Phase 6.1 — Yoga
 * owns placement; the framework's pass would overwrite Yoga's frames), and
 * Yoga re-lays views only when a PATCH BATCH commits. A stock Spinner's user
 * pick therefore sat PENDING — visually stale, `onItemSelected` never fired,
 * nothing ever dispatched — until some unrelated batch happened to re-run the
 * layout. Found by `BnFormDemoAndroidTest`'s round-trip on the first device
 * run (three controls echoed; the picker never did).
 *
 * The fix is React Native's, for the same architecture and the same reason
 * (`ReactPicker.java`: "The spinner relies on a measure + layout pass
 * happening after it calls requestLayout()"): post a self measure+layout at
 * the CURRENT Yoga-applied frame. `requestLayout()` has set the force-layout
 * flag, so the `layout()` call runs `onLayout` even though the bounds have
 * not moved — which is what applies the selection and fires the listener.
 * The frame itself stays Yoga's: same left/top/right/bottom, re-asserted.
 *
 * iOS MUST NOT COPY THIS: `UIPickerView.selectRow` applies immediately and
 * fires nothing — this class exists purely because of Android's
 * layout-coupled selection delivery.
 */
internal class BnSpinner(context: Context) : Spinner(context) {

    /** One reusable runnable (the ReactPicker shape) — requestLayout can fire
     * several times per batch (adapter swap + setSelection), and each post is
     * a cheap re-measure at the same Yoga frame. */
    private val measureAndLayout = Runnable {
        measure(
            View.MeasureSpec.makeMeasureSpec(width, View.MeasureSpec.EXACTLY),
            View.MeasureSpec.makeMeasureSpec(height, View.MeasureSpec.EXACTLY),
        )
        layout(left, top, right, bottom)
    }

    override fun requestLayout() {
        super.requestLayout()
        // The self-answer. Posted, not inline: requestLayout is called from
        // inside AdapterView's own state transitions (adapter swap, selection
        // set), where a synchronous re-entrant layout is exactly the kind of
        // thing AdapterView guards against with mBlockLayoutRequests.
        post(measureAndLayout)
    }
}
