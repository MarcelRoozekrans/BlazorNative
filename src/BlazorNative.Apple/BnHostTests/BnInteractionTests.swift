// ─────────────────────────────────────────────────────────────────────────────
// BnInteractionTests — Phase 5.3 (M5 DoD #3): the interactive two-page proof. The
// iOS twin of BnDemoAndroidTest (bind/clear/theme) + NavigationAndroidTest
// (Settings⇄Back). A HOSTED XCTest (NOT XCUITest) so it reads the exact UIView
// tree + colors, driving controls via `sendActions` (the performClick analog —
// UIKit needs the explicit action for .editingChanged) and polling the tree
// (dispatch is async off the BlazorNative-Dispatch lane).
//
// The v3.0 bar on the simulator: type→echo (input not clobbered), Clear, cascading
// theme (both directions), Settings→ (settings shown, no textfield), ←Back (fresh
// empty remount — state does not survive the swap).
//
// ── PHASE 6.1: THE CHURN ─────────────────────────────────────────────────────
// The five assertions above are UNCHANGED IN MEANING. What broke is the tree
// ACCESSORS: they cast the form to `UIStackView` and indexed `arrangedSubviews`.
// Yoga owns placement now and a `view` node is a plain UIView, so they walk
// `subviews` instead. The interaction lane — attach, dispatch, re-render, patch
// apply — never went through the stack view; this is a pure accessor rewrite, and
// that is precisely what makes these five tests the regression signal that the
// ENGINE changed and the BEHAVIOUR did not.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit
@testable import BnHost

final class BnInteractionTests: BnHostTestCase {

    private static let defaultBg = "#FFEEAA"
    private static let altBg = "#334455"

    /// Held for the test's lifetime so the callback + lane outlive the dispatch.
    private var runtime: BnRuntime?
    private var root: UIView!

    override func setUpWithError() throws {
        root = UIView(frame: CGRect(x: 0, y: 0, width: 390, height: 844))
        let mapper = bnMapper(root: root)
        let runtime = BnRuntime(mapper: mapper)
        runtime.onError = { msg, err in NSLog("[BnInteractionTests] \(msg): \(err)") }
        self.runtime = runtime
        try runtime.start(component: "BnDemo", os: "ios")
        XCTAssertTrue(pollUntil { self.demoForm() != nil }, "BnDemo never rendered its 6-child form")
    }

    // ── bind + echo: type "héllo→世界" → echo == typed, input not clobbered ────

    func testBindLoopTypeEchoesAndInputNotClobbered() throws {
        let input = try XCTUnwrap(demoInput(), "the bound input")
        let typed = "héllo→世界" // the UTF-8/IME leg
        input.text = typed
        input.sendActions(for: .editingChanged)

        XCTAssertTrue(pollUntil { self.demoEchoLabel()?.text == typed },
                      "echo never showed '\(typed)' after typing")
        // The echo frame's value write-back must NOT clobber the input (the
        // inequality check skips the redundant set; UIKit fires no re-entrant
        // .editingChanged on a programmatic set — the iOS simplification).
        XCTAssertEqual(demoInput()?.text, typed, "the input keeps the user's text")
    }

    // ── Clear: both halves reset ─────────────────────────────────────────────

    func testClearResetsInputAndEcho() throws {
        let input = try XCTUnwrap(demoInput())
        input.text = "hello"
        input.sendActions(for: .editingChanged)
        XCTAssertTrue(pollUntil { self.demoEchoLabel()?.text == "hello" }, "echo never showed the seed")

        try tapButton("Clear")
        XCTAssertTrue(pollUntil { self.demoInput()?.text == "" && self.demoEchoLabel()?.text == "" },
                      "input + echo never both emptied after Clear")
    }

    // ── theme: cascading flip on form + echo, both directions ────────────────

    func testThemeFlipsBothBackgroundsBothWays() throws {
        // Mount: both themed containers carry the default background.
        XCTAssertTrue(colorMatches(demoForm()?.backgroundColor, Self.defaultBg), "form mounts #FFEEAA")
        XCTAssertTrue(colorMatches(demoEchoPanel()?.backgroundColor, Self.defaultBg), "echo mounts #FFEEAA")

        try tapButton("Theme")
        XCTAssertTrue(pollUntil {
            self.colorMatches(self.demoForm()?.backgroundColor, Self.altBg) &&
            self.colorMatches(self.demoEchoPanel()?.backgroundColor, Self.altBg)
        }, "both themed backgrounds never flipped to #334455")

        try tapButton("Theme")
        XCTAssertTrue(pollUntil {
            self.colorMatches(self.demoForm()?.backgroundColor, Self.defaultBg) &&
            self.colorMatches(self.demoEchoPanel()?.backgroundColor, Self.defaultBg)
        }, "both themed backgrounds never flipped back to #FFEEAA")
    }

    // ── nav Settings→: settings shown, no textfield, 2-child settings view ────

    func testSettingsNavigationShowsSettingsNoTextField() throws {
        try tapButton("Settings →")
        XCTAssertTrue(pollUntil { self.settingsView() != nil }, "settings page never appeared")

        let settings = try XCTUnwrap(settingsView(), "the settings view")
        XCTAssertEqual(settings.subviews.count, 2, "settings view has exactly title + back")
        XCTAssertEqual((settings.subviews[0] as? UILabel)?.text, "Settings")
        XCTAssertEqual((settings.subviews[1] as? UIButton)?.title(for: .normal), "← Back")
        XCTAssertFalse(containsTextField(root), "the BnDemo input must have left the screen")
        // The settings page is un-styled too — Yoga's default column still stacks it.
        assertStacksVertically(settings)
    }

    // ── nav ←Back: fresh empty BnDemo remount ────────────────────────────────

    func testBackReturnsFreshEmptyBnDemo() throws {
        // Seed state, then navigate away and back.
        let input = try XCTUnwrap(demoInput())
        input.text = "seeded"
        input.sendActions(for: .editingChanged)
        XCTAssertTrue(pollUntil { self.demoEchoLabel()?.text == "seeded" }, "seed echo never showed")

        try tapButton("Settings →")
        XCTAssertTrue(pollUntil { self.settingsView() != nil }, "settings never appeared")

        try tapButton("← Back")
        // BnDemo remounts FRESH — empty input + empty echo, the seeded text nowhere.
        XCTAssertTrue(pollUntil {
            guard let form = self.demoForm() else { return false }
            let freshInput = form.subviews[safe: 1] as? UITextField
            let freshEcho = (form.subviews[safe: 2])?.subviews.first as? UILabel
            return freshInput?.text == "" && freshEcho?.text == ""
        }, "BnDemo did not remount fresh+empty after Back")
        XCTAssertEqual(demoInput()?.text, "", "the seeded text must not survive the swap")
        XCTAssertEqual(demoEchoLabel()?.text, "", "the seeded echo must not survive the swap")
    }

    // ── Tree accessors (re-derived each call — nav/theme mutate the tree) ─────
    //
    // Phase 6.1: `subviews`, not `arrangedSubviews`; a plain UIView, not a
    // UIStackView. The demo form is still root's single child with 6 children, and
    // the settings page is still root's single child with 2 — the SHAPE is what
    // identifies them, and the shape did not change.
    //
    // The shape alone would be a weaker pin than the `as? UIStackView` cast it
    // replaced, so the type check is not dropped — it is INVERTED: a `view` node must
    // now be a PLAIN UIView. That is exactly what 6.1 changed (a container stacks
    // nothing; Yoga places its children), and exactly what a regression would undo.

    /// A `view` node: a plain UIView — not a UIStackView, not a UILabel, not a
    /// UIControl. `type(of:)`, because a subclass is precisely what is being excluded.
    private func isPlainContainer(_ view: UIView) -> Bool { type(of: view) == UIView.self }

    private func demoForm() -> UIView? {
        guard let form = root.subviews.first,
              isPlainContainer(form),
              form.subviews.count >= 6 else { return nil }
        return form
    }
    private func demoInput() -> UITextField? { demoForm()?.subviews[safe: 1] as? UITextField }
    private func demoEchoPanel() -> UIView? { demoForm()?.subviews[safe: 2] }
    private func demoEchoLabel() -> UILabel? { demoEchoPanel()?.subviews.first as? UILabel }

    /// The settings page: root's single child with 2 children whose first is the
    /// "Settings" title (distinguishes it from the 6-child demo).
    private func settingsView() -> UIView? {
        guard let view = root.subviews.first,
              isPlainContainer(view),
              view.subviews.count == 2,
              (view.subviews[0] as? UILabel)?.text == "Settings" else { return nil }
        return view
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// Finds a UIButton by title anywhere under root and sends .touchUpInside (the
    /// performClick twin). Fails if the button is not on screen.
    private func tapButton(_ title: String, file: StaticString = #filePath, line: UInt = #line) throws {
        let button = try XCTUnwrap(findButton(in: root, title: title),
                                   "button '\(title)' not on screen", file: file, line: line)
        button.sendActions(for: .touchUpInside)
    }

    private func findButton(in view: UIView, title: String) -> UIButton? {
        if let b = view as? UIButton, b.title(for: .normal) == title { return b }
        for sub in view.subviews {
            if let f = findButton(in: sub, title: title) { return f }
        }
        return nil
    }

    private func containsTextField(_ view: UIView) -> Bool {
        if view is UITextField { return true }
        return view.subviews.contains { containsTextField($0) }
    }

    /// Pumps the MAIN runloop (draining the mapper's main-queue batch) until the
    /// condition holds or a 10s deadline. Dispatch is async off the lane, so every
    /// interaction assertion polls.
    private func pollUntil(deadline seconds: TimeInterval = 10, _ cond: () -> Bool) -> Bool {
        let end = Date().addingTimeInterval(seconds)
        while Date() < end {
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.05))
            if cond() { return true }
        }
        return cond()
    }

    /// UIColor ≈ #RRGGBB by RGBA components (the ColorDrawable.color twin).
    private func colorMatches(_ color: UIColor?, _ hex: String) -> Bool {
        guard let color = color, let expected = BnColor.parse(hex) else { return false }
        var r1: CGFloat = 0, g1: CGFloat = 0, b1: CGFloat = 0, a1: CGFloat = 0
        var r2: CGFloat = 0, g2: CGFloat = 0, b2: CGFloat = 0, a2: CGFloat = 0
        color.getRed(&r1, green: &g1, blue: &b1, alpha: &a1)
        expected.getRed(&r2, green: &g2, blue: &b2, alpha: &a2)
        let tol: CGFloat = 0.004
        return abs(r1 - r2) < tol && abs(g1 - g2) < tol && abs(b1 - b2) < tol && abs(a1 - a2) < tol
    }
}

private extension Array {
    subscript(safe index: Int) -> Element? {
        indices.contains(index) ? self[index] : nil
    }
}
