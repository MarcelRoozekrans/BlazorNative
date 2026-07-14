package io.blazornative.shell

import android.content.Context
import android.view.View
import android.view.ViewGroup
import androidx.test.platform.app.InstrumentationRegistry
import io.blazornative.jni.RenderFrame
import io.blazornative.jni.RenderPatch
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import java.util.concurrent.atomic.AtomicReference

/**
 * Phase 6.1 — the instrumented suite's shared FRAME vocabulary.
 *
 * Every layout assertion in this package speaks two sentences: "this view's frame
 * is exactly this" and "these children stack". They were copy-pasted across three
 * test classes (and their `render` scaffolding across five); they live here now, so
 * a change to the density contract or the host-root type is made ONCE.
 *
 * **Frames are asserted in dp**, because Yoga computes in density-independent units
 * on BOTH platforms and the iOS XCTest asserts the same numbers (design §Units).
 * The 0.5dp tolerance is the whole-pixel frame-apply: the AVD's density is 2.625,
 * so 300dp is 787.5px and the answer is rounded.
 */

/** The target context's density — the ONE number that converts Yoga's dp to px. */
internal fun density(): Float =
    InstrumentationRegistry.getInstrumentation().targetContext.resources.displayMetrics.density

/** Asserts a View's computed frame, in dp, relative to its parent. */
internal fun assertFrame(what: String, v: View, x: Float, y: Float, w: Float, h: Float) {
    val d = density()
    assertEquals("$what.x", x, v.left / d, 0.5f)
    assertEquals("$what.y", y, v.top / d, 0.5f)
    assertEquals("$what.w", w, v.width / d, 0.5f)
    assertEquals("$what.h", h, v.height / d, 0.5f)
}

/**
 * The frame form of "this container is a vertical stack": every child shares the
 * container's CONTENT-BOX left edge, every child is non-empty, and each is butted
 * up against the previous one's bottom edge.
 *
 * The content-box edge is read from child [0] rather than pinned to 0, because a
 * container with `padding` insets its children — and after Phase 6.1 that inset is
 * the Yoga node's (children are laid out inside the padding box), so
 * `container.paddingLeft` is 0 and the inset lives in the children's frames.
 *
 * This is the pin that replaced `is LinearLayout` + `orientation == VERTICAL`: an
 * UN-STYLED tree must still stack, because Yoga's default flexDirection is column.
 */
internal fun assertStacksVertically(container: ViewGroup) {
    val contentLeft = container.getChildAt(0).left
    var expectedTop = container.getChildAt(0).top
    for (i in 0 until container.childCount) {
        val child = container.getChildAt(i)
        assertEquals("child $i must share the container's content-box left edge",
            contentLeft, child.left)
        assertTrue("child $i must have a real height (got ${child.height}px)", child.height > 0)
        assertEquals("child $i must start exactly where child ${i - 1} ended " +
            "— an un-styled tree is a Yoga COLUMN", expectedTop, child.top)
        expectedTop = child.bottom
    }
}

/**
 * **THE MEASURE ORACLE** — the assertion a FABRICATED measure function cannot pass.
 *
 * Every *relational* assertion about a measured leaf (`height > 0`, `lineCount > 1`,
 * "the row hugs the label", "the label fits the row") also passes when the measure
 * function returns a CONSTANT — the 6.0 spike's 80×20 stub satisfies all four. They
 * pin the plumbing; none of them pins the MEASUREMENT.
 *
 * This does. It asks the SAME widget class, with the SAME text, size and typeface,
 * the SAME question the measure function asks — `measure(AT_MOST(available),
 * UNSPECIFIED)`, which is exactly what [YogaLayout]'s measure func hands the view
 * for a leaf in a row of known width and unconstrained height — and demands the
 * LAID-OUT frame equal the answer. No font metric is written down anywhere, so it
 * stays honest on any device and any font; but the measurement can no longer be
 * invented.
 *
 * 1px tolerance: the frame is a dp round-trip through the density (px → dp in the
 * measure func, dp → px at frame-apply), which is lossless in principle and
 * float-rounding in practice. A fabricated measurement misses by tens of pixels.
 *
 * Gate 3 mirrors this with a throwaway `UILabel`/`UIButton` and `sizeThatFits`.
 */
internal fun assertOracle(what: String, v: android.widget.TextView, availableWidthPx: Int) {
    val ctx = v.context
    val oracle: android.widget.TextView = when (v) {
        is android.widget.Button -> android.widget.Button(ctx)
        is android.widget.EditText -> android.widget.EditText(ctx)
        else -> android.widget.TextView(ctx)
    }
    oracle.text = v.text
    oracle.setTextSize(android.util.TypedValue.COMPLEX_UNIT_PX, v.textSize)
    oracle.typeface = v.typeface
    oracle.measure(
        View.MeasureSpec.makeMeasureSpec(availableWidthPx, View.MeasureSpec.AT_MOST),
        View.MeasureSpec.makeMeasureSpec(0, View.MeasureSpec.UNSPECIFIED),
    )
    val why = "— a measure func that returned a CONSTANT would pass every relational " +
        "assertion in this file and fail THIS one"
    assertEquals("$what.w must equal what the native widget MEASURES to $why",
        oracle.measuredWidth.toFloat(), v.width.toFloat(), 1f)
    assertEquals("$what.h must equal what the native widget MEASURES to $why",
        oracle.measuredHeight.toFloat(), v.height.toFloat(), 1f)
}

internal fun create(nodeId: Int, nodeType: String, parentId: Int?, insertIndex: Int = -1) =
    RenderPatch.CreateNode(
        nodeId = nodeId, nodeType = nodeType, parentId = parentId, insertIndex = insertIndex)

internal fun style(nodeId: Int, property: String, value: String?) =
    RenderPatch.SetStyle(nodeId = nodeId, property = property, value = value)

internal fun text(nodeId: Int, text: String) =
    RenderPatch.ReplaceText(nodeId = nodeId, text = text)

/**
 * A detached host root + its [WidgetMapper], driven ONE FRAME AT A TIME.
 *
 * The root is a [BnYogaFrameLayout] — the PRODUCTION host container (`main.xml`'s
 * `widget_root` is one) — and not a stock FrameLayout, so these tests exercise the
 * container the Activity actually uses. It is given real bounds via [View.layout]
 * before any patch arrives: a detached ViewGroup has no size, and Yoga's available
 * space is the host's.
 *
 * [render] returns only after the batch has been applied on the main looper, so the
 * tree may be READ between frames — which is what the dirty-on-content-change tests
 * need (frame 1's height, then frame 2's).
 */
internal class SyntheticHost(widthDp: Float = 400f, heightDp: Float = 800f) {

    private val instr = InstrumentationRegistry.getInstrumentation()
    private val ctx: Context = instr.targetContext
    private var frameId = 0

    lateinit var root: BnYogaFrameLayout
        private set
    lateinit var mapper: WidgetMapper
        private set

    init {
        instr.runOnMainSync {
            val d = ctx.resources.displayMetrics.density
            root = BnYogaFrameLayout(ctx)
            root.layout(0, 0, (widthDp * d).toInt(), (heightDp * d).toInt())
            mapper = WidgetMapper(ctx, root)
        }
    }

    /** Applies one frame (the CommitFrame is appended) and waits for it to land. */
    fun render(patches: List<RenderPatch>) {
        instr.runOnMainSync {
            frameId++
            mapper.apply(RenderFrame(
                frameId = frameId, timestampMs = 0L,
                patches = patches + RenderPatch.CommitFrame(frameId, 0L),
            ))
        }
        instr.waitForIdleSync()
    }

    /** Re-lays the host root — the exact call the framework makes on a resize. */
    fun resize(widthDp: Float, heightDp: Float) {
        instr.runOnMainSync {
            val d = ctx.resources.displayMetrics.density
            root.layout(0, 0, (widthDp * d).toInt(), (heightDp * d).toInt())
        }
        instr.waitForIdleSync()
    }

    /** Reads the tree on the main thread (views are not thread-safe). */
    fun <T> read(block: () -> T): T {
        val out = AtomicReference<T>()
        instr.runOnMainSync { out.set(block()) }
        return out.get()
    }
}

/**
 * Drives a fresh [SyntheticHost] with one frame per patch list and returns the host
 * root — the shape almost every synthetic frame test wants.
 */
internal fun render(vararg frames: List<RenderPatch>): BnYogaFrameLayout {
    val host = SyntheticHost()
    frames.forEach { host.render(it) }
    assertTrue("no child created in root after apply", host.read { host.root.childCount } > 0)
    return host.root
}
