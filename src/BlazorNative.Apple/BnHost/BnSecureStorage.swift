// ─────────────────────────────────────────────────────────────────────────────
// BnSecureStorage — Phase 9.2 Gate 3 (M9 DoD #4): the iOS half of the encrypted,
// optionally biometric-bound secret store, the mirror of AndroidShellBridge's
// AndroidKeyStore flow. op=SecureStorage rides the SAME
// `AppleShellBridge.hostCallBegin` slot (offset 72) geolocation opened, so the bridge
// stays 80 bytes / 10 exports — no struct grow, no new export. Secure storage adds an
// op-enum value (=3) + a wire-mirrored status enum + a host handler, and touches the
// ABI at NOTHING else. The value returns in the OPTIONAL flat-JSON {"value":…} payload
// `blazornative_host_call_complete` has carried since 9.0 (geolocation's fix is the
// first user; this is the second) — NO new export.
//
// THE STORE — the iOS Keychain (`Security` system framework, no dependency), DISTINCT
// from the plain UserDefaults-style store the storageRead/Write/Delete slots back:
//   • set(key,value,auth=0) → SecItemAdd (kSecClassGenericPassword) with
//     kSecAttrAccessibleWhenUnlockedThisDeviceOnly (device-bound, not iCloud-synced,
//     unreadable while locked).
//   • set(key,value,auth=1) → SecItemAdd with a SecAccessControl created
//     `.biometryCurrentSet` — the OS-KEY-LEVEL binding (§4c, owner-locked): the Secure
//     Enclave itself refuses to release the bytes without a fresh Face/Touch ID, AND
//     `.biometryCurrentSet` INVALIDATES the item if the enrolled biometric set changes.
//   • get(key) → SecItemCopyMatching with kSecUseAuthenticationUI = .fail so a plain
//     get NEVER silently prompts: a non-auth item returns Ok+value; an AUTH-bound item
//     is REFUSED as AuthFailed. THE CONTRACT — a plain get of an auth item must not hand
//     back the plaintext. (See the OS-BINDING note below: on a real device the OS itself
//     refuses; on the simulator the shell refuses off the ACL/marker.)
//   • getWithAuth(key,reason) → the OS Face ID evaluation unlocks the item and the
//     plaintext returns in {"value":…}; a non-auth item is read directly (no prompt).
//   • the authorized read evaluates `.deviceOwnerAuthenticationWithBiometrics` —
//     BIOMETRY, matching the `.biometryCurrentSet` ACL the set attached. It used
//     to evaluate `.deviceOwnerAuthentication`, which is passcode OR biometry, so
//     the effective gate was WEAKER than the stored ACL declared (#213 item 1,
//     confirmed on device). There is deliberately no passcode fallback: Android
//     has none either, and lockout is recoverable by unlocking the device, which
//     resets biometry.
//   • delete(key) → SecItemDelete (idempotent — a missing item is still Ok).
//
// THE OS-KEY BINDING — PROVEN vs UNPROVEN (the honest split, mirroring biometrics and
// the Android gate). The iOS SIMULATOR has NO Secure Enclave and does NOT enforce a
// SecAccessControl — `.biometryCurrentSet` is a documented no-op there: a plain get of
// an auth item returns the bytes the real OS would refuse. So the OS-ENFORCED refusal
// is genuinely unprovable in CI (and iOS real-device is DEFERRED — no Apple Developer
// account). What IS proven, deterministically:
//   • the shell REQUESTS the binding — an auth-bound set builds + attaches a
//     `.biometryCurrentSet` SecAccessControl (asserted via `lastSetAccessControlForTest`,
//     a construction seam; the "drop the ACL" mutation reds it);
//   • the CONTRACT — the shell refuses a plain get of an auth-bound item (AuthFailed, no
//     value), learning the binding from the ACL/marker the way Android reads the
//     keystore's KeyInfo;
//   • the STATUS contract through the gate — getWithAuth authorize returns the REAL
//     Keychain value, deny is AuthFailed, no hang.
// The OS-enforced Secure-Enclave refusal (safe even against a control-flow bypass) is
// the UNPROVEN-until-real-device half — named, not smuggled.
//
// THE iOS-vs-ANDROID ASYMMETRY (a real platform difference, documented not hidden): an
// auth-bound `set` does NOT prompt on iOS. The `.biometryCurrentSet` ACL gates
// RETRIEVAL only — SecItemAdd stores the item without a biometric gesture. Android's
// auth-bound set DOES prompt (an AES per-use-auth key's ENCRYPT doFinal also needs a
// fresh auth — the honest consequence of AES-at-the-key). Same wire, same OS-key
// binding, different write-time UX.
//
// DENIAL IS DATA (the milestone law): every terminal is a wire-mirrored
// `BnSecureStorageStatus` via `blazornative_host_call_complete` — NotFound, AuthFailed,
// Unavailable, Error are all VALUES, never a Swift throw across the C boundary and never
// a dropped completion (a hang). Every branch calls `complete(...)` exactly once.
//
// SIMULATOR-SCOPED + LABELLED (the M9 iOS deferral): the OS Face ID sheet
// `getWithAuth` triggers is not drivable in a hosted XCTest (owner-device territory,
// the geolocation/notifications split). So the gated read is driven behind
// `authGateHookForTest`, which hands the test a `BnSecureAuthGate` INSTEAD of popping
// the sheet — CI proves the wire, the {"value":…} payload, the status mapping and
// denial-as-data no-hang. The plain path (set/get/delete of non-auth items) runs the
// REAL Keychain end-to-end. The OS-key binding is proven at the CONTRACT level (the
// shell attaches the ACL + refuses a plain get); the OS-ENFORCED Secure-Enclave refusal
// is the UNPROVEN-until-real-device half (see the OS-KEY BINDING note above).
//
// THE BEGIN CONTRACT, KEPT OFF THE CALLING THREAD (16.4, #438, the BnBiometrics twin).
// `hostCallBegin` must return at once. Until 16.4 every action ran inside it: the
// Keychain calls, which are synchronous IPC to securityd, and on the auth paths an
// `LAContext()`, whose first creation in a process is a cold cost of up to seconds.
// Since 16.1 the caller is the .NET RENDER thread. Now `begin` parses and enqueues, and
// the action STARTS on `workQueue`, a serial queue this handler owns, so a set and the
// get that follows it keep their order. All Keychain work an action does runs on
// `workQueue`. A getWithAuth of an auth-bound item arms the gate on the queue and frees
// it; the gate answers on LocalAuthentication's reply queue in production, or on
// whatever thread the test seam calls it from, and the answer hops back onto
// `workQueue`, where `finishAuthorizedRead` or the denial runs. 18.3 (#506): requests on
// one key take effect in begin order. The getWithAuth holds its key (`held`) from the
// gate until its answer has run; a later request on that key parks and runs when the
// gate answers, granted or denied. Requests on other keys keep flowing. An auth-bound
// set holds nothing, because it does not prompt on iOS. The pins are the 18.3 section
// of BnSecureStorageTests, `testTheGatesAnswerRunsOnTheWorkQueue` among them. The cores
// `secureSet`, `secureGet` and `secureDelete` stay synchronous, because the
// keystore-half tests call them directly; only the op entry moved. The begin pin is
// `testHostCallBeginReturnsWhileContextCreationIsBlocked` and its getWithAuth twin in
// BnSecureStorageTests.
// ─────────────────────────────────────────────────────────────────────────────

import Foundation
import LocalAuthentication
import Security

/// The wire-mirrored secure-storage status (mirror of .NET SecureStorageStatus / Kotlin
/// SecureStorageStatus, byte-identical — FIVE values). The biometric-gate detail (failed
/// vs cancelled vs lockout) FOLDS into AuthFailed for storage — the caller only needs
/// "couldn't unlock" (the finer grain lives on IBiometrics.AuthenticateAsync). Do NOT
/// reorder — the integer IS the ABI contract.
enum BnSecureStorageStatus {
    static let ok: Int32 = 0          // set/delete succeeded; GET FOUND THE VALUE ({"value":…} on get)
    static let notFound: Int32 = 1    // get/getWithAuth of an absent key (no payload)
    static let authFailed: Int32 = 2  // the biometric gate denied / failed / cancelled / locked out; or a plain get of an auth item
    /// No secure hardware, or the Keychain is unusable. Returned from a nil
    /// `SecAccessControlCreateWithFlags` result, in `secureSet`, or from
    /// `errSecMissingEntitlement` / `errSecNotAvailable`, in `mapAddError`. NOT
    /// "biometrics not enrolled" — nothing on iOS checks enrolment. What an unenrolled
    /// auth-bound `set` returns here is not established (#396). Android's `provisionKey`
    /// path, reached only from its own auth-bound SET, DOES map that case to Unavailable;
    /// a `getWithAuth` of an existing item there does not reach it the same way. iOS may
    /// not agree either way — untested.
    static let unavailable: Int32 = 3
    static let error: Int32 = 4       // unexpected host error (a caught throw, malformed args)
}

/// A pending auth gate handed to the test seam (via `authGateHookForTest`) INSTEAD of
/// the real LAContext Face ID evaluation — the Android `BiometricGate` twin. The test
/// drives the outcome: `authenticate()` models the OS-unlocked read (returns the value),
/// `deny()` completes AuthFailed. `hasAuthBoundItem` is the read-side OS-binding pin (the
/// Android `hasCryptoObject` twin — the gate is only engaged for an auth-bound item).
struct BnSecureAuthGate {
    let reason: String
    let hasAuthBoundItem: Bool
    let authenticate: () -> Void
    let deny: () -> Void
}

final class BnSecureStorage {

    /// The Keychain service every item is filed under (account = the app-chosen key).
    static let service = "io.blazornative.secure"

    /// The kSecAttrLabel sentinel stamped on an AUTH-bound item ALONGSIDE the real
    /// `.biometryCurrentSet` SecAccessControl. WHY A READABLE MARKER: the iOS SIMULATOR
    /// has NO Secure Enclave and does NOT enforce a SecAccessControl (the ACL is a
    /// documented no-op there — a plain get returns the bytes the real OS would refuse).
    /// So the shell cannot learn "this item is auth-bound" from the OS refusing it on the
    /// sim; the label is how the enclave-less simulator surfaces the binding so the shell
    /// still routes the gated read and still REFUSES a plain get. On a REAL device the OS
    /// refuses first (errSecInteractionNotAllowed → auth-bound before the label is even
    /// read), so the marker is a simulator affordance, never the security itself — the
    /// security is the ACL, whose OS-enforced refusal is the UNPROVEN-until-real-device
    /// half (iOS real-device is DEFERRED). The Android parity: there the keystore's own
    /// KeyInfo carries the auth requirement; here the item carries the ACL + this marker.
    static let authLabel = "bn-authbound"

    private let lock = NSLock()

    /// Serial, owned by this handler: every action `begin` accepts runs here, never on
    /// the thread that called `hostCallBegin` (see THE BEGIN CONTRACT in the header).
    private let workQueue = DispatchQueue(label: "io.blazornative.secure-storage", qos: .userInitiated)

    /// 18.3 (#506): set on `workQueue` only, so code can ask whether it is running there.
    private let workQueueKey = DispatchSpecificKey<Bool>()

    init() {
        workQueue.setSpecific(key: workQueueKey, value: true)
    }

    /// 18.3 (#506): the held keys. A key is held by the getWithAuth `owner` whose gate is
    /// up, and `parked` holds the requests begun on that key since, in begin order. A key
    /// in the map is held even when nothing is parked behind it. Read and written ONLY on
    /// `workQueue`, so it needs no lock.
    private var held: [String: (owner: Int64, parked: [() -> Void])] = [:]

    /// STRONG. The `LAContext` under each getWithAuth evaluation, keyed by its request
    /// id — held for the call's duration so a deallocated context cannot cancel the
    /// in-flight read (the CLLocationManager/LAContext retention lesson). `complete`
    /// releases only its own request's entry. Until 16.4 this was one slot that EVERY
    /// completion cleared, so a plain get or delete finishing while a getWithAuth waited
    /// on its prompt released that prompt's context; since 16.4 queues the actions, that
    /// interleaving is ordinary. The pin is
    /// `testACompletionDoesNotReleaseAnotherRequestsRetainedContext`.
    private var inFlightContexts: [Int64: LAContext] = [:]

    // ── Test seams (static, reset in teardown) ───────────────────────────────────

    /// When set (hosted test only), a getWithAuth of an AUTH-bound item hands the test a
    /// `BnSecureAuthGate` INSTEAD of presenting the real Face ID sheet (owner-device
    /// territory). Null in production → the real `LAContext.evaluatePolicy` sheet is used.
    static var authGateHookForTest: ((BnSecureAuthGate) -> Void)?

    /// The CONSTRUCTION spy (the assert-the-code-requests-the-binding seam): the
    /// SecAccessControl the most recent auth-bound `set` ATTACHED to its item, or nil for
    /// a plain set. Because the simulator does not ENFORCE the ACL (no Secure Enclave), a
    /// test cannot prove the OS refuses a plain get there; it CAN prove the shell
    /// requested the binding — this pins that an auth-bound set built + attached a
    /// `.biometryCurrentSet` SecAccessControl. The "drop the ACL" mutation leaves this nil
    /// and reds the construction assertion (the sim's OS-refusal tripwire being
    /// unavailable). Set on every `secureSet`.
    static var lastSetAccessControlForTest: SecAccessControl?

    /// The set of accounts this process stored AUTH-bound — the CERTAIN, enclave-less-
    /// simulator detection of "is this item auth-bound". The simulator does not enforce
    /// the `.biometryCurrentSet` ACL AND its attribute readback is unreliable (it surfaces
    /// a kSecAttrAccessControl attribute for PLAIN items too — the run-3 leak), so the
    /// shell cannot classify an item from the Keychain alone there. This cache is backed
    /// by the REAL ACL on the item (the construction, asserted via the spy) + the
    /// `authLabel` marker; on a REAL device the OS refusal (errSecInteractionNotAllowed) is
    /// authoritative and this is only a fast-path. Populated on an auth-bound set, cleared
    /// on delete / a plain overwrite. Static because the Keychain is process-global.
    ///
    /// Guarded by `authBoundAccountsLock`. Since 16.4 the op entry mutates it on
    /// `workQueue` while tests call the cores directly on main, so every read and write
    /// goes through the three accessors below.
    private static var authBoundAccounts: Set<String> = []
    private static let authBoundAccountsLock = NSLock()

    private static func markAuthBound(_ key: String) {
        authBoundAccountsLock.lock(); authBoundAccounts.insert(key); authBoundAccountsLock.unlock()
    }

    private static func unmarkAuthBound(_ key: String) {
        authBoundAccountsLock.lock(); authBoundAccounts.remove(key); authBoundAccountsLock.unlock()
    }

    private static func isMarkedAuthBound(_ key: String) -> Bool {
        authBoundAccountsLock.lock(); defer { authBoundAccountsLock.unlock() }
        return authBoundAccounts.contains(key)
    }

    /// Intercepts the completion so a PURE unit test (no NativeAOT boot) observes the
    /// routed (requestId, status, payload) without a live .NET continuation.
    static var completeHookForTest: ((Int64, Int32, String?) -> Int32)?

    /// The rc of the most recent `blazornative_host_call_complete` — 0 = delivered to a
    /// live .NET continuation, 1 = unknown/already-completed id (benign). Int32.min
    /// before any completion.
    ///
    /// Lock-guarded since 16.4: `complete` runs on `workQueue`, since 18.3 the gate's
    /// answer included, while tests read this on main. It is stored AFTER the export
    /// returns, so a test that saw the outcome must still poll for it, not read it once.
    static var lastHostCallCompleteRcForTest: Int32 {
        get { rcLock.lock(); defer { rcLock.unlock() }; return lastRc }
        set { rcLock.lock(); lastRc = newValue; rcLock.unlock() }
    }
    private static let rcLock = NSLock()
    private static var lastRc: Int32 = Int32.min

    /// Replaces `LAContext()` so a test can make context creation SLOW on demand, by
    /// blocking inside the factory, and prove `hostCallBegin` does not wait for it (the
    /// begin-contract pin). Null in production → a real `LAContext()`.
    static var contextFactoryForTest: (() -> LAContext)?

    static func resetForTest() {
        contextFactoryForTest = nil
        authGateHookForTest = nil
        lastSetAccessControlForTest = nil
        authBoundAccountsLock.lock(); authBoundAccounts = []; authBoundAccountsLock.unlock()
        completeHookForTest = nil
        lastHostCallCompleteRcForTest = Int32.min
    }

    // ── The op entry (AppleShellBridge.hostCallBegin forwards here for op=SecureStorage) ──

    /// Returns once everything already enqueued on `workQueue` has run. The queue is
    /// serial, so this is a barrier: a unit test calls it after `hostCallBegin` instead
    /// of sleeping. It is a barrier only for work that CAN run: 18.3 (#506), a request
    /// parked behind a held key does not run until that key's gate answers, so a drain
    /// does not wait for it. A gate answered off `workQueue` enqueues its answer there, so
    /// a drain after the answering call has returned covers the answer too. Never call it
    /// from `workQueue` itself.
    func drainForTest() { workQueue.sync {} }

    /// 18.3 (#506) test seam: whether `key` is held by a getWithAuth whose gate is up.
    /// Reads `held` on `workQueue`, its only thread. Never call it from `workQueue` itself.
    func isKeyHeldForTest(_ key: String) -> Bool {
        workQueue.sync { held[key] != nil }
    }

    /// 18.3 (#506) test seam: the key `workQueue` carries, so a test can read
    /// `DispatchQueue.getSpecific(key:)` and learn whether code ran on `workQueue`.
    func workQueueSpecificKeyForTest() -> DispatchSpecificKey<Bool> { workQueueKey }

    /// Whether the context of `requestId`'s getWithAuth is still retained (the retention
    /// pin as a property, the `BnBiometrics.contextIsRetainedForTest` twin).
    func isRetainingContextForTest(_ requestId: Int64) -> Bool {
        lock.lock(); defer { lock.unlock() }
        return inFlightContexts[requestId] != nil
    }

    /// Parses the flat-JSON `action` + key and enqueues it on `workQueue`. Returns FAST
    /// (the begin contract); the terminal status is a deferred `complete(...)`. On the
    /// queue, set/get/delete run to completion (no prompt — the iOS asymmetry: even an
    /// auth-bound set does not prompt); getWithAuth of an auth item suspends behind the
    /// gate.
    func begin(requestId: Int64, argsJson: String) {
        let args = BnFlatJson.parseObject(argsJson) ?? [:]
        workQueue.async { [weak self] in
            self?.run(requestId: requestId, args: args)
        }
    }

    /// On `workQueue`: the action itself, or, when its key is held, a park behind the
    /// held key's gate (18.3, #506). A missing key is "", a key like any other.
    private func run(requestId: Int64, args: [String: String]) {
        let key = args["key"] ?? ""
        if held[key] != nil {
            held[key]?.parked.append { [weak self] in self?.perform(requestId: requestId, key: key, args: args) }
            return
        }
        perform(requestId: requestId, key: key, args: args)
    }

    /// On `workQueue`: runs the action now, from `run` or from the drain in `releaseHold`.
    private func perform(requestId: Int64, key: String, args: [String: String]) {
        switch args["action"] ?? "get" {
        case "set":
            complete(requestId, secureSet(key: key, value: args["value"] ?? "", requireAuth: args["auth"] == "1"), nil)
        case "get":
            let (status, value) = secureGet(key: key)
            complete(requestId, status, value.map { valuePayload($0) })
        case "getWithAuth":
            secureGetWithAuth(requestId: requestId, key: key, reason: args["reason"] ?? "Unlock")
        case "delete":
            complete(requestId, secureDelete(key: key), nil)
        default:
            // An unknown action is DATA (Error 4), never a crash (the Kotlin posture).
            complete(requestId, BnSecureStorageStatus.error, nil)
        }
    }

    /// 18.3 (#506): `requestId` holds `key` from now until its gate's answer has run. Called
    /// on `workQueue` immediately before the gate is handed out, because the test seam may
    /// answer inline. The key is free here: a request reaches its gate only when it started
    /// on a free key, or was drained by `releaseHold`, which removes the hold first.
    private func takeHold(_ key: String, _ requestId: Int64) {
        held[key] = (owner: requestId, parked: [])
    }

    /// 18.3 (#506): ends `requestId`'s hold on `key` and runs the requests parked behind it,
    /// in order, inline on `workQueue`. A drained getWithAuth that takes the hold itself,
    /// because its item is auth-bound, stops the drain, and the rest stay parked behind it.
    /// When nothing takes the hold, the key leaves the map. A no-op unless `requestId` owns
    /// the hold: a second or late answer must not release another request's hold.
    private func releaseHold(_ key: String, _ requestId: Int64) {
        guard let hold = held[key], hold.owner == requestId else {
            BnLog.warn("BnSecureStorage", "ignored a release by a request that does not hold its key", privacy: .safe)
            return
        }
        held[key] = nil
        var rest = hold.parked
        while !rest.isEmpty {
            let next = rest.removeFirst()
            next()
            if held[key] != nil {
                held[key]?.parked.append(contentsOf: rest)
                return
            }
        }
    }

    /// 18.3 (#506): runs a gate's answer on `workQueue`. Inline when it already arrives
    /// there, which is where the test seam's in-hook answers run; a `workQueue.sync` from
    /// the queue itself would deadlock, and an `async` would land after the caller's
    /// drain. Otherwise it is enqueued, so LocalAuthentication's reply queue never waits.
    private func onWorkQueue(_ work: @escaping () -> Void) {
        if DispatchQueue.getSpecific(key: workQueueKey) == true {
            work()
        } else {
            workQueue.async(execute: work)
        }
    }

    /// An unknown host-call op: DATA (Error), not a crash — the awaiting .NET ValueTask
    /// resolves rather than leaking a pending entry (the geolocation unknown-op posture).
    func completeUnknownOp(requestId: Int64) {
        complete(requestId, BnSecureStorageStatus.error, nil)
    }

    // ── set / get / delete cores (also called DIRECTLY by the keystore-half tests) ──

    /// set: idempotent (drop any existing item, then add). auth=1 attaches a
    /// SecAccessControl `.biometryCurrentSet` (retrieval is biometric-gated; the add does
    /// NOT prompt — the iOS asymmetry). auth=0 uses kSecAttrAccessibleWhenUnlockedThis-
    /// DeviceOnly. Ok on success; Unavailable when SecAccessControlCreateWithFlags
    /// returns nil; Error on any other add failure — all DATA.
    ///
    /// NOT ESTABLISHED: whether a device with NO enrolled biometric fails here at ACL
    /// creation, the way Android refuses at provisionKey, or succeeds and fails later
    /// at SecItemAdd. The simulator cannot answer it — it has no Secure Enclave and
    /// does not enforce a SecAccessControl at all. Do not restate this as a guarantee
    /// until a device has shown it (#213 item 1, phase 14.3).
    @discardableResult
    func secureSet(key: String, value: String, requireAuth: Bool) -> Int32 {
        _ = secureDelete(key: key) // idempotent overwrite
        var query = baseQuery(key)
        query[kSecValueData as String] = Data(value.utf8)
        Self.lastSetAccessControlForTest = nil
        if requireAuth {
            var acError: Unmanaged<CFError>?
            guard let access = SecAccessControlCreateWithFlags(
                kCFAllocatorDefault,
                kSecAttrAccessibleWhenUnlockedThisDeviceOnly,
                .biometryCurrentSet,
                &acError) else {
                BnLog.error("BnSecureStorage", "set('\(key)') auth: SecAccessControl create failed — Unavailable")
                return BnSecureStorageStatus.unavailable
            }
            Self.lastSetAccessControlForTest = access // the construction spy — the code REQUESTED the binding
            query[kSecAttrAccessControl as String] = access
            // The enclave-less-simulator detection aids (see `authLabel` / `authBoundAccounts`):
            // the sim neither enforces the ACL nor reliably surfaces it (it returns an
            // accc attribute for PLAIN items too), so the shell stamps a label AND records
            // the account so it can still learn the item is auth-bound. On a real device
            // the OS refuses first, so these are sim affordances, not the security.
            query[kSecAttrLabel as String] = Self.authLabel
            Self.markAuthBound(key)
            // The add must not itself prompt on the sim (there is no gesture to satisfy).
            // A context with interaction disabled keeps SecItemAdd non-interactive.
            let context = Self.makeContext()
            context.interactionNotAllowed = true
            query[kSecUseAuthenticationContext as String] = context
        } else {
            query[kSecAttrAccessible as String] = kSecAttrAccessibleWhenUnlockedThisDeviceOnly
        }
        let status = SecItemAdd(query as CFDictionary, nil)
        if status == errSecSuccess { return BnSecureStorageStatus.ok }
        BnLog.error("BnSecureStorage", "set('\(key)') SecItemAdd → OSStatus \(status)")
        return mapAddError(status)
    }

    /// get (the PLAIN op — NEVER prompts): probe the item with kSecUseAuthenticationUI =
    /// .fail. A non-auth item returns Ok + value; an AUTH-bound item is REFUSED as
    /// AuthFailed — THE CONTRACT: a plain get of a biometric-bound secret must not hand
    /// back the plaintext. On a REAL device the OS refuses first (errSecInteractionNotAllowed);
    /// on the enclave-less SIMULATOR the securityd would return the bytes, so the SHELL
    /// refuses (it knows the item is auth-bound from the ACL/marker — the Android
    /// keyRequiresAuth parity). The OS-LEVEL refusal is the UNPROVEN-until-real-device
    /// half. A missing key is NotFound; anything else Error.
    func secureGet(key: String) -> (status: Int32, value: String?) {
        switch probe(key) {
        case .notFound: return (BnSecureStorageStatus.notFound, nil)
        case .plain(let value): return (BnSecureStorageStatus.ok, value)
        case .authBound: return (BnSecureStorageStatus.authFailed, nil)
        case .failed: return (BnSecureStorageStatus.error, nil)
        }
    }

    /// delete: drop the item. Idempotent — a missing item is still Ok (the Android twin).
    @discardableResult
    func secureDelete(key: String) -> Int32 {
        Self.unmarkAuthBound(key)
        let status = SecItemDelete(baseQuery(key) as CFDictionary)
        if status == errSecSuccess || status == errSecItemNotFound { return BnSecureStorageStatus.ok }
        BnLog.error("BnSecureStorage", "delete('\(key)') SecItemDelete → OSStatus \(status)")
        return BnSecureStorageStatus.error
    }

    // ── getWithAuth (the PAIRING — the gated read) ───────────────────────────────

    /// getWithAuth: NotFound when absent; a NON-auth item is read DIRECTLY (nothing to
    /// gate — no prompt, the Android parity); an AUTH-bound item engages the biometric
    /// gate (the real Face ID sheet in production, the `authGateHookForTest` seam on CI).
    /// A seam/OS AUTHENTICATE returns the plaintext in {"value":…}; a DENY is AuthFailed —
    /// DATA, never a hang. 18.3 (#506): only the auth-bound path holds its key; the other
    /// paths complete at once and take nothing.
    private func secureGetWithAuth(requestId: Int64, key: String, reason: String) {
        switch probe(key) {
        case .notFound:
            complete(requestId, BnSecureStorageStatus.notFound, nil)
        case .plain(let value):
            complete(requestId, BnSecureStorageStatus.ok, valuePayload(value)) // no prompt to show
        case .failed:
            complete(requestId, BnSecureStorageStatus.error, nil)
        case .authBound(let probedValue):
            let context = Self.makeContext()
            lock.lock(); inFlightContexts[requestId] = context; lock.unlock() // retained for the call
            // 18.3 (#506): the answer hops onto workQueue, runs, and then, in a defer so no
            // exit path skips it, releases this request's hold and drains the key's parked
            // requests. The closure keeps the context strongly, as before; self stays weak.
            let gate = BnSecureAuthGate(
                reason: reason,
                hasAuthBoundItem: true,
                authenticate: { [weak self] in
                    guard let self = self else { return }
                    self.onWorkQueue { [weak self] in
                        guard let self = self else { return }
                        defer { self.releaseHold(key, requestId) }
                        self.finishAuthorizedRead(requestId: requestId, key: key, context: context, probedValue: probedValue)
                    }
                },
                deny: { [weak self] in
                    guard let self = self else { return }
                    self.onWorkQueue { [weak self] in
                        guard let self = self else { return }
                        defer { self.releaseHold(key, requestId) }
                        self.complete(requestId, BnSecureStorageStatus.authFailed, nil)
                    }
                })
            takeHold(key, requestId)
            if let hook = Self.authGateHookForTest { hook(gate); return }
            // Production: the OS Face ID evaluation. On success the freshly-evaluated
            // context unlocks the `.biometryCurrentSet` item (finishAuthorizedRead reads
            // it); on failure/cancel/lockout the denial is AuthFailed, DATA.
            context.evaluatePolicy(.deviceOwnerAuthenticationWithBiometrics, localizedReason: reason) { success, _ in
                if success { gate.authenticate() } else { gate.deny() }
            }
        }
    }

    /// The OS-unlocked read of an auth-bound item, run once the gate (OS or seam) grants.
    /// On the enclave-less SIMULATOR the probe ALREADY surfaced the bytes (the ACL is not
    /// enforced), so `probedValue` is the REAL Keychain value the read returns — a genuine
    /// round-trip, only the biometric decision seamed. On a REAL device the probe was
    /// refused (probedValue nil), so this reads the `.biometryCurrentSet` item afresh with
    /// the OS-unlocked context. The value crosses in {"value":…}; a read that yields
    /// nothing is AuthFailed.
    private func finishAuthorizedRead(requestId: Int64, key: String, context: LAContext, probedValue: String?) {
        if let value = probedValue {
            complete(requestId, BnSecureStorageStatus.ok, valuePayload(value))
            return
        }
        var query = baseQuery(key)
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        query[kSecUseAuthenticationContext as String] = context // the OS-unlocked context (real device)
        var out: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &out)
        if status == errSecSuccess, let data = out as? Data, let value = String(data: data, encoding: .utf8) {
            complete(requestId, BnSecureStorageStatus.ok, valuePayload(value))
        } else {
            complete(requestId, BnSecureStorageStatus.authFailed, nil)
        }
    }

    // ── Keychain helpers ─────────────────────────────────────────────────────────

    private enum Probe {
        case notFound
        case plain(String)
        /// Auth-bound. Carries the value when the platform surfaced it (the enclave-less
        /// simulator, which does not enforce the ACL) and nil when the OS refused it (a
        /// real device — the OS-enforced refusal).
        case authBound(String?)
        case failed
    }

    /// Reads the item (data + attributes) with UI DISALLOWED (kSecUseAuthenticationUI =
    /// .fail) so no prompt ever fires, then classifies it:
    ///   • errSecInteractionNotAllowed → auth-bound, value withheld (a REAL device: the OS
    ///     refused the bytes without auth UI — the OS-enforced binding);
    ///   • errSecSuccess for an account in `authBoundAccounts` OR carrying the `authLabel`
    ///     marker → auth-bound WITH the value (the SIMULATOR: no Secure Enclave, so the ACL
    ///     is a no-op and the bytes come back — the shell learns the binding from the
    ///     cache/marker, NOT from the kSecAttrAccessControl attribute, which the sim also
    ///     returns for PLAIN items, and NOT from an OS refusal);
    ///   • errSecSuccess for a plain item → a plain read;
    ///   • errSecItemNotFound → absent.
    private func probe(_ key: String) -> Probe {
        var query = baseQuery(key)
        query[kSecReturnData as String] = true
        query[kSecReturnAttributes as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        query[kSecUseAuthenticationUI as String] = kSecUseAuthenticationUIFail
        var out: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &out)
        switch status {
        case errSecSuccess:
            guard let dict = out as? [String: Any] else { return .failed }
            let value = (dict[kSecValueData as String] as? Data).flatMap { String(data: $0, encoding: .utf8) }
            // Classify off the SHELL's own signals (cache/marker), never the sim's
            // unreliable kSecAttrAccessControl attribute (returned for plain items too).
            let isAuthBound = Self.isMarkedAuthBound(key)
                || (dict[kSecAttrLabel as String] as? String) == Self.authLabel
            if isAuthBound { return .authBound(value) }
            if let value = value { return .plain(value) }
            return .failed
        case errSecInteractionNotAllowed:
            return .authBound(nil) // a REAL device refused the bytes without auth — auth-bound
        case errSecItemNotFound:
            return .notFound
        default:
            BnLog.error("BnSecureStorage", "probe('\(key)') SecItemCopyMatching → OSStatus \(status)")
            return .failed
        }
    }

    private func baseQuery(_ key: String) -> [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: Self.service,
            kSecAttrAccount as String: key,
        ]
    }

    /// SecItemAdd failure → a DATA status: errSecMissingEntitlement / errSecNotAvailable
    /// map to Unavailable (the Keychain is unusable), everything else to Error.
    private func mapAddError(_ status: OSStatus) -> Int32 {
        switch status {
        case errSecMissingEntitlement, errSecNotAvailable:
            return BnSecureStorageStatus.unavailable
        default:
            return BnSecureStorageStatus.error
        }
    }

    /// Every context this handler creates comes from here, timed by `BnLAContextTiming`
    /// (in BnBiometrics.swift) so a pathological cold start is logged.
    private static func makeContext() -> LAContext {
        BnLAContextTiming.timed("BnSecureStorage") { contextFactoryForTest?() ?? LAContext() }
    }

    /// The flat string→string {"value":…} payload the wire carries — the SECOND user of
    /// the host_call_complete payload channel (geolocation's fix is the first). The .NET
    /// ParseSecretResult reads exactly this key.
    private func valuePayload(_ value: String) -> String {
        BnFlatJson.object([("value", value)])
    }

    /// The single completion funnel. Releases the context retained for THIS request, if
    /// any, and no other request's, then delivers the status + optional payload to .NET.
    /// The value crosses as a NUL-terminated UTF-8 C string valid only during the call.
    private func complete(_ requestId: Int64, _ status: Int32, _ payload: String?) {
        lock.lock(); inFlightContexts[requestId] = nil; lock.unlock()

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
