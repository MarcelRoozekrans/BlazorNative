// ─────────────────────────────────────────────────────────────────────────────
// BnRenderTests — Phase 5.2 (M5 DoD #2): the render proof. The iOS third of the
// BnDemo shape assertions, alongside BnDemoTest.kt (JVM, through the dll) and
// BnDemoAndroidTest.kt (on-device UIView tree). NOT XCUITest — a HOSTED XCTest so
// it can read the exact UIView tree + colors, the analog of the Android
// ActivityScenario/WidgetMapper structural test.
//
// ── PHASE 6.1: THE CHURN, AND WHY IT IS THE STRONGER TEST ────────────────────
//
// This file used to pin the form BY TYPE — `root.subviews.first as? UIStackView`,
// then index `arrangedSubviews`. Yoga owns placement now and a `view` node is a
// plain UIView, so the helpers broke, not just the assertions. They are rewritten to
// walk `subviews` and to assert FRAMES.
//
// **BnDemo is an UN-STYLED tree, and it must still render as a vertical stack** —
// that is Yoga's default `flexDirection: column`, not a UIStackView. It is the
// strongest regression signal in the suite that this phase changed the ENGINE and
// not the BEHAVIOUR: no golden moved, no patch changed, and the form still stacks.
// [assertStacksVertically] is the frame form of that sentence.
//
// The mounted shape (unchanged in meaning):
//
//   root: the host UIView
//     └── form UIView (#FFEEAA, Yoga padding 16), 6 subviews, stacked:
//           [0] UILabel  "BnDemo"          (title, fontSize 24, text-collapsed)
//           [1] UITextField                 (bound input; placeholder "Type here...")
//           [2] UIView echo panel (#FFEEAA)
//                 └── UILabel               (the live echo, "" on mount)
//           [3] UIButton "Clear"
//           [4] UIButton "Theme"
//           [5] UIButton "Settings →"
//
// The echo panel's create carries the MID-LIST insertIndex 2 (Blazor's FIFO queue
// creates it after the buttons) — the shape only lands right if the mapper honors it
// in BOTH trees (view and Yoga), which is exactly what child-order [2] asserts.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit
@testable import BnHost

final class BnRenderTests: BnHostTestCase {

    private static let defaultBackground = "#FFEEAA"

    // Hold the runtime for the test's lifetime so the @convention(c) callback
    // trampoline is never released mid-render.
    private var runtime: BnRuntime?

    func testBnDemoRendersCanonicalTree() throws {
        let root = UIView(frame: CGRect(x: 0, y: 0, width: 390, height: 844))

        let mapper = bnMapper(root: root)
        let runtime = BnRuntime(mapper: mapper)
        self.runtime = runtime

        var decodeError: Error?
        runtime.onError = { msg, err in
            decodeError = err
            NSLog("[BnRenderTests] \(msg): \(err)")
        }

        // Boot on the main thread: the sync mount frame is decoded here and the
        // mapper enqueues the batch to the main queue, drained by the poll below.
        try runtime.start(component: "BnDemo", os: "ios")

        let form = try pollForForm(in: root, deadline: 30)
        XCTAssertNil(decodeError, "a frame was dropped during mount")

        // The form is the scroll viewport's content's single child (#204 wrapped the
        // page in a BnScroll; pollForForm walks root -> UIScrollView -> content -> form).
        XCTAssertEqual(root.subviews.count, 1, "root must have exactly the scroll viewport")

        // The canonical SIX come first — title, input, echo panel, Clear, Theme,
        // "Settings →" — and are pinned by index below. #204 appends the capability
        // menu after them, so this is no longer an equality.
        //
        // The old `== 6` was doing two jobs: "the six are there" AND "nothing else
        // is". The first is what the indexed assertions below already prove. The
        // second is kept explicitly by the tail check — the menu heading at [6] and a
        // LAST subview that is the final menu button. A mapper that dropped the tail
        // of a long child list would still satisfy every [0]…[5] pin above, which is
        // exactly the failure this replacement is aimed at.
        XCTAssertGreaterThan(form.subviews.count, 6,
                             "form must carry the canonical six plus the #204 capability menu")

        // ── THE ENGINE CHANGED; THE BEHAVIOUR DID NOT ────────────────────────
        // An un-styled tree still stacks, because Yoga's default flexDirection is
        // column. This replaces the old `as? UIStackView` + `axis == .vertical` pin.
        assertStacksVertically(form)

        // The form is a top-level node under the Yoga host root: `alignItems: stretch`
        // gives it the host's width, and it hugs its content vertically (no height).
        XCTAssertEqual(form.frame.minX, 0, accuracy: 0.5)
        XCTAssertEqual(form.frame.minY, 0, accuracy: 0.5)
        XCTAssertEqual(form.frame.width, root.bounds.width, accuracy: 0.5,
                       "the form stretches to the host's width")

        // `padding: 16` is LAYOUT — it belongs to the Yoga node, which insets the
        // form's CHILDREN. The UIView's own bounds are NOT inset (the pre-6.1 shell
        // set layoutMargins here; a surviving view-level inset would double-apply it).
        XCTAssertEqual(form.subviews[0].frame.minX, 16, accuracy: 0.5,
                       "Yoga's padding insets the CHILDREN by 16")
        XCTAssertEqual(form.subviews[0].frame.minY, 16, accuracy: 0.5)

        // [0] title UILabel "BnDemo", fontSize 24 (text-collapsed span).
        let title = try XCTUnwrap(form.subviews[0] as? UILabel, "[0] must be the title UILabel")
        XCTAssertEqual(title.text, "BnDemo")
        XCTAssertEqual(title.font.pointSize, 24, accuracy: 0.01, "title fontSize must be 24")
        // Font parity Gate B (#126) — THE RESOLVED-FAMILY GUARD. Gate A proved Inter is
        // REGISTERED; this proves the mapper actually FORCED it onto a rendered text leaf
        // (here via the `fontSize` SetStyle arm — the title carries fontSize 24). A silent
        // `?? .systemFont` fallback would render green in Gate A but redden HERE, on the
        // family iOS actually resolved.
        XCTAssertEqual(title.font.familyName, "Inter",
                       "the styled title must render in the bundled Inter family, not the system font")
        XCTAssertEqual(title.font.fontName, "Inter-Regular",
                       "the styled title's font must be the bundled Inter-Regular PostScript face")

        // [1] the bound UITextField with placeholder.
        let input = try XCTUnwrap(form.subviews[1] as? UITextField, "[1] must be the bound UITextField")
        XCTAssertEqual(input.placeholder, "Type here...")

        // [2] the echo panel (mid-list insertIndex 2) with one empty label.
        let echo = try XCTUnwrap(form.subviews[2] as? UIView, "[2] must be the echo panel")
        XCTAssertEqual(echo.subviews.count, 1, "echo panel must have exactly the echo label")
        let echoLabel = try XCTUnwrap(echo.subviews[0] as? UILabel, "echo child must be a UILabel")
        XCTAssertEqual(echoLabel.text, "", "the echo must be empty on mount")
        // Font parity Gate B (#126) — the CREATION-default path (this label carries no
        // `fontSize` style, so it keeps the font set at `UILabel` creation). Proves the
        // default text leaf renders in Inter at the preserved system label point size.
        XCTAssertEqual(echoLabel.font.familyName, "Inter",
                       "an unstyled text leaf must default to the bundled Inter family")
        XCTAssertEqual(echoLabel.font.pointSize, UIFont.labelFontSize, accuracy: 0.01,
                       "the default label point size (17pt) must be preserved — only the family changed")

        // backgroundColor #FFEEAA on BOTH themed containers (DoD #6 surface) — VISUAL
        // names still route to the UIView; only LAYOUT names go to Yoga.
        assertColor(form.backgroundColor, equals: Self.defaultBackground, "form background")
        assertColor(echo.backgroundColor, equals: Self.defaultBackground, "echo panel background")

        // [3][4][5] the three buttons, by title (text-collapsed onto the UIButton), each
        // sized by its own MEASURED intrinsic size (a `button` is a measured nodetype).
        let clear = try XCTUnwrap(form.subviews[3] as? UIButton, "[3] must be the Clear UIButton")
        XCTAssertEqual(clear.title(for: .normal), "Clear")
        XCTAssertGreaterThan(clear.frame.height, 0, "the button is sized by native measurement")
        let theme = try XCTUnwrap(form.subviews[4] as? UIButton, "[4] must be the Theme UIButton")
        XCTAssertEqual(theme.title(for: .normal), "Theme")
        let settings = try XCTUnwrap(form.subviews[5] as? UIButton, "[5] must be the Settings UIButton")
        XCTAssertEqual(settings.title(for: .normal), "Settings →")

        // ── THE #204 CAPABILITY MENU: HEAD AND TAIL ──────────────────────────
        // [6] is the "Explore" heading, and the LAST child is the final menu button.
        // Checking the tail is the load-bearing half: every assertion above reads
        // the FIRST six subviews, so a mapper that truncated a long child list would
        // pass all of them. "Camera" is BnDemo.Destinations' last row — if that list
        // is reordered this fails loudly rather than silently stopping to mean
        // anything (RouteMenuDriftTests owns the list's contents; this owns the
        // claim that the whole list reached the screen).
        let exploreHeading = try XCTUnwrap(form.subviews[6] as? UILabel,
                                           "[6] must be the Explore heading")
        XCTAssertEqual(exploreHeading.text, "Explore")

        let lastMenuButton = try XCTUnwrap(form.subviews.last as? UIButton,
                                           "the form's last child must be the final menu button")
        XCTAssertEqual(lastMenuButton.title(for: .normal), "Camera",
                       "the menu's LAST row must have reached the view tree — a truncated "
                       + "child list would satisfy every index pin above")
    }

    // ── Helpers (mirroring BnDemoAndroidTest's pollForForm/backgroundColorOf) ──

    struct RenderTimeout: Error, CustomStringConvertible {
        let seconds: Int
        var description: String {
            "BnDemo never rendered its 6-child form within \(seconds)s — boot/mapper failed"
        }
    }

    /// Polls the MAIN runloop (draining the mapper's DispatchQueue.main.async batch)
    /// until the form with all 6 children appears AND has been laid out, or the
    /// deadline. A timeout THROWS (fails the test) — it must never skip.
    private func pollForForm(in root: UIView, deadline seconds: TimeInterval) throws -> UIView {
        let end = Date().addingTimeInterval(seconds)
        while Date() < end {
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.05))
            // #204: root → UIScrollView → synthetic content view → form (see
            // BnInteractionTests.demoForm for why the walk goes THROUGH the
            // content node rather than around it).
            if let scroll = root.subviews.first as? UIScrollView,
               let content = scroll.subviews.first,
               let form = content.subviews.first,
               form.subviews.count >= 6, form.frame.height > 0 {
                return form
            }
        }
        XCTFail("BnDemo never rendered its 6-child form within \(Int(seconds))s — boot/mapper failed")
        throw RenderTimeout(seconds: Int(seconds))
    }

    /// Asserts a UIColor equals a #RRGGBB spec by RGBA components (twin of
    /// Android's ColorDrawable.color comparison).
    private func assertColor(_ color: UIColor?,
                             equals hex: String,
                             _ label: String,
                             file: StaticString = #filePath,
                             line: UInt = #line) {
        guard let color = color else {
            XCTFail("\(label): expected \(hex) but backgroundColor was nil", file: file, line: line)
            return
        }
        guard let expected = BnColor.parse(hex) else {
            XCTFail("\(label): could not parse expected color \(hex)", file: file, line: line)
            return
        }
        var r1: CGFloat = 0, g1: CGFloat = 0, b1: CGFloat = 0, a1: CGFloat = 0
        var r2: CGFloat = 0, g2: CGFloat = 0, b2: CGFloat = 0, a2: CGFloat = 0
        color.getRed(&r1, green: &g1, blue: &b1, alpha: &a1)
        expected.getRed(&r2, green: &g2, blue: &b2, alpha: &a2)
        let tol: CGFloat = 0.004 // ~1/255
        XCTAssertEqual(r1, r2, accuracy: tol, "\(label): red", file: file, line: line)
        XCTAssertEqual(g1, g2, accuracy: tol, "\(label): green", file: file, line: line)
        XCTAssertEqual(b1, b2, accuracy: tol, "\(label): blue", file: file, line: line)
        XCTAssertEqual(a1, a2, accuracy: tol, "\(label): alpha", file: file, line: line)
    }
}
