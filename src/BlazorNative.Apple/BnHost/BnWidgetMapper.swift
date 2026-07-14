// ─────────────────────────────────────────────────────────────────────────────
// BnWidgetMapper — Phase 5.2 (M5 DoD #2): maps decoded [BnFrame] patches to real
// UIKit view mutations. The imperative UIKit twin of the Android
// io.blazornative.shell.WidgetMapper — UILabel ↔ TextView, UIButton ↔ Button,
// UITextField ↔ EditText.
//
// Threading (design §2): `apply(frame)` runs on the native frame-callback thread.
// The mapper BUFFERS patches until the CommitFrame patch, then hops to
// DispatchQueue.main.async to build/mutate the UIKit tree atomically — the exact
// twin of the Kotlin mainHandler.post(applyBatch) batch. Every frame ends with a
// CommitFrame patch. UIKit is main-thread-only, so ALL view work happens there.
//
// Node identity: an [Int32: UIView] registry (`views`) alongside an
// [Int32: bn_yoga_node] one (`yogaNodes`). The Phase 2.8 text collapse aliases a
// text child's nodeId onto its text-bearing parent (a UILabel/UIButton/
// UITextField), so a subsequent ReplaceText routes through the parent's
// title/text — mirroring Android's TextView-but-not-ViewGroup collapse.
//
// ── PHASE 6.1: YOGA OWNS PLACEMENT ───────────────────────────────────────────
// This class no longer stacks anything. `view` is a plain UIView (the UIStackView
// — and with it `insertArrangedSubview` and the layout-margins padding — is GONE);
// every node gets a Yoga node mirrored against the view tree; CommitFrame runs ONE
// `bn_yoga_calculate` and assigns every computed frame. An un-styled tree still
// renders as a vertical stack — that is Yoga's default `flexDirection: column`,
// not a UIStackView, and it is the regression signal that the ENGINE changed and
// the BEHAVIOUR did not.
//
// The three invariants this class exists to hold:
//
//   1. **The Yoga tree mirrors the VIEW tree, not the patch tree.** A collapsed
//      text node gets no view — and therefore NO YOGA NODE, or the two trees'
//      child indices skew and every frame after it is wrong.
//   2. **The measure func attaches BY NODETYPE** ([measuredNodeTypes]) — never by
//      "this node has no children". BnLayoutDemo's row is three CHILDLESS `view`s,
//      and a measure func on them would let a UIView's intrinsic size speak over
//      Yoga, destroying the `Grow=1` box's computed 200.
//   3. **[handleSetStyle] is a ROUTER over the partitioned allow-list.** A LAYOUT
//      name (`bn_yoga_is_layout_style` — the mirror of
//      `NativeRenderer.YogaStyleAttributes`) goes to the Yoga node and NOWHERE
//      else. In particular `padding` no longer touches `layoutMargins`: Yoga lays
//      a container's children out inside its padding box, and a surviving view-level
//      inset would apply it a second time.
//
// ── PHASE 6.2: `scroll` — THE SYNTHETIC CONTENT NODE ─────────────────────────
// A `scroll` node is a VIEWPORT (a `UIScrollView`), and its wire children live one
// level deeper than the wire says: under a synthetic content view/node the shell
// creates and the renderer never hears about ([scrollContents] / [contentNodes]).
// The content node's COMPUTED HEIGHT *is* `contentSize` — Yoga's number, read
// straight out, never a shell-side union of child frames.
//
// **A scroll node's wire child at index *i* is the CONTENT node's child at index
// *i***, in the view tree AND the Yoga tree ([containerFor]): the SECOND
// index-mapping rule after the text collapse above, and it fails the same way —
// silently, as a skew. It is worse here than on Android, which is why the index tests
// are mirrored one for one: Android THROWS on an out-of-range insert index, iOS
// CLAMPS (the recorded 6.1 decision), so a skew that fails loudly there is silent
// here.
//
// And the Yoga node is a RAW native allocation: nothing will ever free it for you.
// [handleRemove] purges whole subtrees (one RemoveNodePatch stands for one) and
// [destroy] drops the tree — every navigation replaces it. `BnYogaLifecycleTests`
// is the pin on the first (a leaked node lays out nothing, so no frame assertion can
// see it); `destroy` is called deterministically from `HostViewController.deinit`
// because the mapper's own `deinit` would run on whatever thread dropped the last
// reference, and the .mm's registries are main-thread-only.
// ─────────────────────────────────────────────────────────────────────────────

import UIKit

/// Wraps a Swift closure as an ObjC target-action sink (UIControl targets must be
/// ObjC objects and are held WEAKLY by the control — so the mapper retains these
/// in `eventTargets`). One per (nodeId, eventName).
final class BnControlTarget: NSObject {
    private let handler: () -> Void
    init(_ handler: @escaping () -> Void) { self.handler = handler }
    @objc func fire() { handler() }
}

/// Phase 6.2 — **THE SYNTHETIC CONTENT VIEW**: the single child of a `scroll` node's
/// `UIScrollView`, holding every one of that node's wire children. Behaviourally it is
/// the plain `UIView` the design calls for — it places nothing (Yoga assigns its
/// subviews' frames) and it clips nothing (Yoga's `overflow` default is `visible`; the
/// VIEWPORT is the one thing in this shell that clips).
///
/// It is a NAMED type only so it can be FOUND. `UIScrollView` keeps its own scroll
/// indicators in `subviews`, so "the content view is `scroll.subviews[0]`" is a lie the
/// shell must not tell and the tests must not believe — `scroll.subviews.compactMap { $0
/// as? BnScrollContentView }.first` is the honest question. (Android's content view is
/// a `BnYogaFrameLayout` for a DIFFERENT reason — there the subclass carries the
/// `onMeasure` fallback and the layout suppression. Here it carries nothing at all.)
final class BnScrollContentView: UIView {}

// ── The native measurement callback (DoD #3) ─────────────────────────────────

/// Yoga's measure func → `UIView.sizeThatFits`. A `@convention(c)` function CANNOT
/// capture, so the node's identity travels in the `void*` context: an UNRETAINED
/// UIView pointer (the view is owned by the mapper's registry and its superview,
/// and `bn_yoga_node_free_subtree` clears the measure func before either lets go).
/// The same constraint the runtime's frame callback lives under.
///
/// The mode mapping is the design's: `Exactly` → the imposed size, verbatim;
/// `AtMost` → fit within it; `Undefined` → no constraint at all, i.e.
/// `.greatestFiniteMagnitude` (which is how a UILabel is asked "how tall are you
/// if you may wrap freely?").
private let bnYogaMeasureTrampoline: bn_yoga_measure_fn = { context, width, widthMode, height, heightMode in
    guard let context = context else { return bn_yoga_size(width: 0, height: 0) }
    let view = Unmanaged<UIView>.fromOpaque(context).takeUnretainedValue()
    let fit = view.sizeThatFits(CGSize(
        width: bnMeasureConstraint(width, widthMode),
        height: bnMeasureConstraint(height, heightMode)))
    return bn_yoga_size(
        width: bnMeasureResolve(Float(fit.width), width, widthMode),
        height: bnMeasureResolve(Float(fit.height), height, heightMode))
}

/// What to ASK the view for, per mode.
private func bnMeasureConstraint(_ value: Float, _ mode: bn_yoga_measure_mode) -> CGFloat {
    if mode == bn_yoga_measure_undefined || value.isNaN { return .greatestFiniteMagnitude }
    return CGFloat(value)
}

/// What to TELL Yoga, per mode: an imposed size is returned unchanged; an at-most
/// constraint clamps the measurement; an unconstrained axis reports the measurement.
private func bnMeasureResolve(_ measured: Float, _ value: Float, _ mode: bn_yoga_measure_mode) -> Float {
    if mode == bn_yoga_measure_exactly { return value }
    if mode == bn_yoga_measure_at_most { return min(measured, value) }
    return measured
}

final class BnWidgetMapper {

    /// UI-event seam (the Kotlin mapper's `onUiEvent` constructor arg): wired by
    /// BnRuntime to the dispatch lane. Default no-op keeps the render-only path
    /// (5.2 tests) working. Called on the main thread from control targets.
    var onUiEvent: (_ handlerId: Int32, _ eventName: String, _ payload: String?) -> Void = { _, _, _ in }

    /// NODETYPES whose size is the native widget's business (DoD #3) — and the ONLY
    /// nodes that get a measure function. NOT "the nodes with no children": a
    /// childless `view` is a container (non-negotiable #6).
    private static let measuredNodeTypes: Set<String> = ["text", "button", "input", "image"]

    /// The one nodeType that is a VIEWPORT and owns a synthetic content node — and it
    /// is deliberately NOT in [measuredNodeTypes]: the measure func attaches BY
    /// NODETYPE, and a `scroll` node is a container Yoga sizes itself (a measure func on
    /// it would let `UIScrollView`'s intrinsic size speak over the author's `Height`).
    /// The same constant the `.mm` keys the content node's styles on — one contract, one
    /// spelling.
    private static let scroll = "scroll"

    /// The host container the top-level (parentless) node is added into — the twin
    /// of Android's widget_root. A plain UIView does not re-place its subviews (no
    /// autoresizing mask is set, and `layoutSubviews` places nothing), so unlike
    /// Android's FrameLayout it needs no layout-suppressing subclass: Yoga's frames
    /// survive the framework's own pass. `BnLayoutDemoTests` pins that by asserting
    /// the root column HUGS its content instead of filling the host.
    private let root: UIView

    private var views: [Int32: UIView] = [:]
    private var yogaNodes: [Int32: UnsafeMutableRawPointer] = [:]

    /// The reverse of [views] for the nodes that own one — the lookup [markDirty]
    /// needs, because a collapsed text node's ReplaceText must dirty its PARENT's
    /// (the UIButton's) measure cache, and the only handle we have at that point is
    /// the VIEW. Keyed by object identity.
    private var viewToNode: [ObjectIdentifier: UnsafeMutableRawPointer] = [:]

    /// The nodeIds that are ALIASES, not owners: a `text` node COLLAPSED onto its
    /// text-bearing parent by [handleCreate]. It owns NO view and NO Yoga node, so
    /// removing it must drop only the map entry — detaching the parent UIButton (or
    /// removing "its" Yoga node, which is the button's) would be catastrophic.
    private var collapsedAliases: Set<Int32> = []

    /// Phase 6.2 — **the SYNTHETIC content VIEWS**: a `scroll` node's id → the
    /// [BnScrollContentView] inside its `UIScrollView` that holds its wire children.
    ///
    /// A scroll node's wire children go **into the content view**, at the index the
    /// patch names — and the Yoga tree does the same, into [contentNodes] — or the two
    /// trees' child indices skew and every frame after the first row is wrong. **The
    /// synthetic node is in BOTH trees or NEITHER**: these two maps' entries are made
    /// in the same breath ([handleCreate]) and purged in the same breath
    /// ([handleRemove]).
    ///
    /// It is also the answer to "what view does a child of node N parent into?" — see
    /// [containerFor]. Never on the wire; the renderer knows nothing about it.
    private var scrollContents: [Int32: UIView] = [:]

    /// …and the Yoga half: a `scroll` node's id → its synthetic content NODE. Keyed by
    /// the SCROLL node's id because that is the only id there is.
    ///
    /// Membership here is also what makes a node "a scroll node" to [handleSetStyle]
    /// (the container-style ignore rule) and to [calculateAndApply] (the definite-height
    /// warning) — there is no separate nodeType table, and adding one would be a second
    /// thing to keep in step with this one.
    ///
    /// NOT in [yogaNodes]: that map is the WIRE nodes, and a synthetic node in it would
    /// make [calculateAndApply]'s frame loop try to place a view it does not own. The
    /// content node IS a Yoga child of its scroll node, which is what makes 6.1's
    /// subtree free reach it — **and is exactly why a stale entry here is a DANGLING
    /// `YGNodeRef`, not a leak.**
    private var contentNodes: [Int32: UnsafeMutableRawPointer] = [:]

    /// The scroll-node diagnostics already emitted — `(nodeId, message)` in order — and
    /// the warn-once keys that got them there. See [diagnose].
    ///
    /// Both of the scroll node's diagnostics (the container-style drop and the
    /// definite-height warning) name a failure whose symptom is a page that does not
    /// move: no exception, no wrong frame, nothing to see. So they are RECORDED as well
    /// as logged — `NSLog` is not an assertion surface, and a diagnostic no test can see
    /// is a diagnostic that can quietly stop firing.
    private struct BnDiagnosticKey: Hashable { let nodeId: Int32; let kind: String }
    private var diagnosed: [(nodeId: Int32, message: String)] = []
    private var diagnosedKeys: Set<BnDiagnosticKey> = []

    /// The synthetic Yoga root: not a patch node, it IS the host view. Its children
    /// mirror `root.subviews` (the top-level nodes).
    private let hostRoot: UnsafeMutableRawPointer

    /// Patches accumulate here until a CommitFrame flushes the batch to main.
    /// Touched only on the callback thread before the main hop.
    private var pending: [BnPatch] = []

    /// Live control-event targets keyed by (nodeId, eventName). The control is
    /// retained alongside the target so DetachEvent/RemoveNode can removeTarget by
    /// identity (the text-collapse can alias several nodeIds onto one control).
    /// Main-thread only (mutated inside applyBatch).
    private struct EventKey: Hashable { let nodeId: Int32; let event: String }
    private var eventTargets: [EventKey: (control: UIControl, target: BnControlTarget)] = [:]

    /// Set by [destroy]. The tree is gone; a late batch (one already hopped to main
    /// when the host tore down) must not resurrect it into freed native memory.
    private var destroyed = false

    init(root: UIView) {
        self.root = root
        self.hostRoot = bn_yoga_node_new()
    }

    /// **Explicit, DETERMINISTIC teardown — the twin of Android's
    /// `WidgetMapper.destroy()` (called from `MainActivity.onDestroy`).**
    ///
    /// `deinit` alone is not enough, and the difference is a threading one. `deinit`
    /// runs on whatever thread happens to drop the last reference, and
    /// `HostViewController` boots on a BACKGROUND queue — so a second boot would
    /// release the previous mapper off-main and free its Yoga subtree (mutating the
    /// `.mm`'s unsynchronised `static std::unordered_map` measure registry)
    /// concurrently with the new mapper's main-thread `applyBatch`. One boot today, so
    /// this is latent rather than live; it costs a function to make it impossible.
    ///
    /// Idempotent: `HostViewController.deinit` calls it, and so does [deinit] as the
    /// backstop for a mapper nobody owned.
    func destroy() {
        guard !destroyed else { return }
        destroyed = true

        // The Yoga tree is RAW native memory owned by the .mm — nothing collects it.
        // Freeing the host root frees every node still hanging off it (and clears
        // their measure funcs, breaking the last edges to the UIViews).
        bn_yoga_node_free_subtree(hostRoot)

        yogaNodes.removeAll()
        // The synthetic content nodes were Yoga CHILDREN of their scroll nodes, so the
        // free above already reclaimed them — these handles are now DANGLING and must go
        // with the rest. (Android's twin merely leaks; here it is memory safety.)
        contentNodes.removeAll()
        scrollContents.removeAll()
        diagnosed.removeAll()
        diagnosedKeys.removeAll()
        viewToNode.removeAll()
        for (_, entry) in eventTargets {
            entry.control.removeTarget(entry.target, action: nil, for: .allEvents)
        }
        eventTargets.removeAll()
        views.removeAll()
        collapsedAliases.removeAll()
        // `pending` is deliberately NOT touched: it is the ONE field written on the
        // frame-callback thread, and reaching across for it here would be the very
        // cross-thread mutation this method exists to make impossible. It is a Swift
        // array of value types on an object that is going away — the `destroyed` flag
        // is what stops a late batch, not an emptied buffer.
    }

    deinit {
        destroy()
    }

    // ── Test-only bookkeeping (the twins of WidgetMapper's `nodeCount` /
    // `yogaNodeCount` / `yogaViewCount`) ──────────────────────────────────────
    //
    // `BnYogaLifecycleTests` asserts all three return to their baseline after
    // add→remove cycles — the regression pin for [handleRemove]'s subtree purge, which
    // NO FRAME ASSERTION CAN SEE (a leaked node lays out nothing and shows nothing).
    // On iOS a missed descendant is not a GC-rooted view: it is a malloc'd YGNodeRef
    // nothing will ever free, plus a UIView still on the far end of a live node's
    // measure-func edge.

    /// The view registry — collapsed text aliases included (they hold a map entry
    /// and nothing else).
    var nodeCount: Int { views.count }

    /// The Yoga tree's live WIRE nodes. Collapsed aliases are absent BY DESIGN; so are
    /// the synthetic content nodes ([yogaContentNodeCount] counts those).
    var yogaNodeCount: Int { yogaNodes.count }

    /// The Yoga tree's view mappings — [yogaNodeCount] plus [yogaContentNodeCount] (a
    /// synthetic content node places the content view, so it holds a mapping too).
    var yogaViewCount: Int { viewToNode.count }

    /// Phase 6.2 — the live SYNTHETIC content NODES. **No patch ever names one**, so
    /// only this can witness that removing a scroll node (or an ancestor of one) freed
    /// it. Its return to zero is the pin: a stale entry here is a `YGNodeRef` into freed
    /// native memory, and the next `calculateAndApply` dereferences it.
    var yogaContentNodeCount: Int { contentNodes.count }

    /// …and the view-tree half of the same pair.
    var scrollContentCount: Int { scrollContents.count }

    /// The scroll node's two runtime diagnostics (Phase 6.2): the container-style
    /// ignore-and-log rule, and the definite-height warning. Exposed because `NSLog` is
    /// not an assertion surface and both failures are SILENT on the device — a page that
    /// simply does not move. `BnScrollTests` asserts them. The twin of Kotlin's
    /// `WidgetMapper.scrollDiagnostics`.
    var scrollDiagnostics: [String] { diagnosed.map { $0.message } }

    // ── Buffer on the callback thread; flush atomically on the main queue ─────

    func apply(_ frame: BnFrame) {
        for patch in frame.patches {
            pending.append(patch)
            if case .commitFrame = patch {
                let batch = pending
                pending.removeAll(keepingCapacity: true)
                DispatchQueue.main.async { [weak self] in
                    self?.applyBatch(batch)
                }
            }
        }
    }

    private func applyBatch(_ patches: [BnPatch]) {
        // THE THREADING CONTRACT, ASSERTED. UIKit is main-thread-only, and so is the
        // `.mm` — whose measure registry is a bare `std::unordered_map` whose safety
        // rests ENTIRELY on "main-thread only". A claim that load-bearing deserves a
        // runtime pin, not a comment.
        dispatchPrecondition(condition: .onQueue(.main))
        guard !destroyed else { return }
        for patch in patches {
            switch patch {
            case .createNode(let nodeId, let nodeType, let parentId, let insertIndex):
                handleCreate(nodeId: nodeId, nodeType: nodeType, parentId: parentId, insertIndex: insertIndex)
            case .replaceText(let nodeId, let text):
                handleReplaceText(nodeId: nodeId, text: text)
            case .removeNode(let nodeId):
                handleRemove(nodeId: nodeId)
            case .updateProp(let nodeId, let name, let value):
                handleUpdateProp(nodeId: nodeId, name: name, value: value)
            case .setStyle(let nodeId, let property, let value):
                handleSetStyle(nodeId: nodeId, property: property, value: value)
            case .commitFrame:
                // Phase 6.1: the frame boundary IS the layout trigger — ONE pass over
                // the whole tree, then every computed frame assigned.
                calculateAndApply()
            case .attachEvent(let nodeId, let eventName, let handlerId):
                handleAttachEvent(nodeId: nodeId, eventName: eventName, handlerId: handlerId)
            case .detachEvent(let nodeId, _, let eventName):
                handleDetachEvent(nodeId: nodeId, eventName: eventName)
            }
        }
    }

    // ── The layout pass (Phase 6.1) ──────────────────────────────────────────

    /// ONE `bn_yoga_calculate` over the whole tree, then a walk that assigns every
    /// computed frame. Called at CommitFrame and on a host resize
    /// (`HostViewController.viewDidLayoutSubviews`).
    ///
    /// Yoga's frames are PARENT-RELATIVE, and so is `UIView.frame` — and because the
    /// two trees mirror each other node-for-node, a node's Yoga parent's view IS its
    /// view's superview. So each frame is assigned directly; no tree walk, no
    /// coordinate conversion, and **no rounding of our own** (the shared config has
    /// Yoga's pixel-grid rounding OFF; see BnYogaLayout.mm's header — iOS must stay
    /// exact and fractional or it disagrees with Android on measured content).
    ///
    /// A host with no bounds yet (a detached root — every synthetic-frame test, and
    /// the first commit if it beats the first layout pass) sizes to CONTENT rather
    /// than to zero: `bn_yoga_calculate` reads a non-positive available dimension as
    /// `auto`.
    func calculateAndApply() {
        dispatchPrecondition(condition: .onQueue(.main)) // see [applyBatch]
        guard !destroyed, !yogaNodes.isEmpty else { return }
        let bounds = root.bounds
        bn_yoga_calculate(hostRoot, Float(bounds.width), Float(bounds.height))
        for (nodeId, node) in yogaNodes {
            guard let view = views[nodeId] else { continue }
            let frame = bn_yoga_node_get_frame(node)
            view.frame = CGRect(x: CGFloat(frame.x), y: CGFloat(frame.y),
                                width: CGFloat(frame.width), height: CGFloat(frame.height))
        }
        // The scroll pass runs AFTER the loop above, and the order is load-bearing: the
        // offset clamp is against the VIEWPORT's bounds, which the loop above is what
        // assigns. (A dictionary's iteration order is arbitrary — this cannot be folded
        // into it.)
        applyScrollFrames()
    }

    /// Phase 6.2 — **THE CONTENT SIZE COMES FROM YOGA** (non-negotiable #3): the
    /// synthetic content node's COMPUTED frame, read straight out, never a shell-side
    /// union of child frames (two shells deriving it independently is precisely where
    /// Android and iOS drift apart, and the whole reason the content node exists).
    ///
    /// Three things happen here, and the third is **iOS's alone**:
    ///
    ///  1. the content VIEW gets the content NODE's frame (it is not in [yogaNodes], so
    ///     the loop above never sees it);
    ///  2. `contentSize` becomes that frame's size — which is what makes the viewport
    ///     scrollable at all;
    ///  3. **`contentOffset` is CLAMPED when the content SHRINKS — AND ONLY WHEN THE USER
    ///     IS NOT HOLDING IT.** `UIScrollView` does not move `contentOffset` when
    ///     `contentSize` gets smaller, so a page scrolled to 600 whose content later drops
    ///     to 300 is left scrolled 300pt past its own end — a blank viewport the user
    ///     cannot scroll back into except by flinging.
    ///     **Android gets this for free** (`ScrollView.onLayout` ends with
    ///     `scrollTo(mScrollX, mScrollY)`, which re-clamps against the content child's
    ///     just-laid-out height on every layout it takes part in) — and the mechanism
    ///     that feeds that clamp, `BnYogaFrameLayout.onMeasure`'s UNSPECIFIED fallback,
    ///     is **ANDROID-SPECIFIC and has no `UIScrollView` equivalent to mirror.** What
    ///     iOS owes instead is this clamp, done itself. (Design §"Why this works on
    ///     Android"; `BnScrollTests.testShrinkingTheContentClampsAScrolledOffset` is the
    ///     pin.)
    ///
    /// A clamp, not a reset: an offset still INSIDE the new range is the user's and is
    /// left alone. Only the part that is now past the end is taken back — and while the
    /// user's FINGER is on the glass, none of it is (see the gesture gate below).
    private func applyScrollFrames() {
        for (scrollId, contentNode) in contentNodes {
            guard let scroll = views[scrollId] as? UIScrollView,
                  let contentView = scrollContents[scrollId] else {
                // **THE GUARD THAT HOLDS UP MEMORY SAFETY, SAID OUT LOUD.** A `contentNodes`
                // entry whose scroll node has no view is not a missing view: the content node
                // is a Yoga CHILD of the scroll node, so whatever freed the scroll node's
                // subtree ALREADY FREED THIS HANDLE ([handleRemove] / [destroy]). It is a
                // DANGLING `YGNodeRef`, and `bn_yoga_node_get_frame(contentNode)` — three
                // lines below — dereferences it.
                //
                // The mutation run proves the shape: drop the purge in [handleRemove] and
                // nothing crashes, because this `continue` silently skips the freed node.
                // The ONLY thing between a use-after-free and a dereference is a line that
                // reads like bookkeeping — so it says what it is, and it TRAPS. A hoist of
                // the frame read above this guard is an ordinary-looking refactor and is a
                // use-after-free; `assertionFailure` is a no-op in release (a shell crash is
                // a black screen) and traps in every XCTest run, turning a latent dangling
                // pointer into a loud failure AT THE MOMENT OF DESYNC rather than leaving it
                // to a count assertion two tests happen to make.
                assertionFailure(
                    "scroll node \(scrollId) has a content node but no view — the purge "
                    + "desynced, and this handle is a DANGLING YGNodeRef")
                continue
            }

            warnIfIndefiniteHeight(scrollId: scrollId, contentNode: contentNode)

            let frame = bn_yoga_node_get_frame(contentNode)
            let size = CGSize(width: CGFloat(frame.width), height: CGFloat(frame.height))
            contentView.frame = CGRect(origin: CGPoint(x: CGFloat(frame.x), y: CGFloat(frame.y)),
                                       size: size)
            scroll.contentSize = size

            // ── THE GESTURE GATE — the offset is the USER'S while their finger is down ──
            // `UIScrollView.bounces` defaults to `true`, and vertical bouncing is live
            // whenever the content is scrollable. So mid-drag, rubber-banding past the top,
            // `contentOffset.y` is LEGITIMATELY NEGATIVE (−30, say) — and the clamp below
            // computes 0 for it and assigns, KILLING THE RUBBER BAND UNDER THE USER'S
            // FINGER. Same at the bottom edge during an overscroll bounce. Every commit
            // landing inside that window does it again.
            //
            // Nothing in BnScrollDemo re-renders while you drag, so this is invisible in
            // this phase and CONSTANT in the next: **M7's virtualized list commits
            // continuously while scrolling**, and it is the named customer of this whole
            // mechanism.
            //
            // Skipping is safe because `UIScrollView` settles the offset back into range
            // ITSELF when the gesture ends. What it does not do — and the ONLY thing the
            // clamp exists for — is correct a programmatically-invalidated offset AT REST,
            // which by definition is not happening while `isTracking` is true.
            //
            // **Android has no equivalent and needs none**: its overscroll is a GLOW, not a
            // moved offset, so `ScrollView`'s per-layout re-clamp can never fight a gesture.
            // The clamp is iOS's alone, and so is its gate.
            guard !scroll.isTracking else { continue }

            let offset = scroll.contentOffset
            let clamped = Self.clampedOffset(offset, contentSize: size, viewport: scroll.bounds.size)
            if clamped != offset {
                scroll.contentOffset = clamped
            }
        }
    }

    /// **THE CLAMP'S ARITHMETIC, EXTRACTED SO IT CAN BE TESTED.** The gesture gate above is
    /// awkward to drive from a unit test (it wants a real touch on a real window), so the
    /// gate is kept to ONE commented line sitting over arithmetic that IS tested — a table
    /// in `BnScrollTests.testTheOffsetClampIsAClampAndItsFloorIsNotDecoration`.
    ///
    /// The reachable range, in the viewport's own terms: content − viewport, **floored at
    /// 0**. `contentInsetAdjustmentBehavior` is OFF (see [makeView]), so there is no inset
    /// to fold in, and this is the same arithmetic Android's `ScrollView` does.
    ///
    /// **THE FLOOR IS NOT DECORATION.** Content SHORTER than the viewport — a list that
    /// empties, a filter matching nothing, M7's first under-full frame — makes the maximum
    /// NEGATIVE, and `min(max(0, 600), −120)` is **−120**: the page would sit scrolled
    /// 120pt ABOVE its own content, permanently, because `UIScrollView` does not correct a
    /// programmatically-set out-of-range offset at rest. Floored, the answer is 0. (The
    /// shrink test's three acts are what keep the floor honest: 800 → 240 leaves a POSITIVE
    /// maximum of 40 and never engages it.)
    internal static func clampedOffset(_ offset: CGPoint,
                                       contentSize: CGSize,
                                       viewport: CGSize) -> CGPoint {
        let maxX = max(0, contentSize.width - viewport.width)
        let maxY = max(0, contentSize.height - viewport.height)
        return CGPoint(x: min(max(0, offset.x), maxX), y: min(max(0, offset.y), maxY))
    }

    /// **THE DEFINITE-HEIGHT WARNING** (design §"The constraint this introduces") — the
    /// twin of `YogaLayout.warnIfIndefiniteHeight`, condition for condition.
    ///
    /// An `auto`-height scroll node takes its height **from** its content: the viewport
    /// IS the content, the scrollable range is zero, and the page silently never moves.
    /// No exception, no dropped patch, no wrong frame — which makes it one of the most
    /// baffling symptoms this engine can produce.
    ///
    /// Two conditions, and BOTH are needed:
    ///
    ///  - the DECLARED height is neither a point nor a percent
    ///    (`bn_yoga_node_has_declared_height`) — the author gave it no definite height.
    ///    Not sufficient alone: `Grow="1" Basis="0"` in a bounded parent declares no
    ///    height either and is the *recommended* flex-sized shape.
    ///  - and it COMPUTED OUT **exactly** as tall as its content — the symptom itself,
    ///    read off the frames after the layout pass, so it cannot be fooled by the route
    ///    the height took.
    ///
    /// **EXACTLY, not "at least"** — and the difference is the whole worth of the
    /// diagnostic. A viewport TALLER than its content is **not a mistake**: it is a
    /// viewport with nothing to scroll YET (a list still loading; M7's virtualized list
    /// on its first under-full frame), and it scrolls the moment the content grows past
    /// it. Warning there would fire on the shape the docs prescribe. (Gate 2 shipped `>=`
    /// and its review caught it; the two negative tests are what keep this an equality.)
    ///
    /// **`Grow="1"` ALONE IS WARNED ABOUT, AND CORRECTLY**: it leaves `flexBasis: auto`,
    /// so the viewport's basis is its CONTENT's height (800), the free space against a
    /// shorter parent is NEGATIVE, `flexGrow` only distributes POSITIVE free space, and
    /// the negative goes to the shrink pass — where Yoga's `flexShrink` default is 0.
    /// The viewport keeps its content's height and never scrolls. (The phase's own
    /// mechanism, one level up. It is why CSS's `flex: 1` sets basis to 0.)
    ///
    /// Warn-once per node ([diagnose]): a layout pass runs per committed frame, and a
    /// warning per frame is a flood — and a flood is a thing people mute.
    private func warnIfIndefiniteHeight(scrollId: Int32, contentNode: UnsafeMutableRawPointer) {
        guard let scrollNode = yogaNodes[scrollId] else { return }
        guard bn_yoga_node_has_declared_height(scrollNode) == 0 else { return }
        let content = bn_yoga_node_get_frame(contentNode)
        guard content.height > 0 else { return }
        let scroll = bn_yoga_node_get_frame(scrollNode)
        guard abs(scroll.height - content.height) <= Self.epsilon else { return }
        diagnose(
            nodeId: scrollId, kind: "auto-height",
            message: "scroll node \(scrollId) has no definite height: it computed to "
            + "\(scroll.height)pt, which is exactly its content's height — so there is NOTHING TO "
            + "SCROLL. A viewport takes its height from somewhere definite: an explicit Height, or "
            + "Grow=\"1\" WITH Basis=\"0\" inside a bounded parent (CSS's `flex: 1`). Grow=\"1\" "
            + "ALONE is not enough: flexBasis stays `auto`, so the basis is the CONTENT's height, "
            + "the free space is negative, and flexGrow only distributes POSITIVE free space — the "
            + "negative goes to the shrink pass, and Yoga's flexShrink default is 0.")
    }

    /// A scroll node's runtime diagnostic — **warn-once**, keyed by `(nodeId, kind)`.
    /// ONE key convention for both diagnostics: the node id is a FIELD, not a fragment of
    /// a string, because [handleRemove] EVICTS by it — **node ids are REUSED** (.NET's
    /// restart at 1 after a reset), and a warn-once key that outlives its node silences
    /// the node that inherits its id. The diagnostic is worth most on a freshly-written
    /// page, which is exactly when a ghost key would eat it.
    private func diagnose(nodeId: Int32, kind: String, message: String) {
        guard diagnosedKeys.insert(BnDiagnosticKey(nodeId: nodeId, kind: kind)).inserted else { return }
        diagnosed.append((nodeId, message))
        NSLog("[BnWidgetMapper] \(message)")
    }

    /// Half a point — below the 0.5 tolerance every frame assertion in this engine
    /// already uses; a float comparison of two computed heights needs one. Kotlin's
    /// `YogaLayout.EPSILON` is the same number.
    private static let epsilon: Float = 0.5

    // ── AttachEvent/DetachEvent → UIControl targets (Phase 5.3) ───────────────

    /// Wires a control-event target that forwards to `onUiEvent`. NodeId resolution
    /// rides the text-collapse (twin of WidgetMapper.handleAttachEvent): the
    /// renderer attaches against the interactive element's OWN nodeId (e.g. the
    /// button view, not its text child), so `views[nodeId]` is the real control.
    /// Last-wins: a re-attach for the same (node, event) removes the prior target
    /// before adding — no stacked dispatches.
    ///   click → UIButton .touchUpInside (payload nil)
    ///   change → UITextField .editingChanged (payload = current text)
    ///   focus/blur → UITextField .editingDidBegin/.editingDidEnd (payload nil)
    private func handleAttachEvent(nodeId: Int32, eventName: String, handlerId: Int32) {
        guard let control = views[nodeId] as? UIControl else {
            NSLog("[BnWidgetMapper] AttachEvent '\(eventName)' for node \(nodeId): not a UIControl — ignored")
            return
        }
        let controlEvent: UIControl.Event
        let payload: () -> String?
        switch eventName {
        case "click":
            guard control is UIButton else {
                NSLog("[BnWidgetMapper] AttachEvent 'click' ignored: node \(nodeId) is not a UIButton"); return
            }
            controlEvent = .touchUpInside
            payload = { nil }
        case "change":
            guard let field = control as? UITextField else {
                NSLog("[BnWidgetMapper] AttachEvent 'change' ignored: node \(nodeId) is not a UITextField"); return
            }
            controlEvent = .editingChanged
            payload = { [weak field] in field?.text ?? "" }
        case "focus":
            guard control is UITextField else {
                NSLog("[BnWidgetMapper] AttachEvent 'focus' ignored: node \(nodeId) is not a UITextField"); return
            }
            controlEvent = .editingDidBegin
            payload = { nil }
        case "blur":
            guard control is UITextField else {
                NSLog("[BnWidgetMapper] AttachEvent 'blur' ignored: node \(nodeId) is not a UITextField"); return
            }
            controlEvent = .editingDidEnd
            payload = { nil }
        default:
            NSLog("[BnWidgetMapper] AttachEvent '\(eventName)' not supported (forward compat): skipped")
            return
        }
        let key = EventKey(nodeId: nodeId, event: eventName)
        removeTarget(for: key) // last-wins
        let target = BnControlTarget { [weak self] in
            self?.onUiEvent(handlerId, eventName, payload())
        }
        control.addTarget(target, action: #selector(BnControlTarget.fire), for: controlEvent)
        eventTargets[key] = (control, target)
    }

    private func handleDetachEvent(nodeId: Int32, eventName: String) {
        let key = EventKey(nodeId: nodeId, event: eventName)
        if eventTargets[key] == nil {
            NSLog("[BnWidgetMapper] DetachEvent '\(eventName)' for node \(nodeId): no live target — ignored")
        }
        removeTarget(for: key)
    }

    /// Removes and drops the target for a key (removeTarget for all events — one
    /// target serves one control-event, so `.allEvents` fully detaches it).
    private func removeTarget(for key: EventKey) {
        if let (control, target) = eventTargets.removeValue(forKey: key) {
            control.removeTarget(target, action: nil, for: .allEvents)
        }
    }

    // ── CreateNode: build the view + its Yoga twin, honour the collapse ───────

    private func handleCreate(nodeId: Int32, nodeType: String, parentId: Int32?, insertIndex: Int32) {
        // Text-child-of-non-container collapse (twin of WidgetMapper.handleCreate):
        // a `text` node whose parent is a text-bearing NON-container (UILabel/
        // UIButton/UITextField) does not get its own view — alias its nodeId onto the
        // parent so the subsequent ReplaceText sets the parent's title/text.
        //
        // Phase 6.1: it therefore gets NO YOGA NODE either. The Yoga tree mirrors the
        // VIEW tree, not the patch tree — give this node one and the two trees' child
        // indices skew from here on, and every frame after it is wrong.
        if nodeType == "text", let pid = parentId, let rawParent = views[pid],
           isTextBearingNonContainer(rawParent) {
            views[nodeId] = rawParent
            collapsedAliases.insert(nodeId) // it ALIASES the parent; it owns nothing
            return
        }

        let view: UIView = makeView(nodeType: nodeType)

        // Phase 6.2 — THE SYNTHETIC CONTENT VIEW. Keyed on the NODETYPE, not on
        // `view is UIScrollView`: the nodeType is the CONTRACT ("a `scroll` node is a
        // viewport"), and the widget class is a row in a table that could change (a
        // horizontal `scroll` would be a different configuration and would still owe a
        // content node). Two ways of asking one question is how the two trees end up
        // disagreeing about which nodes are scroll nodes.
        if nodeType == Self.scroll {
            // …and the cast is a GUARD, not a condition, because **a `scroll` node that
            // failed it would silently STOP BEING A SCROLL NODE.** As an `if` this branch
            // could do NOTHING and say nothing, and all four of the consequences are
            // silent: no content VIEW, so [containerFor] parents the wire children
            // straight into the viewport; no content NODE, so `contentSize` is never set
            // and the page cannot scroll; membership in [contentNodes] is what makes a
            // node "a scroll node" to [handleSetStyle], so the container-style ignore rule
            // stops applying; and it is also what drives [warnIfIndefiniteHeight], so the
            // one diagnostic that would have explained the dead page never fires either.
            // Unreachable while [makeView] answers `scroll` with a `UIScrollView` — this
            // is posture, on the platform where silence is the risk. **Android's twin
            // THROWS** (`view as ScrollView`); this aborts the create, loudly in debug and
            // logged in release, and registers NOTHING (the `views` entry is made below).
            guard let scroll = view as? UIScrollView else {
                NSLog("[BnWidgetMapper] node \(nodeId) is a `scroll` node but its view is a "
                      + "\(type(of: view)), not a UIScrollView — the node is DROPPED. It would "
                      + "otherwise be a scroll node with no content view (children parented into "
                      + "the viewport itself), no content node (no contentSize, nothing scrolls), "
                      + "no container-style ignore rule, and no definite-height diagnostic.")
                assertionFailure("`scroll` node \(nodeId) did not get a UIScrollView from makeView")
                return
            }
            let content = BnScrollContentView()
            scroll.addSubview(content)
            scrollContents[nodeId] = content
        }

        views[nodeId] = view

        // The view a child of this node parents INTO: for a child of a scroll node that
        // is the CONTENT view, never the UIScrollView (non-negotiable #2).
        //
        // insertIndex counts HOST views in the target container 1:1 (collapsed text
        // nodes never materialize a view, and they alias onto non-container parents,
        // so they cannot skew a container's indices — the same invariant as Android).
        // -1 = append (explicit; 0 is a valid front index).
        let parentView: UIView = containerFor(parentId) ?? root
        if insertIndex >= 0 && Int(insertIndex) <= parentView.subviews.count {
            parentView.insertSubview(view, at: Int(insertIndex))
        } else {
            parentView.addSubview(view)
        }

        // The Yoga twin, inserted at the SAME index in the SAME parent. The parent is
        // re-derived from the view we ACTUALLY parented to (not from the patch), so an
        // unknown parentId that fell back to the host root falls back to the Yoga host
        // root too — and a child of a scroll node lands under the CONTENT node, because
        // `viewToNode[contentView]` IS the content node. One rule, both trees.
        let node = bn_yoga_node_new()
        yogaNodes[nodeId] = node
        viewToNode[ObjectIdentifier(view)] = node
        if Self.measuredNodeTypes.contains(nodeType) {
            bn_yoga_node_set_measure(node, bnYogaMeasureTrampoline,
                                     Unmanaged.passUnretained(view).toOpaque())
        }
        // …and the Yoga tree gets its synthetic node in the same breath as the view tree
        // got its synthetic view. BOTH TREES OR NEITHER. (The `.mm` sets the viewport's
        // `overflow: scroll` and the content node's three styles — and, load-bearingly,
        // NOT its flexShrink.)
        if let contentView = scrollContents[nodeId] {
            let contentNode = bn_yoga_node_attach_scroll_content(node)
            contentNodes[nodeId] = contentNode
            viewToNode[ObjectIdentifier(contentView)] = contentNode
        }
        let parentNode: UnsafeMutableRawPointer = (parentView === root)
            ? hostRoot
            : (viewToNode[ObjectIdentifier(parentView)] ?? hostRoot)
        bn_yoga_node_insert_child(parentNode, node, insertIndex)
    }

    /// The view a child of [parentId] parents into — **the second index-mapping rule**
    /// (non-negotiable #2), stated once.
    ///
    /// A `scroll` node's children go into its CONTENT view, not into the `UIScrollView`
    /// itself (whose subviews also include UIKit's own scroll indicators, so indexing
    /// them would be wrong twice over). `insertIndex` then counts the content view's
    /// children, and `-1` (append) appends to them — exactly mirroring the Yoga tree,
    /// which redirects to the synthetic content NODE by the same rule. The two trees
    /// mirror each other; neither mirrors the patch tree.
    ///
    /// Nil when [parentId] is nil or names an unknown node — the caller falls back to
    /// the host root, and re-derives the Yoga parent from the view it actually parented
    /// to.
    private func containerFor(_ parentId: Int32?) -> UIView? {
        guard let parentId = parentId else { return nil }
        if let content = scrollContents[parentId] { return content }
        return views[parentId]
    }

    private func makeView(nodeType: String) -> UIView {
        switch nodeType {
        case "view":
            // Phase 6.1: a plain UIView — children are absolutely placed by Yoga,
            // nothing is stacked. (An un-styled tree still LOOKS stacked: Yoga's
            // default flexDirection is column.)
            return UIView()
        case "text":
            let label = UILabel()
            label.numberOfLines = 0
            return label
        case "button":
            let button = UIButton(type: .system)
            return button
        case "input":
            let field = UITextField()
            field.borderStyle = .roundedRect
            return field
        case "image":
            // A REAL UIImageView, not a placeholder UIView — because `image` is a
            // MEASURED nodetype ([measuredNodeTypes]) and `UIView.sizeThatFits`
            // returns the view's CURRENT BOUNDS: a placeholder would measure ITSELF,
            // a self-referential answer that converges to 0 and diverges from
            // Android's `ImageView`, which measures its drawable. Benign today (no
            // demo mounts an `image`) and a trap for 6.3. An empty UIImageView
            // answers `sizeThatFits` with .zero, which is exactly what an
            // ImageView with no drawable measures to — so the two shells agree, by
            // construction, on the only case that exists today. (Gate 4 ledger,
            // entry 1.)
            return UIImageView()
        case Self.scroll:
            // Phase 6.2 — a VIEWPORT. Vertical, like Android's ScrollView (design
            // decision 2 — horizontal is ledgered); its single meaningful child, the
            // synthetic content view, is created by [handleCreate].
            let scroll = UIScrollView()
            // ── clipsToBounds: the VIEWPORT CLIPS, and it is the ONE container in this
            // shell that does ────────────────────────────────────────────────────────
            // Every other container here is a plain UIView, which does NOT clip — that
            // is Yoga's `overflow: visible` default, and it is what Android's
            // BnYogaFrameLayout sets `clipChildren = false` to MATCH. **Do not mirror
            // "our containers don't clip" onto the viewport**: a viewport that does not
            // clip draws all 800pt of its content over the whole screen. `true` is
            // UIScrollView's own default; it is written out because the rule it breaks
            // is written down elsewhere, and a reader has to be able to see that the
            // break is deliberate.
            scroll.clipsToBounds = true
            // ── contentInsetAdjustmentBehavior: OFF, and it is a PARITY knob ─────────
            // The default (.automatic) lets UIKit fold the safe-area inset into
            // `adjustedContentInset`, which shifts the resting `contentOffset` to
            // −inset and moves the maximum offset by the same amount. Android's
            // ScrollView has no such notion, so the two shells' scroll ranges — 600 here,
            // and the number BnScrollDemoAndroidTest asserts — would differ by a device
            // constant, on the device only (it is zero in a detached test host, which is
            // the worst possible place for a divergence to hide). Yoga owns placement;
            // the safe area is the app's business, not the viewport's.
            scroll.contentInsetAdjustmentBehavior = .never
            return scroll
        case "picker":
            // Still stubbed (6.1's honest boundary, minus `scroll`): a framework
            // container that runs its OWN layout over its children, so Yoga does not get
            // the final word inside one. Keep a placeholder so container indices stay
            // consistent.
            NSLog("[BnWidgetMapper] nodeType 'picker' stubbed as a placeholder UIView (Phase 6.3+)")
            return UIView()
        default:
            NSLog("[BnWidgetMapper] Unknown nodeType '\(nodeType)' — falling back to UILabel")
            return UILabel()
        }
    }

    /// UILabel/UIButton/UITextField are the text-bearing collapse targets (twin of
    /// Android's `is TextView && !is ViewGroup`). A plain UIView — what a `view` node
    /// is since 6.1 — is not one, so a text child of a container still gets its own
    /// UILabel.
    private func isTextBearingNonContainer(_ view: UIView) -> Bool {
        return view is UILabel || view is UIButton || view is UITextField
    }

    // ── ReplaceText: route through the collapsed parent's title/text ──────────

    private func handleReplaceText(nodeId: Int32, text: String) {
        guard let view = views[nodeId] else { return }
        if let label = view as? UILabel {
            label.text = text
        } else if let button = view as? UIButton {
            button.setTitle(text, for: .normal)
        } else if let field = view as? UITextField {
            field.text = text
        } else {
            return
        }
        // Phase 6.1: new text = new intrinsic size. Yoga CACHES a measure function's
        // result and will not re-run it unless the node is dirtied — without this the
        // label keeps the frame its OLD text measured to, and the row keeps hugging
        // that. Resolved by VIEW, which is what makes the collapse work: this nodeId
        // may be an alias for the parent UIButton.
        markDirty(view)
    }

    /// Invalidates the measure cache of the node that PLACES [view]. A no-op for a
    /// view whose node carries no measure function.
    private func markDirty(_ view: UIView) {
        guard let node = viewToNode[ObjectIdentifier(view)] else { return }
        bn_yoga_node_mark_dirty(node)
    }

    // ── RemoveNode: ONE patch stands for a WHOLE SUBTREE ─────────────────────

    /// The renderer emits **one** `RemoveNodePatch` for a whole subtree — its
    /// `PurgeNodeSubtree` is .NET-side bookkeeping. So the host purges the subtree
    /// itself, in BOTH trees. Dropping only the named node would leave every
    /// descendant in the registries forever, each pinning a UIView — and, worse than
    /// on Android, leaking its RAW Yoga node, which nothing will ever free. Every
    /// navigation replaces the tree.
    ///
    /// The subtree is read off the VIEW hierarchy and matched by IDENTITY, never by
    /// key: the text collapse aliases nodeIds onto a view they do not own.
    private func handleRemove(nodeId: Int32) {
        // An ALIAS (collapsed text node) owns NO view and NO Yoga node: drop the map
        // entry and stop. Removing its view would detach the parent UIButton, and the
        // Yoga node it would remove is the button's — Yoga would keep reserving space
        // for a widget no longer on screen.
        if collapsedAliases.remove(nodeId) != nil {
            views.removeValue(forKey: nodeId)
            return
        }
        guard let view = views[nodeId] else { return }
        let doomed = subtree(of: view)

        // Yoga FIRST: free_subtree clears every measure function in the subtree,
        // breaking the last edge from a native node to a UIView that is about to be
        // released.
        //
        // Phase 6.2: a doomed subtree may contain SCROLL nodes — this one, or (what
        // navigation actually emits) any number of them under a named ancestor — and each
        // one's SYNTHETIC CONTENT NODE is a Yoga CHILD of it, so `free_subtree` reclaims
        // it here whether or not this shell remembers it exists. **A `contentNodes` entry
        // that survives this call is a DANGLING `YGNodeRef`**, and the next
        // `calculateAndApply` dereferences it — on Android the same miss is merely a
        // leak. ONE RemoveNodePatch stands for a whole subtree, and the synthetic node
        // that no patch could ever name is part of it.
        if let node = yogaNodes[nodeId] {
            if let owner = bn_yoga_node_get_owner(node) {
                bn_yoga_node_remove_child(owner, node)
            }
            bn_yoga_node_free_subtree(node)
        }

        // The doomed IDS first, then ONE sweep per map — the shape Kotlin's
        // `YogaLayout.removeNode` already has (`doomedIds` → `removeAll { it.first in
        // doomedIds }`). The two purges are meant to be read side by side, and the
        // diagnostics sweep is the reason it is not merely cosmetic: nested in the map
        // loop it was a `removeAll` over the whole diagnostics list PER doomed id.
        let doomedIds = Set(views.filter { doomed.contains(ObjectIdentifier($0.value)) }.keys)
        for id in doomedIds {
            views.removeValue(forKey: id)
            yogaNodes.removeValue(forKey: id)
            collapsedAliases.remove(id) // an aliased text child of a doomed UIButton
            // The scroll node's two synthetic halves, dropped together (the content VIEW
            // is in `doomed` — it is a subview of the UIScrollView — so this entry is the
            // only thing that would survive to pin it).
            contentNodes.removeValue(forKey: id)
            scrollContents.removeValue(forKey: id)
        }
        // …and the DIAGNOSTICS BOOKKEEPING, for the reason [diagnose] gives: node ids are
        // REUSED. A warn-once key that outlives its node silences the warning the node
        // inheriting its id would have earned — and the message list would otherwise grow
        // by one per navigation, forever.
        diagnosed.removeAll { doomedIds.contains($0.nodeId) }
        diagnosedKeys = diagnosedKeys.filter { !doomedIds.contains($0.nodeId) }
        for identity in doomed {
            viewToNode.removeValue(forKey: identity)
        }
        // Purge event targets registered anywhere in the doomed subtree, by IDENTITY
        // (the collapse can alias several nodeIds onto one control, so a target may
        // sit under a different key than the one being removed).
        for (key, entry) in eventTargets where doomed.contains(ObjectIdentifier(entry.control)) {
            entry.control.removeTarget(entry.target, action: nil, for: .allEvents)
            eventTargets.removeValue(forKey: key)
        }
        view.removeFromSuperview()
    }

    /// [view] and every descendant, as an identity set.
    private func subtree(of view: UIView) -> Set<ObjectIdentifier> {
        var out: Set<ObjectIdentifier> = [ObjectIdentifier(view)]
        for sub in view.subviews {
            out.formUnion(subtree(of: sub))
        }
        return out
    }

    // ── UpdateProp: value / placeholder ──────────────────────────────────────

    private func handleUpdateProp(nodeId: Int32, name: String, value: String?) {
        guard let view = views[nodeId] else {
            NSLog("[BnWidgetMapper] UpdateProp for unknown nodeId \(nodeId): ignored")
            return
        }
        switch name {
        case "placeholder":
            if let field = view as? UITextField {
                field.placeholder = value
                markDirty(field) // the placeholder sizes an empty UITextField
            } else {
                NSLog("[BnWidgetMapper] UpdateProp placeholder ignored: node \(nodeId) is not a UITextField")
            }
        case "value":
            if let field = view as? UITextField {
                let newValue = value ?? ""
                // The @bind write-back — the iOS SIMPLIFICATION (design §3): NO
                // applyingBatch re-entrancy guard. UIKit does NOT fire
                // .editingChanged on a programmatic `.text` set (unlike Android's
                // TextWatcher), so this write can never re-enter the change lane —
                // the bind loop cannot form. The inequality check stays purely to
                // preserve the caret/IME marked-text mid-typing (reassigning
                // `.text` resets the selection). When the runtime pushes a genuinely
                // new value (e.g. Clear), the caret moves to the end.
                if field.text != newValue {
                    field.text = newValue
                    let end = field.endOfDocument
                    field.selectedTextRange = field.textRange(from: end, to: end)
                    markDirty(field) // new content = new intrinsic size
                }
            } else {
                NSLog("[BnWidgetMapper] UpdateProp value ignored: node \(nodeId) is not a UITextField")
            }
        case "enabled":
            if let control = view as? UIControl {
                control.isEnabled = (value as NSString?)?.boolValue ?? true
            }
        default:
            NSLog("[BnWidgetMapper] UpdateProp '\(name)' not yet supported (Phase 6.2+ extends)")
        }
    }

    // ── SetStyle: THE ROUTER (Phase 6.1) ─────────────────────────────────────

    /// The SetStyle allow-list is PARTITIONED (design §"The allow-list is a routing
    /// table"), and every style name goes to exactly ONE destination:
    ///
    ///  - a **LAYOUT** name (`bn_yoga_is_layout_style` — flexDirection, width,
    ///    height, padding, margin, …) → the node's **Yoga node**, and nowhere else.
    ///  - a **VISUAL** name (backgroundColor, fontSize, …) → the **UIView** (paint).
    ///
    /// `padding`'s old `layoutMargins` / `isLayoutMarginsRelativeArrangement` arm is
    /// GONE, deliberately: Yoga lays a container's children out INSIDE its padding
    /// box, so a surviving view-level inset would apply it a SECOND time and put
    /// every child in that container off by it. Same reasoning retires any view-level
    /// width/height/margin.
    ///
    /// Matching is ordinal/case-sensitive — the same discipline .NET's ordinal
    /// allow-list and Kotlin's keep, so a mis-cased name falls onto the prop wire
    /// (visible) rather than being silently swallowed here.
    private func handleSetStyle(nodeId: Int32, property: String, value: String?) {
        guard let view = views[nodeId] else {
            NSLog("[BnWidgetMapper] SetStyle for unknown nodeId \(nodeId): ignored")
            return
        }
        // Phase 6.2 — **CONTAINER STYLES ON A `scroll` NODE ARE IGNORED AND LOGGED**
        // (design decision 6; the six names live in the `.mm`'s
        // `kScrollIgnoredContainerStyles`, pinned EQUAL to Kotlin's list by
        // `ShellStyleTableDriftTests`). A scroll node's container layout belongs to the
        // shell: its only Yoga child is the synthetic content node, whose styles are
        // fixed. `BnScroll`'s component surface cannot produce these — it forwards
        // BnView's ITEM parameters and none of its CONTAINER ones — but the raw-element
        // hatch can (`OpenElement("scroll") + AddAttribute("gap", …)` reaches the wire,
        // and a .NET test pins that it does, precisely so this rule is known to be LIVE
        // code rather than silently becoming dead).
        //
        // Membership in [contentNodes] is what makes a node "a scroll node" here.
        if contentNodes[nodeId] != nil, bn_yoga_is_scroll_ignored_container_style(property) != 0 {
            diagnose(
                nodeId: nodeId, kind: "container-style/\(property)",
                message: "SetStyle \(property) ignored: node \(nodeId) is a `scroll` node, and a "
                + "scroll node's CONTAINER layout belongs to the shell — its only Yoga child is the "
                + "synthetic content node, whose styles are fixed. Put the layout on a column INSIDE "
                + "the scroll. (Item styles and backgroundColor apply normally.)")
            return
        }
        if bn_yoga_is_layout_style(property) != 0 {
            guard let node = yogaNodes[nodeId] else {
                // A collapsed text alias owns no Yoga node. Android drops the style the
                // same way (its YogaLayout has no entry for the alias id).
                NSLog("[BnWidgetMapper] SetStyle \(property) ignored: node \(nodeId) has no Yoga node")
                return
            }
            // A NULL value RESETS the property to Yoga's default — the wire's reset
            // (the other half of Gate 1's UpdateProp→SetStyle null-reset fix).
            property.withCString { name in
                if let value = value {
                    value.withCString { _ = bn_yoga_node_set_style(node, name, $0) }
                } else {
                    _ = bn_yoga_node_set_style(node, name, nil)
                }
            }
            return
        }
        switch property {
        case "backgroundColor":
            guard let color = value.flatMap(BnColor.parse) else {
                NSLog("[BnWidgetMapper] SetStyle backgroundColor ignored: \(value ?? "nil")")
                return
            }
            view.backgroundColor = color
        case "fontSize":
            guard let label = view as? UILabel else {
                NSLog("[BnWidgetMapper] SetStyle fontSize ignored: node \(nodeId) is not a UILabel")
                return
            }
            guard let size = parseCGFloat(value) else {
                NSLog("[BnWidgetMapper] SetStyle fontSize ignored: \(value ?? "nil")")
                return
            }
            label.font = UIFont.systemFont(ofSize: size)
            markDirty(label) // a bigger font is a bigger intrinsic size
        default:
            NSLog("[BnWidgetMapper] SetStyle '\(property)' not yet supported (Phase 6.2+ extends)")
        }
    }

    /// The LEGACY VISUAL props' tolerant number parser (fontSize) — it strips a
    /// trailing sp/dp/px, exactly as Kotlin's `parseFloatOrNull` still does. The
    /// LAYOUT grammar has NO unit suffixes and `BnYogaLayout.mm` does not strip them;
    /// this tolerance survives only for the visual props that shipped before the
    /// grammar existed.
    private func parseCGFloat(_ s: String?) -> CGFloat? {
        guard var t = s else { return nil }
        for suffix in ["sp", "dp", "px"] where t.hasSuffix(suffix) {
            t = String(t.dropLast(suffix.count))
        }
        guard let d = Double(t) else { return nil }
        return CGFloat(d)
    }
}

/// #RRGGBB / #AARRGGBB → UIColor (twin of Android Color.parseColor, which treats
/// a bare "#FFEEAA" as opaque and "#AARRGGBB" as alpha-first).
enum BnColor {
    static func parse(_ s: String) -> UIColor? {
        guard s.hasPrefix("#") else { return nil }
        let hex = String(s.dropFirst())
        guard let value = UInt64(hex, radix: 16) else { return nil }
        let r, g, b, a: CGFloat
        switch hex.count {
        case 6: // RRGGBB — opaque
            r = CGFloat((value >> 16) & 0xFF) / 255
            g = CGFloat((value >> 8) & 0xFF) / 255
            b = CGFloat(value & 0xFF) / 255
            a = 1
        case 8: // AARRGGBB
            a = CGFloat((value >> 24) & 0xFF) / 255
            r = CGFloat((value >> 16) & 0xFF) / 255
            g = CGFloat((value >> 8) & 0xFF) / 255
            b = CGFloat(value & 0xFF) / 255
        default:
            return nil
        }
        return UIColor(red: r, green: g, blue: b, alpha: a)
    }
}
