// ─────────────────────────────────────────────────────────────────────────────
// BnFormControlTests — Phase 7.3 Gate 3: the form controls' props, wires and,
// above all, **THE PER-CONTROL "PROGRAMMATIC SET FIRES NOTHING" VERIFICATION** —
// tested per control because the design says "verify each, never assume", and
// the verification found iOS is the OPPOSITE of Android:
//
//  - Android's `CompoundButton.setChecked` / `ProgressBar.setProgress` fire
//    their listeners SYNCHRONOUSLY (its `applyingBatch` guard is load-bearing)
//    and its `Spinner.setSelection` fires on a LATER layout pass (its guard is
//    the expected-selection compare). **On iOS `UISwitch.setOn`,
//    `UISlider.setValue` and `UIPickerView.selectRow` fire NOTHING** — inside a
//    batch AND outside one — so the shell carries NO applyingBatch dispatch
//    guard on any of the four. For the SWITCH and the SLIDER that claim is
//    falsifiable as written: no guard exists that could swallow a fire, so if
//    UIKit ever starts firing, their fires-nothing halves redden. The PICKER
//    is NOT like them (the Gate 3 review's S1-1): its apply path records
//    appliedSelection BEFORE calling selectRow — record-before-apply IS,
//    structurally, the expected-selection guard Android's Spinner carries —
//    so a delegate fire through that path WOULD be swallowed by the same-row
//    compare. Only the RAW-selectRow test (calling the view directly, apply
//    path bypassed, guard disarmed) makes the picker's fires-nothing claim
//    falsifiable.
//  - What iOS DOES need, and what the REQUIRED MUTATIONS redden: the slider's
//    step-quantization DEDUP (a float-native drag delivers a distinct
//    `.valueChanged` per sample; Android's int progress dedups structurally),
//    and the picker's SAME-ROW compare (a wheel spun away and back is not a
//    change — AdapterView drops those the same way).
//
// NO ATTACHED WINDOW, and that is a FINDING mirrored from Gate 2's hosting
// split, inverted: Android's Spinner delivers its selection in a LAYOUT PASS
// (hence BnSpinner + the attached-Activity host there); `UIPickerView.selectRow`
// applies immediately and calls no delegate, so every test here runs on the
// detached BnSyntheticHost. The design's DO-NOT-COPY list names both halves.
//
// The picker's NORMATIVE CLAMP RULE (BnPicker.razor's header) is asserted ON
// THE WIRE (the dispatched payload IS the clamped index): .NET's benign
// inbound clamp makes a non-clamping shell invisible from the .NET side, so
// these dispatches are the only place the rule is provable. The twin of
// Kotlin's WidgetMapperFormControlsTest, observable for observable.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit
@testable import BnHost

final class BnFormControlTests: BnHostTestCase {

    private static let node: Int32 = 1
    private static let handler: Int32 = 42
    /// BnFormDemoTests.ItemsJson — THE wire literal, transcribed exactly.
    private static let items = #"["Alpha","Bravo","Charlie"]"#

    /// Records every (handlerId, eventName, payload) the mapper dispatches.
    /// Main-thread only — every control listener and every applyBatch is.
    private final class Recorder {
        var events: [(handlerId: Int32, name: String, payload: String?)] = []
        var payloads: [String?] { events.map { $0.payload } }
    }

    private var recorder = Recorder()
    private var host: BnSyntheticHost!

    override func setUpWithError() throws {
        try super.setUpWithError()
        let recorder = Recorder()
        self.recorder = recorder
        host = BnSyntheticHost()
        host.mapper.onUiEvent = { handlerId, name, payload in
            recorder.events.append((handlerId, name, payload))
        }
    }

    /// One control with its change wire attached and [props] applied — ONE
    /// mount batch, the exact shape the renderer emits for `/form` (attach
    /// BEFORE props, so a prop application that fired would be CAUGHT).
    private func mountPatches(_ nodeType: String,
                              _ props: [(String, String?)] = []) -> [BnPatch] {
        var patches: [BnPatch] = [bnCreate(Self.node, nodeType, nil)]
        patches.append(.attachEvent(nodeId: Self.node, eventName: "change", handlerId: Self.handler))
        for (name, value) in props {
            patches.append(.updateProp(nodeId: Self.node, name: name, value: value))
        }
        return patches
    }

    private func prop(_ name: String, _ value: String?) -> BnPatch {
        .updateProp(nodeId: Self.node, name: name, value: value)
    }

    private func child<T: UIView>(_ index: Int = 0, as type: T.Type,
                                  file: StaticString = #filePath, line: UInt = #line) throws -> T {
        try XCTUnwrap(host.root.subviews[index] as? T,
                      "child \(index) must be a \(type)", file: file, line: line)
    }

    /// The picker's rows, read back THROUGH its own dataSource/delegate — the
    /// adapter-count/getItem twin.
    private func rows(of picker: UIPickerView) -> [String] {
        guard let source = picker.dataSource, let delegate = picker.delegate else { return [] }
        let count = source.pickerView(picker, numberOfRowsInComponent: 0)
        return (0..<count).compactMap {
            delegate.pickerView?(picker, titleForRow: $0, forComponent: 0)
        }
    }

    /// The USER-pick stand-in: what a wheel gesture does — the wheel settles on
    /// [row] and UIKit calls the delegate's didSelectRow. (`selectRow` alone is
    /// the PROGRAMMATIC path and calls no delegate — that difference is half of
    /// what this file verifies.)
    private func userPick(_ picker: UIPickerView, row: Int) {
        picker.selectRow(row, inComponent: 0, animated: false)
        picker.delegate?.pickerView?(picker, didSelectRow: row, inComponent: 0)
    }

    // ── The value prop, per control ───────────────────────────────────────────

    func testCheckboxValuePropDrivesTheOnStateAndGarbageIsIgnored() throws {
        host.render(mountPatches("checkbox", [("value", "true")]))
        let toggle = try child(as: UISwitch.self)
        XCTAssertTrue(toggle.isOn, "value \"true\" must switch it on")

        host.render([prop("value", "false")])
        XCTAssertFalse(toggle.isOn, "value \"false\" must switch it off")

        // The wire grammar is EXACTLY "true"/"false" (ordinal — BnCheckbox's
        // header): "True" is garbage, logged and ignored, state unchanged.
        host.render([prop("value", "True")])
        XCTAssertFalse(toggle.isOn, "\"True\" is not the wire grammar — the state must not move")
    }

    func testSliderPropsAreFloatNativeAndRewritesRederiveTheWholeState() throws {
        // The demo's bound slider: 25 / 0..100 step 5 — float-native, so the
        // widget carries the wire floats VERBATIM (no int geometry to assert;
        // the step lives in the DISPATCH quantization, tested below).
        host.render(mountPatches("slider",
            [("value", "25"), ("min", "0"), ("max", "100"), ("step", "5")]))
        let slider = try child(as: UISlider.self)
        XCTAssertEqual(slider.minimumValue, 0)
        XCTAssertEqual(slider.maximumValue, 100)
        XCTAssertEqual(slider.value, 25)

        // Shrink the range: the state holds the RAW wire floats, so the widget
        // re-derives from the whole state (order-independent).
        host.render([prop("max", "50")])
        XCTAssertEqual(slider.maximumValue, 50)
        XCTAssertEqual(slider.value, 25, "the in-range value survives the range rewrite")

        // Garbage floats are the OTHER shell's grammar screen, mirrored: Swift's
        // Float(String) would take "inf" and hex floats; the strict parse must not.
        host.render([prop("value", "0x1p3")])
        XCTAssertEqual(slider.value, 25, "'0x1p3' is not the wire float grammar — ignored")

        // And none of those programmatic moves dispatched anything.
        XCTAssertEqual(recorder.payloads.count, 0,
                       "prop-driven state changes are not user input")
    }

    // ── The fires-nothing verification, per control (the design's own words) ──

    func testCheckboxProgrammaticSetFiresNothingAndAUserToggleDispatches() throws {
        // The mount batch itself sets value "true" AFTER the change attach —
        // if UISwitch.setOn fired .valueChanged (it does not — VERIFIED here,
        // with no guard in the shell to swallow the evidence), this would
        // dispatch.
        host.render(mountPatches("checkbox", [("value", "true")]))
        XCTAssertEqual(recorder.payloads.count, 0,
                       "a patch-applied value echo must dispatch NOTHING — and on iOS "
                       + "there is NO batch guard: this is the platform's own silence, verified")

        // The SAME programmatic set OUTSIDE a batch fires nothing either — the
        // half Android cannot claim (its setChecked fires synchronously and
        // only the batch flag saves it; here the silence is UIKit's).
        let toggle = try child(as: UISwitch.self)
        toggle.setOn(false, animated: false)
        XCTAssertEqual(recorder.payloads.count, 0,
                       "UISwitch.setOn outside a batch fires nothing — the 5.3 finding, "
                       + "re-verified per control")

        // The user stand-in: a tap flips the state and fires .valueChanged.
        toggle.setOn(true, animated: false)
        toggle.sendActions(for: .valueChanged)
        XCTAssertEqual(recorder.payloads, ["true"],
                       "a user toggle dispatches the wire grammar's payload")
        XCTAssertEqual(recorder.events.first?.handlerId, Self.handler)
        XCTAssertEqual(recorder.events.first?.name, "change")
    }

    func testSwitchProgrammaticSetFiresNothingAndAUserToggleDispatches() throws {
        host.render(mountPatches("switch", [("value", "true")]))
        XCTAssertEqual(recorder.payloads.count, 0,
                       "same silence, same finding, verified per control (never assumed)")

        let toggle = try child(as: UISwitch.self)
        toggle.setOn(false, animated: false)
        toggle.sendActions(for: .valueChanged)
        XCTAssertEqual(recorder.payloads, ["false"])
    }

    func testSliderProgrammaticSetFiresNothingAndAUserDragQuantizesToStepMultiples() throws {
        host.render(mountPatches("slider",
            [("value", "25"), ("min", "0"), ("max", "100"), ("step", "5")]))
        // applySlider ran three setters inside the batch; setValue fires
        // nothing (verified — no guard exists to swallow it).
        XCTAssertEqual(recorder.payloads.count, 0)

        let slider = try child(as: UISlider.self)
        slider.setValue(60, animated: false)
        XCTAssertEqual(recorder.payloads.count, 0,
                       "UISlider.setValue outside a batch fires nothing — verified")
        // Undo the silent programmatic move so the drag below starts from the
        // wire's own state (25).
        slider.setValue(25, animated: false)

        // The user drag stand-in: raw 62 → THE STEP CONTRACT quantizes onto
        // min + n×step = 60, dispatched as the invariant float BnSlider's
        // strict parse expects ("60.0" — never "60,0", on any locale), and the
        // thumb snaps onto the multiple the wire carried.
        slider.setValue(62, animated: false)
        slider.sendActions(for: .valueChanged)
        XCTAssertEqual(recorder.payloads, ["60.0"],
                       "the payload is min + n×step, exactly (raw 62 → 60)")
        XCTAssertEqual(slider.value, 60, "the thumb snapped onto the dispatched multiple")

        // THE DEDUP — the guard iOS actually needs (the required mutation
        // reddens here): a float-native drag delivers a distinct sample per
        // pixel, and raw 61 quantizes onto the SAME multiple 60 — not a
        // change, nothing dispatched. Android's int progress dedups this
        // structurally; iOS must do it at the dispatch site.
        slider.setValue(61, animated: false)
        slider.sendActions(for: .valueChanged)
        XCTAssertEqual(recorder.payloads, ["60.0"],
                       "a sample quantizing onto the SAME step multiple is not a change")

        // …and the next multiple dispatches exactly once more.
        slider.setValue(63, animated: false)
        slider.sendActions(for: .valueChanged)
        XCTAssertEqual(recorder.payloads, ["60.0", "65.0"], "raw 63 → the next multiple, 65")
    }

    // ── The picker: bind, ITS guards, and THE CLAMP RULE ─────────────────────

    func testPickerMountBindsTheWheelSelectsAndDispatchesNothing() throws {
        host.render(mountPatches("picker",
            [("items", Self.items), ("selectedIndex", "1")]))
        let picker = try child(as: UIPickerView.self)

        XCTAssertEqual(rows(of: picker), ["Alpha", "Bravo", "Charlie"],
                       "the items literal round-trips the STRICT parser into the dataSource")
        XCTAssertEqual(picker.selectedRow(inComponent: 0), 1, "the declared selection")

        // The mount applied items (an empty→non-empty clamp — the deliberate
        // NO-NOTIFY asymmetry) and a programmatic selectRow. Zero dispatches is
        // BOTH facts at once: selectRow called no delegate (verified), and the
        // empty→non-empty transition notified nothing.
        XCTAssertEqual(recorder.payloads.count, 0,
                       "mount dispatches NOTHING — selectRow fires no delegate, and "
                       + "empty→non-empty is not a move")
    }

    func testASelectedIndexPatchFiresNothingThePickersProgrammaticPath() throws {
        host.render(mountPatches("picker",
            [("items", Self.items), ("selectedIndex", "0")]))
        let picker = try child(as: UIPickerView.self)

        // A later batch moves the selection programmatically (the bound-state
        // echo shape). NOTE (the Gate 3 review's S1-1): this apply path records
        // appliedSelection BEFORE calling selectRow — record-before-apply is,
        // structurally, the expected-selection guard Android's Spinner carries
        // — so a delegate fire here WOULD be swallowed by the same-row compare.
        // This test pins the wire's silence through the shell's OWN path; the
        // raw-selectRow test below is what makes "selectRow calls no delegate"
        // falsifiable.
        host.render([prop("selectedIndex", "2")])
        XCTAssertEqual(picker.selectedRow(inComponent: 0), 2)
        XCTAssertEqual(recorder.payloads.count, 0,
                       "a programmatic selection set must re-fire NOTHING")
    }

    func testARawSelectRowBypassingTheApplyPathFiresNoDelegateTheGuardDisarmed() throws {
        host.render(mountPatches("picker",
            [("items", Self.items), ("selectedIndex", "0")]))
        let picker = try child(as: UIPickerView.self)

        // THE DISCRIMINATOR (the Gate 3 review's S1-1): every other picker
        // fires-nothing test moves the selection through the shell's apply
        // path, which records appliedSelection BEFORE calling selectRow — if
        // UIKit DID fire didSelectRow on a programmatic selectRow, the
        // same-row compare would swallow it and those tests would stay green
        // in the counterfactual. Here selectRow is called RAW on the view:
        // appliedSelection stays 0 ≠ 2, the guard is disarmed, and a delegate
        // fire would dispatch "2" → red. THIS assertion is the falsifiable
        // form of "UIPickerView.selectRow fires nothing".
        picker.selectRow(2, inComponent: 0, animated: false)
        XCTAssertEqual(picker.selectedRow(inComponent: 0), 2, "the raw move landed")
        XCTAssertEqual(recorder.payloads.count, 0,
                       "selectRow called no delegate — with the same-row guard disarmed, "
                       + "nothing in this shell could swallow the evidence")
    }

    func testSelectedIndexBeforeItemsInOneBatchLandsIdenticallyToTheNormativeOrder() throws {
        // Hand-rolled REVERSED order (the Gate 3 review's S1-3): selectedIndex
        // arrives while items is still empty — clamped to −1 on the spot —
        // then items lands. [requestedIndex] keeps the RAW wire value, so the
        // items write clamps against the wire's own last request: the
        // items/selectedIndex patch order is immaterial, pinned here. (Android
        // is code-identical on this mechanism — noted for Gate 4, not
        // re-tested on the instrumented lane.)
        host.render([
            bnCreate(Self.node, "picker", nil),
            .attachEvent(nodeId: Self.node, eventName: "change", handlerId: Self.handler),
            prop("selectedIndex", "2"),
            prop("items", Self.items),
        ])
        let picker = try child(as: UIPickerView.self)
        XCTAssertEqual(rows(of: picker), ["Alpha", "Bravo", "Charlie"])
        XCTAssertEqual(picker.selectedRow(inComponent: 0), 2,
                       "the reversed order lands on the SAME selection the normative order does")
        XCTAssertEqual(recorder.payloads.count, 0,
                       "…and dispatches nothing, exactly like the normative mount")
    }

    func testAUserSelectionDispatchesTheNewIndexExactlyOnceAndASameRowRepickAddsNothing() throws {
        host.render(mountPatches("picker",
            [("items", Self.items), ("selectedIndex", "0")]))
        let picker = try child(as: UIPickerView.self)

        userPick(picker, row: 2)
        XCTAssertEqual(recorder.payloads, ["2"],
                       "the payload is the new index as an invariant int")
        XCTAssertEqual(recorder.events.first?.handlerId, Self.handler)
        XCTAssertEqual(recorder.events.first?.name, "change")

        // Re-selecting the SAME position is not a change — the SAME-ROW guard
        // (the required mutation reddens here: drop the compare in
        // handlePickerUserSelect and this dispatches a duplicate "2").
        userPick(picker, row: 2)
        XCTAssertEqual(recorder.payloads, ["2"])
    }

    func testAnItemShrinkBelowTheSelectionClampsToTheLASTItemAndNotifiesTheWire() throws {
        host.render(mountPatches("picker",
            [("items", Self.items), ("selectedIndex", "2")]))
        let picker = try child(as: UIPickerView.self)
        XCTAssertEqual(picker.selectedRow(inComponent: 0), 2)

        // Items shrink 3 → 2 with the selection on index 2: THE NORMATIVE
        // CLAMP (BnPicker.razor's header) — clamp TO THE LAST item, and
        // NOTIFY the CLAMPED index on the change wire. Asserted ON THE WIRE
        // deliberately: .NET's benign inbound clamp makes a non-clamping
        // shell invisible from the .NET side; this payload is the only
        // place the rule is provable.
        host.render([prop("items", #"["Alpha","Bravo"]"#)])
        XCTAssertEqual(rows(of: picker), ["Alpha", "Bravo"], "the dataSource re-bound")
        XCTAssertEqual(picker.selectedRow(inComponent: 0), 1,
                       "the selection clamped to the LAST item")
        XCTAssertEqual(recorder.payloads, ["1"],
                       "…and the shell NOTIFIED the clamped index — the bound .NET "
                       + "state re-syncs to what the native widget actually shows")
    }

    func testItemsEmptiedClampsToMinusOneAndNotifies() throws {
        host.render(mountPatches("picker",
            [("items", Self.items), ("selectedIndex", "1")]))
        let picker = try child(as: UIPickerView.self)

        // Empty items → −1 (the only state an empty picker has), notified:
        // the live selection was displaced (the clamp rule's empty arm).
        host.render([prop("items", "[]")])
        XCTAssertEqual(rows(of: picker), [])
        XCTAssertEqual(recorder.payloads, ["-1"])
    }

    func testAnInRangeSelectionIsPRESERVEDAcrossAnItemsChangeNoNotify() throws {
        host.render(mountPatches("picker",
            [("items", Self.items), ("selectedIndex", "1")]))
        let picker = try child(as: UIPickerView.self)

        // Same size, new content: re-bind the wheel, PRESERVE the selection
        // (the rule's other half) — the clamp did not move it, so no notify.
        host.render([prop("items", #"["Delta","Echo","Foxtrot"]"#)])
        XCTAssertEqual(rows(of: picker), ["Delta", "Echo", "Foxtrot"])
        XCTAssertEqual(picker.selectedRow(inComponent: 0), 1)
        XCTAssertEqual(recorder.payloads.count, 0, "an unmoved selection notifies NOTHING")
    }

    func testMalformedItemsRenderAnEMPTYPickerNeverAWrongOne() throws {
        host.render(mountPatches("picker",
            [("items", Self.items), ("selectedIndex", "1")]))
        let picker = try child(as: UIPickerView.self)
        XCTAssertEqual(rows(of: picker).count, 3)

        // A normative malformed vector (whitespace between tokens — the strict
        // grammar has none; BnItemsJsonTests owns the full rejection matrix).
        // The shell logs LOUDLY and renders EMPTY — malformed data never
        // becomes a plausible-looking wrong picker.
        host.render([prop("items", #"[ "a"]"#)])
        XCTAssertEqual(rows(of: picker), [], "malformed → EMPTY")
        // …and the displaced live selection followed the empty-items clamp arm.
        XCTAssertEqual(recorder.payloads, ["-1"])
    }

    // ── Detach + enabled ─────────────────────────────────────────────────────

    func testDetachChangeSilencesEveryControl() throws {
        host.render([
            bnCreate(1, "checkbox", nil),
            bnCreate(2, "switch", nil),
            bnCreate(3, "slider", nil),
            bnCreate(4, "picker", nil),
            .attachEvent(nodeId: 1, eventName: "change", handlerId: 11),
            .attachEvent(nodeId: 2, eventName: "change", handlerId: 12),
            .attachEvent(nodeId: 3, eventName: "change", handlerId: 13),
            .attachEvent(nodeId: 4, eventName: "change", handlerId: 14),
            .updateProp(nodeId: 3, name: "min", value: "0"),
            .updateProp(nodeId: 3, name: "max", value: "100"),
            .updateProp(nodeId: 4, name: "items", value: Self.items),
            .updateProp(nodeId: 4, name: "selectedIndex", value: "0"),
        ])
        host.render([
            .detachEvent(nodeId: 1, handlerId: 11, eventName: "change"),
            .detachEvent(nodeId: 2, handlerId: 12, eventName: "change"),
            .detachEvent(nodeId: 3, handlerId: 13, eventName: "change"),
            .detachEvent(nodeId: 4, handlerId: 14, eventName: "change"),
        ])

        let checkbox = try child(0, as: UISwitch.self)
        checkbox.setOn(true, animated: false)
        checkbox.sendActions(for: .valueChanged)
        let toggle = try child(1, as: UISwitch.self)
        toggle.setOn(true, animated: false)
        toggle.sendActions(for: .valueChanged)
        let slider = try child(2, as: UISlider.self)
        slider.setValue(50, animated: false)
        slider.sendActions(for: .valueChanged)
        userPick(try child(3, as: UIPickerView.self), row: 2)

        XCTAssertEqual(recorder.payloads.count, 0,
                       "a detached wire dispatches nothing — the detach arm mirrors "
                       + "the attach arm's switch (the 3.3 symmetric-arms rule)")
    }

    func testEnabledFalseDisablesEachFormControl() throws {
        host.render([
            bnCreate(1, "checkbox", nil), .updateProp(nodeId: 1, name: "enabled", value: "false"),
            bnCreate(2, "switch", nil), .updateProp(nodeId: 2, name: "enabled", value: "false"),
            bnCreate(3, "slider", nil), .updateProp(nodeId: 3, name: "enabled", value: "false"),
            bnCreate(4, "picker", nil), .updateProp(nodeId: 4, name: "enabled", value: "false"),
        ])
        for i in 0..<3 {
            XCTAssertFalse((host.root.subviews[i] as? UIControl)?.isEnabled ?? true,
                           "child \(i) must render disabled — UIKit's touch gate is what "
                           + "makes a disabled control dispatch nothing on a device")
        }
        // UIPickerView is not a UIControl; its gate is touch delivery itself
        // (the enabled arm's comment) — asserted on the PROPERTY, the 6.2
        // contentInsetAdjustmentBehavior precedent: there is no number to
        // assert it on, and a hosted XCTest cannot synthesize a UITouch.
        XCTAssertFalse((host.root.subviews[3] as? UIPickerView)?.isUserInteractionEnabled ?? true,
                       "a disabled picker's wheel takes no touches, so its delegate never fires")
    }
}
