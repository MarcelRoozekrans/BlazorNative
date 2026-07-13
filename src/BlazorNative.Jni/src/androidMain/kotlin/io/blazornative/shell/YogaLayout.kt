package io.blazornative.shell

import android.content.Context
import android.util.Log
import android.view.View
import android.view.View.MeasureSpec
import android.view.ViewGroup
import com.facebook.soloader.SoLoader
import com.facebook.yoga.YogaAlign
import com.facebook.yoga.YogaConfigFactory
import com.facebook.yoga.YogaConstants
import com.facebook.yoga.YogaDirection
import com.facebook.yoga.YogaEdge
import com.facebook.yoga.YogaFlexDirection
import com.facebook.yoga.YogaGutter
import com.facebook.yoga.YogaJustify
import com.facebook.yoga.YogaMeasureMode
import com.facebook.yoga.YogaMeasureOutput
import com.facebook.yoga.YogaNode
import com.facebook.yoga.YogaNodeFactory
import com.facebook.yoga.YogaPositionType
import com.facebook.yoga.YogaWrap
import kotlin.math.roundToInt

/**
 * The container every `view` node becomes: a [android.widget.FrameLayout] whose
 * children are ABSOLUTELY PLACED by Yoga — the "plain FrameLayout" of the design,
 * with the one override that makes "plain" true.
 *
 * A stock FrameLayout is not inert: its `onLayout` re-places every child by
 * gravity + measured size, and its `onMeasure` re-measures them — both of which
 * run on the framework's own layout pass, AFTER [YogaLayout.calculateAndApply]
 * has already called `view.layout(...)`. It would silently overwrite every
 * computed frame (and it would do so only in the real Activity, where a layout
 * pass actually happens — never in a detached-root test). So the framework's
 * layout is suppressed and Yoga's is the only one: exactly what React Native's
 * `ReactViewGroup` does, and for exactly this reason.
 *
 * It is still a `FrameLayout` in every sense that matters to a caller — z-ordered
 * overlapping children, no stacking, no orientation.
 */
internal class BnYogaFrameLayout(context: Context) : android.widget.FrameLayout(context) {

    /** Yoga sized this node; adopt the spec (which [YogaLayout.applyFrame] built
     * from the computed frame) instead of re-measuring children behind its back. */
    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        setMeasuredDimension(
            getDefaultSize(0, widthMeasureSpec),
            getDefaultSize(0, heightMeasureSpec),
        )
    }

    /** Deliberately empty: the children's frames are Yoga's, applied directly. */
    override fun onLayout(changed: Boolean, left: Int, top: Int, right: Int, bottom: Int) = Unit
}

/**
 * Phase 6.1 (M6 DoD #2/#3) — **the layout engine**. Yoga stops being the 6.0
 * probe and becomes the thing that places every view.
 *
 * One Yoga node per VIEW node, mirrored against [WidgetMapper]'s view tree:
 * create/insert/remove keep the two trees index-for-index; `SetStyle` sets a Yoga
 * style property; `CommitFrame` runs ONE `calculateLayout` and applies every
 * computed frame with `view.layout(...)`. Containers are plain
 * [android.widget.FrameLayout]s — nothing stacks anything any more, Yoga places
 * it.
 *
 * ## The invariants this class exists to hold
 *
 * **The Yoga tree mirrors the VIEW tree, not the patch tree.** A `text` node
 * whose parent is a text-bearing non-container (TextView/Button/EditText) is
 * COLLAPSED onto that parent by [WidgetMapper.handleCreate] and gets no view —
 * so it must get no Yoga node either, or the two trees' child indices skew and
 * every frame after it is wrong. The mapper enforces it by simply not calling
 * [createNode] for a collapsed node.
 *
 * **The measure function attaches BY NODETYPE** ([MEASURED_NODE_TYPES]:
 * text/button/input/image) — never by "this node has no children". They are
 * different sets: BnLayoutDemo's row is three CHILDLESS `view`s, and a measure
 * func on them would let a FrameLayout's intrinsic size (0×0) speak over Yoga,
 * destroying the `flexGrow:1` box's computed width. A childless container is a
 * container.
 *
 * **Yoga computes in density-independent units.** dp in, dp out; the ONE place
 * `density` enters is [applyFrame] (and its mirror, the px MeasureSpec inside the
 * measure func). Style values arrive as dp by contract. That is what lets the
 * iOS shell — where points ARE dp — assert the exact same numbers.
 *
 * **Nothing sets `alignContent`.** Yoga's default is `flex-start`, which DEVIATES
 * from CSS's `stretch`, and the wrap demo's second line sits at the line-1 cross
 * size BECAUSE of it. It is not on the allow-list; "correcting" Yoga toward CSS
 * here would silently move a frame no patch could catch.
 *
 * ## The style grammar
 *
 * Implemented from the ONE normative statement — `docs/plans/2026-07-13-phase-6.1-design.md`
 * §"Style value grammar (normative)". It is not restated here: three copies of a
 * grammar is how two hand-written parsers (this one and iOS's `BnYogaLayout.mm`)
 * drift. Anything the grammar does not accept is **logged and ignored**, never
 * guessed.
 *
 * ## The honest boundary: `scroll` and `picker`
 *
 * A `view` becomes a [BnYogaFrameLayout], which does not lay out its own children
 * — so Yoga's frames survive. `scroll` (ScrollView) and `picker` (Spinner) are
 * FRAMEWORK ViewGroups that run their own layout, and they will overwrite the
 * frames Yoga computed for their children. Their nodes still take part in the
 * Yoga tree (so a ScrollView is itself placed correctly by its parent); it is
 * only what is INSIDE them that Yoga does not get the final word on. Out of scope
 * here by design — 6.2 owns scroll.
 *
 * Threading: main-thread only. Every entry point is called from inside
 * [WidgetMapper.applyBatch] (already posted to the main looper) or from the host
 * root's layout listener.
 */
class YogaLayout(private val context: Context, private val root: ViewGroup) {

    /** nodeId → Yoga node. Collapsed text nodes are absent BY DESIGN (see KDoc). */
    private val nodes = mutableMapOf<Int, YogaNode>()

    /** Yoga node → the View it places. Populated for every node in [nodes]; also
     * the reverse lookup [markDirty] needs, because a collapsed text node's
     * ReplaceText must dirty its PARENT's (the Button's) measure cache. */
    private val views = mutableMapOf<YogaNode, View>()

    /** The subset of [nodes] carrying a measure function. `YogaNode.dirty()`
     * THROWS on a node without one, so [markDirty] checks membership first. */
    private val measured = mutableSetOf<YogaNode>()

    // MUST precede [config]/[hostRoot]: Kotlin runs initializers in DECLARATION
    // order, and YogaNodeFactory.create() links Yoga's JNI core through SoLoader —
    // which throws "SoLoader.init() not yet called" if it has not been.
    init {
        ensureSoLoader(context)
    }

    /**
     * Every node in this tree shares one config, and its whole job is to turn
     * Yoga's OWN pixel-grid rounding **off** (`pointScaleFactor = 0`).
     *
     * Yoga defaults it to 1, meaning it rounds every computed frame to a whole
     * POINT — and, worse for us, it rounds them by two different rules: a node
     * with a measure function has its size **CEILED** (Yoga's "text rounding", so
     * a fractional measurement can never clip a glyph) while its siblings' offsets
     * are **ROUNDED**. Adjacent frames then stop tiling: on the AVD a measured
     * label came out 1dp taller than the container hugging it, and the next
     * sibling started 1dp (3px) above where the previous one ended.
     *
     * The design's rule is that Yoga computes in density-independent units with
     * exactly ONE conversion site ([applyFrame]). Turning Yoga's rounding off is
     * what makes that literally true: Yoga's output stays exact and fractional,
     * and ALL pixel snapping happens in one place, on absolute edges, where
     * adjacent frames tile by construction. It is also lossless for a measured
     * leaf — Android measures in whole pixels, so px → dp → px round-trips exactly
     * and nothing is ever clipped.
     *
     * (The alternative, `pointScaleFactor = density`, would move rounding INTO
     * Yoga and make the layout dp-values device-dependent — precisely the thing
     * that must not differ between the two platforms.)
     */
    private val config = YogaConfigFactory.create().apply { setPointScaleFactor(0f) }

    /** The synthetic host root: not a patch node, it IS the host ViewGroup.
     * Its children mirror [root]'s children (the top-level nodes). Direction is
     * set EXPLICITLY to LTR — a platform default is exactly the kind of thing the
     * two shells could silently disagree on. */
    private val hostRoot: YogaNode = YogaNodeFactory.create(config).apply {
        setDirection(YogaDirection.LTR)
    }

    private val density: Float get() = context.resources.displayMetrics.density

    init {
        // Task 2.3 — RELAYOUT ON HOST RESIZE. A rotation / split-screen / any
        // resize changes the available space Yoga solved against, so it must
        // re-solve. No patch is involved: .NET never learns the host got wider,
        // and nothing in the render tree changed — this is a pure host event.
        //
        // Guarded on a genuine bounds CHANGE: the listener also fires on layout
        // passes that did not move the host (a child's requestLayout re-runs the
        // traversal), and re-solving the whole tree on each of those is work for
        // an identical answer. The framework calls this AFTER the host's own
        // onLayout, so nothing it did is left standing.
        root.addOnLayoutChangeListener { _, left, top, right, bottom, oldL, oldT, oldR, oldB ->
            val resized = (right - left) != (oldR - oldL) || (bottom - top) != (oldB - oldT)
            if (resized && nodes.isNotEmpty()) calculateAndApply()
        }
    }

    // ── Tree mirroring ───────────────────────────────────────────────────────

    /**
     * Creates the Yoga node for [nodeId] and inserts it under [parentId]'s node
     * (or the synthetic host root when the view went to the host root) at exactly
     * the index the VIEW went to — `CreateNode.insertIndex`, −1 = append.
     *
     * [nodeType] decides the measure function and nothing else: the size of a
     * `text`/`button`/`input`/`image` is the NATIVE widget's business (DoD #3);
     * everything else is a container Yoga sizes itself.
     */
    fun createNode(nodeId: Int, nodeType: String, view: View, parentId: Int?, insertIndex: Int) {
        val node = YogaNodeFactory.create(config)
        nodes[nodeId] = node
        views[node] = view
        if (nodeType in MEASURED_NODE_TYPES) {
            node.setMeasureFunction { _, width, widthMode, height, heightMode ->
                val d = density
                view.measure(measureSpec(width, widthMode, d), measureSpec(height, heightMode, d))
                YogaMeasureOutput.make(view.measuredWidth / d, view.measuredHeight / d)
            }
            measured.add(node)
        }
        val parent = parentId?.let { nodes[it] } ?: hostRoot
        val index = if (insertIndex in 0..parent.childCount) insertIndex else parent.childCount
        parent.addChildAt(node, index)
    }

    /** Detaches [nodeId]'s node (with its whole subtree) from the tree. The
     * subtree's map entries are left behind exactly as [WidgetMapper] leaves its
     * own — unreachable from [hostRoot], they are never laid out again. */
    fun removeNode(nodeId: Int) {
        val node = nodes.remove(nodeId) ?: return
        views.remove(node)
        measured.remove(node)
        val owner = node.owner ?: return
        for (i in 0 until owner.childCount) {
            if (owner.getChildAt(i) === node) {
                owner.removeChildAt(i)
                return
            }
        }
    }

    /**
     * Invalidates the measure cache of the node that PLACES [view]. Yoga caches a
     * measure function's result and will not re-run it unless the node is marked
     * dirty — so a ReplaceText / value / fontSize change that alters a widget's
     * intrinsic size must land here or the next layout reuses the stale size.
     *
     * Keyed by VIEW, not nodeId, because the text collapse aliases a text node's
     * id onto its parent Button/EditText: the id has no Yoga node, the view does.
     */
    fun markDirty(view: View) {
        val node = views.entries.firstOrNull { it.value === view }?.key ?: return
        if (node in measured && !node.isDirty) node.dirty()
    }

    // ── The layout pass ──────────────────────────────────────────────────────

    /**
     * ONE `calculateLayout` over the whole tree, then a walk that applies every
     * computed frame. Called at `CommitFrame` and on a host resize.
     *
     * Available space is the HOST's, in dp. A host with no bounds yet (a detached
     * root — every synthetic-frame test, and the very first commit if it beats the
     * first layout pass) sizes to content instead of to zero: `auto`, not 0.
     */
    fun calculateAndApply() {
        val d = density
        val widthPx = root.width
        val heightPx = root.height
        if (widthPx > 0) hostRoot.setWidth(widthPx / d) else hostRoot.setWidthAuto()
        if (heightPx > 0) hostRoot.setHeight(heightPx / d) else hostRoot.setHeightAuto()

        hostRoot.calculateLayout(YogaConstants.UNDEFINED, YogaConstants.UNDEFINED)

        for (i in 0 until hostRoot.childCount) applyFrames(hostRoot.getChildAt(i), 0f, 0f, d)
    }

    /**
     * Walks the tree carrying each node's ABSOLUTE position in dp, because that
     * is what the dp→px conversion has to be done on. See [applyFrame].
     */
    private fun applyFrames(node: YogaNode, parentX: Float, parentY: Float, d: Float) {
        val x = parentX + node.layoutX
        val y = parentY + node.layoutY
        views[node]?.let { applyFrame(node, it, x, y, parentX, parentY, d) }
        for (i in 0 until node.childCount) applyFrames(node.getChildAt(i), x, y, d)
    }

    /**
     * THE conversion site (non-negotiable: there is exactly one). Yoga's frame is
     * dp and relative to its parent — which is precisely `view.layout`'s contract
     * once the parent is a container that does not place its own children.
     *
     * **Every EDGE is rounded in absolute space, and the size is the difference of
     * two rounded edges** — never `round(position) + round(size)`. On a device
     * whose density is not a whole number (the Pixel 6 AVD is 2.625) those two are
     * not the same number: a child at y=20dp h=19dp lands at
     * `round(52.5) + round(49.875) = 53 + 50 = 103`px, while its sibling at y=39dp
     * starts at `round(102.375) = 102`px — a ONE-PIXEL GAP that compounds down a
     * long column and shows up as siblings that no longer tile. Rounding the
     * absolute edges makes adjacent frames share the pixel by construction, and it
     * is exactly how Yoga's own YGRoundToPixelGrid derives a size.
     *
     * The `measure(EXACTLY, EXACTLY)` before the `layout` is not ceremony: a View
     * that was never measured has no internal layout (a TextView would have to
     * assume one at draw time), and widgets read `measuredWidth/Height` in
     * `onLayout`. React Native's NativeViewHierarchyManager does the same two
     * calls for the same reason.
     */
    private fun applyFrame(
        node: YogaNode,
        view: View,
        absX: Float,
        absY: Float,
        parentX: Float,
        parentY: Float,
        d: Float,
    ) {
        // The parent's own rounded absolute origin — the frame below is expressed
        // relative to it, which is what View.layout() wants.
        val originX = (parentX * d).roundToInt()
        val originY = (parentY * d).roundToInt()
        val left = (absX * d).roundToInt() - originX
        val top = (absY * d).roundToInt() - originY
        val right = ((absX + node.layoutWidth) * d).roundToInt() - originX
        val bottom = ((absY + node.layoutHeight) * d).roundToInt() - originY
        view.measure(
            MeasureSpec.makeMeasureSpec(right - left, MeasureSpec.EXACTLY),
            MeasureSpec.makeMeasureSpec(bottom - top, MeasureSpec.EXACTLY),
        )
        view.layout(left, top, right, bottom)
    }

    /** Yoga's measure mode → Android's MeasureSpec mode, with the dp→px hop.
     * `UNDEFINED` (Yoga has no constraint at all) is Android's `UNSPECIFIED`. */
    private fun measureSpec(size: Float, mode: YogaMeasureMode, d: Float): Int = when (mode) {
        YogaMeasureMode.EXACTLY ->
            MeasureSpec.makeMeasureSpec((size * d).roundToInt(), MeasureSpec.EXACTLY)
        YogaMeasureMode.AT_MOST ->
            MeasureSpec.makeMeasureSpec((size * d).roundToInt(), MeasureSpec.AT_MOST)
        YogaMeasureMode.UNDEFINED ->
            MeasureSpec.makeMeasureSpec(0, MeasureSpec.UNSPECIFIED)
    }

    // ── SetStyle → Yoga (the routing table's LAYOUT half) ────────────────────

    /** True when [property] is a LAYOUT style — i.e. it belongs to the Yoga node,
     * never to the View. Mirrors `NativeRenderer.YogaStyleAttributes` exactly;
     * matching is ORDINAL/case-sensitive on both shells. */
    fun owns(property: String): Boolean = property in YOGA_STYLES

    /**
     * Applies one LAYOUT style to [nodeId]'s Yoga node. A **null** value resets
     * the property to Yoga's default (the wire's "reset" — the other half of the
     * 1.2 null-reset fix); anything the grammar does not accept is logged and
     * ignored.
     *
     * Yoga's defaults are Yoga's, not CSS's, and the resets below say so:
     * `flexShrink` back to **0** (CSS says 1), `alignItems` to `stretch`,
     * `flexDirection` to `column`.
     */
    fun setStyle(nodeId: Int, property: String, value: String?) {
        val node = nodes[nodeId] ?: return logIgnore(property, "node $nodeId has no Yoga node")
        when (property) {
            // ── Enum words ──
            "flexDirection" -> node.setFlexDirection(when (value) {
                null -> YogaFlexDirection.COLUMN
                "row" -> YogaFlexDirection.ROW
                "column" -> YogaFlexDirection.COLUMN
                "row-reverse" -> YogaFlexDirection.ROW_REVERSE
                "column-reverse" -> YogaFlexDirection.COLUMN_REVERSE
                else -> return logIgnore(property, value)
            })
            "justifyContent" -> node.setJustifyContent(when (value) {
                null -> YogaJustify.FLEX_START
                "flex-start" -> YogaJustify.FLEX_START
                "center" -> YogaJustify.CENTER
                "flex-end" -> YogaJustify.FLEX_END
                "space-between" -> YogaJustify.SPACE_BETWEEN
                "space-around" -> YogaJustify.SPACE_AROUND
                "space-evenly" -> YogaJustify.SPACE_EVENLY
                else -> return logIgnore(property, value)
            })
            "alignItems" -> node.setAlignItems(
                alignOrNull(value) ?: if (value == null) YogaAlign.STRETCH else return logIgnore(property, value))
            "alignSelf" -> node.setAlignSelf(
                alignOrNull(value) ?: if (value == null) YogaAlign.AUTO else return logIgnore(property, value))
            "flexWrap" -> node.setWrap(when (value) {
                null, "nowrap" -> YogaWrap.NO_WRAP
                "wrap" -> YogaWrap.WRAP
                "wrap-reverse" -> YogaWrap.WRAP_REVERSE
                else -> return logIgnore(property, value)
            })
            "position" -> node.setPositionType(when (value) {
                null, "relative" -> YogaPositionType.RELATIVE
                "absolute" -> YogaPositionType.ABSOLUTE
                else -> return logIgnore(property, value)
            })

            // ── Unitless floats (NOT lengths: no %, no auto) ──
            "flexGrow" -> node.setFlexGrow(unitless(property, value, default = 0f) ?: return)
            // Yoga's flexShrink default is 0 — it DEVIATES from CSS's 1. Resetting
            // to 1 here would silently make every node shrinkable.
            "flexShrink" -> node.setFlexShrink(unitless(property, value, default = 0f) ?: return)

            // ── Layout lengths ──
            "width" -> length(property, value, auto = true)
                ?.applyTo(node::setWidth, node::setWidthPercent, node::setWidthAuto) ?: return
            "height" -> length(property, value, auto = true)
                ?.applyTo(node::setHeight, node::setHeightPercent, node::setHeightAuto) ?: return
            "flexBasis" -> length(property, value, auto = true)
                ?.applyTo(node::setFlexBasis, node::setFlexBasisPercent, node::setFlexBasisAuto) ?: return
            "minWidth" -> length(property, value)
                ?.applyTo(node::setMinWidth, node::setMinWidthPercent) ?: return
            "maxWidth" -> length(property, value)
                ?.applyTo(node::setMaxWidth, node::setMaxWidthPercent) ?: return
            "minHeight" -> length(property, value)
                ?.applyTo(node::setMinHeight, node::setMinHeightPercent) ?: return
            "maxHeight" -> length(property, value)
                ?.applyTo(node::setMaxHeight, node::setMaxHeightPercent) ?: return
            "padding" -> length(property, value)
                ?.applyTo({ node.setPadding(YogaEdge.ALL, it) }, { node.setPaddingPercent(YogaEdge.ALL, it) })
                ?: return
            "gap" -> length(property, value)
                ?.applyTo({ node.setGap(YogaGutter.ALL, it) }, { node.setGapPercent(YogaGutter.ALL, it) })
                ?: return
            // margin and the position offsets are the ONLY places a negative is
            // meaningful (Yoga defines both) — see [length]'s `negative` flag.
            "margin" -> length(property, value, auto = true, negative = true)
                ?.applyTo({ node.setMargin(YogaEdge.ALL, it) },
                    { node.setMarginPercent(YogaEdge.ALL, it) },
                    { node.setMarginAuto(YogaEdge.ALL) })
                ?: return
            "top", "right", "bottom", "left" -> {
                val edge = when (property) {
                    "top" -> YogaEdge.TOP
                    "right" -> YogaEdge.RIGHT
                    "bottom" -> YogaEdge.BOTTOM
                    else -> YogaEdge.LEFT
                }
                length(property, value, negative = true)
                    ?.applyTo({ node.setPosition(edge, it) }, { node.setPositionPercent(edge, it) })
                    ?: return
            }

            else -> logIgnore(property, "not a Yoga style (routing bug — owns() said it was)")
        }
    }

    // ── The grammar's value forms ────────────────────────────────────────────

    /** One parsed length: exactly one of the three forms the grammar allows. */
    private sealed interface Length {
        @JvmInline value class Points(val v: Float) : Length
        @JvmInline value class Percent(val v: Float) : Length
        data object Auto : Length
    }

    /** Dispatches a parsed [Length] onto Yoga's three setter shapes. `auto` with
     * no auto-setter cannot happen: [length] only returns it when `auto = true`. */
    private fun Length.applyTo(
        points: (Float) -> Unit,
        percent: (Float) -> Unit,
        auto: (() -> Unit)? = null,
    ): Unit = when (this) {
        is Length.Points -> points(v)
        is Length.Percent -> percent(v)
        Length.Auto -> auto?.invoke() ?: Unit
    }

    /**
     * Parses a layout length: a bare number (dp), `N%`, or `auto` — nothing else.
     * There are NO unit suffixes in this grammar (`px` is not dp; `sp` is
     * font-scaled; neither exists on iOS).
     *
     * `null` → the property's Yoga default, expressed as the undefined/auto form
     * the setters take: `YGUndefined` points for most, and `auto` where the
     * property has one. Returns null (→ the caller returns) when the value is not
     * in the grammar; the negative rule is enforced here so it is enforced once.
     */
    private fun length(
        property: String,
        value: String?,
        auto: Boolean = false,
        negative: Boolean = false,
    ): Length? {
        if (value == null) return if (auto) Length.Auto else Length.Points(YogaConstants.UNDEFINED)
        if (value == "auto") {
            if (auto) return Length.Auto
            logIgnore(property, "'auto' is not a legal value for $property")
            return null
        }
        val percent = value.endsWith("%")
        val n = (if (percent) value.dropLast(1) else value).toFloatOrNull()
        if (n == null || n.isNaN()) {
            logIgnore(property, "'$value' is not a number, a percentage or 'auto'")
            return null
        }
        // Rejected and LOGGED, never clamped: Yoga's behaviour for a negative
        // width is undefined territory, and a silently-clamped 0 is a frame the
        // two platforms may not agree on.
        if (n < 0 && !negative) {
            logIgnore(property, "negative values are not accepted for $property (got '$value')")
            return null
        }
        return if (percent) Length.Percent(n) else Length.Points(n)
    }

    /** A unitless float (`flexGrow`/`flexShrink`) — not a length: no `%`, no
     * `auto`, no units, no negatives. `null` → [default] (Yoga's). */
    private fun unitless(property: String, value: String?, default: Float): Float? {
        if (value == null) return default
        val n = value.toFloatOrNull()
        if (n == null || n.isNaN()) {
            logIgnore(property, "'$value' is not a unitless number")
            return null
        }
        if (n < 0) {
            logIgnore(property, "negative values are not accepted for $property (got '$value')")
            return null
        }
        return n
    }

    private fun alignOrNull(value: String?): YogaAlign? = when (value) {
        "auto" -> YogaAlign.AUTO
        "flex-start" -> YogaAlign.FLEX_START
        "center" -> YogaAlign.CENTER
        "flex-end" -> YogaAlign.FLEX_END
        "stretch" -> YogaAlign.STRETCH
        "baseline" -> YogaAlign.BASELINE
        else -> null
    }

    private fun logIgnore(property: String, detail: String?) {
        Log.w(TAG, "SetStyle $property ignored: $detail")
    }

    companion object {
        private const val TAG = "BlazorNative.YogaLayout"

        /**
         * NODETYPES whose size is the native widget's business (DoD #3) — and the
         * ONLY nodes that get a measure function. NOT "the nodes with no
         * children": a childless `view` is a container, and measuring it would let
         * its intrinsic size override Yoga (non-negotiable #6).
         */
        private val MEASURED_NODE_TYPES = setOf("text", "button", "input", "image")

        /** The LAYOUT half of the SetStyle partition — the mirror of
         * `NativeRenderer.YogaStyleAttributes` (design §"The allow-list is a
         * routing table"). Every name here goes to the Yoga node and to NOTHING
         * else; in particular `padding`/`margin`/`width`/`height` do NOT also
         * reach the View, which would double-apply them. */
        private val YOGA_STYLES = setOf(
            "flexDirection", "justifyContent", "alignItems", "flexWrap", "gap",
            "alignSelf", "flexGrow", "flexShrink", "flexBasis",
            "width", "height", "minWidth", "maxWidth", "minHeight", "maxHeight",
            "padding", "margin",
            "position", "top", "right", "bottom", "left",
        )

        @Volatile private var soLoaderReady = false

        /**
         * Yoga's JNI core loads through SoLoader, which must be initialised once
         * per process before the first [YogaNodeFactory.create]. `SoLoader.init`
         * is itself idempotent; the flag just keeps the common path off it. The
         * shell calls this at start ([MainActivity]); doing it here as well is
         * what lets a WidgetMapper constructed directly (the synthetic-frame
         * instrumented tests) work without an Activity.
         */
        @JvmStatic
        @Synchronized
        fun ensureSoLoader(context: Context) {
            if (soLoaderReady) return
            SoLoader.init(context.applicationContext, false)
            soLoaderReady = true
        }
    }
}
