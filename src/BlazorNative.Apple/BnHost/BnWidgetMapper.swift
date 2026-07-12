// ─────────────────────────────────────────────────────────────────────────────
// BnWidgetMapper — Phase 5.2 (M5 DoD #2): maps decoded [BnFrame] patches to real
// UIKit view mutations. The imperative UIKit twin of the Android
// io.blazornative.shell.WidgetMapper — UIStackView ↔ LinearLayout, UILabel ↔
// TextView, UIButton ↔ Button, UITextField ↔ EditText.
//
// Threading (design §2): `apply(frame)` runs on the native frame-callback thread.
// The mapper BUFFERS patches until the CommitFrame patch, then hops to
// DispatchQueue.main.async to build/mutate the UIKit tree atomically — the exact
// twin of the Kotlin mainHandler.post(applyBatch) batch. Every frame ends with a
// CommitFrame patch. UIKit is main-thread-only, so ALL view work happens there.
//
// Node identity: an [Int32: UIView] registry (`nodes`). The Phase 2.8 text
// collapse aliases a text child's nodeId onto its text-bearing parent (a
// UILabel/UIButton/UITextField, i.e. a non-container), so a subsequent
// ReplaceText routes through the parent's title/text — mirroring Android's
// TextView-but-not-ViewGroup collapse. RemoveNode purges by IDENTITY (=== ), not
// key, because the collapse can alias several ids onto one view.
//
// Scope: CreateNode (view/text/button/input wired; image/scroll/picker → a
// placeholder UIView + log, to keep container indices consistent), ReplaceText,
// RemoveNode, UpdateProp(value/placeholder), SetStyle(backgroundColor/fontSize/
// padding), CommitFrame. AttachEvent/DetachEvent are LOGGED AND SKIPPED — they
// arrive in BnDemo's mount frame but interactivity is Phase 5.3.
// ─────────────────────────────────────────────────────────────────────────────

import UIKit

final class BnWidgetMapper {

    /// The host container the top-level (parentless) node is added into — the
    /// twin of Android's widget_root FrameLayout.
    private let root: UIView

    private var nodes: [Int32: UIView] = [:]
    /// Patches accumulate here until a CommitFrame flushes the batch to main.
    /// Touched only on the callback thread before the main hop.
    private var pending: [BnPatch] = []

    init(root: UIView) {
        self.root = root
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
                break // boundary marker; no-op here
            case .attachEvent(let nodeId, let eventName, _):
                // Phase 5.3 wires these; they ride BnDemo's mount frame. Skip.
                NSLog("[BnWidgetMapper] AttachEvent '\(eventName)' on node \(nodeId) skipped (interactivity = Phase 5.3)")
            case .detachEvent(let nodeId, _, let eventName):
                NSLog("[BnWidgetMapper] DetachEvent '\(eventName)' on node \(nodeId) skipped (interactivity = Phase 5.3)")
            }
        }
    }

    // ── CreateNode: build the view, honor the text collapse + mid-list insert ─

    private func handleCreate(nodeId: Int32, nodeType: String, parentId: Int32?, insertIndex: Int32) {
        // Text-child-of-non-container collapse (twin of WidgetMapper.handleCreate
        // ~267-283): a `text` node whose parent is a text-bearing NON-container
        // (UILabel/UIButton/UITextField) does not get its own view — alias its
        // nodeId onto the parent so the subsequent ReplaceText sets the parent's
        // title/text. UIStackView is the only container, so anything else is a
        // collapse target.
        if nodeType == "text", let pid = parentId, let rawParent = nodes[pid],
           isTextBearingNonContainer(rawParent) {
            nodes[nodeId] = rawParent
            return
        }

        let view: UIView = makeView(nodeType: nodeType)
        nodes[nodeId] = view

        let parent: UIView = parentId.flatMap { nodes[$0] } ?? root
        if let stack = parent as? UIStackView {
            // insertIndex counts arranged subviews 1:1 (collapsed text nodes never
            // materialize a view, and they alias onto non-container parents, so
            // they can't skew a stack's indices — same invariant as Android).
            // -1 = append (explicit; 0 is a valid front index).
            if insertIndex >= 0 && Int(insertIndex) <= stack.arrangedSubviews.count {
                stack.insertArrangedSubview(view, at: Int(insertIndex))
            } else {
                stack.addArrangedSubview(view)
            }
        } else {
            // The top-level form lands in the plain `root` container — pin its
            // edges so it lays out (the twin of adding to widget_root).
            view.translatesAutoresizingMaskIntoConstraints = false
            parent.addSubview(view)
            NSLayoutConstraint.activate([
                view.topAnchor.constraint(equalTo: parent.safeAreaLayoutGuide.topAnchor),
                view.leadingAnchor.constraint(equalTo: parent.leadingAnchor),
                view.trailingAnchor.constraint(equalTo: parent.trailingAnchor),
            ])
        }
    }

    private func makeView(nodeType: String) -> UIView {
        switch nodeType {
        case "view":
            let stack = UIStackView()
            stack.axis = .vertical
            stack.alignment = .fill
            return stack
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
            // consistent; BnDemo uses none of these. Phase 5.3+ wires them.
            NSLog("[BnWidgetMapper] nodeType '\(nodeType)' stubbed as a placeholder UIView (Phase 5.3+)")
            return UIView()
        default:
            NSLog("[BnWidgetMapper] Unknown nodeType '\(nodeType)' — falling back to UILabel")
            return UILabel()
        }
    }

    /// A UIStackView is the only container; UILabel/UIButton/UITextField are the
    /// text-bearing collapse targets (twin of `is TextView && !is ViewGroup`).
    private func isTextBearingNonContainer(_ view: UIView) -> Bool {
        return view is UILabel || view is UIButton || view is UITextField
    }

    // ── ReplaceText: route through the collapsed parent's title/text ──────────

    private func handleReplaceText(nodeId: Int32, text: String) {
        guard let view = nodes[nodeId] else { return }
        if let label = view as? UILabel {
            label.text = text
        } else if let button = view as? UIButton {
            button.setTitle(text, for: .normal)
        } else if let field = view as? UITextField {
            field.text = text
        }
    }

    private func handleRemove(nodeId: Int32) {
        guard let v = nodes.removeValue(forKey: nodeId) else { return }
        // Purge every registry entry aliasing the SAME view (identity, not key —
        // the collapse can map several ids to one view).
        for (key, value) in nodes where value === v {
            nodes.removeValue(forKey: key)
        }
        if let stack = v.superview as? UIStackView {
            stack.removeArrangedSubview(v)
        }
        v.removeFromSuperview()
    }

    // ── UpdateProp: value / placeholder (twin of WidgetMapper ~332-367) ───────

    private func handleUpdateProp(nodeId: Int32, name: String, value: String?) {
        guard let view = nodes[nodeId] else {
            NSLog("[BnWidgetMapper] UpdateProp for unknown nodeId \(nodeId): ignored")
            return
        }
        switch name {
        case "placeholder":
            if let field = view as? UITextField {
                field.placeholder = value
            } else {
                NSLog("[BnWidgetMapper] UpdateProp placeholder ignored: node \(nodeId) is not a UITextField")
            }
        case "value":
            if let field = view as? UITextField {
                let newValue = value ?? ""
                if field.text != newValue { field.text = newValue }
            } else {
                NSLog("[BnWidgetMapper] UpdateProp value ignored: node \(nodeId) is not a UITextField")
            }
        case "enabled":
            if let control = view as? UIControl {
                control.isEnabled = (value as NSString?)?.boolValue ?? true
            }
        default:
            NSLog("[BnWidgetMapper] UpdateProp '\(name)' not yet supported (Phase 5.3+ extends)")
        }
    }

    // ── SetStyle: backgroundColor / fontSize / padding (twin ~369-403) ────────

    private func handleSetStyle(nodeId: Int32, property: String, value: String?) {
        guard let view = nodes[nodeId] else {
            NSLog("[BnWidgetMapper] SetStyle for unknown nodeId \(nodeId): ignored")
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
        case "padding":
            guard let pad = parseCGFloat(value) else {
                NSLog("[BnWidgetMapper] SetStyle padding ignored: \(value ?? "nil")")
                return
            }
            if let stack = view as? UIStackView {
                stack.isLayoutMarginsRelativeArrangement = true
                stack.layoutMargins = UIEdgeInsets(top: pad, left: pad, bottom: pad, right: pad)
            } else {
                view.layoutMargins = UIEdgeInsets(top: pad, left: pad, bottom: pad, right: pad)
            }
        default:
            NSLog("[BnWidgetMapper] SetStyle '\(property)' not yet supported (Phase 5.3+ extends)")
        }
    }

    /// Twin of Kotlin parseFloatOrNull — strips sp/dp/px suffixes.
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
