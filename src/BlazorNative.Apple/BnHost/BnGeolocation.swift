// ─────────────────────────────────────────────────────────────────────────────
// BnGeolocation — Phase 9.0 Gate 3 (M9 DoD #1 + #2): the iOS half of the permission
// pattern + geolocation, the mirror of AndroidShellBridge's LocationManager flow.
// The generic permission-gated begin (AppleShellBridge.hostCallBegin, op=Geolocation)
// resolves here, and the terminal outcome ALWAYS returns as a wire-mirrored tri-state
// STATUS via `blazornative_host_call_complete` — a grant carries the flat-JSON fix, a
// denial / restriction / no-fix / error is a status integer with a NULL payload.
//
// DENIAL IS DATA (the milestone law): a "no" is never a Swift error thrown across the
// C boundary and never a dropped completion (a hang). Every branch — check, grant,
// denial, restriction, unavailable, error — calls `complete(...)` exactly once, so the
// awaiting .NET ValueTask always resolves.
//
// DELEGATE RETENTION (the 5.3/7.3 iOS-shell lesson, restated for CoreLocation):
// `CLLocationManager.delegate` is a WEAK reference. So the bridge holds THIS handler
// strongly (AppleShellBridge.geolocation, app-lifetime), and this handler holds the
// `CLLocationManager` strongly — neither the manager nor its delegate can be
// deallocated while the async authorization prompt + fix are in flight. A per-call
// manager or a locally-scoped delegate would be gone before the callback fires.
//
// THE ASYNC-SUSPENSION WRINKLE (named + handled): iOS does not recreate the app for
// the permission alert the way Android recreates the Activity, but `CLLocationManager`
// authorization is asynchronous — `requestWhenInUseAuthorization()` returns immediately
// and the delegate resumes LATER, possibly after the app was backgrounded. The pending
// .NET registry is PROCESS-scoped (Gate 1) and survives; the Swift side holds the
// manager + delegate + the in-flight requestId for the whole duration, so the deferred
// completion routes back to the RIGHT .NET call (proven by host_call_complete rc 0 into
// a live continuation — the iOS analogue of Android's recreation-survival concern).
//
// SIMULATOR-SCOPED + LABELLED (the M5→M8 honesty posture): a hosted XCTest cannot drive
// the real system authorization ALERT (owner-device territory, exactly as Android's real
// dialog is owner-phone territory) and a simulator "location" is a set coordinate, not a
// GPS fix. So the shell exposes seams — an authorization-status OVERRIDE, a switch that
// SUPPRESSES the real CoreLocation calls, and a completion HOOK — and the tests drive the
// delegate directly (the hand-rolled-fire pattern of 7.3/7.4: synthesize the trigger, run
// the REAL delegate code). Production paths (requestWhenInUseAuthorization / requestLocation)
// are unsuppressed and untouched.
// ─────────────────────────────────────────────────────────────────────────────

import CoreLocation
import Foundation

/// The generic host-call op enum (mirror of NativeShellBridge.HostCallOp / Kotlin
/// HostCallOp). 9.0 wires exactly one op; the shape is capability-agnostic.
enum BnHostCallOp {
    static let geolocation: Int32 = 0
}

/// The wire-mirrored tri-state status (mirror of GeolocationStatus / Kotlin
/// HostCallStatus, byte-identical): denial (1/2/3), unavailability (4) and error (5)
/// are all VALUES the awaiting .NET ValueTask resolves to — never exceptions, never hangs.
enum BnHostCallStatus {
    static let granted: Int32 = 0            // permission held; a fix was obtained (payload = the fix)
    static let denied: Int32 = 1             // denied THIS time; a later request MAY prompt again
    static let deniedPermanently: Int32 = 2  // iOS .denied after its one prompt — only Settings changes it
    static let restricted: Int32 = 3         // iOS .restricted (parental controls / MDM) — the user CANNOT grant it
    static let locationUnavailable: Int32 = 4 // permission fine, but services off / no fix
    static let error: Int32 = 5              // unexpected host error (a caught throw / unknown state)
}

final class BnGeolocation: NSObject, CLLocationManagerDelegate {

    // ── The retained CoreLocation pair (delegate retention — see the file header) ──

    /// STRONG. `CLLocationManager`'s delegate is weak; this handler is itself that
    /// delegate, and the bridge holds this handler for the app lifetime, so holding the
    /// manager here keeps the whole chain alive across the async prompt + fix.
    private var manager: CLLocationManager?

    private let lock = NSLock()

    /// The single in-flight requestId (one geolocation call per op — the design's
    /// one-in-flight rule; a queue is later work). The authorization + fix delegate
    /// callbacks route their completion back to THIS id. `complete(...)` consumes it
    /// (one-shot), so a late/duplicate callback finds it cleared and no-ops.
    private var inFlightRequestId: Int64?

    // ── Test seams (static, reset in teardown — the AndroidShellBridge companion twins) ──

    /// Overrides the read of the current CLAuthorizationStatus so a hosted test drives a
    /// branch deterministically (the simulator's real TCC state is not drivable). Null in
    /// production → the real manager's status is read.
    static var authorizationStatusOverrideForTest: (() -> CLAuthorizationStatus)?

    /// When true, `beginFix`/`beginAuthorization` SKIP the real CoreLocation calls
    /// (requestLocation / requestWhenInUseAuthorization) — the real system alert is
    /// owner-device territory and a real fix never arrives on the sim; the test fires the
    /// delegate itself. Null/false in production. Reset in teardown so it never leaks.
    static var suppressSystemLocationCallsForTest = false

    /// Intercepts the completion so a PURE unit test (no NativeAOT boot) observes the
    /// routed (requestId, status, payload) without a live .NET continuation. Null in
    /// production → the real `blazornative_host_call_complete` export is called.
    static var completeHookForTest: ((Int64, Int32, String?) -> Int32)?

    /// The return code of the most recent `blazornative_host_call_complete` — 0 =
    /// delivered to a live .NET continuation (proves the deferred completion routed to
    /// the right process-scoped id), 1 = unknown/already-completed id (benign). Int32.min
    /// before any completion. The Android `lastHostCallCompleteRcForTest` twin.
    static var lastHostCallCompleteRcForTest: Int32 = Int32.min

    /// Test seam: drain the in-flight slot + reset the probes/seams between tests so a
    /// leftover request never routes a later test's fire.
    static func resetForTest() {
        authorizationStatusOverrideForTest = nil
        suppressSystemLocationCallsForTest = false
        completeHookForTest = nil
        lastHostCallCompleteRcForTest = Int32.min
    }

    func clearInFlightForTest() { lock.lock(); inFlightRequestId = nil; lock.unlock() }
    func hasInFlightRequestForTest() -> Bool { lock.lock(); defer { lock.unlock() }; return inFlightRequestId != nil }
    /// The live manager exists and this handler is its delegate — the retention pin as a
    /// PROPERTY (no observable can otherwise see that the delegate is held; the 6.2
    /// contentInsetAdjustmentBehavior precedent).
    func managerDelegateIsRetainedForTest() -> Bool {
        guard let m = manager else { return false }
        return m.delegate === self
    }

    // ── The op entry (AppleShellBridge.hostCallBegin forwards here for op=Geolocation) ──

    /// mode=request runs the whole request-then-fetch dance (may prompt); mode=check is
    /// the read-only permission peek (never prompts). Returns FAST (the begin contract) —
    /// the terminal outcome is a deferred `complete(...)`. The requestId is recorded
    /// synchronously for the request path so a completion can never precede the record.
    func begin(requestId: Int64, argsJson: String) {
        let mode = BnFlatJson.parseObject(argsJson)?["mode"] ?? "request"
        if mode == "request" {
            lock.lock(); inFlightRequestId = requestId; lock.unlock()
        }
        // CLLocationManager creation + calls need a thread with a run loop → main. In a
        // hosted XCTest (main thread) this runs inline (deterministic); in production the
        // .NET dispatch lane is off-main, so it hops.
        onMain { [weak self] in
            guard let self = self else { return }
            let status = self.currentAuthorizationStatus()
            if mode == "check" {
                self.complete(requestId, self.checkStatus(status), nil)
                return
            }
            switch status {
            case .authorizedWhenInUse, .authorizedAlways:
                self.beginFix(requestId)
            case .notDetermined:
                self.beginAuthorization(requestId)
            case .denied:
                self.complete(requestId, BnHostCallStatus.deniedPermanently, nil) // iOS one-shot
            case .restricted:
                self.complete(requestId, BnHostCallStatus.restricted, nil)
            @unknown default:
                self.complete(requestId, BnHostCallStatus.error, nil)
            }
        }
    }

    /// An unknown host-call op: DATA, not a crash — complete with Error so the awaiting
    /// .NET ValueTask resolves rather than leaking a pending entry (the Android
    /// unknown-op posture). Not routed through the in-flight slot (never recorded).
    func completeUnknownOp(requestId: Int64) {
        complete(requestId, BnHostCallStatus.error, nil)
    }

    // ── The CLLocationManagerDelegate callbacks (the REAL delegate code) ──────────

    /// iOS 14+ authorization-change entry.
    @available(iOS 14.0, *)
    func locationManagerDidChangeAuthorization(_ manager: CLLocationManager) {
        handleAuthChange(manager.authorizationStatus)
    }

    /// iOS 13 authorization-change entry (deprecated on 14+ but still the drive point the
    /// hand-rolled fire uses, so both routes funnel to `handleAuthChange`).
    func locationManager(_ manager: CLLocationManager, didChangeAuthorization status: CLAuthorizationStatus) {
        handleAuthChange(status)
    }

    func locationManager(_ manager: CLLocationManager, didUpdateLocations locations: [CLLocation]) {
        guard let loc = locations.last, let requestId = currentRequestId() else { return }
        complete(requestId, BnHostCallStatus.granted, fixPayload(loc))
    }

    func locationManager(_ manager: CLLocationManager, didFailWithError error: Error) {
        guard let requestId = currentRequestId() else { return }
        complete(requestId, mapFailure(error), nil)
    }

    // ── Test-only fires (hand-rolled — synthesize the trigger, run the REAL delegate) ──

    func fireAuthorizationChangeForTest(_ status: CLAuthorizationStatus) {
        self.locationManager(ensureManager(), didChangeAuthorization: status)
    }

    func fireLocationFixForTest(_ location: CLLocation) {
        self.locationManager(ensureManager(), didUpdateLocations: [location])
    }

    func fireLocationFailureForTest(_ error: Error) {
        self.locationManager(ensureManager(), didFailWithError: error)
    }

    // ── Internals ─────────────────────────────────────────────────────────────────

    private func handleAuthChange(_ status: CLAuthorizationStatus) {
        guard let requestId = currentRequestId() else { return } // no request awaiting authorization
        switch status {
        case .notDetermined:
            return // the prompt has not resolved yet — keep waiting
        case .authorizedWhenInUse, .authorizedAlways:
            beginFix(requestId) // grant → fetch the fix, keeping the SAME in-flight id
        case .denied:
            complete(requestId, BnHostCallStatus.deniedPermanently, nil)
        case .restricted:
            complete(requestId, BnHostCallStatus.restricted, nil)
        @unknown default:
            complete(requestId, BnHostCallStatus.error, nil)
        }
    }

    private func beginFix(_ requestId: Int64) {
        if Self.suppressSystemLocationCallsForTest { return } // the test drives didUpdateLocations
        ensureManager().requestLocation() // one-shot → didUpdateLocations / didFailWithError
    }

    private func beginAuthorization(_ requestId: Int64) {
        if Self.suppressSystemLocationCallsForTest { return } // owner-device territory; the test drives didChangeAuthorization
        ensureManager().requestWhenInUseAuthorization() // pops the system alert; the delegate resumes later
    }

    private func currentAuthorizationStatus() -> CLAuthorizationStatus {
        if let override = Self.authorizationStatusOverrideForTest { return override() }
        let m = ensureManager()
        if #available(iOS 14.0, *) { return m.authorizationStatus }
        return CLLocationManager.authorizationStatus()
    }

    /// GRANTED/DENIED/… for the read-only Check (never prompts).
    private func checkStatus(_ status: CLAuthorizationStatus) -> Int32 {
        switch status {
        case .authorizedWhenInUse, .authorizedAlways: return BnHostCallStatus.granted
        case .notDetermined: return BnHostCallStatus.denied // not held; a request MAY prompt
        case .denied: return BnHostCallStatus.deniedPermanently // one-shot; only Settings changes it
        case .restricted: return BnHostCallStatus.restricted
        @unknown default: return BnHostCallStatus.error
        }
    }

    private func mapFailure(_ error: Error) -> Int32 {
        guard let cl = error as? CLError else { return BnHostCallStatus.error }
        switch cl.code {
        case .denied: return BnHostCallStatus.deniedPermanently // authorization revoked mid-fix
        case .locationUnknown: return BnHostCallStatus.locationUnavailable // could not determine a fix
        default: return BnHostCallStatus.error
        }
    }

    @discardableResult
    private func ensureManager() -> CLLocationManager {
        if let m = manager { return m }
        let m = CLLocationManager()
        m.delegate = self // held WEAKLY by the manager — retained via this handler (see header)
        m.desiredAccuracy = kCLLocationAccuracyBest
        manager = m
        return m
    }

    private func currentRequestId() -> Int64? {
        lock.lock(); defer { lock.unlock() }; return inFlightRequestId
    }

    /// The single completion funnel. Consumes the in-flight slot iff it still matches
    /// (one-shot — a duplicate/late callback finds it cleared and its export call takes
    /// the unknown-id path, rc 1), then delivers the tri-state to .NET. The fix crosses
    /// as a NUL-terminated UTF-8 C string valid only during the call.
    private func complete(_ requestId: Int64, _ status: Int32, _ payload: String?) {
        lock.lock()
        if inFlightRequestId == requestId { inFlightRequestId = nil }
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

    /// The fix as the flat string→string JSON the wire carries — keys mirror
    /// NativeShellBridge.ParseGeolocationResult and AndroidShellBridge.fixPayload EXACTLY:
    /// lat / lng / accuracy / altitude / timestamp. Numbers are locale-independent (Swift's
    /// Double description always uses '.', matching .NET InvariantCulture + Java toString);
    /// altitude is omitted when the vertical fix is invalid (verticalAccuracy < 0), as
    /// Android omits it when the platform has none. timestamp is Unix ms.
    private func fixPayload(_ loc: CLLocation) -> String {
        var pairs: [(String, String)] = []
        pairs.append(("lat", String(loc.coordinate.latitude)))
        pairs.append(("lng", String(loc.coordinate.longitude)))
        pairs.append(("accuracy", String(loc.horizontalAccuracy >= 0 ? loc.horizontalAccuracy : 0)))
        if loc.verticalAccuracy >= 0 {
            pairs.append(("altitude", String(loc.altitude)))
        }
        let ms = Int64((loc.timestamp.timeIntervalSince1970 * 1000).rounded())
        pairs.append(("timestamp", String(ms)))
        return BnFlatJson.object(pairs)
    }

    private func onMain(_ work: @escaping () -> Void) {
        if Thread.isMainThread { work() } else { DispatchQueue.main.async(execute: work) }
    }
}
