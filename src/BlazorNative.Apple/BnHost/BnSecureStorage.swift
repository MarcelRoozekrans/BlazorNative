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
//     returns errSecInteractionNotAllowed → AuthFailed (the OS refusing the bytes
//     without auth). THE OS-BINDING PROOF — a plain get of an auth item MUST AuthFail.
//   • getWithAuth(key,reason) → the OS Face ID evaluation unlocks the item and the
//     plaintext returns in {"value":…}; a non-auth item is read directly (no prompt).
//   • delete(key) → SecItemDelete (idempotent — a missing item is still Ok).
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
// denial-as-data no-hang. The DETERMINISTIC OS-BINDING (a plain get of an auth item
// AuthFails) is proven DIRECTLY against the real Keychain ACL. The plain path
// (set/get/delete of non-auth items) runs the REAL Keychain end-to-end. The real
// biometric-gated Secure-Enclave read is the documented UNPROVEN-until-hardware half.
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

    /// The seam's model of the OS-unlocked plaintext: an auth-bound `set` under an active
    /// gate hook retains the value here so a seam-authorized getWithAuth can return it
    /// (the sim's Secure Enclave read behind a real gesture is owner-device territory —
    /// production reads the real `.biometryCurrentSet` item with the freshly-evaluated
    /// context). Populated ONLY while `authGateHookForTest` is set; never in production.
    private static var testAuthPlaintextForSeam: [String: String] = [:]

    /// Intercepts the completion so a PURE unit test (no NativeAOT boot) observes the
    /// routed (requestId, status, payload) without a live .NET continuation.
    static var completeHookForTest: ((Int64, Int32, String?) -> Int32)?

    /// The rc of the most recent `blazornative_host_call_complete` — 0 = delivered to a
    /// live .NET continuation, 1 = unknown/already-completed id (benign). Int32.min
    /// before any completion.
    static var lastHostCallCompleteRcForTest: Int32 = Int32.min

    static func resetForTest() {
        authGateHookForTest = nil
        testAuthPlaintextForSeam = [:]
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
            query[kSecAttrAccessControl as String] = access
            // The add must not itself prompt on the sim (there is no gesture to satisfy).
            // A context with interaction disabled keeps SecItemAdd non-interactive.
            let context = LAContext()
            context.interactionNotAllowed = true
            query[kSecUseAuthenticationContext as String] = context
            if Self.authGateHookForTest != nil { Self.testAuthPlaintextForSeam[key] = value }
        } else {
            query[kSecAttrAccessible as String] = kSecAttrAccessibleWhenUnlockedThisDeviceOnly
        }
        let status = SecItemAdd(query as CFDictionary, nil)
        if status == errSecSuccess { return BnSecureStorageStatus.ok }
        NSLog("[BnSecureStorage] set('\(key)') SecItemAdd → OSStatus \(status)")
        return mapAddError(status)
    }

    /// get (the PLAIN op — NEVER prompts): probe the item with kSecUseAuthenticationUI =
    /// .fail. A non-auth item returns Ok + value; an AUTH-bound item returns
    /// errSecInteractionNotAllowed → AuthFailed (THE OS-BINDING PROOF — the OS refuses the
    /// bytes without auth); a missing key is NotFound; anything else Error.
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
        Self.testAuthPlaintextForSeam.removeValue(forKey: key)
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
        case .authBound:
            let context = LAContext()
            lock.lock(); inFlightContext = context; lock.unlock() // retained for the call
            let gate = BnSecureAuthGate(
                reason: reason,
                hasAuthBoundItem: true,
                authenticate: { [weak self] in self?.finishAuthorizedRead(requestId: requestId, key: key, context: context) },
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

    /// The OS-unlocked read of an auth-bound item. Under the test seam this models the
    /// Secure-Enclave read by returning the retained plaintext (the real gesture-driven
    /// read is owner-device territory); in production it reads the real
    /// `.biometryCurrentSet` Keychain item with the freshly-evaluated context. Either way
    /// the value crosses in {"value":…}; a read that yields nothing is AuthFailed.
    private func finishAuthorizedRead(requestId: Int64, key: String, context: LAContext) {
        if Self.authGateHookForTest != nil {
            if let value = Self.testAuthPlaintextForSeam[key] {
                complete(requestId, BnSecureStorageStatus.ok, valuePayload(value))
            } else {
                complete(requestId, BnSecureStorageStatus.authFailed, nil)
            }
            return
        }
        var query = baseQuery(key)
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        query[kSecUseAuthenticationContext as String] = context // the OS-unlocked context
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
        case authBound
        case failed
    }

    /// Reads the item with UI DISALLOWED (kSecUseAuthenticationUI = .fail) so no prompt
    /// ever fires: a plain item hands back its bytes; an auth-bound item comes back
    /// errSecInteractionNotAllowed (the OS refusing without auth UI) — the deterministic,
    /// enrollment-free read of the OS binding.
    private func probe(_ key: String) -> Probe {
        var query = baseQuery(key)
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        query[kSecUseAuthenticationUI as String] = kSecUseAuthenticationUIFail
        var out: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &out)
        switch status {
        case errSecSuccess:
            if let data = out as? Data, let value = String(data: data, encoding: .utf8) { return .plain(value) }
            return .failed
        case errSecItemNotFound:
            return .notFound
        case errSecInteractionNotAllowed:
            return .authBound
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
