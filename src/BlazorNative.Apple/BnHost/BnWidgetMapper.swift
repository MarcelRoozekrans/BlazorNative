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
//
// ── PHASE 6.3: `image` — THE LAST STUBBED LEAF GETS A SOURCE ─────────────────
// `image` made a widget and measured ZERO, because no shell had a source-loading
// path. `UpdateProp("src", …)` now drives one ([handleSrc]): Kingfisher fetches and
// decodes, and on the MAIN thread the shell sets the image, records the decoded
// bytes' NATURAL PIXEL SIZE, marks the Yoga node dirty (the 6.1 path) and re-solves
// (the 6.2 path). **ONE reflow, never two.** `src` is a PROP, not a style — a URL is
// neither layout nor paint — so it rides the `UpdateProp` wire and not the
// partitioned SetStyle routing table.
//
// Four rules of the parity contract are held HERE and are stated where they bite,
// because Coil and Kingfisher do NOT do them the same way by default:
//
//   1. **THE UNIT — one file pixel is one point.** [BnImageLoader.naturalPixelSize]
//      and [BnImageView]; Android's twin is `bitmap.width`. An `image` therefore gets
//      a MEASURE FUNCTION OF ITS OWN ([bnYogaImageMeasureTrampoline]) — it measures
//      its BYTES, not its widget.
//   2. **A MEMORY-CACHE HIT COMPLETES SYNCHRONOUSLY**, inside the patch batch — so
//      [resolveLayout] is a NO-OP inside a batch, and the in-flight task is recorded
//      only if it is STILL LIVE ([handleSrc]'s tail).
//   3. **THE STALE-CALLBACK GUARD IS GENERATION *AND* IDENTITY**, in EVERY terminal
//      callback and not just the painting one — and it is **ONE function asked at BOTH
//      call sites** (`bnIsLiveImageRequest`, from [isLive] and from [clearIfMine]), so
//      the one unit test that pins the conjunction defends both. Getting this wrong
//      leaves a request nothing can cancel, whose completion marks a **freed
//      `YGNodeRef`** dirty.
//   5. **EVERY `src` WRITE BUMPS THE GENERATION — INCLUDING A CLEAR** ([handleSrc]).
//      `cancelImageRequest` is best-effort BY DEFINITION; the generation is what makes
//      the completion it failed to prevent harmless. A `Src` → null whose cancel lost
//      the race would otherwise RE-INFLATE the node the author just cleared.
//   4. **CONTENT MODE: aspect-fit, EXPLICITLY** ([makeView]) — `UIImageView`'s default
//      is `.scaleToFill` (a STRETCH) and Android's `ImageView` is `FIT_CENTER`. The
//      divergence is FRAME-NEUTRAL, so no frame table on either platform can see it.
//
// ── PHASE 7.2: THE onScroll WIRE — THE FIRST 60Hz PRODUCER ───────────────────
// A scroll node with the `scroll` event attached reports its content offset to
// .NET over the EXISTING dispatch wire — CONFLATED. The shell keeps ONE pending
// offset per scroll node ([BnScrollWire]); a new native sample REPLACES it (never
// queue — scroll position is idempotent STATE, not an event log) and at most one
// dispatch is in flight per node at a time: submit when the lane is free, conflate
// while it is not, dispatch the freshest value on the completion signal
// ([maybeDispatchScroll]). Offsets cross in POINTS — which ARE the density-
// independent unit Yoga computes in, so unlike Android (px ÷ density at the
// source) iOS has NO conversion site at all — as an invariant float payload,
// exactly what `NativeRenderer.ParseScrollOffset` parses. The contract is
// NORMATIVE (docs/plans/2026-07-15-phase-7.2-design.md §"The wire contract");
// this shell mirrors Android's CONFLATION and its OBSERVABLES, not its
// mechanics:
//
//  - the sample source is the `UIScrollView` DELEGATE ([BnScrollDelegateProxy] →
//    `scrollViewDidScroll`), not a `setOnScrollChangeListener` — same shape
//    though: a single slot per view, last-wins, firing on the main thread for
//    finger drags, deceleration ticks AND programmatic offset writes alike;
//  - **self-inflicted offset writes DISPATCH, deliberately.** Android's
//    mid-batch re-clamp echo (ScrollView.onLayout's trailing scrollTo) reaches
//    .NET, and its tests pin that OBSERVABLE — a shell-corrected offset must
//    reach .NET or the window desyncs from the glass. iOS has no framework
//    re-clamp; what it has is 6.2's OWN explicit shrink clamp
//    ([applyScrollFrames]), whose `contentOffset` write fires the delegate
//    SYNCHRONOUSLY, inside `calculateAndApply`, inside the batch. That sample
//    conflates under the [applyingBatch] guard and the batch end flushes it
//    ([flushScrollWires]) — same conflate-during-a-batch, flush-after RULE,
//    different echo mechanism;
//  - ORDERING IS PRESERVED, NOT ASSUMED: the dispatch rides [BnRuntime]'s single
//    SERIAL `dispatchLane` — a serial DispatchQueue is FIFO, and scroll
//    dispatches enter the same queue tail as every tap and change event, so a
//    conflated scroll dispatch can never overtake a user-input event queued
//    before it. Pinned by `BnScrollWireTests`' lane-order test, not taken on
//    faith.
//
// Detach/purge is the 6.3 stale-callback discipline: DetachEvent and
// [handleRemove] delete the wire and its pending offset dies with it, never
// dispatched; a late completion resets a flag on an unreachable object and
// re-consults the LIVE map, so it is a no-op by construction.
//
// ── PHASE 7.3: FORM CONTROLS — checkbox/switch/slider, AND `picker` GOES REAL ─
// Three new NodeTypes (`checkbox` → UISwitch — decision 2: iOS HAS no native
// checkbox; `switch` → UISwitch; `slider` → UISlider) and the LAST stubbed
// widget becomes a real `UIPickerView` whose dataSource/delegate is the node's
// own [BnPickerState]. All four ride the EXISTING wires: `value` (and
// `min`/`max`/`step`, `items`/`selectedIndex`) on UpdateProp, selection changes
// back on `change`.
//
// THE PER-CONTROL LOOP GUARD — verified per control, never assumed (the
// design's own words), and the findings are the OPPOSITE of Android's:
//  - `UISwitch.setOn`, `UISlider.setValue` and `UIPickerView.selectRow` all
//    fire NOTHING (the 5.3 finding, re-verified per control by the
//    fires-nothing tests) — so iOS has NO `applyingBatch` dispatch guard on any
//    of the four, deliberately: an unneeded guard would swallow the very
//    verification that pins the platform behaviour. Android's CompoundButton/
//    ProgressBar fire SYNCHRONOUSLY (its guard is the batch flag) and its
//    Spinner fires on a LATER layout pass (its guard is the expected-selection
//    compare); none of that machinery exists here because none of the
//    mechanisms it guards exist here.
//  - What iOS DOES need: the slider's step-quantization DEDUP (a float-native
//    drag delivers a distinct `.valueChanged` per sample where Android's int
//    progress dedups structurally — the attach arm's slider case), and the
//    picker's SAME-ROW compare ([handlePickerUserSelect] — a wheel spun away
//    and back is not a change, matching AdapterView's own behaviour).
//
// THE STEP CONTRACT lives on [BnSliderState] (the wire payload is an exact
// `min + n×step` multiple — quantized at the dispatch site, because the widget
// is float-native); the picker's NORMATIVE CLAMP RULE (BnPicker.razor's
// header, mirrored) on [handleItems] — including notify-on-move with the
// CLAMPED index dispatched on the change wire, the items-before-selectedIndex
// + re-clamp-on-items order, and the empty→non-empty no-notify asymmetry.
//
// TWO iOS-26 PLATFORM FINDINGS (Gate 3, verified on the simulator — both are
// widgets refusing to hold what the shell hands them, in ways no earlier
// widget in this shell did):
//  - `UISlider` reconstructs `value` from a Float32 track FRACTION, so a
//    programmatic set of an exact step multiple reads back one ulp off
//    (60 → 60.000004 on 0…100). [BnSliderView] holds the exact programmatic
//    value; a real drag reads the platform's live value.
//  - `UISwitch` enforces its own intrinsic size on every frame write, so the
//    stretched layout box Yoga computes for the un-styled checkbox/switch
//    quartet is only observable on the Yoga node ([bnYogaFrame(of:)]) — the
//    view snaps back to the platform's own size. The measure path is NOT
//    involved: the shared trampoline already returns the imposed dimension
//    for an Exactly axis, and Yoga imposes the stretched width regardless.
//
// ── PHASE 7.4: `modal` — THE ANCHOR + OVERLAY PAIR, AND `activityindicator` ──
// A `modal` node materializes as TWO shell-side pieces, in both trees, in the
// same breath (the 6.2 synthetic-node machinery, pointed at the ROOT):
//
//  - the ANCHOR ([BnModalAnchorView]) at the modal's WIRE slot — shell-fixed
//    absolute/0×0, out of the flex flow entirely. THE THIRD INDEX-MAPPING RULE
//    (normative): it occupies the modal's slot in its wire parent, in the view
//    tree AND the Yoga tree, so sibling insert indices never skew; the modal's
//    wire child at index i is the OVERLAY's child at index i.
//  - the OVERLAY ([BnModalOverlayView]) attached LAST at the host root —
//    shell-fixed absolute/0/0/100%/100% + justify/align CENTER (the design's
//    ((W−w)/2, (H−h)/2) arithmetic IS that pair; the wire carries no layout for
//    it). Children redirect into it ([containerFor]); `scrimColor` is a PROP
//    painting it; the dismissal-request `click` recognizer listens on IT, never
//    the anchor. A top-level wire APPEND slots in AHEAD of live overlays
//    ("the overlay is LAST, always"). SetStyle on a modal node is
//    diagnosed-and-ignored for EVERY name at ONE site, before the routing.
//
// RemoveNode purges BOTH subtrees as a FIXPOINT ([handleRemove] — the overlay
// is NOT a descendant of the anchor, and a modal can sit inside another
// modal's overlay), with symmetric map eviction. THE iOS MEMORY-SAFETY LAW:
// a dangling YGNodeRef is a crash, not a leak — the two-subtree purge must
// free each Yoga node EXACTLY ONCE (overlays hang directly off hostRoot, so
// per-overlay frees can never overlap; a nested modal's anchor node is freed
// by the OUTER overlay's subtree free and only evicted thereafter).
//
// `click` grows past UIButton (design decision 4): plain views get a
// UITapGestureRecognizer whose delegate filter ([bnClickTouchIsOwn]) declines
// every touch not on the attached view ITSELF — a tap on the modal's content
// box must not dispatch the scrim's click, and the in-modal UISwitch keeps its
// own touches. Every click rides [dispatchClick] (counted — the dismissal
// REQUEST and the content-box swallow are invisible in every frame).
// `activityindicator` is a measured leaf (UIActivityIndicatorView.medium,
// spinning — animating-while-mounted is the contract), sized by ORACLE.
//
// ── PHASE 7.5: IMAGE POLISH — placeholderColor / contentMode / the `error` WIRE ─
// The three features 6.3 ledgered, with ZERO new measurement states. The
// placeholder is PAINT BY CONSTRUCTION ([BnImageView.bnShowPlaceholder] — a
// bounds-tracking color subview; [BnImageWireState]'s state table; never a
// markDirty, never a natural-size write — the measure func reads
// [BnImageView.bnNaturalSize], which no placeholder path touches). The `error`
// event is a new WORD on the existing dispatch wire (the scroll precedent):
// [onImageFailed]'s dispatch site sits behind [decideAndDispatchError] →
// `bnImageErrorDispatchAction` (a pure decision composing `bnIsLiveImageRequest`
// by name — one guard, two consumers; RE-ASKED at every fire time, including the
// deferred one), payload = the WIRE's src, verbatim, deferred out of a batch and
// never dropped — and on THIS shell the synchronous in-batch failure is REAL:
// `URL(string:)` → nil terminates inside `UpdateProp("src")` by construction,
// so the defer row runs live here where Android pins it on the JVM table. The
// content mode is paint-only (`bnContentModeFor`, the strict four-word table →
// `UIView.ContentMode`; unknown → diagnose-don't-apply, null → contain), and
// its corollary is iOS's own: `clipsToBounds = true` at creation ([makeView]),
// because `.scaleAspectFill`/`.center` paint past the Yoga box and iOS — unlike
// Android's ImageView — does not clip.
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

/// Phase 7.2 — the scroll node's sample source: a `UIScrollViewDelegate` whose ONLY
/// job is to forward `scrollViewDidScroll` into the mapper's conflation
/// (`onScrollSample`). The iOS shape of Android's `setOnScrollChangeListener` slot:
/// one per wired scroll node, last-wins, main-thread, firing for finger drags,
/// deceleration ticks and programmatic `contentOffset` writes alike — including the
/// shell's OWN 6.2 shrink clamp, whose write fires this synchronously inside the
/// batch (the iOS echo path; see the file header's Phase 7.2 section).
///
/// `UIScrollView.delegate` is WEAK, so the wire entry retains this proxy — the
/// same ownership shape as `BnControlTarget` in `eventTargets`. The closure holds
/// the mapper weakly and captures the nodeId; the sample resolves the wire from
/// the LIVE map at fire time, so a detached node's late sample no-ops (the 6.3
/// stale-callback discipline).
final class BnScrollDelegateProxy: NSObject, UIScrollViewDelegate {
    private let onSample: (UIScrollView) -> Void
    init(onSample: @escaping (UIScrollView) -> Void) {
        self.onSample = onSample
        super.init()
    }
    func scrollViewDidScroll(_ scrollView: UIScrollView) { onSample(scrollView) }
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

/// Phase 7.4 — **THE ANCHOR** (design decision 1): a `modal` node's WIRE view — a
/// 0-sized view at the modal's wire slot, shell-fixed `position: absolute; width: 0;
/// height: 0`, out of the flex flow entirely. It exists for exactly one reason, and
/// the rule is NORMATIVE (the THIRD index-mapping rule, after the text collapse and
/// the scroll content node): *the anchor occupies the modal's slot in its wire
/// parent, in the view tree AND the Yoga tree, so sibling insert indices never skew;
/// the modal's wire child at index i is the OVERLAY's child at index i.*
///
/// Android's anchor is a plain `View` (not a ViewGroup) so a redirection bug FAILS
/// LOUDLY — UIKit has no non-container view, so this shell cannot borrow that
/// posture; what it has instead is the named type (found by TYPE, the
/// [BnScrollContentView] discipline) and the zero-footprint frame pin, which a child
/// parented in here would break (the anchor is 0 × 0 absolute; anything inside it is
/// invisible and unplaced, and the demo's sibling frame table reddens).
final class BnModalAnchorView: UIView {}

/// Phase 7.4 — **THE OVERLAY** (design decision 1): the modal's SECOND shell-side
/// piece — a full-root container attached as the LAST child of the host root, with
/// shell-fixed Yoga styles `position: absolute; top: 0; left: 0; width: 100%;
/// height: 100%; justifyContent: center; alignItems: center`. The modal's wire
/// children parent into it ([BnWidgetMapper.containerFor]); the scrim paints it
/// (the `scrimColor` PROP); the dismissal-request `click` listens on it. Stacking
/// is creation order — the shell attaches last and never re-orders; a re-shown
/// modal is a re-created one and lands on top. A named type for the
/// [BnScrollContentView] reason: it is never on the wire, so tests can only find
/// it by TYPE and by "the root's LAST subview".
final class BnModalOverlayView: UIView {}

/// Phase 7.4 — **THE TOUCH-VIEW FILTER, as a pure decision** (design decision 4 —
/// NORMATIVE: *a tap dismiss-requests ONLY when the touch lands on the scrim view
/// itself, never on a descendant*). iOS enforces the rule with this filter on the
/// tap-recognizer arm; Android — where a non-clickable child's tap falls THROUGH to
/// the scrim — enforces it by the content box swallowing its own taps. Same
/// observable wire behavior, two named mechanisms.
///
/// A pure function for the `bnIsLiveImageRequest` reason: the delegate callback it
/// decides (`gestureRecognizer(_:shouldReceive:)`) takes a `UITouch`, which no
/// hosted XCTest can construct — so the DECISION lives here, unit-tested as a truth
/// table, and the delegate is one line that asks it.
func bnClickTouchIsOwn(touchView: UIView?, attachedView: UIView?) -> Bool {
    guard let touchView = touchView, let attachedView = attachedView else { return false }
    return touchView === attachedView
}

/// Phase 7.4 — **the `click`-on-a-plain-view arm** (design decision 4): iOS's
/// `click` was UIButton-only (target-action); non-control views get THIS — a
/// `UITapGestureRecognizer` that is its own target and its own delegate, filtered
/// by [bnClickTouchIsOwn] so it NEVER fires for a descendant's touch: a tap on the
/// modal's content box must not dispatch the scrim's click, and a control inside
/// the modal (the demo's UISwitch) must still receive its own touches — the filter
/// declines those touches outright, so the recognizer cannot delay or cancel them.
///
/// Retained by the attached view (`addGestureRecognizer`) AND by the mapper's
/// [BnWidgetMapper.clickRecognizers] map (the [eventTargets] ownership shape);
/// detached by DetachEvent and by the subtree purge.
final class BnClickTapRecognizer: UITapGestureRecognizer, UIGestureRecognizerDelegate {
    private let handler: () -> Void

    init(on view: UIView, handler: @escaping () -> Void) {
        self.handler = handler
        super.init(target: nil, action: nil)
        addTarget(self, action: #selector(bnHandleTap))
        delegate = self
        view.addGestureRecognizer(self)
    }

    @objc private func bnHandleTap() { handler() }

    /// The recognized tap's EFFECT, callable without a `UITouch` — the hand-rolled
    /// dispatch the device tests use where the simulator cannot synthesize touches
    /// (the 7.3 finding, recorded in BnFormDemoTests' header: a hosted XCTest
    /// cannot construct a UITouch, and `sendActions` has no recognizer analog).
    /// Production never calls it; the recognizer's own action does the same thing.
    func bnFire() { handler() }

    /// THE FILTER (see [bnClickTouchIsOwn]): `self.view` is the attached view —
    /// UIKit sets it at `addGestureRecognizer` and clears it at removal, so a
    /// detached recognizer declines everything. The DECISION lives on the
    /// instance (Gate 3 review M1: the delegate callback takes a `UITouch` no
    /// hosted XCTest can construct, so THIS is what the live-recognizer test
    /// calls — the untested seam is the `touch.view` property read alone).
    func bnShouldReceive(touchView: UIView?) -> Bool {
        bnClickTouchIsOwn(touchView: touchView, attachedView: view)
    }

    func gestureRecognizer(_ gestureRecognizer: UIGestureRecognizer,
                           shouldReceive touch: UITouch) -> Bool {
        bnShouldReceive(touchView: touch.view)
    }

    func bnDetach() { view?.removeGestureRecognizer(self) }
}

/// Phase 7.3 — the `slider` widget, and **THE EXACT-VALUE SHIM** the iOS 26
/// runtime made necessary.
///
/// `UISlider` on the iOS 26 SDK does not store `value` verbatim: it round-trips
/// it through the normalized track fraction `(value − min) / (max − min)` in
/// Float32 and reconstructs `min + fraction × range` on read. For most step
/// multiples the fraction is not representable — `setValue(60)` on the demo's
/// 0…100 slider stores 0.6f (= 0.60000002…) and reads back **60.000004**, one
/// ulp above the exact multiple the shell just computed. The step contract
/// says the widget and the payload agree on the EXACT multiple (Android's int
/// progress gives that structurally — reading progress back cannot drift), so
/// the last PROGRAMMATIC value is held here, clamped exactly as UISlider
/// clamps, and reported verbatim.
///
/// **A real drag is the platform's own** ([beginTracking] clears the shim, and
/// a set that lands mid-tracking refuses to record): during tracking `value`
/// must report the live thumb, or the dispatch-site dedup would compare every
/// sample against a frozen number and the drag would go silent after its
/// first step. The wire is unaffected either way — the payload is quantized
/// at the dispatch site from whatever the widget reports.
final class BnSliderView: UISlider {
    /// The last programmatic value, exact — nil while the user is dragging
    /// (the platform's live value is the honest answer then).
    private var bnExactValue: Float?

    override var value: Float {
        get { bnExactValue ?? super.value }
        set { setValue(newValue, animated: false) } // one recording site below
    }

    override func setValue(_ value: Float, animated: Bool) {
        super.setValue(value, animated: animated)
        bnRecordExact(value)
    }

    override func beginTracking(_ touch: UITouch, with event: UIEvent?) -> Bool {
        bnExactValue = nil // the drag's live values are the platform's
        return super.beginTracking(touch, with: event)
    }

    private func bnRecordExact(_ requested: Float) {
        guard !isTracking else { bnExactValue = nil; return }
        // The same clamp UISlider applies internally, done in exact arithmetic.
        bnExactValue = Swift.min(Swift.max(requested, minimumValue), maximumValue)
    }
}

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

/// **AN `image` MEASURES ITS BYTES, NOT ITS WIDGET** (Phase 6.3 — the parity
/// contract's UNIT row). The twin of Kotlin's dedicated `image` measure function, and
/// it exists for the same reason on both platforms: **`image` is the one measured
/// nodeType whose native widget answers in the WRONG UNIT.**
///
///  - Android — an `ImageView`'s intrinsic size is its drawable's size in **pixels**,
///    which 6.1's generic measure path divides by density (61dp for a 160px file on
///    the Pixel 6 AVD's 2.625).
///  - iOS — a `UIImageView`'s `sizeThatFits` answers with `image.size`, which is
///    `pixels / image.scale`. Correct today (`UIImage(data:)` has `scale == 1`) and
///    one `.scaleFactor` option away from silently reporting a THIRD of the number
///    Android does, with no test in either suite able to see it.
///
/// So both shells read the DECODED PIXEL COUNT and report it directly:
/// [BnImageView.bnNaturalSize], recorded by [BnWidgetMapper.onImageLoaded].
///
/// **The measure MODES are deliberately ignored, exactly as Android's twin ignores
/// them** — Yoga itself applies them (`YGNodeWithMeasureFuncSetMeasuredDimensions`
/// substitutes the imposed size for an `EXACTLY` axis and bounds an `AT_MOST` one by
/// the node's own min/max styles), so a measure func that second-guessed it would be
/// two clamping rules where the contract has one. No bytes ⇒ **0 × 0**, which is the
/// pre-load state, the failure state and the cleared state — one answer, three rows of
/// the contract.
private let bnYogaImageMeasureTrampoline: bn_yoga_measure_fn = { context, _, _, _, _ in
    guard let context = context else { return bn_yoga_size(width: 0, height: 0) }
    let view = Unmanaged<UIView>.fromOpaque(context).takeUnretainedValue()
    guard let image = view as? BnImageView, let size = image.bnNaturalSize else {
        return bn_yoga_size(width: 0, height: 0)
    }
    return bn_yoga_size(width: Float(size.width), height: Float(size.height))
}

final class BnWidgetMapper {

    /// UI-event seam (the Kotlin mapper's `onUiEvent` constructor arg): wired by
    /// BnRuntime to the dispatch lane. Default no-op keeps the render-only path
    /// (5.2 tests) working. Called on the main thread from control targets.
    var onUiEvent: (_ handlerId: Int32, _ eventName: String, _ payload: String?) -> Void = { _, _, _ in }

    /// Phase 7.2 — the scroll wire's dispatch, WITH a completion signal: the
    /// conflation ([BnScrollWire]) may not submit the next scroll dispatch until
    /// the previous one has LEFT the lane, and fire-and-forget [onUiEvent] cannot
    /// say when that is. Production wires it to `BnRuntime.dispatchEvent(handlerId:
    /// eventName: "scroll", payload:onComplete:)` (the Swift twin of Kotlin's
    /// overload — no ABI change); `onComplete` may arrive on ANY thread (the lane's)
    /// — the mapper marshals to the main queue itself ([maybeDispatchScroll]).
    ///
    /// `nil` (the default) routes through [onUiEvent] and completes synchronously
    /// ([submitScrollDispatch]) — which keeps every event-agnostic test compiling
    /// unchanged and is still a correct conflation, just one whose lane is never
    /// busy longer than a runloop turn. The twin of Kotlin's constructor default.
    var onScrollEvent: ((_ handlerId: Int32, _ offsetPayload: String, _ onComplete: @escaping () -> Void) -> Void)?

    /// NODETYPES whose size is the native widget's business (DoD #3) — and the ONLY
    /// nodes that get a measure function. NOT "the nodes with no children": a
    /// childless `view` is a container (non-negotiable #6).
    ///
    /// Phase 7.3: the four form controls join. Their intrinsic sizes are the
    /// PLATFORM's own (a fresh `UISwitch` answers `sizeThatFits` with 51×31pt —
    /// nothing like Android's Material numbers) — frame parity applies to LAYOUT
    /// (declared sizes and placement), never to intrinsic control sizes, which
    /// are asserted per-platform by ORACLE (the 6.3 method).
    ///
    /// Phase 7.4: `activityindicator` joins — the measured leaf (design decision
    /// 5), same oracle discipline. `modal` is deliberately NOT here: its anchor
    /// and overlay are CONTAINERS with shell-fixed styles, and a measure func on
    /// a container is the 6.1 law's named violation.
    private static let measuredNodeTypes: Set<String> =
        ["text", "button", "input", "image", "checkbox", "switch", "slider", "picker",
         "activityindicator"]

    /// The one nodeType that is a VIEWPORT and owns a synthetic content node — and it
    /// is deliberately NOT in [measuredNodeTypes]: the measure func attaches BY
    /// NODETYPE, and a `scroll` node is a container Yoga sizes itself (a measure func on
    /// it would let `UIScrollView`'s intrinsic size speak over the author's `Height`).
    /// The same constant the `.mm` keys the content node's styles on — one contract, one
    /// spelling.
    private static let scroll = "scroll"

    /// Phase 6.3 — the one measured nodeType that measures its **BYTES** rather than its
    /// widget, and therefore the one that gets [bnYogaImageMeasureTrampoline] instead of
    /// the generic `sizeThatFits` one. Keyed on the NODETYPE (the contract) and not on
    /// `view is UIImageView` (a row in a table), for the reason [Self.scroll] is.
    private static let image = "image"

    /// Phase 7.3 — the state-owner nodeType: a `picker` owns [BnPickerState]
    /// (its items, its selection AND its dataSource/delegate). Keyed on the
    /// NODETYPE for the reason [Self.scroll] is. The other stateful control:
    private static let picker = "picker"

    /// …and `slider`, which owns [BnSliderState] (the step contract's home).
    private static let slider = "slider"

    /// Phase 7.4 — the one nodeType that is an ANCHOR + OVERLAY pair (design
    /// decision 1: the 6.2 synthetic-node machinery, pointed at the root).
    /// Keyed on the NODETYPE for the reason [Self.scroll] is — one contract,
    /// one spelling, the same constant Kotlin's `WidgetMapper.MODAL` twins.
    private static let modal = "modal"

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

    /// Phase 7.4 — **the modal OVERLAYS, view half**: a `modal` node's id → its
    /// [BnModalOverlayView], attached LAST at [root]. The modal's [views] entry is
    /// the ANCHOR; this map is what redirects its wire children ([containerFor]),
    /// what the `scrimColor` prop paints, what the dismissal-request `click`
    /// attaches to, and what makes a node "a modal node" to [handleSetStyle]'s
    /// style-ignore rule (the [contentNodes] membership discipline).
    ///
    /// **The overlay is NOT a view descendant of the anchor** — `RemoveNode` on the
    /// modal (or any ancestor of its anchor) must purge BOTH subtrees, and this
    /// map's entry is what names the second one ([handleRemove]'s FIXPOINT). Entry
    /// and overlay evicted in the same breath as the anchor (ids are reused — the
    /// [scrollContents] discipline). Both halves of the pair are made in the same
    /// breath ([handleCreate]) and purged in the same breath, or the two counts
    /// below disagree and the lifecycle pin reds.
    private var modalOverlays: [Int32: BnModalOverlayView] = [:]

    /// …and the Yoga half: a `modal` node's id → its overlay NODE, a child of
    /// [hostRoot] attached last. NOT in [yogaNodes] (that map is the WIRE nodes —
    /// the [contentNodes] reasoning), so [calculateAndApply] applies overlay
    /// frames from HERE. The overlay node is NOT a Yoga descendant of the anchor
    /// node, so — unlike the scroll content node — no subtree free ever reaches it
    /// for free: [handleRemove] frees it EXPLICITLY, exactly once (overlays all
    /// hang directly off [hostRoot], never inside another overlay's Yoga subtree
    /// even for a NESTED modal, whose ANCHOR is in the outer overlay's subtree
    /// but whose overlay is not) — **a stale entry here is a dangling `YGNodeRef`,
    /// and on iOS that is a crash, not a leak.**
    private var overlayNodes: [Int32: UnsafeMutableRawPointer] = [:]

    /// Phase 7.4 — the live `click` tap recognizers, keyed by nodeId (one click
    /// wire per node; the [eventTargets] shape for the non-control arm). The
    /// recognizer's attached view is `recognizer.view` — for a modal node that is
    /// the OVERLAY, never the anchor. Last-wins re-attach; detached by
    /// DetachEvent, the subtree purge (by view identity — the recognizer may sit
    /// on a view whose nodeId is not the one removed) and [destroy].
    private var clickRecognizers: [Int32: BnClickTapRecognizer] = [:]

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

    /// Phase 7.2 — **THE CONFLATION SLOT** (the wire contract, design §"The wire
    /// contract" — NORMATIVE; the twin of Kotlin's `WidgetMapper.ScrollWire`): one
    /// per scroll node with the `scroll` event attached, keyed by nodeId like
    /// [eventTargets].
    ///
    ///  - [pendingOffsetPt] is the ONE pending offset. A new native sample
    ///    **REPLACES** it — never a queue: scroll position is idempotent STATE,
    ///    not an event log, so only the freshest value is worth a dispatch and a
    ///    slow consumer sees FEWER, FRESHER events, never a backlog.
    ///  - [inFlight] is true from the moment a dispatch is SUBMITTED to the lane
    ///    until its completion signal comes back ([maybeDispatchScroll]). While it
    ///    is true — or while [applyingBatch] is — new samples conflate into the
    ///    slot; the freshest value goes out when the lane frees.
    ///  - [handlerId] is MUTABLE for the 4.2 stale-watcher reason: a last-wins
    ///    re-attach (same node, new handlerId, no preceding detach) swaps the
    ///    handler on the LIVE wire instead of stacking a second one — and
    ///    deliberately KEEPS the pending offset and the in-flight flag, because
    ///    they describe the NODE's scroll state, which a handler swap does not
    ///    reset.
    ///  - [proxy] is retained HERE because `UIScrollView.delegate` is weak — the
    ///    wire owns its sample source the way [eventTargets] owns its
    ///    `BnControlTarget`s.
    ///
    /// Detach/purge is the 6.3 stale-callback discipline: DetachEvent and
    /// [handleRemove] delete the wire, and the pending offset **dies with it,
    /// never dispatched**. A completion that lands afterwards resets a flag on an
    /// unreachable object and re-consults the LIVE map ([maybeDispatchScroll]
    /// starts with a map lookup), so it is a no-op by construction.
    private final class BnScrollWire {
        var handlerId: Int32
        var pendingOffsetPt: Float?
        var inFlight = false
        let proxy: BnScrollDelegateProxy
        init(handlerId: Int32, proxy: BnScrollDelegateProxy) {
            self.handlerId = handlerId
            self.proxy = proxy
        }
    }
    private var scrollWires: [Int32: BnScrollWire] = [:]

    /// Phase 7.3 — a `slider` node's WIRE state, and **THE STEP CONTRACT'S iOS
    /// HALF**. `UISlider` is float-native (min/max/value are Floats), so none of
    /// Android's int-progress geometry ([SliderState]'s float↔int precision
    /// contract) exists here — what survives the port is the OBSERVABLE: **the
    /// wire payload must be an exact step multiple `min + n×step`** (the demo
    /// slider 0/100/5 puts only multiples of 5 on the wire). Android gets that
    /// for free (one progress unit IS one step, so the int widget can only land
    /// on multiples); iOS must QUANTIZE the user's drag itself ([quantized]) —
    /// one multiply and one add per payload, no accumulated error, exactly
    /// Android's `min + progress×step` arithmetic.
    ///
    /// The state holds the RAW wire floats and the widget is RE-DERIVED from
    /// the whole state on every prop write ([applySlider]) — so patch order
    /// inside a batch cannot matter (a `value` landing before its `max` clamps
    /// nothing; the last recompute wins). Defaults 0/100 mirror Kotlin's — they
    /// only ever serve a hand-rolled wire: `BnSlider` ALWAYS declares min/max.
    ///
    /// Continuous (`step` absent): the payload is the raw float — the widget's
    /// native unit, undivided. (Android quantizes continuous into 1000 progress
    /// units because its widget is an int; that mechanic is Android's own and
    /// is deliberately NOT copied — the design's DO-NOT-COPY list names it.)
    private final class BnSliderState {
        var value: Float = 0
        var min: Float = 0
        var max: Float = 100
        var step: Float?

        /// The STEP contract: [raw] snapped onto `min + n×step`, n clamped into
        /// the declared range. Identity for a continuous slider (UISlider has
        /// already clamped raw into [min, max]).
        func quantized(_ raw: Float) -> Float {
            guard let step = step, step > 0 else { return raw }
            let range = max - min
            guard range > 0 else { return min }
            let n = Swift.min(Swift.max(0, ((raw - min) / step).rounded()),
                              (range / step).rounded())
            return min + n * step
        }
    }

    /// nodeId → its slider wire state. Created with the node ([handleCreate],
    /// keyed on the NODETYPE — the 6.2 lesson), purged with it ([handleRemove]).
    private var sliderStates: [Int32: BnSliderState] = [:]

    /// Phase 7.3 — a `picker` node's state (the state-owner precedent: the
    /// native widget owns its items AND its selection UI) — **and its
    /// dataSource/delegate**: `UIPickerView` has no adapter object, so the
    /// state IS the design's "the component as its own dataSource/delegate".
    /// Both `UIPickerView` slots are weak; this map is what retains the state
    /// (the [eventTargets]/[BnScrollWire] ownership shape).
    ///
    ///  - [appliedSelection] — what the shell last applied to (or last heard
    ///    from) the picker. −1 = nothing selected (empty items — the clamp
    ///    rule's only −1). `selectRow` applies immediately and never calls the
    ///    delegate (VERIFIED by the RAW-selectRow test, which calls the view
    ///    directly — the Gate 3 review's S1-1: both apply sites record THIS
    ///    field BEFORE calling selectRow, and record-before-apply is,
    ///    structurally, the expected-selection guard Android's Spinner
    ///    carries, so a fire through the apply path would be swallowed by the
    ///    same-row compare and could never redden a test). Its everyday guard
    ///    duty is that SAME-ROW compare in [handlePickerUserSelect]: a wheel
    ///    spun away and back to the current row is not a change (Android's
    ///    AdapterView drops those the same way).
    ///  - [requestedIndex] — the wire's last `selectedIndex`, kept RAW so an
    ///    `items` write can honor it regardless of patch order (a selection
    ///    that arrived before its items would otherwise be clamped against an
    ///    empty list and lost). The normative items-before-selectedIndex +
    ///    re-clamp-on-items order (design decision 3) both route through it.
    ///  - [handlerId] — the live `change` wire, last-wins (the 4.2 watcher
    ///    discipline): a re-attach swaps the handler, keeping the node's state.
    ///    Nil when unattached — the clamp's notify-on-move then has no wire.
    ///  - [onUserSelect] — the delegate's ONLY dispatch path, routed back into
    ///    the mapper's LIVE map lookup ([handlePickerUserSelect]) so a purged
    ///    node's late delegate call no-ops (the 6.3 stale-callback discipline).
    private final class BnPickerState: NSObject, UIPickerViewDataSource, UIPickerViewDelegate {
        var items: [String] = []
        var handlerId: Int32?
        var requestedIndex: Int?
        var appliedSelection = -1
        var onUserSelect: ((Int) -> Void)?

        func numberOfComponents(in pickerView: UIPickerView) -> Int { 1 }

        func pickerView(_ pickerView: UIPickerView, numberOfRowsInComponent component: Int) -> Int {
            items.count
        }

        func pickerView(_ pickerView: UIPickerView, titleForRow row: Int,
                        forComponent component: Int) -> String? {
            row >= 0 && row < items.count ? items[row] : nil
        }

        /// A USER pick — `UIPickerView` calls this for wheel gestures only,
        /// never for `selectRow` (verified by the RAW-selectRow test; through
        /// the apply path the record-before-apply ordering would swallow a
        /// fire, so only the raw call is discriminating).
        func pickerView(_ pickerView: UIPickerView, didSelectRow row: Int, inComponent component: Int) {
            onUserSelect?(row)
        }
    }

    /// nodeId → its picker state. Same lifecycle as [sliderStates].
    private var pickerStates: [Int32: BnPickerState] = [:]

    /// Test-only (Phase 7.3): `change` dispatches actually sent, ALL controls
    /// (UITextField/checkbox/switch/slider/picker — every dispatch site routes
    /// through [dispatchChange]). The disabled-controls device assertion reads
    /// it: "disabled controls dispatch NOTHING" is only assertable as a
    /// counter, because the demo's disabled handlers are deliberately unbound
    /// .NET-side — a dispatch that DID leak would move no echo and no frame.
    private(set) var changeDispatchesSent: Int = 0

    /// Every `change` dispatch goes through here — the counter above is the
    /// device tests' only honest observation point.
    private func dispatchChange(_ handlerId: Int32, _ payload: String) {
        changeDispatchesSent += 1
        onUiEvent(handlerId, "change", payload)
    }

    /// Test-only (Phase 7.4): `click` dispatches actually sent — ALL clicks
    /// (buttons, the scrim, the content-box swallow) route through
    /// [dispatchClick]. The device counter assertions read it, because two of
    /// the modal's dispatches are invisible in every frame: the content-box
    /// SWALLOW is a real dispatch that .NET deliberately moves nothing for, and
    /// a dismissal REQUEST a page chose to ignore would likewise move nothing —
    /// "the shell never closes anything itself" is only assertable as a counted
    /// wire dispatch (the [changeDispatchesSent] precedent; the twin of
    /// Kotlin's `clickDispatchesSent`).
    private(set) var clickDispatchesSent: Int = 0

    /// Every `click` dispatch goes through here — see [clickDispatchesSent].
    private func dispatchClick(_ handlerId: Int32) {
        clickDispatchesSent += 1
        onUiEvent(handlerId, "click", nil)
    }

    /// Test-only (Phase 7.4): live modal overlays — the view-tree half of the
    /// overlay-count lifecycle pin ([yogaOverlayNodeCount] is the Yoga half).
    /// Must return to 0 after mount → remove, or the two-subtree purge missed
    /// the subtree no walk from the anchor can reach — which HERE is a dangling
    /// `YGNodeRef` waiting in [overlayNodes], not Android's mere leak.
    var modalOverlayCount: Int { modalOverlays.count }

    /// Test-only (Phase 7.4): the Yoga tree's live overlay nodes — must track
    /// [modalOverlayCount] (both trees or neither, the 6.2 law). The twin of
    /// Kotlin's `yogaOverlayNodeCount`.
    var yogaOverlayNodeCount: Int { overlayNodes.count }

    /// Set by [destroy]. The tree is gone; a late batch (one already hopped to main
    /// when the host tore down) must not resurrect it into freed native memory.
    private var destroyed = false

    // ── Phase 6.3: the image request bookkeeping ─────────────────────────────

    /// **ONE IN-FLIGHT REQUEST PER `image` NODE**, and everything a terminal callback
    /// needs to decide whether it still owns it.
    ///
    /// The entry is disposed on a `Src` change ([handleSrc]), on node removal
    /// ([handleRemove] — as part of the SUBTREE purge, which is what navigation actually
    /// emits) and on teardown ([destroy]). **That is memory safety, not hygiene** (6.3
    /// non-negotiable #4): a completion firing into a purged node marks a **freed
    /// `YGNodeRef`** dirty — `bn_yoga_node_free_subtree` has already reclaimed it.
    ///
    /// It carries the `view` as well as the `generation` because **[clearIfMine] must ask
    /// BOTH**. See `bnIsLiveImageRequest`.
    private struct InFlight {
        let generation: Int
        let view: BnImageView
        let task: BnImageTask
    }
    private var imageRequests: [Int32: InFlight] = [:]

    /// The node's CURRENT generation — bumped by every `src` write, and **deliberately
    /// not folded into [imageRequests]**, because the two answer different questions and
    /// one of them has an answer when the other has none:
    ///
    ///  - [imageRequests] answers *"which request does this node have in flight, and
    ///    whose is it?"* — and there may be **none** even while a request is completing:
    ///    a Kingfisher memory-cache hit runs its completion **inside** `retrieveImage`,
    ///    before [handleSrc] has had a chance to record anything.
    ///  - THIS answers *"has this node's `src` been written since that request was
    ///    issued?"* — the question `bnIsLiveImageRequest` must be able to ask about a node
    ///    with no live entry at all, and about one that has been purged (absent ⇒ `nil` ⇒
    ///    never live).
    private var imageGenerations: [Int32: Int] = [:]

    /// Phase 7.5 — an `image` node's 7.5 wire state, and **THE PLACEHOLDER STATE TABLE'S
    /// BOOKKEEPING** (design decision 1 — the table is NORMATIVE and loader-free; this is
    /// iOS's mechanism for it, verified per shell, never assumed — the 7.3 guard lesson).
    /// The twin of Kotlin's `WidgetMapper.ImageState`, field for field.
    ///
    /// The placeholder is **paint inside whatever box Yoga already gave the node**
    /// ([BnImageView.bnShowPlaceholder] — a bounds-tracking color subview; see that
    /// property's header for why it is neither `backgroundColor` nor a color `UIImage`):
    /// never a Yoga write, never a `markDirty`, never a natural size, so it CANNOT
    /// measure **by construction**. An intrinsic node's placeholder is a 0 × 0 paint —
    /// invisible, correct, not diagnosed.
    ///
    ///  - [placeholderColor] — the parsed `placeholderColor` prop (nil = none/cleared).
    ///    Held as STATE because the props arrive in seq order: `src` (24) lands BEFORE
    ///    `placeholderColor` (25) in the same mount batch, so [handleSrc] must be able to
    ///    paint a color that arrives one patch later — the prop arm repaints iff the node
    ///    is still waiting for bytes ([awaitingBytes]).
    ///  - [srcPresent] / [bytesPainted] — which row of the state table the node is on.
    ///    `src` set + not painted = IN FLIGHT or ERROR (both paint the placeholder — the
    ///    ERROR row keeps it, deliberately: it is the error state's visual, and the
    ///    declared box it fills is held because it was DECLARED, not because it failed).
    ///    SUCCESS sets [bytesPainted], the bytes become the image and the placeholder is
    ///    cleared (letterbox bars then show `BackgroundColor` — the view's background —
    ///    never the placeholder). `src` → null clears both, and the paint with them.
    ///  - [errorHandlerId] — the live `error` wire (decision 2), last-wins on re-attach
    ///    (the 4.2 watcher discipline), nil when unattached. The dispatch decision itself
    ///    is `bnImageErrorDispatchAction` — a pure function in its own file, composing
    ///    `bnIsLiveImageRequest` by name (one guard, two consumers).
    ///
    /// Created with the node, keyed on the NODETYPE ([handleCreate] — the 6.2 lesson),
    /// purged with it ([handleRemove], [destroy]) for the reason every map here purges:
    /// ids restart.
    private final class BnImageWireState {
        var placeholderColor: UIColor?
        var srcPresent = false
        var bytesPainted = false
        var errorHandlerId: Int32?

        /// The rows of the state table on which the placeholder paints: a source is
        /// named and the real bytes are not on screen — IN FLIGHT, or terminal ERROR.
        var awaitingBytes: Bool { srcPresent && !bytesPainted }
    }

    /// nodeId → its image wire state. Same lifecycle as [sliderStates].
    private var imageStates: [Int32: BnImageWireState] = [:]

    /// Test-only (Phase 7.5): `error` dispatches actually sent — every dispatch routes
    /// through [dispatchError] (the [changeDispatchesSent] precedent; the twin of
    /// Kotlin's `errorDispatchesSent`). The device assertion "dispatched EXACTLY ONCE,
    /// and only for the BOUND failure" is only honest as a counted wire dispatch:
    /// /imagepolish has TWO failing images and one attach, and the unbound failure's
    /// non-dispatch moves no frame and no echo.
    private(set) var errorDispatchesSent: Int = 0

    /// Every `error` dispatch goes through here — see [errorDispatchesSent]. The payload
    /// is the WIRE's `src`, verbatim (decision 2: the URL is the only fact two loaders
    /// share about the same failure, so it is the only payload two shells can dispatch
    /// identically).
    private func dispatchError(_ handlerId: Int32, _ src: String) {
        errorDispatchesSent += 1
        onUiEvent(handlerId, "error", src)
    }

    /// Test-only: every image request that has TERMINATED, in order — Kingfisher's own
    /// per-node completion verdict, which is **the synchronization gate** the tests await
    /// (6.3 non-negotiable #6).
    ///
    /// It exists because THE WIRE CARRIES NO COMPLETION SIGNAL, by design (no `OnLoad`, no
    /// `OnError` — each changes measurement), and two of `/image`'s three cases assert that
    /// **nothing moved**: a suite that reads the AFTER table before the bytes land passes
    /// both of them having proven nothing.
    ///
    /// **A BOUNDED RING** (last [maxImageResults]), because it is appended by every terminal
    /// callback and it lives in PRODUCTION code: unbounded it grows one entry per image, per
    /// navigation, for as long as the app runs. It is a diagnostic, not a ledger.
    private static let maxImageResults = 64
    private var imageResultLog: [BnImageResult] = []

    /// Phase 6.3 — **THE BATCH GUARD**, and it exists for exactly ONE caller.
    ///
    /// A Kingfisher memory-cache hit completes SYNCHRONOUSLY, on the main thread, inside the
    /// `retrieveImage` that [handleSrc] issues from within [applyBatch] — so [resolveLayout]
    /// has a RE-ENTRANT caller. Inside a batch it must be a **no-op**: the batch's own
    /// `CommitFrame` re-solves the whole tree at the end, and a re-solve from inside it would
    /// run Yoga against a HALF-APPLIED tree and then again at commit — **two reflows, where
    /// the contract says ONE.**
    ///
    /// (Android's mapper has this flag already, for a second reason iOS does not share: its
    /// programmatic `setText` fires a change event, so patch application is guarded against
    /// re-entrant dispatch — and an inner re-solve's `finally` would clear that guard for the
    /// REST of the batch. UIKit does not fire `.editingChanged` on a programmatic `.text` set,
    /// so on iOS the flag carries only the reflow rule. One flag, one of its two reasons.)
    private var applyingBatch = false

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
    /// **AND THE PRECONDITION IS THE PIN, NOT THE PARAGRAPH ABOVE IT.** [applyBatch] and
    /// [calculateAndApply] both assert `.onQueue(.main)`; this method mutates the SAME
    /// main-thread-only state ([imageRequests], [imageGenerations], and — through
    /// `bn_yoga_node_free_subtree` — the `.mm`'s unsynchronised measure registry), and Phase
    /// 6.3 GREW that surface: `task.cancel()` now runs in here too, on a Kingfisher handle
    /// whose completion queue is the main one. A comment is not the pin the rest of this class
    /// uses.
    ///
    /// Idempotent: `HostViewController.deinit` calls it, and so does [deinit] as the
    /// backstop for a mapper nobody owned.
    func destroy() {
        dispatchPrecondition(condition: .onQueue(.main)) // see [applyBatch]
        guard !destroyed else { return }
        destroyed = true

        // Phase 6.3 — THE IN-FLIGHT IMAGE REQUESTS GO FIRST, and the order is the same one
        // [handleRemove] keeps: a request still in flight when the Yoga tree below is freed
        // would complete into a node whose `YGNodeRef` no longer exists. The generation map
        // is emptied with it, so a completion that was ALREADY in its main-thread
        // continuation when `cancel()` ran finds `nil` and drops itself (`bnIsLiveImageRequest`
        // — the guard behind the cancel, for the completion the cancel could not prevent).
        for (_, request) in imageRequests { request.task.cancel() }
        imageRequests.removeAll()
        imageGenerations.removeAll()
        imageResultLog.removeAll()

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
        // Phase 7.4: the overlay nodes were Yoga CHILDREN of the host root, so the
        // free above reclaimed them too — same dangling-handle rule; and the view
        // half and the recognizers die with the host (a click after teardown would
        // dispatch into a retired lane).
        overlayNodes.removeAll()
        modalOverlays.removeAll()
        for (_, recognizer) in clickRecognizers { recognizer.bnDetach() }
        clickRecognizers.removeAll()
        // Phase 7.2: pending scroll offsets die with the host — a dispatch after
        // teardown would enter a retired lane for a dead view hierarchy.
        scrollWires.removeAll()
        // Phase 7.3: the form controls' wire state too, same reason.
        sliderStates.removeAll()
        pickerStates.removeAll()
        // Phase 7.5: …and the image wire state — a deferred error dispatch that
        // lands after teardown re-asks the decision, finds no generation and no
        // handler, and DROPs (the [recordImageResult] destroyed-guard's shape).
        imageStates.removeAll()
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

    /// **THE BACKSTOP, AND IT HAS TO GET ITSELF ONTO THE MAIN THREAD.**
    ///
    /// `deinit` runs on whatever thread drops the last reference — which is precisely the
    /// thread [destroy] must not run on, and [destroy] now TRAPS on it (`dispatchPrecondition`).
    /// The owner (`HostViewController.deinit`, main-thread by UIKit's contract) calls [destroy]
    /// deterministically and this is a no-op; a mapper nobody owned — a test's, a future
    /// second boot's — still has to free a raw Yoga tree and cancel Kingfisher tasks, and it
    /// may only do that from main.
    ///
    /// So: `assertionFailure` names the ownership bug (loud in every XCTest run, a no-op in
    /// release), and then the work HOPS — synchronously, because after this returns the object
    /// is gone and an async block would capture a `self` that no longer exists.
    deinit {
        if Thread.isMainThread {
            destroy()
        } else {
            assertionFailure(
                "BnWidgetMapper was deallocated OFF THE MAIN THREAD — its owner should have "
                + "called destroy() deterministically (HostViewController.deinit does). Freeing "
                + "the Yoga tree here would mutate the .mm's unsynchronised measure registry from "
                + "this thread, concurrently with any main-thread applyBatch.")
            DispatchQueue.main.sync { self.destroy() }
        }
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

    /// The mapper's runtime diagnostics, in emission order: the scroll pair (6.2's
    /// container-style ignore-and-log rule and the definite-height warning), the
    /// modal-style drops (7.4), and the image contentMode rejections (7.5). Exposed
    /// because `NSLog` is not an assertion surface and every one of these failures is
    /// SILENT on the device. `BnScrollTests` / `BnModalMapperTests` /
    /// `BnImagePolishMapperTests` assert them. The twin of Kotlin's
    /// `WidgetMapper.diagnostics`. (Renamed from `scrollDiagnostics` in 7.6 — the
    /// name predated the 7.4/7.5 additions and lied about the scope.)
    var diagnostics: [String] { diagnosed.map { $0.message } }

    // ── Phase 6.3 test-only bookkeeping (the twins of Kotlin's) ──────────────

    /// The last [maxImageResults] image requests that TERMINATED, with Kingfisher's own
    /// verdict. **The AFTER frames of `/image` may only be asserted once this holds all
    /// three** (6.3 non-negotiable #6 — the synchronization gate).
    var imageResults: [BnImageResult] { imageResultLog }

    /// How many have terminated IN TOTAL — which [imageResults] deliberately cannot say,
    /// because it is a bounded ring. A test that waited for "more than the cap" on the ring
    /// would wait forever.
    var imageTerminalCount: Int = 0

    /// The ring's cap, so the bound is asserted against the shell's own number rather than
    /// one a test invented.
    var imageResultCap: Int { Self.maxImageResults }

    /// Requests currently in flight. **Its return to 0 is an invariant several tests
    /// assert** — a synchronously-completed request that was recorded anyway sits here
    /// forever (its completion's bookkeeping ran before the entry existed), and a later
    /// removal would "cancel" a request that finished long ago.
    var inFlightImageCount: Int { imageRequests.count }

    /// Test-only: a node's CURRENT generation (`nil` = it has none — it was purged, or never
    /// carried a `src`). The twin of Kotlin's `WidgetMapper.imageGeneration`.
    ///
    /// It is exposed for ONE reason, and it is the reason [layoutPassCount] is: **the rule it
    /// pins is invisible in every frame.** *Every* `src` write bumps the generation, **including
    /// a CLEAR** — because a clear cancels, a cancel races its own completion, and the
    /// generation is the only thing that stops the loser painting ([handleSrc]). A shell that
    /// bumped only on a real URL is GREEN on every frame table in this repo: the cancel wins
    /// that race in every ordering a device test can produce (the main queue is FIFO, so a
    /// completion already enqueued runs BEFORE any batch a test enqueues after it — a device
    /// test can only ever stage the clear WINNING). The bump is therefore asserted as the
    /// number it is, and `BnImageGuardTests.testASupersededGenerationIsNotLive` is what that
    /// number then BUYS.
    func imageGeneration(of nodeId: Int32) -> Int? { imageGenerations[nodeId] }

    /// **HOW MANY LAYOUT PASSES HAVE RUN** — the only way to assert *"ONE reflow, never
    /// two"* as a fact rather than as prose.
    ///
    /// It is what pins [resolveLayout]'s batch guard: with a WARM cache both completions run
    /// synchronously INSIDE `applyBatch`, and a re-solve from in there would run Yoga against
    /// a half-applied tree and then AGAIN at `CommitFrame`. The final frames would be
    /// identical (the commit fixes them up), so **no frame assertion anywhere can see it** —
    /// only the pass count can.
    var layoutPassCount: Int = 0

    // ── Phase 7.2 test-only bookkeeping (the twins of Kotlin's) ──────────────

    /// Test-only: native scroll samples the delegate delivered to a live wire —
    /// the numerator of the throughput evidence (samples-seen vs
    /// events-dispatched, the contract's "Throughput evidence" row). Main-thread
    /// only — every `scrollViewDidScroll` is.
    private(set) var scrollSamplesSeen: Int = 0

    /// Test-only: scroll dispatches actually SUBMITTED to the lane — the
    /// denominator of the conflation ratio. By construction ≤ [scrollSamplesSeen]:
    /// at most one in flight per node, ever.
    private(set) var scrollDispatchesSent: Int = 0

    /// Test-only: the offset (pt) the LAST submitted dispatch carried — how a
    /// test asserts "the FINAL offset always arrives" without parsing a log.
    private(set) var lastScrollDispatchPt: Float?

    /// Test-only: live conflation slots — must return to 0 after detach/purge, or
    /// a detached node's pending offset is one runloop turn from being dispatched
    /// into a stale handler.
    var scrollWireCount: Int { scrollWires.count }

    /// Test-only: a node's pending (conflated, not yet dispatched) offset in pt —
    /// nil when the slot is empty or the node has no wire.
    func scrollPendingOffsetPt(of nodeId: Int32) -> Float? { scrollWires[nodeId]?.pendingOffsetPt }

    /// Test-only (Phase 7.5): whether a patch batch is being applied RIGHT NOW —
    /// read by the defer end-to-end test's event sink, because "the dispatch never
    /// runs inside a patch batch" is a statement about WHEN, and the main queue's
    /// drain can run a deferred block before the test regains control (so "no
    /// dispatch yet, then one" is not a reliably observable ordering; "the dispatch
    /// that arrived saw no open batch" is).
    var isApplyingBatch: Bool { applyingBatch }

    /// Test-only: wires with work outstanding — a dispatch in flight or a
    /// conflated offset waiting for the lane. 0 = the scroll wire is QUIESCENT
    /// (the freshest sample has been dispatched AND completed) — the device
    /// tests' settle gate, because "the FINAL offset always arrives" is only
    /// assertable about a wire that has finished arriving.
    var scrollBusyWireCount: Int {
        scrollWires.values.filter { $0.inFlight || $0.pendingOffsetPt != nil }.count
    }

    // ── Phase 7.3 test-only bookkeeping ──────────────────────────────────────

    /// Test-only: the Yoga-computed (parent-relative) frame of the node that
    /// places [view] — nil for a view the mapper does not place.
    ///
    /// It exists because **one widget cannot witness its own stretch**: UIKit's
    /// `UISwitch` enforces its own size on every frame write (documented
    /// behaviour — "the size components of the frame rectangle are ignored";
    /// on the iOS 26 simulator a 390-wide assignment reads back the intrinsic
    /// 63), so `alignItems: stretch` on the un-styled checkbox/switch quartet
    /// is only observable on the YOGA node. The layout law is asserted here;
    /// what the view then shows is the platform's own answer, asserted by
    /// ORACLE (the 6.3 method). Every other widget in this shell accepts the
    /// frame it is given, which is why no test needed this before 7.3.
    func bnYogaFrame(of view: UIView) -> CGRect? {
        guard let node = viewToNode[ObjectIdentifier(view)] else { return nil }
        let frame = bn_yoga_node_get_frame(node)
        return CGRect(x: CGFloat(frame.x), y: CGFloat(frame.y),
                      width: CGFloat(frame.width), height: CGFloat(frame.height))
    }

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
        // Phase 6.3 — see [applyingBatch]: an image completion that lands INSIDE this loop
        // (a memory-cache hit completes synchronously, from `UpdateProp("src")`) must not
        // re-solve. The batch's own CommitFrame does that, once.
        applyingBatch = true
        defer {
            applyingBatch = false
            // Phase 7.2: scroll samples that arrived DURING the batch (the 6.2
            // shrink clamp's contentOffset write inside calculateAndApply fires
            // the delegate SYNCHRONOUSLY — the iOS echo path) were CONFLATED into
            // their slots, per the wire contract's backpressure row. The batch
            // end is a lane-availability: flush the freshest values now, AFTER
            // the guard dropped — a dispatch from inside the guard would be
            // swallowed, and the clamped offset would never reach .NET.
            flushScrollWires()
        }
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
        layoutPassCount += 1 // Phase 6.3: "ONE reflow, never two" is a COUNT — see [layoutPassCount]
        bn_yoga_calculate(hostRoot, Float(bounds.width), Float(bounds.height))
        for (nodeId, node) in yogaNodes {
            guard let view = views[nodeId] else { continue }
            let frame = bn_yoga_node_get_frame(node)
            view.frame = CGRect(x: CGFloat(frame.x), y: CGFloat(frame.y),
                                width: CGFloat(frame.width), height: CGFloat(frame.height))
        }
        // Phase 7.4 — the modal OVERLAYS: not in [yogaNodes] (they are shell-side,
        // no patch ever names one), so their computed full-root frames are applied
        // from their own map — the [applyScrollFrames] shape. The guard is the same
        // memory-safety guard that pass carries: both halves of the pair are made
        // and purged in the same breath, so a node with no view means the purge
        // desynced and this handle is (or is about to be) a dangling YGNodeRef.
        for (modalId, node) in overlayNodes {
            guard let overlay = modalOverlays[modalId] else {
                assertionFailure(
                    "modal \(modalId) has an overlay node but no overlay view — the purge "
                    + "desynced, and this handle is a DANGLING YGNodeRef")
                continue
            }
            let frame = bn_yoga_node_get_frame(node)
            overlay.frame = CGRect(x: CGFloat(frame.x), y: CGFloat(frame.y),
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
        // Phase 7.2 — `scroll` first, BEFORE the UIControl guard below: a
        // UIScrollView is a UIView, not a UIControl, and its wire is the
        // conflation slot, not a control-event target.
        if eventName == "scroll" {
            handleAttachScroll(nodeId: nodeId, handlerId: handlerId)
            return
        }
        // Phase 7.4 — `click` grows past UIButton (design decision 4): the modal's
        // dismissal-request wire listens on the SCRIM (the overlay), and plain
        // views get the tap-recognizer arm with the touch-view filter. One entry
        // point, three arms — see [handleAttachClick].
        if eventName == "click" {
            handleAttachClick(nodeId: nodeId, handlerId: handlerId)
            return
        }
        // Phase 7.3 — the picker next, ALSO before the UIControl guard: a
        // UIPickerView is a UIView, not a UIControl, and its wire is the state's
        // handlerId (the delegate dispatches through [handlePickerUserSelect]),
        // not a control-event target. Last-wins re-attach, the 4.2 watcher
        // discipline: swap the handler on the LIVE state, keep the node's
        // selection.
        if eventName == "change", views[nodeId] is UIPickerView {
            guard let state = pickerStates[nodeId] else {
                NSLog("[BnWidgetMapper] AttachEvent 'change' for picker \(nodeId) has no state: ignored")
                return
            }
            state.handlerId = handlerId
            return
        }
        // Phase 7.5 (design decision 2) — the `error` wire: a new WORD on the
        // existing dispatch wire (the scroll precedent), attached .NET-side iff
        // OnError has a delegate. No native listener to install — the "listener"
        // is Kingfisher's own terminal callback ([onImageFailed]'s dispatch site,
        // and the nil-URL synchronous failure that reaches the same site); this
        // arm only records WHERE the failure flows. Last-wins re-attach, the 4.2
        // watcher discipline. Before the UIControl guard below: an image is not a
        // UIControl, and its wire is state, not a control-event target.
        if eventName == "error" {
            guard let state = imageStates[nodeId] else {
                NSLog("[BnWidgetMapper] AttachEvent 'error' ignored: node \(nodeId) is "
                      + "\(views[nodeId].map { String(describing: type(of: $0)) } ?? "unknown"), "
                      + "not an image node")
                return
            }
            state.errorHandlerId = handlerId
            return
        }
        guard let control = views[nodeId] as? UIControl else {
            NSLog("[BnWidgetMapper] AttachEvent '\(eventName)' for node \(nodeId): not a UIControl — ignored")
            return
        }
        let controlEvent: UIControl.Event
        let payload: () -> String?
        switch eventName {
        case "change":
            switch control {
            case let field as UITextField:
                controlEvent = .editingChanged
                payload = { [weak field] in field?.text ?? "" }
            // Phase 7.3 — checkbox AND switch: both are UISwitch (decision 2)
            // and share one wire grammar (payload exactly "true"/"false" — what
            // BnCheckbox/BnSwitch parse ordinally).
            //
            // ── THE PER-CONTROL LOOP-GUARD FINDING (verified, never assumed —
            // the design's own words), and it is the OPPOSITE of Android's:
            // `UISwitch.setOn` does NOT fire `.valueChanged` (the 5.3 finding,
            // re-verified per control by the fires-nothing tests), so the
            // patch-applied value echo CANNOT re-enter the change lane and
            // there is deliberately NO `applyingBatch` dispatch guard here —
            // adding one would swallow the very verification that pins the
            // platform behaviour (the test reddens if UIKit ever starts
            // firing). Android's CompoundButton fires SYNCHRONOUSLY and needs
            // the guard; same wire, different platform, different (absent)
            // mechanism.
            case let toggle as UISwitch:
                controlEvent = .valueChanged
                payload = { [weak toggle] in (toggle?.isOn ?? false) ? "true" : "false" }
            // Phase 7.3 — slider: the payload is the wire VALUE quantized onto
            // the step grid (an invariant float — Float.description never
            // localizes; exactly what BnSlider's strict parse expects). See
            // [BnSliderState]: iOS is float-native, so the STEP contract is
            // enforced HERE, at the dispatch site, where Android's int widget
            // enforces it structurally.
            //
            // `UISlider.setValue` fires nothing (verified — no applyingBatch
            // guard, same reasoning as the UISwitch arm above). The guard iOS
            // DOES need is the DEDUP: a float-native drag delivers a distinct
            // `.valueChanged` per sample, and with a step declared, runs of
            // samples quantize onto the SAME multiple — Android's int progress
            // dedups those structurally (onProgressChanged fires per progress
            // CHANGE); here `nil` (= "not a change") is what keeps one step
            // from dispatching twice.
            case let slider as UISlider:
                controlEvent = .valueChanged
                payload = { [weak self, weak slider] in
                    guard let self = self, let slider = slider,
                          let state = self.sliderStates[nodeId] else {
                        return nil // purged: stale sample, no-op (the 6.3 discipline)
                    }
                    let quantized = state.quantized(slider.value)
                    if quantized == state.value { return nil } // the dedup — see above
                    state.value = quantized
                    // Snap the thumb onto the multiple the wire carries (setValue
                    // fires nothing, so this cannot re-enter) — the native state
                    // and the payload agree, as Android's int progress does
                    // structurally.
                    slider.setValue(quantized, animated: false)
                    return quantized.description
                }
            default:
                NSLog("[BnWidgetMapper] AttachEvent 'change' ignored: node \(nodeId) is a "
                      + "\(type(of: control)) — not a change-bearing widget")
                return
            }
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
            guard let self = self else { return }
            if eventName == "change" {
                // Every change dispatch rides [dispatchChange] (the counter is
                // the disabled-controls assertion's only honest witness). A nil
                // payload here is the slider's dedup / a purged node's stale
                // sample — NOT a change, nothing to dispatch. (click/focus/blur
                // legitimately dispatch nil payloads; change never does — the
                // UITextField arm answers "" for an empty field.)
                guard let value = payload() else { return }
                self.dispatchChange(handlerId, value)
            } else {
                self.onUiEvent(handlerId, eventName, payload())
            }
        }
        control.addTarget(target, action: #selector(BnControlTarget.fire), for: controlEvent)
        eventTargets[key] = (control, target)
    }

    /// Phase 7.4 — **AttachEvent("click"), three arms** (design decision 4):
    ///
    ///  1. **A `modal` node** — the dismissal-request wire listens on the SCRIM
    ///     (the overlay), never the 0-sized anchor `views` names. The scrim-tap
    ///     rule (NORMATIVE): a tap dismiss-requests ONLY when the touch lands on
    ///     the scrim view itself — iOS enforces it with the recognizer's
    ///     touch-view filter ([BnClickTapRecognizer]), so a tap on the content
    ///     box (a descendant) never fires it. Android's mechanism is different
    ///     (the clickable content box consumes its own taps before they fall
    ///     through) — same observable wire behavior, verified per shell.
    ///  2. **A UIButton** — the 5.3 target-action arm, unchanged except that the
    ///     dispatch now rides [dispatchClick] (the counter).
    ///  3. **Any other non-control view** — the tap-recognizer arm: this is what
    ///     the modal's content box (a plain `view` carrying the no-op SWALLOW
    ///     click) attaches through, and it is the survey's down payment on
    ///     `Pressable`. A non-UIButton CONTROL keeps the 5.3 ignore: controls
    ///     own their touches, and a recognizer on one would race them.
    ///
    /// Last-wins across BOTH mechanisms: a re-attach replaces whichever of the
    /// two the node held (the 4.2 watcher discipline — no stacked dispatches).
    private func handleAttachClick(nodeId: Int32, handlerId: Int32) {
        let key = EventKey(nodeId: nodeId, event: "click")
        removeTarget(for: key) // last-wins, the target half
        clickRecognizers.removeValue(forKey: nodeId)?.bnDetach() // …and the recognizer half

        if let overlay = modalOverlays[nodeId] {
            clickRecognizers[nodeId] = BnClickTapRecognizer(on: overlay) { [weak self] in
                self?.dispatchClick(handlerId)
            }
            return
        }
        guard let view = views[nodeId] else {
            NSLog("[BnWidgetMapper] AttachEvent 'click' for unknown nodeId \(nodeId): ignored")
            return
        }
        if let button = view as? UIButton {
            let target = BnControlTarget { [weak self] in self?.dispatchClick(handlerId) }
            button.addTarget(target, action: #selector(BnControlTarget.fire), for: .touchUpInside)
            eventTargets[key] = (button, target)
            return
        }
        if view is UIControl {
            NSLog("[BnWidgetMapper] AttachEvent 'click' ignored: node \(nodeId) is a "
                  + "\(type(of: view)) — a control owns its touches; the tap-recognizer "
                  + "arm is for PLAIN views (design decision 4)")
            return
        }
        clickRecognizers[nodeId] = BnClickTapRecognizer(on: view) { [weak self] in
            self?.dispatchClick(handlerId)
        }
    }

    private func handleDetachEvent(nodeId: Int32, eventName: String) {
        // Phase 7.4 — the recognizer half of the click wire detaches first (the
        // 3.3 symmetric-arms rule: whichever mechanism the attach picked, the
        // detach finds). A UIButton's click falls through to the target path
        // below, exactly as before.
        if eventName == "click", let recognizer = clickRecognizers.removeValue(forKey: nodeId) {
            recognizer.bnDetach()
            return
        }
        // Phase 7.2 — the 6.3 stale-callback discipline, for scroll: the wire
        // dies HERE, and its pending offset dies WITH it, never dispatched (the
        // contract's detach row). An in-flight dispatch already on the lane is
        // beyond recall — its completion resets a flag on this now-unreachable
        // wire and finds no map entry to dispatch from; a stale handlerId is
        // absorbed downstream (the rc-0 at-most-once contract, same as click).
        if eventName == "scroll" {
            guard scrollWires.removeValue(forKey: nodeId) != nil else {
                NSLog("[BnWidgetMapper] DetachEvent 'scroll' for node \(nodeId) has no live wire: ignored")
                return
            }
            // The proxy died with the wire (the delegate slot is weak and zeroes),
            // but nil it out explicitly — a dangling-but-unfired delegate is a
            // thing a reader should never have to reason about.
            (views[nodeId] as? UIScrollView)?.delegate = nil
            return
        }
        // Phase 7.3 — the picker detaches by state, mirroring its attach arm (the
        // 3.3 symmetric-arms rule): the handlerId goes, and the clamp's
        // notify-on-move loses its wire with it — a detached picker clamps
        // silently, like every other dead wire (the 7.2 detach discipline). The
        // delegate stays: it resolves the LIVE state's handlerId at fire time,
        // and nil dispatches nothing.
        if eventName == "change", views[nodeId] is UIPickerView {
            guard let state = pickerStates[nodeId] else {
                NSLog("[BnWidgetMapper] DetachEvent 'change' for picker \(nodeId) has no state: ignored")
                return
            }
            state.handlerId = nil
            return
        }
        // Phase 7.5 — the attach arm's mirror (the 3.3 rule: a new event type
        // extends both arms symmetrically). The wire dies here; a failure that
        // terminates later finds no handler and DROPs at the decision
        // (`bnImageErrorDispatchAction`).
        if eventName == "error" {
            guard let state = imageStates[nodeId] else {
                NSLog("[BnWidgetMapper] DetachEvent 'error' for node \(nodeId) has no image state: ignored")
                return
            }
            state.errorHandlerId = nil
            return
        }
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

    // ── Phase 7.2: THE onScroll WIRE (the conflation — NORMATIVE, mirrored from
    //    Android's WidgetMapper) ─────────────────────────────────────────────────
    //
    // The contract (docs/plans/2026-07-15-phase-7.2-design.md §"The wire contract"):
    //
    //   sample (pt, main thread)                 ← UIScrollView delegate
    //                                              (scrollViewDidScroll)
    //     → REPLACES the node's pending offset   ← never queue: scroll position is
    //                                              idempotent STATE, not an event log
    //     → dispatch IF the lane is free         ← at most ONE in flight per node;
    //       (not in flight, not mid-batch)         payload = the offset as an
    //                                              invariant float string, exactly
    //                                              what NativeRenderer.
    //                                              ParseScrollOffset parses
    //     → completion → flush the freshest      ← a slow consumer sees FEWER,
    //                                              FRESHER events — the backlog is
    //                                              impossible by construction
    //
    // NO UNIT CONVERSION, and that is iOS's half of the 6.1 one-conversion-site
    // rule: points ARE the density-independent unit Yoga computes in, so the number
    // read off `contentOffset.y` is already the number Android sends as dp.
    //
    // ORDERING IS PRESERVED, NOT ASSUMED: the dispatch rides BnRuntime's single
    // SERIAL dispatchLane — FIFO by DispatchQueue's own contract — and enters the
    // same queue tail as every tap and change event, so a conflated scroll dispatch
    // can never overtake a user-input event queued before it. BnScrollWireTests
    // pins the FIFO on the lane itself.
    //
    // iOS-SPECIFIC, ANDROID DOES NOT COPY: the delegate proxy (Android has
    // setOnScrollChangeListener), the ABSENT px÷density division, and the echo
    // path — Android's is ScrollView.onLayout's framework re-clamp; iOS's is the
    // shell's OWN 6.2 shrink clamp in [applyScrollFrames], whose contentOffset
    // write fires the delegate synchronously inside the batch. The RULE both obey
    // is the contract's: conflate during a batch, flush after.

    /// AttachEvent("scroll") — only a viewport can scroll. Last-wins re-attach,
    /// the 4.2 watcher discipline: swap the handler on the LIVE wire (keeping its
    /// pending offset and in-flight flag — they describe the NODE, not the
    /// handler) instead of stacking a second slot.
    private func handleAttachScroll(nodeId: Int32, handlerId: Int32) {
        guard let scroll = views[nodeId] as? UIScrollView else {
            NSLog("[BnWidgetMapper] AttachEvent 'scroll' ignored: node \(nodeId) is a "
                  + "\(views[nodeId].map { String(describing: type(of: $0)) } ?? "unknown node"), "
                  + "not a UIScrollView")
            return
        }
        if let wire = scrollWires[nodeId] {
            wire.handlerId = handlerId
            scroll.delegate = wire.proxy // idempotent; the slot is single anyway
            return
        }
        // The proxy resolves the wire from the LIVE map at fire time (the sample
        // goes through [onScrollSample]'s lookup), so a detached node's late
        // sample no-ops — the 6.3 stale-callback discipline.
        let proxy = BnScrollDelegateProxy { [weak self] scrollView in
            self?.onScrollSample(nodeId: nodeId, offsetPt: scrollView.contentOffset.y)
        }
        scrollWires[nodeId] = BnScrollWire(handlerId: handlerId, proxy: proxy)
        scroll.delegate = proxy
    }

    /// A native scroll sample landed (main thread — the delegate fires there).
    /// No conversion (points ARE pt — see the section comment) and conflation:
    /// the slot holds ONE offset, and this sample REPLACES whatever was there.
    private func onScrollSample(nodeId: Int32, offsetPt: CGFloat) {
        guard let wire = scrollWires[nodeId] else { return } // detached/purged: stale sample, no-op
        scrollSamplesSeen += 1
        // Bounce/rubber-band can make contentOffset legitimately NEGATIVE
        // mid-gesture (Android's overscroll is a glow, so its offsets never
        // are). The raw sample is dispatched honestly — .NET's window math
        // clamps (BnListWindow.Compute), which is the one clamp the contract
        // has — and the freshest post-gesture sample supersedes it anyway.
        wire.pendingOffsetPt = Float(offsetPt)
        maybeDispatchScroll(nodeId)
    }

    /// Dispatches the node's pending offset IF the lane is available — not
    /// mid-batch, and no dispatch of this node's already in flight. Called from
    /// three places, which are exactly the three lane-availability edges: a new
    /// sample ([onScrollSample]), a completion (below), and the end of a patch
    /// batch ([flushScrollWires]).
    ///
    /// The completion marshals to the main queue (all conflation state is
    /// main-thread-only, like every other map in this class) and re-consults the
    /// LIVE map: the wire it captured may have been detached/purged, or the
    /// nodeId may already belong to a NEW node (ids restart — the 6.2/6.3
    /// lesson). Resetting the captured wire's flag and then looking the nodeId up
    /// fresh is what makes both cases harmless without a generation counter: a
    /// dead wire is unreachable from the map, and a new node's wire has its own
    /// independent flag.
    private func maybeDispatchScroll(_ nodeId: Int32) {
        if applyingBatch { return } // conflate; applyBatch's tail flushes
        guard let wire = scrollWires[nodeId] else { return }
        if wire.inFlight { return } // conflate; the completion flushes
        guard let offsetPt = wire.pendingOffsetPt else { return }
        wire.pendingOffsetPt = nil
        wire.inFlight = true
        scrollDispatchesSent += 1
        lastScrollDispatchPt = offsetPt
        // The payload is the offset as an INVARIANT float string — mirroring
        // NativeRenderer.ParseScrollOffset (NumberStyles.Float, invariant
        // culture) exactly: Swift's Float.description never localizes (a "1,5"
        // from a Dutch device would be a loud rc-2 fault, by design).
        submitScrollDispatch(handlerId: wire.handlerId, payload: offsetPt.description) { [weak self] in
            DispatchQueue.main.async {
                wire.inFlight = false
                self?.maybeDispatchScroll(nodeId)
            }
        }
    }

    /// The seam's default: [onScrollEvent] when the host wired one (BnRuntime
    /// does), else [onUiEvent] with a synchronous completion — the twin of
    /// Kotlin's constructor default, keeping every event-agnostic test unchanged.
    private func submitScrollDispatch(handlerId: Int32, payload: String,
                                      onComplete: @escaping () -> Void) {
        if let onScrollEvent = onScrollEvent {
            onScrollEvent(handlerId, payload, onComplete)
        } else {
            onUiEvent(handlerId, "scroll", payload)
            onComplete()
        }
    }

    /// The batch-end lane-availability: give every wire that conflated during the
    /// guard its dispatch chance. Snapshot the keys — a dispatcher completing
    /// synchronously (the seam's default) can re-enter the map.
    private func flushScrollWires() {
        guard !scrollWires.isEmpty else { return }
        for nodeId in Array(scrollWires.keys) { maybeDispatchScroll(nodeId) }
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

        // Phase 7.3 — the stateful controls' wire state, created with the node and
        // keyed on the NODETYPE (never the widget class — the 6.2 lesson: the
        // nodeType is the contract, the class is a table row that could change).
        if nodeType == Self.slider {
            sliderStates[nodeId] = BnSliderState()
        }
        // Phase 7.5 — the image's 7.5 wire state (placeholder state table + the
        // `error` wire), created with the node like the two above. Keyed on the
        // NODETYPE, and its existence doubles as "is this node an image?" for the
        // attach arm (the [contentNodes] membership discipline).
        if nodeType == Self.image {
            imageStates[nodeId] = BnImageWireState()
        }
        if nodeType == Self.picker {
            // The guard-cast is posture, exactly like the scroll arm above: a
            // `picker` whose view failed it would silently stop being a picker
            // (no dataSource → an empty wheel, no delegate → no dispatches).
            guard let pickerView = view as? UIPickerView else {
                NSLog("[BnWidgetMapper] node \(nodeId) is a `picker` node but its view is a "
                      + "\(type(of: view)), not a UIPickerView — the node is DROPPED (it would "
                      + "otherwise be a picker with no dataSource — an empty wheel — and no "
                      + "delegate — no dispatches).")
                assertionFailure("`picker` node \(nodeId) did not get a UIPickerView from makeView")
                views.removeValue(forKey: nodeId) // registered five lines up; a dropped node owns nothing
                return
            }
            let state = BnPickerState()
            // The delegate resolves the LIVE map at fire time ([handlePickerUserSelect]
            // starts with a lookup), so a purged node's late delegate call no-ops —
            // the 6.3 stale-callback discipline.
            state.onUserSelect = { [weak self] row in
                self?.handlePickerUserSelect(nodeId, row: row)
            }
            pickerStates[nodeId] = state
            // Both UIPickerView slots are WEAK — the map above is the retainer
            // (the [eventTargets] ownership shape).
            pickerView.dataSource = state
            pickerView.delegate = state
        }

        // The view a child of this node parents INTO: for a child of a scroll node that
        // is the CONTENT view, never the UIScrollView (non-negotiable #2); for a child
        // of a modal node the OVERLAY, never the anchor (Phase 7.4, the third
        // index-mapping rule).
        //
        // insertIndex counts HOST views in the target container 1:1 (collapsed text
        // nodes never materialize a view, and they alias onto non-container parents,
        // so they cannot skew a container's indices — the same invariant as Android).
        // -1 = append (explicit; 0 is a valid front index).
        //
        // Phase 7.4 — the ONE exception to the root's 1:1: live modal OVERLAYS are
        // shell-side extras at the END of the host root's child list, so a top-level
        // wire APPEND slots in AHEAD of them ("the overlay is LAST, always" — a new
        // page must never draw over an open modal's scrim, and the root's index
        // arithmetic must not skew). Indexed inserts pass through unchanged: wire
        // indices count wire children, which all sit before the overlays by this very
        // rule. The SAME resolved index feeds both trees below — the view tree and the
        // Yoga host root mirror each other 1:1, so one arithmetic serves both (and the
        // .mm clamps an out-of-range index exactly as the view insert does — the
        // recorded 6.1 decision).
        let parentView: UIView = containerFor(parentId) ?? root
        let resolvedIndex: Int32 = (insertIndex >= 0)
            ? insertIndex
            : (parentView === root ? Int32(parentView.subviews.count - modalOverlays.count) : -1)
        if resolvedIndex >= 0 && Int(resolvedIndex) <= parentView.subviews.count {
            parentView.insertSubview(view, at: Int(resolvedIndex))
        } else {
            parentView.addSubview(view)
        }

        // The Yoga twin, inserted at the SAME index in the SAME parent. The parent is
        // re-derived from the view we ACTUALLY parented to (not from the patch), so an
        // unknown parentId that fell back to the host root falls back to the Yoga host
        // root too — and a child of a scroll node lands under the CONTENT node, because
        // `viewToNode[contentView]` IS the content node; a child of a modal node lands
        // under the OVERLAY node the same way. One rule, both trees.
        let node = bn_yoga_node_new()
        yogaNodes[nodeId] = node
        viewToNode[ObjectIdentifier(view)] = node
        if Self.measuredNodeTypes.contains(nodeType) {
            // Phase 6.3: an `image` measures its BYTES, not its widget — the ONE measured
            // nodeType whose native widget answers in the wrong unit. See
            // [bnYogaImageMeasureTrampoline]; everything else asks the widget (DoD #3).
            let measure = (nodeType == Self.image)
                ? bnYogaImageMeasureTrampoline
                : bnYogaMeasureTrampoline
            bn_yoga_node_set_measure(node, measure, Unmanaged.passUnretained(view).toOpaque())
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
        bn_yoga_node_insert_child(parentNode, node, resolvedIndex)

        // Phase 7.4 — the modal's TWO shell-side pieces, in both trees, in the same
        // breath (design decision 1). AFTER the anchor took its wire slot above, so a
        // TOP-LEVEL modal's overlay still lands above its own anchor.
        if nodeType == Self.modal {
            // THE ANCHOR's shell-fixed styles: absolutely positioned and 0-sized —
            // out of the flex flow entirely, contributing nothing to any sibling's
            // frame (the third index-mapping rule's Yoga half; the demo's sibling
            // frame table is the pin). Applied through the same grammar the wire
            // uses — the .mm's parser accepts all of these by construction, and
            // [applyShellStyle] asserts it stays true.
            applyShellStyle(node, "position", "absolute")
            applyShellStyle(node, "width", "0")
            applyShellStyle(node, "height", "0")

            // THE OVERLAY — attached LAST at the host root in BOTH trees (stacking
            // is creation order; the shell never re-orders; a re-shown modal is a
            // re-created one and lands on top). Its styles are SHELL-FIXED and they
            // are the whole of the modal's geometry: full-root (the scrim IS the
            // root's own bounds, re-solved for free on every host resize — the
            // overlay lives in the ONE tree the existing resize hook re-solves) and
            // justify/align CENTER — the design's ((W − w)/2, (H − h)/2) arithmetic
            // IS that pair; the wire carries no layout for it (the modal node's
            // zero-styles rule), so both shells fix the same pair and the frame
            // tables agree by construction.
            let overlayView = BnModalOverlayView()
            root.addSubview(overlayView)
            let overlayNode = bn_yoga_node_new()
            applyShellStyle(overlayNode, "position", "absolute")
            applyShellStyle(overlayNode, "top", "0")
            applyShellStyle(overlayNode, "left", "0")
            applyShellStyle(overlayNode, "width", "100%")
            applyShellStyle(overlayNode, "height", "100%")
            applyShellStyle(overlayNode, "justifyContent", "center")
            applyShellStyle(overlayNode, "alignItems", "center")
            bn_yoga_node_insert_child(hostRoot, overlayNode, -1)
            modalOverlays[nodeId] = overlayView
            overlayNodes[nodeId] = overlayNode
            viewToNode[ObjectIdentifier(overlayView)] = overlayNode
        }
    }

    /// A SHELL-FIXED style (the modal's anchor/overlay geometry), applied through
    /// the one grammar the wire uses — no second style surface to keep in step with
    /// the `.mm`'s. Every name/value here is in the routing table by construction;
    /// the assertion turns "the grammar moved under the shell" from a silent
    /// geometry bug (an anchor back in the flex flow) into a loud test failure.
    private func applyShellStyle(_ node: UnsafeMutableRawPointer, _ name: String, _ value: String) {
        let rc = name.withCString { n in
            value.withCString { v in bn_yoga_node_set_style(node, n, v) }
        }
        if rc != 1 {
            NSLog("[BnWidgetMapper] shell-fixed style \(name)=\(value) was REJECTED by the "
                  + "style grammar — the modal's fixed geometry is broken")
            assertionFailure("shell-fixed style \(name)=\(value) rejected")
        }
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
        // Phase 7.4 — the THIRD index-mapping rule: a `modal` node's children go
        // into its OVERLAY, never the anchor. On Android the anchor is a
        // non-ViewGroup, so a missed redirection fails loudly; here every UIView
        // can host, so THIS line is the redirection AND the only one — the anchor's
        // zero-footprint frame pin is what catches a miss.
        if let overlay = modalOverlays[parentId] { return overlay }
        if let content = scrollContents[parentId] { return content }
        return views[parentId]
    }

    // ── Font parity Gate B (#126): the bundled Inter, forced on every text leaf ──
    /// Whether the Inter fallback has already been logged — the miss is logged ONCE
    /// (not per label) so a name regression is visible in the log without flooding it.
    private static var interFontFallbackLogged = false

    /// The one place a `UILabel`/measure gets its font. Resolves the bundled Inter by
    /// its PostScript name (Gate A registered it via `UIAppFonts`; `BnFontTests` guards
    /// the name). A nil here means the registration/name regressed — we fall back to the
    /// system font so text still renders, but LOG it once, because a silent fallback is
    /// exactly the parity break this feature exists to close.
    private static func interFont(ofSize size: CGFloat) -> UIFont {
        if let inter = UIFont(name: "Inter-Regular", size: size) { return inter }
        if !interFontFallbackLogged {
            interFontFallbackLogged = true
            NSLog("[BnWidgetMapper] Inter-Regular did not resolve — falling back to the system "
                + "font. FONT PARITY IS BROKEN: check the bundled Inter-Regular.ttf is in the app "
                + "bundle and Info.plist UIAppFonts registers it (see BnFontTests).")
        }
        return .systemFont(ofSize: size)
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
            // Font parity Gate B (#126): force Inter at creation, at the system default
            // label point size (`UIFont.labelFontSize` — 17pt, what an unstyled UILabel
            // uses today) so only the FAMILY changes, not the visible default size. The
            // measure path is `sizeThatFits`, which reads `label.font`, so this one
            // assignment also makes measurement use Inter (no separate measure font).
            label.font = Self.interFont(ofSize: UIFont.labelFontSize)
            return label
        case "button":
            let button = UIButton(type: .system)
            return button
        case "input":
            let field = UITextField()
            field.borderStyle = .roundedRect
            return field
        case Self.image:
            // A REAL UIImageView (6.1's Gate 3 review made it one, so an EMPTY image
            // measures .zero by construction rather than by accident — exactly the
            // pre-load state 6.3 needs). Phase 6.3 gives it its natural size, and
            // therefore its own subclass: [BnImageView] carries the decoded bytes'
            // PIXEL COUNT, which is what its measure func reports.
            let image = BnImageView()
            // ── THE CONTENT MODE IS PART OF THE PARITY CONTRACT, AND IT IS SET
            //    EXPLICITLY BECAUSE THE TWO FRAMEWORK DEFAULTS DISAGREE ──────────────
            // `UIImageView`'s default is `.scaleToFill` — a STRETCH — and Android's
            // `ImageView` is `FIT_CENTER` (aspect-preserving). For `/image`'s case [0]
            // (a 64 × 48 fixture inside a DECLARED 200 × 120 frame) that means Android
            // letterboxes and iOS would DISTORT.
            //
            // It is FRAME-NEUTRAL, so every number in every frame table survives it and
            // **no test on either platform could catch it**: what breaks is "renders
            // identically", silently, on one platform. Aspect-fit because it cannot LIE
            // about the pixels (and this phase's whole subject is an image reporting its
            // true size); because it is free on the intrinsic path (there the frame IS
            // the natural size, so fit and fill are pixel-identical); and because it is
            // what an M7 `ContentMode` would default to. **Deferring the ContentMode API
            // (design decision 3) does not defer the DEFAULT** — 6.1's `clipChildren =
            // false` precedent: it costs one line to align the two shells.
            //
            // Phase 7.5 (decision 3): `ContentMode` now EXISTS ([handleUpdateProp]'s
            // `contentMode` arm), and `contain` is that default's name. This explicit
            // set stays: an image whose wire never carries the prop must still paint
            // aspect-fit, and `bnContentModeFor(nil)`'s CONTAIN row is the same
            // decision arriving by the other door (the prop-removed reset).
            image.contentMode = .scaleAspectFit
            // ── Phase 7.5 (decision 3's corollary): THE PAINT NEVER ESCAPES THE
            //    LAYOUT BOX — `clipsToBounds = true`, ALWAYS, AT CREATION ───────────
            // `Cover` and `Center` can both paint BIGGER than the box, and iOS does
            // not clip: `.scaleAspectFill` and `.center` bleed over siblings without
            // this. Android's ImageView clips its drawable to its bounds BY
            // CONSTRUCTION, so this one line is what aligns the two shells (the 6.1
            // `clipChildren` precedent) — and, like that one, it is INVISIBLE to
            // every frame assertion (the Yoga box never changes with mode), which is
            // precisely why it is pinned as a PROPERTY
            // (BnImagePolishMapperTests' creation pin — the design's named iOS
            // mutation: drop this line ⇒ that pin red, nothing else).
            image.clipsToBounds = true
            return image
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
        case "checkbox", "switch":
            // Phase 7.3, decision 2 — **iOS HAS NO NATIVE CHECKBOX**: UIKit's toggle
            // is UISwitch (React Native ships only `Switch` for the same reason), so
            // `checkbox` and `switch` are semantically identical here and visually
            // distinct only on Android (CheckBox vs Switch). A RECORDED
            // "renders natively, not identically" divergence — same wire, same
            // events, same declared frames, different pixels. A custom-drawn
            // checkbox would buy pixel parity at the cost of not being native —
            // against the project's thesis.
            return UISwitch()
        case Self.slider:
            // Phase 7.3 — float-native (no int-progress geometry to derive; see
            // [BnSliderState] for the one thing that DID survive the port: the
            // step contract on the wire payload). [BnSliderView], not a bare
            // UISlider: the iOS 26 widget reconstructs `value` from a Float32
            // track fraction, so a bare one reads back 60.000004 for the exact
            // 60 the shell just set — see the subclass header.
            return BnSliderView()
        case Self.picker:
            // Phase 7.3 — REAL since this phase: a UIPickerView whose
            // dataSource/delegate is the node's [BnPickerState] (wired in
            // [handleCreate], where the nodeId is known). NodeType 7 was a
            // do-nothing placeholder from 2.5 until now. No BnSpinner-style
            // self-layout subclass — `selectRow` applies immediately on iOS;
            // that class exists purely because of Android's layout-coupled
            // selection delivery (its KDoc says so by name).
            return UIPickerView()
        case Self.modal:
            // Phase 7.4 (design decision 1): the modal's WIRE view is the ANCHOR —
            // see [BnModalAnchorView]'s header for the third index-mapping rule.
            // The overlay is created by [handleCreate] AFTER the anchor takes its
            // wire slot.
            return BnModalAnchorView()
        case "activityindicator":
            // Phase 7.4 (design decision 5, the parity survey's cheap win): the
            // measured leaf. `UIActivityIndicatorView.medium` — the design's named
            // widget; SPINNING is set explicitly because "animating while mounted"
            // is the contract (a stopped indicator hides itself — UIKit's
            // hidesWhenStopped default — and an invisible leaf is not the cheap
            // win the survey shipped). No props, no events, no children —
            // presence is @if, stop == RemoveNode (the decision-2 posture); its
            // intrinsic size is the PLATFORM's own, asserted by ORACLE (the 6.3
            // method — 7.3's lesson: per-platform intrinsics, never
            // cross-platform pixel claims), never a transcribed constant.
            let spinner = UIActivityIndicatorView(style: .medium)
            spinner.startAnimating()
            return spinner
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
    /// A nested child COMPONENT disposed in the same batch as a removed ancestor
    /// still emits RemoveNode for its own root views — and since 7.2 (disposal
    /// removes are emitted BEFORE the batch's diffs; the host contract on
    /// `EmitDisposedComponentRemoves`, 7.2's split of the 3.3-era
    /// `ProcessDisposedComponent`) those child removes PRECEDE the ancestor's
    /// rather than trailing it: they arrive for ids still LIVE and detach the
    /// child view before its ancestor, a legal detach order (the ancestor's
    /// subtree purge simply finds one view fewer). Under the old trailing order
    /// they no-opped on already-purged ids; both orders land on the same `guard`
    /// below — unknown ids are a no-op, the documented host contract.
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
        var doomed = subtree(of: view)

        // Phase 7.4 — **THE TWO-SUBTREE PURGE** (design decision 1; NOT the scroll
        // shape, and the design names the difference so it cannot be an
        // assumption): a modal's overlay is not a view descendant of its anchor —
        // it hangs off the host root — so the walk above can never find it. Any
        // modal whose ANCHOR is doomed takes its OVERLAY subtree with it; the
        // [modalOverlays] entry is what names the second subtree. A FIXPOINT,
        // because a modal can sit INSIDE another modal's overlay (BnModal in
        // ChildContent), so dooming one overlay can doom another modal's anchor.
        // It rides the SUBTREE purge for the 6.2/6.3 reason: navigating away from
        // /modal with the modal open names the PAGE, never the modal inside it.
        // Miss this and Android leaks the overlay, the content box and every row
        // under it once per dismissal — HERE the same miss leaves [overlayNodes]
        // holding a YGNodeRef the next calculateAndApply dereferences.
        var grew = true
        while grew {
            grew = false
            for (modalId, overlay) in modalOverlays {
                if doomed.contains(ObjectIdentifier(overlay)) { continue }
                guard let anchor = views[modalId] else { continue }
                if doomed.contains(ObjectIdentifier(anchor)) {
                    doomed.formUnion(subtree(of: overlay))
                    grew = true
                }
            }
        }

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

        // Phase 7.4 — each doomed overlay's Yoga subtree, freed EXACTLY ONCE (the
        // iOS memory-safety law: a double free is as fatal as a dangling ref).
        // The arithmetic that makes "once" true: overlay nodes all hang DIRECTLY
        // off [hostRoot] — never inside another overlay's Yoga subtree, even for a
        // NESTED modal, whose ANCHOR node sits in the outer overlay's subtree but
        // whose overlay node does not — so none of these frees can overlap the
        // named node's free above or each other's. (A nested modal's anchor node
        // was ALREADY freed by the outer overlay's free_subtree; its [yogaNodes]
        // entry is evicted by the id sweep below, never freed a second time.)
        // The wire nodes INSIDE an overlay (the content box and everything under
        // it) are freed here too — their ids are in the sweep below because the
        // fixpoint put their views in `doomed`.
        let doomedOverlayIds = modalOverlays
            .filter { doomed.contains(ObjectIdentifier($0.value)) }
            .map(\.key)
        for id in doomedOverlayIds {
            if let overlayNode = overlayNodes.removeValue(forKey: id) {
                if let owner = bn_yoga_node_get_owner(overlayNode) {
                    bn_yoga_node_remove_child(owner, overlayNode)
                }
                bn_yoga_node_free_subtree(overlayNode)
            }
            // The view half leaves the host root's child list in the same breath
            // (the anchor leaves via the generic removeFromSuperview below or its
            // ancestor's detach); the map entry — which is what answers "is N a
            // modal?" and holds the redirection — dies with it (ids are reused;
            // the [scrollContents] discipline).
            modalOverlays.removeValue(forKey: id)?.removeFromSuperview()
        }

        // The doomed IDS first, then ONE sweep per map — the shape Kotlin's
        // `YogaLayout.removeNode` already has (`doomedIds` → `removeAll { it.first in
        // doomedIds }`). The two purges are meant to be read side by side, and the
        // diagnostics sweep is the reason it is not merely cosmetic: nested in the map
        // loop it was a `removeAll` over the whole diagnostics list PER doomed id.
        let doomedIds = Set(views.filter { doomed.contains(ObjectIdentifier($0.value)) }.keys)
        for id in doomedIds {
            // ── Phase 6.3: CANCEL, AND IT IS MEMORY SAFETY (non-negotiable #4) ──────────
            // The Yoga subtree was freed four lines up. A request still in flight into a node
            // in it would complete into a DETACHED UIImageView — harmless — and then
            // `markDirty` a **freed `YGNodeRef`**. That is 6.2's dangling-pointer lesson in a
            // new costume, and it is why this hangs off the SUBTREE purge rather than off the
            // named node: navigation emits ONE RemoveNodePatch, and it names the PAGE's root
            // column — never the image inside it.
            //
            // The generation goes with it, which is what makes the guard behind the cancel
            // work: a completion already in its main-thread continuation when `cancel()` ran
            // finds `imageGenerations[id] == nil` and drops itself (`bnIsLiveImageRequest`).
            cancelImageRequest(id)
            imageGenerations.removeValue(forKey: id)

            views.removeValue(forKey: id)
            yogaNodes.removeValue(forKey: id)
            collapsedAliases.remove(id) // an aliased text child of a doomed UIButton
            // The scroll node's two synthetic halves, dropped together (the content VIEW
            // is in `doomed` — it is a subview of the UIScrollView — so this entry is the
            // only thing that would survive to pin it).
            contentNodes.removeValue(forKey: id)
            scrollContents.removeValue(forKey: id)
            // Phase 7.2 — the purge half of the stale-callback discipline: a
            // removed scroll node's conflation slot dies here, pending offset and
            // all, NEVER dispatched (the wire contract's detach/purge row). Rides
            // the SUBTREE purge for the 6.3 reason: navigation names the page,
            // never the scroll inside it.
            scrollWires.removeValue(forKey: id)
            // Phase 7.3 — the form controls' wire state dies with the node, for
            // the same two reasons every other map purges here: ids restart, and
            // an entry outliving its node answers for the next node to inherit
            // the id (a late delegate call would compare against a ghost).
            sliderStates.removeValue(forKey: id)
            pickerStates.removeValue(forKey: id)
            // Phase 7.5 — the image wire state dies with the node, and the `error`
            // wire dies INSIDE it: a failure that terminates after this purge finds
            // no handler and DROPs at the decision (`bnImageErrorDispatchAction` —
            // whose liveness half already said DROP anyway, the generation having
            // been evicted two lines up). Ids restart; an entry outliving its node
            // would answer for the next node to inherit the id.
            imageStates.removeValue(forKey: id)
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
        // Phase 7.4 — …and the click recognizers, ALSO by identity: a modal's
        // recognizer sits on its OVERLAY, whose nodeId is the modal's — the view
        // it is attached to, not the key, is what says whether it is doomed.
        for (id, recognizer) in clickRecognizers {
            guard let attached = recognizer.view, doomed.contains(ObjectIdentifier(attached)) else { continue }
            recognizer.bnDetach()
            clickRecognizers.removeValue(forKey: id)
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
            switch view {
            case let field as UITextField:
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
            // Phase 7.3 — checkbox/switch. The wire grammar is EXACTLY
            // "true"/"false" (ordinal — BnCheckbox's header; "True" is garbage
            // there and is garbage here). null = the attribute was removed →
            // the component default (false). `setOn` fires nothing (the
            // verified per-control finding — the attach arm's comment has it),
            // so this write-back needs no guard on this platform.
            case let toggle as UISwitch:
                switch value {
                case "true": toggle.setOn(true, animated: false)
                case "false", nil: toggle.setOn(false, animated: false)
                default:
                    NSLog("[BnWidgetMapper] UpdateProp value ignored on node \(nodeId): "
                          + "'\(value ?? "nil")' is not the checkbox wire grammar "
                          + "(exactly \"true\"/\"false\")")
                }
            // Phase 7.3 — slider: store the RAW wire float, re-apply the whole
            // state ([applySlider] — order-independent within a batch).
            case let slider as UISlider:
                guard let state = sliderStates[nodeId] else {
                    NSLog("[BnWidgetMapper] UpdateProp value for slider \(nodeId) has no state: ignored")
                    return
                }
                guard let v = value.flatMap(Self.parseWireFloat) else {
                    NSLog("[BnWidgetMapper] UpdateProp value ignored on slider \(nodeId): "
                          + "'\(value ?? "nil")' is not an invariant float")
                    return
                }
                state.value = v
                applySlider(slider, state)
            default:
                NSLog("[BnWidgetMapper] UpdateProp value ignored: node \(nodeId) is a "
                      + "\(type(of: view)), not a value-bearing widget")
            }
        // Phase 7.3 — the slider's declared range/step (BnSlider always declares
        // min/max; step only when set — null resets to continuous).
        case "min", "max", "step":
            guard let slider = view as? UISlider else {
                NSLog("[BnWidgetMapper] UpdateProp \(name) ignored: node \(nodeId) is a "
                      + "\(type(of: view)), not a UISlider")
                return
            }
            guard let state = sliderStates[nodeId] else {
                NSLog("[BnWidgetMapper] UpdateProp \(name) for slider \(nodeId) has no state: ignored")
                return
            }
            var parsed: Float?
            if let value = value {
                guard let v = Self.parseWireFloat(value) else {
                    NSLog("[BnWidgetMapper] UpdateProp \(name) ignored on slider \(nodeId): "
                          + "'\(value)' is not an invariant float")
                    return
                }
                parsed = v
            }
            switch name {
            case "min": state.min = parsed ?? 0
            case "max": state.max = parsed ?? 100
            default: state.step = parsed // null = continuous (the un-styled invariant)
            }
            applySlider(slider, state)
        // Phase 7.3 — the picker's two props (the state-owner precedent).
        case "items":
            if let picker = view as? UIPickerView {
                handleItems(nodeId: nodeId, picker: picker, json: value)
            } else {
                NSLog("[BnWidgetMapper] UpdateProp items ignored: node \(nodeId) is a "
                      + "\(type(of: view)), not a UIPickerView")
            }
        case "selectedIndex":
            if let picker = view as? UIPickerView {
                handleSelectedIndex(nodeId: nodeId, picker: picker, value: value)
            } else {
                NSLog("[BnWidgetMapper] UpdateProp selectedIndex ignored: node \(nodeId) is a "
                      + "\(type(of: view)), not a UIPickerView")
            }
        case "enabled":
            if let control = view as? UIControl {
                control.isEnabled = (value as NSString?)?.boolValue ?? true
            } else if let picker = view as? UIPickerView {
                // Phase 7.3 — UIPickerView is not a UIControl and has no
                // `isEnabled`; the gate UIKit gives a plain view is touch
                // delivery. A disabled picker's wheel cannot be moved, so its
                // delegate never fires — the "disabled dispatches nothing"
                // enforcement, by the platform's own mechanism. (Android's
                // Spinner greys itself via isEnabled; the missing grey here is
                // pixels no assertion reads — the decision-2 posture.)
                picker.isUserInteractionEnabled = (value as NSString?)?.boolValue ?? true
            }
        // Phase 7.4 — the scrim's paint. A PROP, not a style, by design: SetStyle
        // on a `modal` node is diagnosed-and-ignored (every style would land on
        // the anchor or the overlay, neither of which the author owns), so the
        // one paintable thing the author DOES own arrives on the prop wire.
        // ALWAYS emitted by BnModal (the BnInput posture — no shell-side default
        // two platforms would have to keep equal). Unparseable is logged and
        // ignored, the backgroundColor arm's posture.
        case "scrimColor":
            guard let overlay = modalOverlays[nodeId] else {
                NSLog("[BnWidgetMapper] UpdateProp scrimColor ignored: node \(nodeId) is not a modal")
                return
            }
            guard let color = value.flatMap(BnColor.parse) else {
                NSLog("[BnWidgetMapper] UpdateProp scrimColor ignored on modal \(nodeId): "
                      + "'\(value ?? "nil")' is not a parseable color")
                return
            }
            overlay.backgroundColor = color
        // Phase 6.3 (M6 DoD #5): the LAST stubbed leaf stops being one. `src` is a PROP,
        // not a style — a URL is neither layout nor paint, so it rides this wire and not
        // the partitioned SetStyle routing table (BnImage.cs's header).
        case "src":
            if let image = view as? BnImageView {
                handleSrc(nodeId: nodeId, view: image, url: value)
            } else {
                NSLog("[BnWidgetMapper] UpdateProp src ignored: node \(nodeId) is a "
                      + "\(type(of: view)), not an image")
            }
        // Phase 7.5 (design decision 1) — the image's placeholder COLOR. A PROP, not a
        // style (`StyleAttributePartitionTests` pins both 7.5 names as props), and NOT
        // the name `placeholder` — that has been the input hint since M2 (the arm at
        // the top of this switch), and reusing it would fork one prop's meaning by
        // NodeType. PAINT-ONLY by construction: it writes [BnImageView]'s placeholder
        // paint, never Yoga.
        case "placeholderColor":
            guard let image = view as? BnImageView else {
                NSLog("[BnWidgetMapper] UpdateProp placeholderColor ignored: node \(nodeId) is a "
                      + "\(type(of: view)), not an image")
                return
            }
            guard let state = imageStates[nodeId] else {
                NSLog("[BnWidgetMapper] UpdateProp placeholderColor for image \(nodeId) has no "
                      + "state: ignored")
                return
            }
            guard let value = value else {
                // The author took the parameter away (the Enabled-null precedent): no
                // placeholder — and if one is on screen right now (in flight / error),
                // it goes with the setting that painted it.
                state.placeholderColor = nil
                if state.awaitingBytes { image.bnClearPlaceholder() }
                return
            }
            guard let color = BnColor.parse(value) else {
                NSLog("[BnWidgetMapper] UpdateProp placeholderColor ignored on image \(nodeId): "
                      + "'\(value)' is not a parseable color") // the backgroundColor posture
                return
            }
            state.placeholderColor = color
            // The props ride the wire in seq order — `src` (24) BEFORE `placeholderColor`
            // (25) — so on the ordinary mount the request is already in flight when this
            // arrives: paint the IN-FLIGHT (or ERROR) row now. A node showing real bytes
            // keeps them (the SUCCESS row: the placeholder never paints over bytes).
            if state.awaitingBytes { image.bnShowPlaceholder(color) }
        // Phase 7.5 (design decision 3) — the content mode. THE TABLE IS THE SHARED
        // DECISION (`bnContentModeFor` — strict four-word set, null restores the
        // default, unknown → diagnose-don't-apply); this arm is the lookup plus the
        // iOS spelling of each row. PAINT-ONLY, normatively: the layout box is Yoga's
        // and never changes with mode — no markDirty, no Yoga write, and the measure
        // func never consults `contentMode` (it reads [BnImageView.bnNaturalSize]).
        // The paint never escapes the box: `clipsToBounds = true` at creation
        // ([makeView]'s image arm — the pinned corollary; Android's ImageView clips
        // by construction).
        case "contentMode":
            guard let image = view as? BnImageView else {
                NSLog("[BnWidgetMapper] UpdateProp contentMode ignored: node \(nodeId) is a "
                      + "\(type(of: view)), not an image")
                return
            }
            guard let mode = bnContentModeFor(value) else {
                // Diagnose loudly, apply NOTHING — the node keeps its current mode (the
                // modal style-ignore precedent; reachable by hand-rolled wire only, and
                // recorded where a test can read it, because the failure is silent on
                // every frame table by the mode-invariance rule itself).
                diagnose(nodeId: nodeId, kind: "contentMode",
                         message: "image node \(nodeId): contentMode '\(value ?? "nil")' is not "
                         + "one of the four strict wire words (contain/cover/stretch/center) — "
                         + "diagnosed and NOT applied; the node keeps its current mode (a guessed "
                         + "fallback is how two shells guess differently)")
                return
            }
            switch mode {
            case .contain: image.contentMode = .scaleAspectFit
            case .cover: image.contentMode = .scaleAspectFill
            case .stretch: image.contentMode = .scaleToFill
            case .center: image.contentMode = .center
            }
        default:
            NSLog("[BnWidgetMapper] UpdateProp '\(name)' not yet supported (Phase 6.3+ extends)")
        }
    }

    // ── Phase 6.3: IMAGES ────────────────────────────────────────────────────
    //
    // The model (design §"The model") — and there is no binary path on the wire, by
    // design: .NET names the source, THE SHELL FETCHES THE BYTES (React Native's model).
    //
    //   UpdateProp(nodeId, "src", url)
    //         → cancel any in-flight request for this node
    //         → clear the bytes it already holds  ← back to 0 × 0 until the new ones land
    //         → Kingfisher fetches + decodes (off the main thread)
    //         → on the MAIN thread: set the UIImage
    //                               record the NATURAL PIXEL SIZE (BnImageView)
    //                               markDirty            ← the 6.1 path
    //                               re-solve + apply     ← the 6.2 path
    //
    // ONE reflow, never two. That is why there is no placeholder (design decision 3): a
    // placeholder that MEASURED would reflow the page twice.

    /// `src` arrived — with a URL, or with **null**.
    ///
    /// Both are the same code path, and that is the point: the renderer emits
    /// `UpdateProp(nodeId, "src", null)` when an author sets `Src` back to null (a
    /// `RemoveAttribute` on a non-style name — `BnButton.Enabled`'s precedent, pinned in
    /// .NET by `BnComponentTests.BnImage_SrcGoesNull_EmitsUpdatePropNullOnThePropWire`),
    /// and the contract for it is *cancel, CLEAR, markDirty, re-solve* — an intrinsic node
    /// collapses back to 0 × 0 and **its siblings move back UP**. Which is exactly the
    /// first half of what a `src` CHANGE owes as well ("back to 0 × 0 until the new bytes
    /// land"). Two rows of the parity contract, one path; a shell that split them is a
    /// shell where one of them rots.
    ///
    /// No re-solve here: this runs inside [applyBatch], whose `CommitFrame` re-solves the
    /// whole tree at the end. Only the ASYNCHRONOUS completion — which arrives with no
    /// patch behind it — has to trigger its own ([resolveLayout]).
    private func handleSrc(nodeId: Int32, view: BnImageView, url: String?) {
        // ── THE GENERATION IS BUMPED FIRST, AND IT IS BUMPED BY *EVERY* `src` WRITE —
        //    INCLUDING A CLEAR. It sits above the early returns on purpose ──────────────
        //
        // **A clear cancels; a cancel RACES ITS OWN COMPLETION; and the generation is the only
        // thing that stops the loser painting.** [cancelImageRequest] is best-effort *by
        // definition* — that is the whole reason this counter exists. When `Src` goes to null
        // (or `""`, or an unparseable string) while a request is in flight **whose download has
        // already finished and whose completion is already on its way to the main queue**, the
        // `cancel()` below arrives too late: that completion reaches [onImageLoaded] with
        // generation *N*. If the clear had not bumped, it would find `imageGenerations[nodeId]`
        // still *N* and the very same view — `bnIsLiveImageRequest` would say **LIVE** — and it
        // would paint the stale bytes, record their natural size, `markDirty` and re-solve.
        // **The node the author just cleared would RE-INFLATE, and its sibling would move back
        // down** — defeating the contract's `Src` → `null` row ("cancel, CLEAR, collapse to
        // 0 × 0, siblings move UP") *and* "one reflow, never two", on this phase's own home
        // ground.
        //
        // Pinned by `BnImageTests.testEVERYSrcWriteBumpsTheGenerationINCLUDINGAClear…` (the bump
        // itself, as the number it is — no frame can see it) composed with
        // `BnImageGuardTests.testASupersededGenerationIsNotLive` (what the bump then BUYS).
        let generation = (imageGenerations[nodeId] ?? 0) + 1
        imageGenerations[nodeId] = generation

        cancelImageRequest(nodeId)
        // The bytes the node already holds go NOW, not when (or if) the new ones arrive:
        // "on a Src change the node measures 0 × 0 again until the new bytes land".
        view.image = nil
        view.bnNaturalSize = nil
        markDirty(view)

        // Phase 7.5 — the state-table row change rides the same one path as the clear
        // itself ("two rows of the parity contract, one path; a shell that split them
        // is a shell where one of them rots"): whatever src writes, the node is no
        // longer showing real bytes, and whatever placeholder was on screen went out
        // with them (it is repainted below iff a source is named — the IN-FLIGHT row).
        let imageState = imageStates[nodeId]
        imageState?.bytesPainted = false
        view.bnClearPlaceholder()

        // An EMPTY string is the null/clear contract, not a fetch of "". On iOS that is
        // not a nicety: **`URL(string: "")` is `nil`**, so a shell that force-unwrapped it
        // would CRASH — an NPE by another name. It is a SHELL decision, so it is written
        // into the shared contract rather than left for the two shells to make differently
        // (design §"The parity contract", the `Src` → `null` row).
        //
        // Phase 7.5: NO source names NO pending image — the placeholder was cleared with
        // the image above (the state table's `src → null` row) and must NOT be repainted;
        // and nothing here dispatches (`""` takes the null path: never fetched, so it can
        // never fail — no honest shell can send an empty src as a failure payload).
        guard let raw = url, !raw.isEmpty else {
            imageState?.srcPresent = false
            return
        }

        // Phase 7.5 (design decision 1) — the state table's IN-FLIGHT row: the
        // placeholder color fills the box while the request is out. It is PAINT inside
        // the box Yoga already gave the node (no markDirty, no natural size, so it
        // cannot measure by construction), and it is painted BEFORE the load below,
        // deliberately: a warm memory-cache hit completes SYNCHRONOUSLY INSIDE
        // `retrieveImage` (the 6.3 finding), and the real bytes must be the LAST
        // write, not the placeholder — Android's paint-before-enqueue ordering,
        // mirrored.
        imageState?.srcPresent = true
        if let color = imageState?.placeholderColor { view.bnShowPlaceholder(color) }

        guard let parsed = URL(string: raw) else {
            // ── Phase 7.5 (design decision 2): AN UNPARSEABLE NON-EMPTY URL IS A
            //    FAILURE, AND IT REACHES THE SAME DISPATCH SITE — NOT A SILENT LOG ────
            // This is iOS's own immediate-failure path, and it is SYNCHRONOUS BY
            // CONSTRUCTION: it terminates right here, inside `UpdateProp("src")`,
            // inside [applyBatch] — which is exactly what makes it the LIVE staging of
            // the defer-out-of-batch rule (`bnImageErrorDispatchAction`'s DEFER row;
            // Android cannot fail synchronously mid-batch, so its shell pins that row
            // on the JVM table only). The node's frames are the 6.3 failure row's,
            // unchanged: it was cleared above, it stays 0 × 0 (or holds its DECLARED
            // box), the placeholder painted above STAYS (the ERROR row).
            //
            // (Foundation's lenient parser makes this arm NARROW — `URL(string:)`
            // percent-encodes most garbage rather than rejecting it (the recorded 6.3
            // finding), but STRUCTURAL violations still return nil, and `handleSrc`
            // owes them a terminal verdict, not a shrug.)
            onImageFailed(nodeId: nodeId, generation: generation, view: view, url: raw,
                          error: BnImageUnparseableUrlError(raw: raw))
            return
        }

        // …and the generation the load below is issued under is the one taken above — which is
        // also load-bearing in the OTHER direction: a Kingfisher memory-cache hit completes
        // SYNCHRONOUSLY (see below), inside the call, and its completion asks
        // `bnIsLiveImageRequest` for this very number.

        // ── A MEMORY-CACHE HIT COMPLETES *INSIDE* THIS CALL ──────────────────────────
        // Kingfisher's callback queue is `.mainCurrentOrAsync` and [handleSrc] runs on the
        // main thread (inside [applyBatch]). So a request that hits the memory cache runs
        // the WHOLE completion — set-image, natural size, markDirty, [resolveLayout] — TO
        // COMPLETION BEFORE `load` RETURNS. That is the ordinary case on the SECOND mount of
        // any page whose images the process has already fetched (the cache is process-wide),
        // and it means the handle this line receives can already be spent.
        //
        // Recording it unconditionally is a PERMANENT LEAK: the entry is never removed (the
        // completion's [clearIfMine] ran before it existed), [inFlightImageCount] never
        // returns to 0 — an invariant several tests assert — and [handleRemove] would later
        // "cancel" a request that finished long ago. So: record only what is STILL LIVE.
        // `terminated` is the Swift shape of Coil's `disposable.isDisposed`.
        var terminated = false
        let task = BnImageLoader.load(url: parsed) { [weak self] outcome in
            terminated = true
            guard let self = self else { return }
            switch outcome {
            case .success(let image):
                self.onImageLoaded(nodeId: nodeId, generation: generation, view: view,
                                   url: raw, image: image)
            case .failure(let error):
                self.onImageFailed(nodeId: nodeId, generation: generation, view: view,
                                   url: raw, error: error)
            case .cancelled:
                self.onImageCancelled(nodeId: nodeId, generation: generation, view: view, url: raw)
            }
        }
        if let task = task, !terminated {
            imageRequests[nodeId] = InFlight(generation: generation, view: view, task: task)
        }
    }

    /// The bytes landed. On the MAIN thread (Kingfisher's callback queue is
    /// `.mainCurrentOrAsync`), which is what makes the four calls below safe to make
    /// back-to-back.
    private func onImageLoaded(nodeId: Int32, generation: Int, view: BnImageView,
                               url: String, image: UIImage) {
        guard !destroyed else { return } // see [recordImageResult]
        recordImageResult(BnImageResult(nodeId: nodeId, url: url, outcome: .success))
        guard isLive(nodeId: nodeId, generation: generation, view: view, url: url,
                     what: "completion") else { return }
        clearIfMine(nodeId: nodeId, generation: generation, view: view)

        view.image = image
        // Phase 7.5 — the state table's SUCCESS row: the bytes are the LAST write and
        // the placeholder is CLEARED by them. Letterbox bars under Contain now show
        // the view's BACKGROUND (`BackgroundColor` — a style), never the placeholder,
        // and `clipsToBounds` (set at creation) keeps Cover/Center's overdraw inside
        // the box those bars frame.
        imageStates[nodeId]?.bytesPainted = true
        view.bnClearPlaceholder()
        // THE NATURAL SIZE — the decoded PIXEL COUNT, read as points. One file pixel is one
        // dp/pt: the parity contract's UNIT row, and the only reading under which iOS and
        // Android compute the same frame. See [BnImageLoader.naturalPixelSize] (and its
        // 0 × 0 answer for a decoded image with no pixel buffer — the GIF/SVG ledger).
        if let natural = BnImageLoader.naturalPixelSize(of: image) {
            view.bnNaturalSize = natural
        } else {
            view.bnNaturalSize = .zero
            NSLog("[BnWidgetMapper] the image for node \(nodeId) (\(url)) decoded to a UIImage "
                  + "with NO pixel buffer (an animated/vector format): it has no natural size "
                  + "this shell knows how to read in the contract's unit (one FILE PIXEL is one "
                  + "dp/pt), so it measures 0 × 0 and reserves nothing. Ledgered — those formats "
                  + "need a design, not a guess.")
        }
        // …and the 6.1 path, WITHOUT WHICH THE IMAGE PAINTS AND THE PAGE NEVER MOVES: Yoga
        // caches a measure function's result and will not re-run it on a clean node.
        markDirty(view)
        // …and the 6.2 path. No patch is behind this frame — the wire carries no completion
        // signal — so the re-solve is the shell's to trigger. A NO-OP inside a batch.
        resolveLayout()
    }

    /// The load failed — a 404, a refused connection, a blocked cleartext fetch, a timeout,
    /// or (Phase 7.5, iOS's own) an unparseable URL that failed SYNCHRONOUSLY inside
    /// [handleSrc]. The node **keeps measuring 0 × 0 if intrinsic** (it was cleared when the
    /// request was issued) or **holds its declared box** (Yoga never measured it, so the
    /// failure *cannot* move the frame — the space stays reserved because it was DECLARED,
    /// not because it failed); it **reserves nothing it did not have**, and it **does not
    /// retry**. There is nothing to markDirty and nothing to re-solve: no frame changed,
    /// which is the whole content of the contract's failure row. **The placeholder STAYS**
    /// (Phase 7.5, the state table's ERROR row): nothing here touches the paint — the error
    /// state's visual, deliberately.
    ///
    /// ── PHASE 7.5: THE `error` DISPATCH (design decision 2) ──────────────────────────
    /// The one thing 6.3 could not say now rides the wire: the failure flows .NET-ward as
    /// the event name `error`, payload = **the wire's `src`, verbatim** ([dispatchError]).
    /// The DECISION is [decideAndDispatchError] → `bnImageErrorDispatchAction` — a pure
    /// function composing `bnIsLiveImageRequest` by name (one guard, two consumers: a
    /// superseded / purged / recycled request's error dispatches nothing, exactly as it
    /// paints nothing) plus the batch rule: **a dispatch never runs inside a patch batch —
    /// deferred to a fresh main-queue turn, NEVER dropped.** On THIS shell the synchronous
    /// failure is not a table-only case: the nil-URL path above fails inside [applyBatch]
    /// by construction, so the DEFER row runs live here where Android could only pin it on
    /// the JVM. At-most-once per terminated request is Kingfisher's own completion contract
    /// (one terminal verdict per request) times the liveness gate; the counted
    /// [errorDispatchesSent] is what the device asserts it by. CANCELLED never gets here
    /// at all ([onImageCancelled] has no dispatch site — structural, on purpose).
    private func onImageFailed(nodeId: Int32, generation: Int, view: BnImageView,
                               url: String, error: Error) {
        guard !destroyed else { return } // see [recordImageResult]
        recordImageResult(BnImageResult(nodeId: nodeId, url: url, outcome: .error))
        clearIfMine(nodeId: nodeId, generation: generation, view: view)
        NSLog("[BnWidgetMapper] image load failed for node \(nodeId) (\(url)): \(error) — the "
              + "node stays 0 × 0 and reserves nothing")
        decideAndDispatchError(nodeId: nodeId, generation: generation, view: view, url: url)
    }

    /// **THE DISPATCH DECISION, RE-ENTERED AT EVERY FIRE TIME.**
    ///
    /// The DEFER arm posts THIS FUNCTION to a fresh main-queue turn — it does NOT post a
    /// captured `dispatchError(handler, url)` — and the difference is not style, it is the
    /// stale-callback rule with the batch still open. The nil-URL failure terminates
    /// synchronously INSIDE [applyBatch], so ordinary two-patch frames put patches BEHIND
    /// the failure in the same batch:
    ///
    ///  - `src = <bad>` … `RemoveNode` later in the batch — a deferred dispatch captured
    ///    at decision time would fire for a PURGED node;
    ///  - `src = <bad>` … `src = <good>` later in the batch — it would deliver the
    ///    SUPERSEDED source's error into live user code, the exact class of stale callback
    ///    the generation exists to kill.
    ///
    /// So the deferred turn re-asks `bnImageErrorDispatchAction` with FIRE-TIME facts —
    /// the LIVE generation, the LIVE view, the LIVE handler — against the request's own
    /// captured (nodeId, generation, view, url). Both adversarial frames are pinned
    /// end-to-end in BnImagePolishMapperTests, and only this shell can stage them (Android
    /// has no deterministic in-batch failure; its DEFER arm currently replays a
    /// decision-time capture — latent there, live here).
    ///
    /// The re-entered decision can, in principle, answer DEFER again (a fresh turn cannot
    /// run mid-batch — [applyBatch] is synchronous on main — so this is defensive); it
    /// re-posts rather than drops: deferred, never dropped.
    private func decideAndDispatchError(nodeId: Int32, generation: Int, view: BnImageView,
                                        url: String) {
        let handlerId = imageStates[nodeId]?.errorHandlerId
        switch bnImageErrorDispatchAction(currentGeneration: imageGenerations[nodeId],
                                          requestGeneration: generation,
                                          currentView: views[nodeId],
                                          requestView: view,
                                          handlerAttached: handlerId != nil,
                                          applyingBatch: applyingBatch) {
        case .dispatchNow:
            dispatchError(handlerId!, url)
        case .deferToFreshTurn:
            DispatchQueue.main.async { [weak self] in
                self?.decideAndDispatchError(nodeId: nodeId, generation: generation,
                                             view: view, url: url)
            }
        case .drop:
            break
        }
    }

    /// We cancelled it: a `Src` change, a node removal, or teardown. Nothing is painted and
    /// nothing is re-solved — that is what "cancelled" means.
    private func onImageCancelled(nodeId: Int32, generation: Int, view: BnImageView, url: String) {
        guard !destroyed else { return } // see [recordImageResult]
        recordImageResult(BnImageResult(nodeId: nodeId, url: url, outcome: .cancelled))
        clearIfMine(nodeId: nodeId, generation: generation, view: view)
    }

    /// Appends to the bounded [imageResultLog], evicting the oldest. Main-thread only —
    /// every Kingfisher completion is.
    ///
    /// **A completion that lands AFTER [destroy] is dropped before it gets here** (the
    /// `guard !destroyed` at the top of all three terminal callbacks): [destroy] cancels every
    /// in-flight request and empties the log, and a completion already in its main-thread
    /// continuation when `cancel()` ran would otherwise RE-GROW the log and bump
    /// [imageTerminalCount] on a torn-down mapper — resurrecting, in bookkeeping, the very
    /// state the teardown exists to end.
    private func recordImageResult(_ result: BnImageResult) {
        imageTerminalCount += 1
        imageResultLog.append(result)
        if imageResultLog.count > Self.maxImageResults {
            imageResultLog.removeFirst(imageResultLog.count - Self.maxImageResults)
        }
    }

    /// **DROP THE IN-FLIGHT ENTRY ONLY IF IT IS THIS CALLBACK'S OWN.**
    ///
    /// Every terminal callback ends here, and every one of them asks BOTH questions — which
    /// is the whole of the rule. A `clearIfMine` that evicted on a **generation match alone**
    /// is exactly the case a generation cannot decide: `/image` → back → `/image` re-uses
    /// this mapper, **node ids restart at 1**, and the OLD node 2's cancellation callback
    /// (generation 1) arrives as a later main-thread message to find the NEW node 2 also on
    /// generation 1. It matches, and it evicts the LIVE request's `DownloadTask` — leaving a
    /// request nothing can cancel, **whose completion then marks a freed `YGNodeRef` dirty**
    /// (non-negotiable #4, arrived at through the guard that was supposed to prevent it).
    ///
    /// Comparing against the ENTRY (its generation, its view) rather than against
    /// `views`/`imageGenerations` is deliberate: it is a question about the *request*, and it
    /// answers correctly even for a node that has since been purged from both maps.
    ///
    /// **AND THE DECISION IS `bnIsLiveImageRequest`'s — the SAME pure function [isLive] asks,
    /// not a second inline copy of the conjunction.** It used to be a copy, and the copy was
    /// UNPINNED: `BnImageGuardTests` tests the function, so dropping `&& entry.view === view`
    /// from HERE ALONE left all 70 tests green — the mutation had to be applied in two places
    /// to redden one test, which is the definition of a second site nothing defends. One
    /// decision, one function, one unit test, two call sites. (What differs between the two
    /// call sites is only WHERE the "current" facts come from — the ENTRY here, the LIVE maps
    /// there — and that distinction is deliberate and is preserved.)
    private func clearIfMine(nodeId: Int32, generation: Int, view: BnImageView) {
        guard let entry = imageRequests[nodeId] else { return }
        if bnIsLiveImageRequest(currentGeneration: entry.generation,
                                requestGeneration: generation,
                                currentView: entry.view,
                                requestView: view) {
            imageRequests.removeValue(forKey: nodeId)
        }
    }

    /// **THE PURGED-NODE GUARD** — the defence behind the cancel, and it is not
    /// belt-and-braces theatre: [cancelImageRequest] is what *prevents* a completion, and
    /// this is what makes one HARMLESS if it ever arrives anyway (a completion already in
    /// its main-thread continuation when `cancel()` ran — and, on iOS, every cache-hit
    /// completion, for which Kingfisher hands back no cancellation handle at all).
    ///
    /// The DECISION is `bnIsLiveImageRequest` — a pure function, in its own file,
    /// **unit-tested with no UIKit tree at all** (`BnImageGuardTests`), including the reset
    /// collision that no single-mount test can stage. This method is the lookup and the log
    /// around it; the reasoning lives with the function.
    private func isLive(nodeId: Int32, generation: Int, view: BnImageView,
                        url: String, what: String) -> Bool {
        if !bnIsLiveImageRequest(currentGeneration: imageGenerations[nodeId],
                                 requestGeneration: generation,
                                 currentView: views[nodeId],
                                 requestView: view) {
            NSLog("[BnWidgetMapper] stale image \(what) for node \(nodeId) (\(url)) dropped: the "
                  + "node was removed, or its src was written again. Nothing painted, and no "
                  + "freed YGNodeRef was touched.")
            return false
        }
        return true
    }

    /// Cancels [nodeId]'s in-flight request, if any. Idempotent; safe for a node that never
    /// had one.
    private func cancelImageRequest(_ nodeId: Int32) {
        imageRequests.removeValue(forKey: nodeId)?.task.cancel()
    }

    /// A layout pass triggered by something that is NOT a patch — an image completion.
    ///
    /// **IT MUST NOT RUN INSIDE A BATCH.** A Kingfisher memory-cache hit completes
    /// SYNCHRONOUSLY, on the main thread, inside the `retrieveImage` that [handleSrc] issues
    /// from within [applyBatch] — so this method has exactly one RE-ENTRANT caller. Inside a
    /// batch it would re-solve Yoga against a HALF-APPLIED tree (the image's sibling band may
    /// not have been created yet — the patches are still arriving) and then again at
    /// `CommitFrame`: **two reflows, where the contract says ONE.**
    ///
    /// The final frames would be IDENTICAL either way, because the commit's pass fixes them
    /// up — which is exactly why no frame assertion can see this, and why
    /// [layoutPassCount] is pinned instead.
    ///
    /// So inside a batch this is a NO-OP, and it loses nothing: the batch's own `CommitFrame`
    /// re-solves the whole tree at the end, which is where the synchronously-recorded natural
    /// size and `markDirty` are picked up. Only a completion that arrives with NO patch
    /// behind it — the asynchronous case, the one the wire carries no signal for — has a
    /// layout pass to trigger.
    private func resolveLayout() {
        guard !applyingBatch else { return }
        calculateAndApply()
    }

    // ── Phase 7.3: THE FORM CONTROLS' PROP MACHINERY ─────────────────────────

    /// Re-applies the WHOLE slider state to the widget — float-native, so this
    /// is three assignments, not Android's int-geometry derivation. Ordering
    /// (min, max, THEN value) matters only within this call: `UISlider` clamps
    /// `value` against the range it has at assignment time, and re-applying all
    /// three on every prop write is what makes the patch order inside a batch
    /// immaterial (the last recompute wins — [BnSliderState]'s contract).
    ///
    /// None of these setters fires `.valueChanged` (the verified per-control
    /// finding), so unlike Android's `applySlider` this needs no batch guard.
    private func applySlider(_ slider: UISlider, _ state: BnSliderState) {
        slider.minimumValue = state.min
        slider.maximumValue = state.max
        slider.setValue(state.value, animated: false)
    }

    /// `items` arrived — the flat-JSON string array (the NORMATIVE grammar in
    /// `BnItemsJson.cs`'s header, parsed by the STRICT [BnItemsJson] — the same
    /// escaping matrix as the dispatch-args wire, none of its reader leniency).
    /// MALFORMED IS LOUD AND EMPTY: log + render an empty picker, never a wrong
    /// one (the grammar's own posture).
    ///
    /// ── THE CLAMP RULE (NORMATIVE — BnPicker.razor's header, mirrored verbatim
    /// by both shells; Android's twin is `WidgetMapper.handleItems`) ──────────
    ///
    ///   items empty      → selection −1 (the only state an empty picker has)
    ///   items non-empty  → clamp into [0, Count−1]
    ///
    /// Re-bind the wheel and CLAMP/PRESERVE the selection — always, on every
    /// items write, regardless of patch order (the normative
    /// items-before-selectedIndex + RE-CLAMP-on-items order, design decision 3).
    /// And NOTIFY-ON-MOVE: when the clamp MOVED a LIVE selection (an item
    /// shrink below it → the LAST item; the items emptied → −1), the shell
    /// dispatches the CLAMPED index on the change wire — the bound .NET state
    /// re-syncs to what the native widget actually shows, instead of the echo
    /// and the screen disagreeing. An in-range preserved selection dispatches
    /// nothing (the notify happens only when the clamp moved the value). The
    /// dispatch is asserted ON THE WIRE deliberately: .NET's benign inbound
    /// clamp makes a non-clamping shell invisible from the .NET side.
    ///
    /// THE ONE DELIBERATE ASYMMETRY: the empty→non-empty transition is NOT a
    /// move. A picker with no items has no selection to displace, and the same
    /// batch always carries the authoritative `selectedIndex` (BnPicker emits
    /// it unconditionally, AFTER `items` in attribute order). Notifying "0"
    /// there would race — and on the disabled picker (mounts at 1) could
    /// overwrite — that very prop. The base for the clamp in that case is the
    /// wire's OWN last request ([BnPickerState.requestedIndex]), which also
    /// makes the items/selectedIndex patch order immaterial.
    private func handleItems(nodeId: Int32, picker: UIPickerView, json: String?) {
        guard let state = pickerStates[nodeId] else {
            NSLog("[BnWidgetMapper] UpdateProp items for picker \(nodeId) has no state: ignored")
            return
        }
        // null = the attribute was removed; BnPicker never does (it writes "[]"),
        // so it is the raw wire's empty list, same code path as "[]".
        var items: [String] = []
        if let json = json {
            do {
                items = try BnItemsJson.parse(json)
            } catch {
                NSLog("[BnWidgetMapper] UpdateProp items for picker \(nodeId) is MALFORMED — "
                      + "rendering an EMPTY picker rather than a wrong one: \(error)")
                items = []
            }
        }
        let hadLiveSelection = state.appliedSelection >= 0
        let base = hadLiveSelection ? state.appliedSelection : (state.requestedIndex ?? -1)
        let clamped = Self.clampSelection(base, count: items.count)
        state.items = items
        state.appliedSelection = clamped
        picker.reloadAllComponents()
        // selectRow applies IMMEDIATELY and calls no delegate (verified by the
        // RAW-selectRow test — appliedSelection is already recorded above, so
        // a fire HERE would be swallowed by the same-row compare; that
        // record-before-apply ordering is itself the Spinner-style
        // expected-selection guard, which is why no BnSpinner is needed).
        if clamped >= 0 { picker.selectRow(clamped, inComponent: 0, animated: false) }
        markDirty(picker) // new items = a new intrinsic size (the widest item)
        if hadLiveSelection && clamped != base, let handlerId = state.handlerId {
            dispatchChange(handlerId, String(clamped))
        }
    }

    /// `selectedIndex` arrived — an invariant int, clamped INBOUND by the same
    /// normative rule (an out-of-range but well-formed index is a hand-rolled
    /// wire, not garbage — BnPicker clamps before emitting, so the wire always
    /// carries a clamped index already). Unparseable is logged and ignored.
    private func handleSelectedIndex(nodeId: Int32, picker: UIPickerView, value: String?) {
        guard let state = pickerStates[nodeId] else {
            NSLog("[BnWidgetMapper] UpdateProp selectedIndex for picker \(nodeId) has no state: ignored")
            return
        }
        var requested: Int?
        if let value = value {
            guard let parsed = Int(value) else {
                NSLog("[BnWidgetMapper] UpdateProp selectedIndex ignored on picker \(nodeId): "
                      + "'\(value)' is not an invariant int")
                return
            }
            requested = parsed
        }
        state.requestedIndex = requested
        let clamped = Self.clampSelection(requested ?? 0, count: state.items.count)
        state.appliedSelection = clamped
        if clamped >= 0 { picker.selectRow(clamped, inComponent: 0, animated: false) }
    }

    /// A USER pick landed (the delegate's `didSelectRow` — wheel gestures only;
    /// `selectRow` never calls it, verified by the RAW-selectRow test — a fire
    /// through the apply path would be swallowed by the same-row guard below,
    /// which is exactly why the raw test exists). Resolves the LIVE map first (the
    /// 6.3 stale-callback discipline), then the SAME-ROW guard: a wheel spun
    /// away and back to the current row is not a change — Android's
    /// AdapterView drops those the same way, so re-selecting the same position
    /// dispatches nothing on either shell.
    private func handlePickerUserSelect(_ nodeId: Int32, row: Int) {
        guard let state = pickerStates[nodeId] else { return } // purged: stale, no-op
        if row == state.appliedSelection { return } // the same-row guard
        state.appliedSelection = row
        if let handlerId = state.handlerId {
            // The payload is the new index as an invariant int (BnPicker's
            // strict parse).
            dispatchChange(handlerId, String(row))
        }
    }

    /// THE NORMATIVE CLAMP (BnPicker.razor's header): empty → −1; otherwise
    /// into [0, count−1] — a shrink below the selection lands on the LAST item,
    /// a negative on 0. One function, both call sites ([handleItems] /
    /// [handleSelectedIndex]) — the 6.3 one-decision-one-function lesson.
    internal static func clampSelection(_ index: Int, count: Int) -> Int {
        count <= 0 ? -1 : Swift.min(Swift.max(index, 0), count - 1)
    }

    /// The form-control props' STRICT float parse — the 6.1 number production
    /// (anchored: the WHOLE string must be consumed), the twin of Kotlin's
    /// `parseWireFloat` and mirrored for the same reason: `Float(String)`
    /// accepts spellings the other shell rejects (`"inf"`, `"nan"`, hex floats
    /// like `"0x1p3"`), and a value one shell honours and the other ignores
    /// makes the two shells' widgets disagree for a reason that has nothing to
    /// do with the engine. NOT [parseCGFloat] (the legacy visual-prop parser),
    /// which strips unit suffixes these props never carry.
    internal static func parseWireFloat(_ s: String) -> Float? {
        guard let _ = s.range(of: #"^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$"#,
                              options: .regularExpression) else { return nil }
        guard let v = Float(s), v.isFinite else { return nil }
        return v
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
        // Phase 7.4 — **SetStyle on a `modal` node is diagnosed-and-ignored**
        // (design decision 1, the scroll container-style rule's shape): every
        // style would land on the anchor or the overlay, neither of which the
        // author owns. ONE site, BEFORE the layout/visual routing, because the
        // rule covers EVERY name — a layout `width` would size the anchor back
        // into the flex flow, a visual `backgroundColor` would paint it.
        // BnModal's surface cannot emit one, but the hand-rolled-wire hatch is
        // open (the .NET test pins that), so this is live code, not dead —
        // recorded in [diagnostics] because the failure
        // it prevents is silent on every frame table. Membership in
        // [modalOverlays] is what makes a node "a modal node" here (the
        // [contentNodes] discipline).
        if modalOverlays[nodeId] != nil {
            diagnose(
                nodeId: nodeId, kind: "modal-style/\(property)",
                message: "SetStyle \(property) ignored: node \(nodeId) is a `modal` node, and a "
                + "modal's two shell-side pieces (the 0-sized anchor at its wire slot; the "
                + "full-root overlay) both carry SHELL-FIXED styles — every style would land on "
                + "a node the author does not own. Style the CONTENT BOX (the modal's wire "
                + "child) instead; the scrim's paint is the scrimColor PROP.")
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
            // Font parity Gate B (#126): the same bundled Inter as at creation, at the
            // requested size — so a `fontSize` restyle keeps the family. `markDirty`'s
            // re-measure runs through `sizeThatFits`/`label.font`, so measurement follows.
            label.font = Self.interFont(ofSize: size)
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
