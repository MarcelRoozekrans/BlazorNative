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
    static let unavailable: Int32 = 3 // no secure hardware / Keychain unusable / biometrics not enrolled
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

    /// STRONG. The `LAContext` under a getWithAuth evaluation — held for the call's
    /// duration so a deallocated context cannot cancel the in-flight read (the
    /// CLLocationManager/LAContext retention lesson). Cleared by `complete`.
    private var inFlightContext: LAContext?

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
    private static var authBoundAccounts: Set<String> = []

    /// Intercepts the completion so a PURE unit test (no NativeAOT boot) observes the
    /// routed (requestId, status, payload) without a live .NET continuation.
    static var completeHookForTest: ((Int64, Int32, String?) -> Int32)?

    /// The rc of the most recent `blazornative_host_call_complete` — 0 = delivered to a
    /// live .NET continuation, 1 = unknown/already-completed id (benign). Int32.min
    /// before any completion.
    static var lastHostCallCompleteRcForTest: Int32 = Int32.min

    static func resetForTest() {
        authGateHookForTest = nil
        lastSetAccessControlForTest = nil
        authBoundAccounts = []
        completeHookForTest = nil
        lastHostCallCompleteRcForTest = Int32.min
    }

    // ── The op entry (AppleShellBridge.hostCallBegin forwards here for op=SecureStorage) ──

    /// Parses the flat-JSON `action` + key and dispatches. Returns FAST (the begin
    /// contract); the terminal status is a deferred `complete(...)`. set/get/delete are
    /// synchronous (no prompt — the iOS asymmetry: even an auth-bound set does not
    /// prompt); getWithAuth of an auth item suspends behind the gate.
    func begin(requestId: Int64, argsJson: String) {
        let args = BnFlatJson.parseObject(argsJson) ?? [:]
        let key = args["key"] ?? ""
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

    /// An unknown host-call op: DATA (Error), not a crash — the awaiting .NET ValueTask
    /// resolves rather than leaking a pending entry (the geolocation unknown-op posture).
    func completeUnknownOp(requestId: Int64) {
        complete(requestId, BnSecureStorageStatus.error, nil)
    }

    // ── set / get / delete cores (also called DIRECTLY by the keystore-half tests) ──

    /// set: idempotent (drop any existing item, then add). auth=1 attaches a
    /// SecAccessControl `.biometryCurrentSet` (retrieval is biometric-gated; the add does
    /// NOT prompt — the iOS asymmetry). auth=0 uses kSecAttrAccessibleWhenUnlockedThis-
    /// DeviceOnly. Ok on success; Unavailable when the ACL cannot be created (no secure
    /// hardware / none enrolled); Error on any other add failure — all DATA.
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
                NSLog("[BnSecureStorage] set('\(key)') auth: SecAccessControl create failed — Unavailable")
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
            Self.authBoundAccounts.insert(key)
            // The add must not itself prompt on the sim (there is no gesture to satisfy).
            // A context with interaction disabled keeps SecItemAdd non-interactive.
            let context = LAContext()
            context.interactionNotAllowed = true
            query[kSecUseAuthenticationContext as String] = context
        } else {
            query[kSecAttrAccessible as String] = kSecAttrAccessibleWhenUnlockedThisDeviceOnly
        }
        let status = SecItemAdd(query as CFDictionary, nil)
        if status == errSecSuccess { return BnSecureStorageStatus.ok }
        NSLog("[BnSecureStorage] set('\(key)') SecItemAdd → OSStatus \(status)")
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
        Self.authBoundAccounts.remove(key)
        let status = SecItemDelete(baseQuery(key) as CFDictionary)
        if status == errSecSuccess || status == errSecItemNotFound { return BnSecureStorageStatus.ok }
        NSLog("[BnSecureStorage] delete('\(key)') SecItemDelete → OSStatus \(status)")
        return BnSecureStorageStatus.error
    }

    // ── getWithAuth (the PAIRING — the gated read) ───────────────────────────────

    /// getWithAuth: NotFound when absent; a NON-auth item is read DIRECTLY (nothing to
    /// gate — no prompt, the Android parity); an AUTH-bound item engages the biometric
    /// gate (the real Face ID sheet in production, the `authGateHookForTest` seam on CI).
    /// A seam/OS AUTHENTICATE returns the plaintext in {"value":…}; a DENY is AuthFailed —
    /// DATA, never a hang.
    private func secureGetWithAuth(requestId: Int64, key: String, reason: String) {
        switch probe(key) {
        case .notFound:
            complete(requestId, BnSecureStorageStatus.notFound, nil)
        case .plain(let value):
            complete(requestId, BnSecureStorageStatus.ok, valuePayload(value)) // no prompt to show
        case .failed:
            complete(requestId, BnSecureStorageStatus.error, nil)
        case .authBound(let probedValue):
            let context = LAContext()
            lock.lock(); inFlightContext = context; lock.unlock() // retained for the call
            let gate = BnSecureAuthGate(
                reason: reason,
                hasAuthBoundItem: true,
                authenticate: { [weak self] in self?.finishAuthorizedRead(requestId: requestId, key: key, context: context, probedValue: probedValue) },
                deny: { [weak self] in self?.complete(requestId, BnSecureStorageStatus.authFailed, nil) })
            if let hook = Self.authGateHookForTest { hook(gate); return }
            // Production: the OS Face ID evaluation. On success the freshly-evaluated
            // context unlocks the `.biometryCurrentSet` item (finishAuthorizedRead reads
            // it); on failure/cancel/lockout the denial is AuthFailed, DATA.
            context.evaluatePolicy(.deviceOwnerAuthentication, localizedReason: reason) { success, _ in
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
            let isAuthBound = Self.authBoundAccounts.contains(key)
                || (dict[kSecAttrLabel as String] as? String) == Self.authLabel
            if isAuthBound { return .authBound(value) }
            if let value = value { return .plain(value) }
            return .failed
        case errSecInteractionNotAllowed:
            return .authBound(nil) // a REAL device refused the bytes without auth — auth-bound
        case errSecItemNotFound:
            return .notFound
        default:
            NSLog("[BnSecureStorage] probe('\(key)') SecItemCopyMatching → OSStatus \(status)")
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

    /// The flat string→string {"value":…} payload the wire carries — the SECOND user of
    /// the host_call_complete payload channel (geolocation's fix is the first). The .NET
    /// ParseSecretResult reads exactly this key.
    private func valuePayload(_ value: String) -> String {
        BnFlatJson.object([("value", value)])
    }

    /// The single completion funnel. Releases the retained context iff the id matches
    /// (one-shot), then delivers the status + optional payload to .NET. The value crosses
    /// as a NUL-terminated UTF-8 C string valid only during the call.
    private func complete(_ requestId: Int64, _ status: Int32, _ payload: String?) {
        lock.lock(); inFlightContext = nil; lock.unlock()

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
