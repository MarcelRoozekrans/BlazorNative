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
// And the Yoga node is a RAW native allocation: nothing will ever free it for you.
// [handleRemove] purges whole subtrees (one RemoveNodePatch stands for one) and
// [deinit] drops the tree — every navigation replaces it.
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

    init(root: UIView) {
        self.root = root
        self.hostRoot = bn_yoga_node_new()
    }

    deinit {
        // The Yoga tree is RAW native memory owned by the .mm — nothing collects it.
        // Freeing the host root frees every node still hanging off it (and clears
        // their measure funcs, breaking the last edges to the UIViews).
        bn_yoga_node_free_subtree(hostRoot)
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
        guard !yogaNodes.isEmpty else { return }
        let bounds = root.bounds
        bn_yoga_calculate(hostRoot, Float(bounds.width), Float(bounds.height))
        for (nodeId, node) in yogaNodes {
            guard let view = views[nodeId] else { continue }
            let frame = bn_yoga_node_get_frame(node)
            view.frame = CGRect(x: CGFloat(frame.x), y: CGFloat(frame.y),
                                width: CGFloat(frame.width), height: CGFloat(frame.height))
        }
    }

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
        views[nodeId] = view

        // insertIndex counts HOST views in the target container 1:1 (collapsed text
        // nodes never materialize a view, and they alias onto non-container parents,
        // so they cannot skew a container's indices — the same invariant as Android).
        // -1 = append (explicit; 0 is a valid front index).
        let parentView: UIView = parentId.flatMap { views[$0] } ?? root
        if insertIndex >= 0 && Int(insertIndex) <= parentView.subviews.count {
            parentView.insertSubview(view, at: Int(insertIndex))
        } else {
            parentView.addSubview(view)
        }

        // The Yoga twin, inserted at the SAME index in the SAME parent. The parent is
        // re-derived from the view we ACTUALLY parented to (not from the patch), so an
        // unknown parentId that fell back to the host root falls back to the Yoga host
        // root too — or the two trees diverge.
        let node = bn_yoga_node_new()
        yogaNodes[nodeId] = node
        viewToNode[ObjectIdentifier(view)] = node
        if Self.measuredNodeTypes.contains(nodeType) {
            bn_yoga_node_set_measure(node, bnYogaMeasureTrampoline,
                                     Unmanaged.passUnretained(view).toOpaque())
        }
        let parentNode: UnsafeMutableRawPointer = (parentView === root)
            ? hostRoot
            : (viewToNode[ObjectIdentifier(parentView)] ?? hostRoot)
        bn_yoga_node_insert_child(parentNode, node, insertIndex)
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
        case "image", "scroll", "picker":
            // Stubbed (design §1c): keep a placeholder so container indices stay
            // consistent. Scroll/picker are 6.2's — the honest boundary is that a
            // framework container runs its OWN layout over its children, so Yoga does
            // not get the final word inside one.
            NSLog("[BnWidgetMapper] nodeType '\(nodeType)' stubbed as a placeholder UIView (Phase 6.2+)")
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
        if let node = yogaNodes[nodeId] {
            if let owner = bn_yoga_node_get_owner(node) {
                bn_yoga_node_remove_child(owner, node)
            }
            bn_yoga_node_free_subtree(node)
        }

        for (id, mapped) in views where doomed.contains(ObjectIdentifier(mapped)) {
            views.removeValue(forKey: id)
            yogaNodes.removeValue(forKey: id)
            collapsedAliases.remove(id) // an aliased text child of a doomed UIButton
        }
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
