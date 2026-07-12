// ─────────────────────────────────────────────────────────────────────────────
// BnRenderTests — Phase 5.2 (M5 DoD #2): the render proof. The iOS third of the
// BnDemo shape assertions, alongside BnDemoTest.kt (JVM, through the dll) and
// BnDemoAndroidTest.kt (on-device UIView tree). NOT XCUITest — a HOSTED XCTest so
// it can read the exact UIView tree + colors, the analog of the Android
// ActivityScenario/WidgetMapper structural test.
//
// Boot BnDemo through the linked NativeAOT static archive → the mount frame fires
// synchronously and the mapper hops its batch to the main queue → poll the main
// runloop until the form appears → assert the structural pins mirroring
// BnDemoAndroidTest:
//
//   root: the host UIView
//     └── form UIStackView (#FFEEAA, layoutMargins 16), 6 arranged subviews:
//           [0] UILabel  "BnDemo"          (title, fontSize 24, text-collapsed)
//           [1] UITextField                 (bound input; placeholder "Type here...")
//           [2] UIStackView echo panel (#FFEEAA)
//                 └── UILabel               (the live echo, "" on mount)
//           [3] UIButton "Clear"
//           [4] UIButton "Theme"
//           [5] UIButton "Settings →"
//
// The echo panel's create carries the MID-LIST insertIndex 2 (Blazor's FIFO queue
// creates it after the buttons) — the shape only lands right if the mapper honors
// it, which is exactly what child-order [2] asserts.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit
@testable import BnHost

final class BnRenderTests: XCTestCase {

    private static let defaultBackground = "#FFEEAA"

    // Hold the runtime for the test's lifetime so the @convention(c) callback
    // trampoline is never released mid-render.
    private var runtime: BnRuntime?

    func testBnDemoRendersCanonicalTree() throws {
        let root = UIView(frame: CGRect(x: 0, y: 0, width: 390, height: 844))

        let mapper = BnWidgetMapper(root: root)
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

        // The form is the host view's single child.
        XCTAssertEqual(root.subviews.count, 1, "root must have exactly the form stack")

        // 6 arranged subviews, in order.
        XCTAssertEqual(form.arrangedSubviews.count, 6, "form must have exactly 6 arranged subviews")

        // [0] title UILabel "BnDemo", fontSize 24 (text-collapsed span).
        let title = try XCTUnwrap(form.arrangedSubviews[0] as? UILabel, "[0] must be the title UILabel")
        XCTAssertEqual(title.text, "BnDemo")
        XCTAssertEqual(title.font.pointSize, 24, accuracy: 0.01, "title fontSize must be 24")

        // [1] the bound UITextField with placeholder.
        let input = try XCTUnwrap(form.arrangedSubviews[1] as? UITextField, "[1] must be the bound UITextField")
        XCTAssertEqual(input.placeholder, "Type here...")

        // [2] the echo panel stack (mid-list insertIndex 2) with one empty label.
        let echo = try XCTUnwrap(form.arrangedSubviews[2] as? UIStackView, "[2] must be the echo panel UIStackView")
        XCTAssertEqual(echo.arrangedSubviews.count, 1, "echo panel must have exactly the echo label")
        let echoLabel = try XCTUnwrap(echo.arrangedSubviews[0] as? UILabel, "echo child must be a UILabel")
        XCTAssertEqual(echoLabel.text, "", "the echo must be empty on mount")

        // backgroundColor #FFEEAA on BOTH themed containers (DoD #6 surface).
        assertColor(form.backgroundColor, equals: Self.defaultBackground, "form background")
        assertColor(echo.backgroundColor, equals: Self.defaultBackground, "echo panel background")

        // [3][4][5] the three buttons, by title (text-collapsed onto the UIButton).
        let clear = try XCTUnwrap(form.arrangedSubviews[3] as? UIButton, "[3] must be the Clear UIButton")
        XCTAssertEqual(clear.title(for: .normal), "Clear")
        let theme = try XCTUnwrap(form.arrangedSubviews[4] as? UIButton, "[4] must be the Theme UIButton")
        XCTAssertEqual(theme.title(for: .normal), "Theme")
        let settings = try XCTUnwrap(form.arrangedSubviews[5] as? UIButton, "[5] must be the Settings UIButton")
        XCTAssertEqual(settings.title(for: .normal), "Settings →")
    }

    // ── Helpers (mirroring BnDemoAndroidTest's pollForForm/backgroundColorOf) ──

    struct RenderTimeout: Error, CustomStringConvertible {
        let seconds: Int
        var description: String {
            "BnDemo never rendered its 6-child form within \(seconds)s — boot/mapper failed"
        }
    }

    /// Polls the MAIN runloop (draining the mapper's DispatchQueue.main.async
    /// batch) until the form stack with all 6 children appears, or the deadline.
    /// A timeout THROWS (fails the test) — it must never skip.
    private func pollForForm(in root: UIView, deadline seconds: TimeInterval) throws -> UIStackView {
        let end = Date().addingTimeInterval(seconds)
        while Date() < end {
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.05))
            if let form = root.subviews.first as? UIStackView, form.arrangedSubviews.count >= 6 {
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
