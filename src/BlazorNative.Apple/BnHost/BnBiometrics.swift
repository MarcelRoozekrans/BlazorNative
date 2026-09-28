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
//
// THE BEGIN CONTRACT, KEPT OFF THE CALLING THREAD (16.4, #438). `hostCallBegin` must
// return at once; the outcome arrives later through `host_call_complete`. The first
// `LAContext()` in a process is a cold, synchronous cost: 0.5 to 1.7 s on the simulator
// lane, once about 33 s, against 3 to 5 ms warm. Until 16.4 it ran inside
// `hostCallBegin`, so it held whatever thread called it, and since 16.1 that is the
// .NET RENDER thread, on a user's first Authenticate. Now every LocalAuthentication
// call, creating the context, `canEvaluatePolicy` and arming `evaluatePolicy`, runs on
// `workQueue`, a serial queue this handler owns. `begin` only parses, records the
// request and enqueues. The pin is `testHostCallBeginReturnsWhileContextCreationIsBlocked`
// and its `check` twin in BnBiometricsTests, which block `contextFactoryForTest` and
// require `hostCallBegin` to return while the factory is still blocked.
// ─────────────────────────────────────────────────────────────────────────────

import Foundation
import LocalAuthentication

/// 16.4 (#438): times `LAContext` creation and warns ONCE per process when it is
/// pathologically slow. Shared by BnBiometrics and BnSecureStorage, the two handlers
/// that create contexts.
///
/// WHY IT EXISTS. Until 16.4 a cold `LAContext()` ran inside `hostCallBegin`, on the
/// .NET render thread, so 16.3's slow-handler warning saw it: that line, "held the
/// render thread for 33381 ms", was the only evidence of the worst sample. Moving the
/// creation onto the handler's own queue fixed the contract and blinded that warning,
/// so a recurrence would otherwise leave no trace. This line puts the trace back, at
/// the call that is slow.
///
/// THE THRESHOLD, 5000 ms, and why. The phase record logged cold creation at 525, 909
/// and 1667 ms, warm at 3 to 5 ms, and one synchronous part of 33381 ms that was almost
/// all creation. 5 s is three times the slowest normal cold start, so an ordinary
/// first prompt never warns, and it is six times below the boot tests' 30 s wait, so a
/// recurrence of the 33 s case is logged long before a test gives up. It is also a
/// delay no user should see before a Face ID sheet appears.
///
/// ONCE PER PROCESS, one flag for both handlers: a cold start is a property of the
/// process, and after the first slow creation every later one is warm. The message
/// carries a duration and constant text only, never a reason string, key or value, so
/// it is written with `.safe` privacy: redacting it would erase the one number it
/// exists to report. It is a diagnostic and is not pinned by a test.
enum BnLAContextTiming {
    static let slowCreationThresholdMs: UInt64 = 5_000

    private static let lock = NSLock()
    private static var warned = false

    static func timed(_ category: String, _ create: () -> LAContext) -> LAContext {
        let start = DispatchTime.now().uptimeNanoseconds
        let context = create()
        let elapsedMs = (DispatchTime.now().uptimeNanoseconds - start) / 1_000_000
        guard elapsedMs > slowCreationThresholdMs else { return context }
        lock.lock()
        let first = !warned
        warned = true
        lock.unlock()
        if first {
            BnLog.warn(category, "slow LAContext creation: \(elapsedMs) ms, over the "
                + "\(slowCreationThresholdMs) ms threshold. A cold LocalAuthentication start; "
                + "the first biometric prompt waits for it. Warned once per process.",
                privacy: .safe)
        }
        return context
    }
}

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

    /// Serial, owned by this handler: every LocalAuthentication call runs here, never on
    /// the thread that called `hostCallBegin` (see THE BEGIN CONTRACT in the header).
    /// Serial so requests keep their order, and so `drainForTest` is a barrier.
    private let workQueue = DispatchQueue(label: "io.blazornative.biometrics", qos: .userInitiated)

    /// The single in-flight requestId (one authenticate per op — the one-in-flight
    /// rule). `complete(...)` consumes it (one-shot), so a late/duplicate reply no-ops.
    ///
    /// WHAT IT MEANS, AND WHAT IT DOES NOT (16.4, #438). It is published in `begin`, on
    /// the calling thread, BEFORE the evaluation is armed on `workQueue`. So it means
    /// "the request is recorded", NOT "the reply is armed". That order is required, not
    /// incidental: an evaluation, or the test seam standing in for it, may reply
    /// synchronously, and `complete` clears the slot only when the id matches, so a
    /// record written after the reply would leave a stale in-flight id behind. No
    /// production code waits on this flag; only `complete` reads it, and
    /// `hasInFlightRequestForTest` exposes it. A caller that needs the evaluation armed
    /// must wait for the arming itself, which is what the boot tests do since 16.4.
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

    /// Replaces `LAContext()` so a test can make context creation SLOW on demand, by
    /// blocking inside the factory, and prove `hostCallBegin` does not wait for it (the
    /// begin-contract pin). Null in production → a real `LAContext()`.
    static var contextFactoryForTest: (() -> LAContext)?

    /// Runs on `workQueue` after the context exists and before the evaluation is armed:
    /// the window in which `hasInFlightRequestForTest()` is already true but no reply is
    /// armed yet. A test blocks here to hold that window open deterministically (the
    /// reply-arming pin). Null in production.
    static var beforeEvaluationArmedHookForTest: (() -> Void)?

    /// The rc of the most recent `blazornative_host_call_complete` — 0 = delivered to a
    /// live .NET continuation, 1 = unknown/already-completed id (benign). Int32.min
    /// before any completion. The BnGeolocation/BnNotifications twin.
    static var lastHostCallCompleteRcForTest: Int32 = Int32.min

    static func resetForTest() {
        canEvaluatePolicyOverrideForTest = nil
        evaluatePolicyReplyOverrideForTest = nil
        completeHookForTest = nil
        contextFactoryForTest = nil
        beforeEvaluationArmedHookForTest = nil
        lastHostCallCompleteRcForTest = Int32.min
    }

    /// Returns once everything already enqueued on `workQueue` has run. The queue is
    /// serial, so this is a barrier: a unit test calls it after `hostCallBegin` instead
    /// of sleeping. Never call it from `workQueue` itself.
    func drainForTest() { workQueue.sync {} }

    func clearInFlightForTest() { lock.lock(); inFlightRequestId = nil; inFlightContext = nil; lock.unlock() }
    func hasInFlightRequestForTest() -> Bool { lock.lock(); defer { lock.unlock() }; return inFlightRequestId != nil }
    /// The LAContext is retained for the call's duration — the retention pin as a
    /// PROPERTY (no observable otherwise sees the hold, the CLLocationManager precedent).
    func contextIsRetainedForTest() -> Bool { lock.lock(); defer { lock.unlock() }; return inFlightContext != nil }

    // ── The op entry (AppleShellBridge.hostCallBegin forwards here for op=Biometrics) ──

    /// action=check is the read-only availability peek (never prompts — the geolocation
    /// `mode:check` sibling); action=authenticate presents the Face/Touch ID prompt and
    /// maps its outcome to a status. Returns FAST (the begin contract): both actions do
    /// their LocalAuthentication work on `workQueue`, and the terminal status is a
    /// deferred `complete(...)`.
    func begin(requestId: Int64, argsJson: String) {
        let args = BnFlatJson.parseObject(argsJson) ?? [:]
        let action = args["action"] ?? "authenticate"
        switch action {
        case "check":
            workQueue.async { [weak self] in
                guard let self = self else { return }
                self.complete(requestId, self.canAuthenticateStatus(), nil)
            }
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
            canEvaluate = Self.makeContext().canEvaluatePolicy(.deviceOwnerAuthenticationWithBiometrics, error: &err)
            error = err
        }
        if canEvaluate { return BnBiometricStatus.authenticated }
        return mapError(error)
    }

    /// Presents the Face/Touch ID prompt (or the seam reply) and maps the outcome. The
    /// requestId is recorded synchronously, on the calling thread, BEFORE any evaluation
    /// is armed, so a reply can never precede the record. Everything else, the cold
    /// `LAContext()` included, runs on `workQueue`, so `begin` returns at once.
    private func authenticate(requestId: Int64, reason: String) {
        lock.lock()
        inFlightRequestId = requestId
        lock.unlock()
        workQueue.async { [weak self] in
            self?.armEvaluation(requestId: requestId, reason: reason)
        }
    }

    /// On `workQueue`: create and retain the context, then arm the evaluation.
    private func armEvaluation(requestId: Int64, reason: String) {
        let context = Self.makeContext()
        lock.lock()
        // Retained for the call's duration (see the file header), but only while this
        // request is still the in-flight one, so a request cleared or superseded while it
        // waited on the queue cannot pin a context that no completion will release.
        if inFlightRequestId == requestId { inFlightContext = context }
        lock.unlock()

        Self.beforeEvaluationArmedHookForTest?()

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

    /// Every context this handler creates, for `check` and for `armEvaluation`, comes
    /// from here, timed by `BnLAContextTiming` so a pathological cold start is logged.
    private static func makeContext() -> LAContext {
        BnLAContextTiming.timed("BnBiometrics") { contextFactoryForTest?() ?? LAContext() }
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
