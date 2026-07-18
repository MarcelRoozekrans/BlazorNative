// ─────────────────────────────────────────────────────────────────────────────
// BnCameraTests — Phase 9.3 Gate 3 (M9 DoD #5): camera photo capture on the iOS
// simulator. The iOS third of BnCameraDemoTests.cs (.NET, DevHostBridge drives every
// status headless) + the AVD BnCameraAndroidTest.kt (ACTION_IMAGE_CAPTURE). The
// bridge/struct/export pins are UNCHANGED — camera adds op=4 + a wire status enum + a
// handler, and touches the ABI at nothing else (BnDriftTests still asserts 80 bytes /
// offset 72 / 10 exports, UNMOVED — this suite does NOT move it, it relies on it).
//
// THE SIMULATOR HAS NO CAMERA — the phase's iOS honesty (design §9 Gate 3, worse than
// 9.2's no-Secure-Enclave). Three postures, each labelled:
//   (a) `check → Unavailable` and a bare `capture → Unavailable` are the REAL, CORRECT
//       simulator results — `UIImagePickerController.isSourceTypeAvailable(.camera)` is
//       genuinely FALSE — and are asserted AS SUCH (no override), not smoothed over.
//   (b) The capture round-trip (present the camera UI → shoot → a UIImage) is NOT drivable
//       in CI, so it is SEAM-DRIVEN: the availability + authorization overrides open the
//       gates, `suppressSystemCameraPresentForTest` skips the un-presentable picker, and
//       the test fires the REAL delegate with a canned UIImage (the geolocation hand-rolled
//       fire). CI then asserts the file→BnImage contract + the wire deterministically.
//   (c) The real camera UI + a real sensor capture are DOUBLY UNPROVEN (no sim camera AND
//       no Apple account — iOS real-device DEFERRED). Named here, not smuggled.
//
// THE IMAGE CROSSES AS A PATH (design §2): the seam capture writes a canned JPEG THROUGH
// the SAME processing path (normalize + downscale + encode + write) and returns its
// file:// path in the payload; the composition test then proves that path names a real
// file BnImage measures (BnImageLoader.naturalPixelSize — the exact function the image
// node's Yoga measure func calls). The bytes never cross the wire.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import AVFoundation
import UIKit
@testable import BnHost

final class BnCameraTests: BnHostTestCase {

    private let capturedLock = NSLock()
    private var captured: [(id: Int64, status: Int32, payload: String?)] = []
    private var runtime: BnRuntime?
    private var root: UIView!

    override func setUp() {
        super.setUp()
        BnCamera.resetForTest()
        captured = []
    }

    override func tearDown() {
        BnCamera.resetForTest()
        super.tearDown()
    }

    private func installCapture() {
        BnCamera.completeHookForTest = { [weak self] id, status, payload in
            guard let self = self else { return 0 }
            self.capturedLock.lock(); self.captured.append((id, status, payload)); self.capturedLock.unlock()
            return 0
        }
    }

    private func capturedStatuses() -> [Int32] {
        capturedLock.lock(); defer { capturedLock.unlock() }; return captured.map { $0.status }
    }

    private func lastPayload() -> String? {
        capturedLock.lock(); defer { capturedLock.unlock() }; return captured.last?.payload
    }

    /// A solid-blue canned image of EXACTLY `width`×`height` pixels (scale 1) with the given
    /// display orientation — the stand-in for a real capture (the sim has no camera). The
    /// pixel dims are the contract the file→BnImage composition and the orientation-normalize
    /// assert on.
    private func cannedImage(width: Int, height: Int,
                             orientation: UIImage.Orientation = .up) -> UIImage {
        let cs = CGColorSpaceCreateDeviceRGB()
        let ctx = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8,
                            bytesPerRow: 0, space: cs,
                            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        ctx.setFillColor(UIColor.blue.cgColor)
        ctx.fill(CGRect(x: 0, y: 0, width: width, height: height))
        return UIImage(cgImage: ctx.makeImage()!, scale: 1, orientation: orientation)
    }

    /// Opens both capture gates on the sim (availability + authorization) and skips the
    /// un-presentable picker — the seam the round-trip runs behind.
    private func openTheCaptureGatesUnderTheSeam() {
        BnCamera.sourceTypeAvailableOverrideForTest = { true }
        BnCamera.cameraAuthorizationStatusOverrideForTest = { .authorized }
        BnCamera.suppressSystemCameraPresentForTest = true
    }

    // ── (a) THE HONEST SIMULATOR RESULT: no camera → Unavailable, asserted as correct ─────

    func testCheckOnTheSimulatorIsUnavailableTheHonestNoCameraResult() {
        installCapture()
        let bridge = AppleShellBridge()
        // NO override — the REAL isSourceTypeAvailable(.camera), which is FALSE on the sim.
        // This is the correct, honest result, not a failure. The named mutation "force
        // isSourceTypeAvailable true on the sim" reds THIS test (it reads the real FALSE).
        _ = bridge.hostCallBegin(1, BnHostCallOp.camera, "{\"action\":\"check\"}")
        XCTAssertEqual(capturedStatuses(), [BnCameraStatus.unavailable],
                       "the simulator has no camera — check MUST honestly report Unavailable")
    }

    func testCaptureOnTheSimulatorIsUnavailableNoCameraNoHang() {
        installCapture()
        let bridge = AppleShellBridge()
        // NO override — a real capture on the sim cannot present a camera; it is Unavailable,
        // DATA (no path), never a hang. The availability gate is checked BEFORE permission.
        _ = bridge.hostCallBegin(2, BnHostCallOp.camera, "{\"action\":\"capture\"}")
        XCTAssertEqual(capturedStatuses(), [BnCameraStatus.unavailable])
        XCTAssertNil(lastPayload(), "Unavailable carries no path")
    }

    // ── check with a camera present (the seam) → Captured ("present + usable") ────────────

    func testCheckReportsCapturedWhenACameraIsAvailable() {
        installCapture()
        let bridge = AppleShellBridge()
        BnCamera.sourceTypeAvailableOverrideForTest = { true }
        _ = bridge.hostCallBegin(3, BnHostCallOp.camera, "{\"action\":\"check\"}")
        // present + usable — the Android cameraAvailabilityStatus CAPTURED twin (no capture ran).
        XCTAssertEqual(capturedStatuses(), [BnCameraStatus.captured])
        XCTAssertNil(lastPayload(), "check carries no payload")
    }

    // ── (b) THE SEAM CAPTURE: a canned image → a file → BnImage measures it (the composition) ─

    func testTheSeamCaptureWritesAFileWhosePathBnImageMeasures() throws {
        installCapture()
        openTheCaptureGatesUnderTheSeam()
        let bridge = AppleShellBridge()

        _ = bridge.hostCallBegin(10, BnHostCallOp.camera, "{\"action\":\"capture\",\"maxDim\":\"2048\",\"quality\":\"85\"}")
        XCTAssertTrue(bridge.camera.hasInFlightRequestForTest(), "the capture awaits the shutter (the seam)")
        // The user "shoots" — a canned 48×64 photo runs the REAL processing path.
        bridge.camera.fireDidFinishForTest(cannedImage(width: 48, height: 64))

        XCTAssertEqual(capturedStatuses(), [BnCameraStatus.captured])
        let payload = try XCTUnwrap(lastPayload(), "Captured must carry the {\"path\",…} payload")
        let fields = try XCTUnwrap(BnFlatJson.parseObject(payload))

        // The payload NAMES the file (a path), it does not carry the bytes — design §2.
        let pathString = try XCTUnwrap(fields["path"], "the payload's `path` key")
        XCTAssertTrue(pathString.hasPrefix("file://"), "the path is a file:// URL — a valid BnImage.Src")
        XCTAssertEqual(fields["width"], "48")
        XCTAssertEqual(fields["height"], "64")
        let bytes = try XCTUnwrap(fields["bytes"].flatMap { Int($0) })
        XCTAssertGreaterThan(bytes, 0, "the file the path names has real bytes")

        // THE COMPOSITION: the path names a real file on disk, and BnImage MEASURES it — read
        // through BnImageLoader.naturalPixelSize, the EXACT function the image node's Yoga
        // measure func calls (the capture → file → BnImage compose the M7 image component).
        let fsPath = try XCTUnwrap(URL(string: pathString)?.path, "the file:// URL resolves to a filesystem path")
        XCTAssertTrue(FileManager.default.fileExists(atPath: fsPath), "the captured file exists on disk")
        XCTAssertEqual(Int((try Data(contentsOf: URL(fileURLWithPath: fsPath))).count), bytes,
                       "the payload's `bytes` is the file's real size")
        let decoded = try XCTUnwrap(UIImage(contentsOfFile: fsPath), "the captured file decodes")
        let natural = try XCTUnwrap(BnImageLoader.naturalPixelSize(of: decoded), "BnImage reads a natural size")
        XCTAssertEqual(natural.width, 48, accuracy: 0.5)
        XCTAssertEqual(natural.height, 64, accuracy: 0.5)

        try? FileManager.default.removeItem(atPath: fsPath) // the app owns the file after handoff
    }

    // ── The cancel path → Cancelled, no path, no hang ─────────────────────────────────────

    func testCancelIsCancelledWithNoPathNoHang() {
        installCapture()
        openTheCaptureGatesUnderTheSeam()
        let bridge = AppleShellBridge()

        _ = bridge.hostCallBegin(11, BnHostCallOp.camera, "{\"action\":\"capture\"}")
        XCTAssertTrue(bridge.camera.hasInFlightRequestForTest())
        bridge.camera.fireDidCancelForTest() // the user backs out of the camera UI

        // Cancelled is DATA — a normal outcome, never a path, never a hang. The named mutation
        // "didCancel mapped to Error not Cancelled" reds here.
        XCTAssertEqual(capturedStatuses(), [BnCameraStatus.cancelled])
        XCTAssertNil(lastPayload(), "a cancel carries no path")
        XCTAssertFalse(bridge.camera.hasInFlightRequestForTest(), "the in-flight slot is consumed (no hang)")
    }

    // ── (b) EXIF/orientation NORMALIZE: a rotated input → upright pixels ───────────────────

    func testOrientationIsNormalizedToUpright() throws {
        installCapture()
        openTheCaptureGatesUnderTheSeam()
        let bridge = AppleShellBridge()

        _ = bridge.hostCallBegin(12, BnHostCallOp.camera, "{\"action\":\"capture\"}")
        // A 40×60-pixel buffer flagged `.right` DISPLAYS as 60×40 (portrait shot stored
        // landscape + "rotate" — the common phone case). The shell must BAKE the rotation so
        // the written pixels are upright: the reported dims are the DISPLAY dims (60×40), not
        // the raw buffer's (40×60). The named mutation "skip normalize" leaves 40×60 → reds.
        bridge.camera.fireDidFinishForTest(cannedImage(width: 40, height: 60, orientation: .right))

        XCTAssertEqual(capturedStatuses(), [BnCameraStatus.captured])
        let fields = try XCTUnwrap(BnFlatJson.parseObject(try XCTUnwrap(lastPayload())))
        XCTAssertEqual(fields["width"], "60", "orientation baked → the long edge is upright width")
        XCTAssertEqual(fields["height"], "40")

        // And the file on disk is upright too (a raw-file consumer needs no EXIF literacy).
        let fsPath = try XCTUnwrap(URL(string: try XCTUnwrap(fields["path"]))?.path)
        let decoded = try XCTUnwrap(UIImage(contentsOfFile: fsPath))
        let natural = try XCTUnwrap(BnImageLoader.naturalPixelSize(of: decoded))
        XCTAssertEqual(natural.width, 60, accuracy: 0.5)
        XCTAssertEqual(natural.height, 40, accuracy: 0.5)
        try? FileManager.default.removeItem(atPath: fsPath)
    }

    // ── The downscale bounds the long edge to maxDim ──────────────────────────────────────

    func testADownscaleBoundsTheLongEdge() throws {
        installCapture()
        openTheCaptureGatesUnderTheSeam()
        let bridge = AppleShellBridge()

        // maxDim 100 against a 400×200 capture → the long edge is bounded to 100 (aspect kept:
        // 100×50). The reported dims are the FINAL file's (design §2d), so the app + BnImage
        // measure reality, not the sensor's raw size.
        _ = bridge.hostCallBegin(13, BnHostCallOp.camera, "{\"action\":\"capture\",\"maxDim\":\"100\",\"quality\":\"85\"}")
        bridge.camera.fireDidFinishForTest(cannedImage(width: 400, height: 200))

        XCTAssertEqual(capturedStatuses(), [BnCameraStatus.captured])
        let fields = try XCTUnwrap(BnFlatJson.parseObject(try XCTUnwrap(lastPayload())))
        XCTAssertEqual(fields["width"], "100", "the long edge is bounded to maxDim")
        XCTAssertEqual(fields["height"], "50", "aspect ratio preserved")
        let fsPath = try XCTUnwrap(URL(string: try XCTUnwrap(fields["path"]))?.path)
        try? FileManager.default.removeItem(atPath: fsPath)
    }

    // ── The permission matrix (the AVAuthorizationStatus seam) — denial is DATA ────────────

    func testDeniedPermissionIsDenied() {
        installCapture()
        let bridge = AppleShellBridge()
        BnCamera.sourceTypeAvailableOverrideForTest = { true } // a camera is present…
        BnCamera.cameraAuthorizationStatusOverrideForTest = { .denied } // …but access is refused
        _ = bridge.hostCallBegin(14, BnHostCallOp.camera, "{\"action\":\"capture\"}")
        // The OS refused camera access — Denied, DATA (no path), never a thrown Swift error.
        XCTAssertEqual(capturedStatuses(), [BnCameraStatus.denied])
        XCTAssertNil(lastPayload())
    }

    func testRestrictedPermissionIsUnavailable() {
        installCapture()
        let bridge = AppleShellBridge()
        BnCamera.sourceTypeAvailableOverrideForTest = { true }
        BnCamera.cameraAuthorizationStatusOverrideForTest = { .restricted }
        _ = bridge.hostCallBegin(15, BnHostCallOp.camera, "{\"action\":\"capture\"}")
        // A restriction the user CANNOT lift (parental controls / MDM) — no usable camera for
        // this app → Unavailable (design §9 Gate 3).
        XCTAssertEqual(capturedStatuses(), [BnCameraStatus.unavailable])
    }

    // ── Error (4) is DATA: an unknown action never crashes ────────────────────────────────

    func testUnknownActionCompletesErrorAsData() {
        installCapture()
        let bridge = AppleShellBridge()
        let rc = bridge.hostCallBegin(16, BnHostCallOp.camera, "{\"action\":\"frobnicate\"}")
        XCTAssertEqual(rc, 0, "begin returns synchronously even for an unknown action")
        XCTAssertEqual(capturedStatuses(), [BnCameraStatus.error])
    }

    // ── The op routes to the camera handler (op=4, not geolocation/…/secureStorage) ───────

    func testHostCallBeginRoutesTheCameraOpToTheHandler() {
        installCapture()
        let bridge = AppleShellBridge()
        BnCamera.sourceTypeAvailableOverrideForTest = { true }
        // op=4 must reach BnCamera — the completeHook installed here is BnCamera's alone, so
        // its FIRING at all proves the route (a mis-route to geolocation(0)/…/secureStorage(3)
        // would use THAT handler's nil hook and this one would never fire → captured empty).
        _ = bridge.hostCallBegin(17, BnHostCallOp.camera, "{\"action\":\"check\"}")
        XCTAssertEqual(capturedStatuses(), [BnCameraStatus.captured])
    }

    // ── The wire status enum + op value — the three-way mirror pinned (ABI UNCHANGED) ─────

    func testTheWireStatusConstantsMatchTheThreeWayContract() {
        // The EXACT integers .NET CameraStatus / Kotlin CameraStatus carry (FIVE).
        XCTAssertEqual(BnCameraStatus.captured, 0)
        XCTAssertEqual(BnCameraStatus.cancelled, 1)
        XCTAssertEqual(BnCameraStatus.denied, 2)
        XCTAssertEqual(BnCameraStatus.unavailable, 3)
        XCTAssertEqual(BnCameraStatus.error, 4)
        // The op value — wire vocabulary on the existing int op field (no struct grow, no new
        // export; BnDriftTests still pins 80 bytes / offset 72 / 10 exports UNMOVED).
        XCTAssertEqual(BnHostCallOp.camera, 4)
    }

    // ── Picker + delegate RETENTION across the capture (the weak-delegate lesson) ─────────

    func testThePickerAndDelegateAreRetainedDuringCaptureThenReleased() {
        installCapture()
        openTheCaptureGatesUnderTheSeam()
        let bridge = AppleShellBridge()

        _ = bridge.hostCallBegin(18, BnHostCallOp.camera, "{\"action\":\"capture\"}")
        XCTAssertTrue(bridge.camera.hasInFlightRequestForTest(), "the request awaits the shutter")
        XCTAssertTrue(bridge.camera.pickerAndDelegateRetainedForTest(),
                      "the picker + its (weakly-held) delegate must be retained for the call's duration")
        XCTAssertTrue(capturedStatuses().isEmpty, "no completion before the shutter")

        bridge.camera.fireDidFinishForTest(cannedImage(width: 16, height: 16))
        XCTAssertEqual(capturedStatuses(), [BnCameraStatus.captured])
        XCTAssertFalse(bridge.camera.pickerAndDelegateRetainedForTest(), "released on completion")
        XCTAssertFalse(bridge.camera.hasInFlightRequestForTest())
        if let p = lastPayload(), let f = BnFlatJson.parseObject(p), let path = f["path"],
           let fs = URL(string: path)?.path { try? FileManager.default.removeItem(atPath: fs) }
    }

    // ── BOOT: the round trip through the REAL host_call_complete (the /camera demo) ───────

    func testCaptureBootRoundTripsCapturedThroughTheRealHostCallComplete() throws {
        openTheCaptureGatesUnderTheSeam()
        let form = try bootCameraDemo()
        let camera = try XCTUnwrap(runtime?.bridge.camera)

        try tapButton("Take Photo", in: form)
        XCTAssertTrue(pollUntil(deadline: 30) { camera.hasInFlightRequestForTest() },
                      "Take Photo never reached the camera op (the tap did not round-trip to op=Camera)")
        camera.fireDidFinishForTest(cannedImage(width: 48, height: 64)) // the seam "shutter"

        // The captured path round-trips the FINAL dims + size through the REAL
        // host_call_complete into a LIVE .NET continuation — the echo shows "captured:48x64:…".
        XCTAssertTrue(pollUntil { self.echoLabel()?.text?.hasPrefix("captured:48x64:") == true },
                      "Take Photo never round-tripped Captured to the echo (a hang or mis-route)")
        XCTAssertEqual(BnCamera.lastHostCallCompleteRcForTest, 0,
                       "host_call_complete did not route to the in-flight .NET requestId")
    }

    func testCaptureBootCancelIsDataWithinABoundedAwaitNoHang() throws {
        openTheCaptureGatesUnderTheSeam()
        let form = try bootCameraDemo()
        let camera = try XCTUnwrap(runtime?.bridge.camera)

        try tapButton("Take Photo", in: form)
        XCTAssertTrue(pollUntil(deadline: 30) { camera.hasInFlightRequestForTest() },
                      "Take Photo never reached the camera op")
        camera.fireDidCancelForTest() // the user backs out (the camera UI is owner-device territory)

        // The awaiting .NET ValueTask resolves to a Cancelled the echo shows — bounded await.
        // A HANG (cancel thrown/dropped) times this poll out and reddens.
        XCTAssertTrue(pollUntil { self.echoLabel()?.text == "status:Cancelled" },
                      "a cancelled capture never reached the echo within the bounded await (a HANG)")
        XCTAssertEqual(BnCamera.lastHostCallCompleteRcForTest, 0)
    }

    // ── Boot + tree accessors (the BnBiometricsTests house style) ─────────────────────────

    struct BootTimeout: Error {}

    private func bootCameraDemo() throws -> UIView {
        root = UIView(frame: CGRect(x: 0, y: 0, width: 390, height: 844))
        let mapper = bnMapper(root: root)
        let rt = BnRuntime(mapper: mapper)
        rt.onError = { msg, err in NSLog("[BnCameraTests] \(msg): \(err)") }
        self.runtime = rt
        try rt.start(component: "BnCameraDemo", os: "ios")
        guard pollUntil(deadline: 30, { self.probeForm() != nil }), let form = probeForm() else {
            XCTFail("BnCameraDemo never rendered its Take Photo / Check / BnImage / echo tree within 30s")
            throw BootTimeout()
        }
        return form
    }

    /// The demo root div: root's single child with 4 children (2 buttons + BnImage + echo).
    private func probeForm() -> UIView? {
        guard let form = root.subviews.first, form.subviews.count >= 4 else { return nil }
        return form
    }

    private func echoLabel() -> UILabel? {
        probeForm()?.subviews.first { $0 is UILabel } as? UILabel
    }

    private func tapButton(_ title: String, in view: UIView,
                           file: StaticString = #filePath, line: UInt = #line) throws {
        let button = try XCTUnwrap(findButton(in: view, title: title),
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

    private func pollUntil(deadline seconds: TimeInterval = 10, _ cond: () -> Bool) -> Bool {
        let end = Date().addingTimeInterval(seconds)
        while Date() < end {
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.05))
            if cond() { return true }
        }
        return cond()
    }
}
