package io.blazornative.shell

import android.content.Context
import android.util.AttributeSet
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
import com.facebook.yoga.YogaOverflow
import com.facebook.yoga.YogaPositionType
import com.facebook.yoga.YogaUnit
import com.facebook.yoga.YogaWrap
import kotlin.math.abs
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
 * **THE HOST ROOT IS ONE OF THESE TOO** (`res/layout/main.xml`'s `widget_root`),
 * which is why the class is public and XML-inflatable. A stock FrameLayout there
 * would put the TOP-LEVEL nodes' frames back under the framework's control: every
 * `addView`/`setText` in a batch calls `requestLayout()`, the ensuing traversal
 * re-measures the host and `FrameLayout.onLayout` re-places each top-level child
 * at (0, 0, hostW, hostH) — and the resize listener's bounds guard sees no change,
 * so nothing repairs it. With ONE top-level node whose width happens to match the
 * host that is invisible; with TWO they would overlap on Android and lay out
 * correctly on iOS, i.e. a cross-platform divergence in exactly the numbers DoD #2
 * asserts. `BnLayoutDemoAndroidTest` pins it by asserting the root column HUGS its
 * content instead of filling the host.
 *
 * It is still a `FrameLayout` in every sense that matters to a caller — z-ordered
 * overlapping children, no stacking, no orientation.
 */
open class BnYogaFrameLayout @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null,
) : android.widget.FrameLayout(context, attrs) {

    init {
        // Yoga's `overflow` default is VISIBLE and `UIView.clipsToBounds` is false,
        // but `ViewGroup.clipChildren` defaults to TRUE — so an overflowing child
        // would be clipped on Android and drawn on iOS. Not a frame-number
        // divergence (DoD #2 still passes) but a "renders identically" one, and it
        // costs one line to align the two shells on Yoga's default.
        clipChildren = false
    }

    /**
     * The last size **Yoga** applied to this container, in px.
     *
     * Recorded from the **`EXACTLY`** spec [YogaLayout.applyFrame] measures with —
     * and from **that arm only**, which is the whole of the field's meaning. A
     * framework pass can hand this container an `AT_MOST` or `UNSPECIFIED` spec at
     * any time (a `ScrollView` does it on every layout), and writing THOSE answers
     * back would let a framework-shrunk value stick and then be reported as "what
     * Yoga said" on the next `UNSPECIFIED` — a number no Yoga node ever computed,
     * feeding the fallback below.
     */
    private var yogaWidth = 0
    private var yogaHeight = 0

    /**
     * Yoga sized this node; adopt the spec (which [YogaLayout.applyFrame] built
     * from the computed frame) instead of re-measuring children behind its back.
     *
     * Under a NON-EXACTLY spec the answer is the last size Yoga applied, not
     * `getDefaultSize(0, …)`'s zero. **`UNSPECIFIED` is exactly what a `ScrollView`
     * hands its content child** ("tell me how tall you want to be"), and what this
     * fallback protects is NOT "the content would otherwise vanish" — it would not;
     * [YogaLayout.applyFrames] walks parent-first and [YogaLayout.applyFrame] does a
     * direct `measure(EXACTLY) + layout()`, so **Yoga is the last word on the
     * content's frame either way, and the page still scrolls.**
     *
     * What it protects is the **scroll OFFSET**:
     * `ScrollView.onLayout` ends with `scrollTo(mScrollX, mScrollY)`, which
     * **re-clamps the offset against the content child's just-laid-out height** — on
     * every layout the ScrollView takes part in. Answer 0 here and the content is
     * 0-tall *at that moment*, so `mScrollY` is clamped to **0**: any re-render that
     * dirties the scroll subtree (a row appended, a label re-texted — M7's
     * virtualized list does it constantly) **snaps a scrolled page back to the top**.
     * Real, user-visible, and completely silent in a frame table.
     *
     * **THIS MECHANISM IS ANDROID-SPECIFIC.** `UIScrollView` neither re-measures its
     * content view nor re-clamps `contentOffset` on layout, so the iOS shell has no
     * equivalent of this fallback to mirror — and must instead handle a SHRINKING
     * `contentSize` under a live `contentOffset` itself. Do not go looking for this
     * on iOS; do not copy it there. (Design §"Why this works on Android".)
     *
     * Before the first layout pass there is no Yoga size yet, so an `AT_MOST` spec
     * falls back to filling it, which is what a stock FrameLayout would have said.
     */
    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        // Record ONLY the EXACTLY arm — see [yogaWidth]. `applyFrame` is the only
        // caller that uses it, and it is the only caller whose spec IS a Yoga frame.
        if (MeasureSpec.getMode(widthMeasureSpec) == MeasureSpec.EXACTLY) {
            yogaWidth = MeasureSpec.getSize(widthMeasureSpec)
        }
        if (MeasureSpec.getMode(heightMeasureSpec) == MeasureSpec.EXACTLY) {
            yogaHeight = MeasureSpec.getSize(heightMeasureSpec)
        }
        setMeasuredDimension(
            resolve(widthMeasureSpec, yogaWidth),
            resolve(heightMeasureSpec, yogaHeight),
        )
    }

    private fun resolve(spec: Int, lastYogaSize: Int): Int = when (MeasureSpec.getMode(spec)) {
        MeasureSpec.EXACTLY -> MeasureSpec.getSize(spec)
        MeasureSpec.AT_MOST ->
            if (lastYogaSize > 0) minOf(lastYogaSize, MeasureSpec.getSize(spec))
            else MeasureSpec.getSize(spec)
        else -> lastYogaSize // UNSPECIFIED — the ScrollView path
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
 * text/button/input/image, plus the Phase 7.3 form-control leaves
 * checkbox/switch/slider/picker) — never by "this node has no children". They are
 * different sets: BnLayoutDemo's row is three CHILDLESS `view`s, and a measure
 * func on them would let a FrameLayout's intrinsic size (0×0) speak over Yoga,
 * destroying the `flexGrow:1` box's computed width. A childless container is a
 * container.
 *
 * **…and an `image` measures its BYTES, not its widget** (Phase 6.3). It is the ONE
 * measured nodeType whose native widget answers in the WRONG UNIT: an `ImageView`'s
 * intrinsic size is its drawable's size in PIXELS, so the generic `view.measure` path
 * would report `px / density` — a number that differs on every device and can never
 * agree with iOS, where a `UIImage`'s size is already in points. `image` therefore
 * gets a measure function of its own, fed by [setImageNaturalSize] — which is where
 * the rule (ONE FILE PIXEL IS ONE dp/pt) is stated, and why.
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
 * ## `scroll` — the SYNTHETIC CONTENT NODE (Phase 6.2)
 *
 * A `scroll` node is a **viewport**, and its wire children live one level deeper
 * than the wire says: under a synthetic content node the shell creates and the
 * renderer never hears about (see [contentNodes] and [attachContentNode]). That
 * node's COMPUTED HEIGHT *is* the content size — Yoga's number, not a shell-side
 * union of child frames. **A scroll node's wire child at index *i* is the CONTENT
 * node's child at index *i***, in this tree and in [WidgetMapper]'s view tree: the
 * second index-mapping rule after the text collapse above, failing the same way if
 * broken — silently, as a skew.
 *
 * ## The honest boundary that is left: `picker`
 *
 * A `view` becomes a [BnYogaFrameLayout], which does not lay out its own children
 * — so Yoga's frames survive. `picker` (Spinner) is a FRAMEWORK ViewGroup that runs
 * its own layout and will overwrite the frames Yoga computed for its children. Its
 * node still takes part in the Yoga tree (so the Spinner is itself placed correctly
 * by its parent — and, since Phase 7.3, MEASURED as a leaf: its items are wire DATA,
 * never wire children, so nothing inside it ever has a Yoga node to fight over); it
 * is only what is INSIDE it that Yoga does not get the final word on. `scroll` used
 * to be in this paragraph; 6.2 took it out.
 *
 * Threading: main-thread only. Every entry point is called from inside
 * [WidgetMapper.applyBatch] (already posted to the main looper) or from the host
 * root's layout listener.
 */
class YogaLayout(private val context: Context, private val root: ViewGroup) {

    /** nodeId → Yoga node. Collapsed text nodes are absent BY DESIGN (see KDoc). */
    private val nodes = mutableMapOf<Int, YogaNode>()

    /**
     * Phase 6.2 — **the SYNTHETIC content nodes**: a `scroll` node's id → the Yoga
     * node that holds its wire children. Keyed by the SCROLL node's id because that
     * is the only id there is: the content node is the shell's, never on the wire.
     *
     * Its presence in this map is also what makes a node "a scroll node" to
     * [setStyle] (the container-style ignore rule) and to [calculateAndApply] (the
     * definite-height warning) — there is no separate nodeType table, and adding one
     * would be a second thing to keep in step with this one.
     *
     * NOT in [nodes]: [nodeCount] counts WIRE nodes, and a synthetic node in there
     * would make the mapper's two trees look mismatched. It IS in [views] (it places
     * the content view) and it IS a Yoga child of its scroll node — which is what
     * makes 6.1's subtree purge free it for nothing, and `YogaNodeLifecycleAndroidTest`
     * is where that stops being an assumption.
     */
    private val contentNodes = mutableMapOf<Int, YogaNode>()

    /**
     * Phase 7.4 — **the modal OVERLAY nodes**: a `modal` node's id → the Yoga node
     * of its full-root overlay (the 6.2 synthetic-node machinery, pointed at the
     * root). The modal's WIRE node ([nodes]) is the 0-sized absolute ANCHOR at its
     * wire slot; the overlay is a SECOND shell-side node, attached as the LAST
     * child of [hostRoot], that its wire children actually parent into (see
     * [createNode]'s redirection — the modal's wire child at index i is the
     * overlay's child at index i, the THIRD index-mapping rule).
     *
     * INSERTION-ORDERED (a LinkedHashMap by construction): stacking is creation
     * order — the overlay attaches last at creation and is never re-ordered, so a
     * re-shown (re-created) modal lands on top, and iteration order IS the stack.
     *
     * **The overlay is NOT a Yoga descendant of the anchor** — the one purge
     * difference from `scroll`, stated in the design so it cannot be an
     * assumption: `RemoveNode(modalId)` must purge BOTH subtrees, and this map's
     * entry is what names the second one ([removeNode]'s fixpoint). Same
     * [contentNodes] discipline otherwise: in [views] (it places the overlay
     * view), never in [nodes] (no patch can name it), evicted in the same breath
     * as the anchor.
     */
    private val overlayNodes = LinkedHashMap<Int, YogaNode>()

    /** Yoga node → the View it places. Populated for every node in [nodes]. */
    private val views = mutableMapOf<YogaNode, View>()

    /** The reverse of [views] — the lookup [markDirty] needs, because a collapsed
     * text node's ReplaceText must dirty its PARENT's (the Button's) measure cache,
     * and the only handle the mapper has at that point is the VIEW. A map, not a
     * scan of [views]: markDirty runs once per changed leaf per frame, on the main
     * thread, inside the commit path. `View` does not override `equals`, so the
     * key semantics are identity — which is exactly what is wanted. */
    private val viewToNode = mutableMapOf<View, YogaNode>()

    /** The subset of [nodes] carrying a measure function. `YogaNode.dirty()`
     * THROWS on a node without one, so [markDirty] checks membership first. */
    private val measured = mutableSetOf<YogaNode>()

    /**
     * Phase 6.3 — **the natural size of the bytes an `image` node currently holds**, in dp.
     * Absent = no bytes (never fetched, failed, cancelled, or cleared): the node measures
     * **0 × 0**. See [setImageNaturalSize], which is where the number comes from and where
     * the parity rule that governs it is stated.
     */
    private val imageSizes = mutableMapOf<YogaNode, NaturalSize>()

    /**
     * The scroll-node diagnostics already emitted — `(nodeId, message)` in order —
     * and the warn-once keys that got them there, `(nodeId, kind)`. See [diagnose].
     *
     * **Keyed by NODE ID because they are EVICTED with the node** ([removeNode]), for
     * the same reason [contentNodes] is: node ids **restart at 1 after a reset**, so a
     * retired id is handed out again. Keep a warn-once key past its node's death and
     * the NEXT scroll node to reuse that id is warned about **nothing at all** — the
     * one diagnostic it needed, suppressed by a ghost. (And the message list would
     * grow monotonically across every navigation.)
     */
    private val diagnosed = mutableListOf<Pair<Int, String>>()
    private val diagnosedKeys = mutableSetOf<Pair<Int, String>>()

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
     *
     * **[contentView] is the Phase 6.2 half**: for a `scroll` node the mapper passes
     * the content `View` it created inside the `ScrollView`, and this method creates
     * the matching SYNTHETIC content NODE (see [contentNodes]) — the two trees get
     * their synthetic node together or not at all. Null for every other nodeType.
     *
     * **[overlayView] is the Phase 7.4 half**: for a `modal` node the mapper passes
     * the overlay view it attached last at the host root, and this method fixes the
     * ANCHOR's styles (absolute, 0 × 0 — out of the flex flow entirely, so the
     * anchor's siblings never move) and attaches the matching overlay NODE last at
     * [hostRoot] (see [overlayNodes]). Null for every other nodeType.
     */
    fun createNode(
        nodeId: Int,
        nodeType: String,
        view: View,
        parentId: Int?,
        insertIndex: Int,
        contentView: View? = null,
        overlayView: View? = null,
    ) {
        val node = YogaNodeFactory.create(config)
        nodes[nodeId] = node
        views[node] = view
        viewToNode[view] = node
        if (nodeType in MEASURED_NODE_TYPES) {
            // Phase 6.3: an `image` measures its BYTES, not its widget — see
            // [setImageNaturalSize]. Everything else asks the native widget (DoD #3).
            if (nodeType == IMAGE) {
                node.setMeasureFunction { n, _, _, _, _ ->
                    val size = imageSizes[n]
                    if (size == null) YogaMeasureOutput.make(0f, 0f)
                    else YogaMeasureOutput.make(size.widthDp, size.heightDp)
                }
            } else {
                node.setMeasureFunction { _, width, widthMode, height, heightMode ->
                    val d = density
                    view.measure(
                        measureSpec(width, widthMode, d), measureSpec(height, heightMode, d))
                    YogaMeasureOutput.make(view.measuredWidth / d, view.measuredHeight / d)
                }
            }
            measured.add(node)
        }
        if (nodeType == SCROLL) {
            requireNotNull(contentView) { "a scroll node must be created with its content view" }
            node.setOverflow(YogaOverflow.SCROLL)
            attachContentNode(nodeId, node, contentView)
        }
        // A child of a SCROLL node is a child of its CONTENT node — in this tree and
        // in the view tree, at the same index (non-negotiable #2). A child of a
        // MODAL node is a child of its OVERLAY node — same rule, third mapping
        // (Phase 7.4): the modal's wire child at index i is the overlay's child at
        // index i, never the anchor's (the anchor hosts nothing, ever).
        val parent = parentId?.let { overlayNodes[it] ?: contentNodes[it] ?: nodes[it] } ?: hostRoot
        // −1 = append; anything else is the exact index the VIEW went to.
        //
        // **An out-of-range insertIndex is a RENDERER BUG, and the two shells answer
        // it differently ON PURPOSE — ONE recorded decision, not two parsers
        // disagreeing.** See the Gate 4 ledger
        // (docs/plans/2026-07-13-phase-6.1-implementation-plan.md §"Recorded decisions
        // carried in from the Gate 3 review", entry 2), which is the single statement;
        // `bn_yoga_node_insert_child`'s comment points at the same entry.
        // Here: it THROWS (YogaNode.addChildAt → List.add(i, …)), mirroring
        // WidgetMapper.addView, because a JNI throw surfaces as a stack trace naming
        // the renderer. iOS clamps instead — it clamps BOTH trees identically, so they
        // cannot skew against each other, and trapping inside a render callback there
        // aborts the app with no diagnostic at all.
        //
        // Phase 7.4 — the hostRoot APPEND lands BEFORE the live overlays: the
        // overlays are shell-side extras at the END of hostRoot's child list (the
        // "overlay is LAST, always" invariant), so a top-level wire append must slot
        // in ahead of them or the new page would draw OVER an open modal's scrim —
        // and the wire's 1:1 index arithmetic at the root would silently skew.
        val index = if (insertIndex >= 0) insertIndex
            else if (parent === hostRoot) parent.childCount - overlayNodes.size
            else parent.childCount
        parent.addChildAt(node, index)

        if (nodeType == MODAL) {
            requireNotNull(overlayView) { "a modal node must be created with its overlay view" }
            // THE ANCHOR's shell-fixed styles: absolutely positioned and 0-sized —
            // out of the flex flow entirely, contributing nothing to any sibling's
            // frame. It exists for exactly one reason (the third index-mapping
            // rule): it occupies the modal's slot in its wire parent, in BOTH
            // trees, so sibling insert indices never skew.
            node.setPositionType(YogaPositionType.ABSOLUTE)
            node.setWidth(0f)
            node.setHeight(0f)
            // …and the overlay, attached LAST at the host root — AFTER the anchor's
            // own insertion above, so a top-level modal's overlay still lands on top.
            attachOverlayNode(nodeId, overlayView)
        }
    }

    /**
     * **THE MODAL OVERLAY NODE** (Phase 7.4, design decision 1) — the second
     * shell-side piece of a `modal`, attached as the LAST child of [hostRoot]
     * (stacking is creation order; the shell never re-orders it — a re-shown
     * modal is a re-created one and lands on top).
     *
     * Its styles are SHELL-FIXED, and they are the whole of the modal's geometry:
     *
     *  - `position: absolute; top: 0; left: 0; width: 100%; height: 100%` — the
     *    scrim IS the root's own bounds, re-solved for free on every host resize
     *    (the overlay lives in the ONE tree the existing resize hook re-solves).
     *  - `justifyContent: center; alignItems: center` — the design's frame
     *    arithmetic, as styles: the content box (the modal's one wire child,
     *    declared w × h) computes to ((W − w)/2, (H − h)/2) against the root.
     *    The CENTERING is the shell's, deliberately — the wire carries no
     *    layout for it (the modal node's zero-styles rule), so both shells fix
     *    the same pair and the frame tables agree by construction.
     */
    private fun attachOverlayNode(modalId: Int, overlayView: View) {
        val overlay = YogaNodeFactory.create(config).apply {
            setPositionType(YogaPositionType.ABSOLUTE)
            setPosition(YogaEdge.TOP, 0f)
            setPosition(YogaEdge.LEFT, 0f)
            setWidthPercent(100f)
            setHeightPercent(100f)
            setJustifyContent(YogaJustify.CENTER)
            setAlignItems(YogaAlign.CENTER)
        }
        hostRoot.addChildAt(overlay, hostRoot.childCount)
        overlayNodes[modalId] = overlay
        views[overlay] = overlayView
        viewToNode[overlayView] = overlay
    }

    /**
     * Phase 7.4 — **the modal STYLE-IGNORE rule's diagnostic** (design decision 1,
     * the scroll container-style rule's shape). The DECISION lives in
     * [WidgetMapper.handleSetStyle] — one site, BEFORE the layout/visual routing,
     * because every style name must land here (a visual `backgroundColor` would
     * otherwise paint the anchor; a layout `width` would size it). This method is
     * the recording: warn-once per (node, property), evicted with the node, read
     * by tests through [diagnostics] — logcat is not an assertion surface.
     */
    internal fun diagnoseModalStyle(nodeId: Int, property: String) {
        diagnose(nodeId, "modal-style/$property",
            "SetStyle $property ignored: node $nodeId is a `modal` node, and a modal's two " +
                "shell-side pieces (the 0-sized anchor at its wire slot; the full-root " +
                "overlay) both carry SHELL-FIXED styles — every style would land on a node " +
                "the author does not own. Style the CONTENT BOX (the modal's wire child) " +
                "instead; the scrim's paint is the scrimColor PROP.")
    }

    /**
     * **THE SYNTHETIC CONTENT NODE** (design §"The model") — the scroll node's only
     * Yoga child, and the parent of every one of its wire children.
     *
     * Its three styles are the whole of it, and the one it must NOT have is the
     * load-bearing one:
     *
     *  - `height: auto` — **its computed height IS the content size.** Yoga sizes it
     *    to its children (800 for ten 80-high rows), and the shell READS that number
     *    rather than deriving one from a union of child frames (non-negotiable #3;
     *    two shells deriving it independently is exactly where Android and iOS drift).
     *  - `width: 100%` — it spans the viewport, so a row with no width stretches to it.
     *  - `flexDirection: column` — Yoga's default, set anyway: this node's layout is
     *    the SHELL's, and an explicit column is one less thing for the two shells to
     *    silently disagree about.
     *  - **`flexShrink`: NEVER SET.** Yoga's default is **0** where CSS's is 1, and
     *    that default is the ENTIRE mechanism by which this node keeps its 800 against
     *    a 200-high viewport: free space is `200 − 800 = −600`, negative free space is
     *    distributed by the SHRINK pass in proportion to `flexShrink`, and 0 means
     *    "none of it". It is **not** `overflow: scroll` that does this —
     *    `YogaScrollNodeAndroidTest` computes the same 800 with `overflow: visible`,
     *    and computes 200 the moment `flexShrink: 1` is set. Write that one line here
     *    and the page stops scrolling with no error anywhere.
     *
     * (`overflow: scroll` is set on the SCROLL node, in [createNode], because that is
     * what a scrolling viewport *means* and what React Native sets — not because it
     * computes anything. The contract does not claim it does.)
     */
    private fun attachContentNode(scrollId: Int, scrollNode: YogaNode, contentView: View) {
        val content = YogaNodeFactory.create(config).apply {
            setHeightAuto()
            setWidthPercent(100f)
            setFlexDirection(YogaFlexDirection.COLUMN)
            // NO setFlexShrink. See above. This absence is the mechanism.
        }
        scrollNode.addChildAt(content, 0)
        contentNodes[scrollId] = content
        views[content] = contentView
        viewToNode[contentView] = content
    }

    /**
     * Detaches [nodeId]'s node from the tree and PURGES ITS WHOLE SUBTREE from
     * every map.
     *
     * The subtree part is not defensive tidiness — it is the contract. The renderer
     * emits **one** `RemoveNodePatch` for a whole subtree (`NativeRenderer`'s
     * `PurgeNodeSubtree` is .NET-side bookkeeping; the host contract at its
     * `EmitDisposedComponentRemoves` — 7.2's split of the 3.3-era
     * `ProcessDisposedComponent` — says hosts must tolerate — and here, must handle —
     * the descendants themselves). Dropping only this node's entry would leave every
     * descendant in [nodes]/[views]/[measured] forever, and [views] holds a STRONG
     * ref to the View → to the Activity Context; the Java `YogaNode`'s native peer is
     * only reclaimed once nothing references it, so a dead subtree would keep its
     * native Yoga memory alive too. Every navigation would leak a complete tree.
     *
     * `setMeasureFunction(null)` is part of the purge on purpose: the measure lambda
     * CAPTURES the View, so clearing it breaks the last edge from the (native-backed)
     * node to the view hierarchy.
     */
    fun removeNode(nodeId: Int) {
        val node = nodes[nodeId] ?: return
        detachFromOwner(node)
        val doomed = mutableSetOf<YogaNode>()
        collectSubtree(node, doomed)
        // Phase 7.4 — **THE TWO-SUBTREE PURGE** (design decision 1; NOT the scroll
        // shape, and the design names the difference so it cannot be an
        // assumption): a modal's overlay is NOT a Yoga descendant of its anchor —
        // it hangs off [hostRoot] — so the subtree walk above can never find it.
        // Any modal whose ANCHOR is doomed takes its OVERLAY subtree with it, and
        // the [overlayNodes] entry is what names it. A FIXPOINT, not one pass:
        // a modal can sit INSIDE another modal's overlay (BnModal in
        // ChildContent), so purging one overlay can doom another modal's anchor.
        // Miss this and Android leaks the overlay, the content box and every row
        // under it once per dismissal — and iOS is left with a dangling YGNodeRef.
        var grew = true
        while (grew) {
            grew = false
            for ((modalId, overlay) in overlayNodes) {
                if (overlay in doomed) continue
                val anchor = nodes[modalId] ?: continue
                if (anchor in doomed) {
                    detachFromOwner(overlay)
                    collectSubtree(overlay, doomed)
                    grew = true
                }
            }
        }
        val doomedIds = nodes.entries.filter { it.value in doomed }.map { it.key }.toSet()
        nodes.entries.removeAll { it.value in doomed }
        // Phase 7.4: the overlay entries die with their anchors (the contentNodes
        // discipline — ids are reused, and an entry outliving its node answers
        // "is N a modal?" for the next node to inherit the id).
        overlayNodes.entries.removeAll { it.value in doomed }
        // Phase 6.2: the SYNTHETIC content node is a Yoga child of its scroll node, so
        // [collectSubtree] already found it and [purge] already dropped its view
        // mapping — but THIS map would keep the YogaNode itself (and with it the native
        // peer) alive forever, and would keep answering "yes" to "is node N a scroll
        // node?" long after N was reused. One RemoveNodePatch means a whole subtree, and
        // the synthetic node is now part of that subtree.
        contentNodes.entries.removeAll { it.value in doomed }
        // …and the DIAGNOSTICS BOOKKEEPING, for exactly the reason written one line up:
        // ids are REUSED after a reset. A warn-once key that outlives its node silences
        // the warning the node that inherits its id would have earned — the diagnostic
        // is at its most valuable on a freshly-written page, and that is precisely when
        // a stale key eats it. (And the message list would otherwise grow forever, one
        // navigation at a time.)
        diagnosed.removeAll { it.first in doomedIds }
        diagnosedKeys.removeAll { it.first in doomedIds }
        for (dead in doomed) purge(dead)
    }

    /** Detaches [node] from its Yoga owner, if it has one. */
    private fun detachFromOwner(node: YogaNode) {
        node.owner?.let { owner ->
            for (i in 0 until owner.childCount) {
                if (owner.getChildAt(i) === node) {
                    owner.removeChildAt(i)
                    break
                }
            }
        }
    }

    /** [node] and every descendant — the set ONE RemoveNodePatch stands for. */
    private fun collectSubtree(node: YogaNode, into: MutableSet<YogaNode>) {
        into.add(node)
        for (i in 0 until node.childCount) collectSubtree(node.getChildAt(i), into)
    }

    /** Drops one node's every reference: its measure function (which captures the
     * View), its view mapping, and its dirty-lookup entry. */
    private fun purge(node: YogaNode) {
        if (measured.remove(node)) node.setMeasureFunction(null)
        // Phase 6.3: an image node's recorded natural size dies with it. The measure
        // lambda captures the node (not the View), so this entry is the last edge from a
        // purged node into this class's maps.
        imageSizes.remove(node)
        val view = views.remove(node) ?: return
        if (viewToNode[view] === node) viewToNode.remove(view)
    }

    /**
     * Invalidates the measure cache of the node that PLACES [view]. Yoga caches a
     * measure function's result and will not re-run it unless the node is marked
     * dirty — so a ReplaceText / value / fontSize change that alters a widget's
     * intrinsic size must land here or the next layout reuses the stale size.
     * (`YogaDirtyAndroidTest` is the pin: delete this call and a re-texted label
     * keeps the frame its OLD text measured to, and the row keeps hugging that.)
     *
     * Keyed by VIEW, not nodeId, because the text collapse aliases a text node's
     * id onto its parent Button/EditText: the id has no Yoga node, the view does.
     */
    fun markDirty(view: View) {
        val node = viewToNode[view] ?: return
        if (node in measured && !node.isDirty) node.dirty()
    }

    /**
     * Phase 6.3 — records (or, with a null [naturalPx], CLEARS) the natural size of the
     * bytes [view]'s `image` node now holds. It is the ONLY input to an image's measure
     * function; absent, the node measures **0 × 0**.
     *
     * It does **NOT** dirty the node, deliberately: `markDirty` is the caller's next call
     * (the 6.1 path, mutation-proven), and folding the two together would hide the one that
     * a mutation test needs to be able to delete on its own.
     *
     * ── ONE PIXEL OF THE FILE IS ONE dp/pt. THE WHOLE OF THE PARITY RULE. ───────────────
     * The contract says an intrinsic image measures "the image's NATURAL pixel size in
     * dp/pt", and the two shells only compute the same number if that is read literally:
     * `UIImage(data:)` has `scale = 1`, so on iOS a 160-pixel-wide PNG has `size.width ==
     * 160 POINTS` and Yoga measures 160. Android must therefore report **160 dp** — not
     * `160 px / density` (61dp on the Pixel 6 AVD's 2.625), which is what the generic
     * `view.measure` path above would have said, because an `ImageView`'s intrinsic size is
     * its drawable's size in PIXELS.
     *
     * That is exactly why `image` gets a measure function of its own rather than the widget
     * one: the widget's answer is in the wrong unit, and it is wrong by a factor that is
     * DIFFERENT ON EVERY DEVICE — a divergence no frame table on a single AVD would ever
     * reveal.
     *
     * ── AND NO DOWNSAMPLING (the contract's last row) ───────────────────────────────────
     * [naturalPx] is the size of the DECODED BITMAP, and the shell asks Coil for
     * `Size.ORIGINAL` so that is the size of the FILE. A decoder that quietly halved it
     * would halve the reported size; `WidgetMapperImageTest` and `BnImageDemoAndroidTest`
     * assert the measured frame against the decoded fixture's own pixel count, so it cannot.
     *
     * The natural size is reported UNCLAMPED — Yoga is told what the image *is*, and what to
     * do when that exceeds the available space is a `ContentMode` question this phase
     * deliberately does not answer (design decision 3; the fixtures are ≤ 300 wide so it is
     * never asked).
     */
    fun setImageNaturalSize(view: View, naturalPx: NaturalSize?) {
        val node = viewToNode[view] ?: return
        if (naturalPx == null) imageSizes.remove(node) else imageSizes[node] = naturalPx
    }

    /** An image's natural size — the PIXEL count of its decoded bytes, which by the rule in
     * [setImageNaturalSize] *is* its size in dp. */
    data class NaturalSize(val widthPx: Int, val heightPx: Int) {
        val widthDp: Float get() = widthPx.toFloat()
        val heightDp: Float get() = heightPx.toFloat()
    }

    /**
     * Activity teardown: drop the whole tree. Same purge as [removeNode], applied
     * to every top-level node — the host root's children go, the measure functions
     * (and with them their captured Views) go, and the maps empty out, so nothing
     * pins the Activity's Context past `onDestroy`. `config`/[hostRoot] are Java
     * objects with native peers and are reclaimed with this instance once the maps
     * no longer hold the tree.
     */
    fun destroy() {
        for (i in hostRoot.childCount - 1 downTo 0) hostRoot.removeChildAt(i)
        for (node in nodes.values.toList()) purge(node)
        // The synthetic content nodes are not in [nodes] — teardown must name them.
        for (node in contentNodes.values.toList()) purge(node)
        // Phase 7.4: the modal overlay nodes likewise — no patch ever names one.
        for (node in overlayNodes.values.toList()) purge(node)
        nodes.clear()
        contentNodes.clear()
        overlayNodes.clear()
        views.clear()
        viewToNode.clear()
        measured.clear()
        imageSizes.clear()
        diagnosed.clear()
        diagnosedKeys.clear()
    }

    /** The scroll-node diagnostics, in order — see [diagnose]. */
    internal val diagnostics: List<String> get() = diagnosed.map { it.second }

    /** Test-only: the live node count. `YogaNodeLifecycleAndroidTest` asserts it
     * returns to its baseline after mount → remove cycles — the regression pin for
     * the subtree purge above. WIRE nodes only: the synthetic content nodes are
     * counted by [contentNodeCount], because a synthetic node in here would make the
     * mapper's view-tree/Yoga-tree count comparison lie. */
    internal val nodeCount: Int get() = nodes.size

    /** Test-only: the live view mappings. Tracks [nodeCount] + [contentNodeCount] —
     * a synthetic content node places the content View, so it holds a mapping too. */
    internal val viewCount: Int get() = views.size

    /** Test-only: the live SYNTHETIC content nodes (Phase 6.2). Its return to zero
     * after a scroll node is removed is what proves the subtree purge reaches the one
     * node no patch ever names — on Android a leak, on iOS a dangling YGNodeRef. */
    internal val contentNodeCount: Int get() = contentNodes.size

    /** Test-only (Phase 7.4): the live modal OVERLAY nodes — the Yoga half of the
     * overlay-count lifecycle pin. Must return to 0 after mount → remove, or the
     * two-subtree purge missed the subtree no walk from the anchor can reach. */
    internal val overlayNodeCount: Int get() = overlayNodes.size

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

        for ((scrollId, content) in contentNodes) warnIfIndefiniteHeight(scrollId, content)

        for (i in 0 until hostRoot.childCount) applyFrames(hostRoot.getChildAt(i), 0f, 0f, d)
    }

    /**
     * **THE DEFINITE-HEIGHT WARNING** (design §"The constraint this introduces").
     *
     * An `auto`-height scroll node takes its height **from** its content: the viewport
     * IS the content, the scrollable range is zero, and the page silently never moves.
     * No exception, no dropped patch, no wrong frame — which makes it one of the most
     * baffling symptoms this engine can produce, and the reason it gets a diagnostic
     * of its own.
     *
     * Two conditions, and BOTH are needed:
     *
     *  - the declared height is not a POINT or a PERCENT — i.e. the author gave the
     *    node no definite height of its own. (Not sufficient alone: `Grow="1"` inside
     *    a bounded parent is the *recommended* shape and declares no height at all.)
     *  - and it computed out **EXACTLY** as tall as its content — i.e. flex did not
     *    give it a bounded height either. That is the symptom itself, read off the
     *    computed frames after `calculateLayout`, so it cannot be fooled by the route
     *    the height took.
     *
     * **EXACTLY, not "at least"** — and the difference is the whole worth of the
     * diagnostic. A viewport TALLER than its content is **not a mistake**: it is the
     * recommended shape (`Grow="1"` in a bounded parent) with **nothing to scroll
     * YET** — a list still loading, a page with three items in it, M7's virtualized
     * list on its first under-full frame. It scrolls the moment the content grows past
     * it. Warning there would fire on the shape the docs prescribe, state a falsehood
     * ("it is exactly its content's height" — it is not), and prescribe the fix the
     * author has already applied. A diagnostic that cries wolf on the recommended
     * shape is worse than no diagnostic, so the comparison is an approximate EQUALITY
     * and the two "no diagnostic" tests in `WidgetMapperScrollTest` are what keep it
     * one.
     *
     * **KNOWN FALSE NEGATIVE, ledgered for Gate 4** (spec review): `Height="100%"`
     * against a parent with no definite height resolves to `auto` in Yoga, so the node
     * hugs its content and dies exactly as silently — but its declared unit is
     * `PERCENT`, so it exits at the first condition and is never warned about. The
     * conjunction is deliberate and this is its price; closing it means asking Yoga
     * whether the percent RESOLVED, which it does not expose.
     *
     * Warn-once per node ([diagnose]): a layout pass runs per committed frame, and a
     * warning per frame is a flood — and a flood is a thing people mute.
     */
    private fun warnIfIndefiniteHeight(scrollId: Int, content: YogaNode) {
        val scroll = nodes[scrollId] ?: return
        val unit = scroll.height.unit
        if (unit == YogaUnit.POINT || unit == YogaUnit.PERCENT) return
        if (content.layoutHeight <= 0f) return
        if (abs(scroll.layoutHeight - content.layoutHeight) > EPSILON) return
        diagnose(scrollId, "auto-height",
            "scroll node $scrollId has no definite height: it computed to " +
                "${scroll.layoutHeight}dp, which is exactly its content's height — so there is " +
                "NOTHING TO SCROLL. A viewport takes its height from somewhere definite: an " +
                "explicit Height, or Grow=\"1\" WITH Basis=\"0\" inside a bounded parent (CSS's " +
                "`flex: 1`). Grow=\"1\" ALONE is not enough: flexBasis stays `auto`, so the " +
                "basis is the CONTENT's height, the free space is negative, and flexGrow only " +
                "distributes POSITIVE free space — the negative goes to the shrink pass, and " +
                "Yoga's flexShrink default is 0.")
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
        if (nodeId in contentNodes && property in SCROLL_IGNORED_CONTAINER_STYLES) {
            return diagnose(nodeId, "container-style/$property",
                "SetStyle $property ignored: node $nodeId is a `scroll` node, and a scroll " +
                    "node's CONTAINER layout belongs to the shell — its only Yoga child is the " +
                    "synthetic content node, whose styles are fixed. Put the layout on a column " +
                    "INSIDE the scroll. (Item styles and backgroundColor apply normally.)")
        }
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
            // width/height/flexBasis: `auto` is BOTH a legal value AND the Yoga
            // default, so a null reset lands on setWidthAuto() and friends.
            "width" -> length(property, value, auto = true, autoIsDefault = true)
                ?.applyTo(node::setWidth, node::setWidthPercent, node::setWidthAuto) ?: return
            "height" -> length(property, value, auto = true, autoIsDefault = true)
                ?.applyTo(node::setHeight, node::setHeightPercent, node::setHeightAuto) ?: return
            "flexBasis" -> length(property, value, auto = true, autoIsDefault = true)
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
            //
            // `auto = true, autoIsDefault = false` — and the second half is the
            // whole point (see [length]). `margin: auto` is a legal VALUE (it
            // absorbs free space and re-centres the node) but it is NOT margin's
            // DEFAULT, which is undefined → 0. Resetting a removed margin to `auto`
            // would MOVE the node instead of putting it back, on the exact wire path
            // Gate 1's UpdateProp→SetStyle null-reset fix exists to serve.
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
     * `null` → **the property's Yoga default**, and the two flags are what make
     * that a per-property fact rather than a guess:
     *
     *  - [auto] — `auto` is an accepted VALUE here (Yoga has a setter for it).
     *  - [autoIsDefault] — `auto` is also the property's DEFAULT.
     *
     * They coincide for `width`/`height`/`flexBasis` and they DO NOT for `margin`,
     * whose default is undefined → 0 while `auto` means "absorb the free space".
     * One flag doing both jobs is how a null reset silently re-centres a node.
     * Everything else resets to `YGUndefined` points, which is Yoga's "unset".
     *
     * Returns null (→ the caller returns) when the value is not in the grammar; the
     * negative rule is enforced here so it is enforced once.
     */
    private fun length(
        property: String,
        value: String?,
        auto: Boolean = false,
        autoIsDefault: Boolean = false,
        negative: Boolean = false,
    ): Length? {
        if (value == null) {
            return if (autoIsDefault) Length.Auto else Length.Points(YogaConstants.UNDEFINED)
        }
        if (value == "auto") {
            if (auto) return Length.Auto
            logIgnore(property, "'auto' is not a legal value for $property")
            return null
        }
        val percent = value.endsWith("%")
        val n = number(if (percent) value.dropLast(1) else value)
        if (n == null) {
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
        val n = number(value)
        if (n == null) {
            logIgnore(property, "'$value' is not a unitless number")
            return null
        }
        if (n < 0) {
            logIgnore(property, "negative values are not accepted for $property (got '$value')")
            return null
        }
        return n
    }

    /**
     * THE number production of the grammar (design §"Style value grammar
     * (normative)"), screened BEFORE parsing so that **the whole string must be
     * consumed** — trailing garbage is a rejection, never a prefix parse.
     *
     * The screen is not paranoia about `"12px"` alone (Kotlin's `toFloatOrNull`
     * already rejects that); it is about the two platform parsers being written to
     * the SAME production. Left to their natural implementations they diverge in
     * both directions: Java's float grammar (which `toFloatOrNull` screens with)
     * accepts a trailing `f`/`d` — `"12f"` → 12.0 — while C's `strtof` accepts hex
     * (`0x10`), `inf`, `nan` and a leading-whitespace prefix parse. A value one
     * shell HONOURS and the other IGNORES makes the two frame tables disagree for a
     * reason that has nothing to do with the engine. So both shells screen against
     * this production, and the rejection tests are the contract.
     */
    private fun number(value: String): Float? =
        if (NUMBER.matches(value)) value.toFloatOrNull()?.takeIf { !it.isNaN() && !it.isInfinite() }
        else null

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

    /**
     * A scroll node's runtime diagnostics — **warn-once**, keyed by
     * `(nodeId, kind)`. ONE key convention for both diagnostics: the node id is a
     * FIELD, not a fragment of a string, because [removeNode] evicts by it (see
     * [diagnosed] — node ids are reused, and a stale key silences the node that
     * inherits the id).
     *
     * Both of them (the container-style drop and the definite-height warning) name a
     * failure whose symptom is a page that does not move: no exception, no wrong
     * frame, nothing to see. So they are recorded as well as logged — logcat is not an
     * assertion surface, and a diagnostic no test can see is a diagnostic that can
     * quietly stop firing (`WidgetMapperScrollTest` asserts both, and asserts that the
     * WORKING case — and the not-yet-scrollable one — produce neither).
     */
    private fun diagnose(nodeId: Int, kind: String, message: String) {
        if (!diagnosedKeys.add(nodeId to kind)) return
        diagnosed.add(nodeId to message)
        Log.w(TAG, message)
    }

    companion object {
        private const val TAG = "BlazorNative.YogaLayout"

        /** The one nodeType that carries a synthetic content node. */
        private const val SCROLL = "scroll"

        /** The one nodeType that carries an anchor + overlay pair (Phase 7.4).
         * The same constant [WidgetMapper] keys its half on — one contract,
         * one spelling, the SCROLL precedent. */
        private const val MODAL = "modal"

        /** Phase 6.3 — the one nodeType whose measured size is its BYTES' rather than its
         * widget's. See [setImageNaturalSize]. */
        private const val IMAGE = "image"

        /** Half a dp — below the tolerance every frame assertion in this engine
         * already uses; a float comparison of two computed heights needs one. */
        private const val EPSILON = 0.5f

        /**
         * **IGNORED AND LOGGED on a `scroll` node** (design decision 6, NORMATIVE for
         * both shells). Every one of these styles the *scroll* node, whose only Yoga
         * child is the synthetic content node — so every one of them fails silently
         * and bafflingly:
         *
         *  - `flexDirection: row` lays the content node out across the cross axis and
         *    stretches it to the viewport height: **the page just stops scrolling.**
         *  - `justifyContent` / `alignItems` — the free space on a scrolling viewport
         *    is NEGATIVE (200 − 800 = −600), so `center` offsets the content to
         *    y = −300 and `flex-end` to −600, and a ScrollView cannot scroll above 0:
         *    **the top of the content becomes permanently unreachable.**
         *  - `gap` spaces the scroll node's ONE child against nothing.
         *  - `padding` insets the content node, moving every frame in the parity table.
         *
         * `BnScroll`'s component surface cannot produce them (it forwards `BnView`'s
         * ITEM parameters and none of its CONTAINER ones) — but `YogaStyleAttributes`
         * is a global, name-keyed allow-list and `scroll` is a mappable element name,
         * so `OpenElement("scroll") + AddAttribute("gap", …)` still reaches the wire. A
         * .NET test pins that the hatch is genuinely open, precisely so this rule is
         * known to be LIVE code rather than silently becoming dead.
         *
         * ITEM styles (`flexGrow`, `flexShrink`, `flexBasis`, `alignSelf`, the box,
         * `margin`, `position`, the offsets) and `backgroundColor` apply NORMALLY: a
         * `BnScroll` *is* a flex item, and how the viewport is placed in its parent is
         * the author's business. Over-broad filtering here would be as wrong as none.
         *
         * ── THIS IS A TWO-SHELL CONTRACT, AND IT IS PINNED BY A DRIFT TEST ─────────
         * `ShellStyleTableDriftTests` (tests/BlazorNative.Renderer.Tests) parses this
         * literal out of this file and asserts **two** facts, because six names in a
         * KDoc are exactly what two hand-written shells drift on:
         *
         *  1. **It equals the `.mm`'s list** (`kScrollIgnoredContainerStyles`, Gate 3).
         *     A shell that misses `justifyContent` here offsets the content to
         *     y = −300 and permanently hides the top of the page — silently, on ONE
         *     platform, with every frame in the parity table still correct.
         *  2. **Every name here is on [YOGA_STYLES]** — and that is not ceremony. This
         *     rule lives in [setStyle], which is only ever reached when [owns] says
         *     yes. A name on THIS list but off the routing table would never reach the
         *     rule at all: it would fall through [WidgetMapper]'s VISUAL branch onto
         *     "not yet supported" and be **silently dropped** — the very failure the
         *     drift-test file exists to prevent, arrived at from the other side.
         *
         * Keep the declaration a plain `setOf` of quoted names, declared at the start
         * of its line — same parser, same reason, as [YOGA_STYLES].
         */
        private val SCROLL_IGNORED_CONTAINER_STYLES = setOf(
            "flexDirection", "justifyContent", "alignItems", "flexWrap", "gap", "padding",
        )

        /** `[+-]? ( digit+ ('.' digit*)? | '.' digit+ ) ( [eE] [+-]? digit+ )?` —
         * anchored, so the ENTIRE string must be consumed. See [number]. */
        private val NUMBER = Regex("""[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?""")

        /**
         * NODETYPES whose size is the native widget's business (DoD #3) — and the
         * ONLY nodes that get a measure function. NOT "the nodes with no
         * children": a childless `view` is a container, and measuring it would let
         * its intrinsic size override Yoga (non-negotiable #6).
         *
         * Phase 7.3 adds the four form controls — leaves with FIXED intrinsic
         * sizes via the measure func, BY NODETYPE (the 6.1 law). Their intrinsic
         * sizes are the PLATFORM's own (a CheckBox is whatever the theme says):
         * frame parity applies to LAYOUT (declared sizes and placement), never to
         * intrinsic control sizes — the demo declares Width where a number is
         * asserted cross-platform (sliders/pickers, 240) and the checkbox/switch
         * quartet is asserted per-platform by ORACLE (the 6.3 method).
         *
         * Phase 7.4 adds `activityindicator` (the ProgressBar leaf, same oracle
         * discipline). **`modal` is deliberately NOT here** (design decision 1's
         * measure story): a modal materializes as an anchor and an overlay, both
         * CONTAINERS with shell-fixed styles — a measure func on a container is
         * the 6.1 law's named violation, and there is no widget to ask anyway.
         */
        private val MEASURED_NODE_TYPES = setOf(
            "text", "button", "input", "image",
            "checkbox", "switch", "slider", "picker",
            "activityindicator",
        )

        /**
         * The LAYOUT half of the SetStyle partition — the mirror of
         * `NativeRenderer.YogaStyleAttributes` (design §"The allow-list is a
         * routing table"). Every name here goes to the Yoga node and to NOTHING
         * else; in particular `padding`/`margin`/`width`/`height` do NOT also
         * reach the View, which would double-apply them.
         *
         * **This is a hand-written MIRROR of a .NET set, and it is pinned by a
         * DRIFT TEST** — `ShellStyleTableDriftTests` (tests/BlazorNative.Renderer.Tests),
         * which parses this literal out of this file and asserts set-equality with
         * `NativeRenderer.YogaStyleAttributes` plus disjointness from
         * `VisualStyleAttributes`. Without it, a name added on the .NET side and
         * missed here does not fail loudly: it falls into [WidgetMapper]'s visual
         * branch and is logged as "not yet supported", i.e. the style is SILENTLY
         * DROPPED. The drift test runs in the required `build-test` lane (the only
         * one where .NET, Kotlin and — from Gate 3 — the `.mm` are all
         * checkout-visible), and Gate 3 extends it with `BnYogaLayout.mm`'s name
         * table rather than adding a third un-pinned copy.
         *
         * Keep the declaration a plain `setOf` of quoted names, declared at the start
         * of its line: the drift test parses that literal out of this file, and a
         * declaration it cannot find fails it LOUDLY (never silently-empty).
         */
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
