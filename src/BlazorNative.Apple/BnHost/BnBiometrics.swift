// ─────────────────────────────────────────────────────────────────────────────
// BnBiometrics — Phase 9.2 Gate 3 (M9 DoD #4): the iOS half of biometric
// authentication, the mirror of AndroidShellBridge.handleBiometrics's
// BiometricPrompt / BiometricManager flow. The SECOND reuse of the 9.0 generic ABI
// on iOS (notifications was the first): op=Biometrics rides the SAME
// `AppleShellBridge.hostCallBegin` slot (offset 72) geolocation opened, so the
// bridge stays 80 bytes / 10 exports — no struct grow, no new export. Biometrics
// adds an op-enum value (=2) + a wire-mirrored status enum + a host handler, and
// touches the ABI at NOTHING else.
//
// DENIAL IS DATA (the milestone law, restated for LocalAuthentication): the terminal
// outcome ALWAYS returns as a wire-mirrored `BnBiometricStatus` via
// `blazornative_host_call_complete` — never a Swift error thrown across the C
// boundary, never a dropped completion (a hang). Failure, cancellation, lockout and
// no-hardware are all VALUES; every branch calls `complete(...)` exactly once, so the
// awaiting .NET ValueTask always resolves within a bounded await. `authenticate` and
// `check` carry NO payload (a status is the whole answer — a token would tempt an
// app-level bypass, the design §3d refuses it).
//
// CONTEXT RETENTION (the CLLocationManager weak-delegate lesson from 9.0, restated for
// LAContext): an `LAContext` deallocated mid-evaluation CANCELS the in-flight policy
// evaluation. So the bridge holds THIS handler strongly (AppleShellBridge.biometrics,
// app-lifetime), and this handler holds the `LAContext` for the whole call duration
// (`inFlightContext`) — a per-call local context would be gone before the async
// `evaluatePolicy` reply fires. Cleared by `complete`.
//
// SIMULATOR-SCOPED + LABELLED (the M9 iOS deferral): `evaluatePolicy`'s system Face/
// Touch ID prompt is not drivable in a hosted XCTest (the 9.0/9.1 real-dialog split —
// real biometric UX is owner-device territory, and iOS real-device is DEFERRED). So
// the shell exposes seams — a `canEvaluatePolicy` OVERRIDE (for `check`) and an
// `evaluatePolicy` REPLY override (drives the authenticate outcome deterministically)
// — and CI asserts the authenticated path + the failed/cancelled/lockout/unavailable
// matrix as DATA (no hang). The production `LAContext.evaluatePolicy` /
// `canEvaluatePolicy` calls are unsuppressed and untouched.
// ─────────────────────────────────────────────────────────────────────────────

import Foundation
import LocalAuthentication

/// The wire-mirrored biometric status (mirror of .NET BiometricStatus / Kotlin
/// BiometricStatus, byte-identical — SIX values). Failure (1), cancellation (2),
/// unavailability (3), lockout (4) and error (5) are all VALUES the awaiting .NET
/// ValueTask resolves to — never exceptions, never hangs. Do NOT reorder — the integer
/// IS the ABI contract.
enum BnBiometricStatus {
    static let authenticated: Int32 = 0  // the user proved presence; or, on check, "present + enrolled + ready"
    static let failed: Int32 = 1         // a biometric was presented and rejected; retry allowed
    static let cancelled: Int32 = 2      // the user (or app/system) dismissed the prompt
    static let unavailable: Int32 = 3    // no hardware, or none enrolled
    static let lockedOut: Int32 = 4      // too many failures — temporarily (or permanently) locked
    static let error: Int32 = 5          // unexpected host error (a caught throw / unknown outcome)
}

final class BnBiometrics {

    private let lock = NSLock()

    /// The single in-flight requestId (one authenticate per op — the one-in-flight
    /// rule). `complete(...)` consumes it (one-shot), so a late/duplicate reply no-ops.
    private var inFlightRequestId: Int64?

    /// STRONG. The `LAContext` under evaluation — held for the call's duration so a
    /// deallocated context can never cancel the in-flight `evaluatePolicy` (the
    /// CLLocationManager retention lesson for LocalAuthentication). Cleared by `complete`.
    private var inFlightContext: LAContext?

    // ── Test seams (static, reset in teardown — the BnGeolocation/BnNotifications twins) ──

    /// Overrides the read of `canEvaluatePolicy` so a hosted test drives `check`
    /// deterministically (the sim's real biometric-availability state is not drivable).
    /// Returns (canEvaluate, error?). Null in production → the real LAContext is asked.
    static var canEvaluatePolicyOverrideForTest: (() -> (Bool, Error?))?

    /// Overrides the async `evaluatePolicy` REPLY so a hosted test drives the
    /// authenticate outcome without the un-drivable system sheet (owner-device
    /// territory). Given the reason, it must call the reply with (success, error?) — the
    /// SAME (Bool, Error?) shape LAContext hands its own completion. Null/absent in
    /// production → the real `LAContext.evaluatePolicy` sheet is presented.
    static var evaluatePolicyReplyOverrideForTest: ((_ reason: String, _ reply: @escaping (Bool, Error?) -> Void) -> Void)?

    /// Intercepts the completion so a PURE unit test (no NativeAOT boot) observes the
    /// routed (requestId, status) without a live .NET continuation. Null in production →
    /// the real `blazornative_host_call_complete` export is called.
    static var completeHookForTest: ((Int64, Int32, String?) -> Int32)?

    /// The rc of the most recent `blazornative_host_call_complete` — 0 = delivered to a
    /// live .NET continuation, 1 = unknown/already-completed id (benign). Int32.min
    /// before any completion. The BnGeolocation/BnNotifications twin.
    static var lastHostCallCompleteRcForTest: Int32 = Int32.min

    static func resetForTest() {
        canEvaluatePolicyOverrideForTest = nil
        evaluatePolicyReplyOverrideForTest = nil
        completeHookForTest = nil
        lastHostCallCompleteRcForTest = Int32.min
    }

    func clearInFlightForTest() { lock.lock(); inFlightRequestId = nil; inFlightContext = nil; lock.unlock() }
    func hasInFlightRequestForTest() -> Bool { lock.lock(); defer { lock.unlock() }; return inFlightRequestId != nil }
    /// The LAContext is retained for the call's duration — the retention pin as a
    /// PROPERTY (no observable otherwise sees the hold, the CLLocationManager precedent).
    func contextIsRetainedForTest() -> Bool { lock.lock(); defer { lock.unlock() }; return inFlightContext != nil }

    // ── The op entry (AppleShellBridge.hostCallBegin forwards here for op=Biometrics) ──

    /// action=check is the read-only availability peek (never prompts — the geolocation
    /// `mode:check` sibling); action=authenticate presents the Face/Touch ID prompt and
    /// maps its outcome to a status. Returns FAST (the begin contract); the terminal
    /// status is a deferred `complete(...)`.
    func begin(requestId: Int64, argsJson: String) {
        let args = BnFlatJson.parseObject(argsJson) ?? [:]
        let action = args["action"] ?? "authenticate"
        switch action {
        case "check":
            complete(requestId, canAuthenticateStatus(), nil)
        case "authenticate":
            authenticate(requestId: requestId, reason: args["reason"] ?? "Authenticate")
        default:
            // An unknown action is DATA (Error 5), never a crash (the Kotlin posture).
            complete(requestId, BnBiometricStatus.error, nil)
        }
    }

    /// An unknown host-call op: DATA (Error), not a crash — the awaiting .NET ValueTask
    /// resolves rather than leaking a pending entry (the geolocation unknown-op posture).
    func completeUnknownOp(requestId: Int64) {
        complete(requestId, BnBiometricStatus.error, nil)
    }

    // ── Internals ─────────────────────────────────────────────────────────────────

    /// `canEvaluatePolicy(.deviceOwnerAuthenticationWithBiometrics)` → a BnBiometricStatus
    /// for the read-only check: can-evaluate ⇒ Authenticated ("present + enrolled +
    /// ready", the Android canAuthenticateStatus SUCCESS→AUTHENTICATED twin); otherwise
    /// map the LAError (not-available/not-enrolled ⇒ Unavailable, lockout ⇒ LockedOut) —
    /// a status, never a throw. Never prompts.
    private func canAuthenticateStatus() -> Int32 {
        let canEvaluate: Bool
        let error: Error?
        if let override = Self.canEvaluatePolicyOverrideForTest {
            (canEvaluate, error) = override()
        } else {
            var err: NSError?
            canEvaluate = LAContext().canEvaluatePolicy(.deviceOwnerAuthenticationWithBiometrics, error: &err)
            error = err
        }
        if canEvaluate { return BnBiometricStatus.authenticated }
        return mapError(error)
    }

    /// Presents the Face/Touch ID prompt (or the seam reply) and maps the outcome. The
    /// requestId + the LAContext are recorded synchronously BEFORE the async evaluation
    /// so a reply can never precede the record.
    private func authenticate(requestId: Int64, reason: String) {
        let context = LAContext()
        lock.lock()
        inFlightRequestId = requestId
        inFlightContext = context // retained for the call's duration (see the file header)
        lock.unlock()

        if let override = Self.evaluatePolicyReplyOverrideForTest {
            override(reason) { [weak self] success, error in
                self?.finishAuthenticate(requestId, success: success, error: error)
            }
            return
        }
        context.evaluatePolicy(.deviceOwnerAuthenticationWithBiometrics, localizedReason: reason) { [weak self] success, error in
            self?.finishAuthenticate(requestId, success: success, error: error)
        }
    }

    private func finishAuthenticate(_ requestId: Int64, success: Bool, error: Error?) {
        if success { complete(requestId, BnBiometricStatus.authenticated, nil); return }
        complete(requestId, mapError(error), nil)
    }

    /// Maps an `LAError` code into a BnBiometricStatus (the design §3a matrix): userCancel
    /// / systemCancel / appCancel ⇒ Cancelled; authenticationFailed ⇒ Failed;
    /// biometryNotAvailable / biometryNotEnrolled ⇒ Unavailable; biometryLockout ⇒
    /// LockedOut; anything else (or a nil/foreign error) ⇒ Error. An out-of-set code maps
    /// to Error — still data, never a throw (the .NET ToBiometricStatus twin).
    private func mapError(_ error: Error?) -> Int32 {
        guard let code = (error as? LAError)?.code else { return BnBiometricStatus.error }
        switch code {
        case .userCancel, .systemCancel, .appCancel:
            return BnBiometricStatus.cancelled
        case .authenticationFailed:
            return BnBiometricStatus.failed
        case .biometryNotAvailable, .biometryNotEnrolled:
            return BnBiometricStatus.unavailable
        case .biometryLockout:
            return BnBiometricStatus.lockedOut
        default:
            return BnBiometricStatus.error
        }
    }

    /// The single completion funnel. Consumes the in-flight slot + releases the retained
    /// context iff the id still matches (one-shot — a duplicate/late reply finds it
    /// cleared and its export call takes the unknown-id path, rc 1), then delivers the
    /// status to .NET (payload is always nil for biometrics).
    private func complete(_ requestId: Int64, _ status: Int32, _ payload: String?) {
        lock.lock()
        if inFlightRequestId == requestId { inFlightRequestId = nil; inFlightContext = nil }
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
}
