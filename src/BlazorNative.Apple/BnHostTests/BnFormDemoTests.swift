// ─────────────────────────────────────────────────────────────────────────────
// BnFormDemoTests — Phase 7.3 Gate 3: **`/form` ON THE SIMULATOR** (M7 DoD #4's
// iOS half). Mounts `BnFormDemo` through the real NativeAOT boot — by its
// registry NAME, the BnListDemoTests pattern — and asserts the numbers
// **`BnFormDemoTests.cs` pinned as the source of truth** — derived there,
// transcribed here, already asserted by Gate 2 on the AVD as THE SAME NUMBERS
// (for the LAYOUT — declared 240/300 widths; the intrinsic sizes are each
// platform's OWN, asserted per-platform by ORACLE, the 6.3 method):
//
//     10 root children: 4 UISwitch (checkbox ×2 + switch ×2 — decision 2: iOS
//     has no native checkbox), 2 UISlider, 2 UIPickerView, the echo UILabel,
//     the back row. Echo literal "cb:false sw:true sl:25 pk:0". Both pickers
//     hold the ["Alpha","Bravo","Charlie"] literal; selections 0 (bound) /
//     1 (disabled). Sliders are FLOAT-NATIVE: min 0, max 100, values 25/50
//     verbatim (Android's 20/1000-unit int geometry is its own — the design's
//     DO-NOT-COPY list).
//
// The round-trips ride **the real wire**: a drive on the native widget → the
// change target/delegate → `blazornative_dispatch_event` → NativeAOT → the
// `@bind-` pair → a re-render → the echo UILabel repaints. Every echo assertion
// is therefore an end-to-end assertion of Gate 1's .NET half AND this gate's
// wiring at once — and the exact-4 dispatch count is the loop-guard story end
// to end (the value write-backs the runtime pushed re-fired NONE).
//
// ── "DISABLED CONTROLS DISPATCH NOTHING" — THE iOS SHAPE OF THE ASSERTION ────
// Android drives REAL touch streams at its disabled quartet (dispatchTouchEvent;
// performClick bypasses `enabled` by design). A hosted XCTest cannot synthesize
// a UITouch, and `sendActions` is the iOS performClick — it bypasses `isEnabled`
// BY DESIGN, so driving it at a disabled control would "prove" a false failure.
// What enforces the contract on iOS is UIKit's own touch gate: a disabled
// UIControl does not track touches, and a `isUserInteractionEnabled == false`
// picker's wheel cannot be moved — no touch, no action, no delegate, no
// dispatch. So the iOS assertion is the 6.2 contentInsetAdjustmentBehavior
// precedent: **asserted on the PROPERTY, because there is no number to assert
// it on** — the gate's presence (isEnabled/isUserInteractionEnabled false on
// all four) plus the counter staying exactly at the bound drives' count across
// the whole test (a dispatch that leaked from mount, write-backs or the
// disabled quartet would move it; the demo's disabled handlers are unbound
// .NET-side, so only the counter could see it).
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit
@testable import BnHost

final class BnFormDemoTests: BnHostTestCase {

    // BnFormDemo.razor's consts (derived there, transcribed here — the
    // BnScrollDemo discipline).
    private let controlW: CGFloat = 240
    private let backRowW: CGFloat = 300
    private let initialEcho = "cb:false sw:true sl:25 pk:0"
    private let items = ["Alpha", "Bravo", "Charlie"]

    /// Hold the runtime for the test's lifetime so the @convention(c) callback
    /// trampoline is never released mid-render.
    private var runtime: BnRuntime?
    private var host: UIView!
    private var mapper: BnWidgetMapper!

    override func setUpWithError() throws {
        try super.setUpWithError()
        host = UIView(frame: CGRect(x: 0, y: 0, width: 390, height: 844))
        let mapper = bnMapper(root: host)
        self.mapper = mapper
        let runtime = BnRuntime(mapper: mapper)
        self.runtime = runtime
        runtime.onError = { msg, err in NSLog("[BnFormDemoTests] \(msg): \(err)") }
        try runtime.start(component: "BnFormDemo", os: "ios")
    }

    // ── Tree access ───────────────────────────────────────────────────────────

    private func rootView() throws -> UIView {
        try XCTUnwrap(host.subviews.first, "BnFormDemo has no root view")
    }

    private func control<T: UIView>(_ index: Int, as type: T.Type,
                                    file: StaticString = #filePath, line: UInt = #line) throws -> T {
        try XCTUnwrap(try rootView().subviews[index] as? T,
                      "root child \(index) must be a \(type)", file: file, line: line)
    }

    private func echoLabel() -> UILabel? {
        (host.subviews.first?.subviews.count ?? 0) == 10
            ? host.subviews.first?.subviews[8] as? UILabel : nil
    }

    private func rows(of picker: UIPickerView) -> [String] {
        guard let source = picker.dataSource, let delegate = picker.delegate else { return [] }
        let count = source.pickerView(picker, numberOfRowsInComponent: 0)
        return (0..<count).compactMap {
            delegate.pickerView?(picker, titleForRow: $0, forComponent: 0)
        }
    }

    /// The USER-pick stand-in (BnFormControlTests' helper, same reasoning):
    /// the wheel settles on [row] and UIKit calls the delegate.
    private func userPick(_ picker: UIPickerView, row: Int) {
        picker.selectRow(row, inComponent: 0, animated: false)
        picker.delegate?.pickerView?(picker, didSelectRow: row, inComponent: 0)
    }

    // ── Mount + poll ──────────────────────────────────────────────────────────

    /// Pumps the MAIN runloop until the page is mounted AND settled: 10
    /// children, laid out, the echo at [echo], and both pickers positioned.
    private func pollForForm(echo expected: String? = nil,
                             deadline seconds: TimeInterval = 60) -> Bool {
        let expectedEcho = expected ?? initialEcho
        let end = Date().addingTimeInterval(seconds)
        while Date() < end {
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.02))
            guard let root = host.subviews.first, root.subviews.count == 10,
                  root.frame.height > 0,
                  (root.subviews[8] as? UILabel)?.text == expectedEcho,
                  (root.subviews[6] as? UIPickerView)?.selectedRow(inComponent: 0) == 0,
                  (root.subviews[7] as? UIPickerView)?.selectedRow(inComponent: 0) == 1
            else { continue }
            return true
        }
        return false
    }

    /// Polls the echo only (post-round-trip: the re-render is async off the lane).
    private func pollForEcho(_ echo: String, deadline seconds: TimeInterval = 10) -> Bool {
        let end = Date().addingTimeInterval(seconds)
        while Date() < end {
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.02))
            if echoLabel()?.text == echo { return true }
        }
        return echoLabel()?.text == echo
    }

    // ── [1] The mount golden, on the glass ────────────────────────────────────

    func testMountingFormMatchesTheDotnetGoldensNumbers() throws {
        XCTAssertTrue(pollForForm(), "BnFormDemo never mounted/settled within 60s")
        let root = try rootView()

        // The widget classes, in the golden's child order — TWO of each new
        // NodeType actually decoded and instantiated (a missed nodeTypes entry
        // would have made a "?" → UILabel fallback here). checkbox AND switch
        // are UISwitch: the recorded decision-2 divergence, visible only as
        // pixels no assertion reads.
        let cb0 = try control(0, as: UISwitch.self)
        let cb1 = try control(1, as: UISwitch.self)
        let sw2 = try control(2, as: UISwitch.self)
        let sw3 = try control(3, as: UISwitch.self)
        let sl4 = try control(4, as: UISlider.self)
        let sl5 = try control(5, as: UISlider.self)
        let pk6 = try control(6, as: UIPickerView.self)
        let pk7 = try control(7, as: UIPickerView.self)

        // Initial state = the goldens' prop tables.
        XCTAssertFalse(cb0.isOn, "bound checkbox starts unchecked")
        XCTAssertTrue(cb1.isOn, "disabled checkbox is fixed CHECKED")
        XCTAssertTrue(sw2.isOn, "bound switch starts ON")
        XCTAssertTrue(sw3.isOn, "disabled switch is fixed ON")
        // FLOAT-NATIVE: the wire floats land verbatim (no int geometry — the
        // 20-unit/1000-unit numbers BnFormDemoAndroidTest asserts are
        // Android's own precision contract, deliberately not copied).
        XCTAssertEqual(sl4.minimumValue, 0); XCTAssertEqual(sl4.maximumValue, 100)
        XCTAssertEqual(sl4.value, 25, "bound slider at the declared 25")
        XCTAssertEqual(sl5.minimumValue, 0); XCTAssertEqual(sl5.maximumValue, 100)
        XCTAssertEqual(sl5.value, 50, "disabled slider at the fixed 50")
        XCTAssertEqual(rows(of: pk6), items, "the items literal, through the strict parser")
        XCTAssertEqual(rows(of: pk7), items)
        XCTAssertEqual(pk6.selectedRow(inComponent: 0), 0)
        XCTAssertEqual(pk7.selectedRow(inComponent: 0), 1)

        // The disabled quartet renders DISABLED (its dispatch silence is [4]).
        XCTAssertFalse(cb1.isEnabled); XCTAssertFalse(sw3.isEnabled)
        XCTAssertFalse(sl5.isEnabled); XCTAssertFalse(pk7.isUserInteractionEnabled)
        XCTAssertTrue(cb0.isEnabled); XCTAssertTrue(sw2.isEnabled)
        XCTAssertTrue(sl4.isEnabled); XCTAssertTrue(pk6.isUserInteractionEnabled)

        // The DECLARED widths — the numbers asserted CROSS-platform (the
        // measurement rule: declared where asserted on both shells).
        XCTAssertEqual(sl4.frame.width, controlW, accuracy: 0.5,
                       "bound slider width is the declared 240")
        XCTAssertEqual(sl5.frame.width, controlW, accuracy: 0.5)
        XCTAssertEqual(pk6.frame.width, controlW, accuracy: 0.5)
        XCTAssertEqual(pk7.frame.width, controlW, accuracy: 0.5)

        // The echo literal (Gate 1 pinned it for exactly this line).
        XCTAssertEqual(echoLabel()?.text, initialEcho)

        // Nav parity: the back row (declared 300) and its measured button.
        let backRow = root.subviews[9]
        XCTAssertEqual(backRow.frame.width, backRowW, accuracy: 0.5)
        let back = try XCTUnwrap(backRow.subviews.first as? UIButton)
        XCTAssertEqual(back.title(for: .normal), "← Back")
    }

    // ── [2] The intrinsic-size ORACLE (the 6.3 method) ────────────────────────

    /// The checkbox/switch quartet declares NO styles (the .NET golden pins
    /// zero), so their sizes are the PLATFORM's own — asserted against the
    /// platform's own measurement, never a transcribed constant: a fresh
    /// widget of the same class, asked the SAME question the measure
    /// trampoline asks the live one (`sizeThatFits` at the stretched width —
    /// a column's default alignItems is stretch, so the cross-axis WIDTH is
    /// layout, not intrinsic — and unconstrained height). Android mirrors this
    /// with a fresh CheckBox/Switch and measure() — DIFFERENT numbers, same
    /// method: frame parity applies to layout, never to intrinsic control
    /// sizes. The sliders get the same oracle at their DECLARED width.
    func testControlsTakeThePlatformsOwnIntrinsicHeightByOracle() throws {
        XCTAssertTrue(pollForForm())
        let root = try rootView()

        func assertOracleHeight(_ what: String, _ live: UIView, _ oracle: UIView,
                                file: StaticString = #filePath, line: UInt = #line) {
            let fit = oracle.sizeThatFits(
                CGSize(width: live.frame.width, height: .greatestFiniteMagnitude))
            XCTAssertEqual(live.frame.height, fit.height, accuracy: 1,
                           "\(what).h must equal what the platform's OWN widget measures — "
                           + "a fabricated measure func passes every relational assertion "
                           + "and fails this one", file: file, line: line)
            XCTAssertGreaterThan(live.frame.height, 0, "\(what) must have a real height",
                                 file: file, line: line)
        }
        assertOracleHeight("bound checkbox", root.subviews[0], UISwitch())
        assertOracleHeight("disabled checkbox", root.subviews[1], UISwitch())
        assertOracleHeight("bound switch", root.subviews[2], UISwitch())
        assertOracleHeight("disabled switch", root.subviews[3], UISwitch())
        assertOracleHeight("bound slider", root.subviews[4], UISlider())
        assertOracleHeight("disabled slider", root.subviews[5], UISlider())

        // The cross-axis width is the STRETCH (Yoga's default alignItems),
        // not an intrinsic: the un-styled quartet fills the column. Pinned so
        // nobody "fixes" a full-width switch into a declared width silently.
        let colW = root.frame.width
        for i in 0...3 {
            XCTAssertEqual(root.subviews[i].frame.width, colW, accuracy: 0.5,
                           "child \(i) stretches to the column width (alignItems: stretch)")
        }
        // …and the pickers hold their declared width with a real (platform's
        // own) height — UIPickerView's wheel height is nobody's frame table.
        XCTAssertGreaterThan(root.subviews[6].frame.height, 0)
        XCTAssertGreaterThan(root.subviews[7].frame.height, 0)
    }

    // ── [3] The four round-trips, over the REAL wire, into the echo ──────────

    func testDrivingEachBoundControlRoundTripsIntoTheEcho() throws {
        XCTAssertTrue(pollForForm())
        let dispatchesBefore = mapper.changeDispatchesSent

        // Checkbox: the user stand-in (a tap flips the state and fires
        // .valueChanged — sendActions is the performClick analog, and the
        // control is ENABLED, so the stand-in is honest here).
        let cb0 = try control(0, as: UISwitch.self)
        cb0.setOn(true, animated: false)
        cb0.sendActions(for: .valueChanged)
        XCTAssertTrue(pollForEcho("cb:true sw:true sl:25 pk:0"),
                      "the checkbox round-trip never re-rendered the echo")
        XCTAssertTrue((try control(0, as: UISwitch.self)).isOn,
                      "…and the value prop wrote back into the widget")

        // Switch: drive it OFF.
        let sw2 = try control(2, as: UISwitch.self)
        sw2.setOn(false, animated: false)
        sw2.sendActions(for: .valueChanged)
        XCTAssertTrue(pollForEcho("cb:true sw:false sl:25 pk:0"),
                      "the switch round-trip never re-rendered the echo")

        // Slider: raw 62 → THE STEP CONTRACT quantizes to 60 at the dispatch
        // site (min + 12×5), the payload is the invariant float "60.0" and
        // .NET echoes it as sl:60 — the same on-the-wire multiple Android's
        // progress-12 drive produces.
        let sl4 = try control(4, as: UISlider.self)
        sl4.setValue(62, animated: false)
        sl4.sendActions(for: .valueChanged)
        XCTAssertTrue(pollForEcho("cb:true sw:false sl:60 pk:0"),
                      "the slider round-trip never re-rendered the echo — the payload is "
                      + "the quantized invariant float 60.0 and .NET echoes it as sl:60")
        XCTAssertEqual((try control(4, as: UISlider.self)).value, 60,
                       "the thumb snapped onto the dispatched step multiple")

        // Picker: the wheel settles on row 2 and the delegate fires.
        userPick(try control(6, as: UIPickerView.self), row: 2)
        XCTAssertTrue(pollForEcho("cb:true sw:false sl:60 pk:2"),
                      "the picker round-trip never re-rendered the echo")

        // Four drives → EXACTLY four change dispatches — and the value echoes
        // the runtime wrote back re-fired NONE of them (or this count runs
        // away and the echoes above never settle: the per-control silence,
        // end to end).
        XCTAssertEqual(mapper.changeDispatchesSent - dispatchesBefore, 4,
                       "expected exactly the four drives' dispatches")
    }

    // ── [4] Disabled controls dispatch NOTHING (the iOS shape — see header) ──

    func testDisabledControlsRenderDisabledAndNothingLeaksOnTheWire() throws {
        XCTAssertTrue(pollForForm())

        // Sensitivity first: the SAME stand-in on the BOUND checkbox moves the
        // echo — so the silence below is a fact about the wiring, not the helper.
        let cb0 = try control(0, as: UISwitch.self)
        cb0.setOn(true, animated: false)
        cb0.sendActions(for: .valueChanged)
        XCTAssertTrue(pollForEcho("cb:true sw:true sl:25 pk:0"),
                      "the stand-in gesture itself must work (bound checkbox toggles)")

        let before = mapper.changeDispatchesSent

        // THE GATE, ASSERTED ON THE PROPERTY (the file header says why a
        // hosted XCTest can assert nothing stronger): a disabled UIControl
        // does not track touches and a non-interactive picker's wheel cannot
        // move — no touch, no action, no delegate, no dispatch. UIKit's gate,
        // present on all four.
        XCTAssertFalse((try control(1, as: UISwitch.self)).isEnabled)
        XCTAssertFalse((try control(3, as: UISwitch.self)).isEnabled)
        XCTAssertFalse((try control(5, as: UISlider.self)).isEnabled)
        XCTAssertFalse((try control(7, as: UIPickerView.self)).isUserInteractionEnabled)

        // Settle a generous window: any async dispatch (a leak from the mount,
        // the write-backs, or the disabled quartet's props) would land here.
        let end = Date().addingTimeInterval(1)
        while Date() < end {
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.05))
        }

        XCTAssertEqual(mapper.changeDispatchesSent, before,
                       "NOTHING leaked on the change wire — the counter is the only honest "
                       + "witness (the demo's disabled handlers are unbound .NET-side, so a "
                       + "leaked dispatch would move no echo)")
        // …and the disabled quartet's native state never moved either.
        XCTAssertTrue((try control(1, as: UISwitch.self)).isOn)
        XCTAssertTrue((try control(3, as: UISwitch.self)).isOn)
        XCTAssertEqual((try control(5, as: UISlider.self)).value, 50)
        XCTAssertEqual((try control(7, as: UIPickerView.self)).selectedRow(inComponent: 0), 1)
        XCTAssertEqual(echoLabel()?.text, "cb:true sw:true sl:25 pk:0",
                       "…and the echo never moved")
    }
}
