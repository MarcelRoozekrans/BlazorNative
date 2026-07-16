// ─────────────────────────────────────────────────────────────────────────────
// BnClipboardTests — Phase 5.4 Gate 3 (M5 DoD #6): clipboard + share on iOS, real
// UIPasteboard. The iOS third of ClipboardProbeTests.cs (.NET) + the JVM clipboard
// test + ClipboardAndroidTest.kt (on-device ClipboardManager). Two proofs:
//   • end-to-end through the ClipboardProbe component (Copy→Paste→echo) against the
//     REAL UIPasteboard — the Android twin (dispatch → .NET → bridge → pasteboard);
//   • a direct bridge round-trip (clipboardWrite→clipboardRead) that isolates the
//     UIPasteboard wiring + the -needed buffer protocol from the dispatch chain.
// Share is asserted via the `shareHook` seam (the AndroidShellBridge.shareLaunchHook
// twin): the built activity content is captured WITHOUT popping the system sheet.
//
// ClipboardProbe tree (ClipboardProbe.cs → div/Copy/Paste/Share/echo, WidgetMapper
// NodeType table): root → UIView (div) → [0] UIButton "Copy", [1] "Paste",
// [2] "Share", [3] UILabel echo (text-collapsed span).
//
// Phase 6.1: the tree ACCESSORS churned with the rest — the div was a UIStackView
// and is now a plain UIView (Yoga owns placement), so `probeForm` walks `subviews`.
// The clipboard/share assertions themselves are untouched: this lane never went
// through the stack view, and that is exactly why it is a regression signal.
//
// Phase 7.6 (H3) — flake-hardening after 7.5's one flake (run 29504511994): on a
// shared CI simulator a UIPasteboard write can be transiently dropped by the
// pasteboard daemon — environmental (zero clipboard-adjacent diff on that branch,
// green on identical content re-run). The Copy/Paste taps now go through
// `tapAndAwait`: up to 3 attempts, each a NEW tap with its own poll window. A
// re-tap is a fresh dispatch through the same REAL chain (button → dispatch →
// .NET → bridge → UIPasteboard) and the assertion stays on pasteboard/echo
// CONTENT, so the proof is not weakened — and a persistent failure still fails,
// loudly, with its retry history in the message. The test stays REAL: no mock
// pasteboard, no longer poll (10s already dwarfs the operation; if the write was
// dropped, waiting proves nothing).
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit
@testable import BnHost

final class BnClipboardTests: BnHostTestCase {

    private static let copyPayload = "clip!" // mirror of ClipboardProbe.CopyPayload

    private var runtime: BnRuntime?
    private var root: UIView!

    override func tearDown() {
        AppleShellBridge.shareHook = nil // never leak the seam across tests
        super.tearDown()
    }

    // ── Copy → Paste round-trip through the REAL UIPasteboard (component path) ─

    func testClipboardProbeCopyPasteEchoes() throws {
        let form = try bootClipboardProbe()

        // Tap Copy → dispatch → ClipboardWriteAsync → the system UIPasteboard holds it.
        try tapAndAwait("Copy", in: form, orFail: "Copy never reached the real UIPasteboard") {
            UIPasteboard.general.string == Self.copyPayload
        }

        // Tap Paste → dispatch → ClipboardReadAsync → the echo label shows the payload.
        try tapAndAwait("Paste", in: form, orFail: "echo never showed the pasted clipboard value") {
            self.echoLabel()?.text == Self.copyPayload
        }
    }

    // ── Direct bridge round-trip (isolates UIPasteboard + the -needed protocol) ─

    func testClipboardRoundTripDirectAgainstRealPasteboard() {
        let bridge = AppleShellBridge()

        XCTAssertEqual(bridge.clipboardWrite(Self.copyPayload), 0)
        XCTAssertEqual(UIPasteboard.general.string, Self.copyPayload,
                       "clipboardWrite must hit the real UIPasteboard")

        // Read back via the -needed buffer protocol (the storageRead/currentRoute twin).
        let cap = 64
        var buf = [CChar](repeating: 0x7F, count: cap)
        let rc = buf.withUnsafeMutableBufferPointer {
            bridge.clipboardRead($0.baseAddress!, Int32(cap))
        }
        XCTAssertEqual(rc, 6, "\"clip!\" = 5 bytes + NUL")
        XCTAssertEqual(String(cString: buf), Self.copyPayload)
    }

    // ── Share: the seam captures the content, the sheet never pops ────────────

    func testShareHookCapturesContentNotSheet() throws {
        var captured: [Any]?
        // Capture on main (the hook fires on the lane thread) so the poll reads it race-free.
        AppleShellBridge.shareHook = { items in
            DispatchQueue.main.async { captured = items }
        }

        let form = try bootClipboardProbe()
        // Seed the echo with the payload (Copy → Paste), then Share it.
        try tapAndAwait("Copy", in: form, orFail: "Copy never reached the real UIPasteboard") {
            UIPasteboard.general.string == Self.copyPayload
        }
        try tapAndAwait("Paste", in: form, orFail: "echo never showed the pasted clipboard value") {
            self.echoLabel()?.text == Self.copyPayload
        }

        try tapButton("Share", in: form)
        XCTAssertTrue(pollUntil { captured != nil }, "the share hook never fired")
        XCTAssertEqual(captured?.first as? String, Self.copyPayload,
                       "the share content must be the echoed clipboard value")
        XCTAssertEqual(captured?.count, 1, "share carries exactly the one text item")
    }

    // ── Boot + tree accessors ─────────────────────────────────────────────────

    struct BootTimeout: Error {}

    private func bootClipboardProbe() throws -> UIView {
        root = UIView(frame: CGRect(x: 0, y: 0, width: 390, height: 844))
        let mapper = bnMapper(root: root)
        let runtime = BnRuntime(mapper: mapper)
        runtime.onError = { msg, err in NSLog("[BnClipboardTests] \(msg): \(err)") }
        self.runtime = runtime
        try runtime.start(component: "ClipboardProbe", os: "ios")
        guard pollUntil(deadline: 30, { self.probeForm() != nil }), let form = probeForm() else {
            XCTFail("ClipboardProbe never rendered its Copy/Paste/Share/echo tree within 30s")
            throw BootTimeout()
        }
        return form
    }

    /// The probe root: root's single child (a plain UIView since 6.1) with the 4
    /// children (Copy/Paste/Share buttons + echo label).
    private func probeForm() -> UIView? {
        guard let form = root.subviews.first, form.subviews.count >= 4 else { return nil }
        return form
    }

    /// The echo: the only UILabel that is a DIRECT child of the div (a UIButton's
    /// internal titleLabel is a subview of the BUTTON, never of the div).
    private func echoLabel() -> UILabel? {
        probeForm()?.subviews.first { $0 is UILabel } as? UILabel
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private func tapButton(_ title: String, in view: UIView, file: StaticString = #filePath, line: UInt = #line) throws {
        let button = try XCTUnwrap(findButton(in: view, title: title),
                                   "button '\(title)' not on screen", file: file, line: line)
        button.sendActions(for: .touchUpInside)
    }

    /// Phase 7.6 (H3) — the bounded-retry tap: taps `title` and polls for its
    /// observable effect; if the poll window closes empty, taps AGAIN, up to
    /// `attempts` times. Each re-tap is a new dispatch through the same real chain
    /// (button → dispatch → .NET → bridge → UIPasteboard), so the retry does not
    /// weaken the proof — the condition is on content, and a deterministic break
    /// fails all attempts identically, with the attempt count in the message.
    private func tapAndAwait(_ title: String, in view: UIView, attempts: Int = 3,
                             orFail failure: String,
                             file: StaticString = #filePath, line: UInt = #line,
                             until cond: () -> Bool) throws {
        for attempt in 1...attempts {
            try tapButton(title, in: view, file: file, line: line)
            if pollUntil(cond) {
                if attempt > 1 {
                    NSLog("[BnClipboardTests] '\(title)' needed \(attempt) attempts (H3 retry)")
                }
                return
            }
        }
        XCTFail("\(failure) — after \(attempts) '\(title)' taps, each with its own poll window",
                file: file, line: line)
    }

    private func findButton(in view: UIView, title: String) -> UIButton? {
        if let b = view as? UIButton, b.title(for: .normal) == title { return b }
        for sub in view.subviews {
            if let f = findButton(in: sub, title: title) { return f }
        }
        return nil
    }

    private func pollUntil(deadline seconds: TimeInterval = 10, _ cond: () -> Bool) -> Bool {
        let end = Date().addingTimeInterval(seconds)
        while Date() < end {
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.05))
            if cond() { return true }
        }
        return cond()
    }
}
