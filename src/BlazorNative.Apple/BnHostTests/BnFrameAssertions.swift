// ─────────────────────────────────────────────────────────────────────────────
// BnFrameAssertions — Phase 6.1 Gate 3: the XCTest suite's shared FRAME vocabulary.
// The iOS mirror of the instrumented suite's `FrameAssertions.kt`, sentence for
// sentence, because the two suites assert THE SAME NUMBERS (M6 DoD #2) and a
// vocabulary that drifts is a parity claim that drifts.
//
// **Frames are asserted in points**, which on iOS ARE the density-independent units
// Yoga computes in (Android multiplies by `density` at frame-apply time — the one
// conversion site). That 1:1 mapping is exactly why the two shells can assert
// identical numbers.
//
// The 0.5pt tolerance is carried over from Android's dp contract deliberately: it
// is the tolerance the SHARED table is stated to, and iOS — which does no rounding
// at all (the shared YGConfig has Yoga's pixel-grid rounding off, and the shell
// invents none) — comfortably lands inside it. Same table, same tolerance, no iOS
// dialect.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit
@testable import BnHost

// ── DETERMINISTIC TEARDOWN — the contract, honoured by the suite too ─────────

/// Every [BnWidgetMapper] this target builds, so [BnHostTestCase] can `destroy()` them
/// **deterministically, on the main thread, at the end of the test that made them**.
///
/// `BnWidgetMapper.destroy()`'s own doc comment says why it exists: `deinit` runs on
/// whatever thread drops the LAST REFERENCE, and the mapper's Yoga tree and the `.mm`'s
/// unsynchronised measure registry are **main-thread-only**. Every production owner calls
/// `destroy()` explicitly (`HostViewController.deinit`) — and until now the ONE place that
/// did not was the test suite, which left the tree to `deinit` and to whichever thread
/// released the last reference (a `BnRuntime` frame callback can hold one). Harmless under
/// XCTest's main-thread teardown, and exactly the wrong place to leave the contract
/// unhonoured: the suite is the executable statement of it.
///
/// Registration is at CONSTRUCTION ([bnMapper]) rather than at use, so a mapper cannot be
/// built and forgotten.
enum BnTestMappers {
    private static var live: [BnWidgetMapper] = []

    static func track(_ mapper: BnWidgetMapper) -> BnWidgetMapper {
        live.append(mapper)
        return mapper
    }

    /// Idempotent, and `destroy()` is too — a suite may destroy its own mapper first.
    static func destroyAll() {
        let doomed = live
        live.removeAll()
        for mapper in doomed { mapper.destroy() }
    }
}

/// A mapper that will be torn down deterministically. The only way this target should
/// build one.
func bnMapper(root: UIView) -> BnWidgetMapper {
    BnTestMappers.track(BnWidgetMapper(root: root))
}

/// The base class for every suite that builds a mapper (directly or through
/// [BnSyntheticHost]) — it destroys them in `tearDown`, on the main thread, one test at a
/// time. Contributes no tests of its own.
class BnHostTestCase: XCTestCase {
    override func tearDown() {
        BnTestMappers.destroyAll()
        super.tearDown()
    }
}

// ── Patch builders (the RenderPatch/`create`/`style`/`text` twins) ───────────

func bnCreate(_ nodeId: Int32, _ nodeType: String, _ parentId: Int32?, insertIndex: Int32 = -1) -> BnPatch {
    .createNode(nodeId: nodeId, nodeType: nodeType, parentId: parentId, insertIndex: insertIndex)
}

func bnStyle(_ nodeId: Int32, _ property: String, _ value: String?) -> BnPatch {
    .setStyle(nodeId: nodeId, property: property, value: value)
}

func bnText(_ nodeId: Int32, _ text: String) -> BnPatch {
    .replaceText(nodeId: nodeId, text: text)
}

// ── The scroll view and its SYNTHETIC content view (Phase 6.2) ───────────────

/// A `scroll` node's view, unwrapped — the VIEWPORT.
func bnScrollView(_ view: UIView,
                  file: StaticString = #filePath, line: UInt = #line) throws -> UIScrollView {
    try XCTUnwrap(view as? UIScrollView,
                  "a `scroll` node's view must be a UIScrollView (got \(type(of: view)))",
                  file: file, line: line)
}

/// The SYNTHETIC content view inside a viewport — the single meaningful child of a
/// `UIScrollView`, holding every one of the scroll node's wire children.
///
/// Found by TYPE, never by index, and that is not fussiness: **`UIScrollView` keeps its
/// own scroll-indicator subviews**, so `scroll.subviews[0]` is a coin flip and
/// `subviews.count == 1` is simply false. Android can say "the ScrollView's ONLY child
/// is the content view" because a `ScrollView` throws on a second; iOS cannot, and a
/// test that assumed it would be pinning UIKit's private view hierarchy instead of this
/// shell's contract.
func bnContentView(of scroll: UIScrollView,
                   file: StaticString = #filePath, line: UInt = #line) throws -> UIView {
    let content = scroll.subviews.compactMap { $0 as? BnScrollContentView }
    XCTAssertEqual(content.count, 1,
                   "a viewport has exactly ONE synthetic content view — the shell creates it with "
                   + "the UIScrollView and purges it with it (got \(content.count))",
                   file: file, line: line)
    return try XCTUnwrap(content.first, "no content view under the viewport", file: file, line: line)
}

// ── Frame assertions ─────────────────────────────────────────────────────────

/// Asserts a UIView's computed frame, in points, RELATIVE TO ITS PARENT.
func assertFrame(_ what: String, _ view: UIView,
                 _ x: CGFloat, _ y: CGFloat, _ width: CGFloat, _ height: CGFloat,
                 file: StaticString = #filePath, line: UInt = #line) {
    XCTAssertEqual(view.frame.minX, x, accuracy: 0.5, "\(what).x", file: file, line: line)
    XCTAssertEqual(view.frame.minY, y, accuracy: 0.5, "\(what).y", file: file, line: line)
    XCTAssertEqual(view.frame.width, width, accuracy: 0.5, "\(what).w", file: file, line: line)
    XCTAssertEqual(view.frame.height, height, accuracy: 0.5, "\(what).h", file: file, line: line)
}

/// The frame form of "this container is a vertical stack": every child shares the
/// container's CONTENT-BOX left edge and each is butted up against the previous
/// one's bottom edge.
///
/// The content-box edge is read from child [0] rather than pinned to 0, because a
/// container with `padding` insets its children — and after Phase 6.1 that inset is
/// the Yoga node's (the UIView's own bounds are NOT inset), so the padding lives in
/// the children's frames.
///
/// This is the pin that replaced `as? UIStackView` + `axis == .vertical`: an
/// UN-STYLED tree must still stack, because Yoga's default flexDirection is column.
///
/// PER-CHILD heights are NOT asserted non-zero here (Android's twin does): an empty
/// `UILabel` answers `sizeThatFits` with height 0 where an empty `TextView` answers
/// one line height, and BnDemo mounts with an EMPTY echo label. That is a real (and
/// honest) platform difference in what a widget measures to — it is not a frame-table
/// number, and inventing a minimum here to paper over it would be exactly the kind
/// of "helpful" correction the engine must not make.
///
/// **But the container must have CONSUMED vertical space, and that is asserted.**
/// Without it this helper VACUOUSLY PASSES on a tree that was never laid out: with
/// every frame `.zero`, `contentLeft` is 0, `expectedTop` starts at 0, every child's
/// `minX == 0` ✓ and `minY == 0 == expectedTop` ✓, and `expectedTop = maxY = 0` for
/// the next one. Every assertion above holds and NOTHING was laid out. The type-pin
/// this helper replaced (`as? UIStackView` + `axis == .vertical`) could not pass
/// vacuously, so without this line the replacement is a strict WEAKENING — and
/// `BnInteractionTests.testSettingsNavigationShowsSettingsNoTextField` would be
/// pinning the settings page's layout with an assertion that holds on a completely
/// un-laid-out tree.
func assertStacksVertically(_ container: UIView,
                            file: StaticString = #filePath, line: UInt = #line) {
    guard let first = container.subviews.first else {
        XCTFail("a stacking container must have children", file: file, line: line)
        return
    }
    XCTAssertTrue(container.subviews.contains { $0.frame.height > 0 },
                  "the stack must have CONSUMED vertical space — every frame at zero "
                  + "means the layout pass never ran", file: file, line: line)
    let contentLeft = first.frame.minX
    var expectedTop = first.frame.minY
    for (i, child) in container.subviews.enumerated() {
        XCTAssertEqual(child.frame.minX, contentLeft, accuracy: 0.5,
                       "child \(i) must share the container's content-box left edge",
                       file: file, line: line)
        XCTAssertEqual(child.frame.minY, expectedTop, accuracy: 0.5,
                       "child \(i) must start exactly where child \(i - 1) ended "
                       + "— an un-styled tree is a Yoga COLUMN", file: file, line: line)
        expectedTop = child.frame.maxY
    }
}

/// **THE MEASURE ORACLE** — the assertion a FABRICATED measure function cannot pass.
///
/// Every *relational* assertion about a measured leaf (`height > 0`, "the row hugs
/// the label", "the label fits the row") also passes when the measure function
/// returns a CONSTANT — the 6.0 spike's 80×20 stub satisfies all of them. They pin
/// the plumbing; none of them pins the MEASUREMENT.
///
/// This does. It builds a THROWAWAY widget of the same class, with the same text and
/// font, and asks it the SAME question `BnWidgetMapper`'s measure trampoline asks the
/// real one — `sizeThatFits(available, .greatestFiniteMagnitude)`, which is exactly
/// what Yoga hands a leaf in a row of known width and unconstrained height — then
/// demands the LAID-OUT frame equal the answer (with the at-most clamp the trampoline
/// applies to the main axis). No font metric is written down anywhere, so it stays
/// honest on any simulator and any font; but the measurement can no longer be invented.
///
/// The 1pt tolerance mirrors Android's 1px one. A fabricated measurement misses by tens.
func assertOracle(_ what: String, _ view: UIView, availableWidth: CGFloat,
                  file: StaticString = #filePath, line: UInt = #line) {
    let oracle: UIView
    if let label = view as? UILabel {
        let l = UILabel()
        l.numberOfLines = label.numberOfLines
        l.font = label.font
        l.text = label.text
        oracle = l
    } else if let button = view as? UIButton {
        let b = UIButton(type: .system)
        b.titleLabel?.font = button.titleLabel?.font
        b.setTitle(button.title(for: .normal), for: .normal)
        oracle = b
    } else if let field = view as? UITextField {
        let f = UITextField()
        f.borderStyle = field.borderStyle
        f.font = field.font
        f.text = field.text
        f.placeholder = field.placeholder
        oracle = f
    } else {
        XCTFail("\(what): the oracle only knows how to re-measure the measured NODETYPES",
                file: file, line: line)
        return
    }

    let fit = oracle.sizeThatFits(CGSize(width: availableWidth, height: .greatestFiniteMagnitude))
    let why = "— a measure func that returned a CONSTANT would pass every relational "
        + "assertion in this file and fail THIS one"

    XCTAssertEqual(view.frame.width, min(fit.width, availableWidth), accuracy: 1,
                   "\(what).w must equal what the native widget MEASURES to \(why)",
                   file: file, line: line)
    XCTAssertEqual(view.frame.height, fit.height, accuracy: 1,
                   "\(what).h must equal what the native widget MEASURES to \(why)",
                   file: file, line: line)
}

// ── The synthetic host ───────────────────────────────────────────────────────

/// A detached host root + its [BnWidgetMapper], driven ONE FRAME AT A TIME — the
/// twin of Kotlin's `SyntheticHost`.
///
/// The root is given real bounds up front: a detached UIView has none, and Yoga's
/// available space is the host's.
///
/// [render] returns only after the batch has been applied on the main queue, so the
/// tree may be READ between frames — which is what the dirty-on-content-change tests
/// need (frame 1's height, then frame 2's).
final class BnSyntheticHost {

    let root: UIView
    let mapper: BnWidgetMapper
    private var frameId: Int32 = 0

    init(width: CGFloat = 400, height: CGFloat = 800) {
        root = UIView(frame: CGRect(x: 0, y: 0, width: width, height: height))
        mapper = bnMapper(root: root) // …and it is destroyed in BnHostTestCase.tearDown
    }

    /// Applies one frame (the CommitFrame is appended) and pumps the main runloop
    /// until it has landed. The mapper hops its batch to `DispatchQueue.main.async`,
    /// and the main queue is FIFO — so a marker enqueued right after it fires only
    /// once the batch has been applied. No sleeps, no polling for a shape.
    func render(_ patches: [BnPatch], file: StaticString = #filePath, line: UInt = #line) {
        frameId += 1
        mapper.apply(BnFrame(
            frameId: frameId,
            timestampMs: 0,
            patches: patches + [.commitFrame(frameId: frameId, timestampMs: 0)]))

        var applied = false
        DispatchQueue.main.async { applied = true }
        let deadline = Date().addingTimeInterval(10)
        while !applied && Date() < deadline {
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.01))
        }
        XCTAssertTrue(applied, "the render batch never reached the main queue", file: file, line: line)
    }

    /// Re-lays the host root — the twin of a resize; the mapper re-solves against
    /// the new bounds (`HostViewController.viewDidLayoutSubviews` does this in
    /// production).
    func resize(width: CGFloat, height: CGFloat) {
        root.frame = CGRect(x: 0, y: 0, width: width, height: height)
        mapper.calculateAndApply()
    }
}

/// Drives a fresh [BnSyntheticHost] with one frame per patch list and returns the
/// host root — the shape almost every synthetic-frame test wants. The host is
/// returned alongside so it (and its mapper, which owns the Yoga tree) outlives the
/// assertions.
func bnRender(_ frames: [BnPatch]...,
              file: StaticString = #filePath, line: UInt = #line) -> BnSyntheticHost {
    let host = BnSyntheticHost()
    for frame in frames {
        host.render(frame, file: file, line: line)
    }
    XCTAssertFalse(host.root.subviews.isEmpty, "no child created in root after apply",
                   file: file, line: line)
    return host
}
