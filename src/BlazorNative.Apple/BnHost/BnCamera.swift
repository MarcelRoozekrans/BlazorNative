// ─────────────────────────────────────────────────────────────────────────────
// BnCamera — Phase 9.3 Gate 3 (M9 DoD #5): the iOS half of camera photo capture, the
// mirror of AndroidShellBridge.handleCamera's ACTION_IMAGE_CAPTURE flow. The FOURTH
// reuse of the 9.0 generic ABI on iOS (after notifications, biometrics, secure
// storage): op=Camera rides the SAME `AppleShellBridge.hostCallBegin` slot (offset 72)
// geolocation opened, so the bridge stays 80 bytes / 10 exports — no struct grow, no
// new export. Camera adds an op-enum value (=4) + a wire-mirrored status enum + a host
// handler, and touches the ABI at NOTHING else.
//
// THE IMAGE CROSSES AS A PATH, NOT BYTES (design §2 — the phase's headline). The
// captured `UIImage` is normalized (§2e), downscaled to `maxDim` (§2d), JPEG-encoded at
// `quality`, and written to a temp file; the completion payload carries
// {"path":"file:///…","width":…,"height":…,"bytes":…} — a few hundred bytes NAMING the
// file, never the megabytes. The bytes stay on disk (the OS wrote them there) and .NET
// is handed their address, so the wire — and the ABI — stay small. The path IS a valid
// `BnImage.Src` (Kingfisher loads locals), so the capture composes into the M7 image
// component for free (BnCameraDemo / the composition test).
//
// DENIAL IS DATA (the milestone law, restated for the camera): the terminal outcome
// ALWAYS returns as a wire-mirrored `BnCameraStatus` via `blazornative_host_call_complete`
// — never a Swift error thrown across the C boundary, never a dropped completion (a
// hang). A user cancel (Cancelled), a denied permission (Denied), no camera hardware
// (Unavailable), a restriction (Unavailable), a write/encode failure or malformed args
// (Error) are all VALUES; every branch calls `complete(...)` exactly once, so the
// awaiting .NET ValueTask always resolves within a bounded await. Only `Captured` carries
// a payload (the path); every other status carries nil.
//
// DELEGATE RETENTION (the CLLocationManager / UNUserNotificationCenter weak-delegate
// lesson from 9.0/9.1, restated for UIImagePickerController): `picker.delegate` is a WEAK
// reference. So the bridge holds THIS handler strongly (AppleShellBridge.camera,
// app-lifetime), and this handler holds BOTH the `UIImagePickerController` AND its
// delegate object (`inFlightPicker` / `inFlightDelegate`) for the whole capture duration
// — a per-call delegate would be deallocated the instant `present` returns, and the
// picker's `didFinishPickingMediaWithInfo` / `didCancel` would then fire into nothing (a
// hang). Cleared by `complete`.
//
// THE SIMULATOR HAS NO CAMERA — a NEW honesty shape, worse than 9.2's no-Secure-Enclave.
// `UIImagePickerController.isSourceTypeAvailable(.camera)` is FALSE on the simulator
// (there is no camera to present), so:
//   (a) `check → Unavailable` is the HONEST simulator result, asserted AS CORRECT
//       behaviour (not smoothed over) — and a bare `capture` on the sim is Unavailable too.
//   (b) The real capture round-trip (present the camera UI → shoot → a UIImage) is NOT
//       drivable in CI at all, so it is SEAM-DRIVEN: `sourceTypeAvailableOverrideForTest`
//       + `cameraAuthorizationStatusOverrideForTest` open the two gates, and
//       `suppressSystemCameraPresentForTest` skips the un-presentable picker while the
//       test fires the REAL delegate (`fireDidFinishForTest` / `fireDidCancelForTest`,
//       the geolocation hand-rolled-fire pattern) with a canned UIImage. CI then asserts
//       the file→BnImage contract + the wire deterministically.
//   (c) The real camera UI + a real sensor capture are DOUBLY UNPROVEN — no sim camera AND
//       no Apple Developer account (iOS real-device DEFERRED). Named, not smuggled.
// The production `UIImagePickerController` / `AVCaptureDevice` calls are unsuppressed and
// untouched.
// ─────────────────────────────────────────────────────────────────────────────

import AVFoundation
import UIKit

/// The wire-mirrored camera status (mirror of .NET CameraStatus / Kotlin CameraStatus,
/// byte-identical — FIVE values). Cancellation (1), denial (2), unavailability (3) and
/// error (4) are all VALUES the awaiting .NET ValueTask resolves to — never exceptions,
/// never hangs. Do NOT reorder — the integer IS the ABI contract.
enum BnCameraStatus {
    static let captured: Int32 = 0     // a photo was taken and written; the payload carries the path
    static let cancelled: Int32 = 1    // the user backed out of the camera UI (no path)
    static let denied: Int32 = 2       // the OS refused camera access (AVAuthorizationStatus.denied)
    static let unavailable: Int32 = 3  // no camera hardware / capture UI unavailable (the honest sim result)
    static let error: Int32 = 4        // unexpected host error (a caught throw, a write/encode failure, bad args)
}

/// The capture options ridden on the flat-JSON args (numbers string-encoded — the twin of
/// .NET CaptureOptions / Kotlin CaptureOptions). `maxDim` bounds the LONG edge of the
/// written file (0 = keep full resolution); `quality` is the JPEG re-encode quality (1..100).
struct BnCaptureOptions {
    let maxDim: Int
    let quality: Int
    static let defaultMaxDim = 2048
    static let defaultQuality = 85
}

final class BnCamera {

    private let lock = NSLock()

    /// The single in-flight requestId (one capture per op — the one-in-flight rule).
    /// `complete(...)` consumes it (one-shot), so a late/duplicate delegate callback finds
    /// it cleared and no-ops.
    private var inFlightRequestId: Int64?
    private var inFlightOptions: BnCaptureOptions?

    /// STRONG. The presented picker + its delegate, held for the capture's duration so a
    /// deallocated delegate can never orphan the `didFinishPickingMediaWithInfo` / `didCancel`
    /// callback (the weak-delegate retention lesson — see the file header). Cleared by `complete`.
    private var inFlightPicker: UIImagePickerController?
    private var inFlightDelegate: BnCameraPickerDelegate?

    // ── Test seams (static, reset in teardown — the BnGeolocation/BnBiometrics twins) ─────

    /// Overrides `UIImagePickerController.isSourceTypeAvailable(.camera)` so a hosted test
    /// can OPEN the availability gate the sim keeps shut (there is no camera). Null in
    /// production → the real availability is asked (FALSE on the simulator — the honest
    /// `check → Unavailable`). The named mutation "force isSourceTypeAvailable true" reds
    /// the honesty test precisely because that test sets NO override.
    static var sourceTypeAvailableOverrideForTest: (() -> Bool)?

    /// Overrides the read of `AVCaptureDevice.authorizationStatus(for: .video)` so a hosted
    /// test drives the permission branch deterministically (the sim's real TCC state is not
    /// drivable, and `.notDetermined` would pop the un-drivable system alert). Null in
    /// production → the real status is read.
    static var cameraAuthorizationStatusOverrideForTest: (() -> AVAuthorizationStatus)?

    /// Overrides `AVCaptureDevice.requestAccess(for: .video)` (the `.notDetermined` prompt)
    /// so a hosted test drives the grant/deny outcome without the un-drivable system alert
    /// (owner-device territory). Must call the reply with `granted`. Null in production →
    /// the real `requestAccess` alert is presented.
    static var requestAccessReplyOverrideForTest: ((_ reply: @escaping (Bool) -> Void) -> Void)?

    /// When true, `presentPicker` records the in-flight capture (and RETAINS the picker +
    /// delegate — the retention contract holds on the seam path too) but SKIPS the actual
    /// `present(...)` — the sim cannot present a `.camera` picker. The test then fires the
    /// REAL delegate (`fireDidFinishForTest` / `fireDidCancelForTest`). Null/false in
    /// production. Reset in teardown so it never leaks.
    static var suppressSystemCameraPresentForTest = false

    /// Intercepts the completion so a PURE unit test (no NativeAOT boot) observes the routed
    /// (requestId, status, payload) without a live .NET continuation. Null in production →
    /// the real `blazornative_host_call_complete` export is called.
    static var completeHookForTest: ((Int64, Int32, String?) -> Int32)?

    /// The rc of the most recent `blazornative_host_call_complete` — 0 = delivered to a live
    /// .NET continuation (proves the deferred completion routed to the right process-scoped
    /// id), 1 = unknown/already-completed id (benign). Int32.min before any completion. The
    /// BnGeolocation/BnBiometrics twin.
    static var lastHostCallCompleteRcForTest: Int32 = Int32.min

    static func resetForTest() {
        sourceTypeAvailableOverrideForTest = nil
        cameraAuthorizationStatusOverrideForTest = nil
        requestAccessReplyOverrideForTest = nil
        suppressSystemCameraPresentForTest = false
        completeHookForTest = nil
        lastHostCallCompleteRcForTest = Int32.min
    }

    func clearInFlightForTest() {
        lock.lock(); inFlightRequestId = nil; inFlightOptions = nil
        inFlightPicker = nil; inFlightDelegate = nil; lock.unlock()
    }
    func hasInFlightRequestForTest() -> Bool { lock.lock(); defer { lock.unlock() }; return inFlightRequestId != nil }

    /// The picker + its delegate are held for the capture's duration — the retention pin as
    /// a PROPERTY (no observable otherwise sees the hold; the CLLocationManager precedent).
    /// The delegate must be the picker's own delegate (held WEAKLY by the picker, retained
    /// via this handler), or a `present` that returned would orphan the callback.
    func pickerAndDelegateRetainedForTest() -> Bool {
        lock.lock(); defer { lock.unlock() }
        guard let picker = inFlightPicker, let delegate = inFlightDelegate else { return false }
        return picker.delegate === delegate
    }

    // ── The op entry (AppleShellBridge.hostCallBegin forwards here for op=Camera) ──────────

    /// action=check is the read-only availability peek (never launches the camera UI — the
    /// geolocation `mode:check` / biometrics `action:check` sibling); action=capture presents
    /// the camera UI (or the seam) and maps its outcome to a status + a {"path",…} payload on
    /// Captured. maxDim/quality ride the flat JSON (numbers string-encoded). Returns FAST (the
    /// begin contract); the terminal status is a deferred `complete(...)`.
    func begin(requestId: Int64, argsJson: String) {
        let args = BnFlatJson.parseObject(argsJson) ?? [:]
        let action = args["action"] ?? "capture"
        switch action {
        case "check":
            onMain { self.complete(requestId, self.availabilityStatus(), nil) }
        case "capture":
            let options = BnCaptureOptions(
                maxDim: args["maxDim"].flatMap { Int($0) } ?? BnCaptureOptions.defaultMaxDim,
                quality: args["quality"].flatMap { Int($0) } ?? BnCaptureOptions.defaultQuality)
            capture(requestId: requestId, options: options)
        default:
            // An unknown action is DATA (Error), never a crash (the Kotlin posture).
            complete(requestId, BnCameraStatus.error, nil)
        }
    }

    /// An unknown host-call op: DATA (Error), not a crash — the awaiting .NET ValueTask
    /// resolves rather than leaking a pending entry (the geolocation unknown-op posture).
    func completeUnknownOp(requestId: Int64) {
        complete(requestId, BnCameraStatus.error, nil)
    }

    // ── Availability (the honest sim result) ──────────────────────────────────────────────

    /// Is a usable camera present? Captured ("present + usable" — no capture ran, mirroring
    /// Android's cameraAvailabilityStatus which returns CAPTURED when a camera app resolves)
    /// when `isSourceTypeAvailable(.camera)`; Unavailable (the honest no-camera answer, TRUE
    /// on the simulator) otherwise. Never prompts, never launches the camera UI.
    private func availabilityStatus() -> Int32 {
        isCameraAvailable() ? BnCameraStatus.captured : BnCameraStatus.unavailable
    }

    /// `UIImagePickerController.isSourceTypeAvailable(.camera)` (or the seam). FALSE on the
    /// simulator — there is no camera. The mutation "force this true on the sim" reds the
    /// honesty test (which sets NO override, so it reads the real FALSE).
    private func isCameraAvailable() -> Bool {
        if let override = Self.sourceTypeAvailableOverrideForTest { return override() }
        return UIImagePickerController.isSourceTypeAvailable(.camera)
    }

    // ── Capture ───────────────────────────────────────────────────────────────────────────

    /// The capture flow: availability gate → permission gate → present the picker. Each gate
    /// resolves to a STATUS (Unavailable / Denied), never a throw, never a hang. Runs on main
    /// (UIImagePickerController + AVCaptureDevice need it); in a hosted XCTest (main thread)
    /// this runs inline (deterministic), in production the off-main .NET lane hops.
    private func capture(requestId: Int64, options: BnCaptureOptions) {
        onMain { [weak self] in
            guard let self = self else { return }
            // Gate 1 — availability. The sim has NO camera → Unavailable, DATA (no path, no hang).
            guard self.isCameraAvailable() else {
                self.complete(requestId, BnCameraStatus.unavailable, nil)
                return
            }
            // Gate 2 — permission. Denied / restricted are DATA; a grant proceeds to the UI.
            self.ensureAuthorized { authorized, denialStatus in
                guard authorized else {
                    self.complete(requestId, denialStatus, nil)
                    return
                }
                self.presentPicker(requestId: requestId, options: options)
            }
        }
    }

    /// Maps `AVAuthorizationStatus(.video)` to (authorized, denialStatus). `.denied → Denied`,
    /// `.restricted → Unavailable` (design §9 Gate 3 — a restriction the user CANNOT lift is
    /// "no usable camera for this app", the Unavailable bucket); `.notDetermined` asks for
    /// access (the reply is DATA); `.authorized` proceeds. denialStatus is meaningful only
    /// when authorized is false.
    private func ensureAuthorized(_ completion: @escaping (Bool, Int32) -> Void) {
        switch currentAuthorizationStatus() {
        case .authorized:
            completion(true, BnCameraStatus.captured) // status unused on the authorized path
        case .denied:
            completion(false, BnCameraStatus.denied)
        case .restricted:
            completion(false, BnCameraStatus.unavailable)
        case .notDetermined:
            requestAccess { granted in
                completion(granted, granted ? BnCameraStatus.captured : BnCameraStatus.denied)
            }
        @unknown default:
            completion(false, BnCameraStatus.error)
        }
    }

    private func currentAuthorizationStatus() -> AVAuthorizationStatus {
        if let override = Self.cameraAuthorizationStatusOverrideForTest { return override() }
        return AVCaptureDevice.authorizationStatus(for: .video)
    }

    private func requestAccess(_ reply: @escaping (Bool) -> Void) {
        if let override = Self.requestAccessReplyOverrideForTest { override(reply); return }
        AVCaptureDevice.requestAccess(for: .video) { granted in
            // The completion may arrive off-main; the caller hops to main in presentPicker.
            reply(granted)
        }
    }

    /// Creates + RETAINS the picker and its delegate (the retention contract — see the file
    /// header) and presents the camera UI. On the seam path (`suppressSystemCameraPresent…`)
    /// the in-flight is recorded and the present is skipped (the sim cannot present a
    /// `.camera` picker); the test fires the delegate.
    private func presentPicker(requestId: Int64, options: BnCaptureOptions) {
        let delegate = BnCameraPickerDelegate(owner: self, requestId: requestId)
        let picker = UIImagePickerController()
        picker.delegate = delegate // held WEAKLY by the picker — retained via inFlightDelegate

        lock.lock()
        inFlightRequestId = requestId
        inFlightOptions = options
        inFlightPicker = picker
        inFlightDelegate = delegate
        lock.unlock()

        if Self.suppressSystemCameraPresentForTest { return } // the test fires the delegate

        picker.sourceType = .camera
        guard let presenter = Self.topPresenter() else {
            // No foreground VC to present on — Unavailable, DATA (never a crash).
            complete(requestId, BnCameraStatus.unavailable, nil)
            return
        }
        presenter.present(picker, animated: true)
    }

    // ── The delegate callbacks (the REAL delegate code, called by the picker OR the seam) ──

    /// The user shot a photo: extract the UIImage, process it (normalize + downscale + encode
    /// + write), and complete Captured with the {"path",…} payload — or Error if the image is
    /// missing or the write fails (still DATA). Dismisses the (real) picker.
    func didFinish(requestId: Int64, image: UIImage?) {
        dismissPicker()
        guard let image = image else {
            complete(requestId, BnCameraStatus.error, nil)
            return
        }
        let options = currentOptions()
        let outcome = processAndWrite(image, options)
        complete(requestId, outcome.status, outcome.payload)
    }

    /// The user backed out — Cancelled, no path, no hang. Dismisses the (real) picker.
    func didCancel(requestId: Int64) {
        dismissPicker()
        complete(requestId, BnCameraStatus.cancelled, nil)
    }

    // ── Test-only fires (hand-rolled — synthesize the trigger, run the REAL delegate) ──────

    func fireDidFinishForTest(_ image: UIImage) {
        guard let (delegate, picker) = liveDelegateAndPicker() else { return }
        let info: [UIImagePickerController.InfoKey: Any] = [.originalImage: image]
        delegate.imagePickerController(picker, didFinishPickingMediaWithInfo: info)
    }

    func fireDidCancelForTest() {
        guard let (delegate, picker) = liveDelegateAndPicker() else { return }
        delegate.imagePickerControllerDidCancel(picker)
    }

    private func liveDelegateAndPicker() -> (BnCameraPickerDelegate, UIImagePickerController)? {
        lock.lock(); defer { lock.unlock() }
        guard let d = inFlightDelegate, let p = inFlightPicker else { return nil }
        return (d, p)
    }

    // ── Image processing — the file-path handoff (§2d/§2e) ────────────────────────────────

    private struct CaptureOutcome {
        let status: Int32
        let payload: String?
    }

    /// The result-as-a-FILE processing path, the twin of AndroidShellBridge.processCaptureFile:
    /// NORMALIZE orientation (bake the UIImage's `imageOrientation` into upright pixels so a
    /// raw-file consumer needs no EXIF literacy AND Kingfisher — which honors EXIF on decode —
    /// does NOT rotate a second time), DOWNSCALE the long edge to `maxDim`, JPEG-encode at
    /// `quality`, and WRITE to the temp capture dir. Returns Captured + {"path",…} on success;
    /// any failure is Error (DATA), never a throw across the op dispatch.
    private func processAndWrite(_ image: UIImage, _ options: BnCaptureOptions) -> CaptureOutcome {
        pruneCaptureDir() // the leak backstop — keep-last-N, before every capture (design §2c)
        // Bake orientation FIRST (so the reported dims are upright), then bound the long edge.
        let upright = normalizedUpright(image)
        let scaled = downscaled(upright, maxDim: options.maxDim)
        guard let cg = scaled.cgImage else {
            BnLog.error("BnCamera", "processAndWrite: the captured image has no pixel buffer")
            return CaptureOutcome(status: BnCameraStatus.error, payload: nil)
        }
        let quality = CGFloat(min(max(options.quality, 1), 100)) / 100.0
        guard let jpeg = scaled.jpegData(compressionQuality: quality) else {
            BnLog.error("BnCamera", "processAndWrite: JPEG encode returned nil")
            return CaptureOutcome(status: BnCameraStatus.error, payload: nil)
        }
        do {
            let url = try writeCaptureFile(jpeg)
            let payload = BnFlatJson.object([
                ("path", url.absoluteString), // a proper file:// URL — a valid BnImage.Src (Kingfisher loads locals)
                ("width", String(cg.width)),  // FINAL (post-downscale) pixel dims — reality, not the sensor's raw
                ("height", String(cg.height)),
                ("bytes", String(jpeg.count)),
            ])
            return CaptureOutcome(status: BnCameraStatus.captured, payload: payload)
        } catch {
            BnLog.error("BnCamera", "processAndWrite: could not write the capture file: \(error)")
            return CaptureOutcome(status: BnCameraStatus.error, payload: nil)
        }
    }

    /// Bakes the UIImage's `imageOrientation` into upright pixels and returns an `.up` image.
    /// `image.size` already reports the DISPLAY size (orientation applied), so drawing into a
    /// context of that size produces upright pixels; `scale = 1` keeps one pixel = one point
    /// (the BnImageLoader unit rule — a captured photo must measure in raw pixels like
    /// Android's `bitmap.width`). An already-upright image is returned untouched.
    private func normalizedUpright(_ image: UIImage) -> UIImage {
        if image.imageOrientation == .up { return image }
        let format = UIGraphicsImageRendererFormat.default()
        format.scale = 1
        format.opaque = false
        let renderer = UIGraphicsImageRenderer(size: image.size, format: format)
        return renderer.image { _ in
            image.draw(in: CGRect(origin: .zero, size: image.size))
        }
    }

    /// Bounds the LONG edge to `maxDim` (0/negative → keep full resolution), preserving aspect.
    /// Redraws at `scale = 1` so the reported pixel dims equal the drawn size (the unit rule).
    private func downscaled(_ image: UIImage, maxDim: Int) -> UIImage {
        guard maxDim > 0, let cg = image.cgImage else { return image }
        let w = cg.width, h = cg.height
        let longEdge = max(w, h)
        guard longEdge > maxDim else { return image } // already within budget
        let factor = CGFloat(maxDim) / CGFloat(longEdge)
        let target = CGSize(width: (CGFloat(w) * factor).rounded(), height: (CGFloat(h) * factor).rounded())
        let format = UIGraphicsImageRendererFormat.default()
        format.scale = 1
        format.opaque = false
        let renderer = UIGraphicsImageRenderer(size: target, format: format)
        return renderer.image { _ in
            image.draw(in: CGRect(origin: .zero, size: target))
        }
    }

    /// Writes `jpeg` to a uniquely-named file under the capture subdir of the temp dir (cache
    /// semantics — OS-reclaimable, never backed up; design §2c). Returns its file:// URL.
    private func writeCaptureFile(_ jpeg: Data) throws -> URL {
        let dir = try captureDir()
        let url = dir.appendingPathComponent("capture-\(UUID().uuidString).jpg")
        try jpeg.write(to: url, options: .atomic)
        return url
    }

    /// `NSTemporaryDirectory()/blazornative_captures`, created on demand.
    private func captureDir() throws -> URL {
        let dir = URL(fileURLWithPath: NSTemporaryDirectory(), isDirectory: true)
            .appendingPathComponent("blazornative_captures", isDirectory: true)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir
    }

    /// The leak backstop (design §2c): keep the last N captures, delete the rest. A crashed or
    /// careless app cannot leak captures forever — a shell-side safety net, not a correctness
    /// guarantee the app can lean on (the app owns the file after handoff). Best-effort.
    private func pruneCaptureDir() {
        guard let dir = try? captureDir() else { return }
        let fm = FileManager.default
        guard let entries = try? fm.contentsOfDirectory(
            at: dir, includingPropertiesForKeys: [.contentModificationDateKey], options: []) else { return }
        let keepLast = 5
        guard entries.count > keepLast else { return }
        let sorted = entries.sorted { a, b in
            let da = (try? a.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast
            let db = (try? b.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast
            return da < db // oldest first
        }
        for url in sorted.prefix(sorted.count - keepLast) { try? fm.removeItem(at: url) }
    }

    // ── Internals ─────────────────────────────────────────────────────────────────────────

    private func currentOptions() -> BnCaptureOptions {
        lock.lock(); defer { lock.unlock() }
        return inFlightOptions ?? BnCaptureOptions(maxDim: BnCaptureOptions.defaultMaxDim, quality: BnCaptureOptions.defaultQuality)
    }

    private func dismissPicker() {
        // The real picker dismisses itself; the seam path never presented one. Best-effort on
        // main; never a crash if nothing is presented.
        guard !Self.suppressSystemCameraPresentForTest else { return }
        let picker = inFlightPicker
        onMain { picker?.presentingViewController?.dismiss(animated: true) }
    }

    /// The single completion funnel. Consumes the in-flight slot + releases the retained
    /// picker/delegate iff the id still matches (one-shot — a duplicate/late callback finds it
    /// cleared and its export call takes the unknown-id path, rc 1), then delivers the status
    /// to .NET. The payload (a NUL-terminated UTF-8 C string) is non-nil only for Captured.
    private func complete(_ requestId: Int64, _ status: Int32, _ payload: String?) {
        lock.lock()
        if inFlightRequestId == requestId {
            inFlightRequestId = nil
            inFlightOptions = nil
            inFlightPicker = nil
            inFlightDelegate = nil
        }
        lock.unlock()

        let rc: Int32
        if let hook = Self.completeHookForTest {
            rc = hook(requestId, status, payload)
        } else if let payload = payload {
            rc = payload.withCString { blazornative_host_call_complete(requestId, status, $0) }
        } else {
            rc = blazornative_host_call_complete(requestId, status, nil)
        }
        Self.lastHostCallCompleteRcForTest = rc
    }

    private func onMain(_ work: @escaping () -> Void) {
        if Thread.isMainThread { work() } else { DispatchQueue.main.async(execute: work) }
    }

    /// The top-most presenting view controller (key window's root, walking any presented
    /// chain) — the twin of AppleShellBridge.topPresenter. nil when no foreground window
    /// exists (capture then completes Unavailable, never a crash).
    private static func topPresenter() -> UIViewController? {
        let keyWindow = UIApplication.shared.connectedScenes
            .compactMap { $0 as? UIWindowScene }
            .flatMap { $0.windows }
            .first { $0.isKeyWindow }
        var top = keyWindow?.rootViewController
        while let presented = top?.presentedViewController { top = presented }
        return top
    }
}

/// The per-capture `UIImagePickerControllerDelegate` (also a `UINavigationControllerDelegate`,
/// which the protocol requires). Held STRONGLY by `BnCamera.inFlightDelegate` for the capture's
/// duration — the picker's own `delegate` is WEAK, so without this hold the callback would fire
/// into a deallocated object (a hang). Routes the two outcomes back to the owner by requestId.
final class BnCameraPickerDelegate: NSObject, UIImagePickerControllerDelegate, UINavigationControllerDelegate {

    private weak var owner: BnCamera?
    private let requestId: Int64

    init(owner: BnCamera, requestId: Int64) {
        self.owner = owner
        self.requestId = requestId
    }

    func imagePickerController(_ picker: UIImagePickerController,
                              didFinishPickingMediaWithInfo info: [UIImagePickerController.InfoKey: Any]) {
        // The camera source hands back the original still under `.originalImage`. An edited
        // image is not requested (allowsEditing defaults false); nil → Error, DATA.
        let image = info[.originalImage] as? UIImage
        owner?.didFinish(requestId: requestId, image: image)
    }

    func imagePickerControllerDidCancel(_ picker: UIImagePickerController) {
        owner?.didCancel(requestId: requestId)
    }
}
