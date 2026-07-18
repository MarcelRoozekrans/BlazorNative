// ─────────────────────────────────────────────────────────────────────────────
// BnSecureStorageTests — Phase 9.2 Gate 3 (M9 DoD #4): the Keychain-backed secret
// store + the OS-key biometric binding on the iOS simulator. The iOS third of
// BnSecureDemoTests.cs (.NET, DevHostBridge headless) + the AVD BnSecureAndroidTest.kt
// (AndroidKeyStore). The bridge/struct/export pins are UNCHANGED — secure storage adds
// op=3 + a wire status enum + a handler + the SECOND user of the {"value":…} payload
// channel, and touches the ABI at nothing else.
//
// THE PROVEN / UNPROVEN SPLIT (the design's honesty, mirroring the AVD gate):
//   • PROVEN here, deterministically, against the REAL Keychain:
//       – the plain (non-auth) store round-trips a secret (set→get→delete→NotFound);
//       – THE OS-KEY BINDING: a plain get of a `.biometryCurrentSet` AUTH item returns
//         AuthFailed — the Keychain refuses the bytes without auth UI
//         (kSecUseAuthenticationUI=.fail → errSecInteractionNotAllowed). The "drop the
//         SecAccessControl" mutation reds this (a plain item then reads back Ok);
//       – getWithAuth of an absent key is NotFound; of a plain item reads DIRECTLY (no
//         prompt); of an auth item engages the gate and a seam-DENY is AuthFailed — DATA,
//         no hang; a seam-AUTHORIZE returns the value in {"value":…};
//       – the iOS-vs-Android ASYMMETRY: an auth-bound `set` does NOT prompt on iOS.
//   • UNPROVEN until a physical iPhone (the M9 deferral, named not smuggled): the REAL
//     Face ID sheet + the TEE-enforced Secure-Enclave read behind it (the simulator's
//     enrolled Face ID is a menu toggle, not a TrueDepth capture, and the real gesture is
//     not drivable in a hosted XCTest). CI drives the outcome through
//     `authGateHookForTest`, the geolocation/notifications real-dialog-bypass twin.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit
@testable import BnHost

final class BnSecureStorageTests: BnHostTestCase {

    private let plainKey = "bn_test_plain"
    private let authKey = "bn_test_auth"
    private let demoKey = "demo-secret" // mirrors BnSecureDemo.Key

    private let capturedLock = NSLock()
    private var captured: [(id: Int64, status: Int32, payload: String?)] = []
    private var runtime: BnRuntime?
    private var root: UIView!

    override func setUp() {
        super.setUp()
        BnSecureStorage.resetForTest()
        purgeKeys()
        captured = []
    }

    override func tearDown() {
        BnSecureStorage.resetForTest()
        purgeKeys()
        super.tearDown()
    }

    /// Drop every key this suite touches from the REAL Keychain so no item leaks across
    /// tests in the same simulator session (the Keychain outlives a single test).
    private func purgeKeys() {
        let bridge = AppleShellBridge()
        for key in [plainKey, authKey, demoKey] { _ = bridge.secureStorage.secureDelete(key: key) }
    }

    private func installCapture() {
        BnSecureStorage.completeHookForTest = { [weak self] id, status, payload in
            guard let self = self else { return 0 }
            self.capturedLock.lock(); self.captured.append((id, status, payload)); self.capturedLock.unlock()
            return 0
        }
    }

    private func captured1() -> (status: Int32, payload: String?)? {
        capturedLock.lock(); defer { capturedLock.unlock() }
        guard let last = captured.last else { return nil }
        return (last.status, last.payload)
    }

    // ── The wire status enum + op value — the three-way mirror pinned ─────────────

    func testTheWireStatusConstantsMatchTheThreeWayContract() {
        // The EXACT integers .NET SecureStorageStatus / Kotlin SecureStorageStatus carry.
        XCTAssertEqual(BnSecureStorageStatus.ok, 0)
        XCTAssertEqual(BnSecureStorageStatus.notFound, 1)
        XCTAssertEqual(BnSecureStorageStatus.authFailed, 2)
        XCTAssertEqual(BnSecureStorageStatus.unavailable, 3)
        XCTAssertEqual(BnSecureStorageStatus.error, 4)
        XCTAssertEqual(BnHostCallOp.secureStorage, 3)
    }

    // ── The plain (non-auth) store round-trips through the REAL Keychain ──────────

    func testPlainSecretRoundTripsThroughTheKeychain() {
        let bridge = AppleShellBridge()
        XCTAssertEqual(bridge.secureStorage.secureSet(key: plainKey, value: "hunter2", requireAuth: false),
                       BnSecureStorageStatus.ok)

        let got = bridge.secureStorage.secureGet(key: plainKey)
        XCTAssertEqual(got.status, BnSecureStorageStatus.ok)
        XCTAssertEqual(got.value, "hunter2", "the real Keychain round-trips the exact bytes")

        XCTAssertEqual(bridge.secureStorage.secureDelete(key: plainKey), BnSecureStorageStatus.ok)
        XCTAssertEqual(bridge.secureStorage.secureGet(key: plainKey).status, BnSecureStorageStatus.notFound)
    }

    func testGetOfAnAbsentKeyIsNotFound() {
        let bridge = AppleShellBridge()
        XCTAssertEqual(bridge.secureStorage.secureGet(key: plainKey).status, BnSecureStorageStatus.notFound)
    }

    func testDeleteIsIdempotentOk() {
        let bridge = AppleShellBridge()
        // Delete of an absent key is still Ok (nothing to remove) — the Android twin.
        XCTAssertEqual(bridge.secureStorage.secureDelete(key: plainKey), BnSecureStorageStatus.ok)
    }

    // ── THE OS-KEY BINDING: a plain get of an auth-bound item AuthFails ───────────

    func testPlainGetOfAnAuthBoundItemAuthFailsTheOsKeyBinding() {
        let bridge = AppleShellBridge()
        // An auth-bound set provisions the item under a SecAccessControl(.biometryCurrentSet)
        // — the OS gates RETRIEVAL. Note it does NOT prompt on the WRITE (the iOS asymmetry).
        XCTAssertEqual(bridge.secureStorage.secureSet(key: authKey, value: "hunter2", requireAuth: true),
                       BnSecureStorageStatus.ok, "auth-bound set must store the .biometryCurrentSet item")

        // A plain get cannot satisfy the item's biometric requirement, and it never
        // prompts (kSecUseAuthenticationUI=.fail) — the OS returns the bytes to NO ONE
        // without auth. THE SECURITY CONTRACT: AuthFailed, never the plaintext.
        let got = bridge.secureStorage.secureGet(key: authKey)
        XCTAssertEqual(got.status, BnSecureStorageStatus.authFailed,
                       "the OS-key binding must refuse a plain get of an auth-bound item")
        XCTAssertNil(got.value, "an AuthFailed get carries no value")
    }

    // ── getWithAuth: absent → NotFound (before any prompt) ───────────────────────

    func testGetWithAuthOfAnAbsentSecretIsNotFound() {
        installCapture()
        let bridge = AppleShellBridge()
        _ = bridge.hostCallBegin(40, BnHostCallOp.secureStorage,
                                 "{\"action\":\"getWithAuth\",\"key\":\"\(authKey)\",\"reason\":\"Unlock\"}")
        XCTAssertEqual(captured1()?.status, BnSecureStorageStatus.notFound)
    }

    // ── getWithAuth: the PAIRING — a seam-authorize returns the value in {"value":…} ─

    func testGetWithAuthReturnsTheValueViaTheSeam() {
        let bridge = AppleShellBridge()
        var gateHadAuthBoundItem: Bool?
        // The gate stands in for the un-drivable Face ID sheet; it AUTHORIZES → the shell
        // completes the OS-unlocked read and the value crosses in the {"value":…} payload.
        BnSecureStorage.authGateHookForTest = { gate in
            gateHadAuthBoundItem = gate.hasAuthBoundItem
            gate.authenticate()
        }
        // Store behind the gate (retains the seam plaintext) THEN unlock through it.
        XCTAssertEqual(bridge.secureStorage.secureSet(key: authKey, value: "hunter2", requireAuth: true),
                       BnSecureStorageStatus.ok)
        installCapture()
        _ = bridge.hostCallBegin(41, BnHostCallOp.secureStorage,
                                 "{\"action\":\"getWithAuth\",\"key\":\"\(authKey)\",\"reason\":\"Unlock your secret\"}")

        XCTAssertEqual(captured1()?.status, BnSecureStorageStatus.ok, "a seam-authorized getWithAuth unlocks")
        XCTAssertEqual(captured1()?.payload, "{\"value\":\"hunter2\"}", "the plaintext returns in the {\"value\":…} payload")
        XCTAssertEqual(gateHadAuthBoundItem, true, "the gate is engaged ONLY for an auth-bound item (the read-side binding)")
    }

    // ── getWithAuth: a seam-DENY is AuthFailed — DATA, no hang ────────────────────

    func testGetWithAuthDenialIsAuthFailedNoHang() {
        let bridge = AppleShellBridge()
        BnSecureStorage.authGateHookForTest = { gate in gate.deny() }
        XCTAssertEqual(bridge.secureStorage.secureSet(key: authKey, value: "hunter2", requireAuth: true),
                       BnSecureStorageStatus.ok)
        installCapture()
        _ = bridge.hostCallBegin(42, BnHostCallOp.secureStorage,
                                 "{\"action\":\"getWithAuth\",\"key\":\"\(authKey)\",\"reason\":\"Unlock\"}")
        // A denied gate resolves to AuthFailed — a value, never a Swift throw, never a hang.
        XCTAssertEqual(captured1()?.status, BnSecureStorageStatus.authFailed)
        XCTAssertNil(captured1()?.payload)
    }

    // ── getWithAuth of a PLAIN item reads directly (no prompt, no gate) ───────────

    func testGetWithAuthOfAPlainItemReadsDirectlyWithoutPrompt() {
        let bridge = AppleShellBridge()
        var gateEngaged = false
        BnSecureStorage.authGateHookForTest = { gate in gateEngaged = true; gate.deny() }
        XCTAssertEqual(bridge.secureStorage.secureSet(key: plainKey, value: "hunter2", requireAuth: false),
                       BnSecureStorageStatus.ok)
        installCapture()
        _ = bridge.hostCallBegin(43, BnHostCallOp.secureStorage,
                                 "{\"action\":\"getWithAuth\",\"key\":\"\(plainKey)\",\"reason\":\"Unlock\"}")

        XCTAssertEqual(captured1()?.status, BnSecureStorageStatus.ok, "a plain item needs no gate — read directly")
        XCTAssertEqual(captured1()?.payload, "{\"value\":\"hunter2\"}")
        XCTAssertFalse(gateEngaged, "a plain item must NOT engage the biometric gate")
    }

    // ── The iOS-vs-Android ASYMMETRY: an auth-bound set does NOT prompt ──────────

    func testAuthBoundSetDoesNotPromptTheIosAsymmetry() {
        let bridge = AppleShellBridge()
        var gateEngaged = false
        BnSecureStorage.authGateHookForTest = { gate in gateEngaged = true; gate.deny() }
        // The `.biometryCurrentSet` ACL gates RETRIEVAL, not storage — SecItemAdd stores
        // the item with NO biometric gesture. (Android's auth-bound set DOES prompt: an AES
        // per-use-auth key's ENCRYPT also needs a fresh auth. Same wire, different write UX.)
        let status = bridge.secureStorage.secureSet(key: authKey, value: "hunter2", requireAuth: true)
        XCTAssertEqual(status, BnSecureStorageStatus.ok, "the auth-bound set completes synchronously — no prompt")
        XCTAssertFalse(gateEngaged, "iOS does NOT engage a biometric gate on an auth-bound WRITE")
    }

    // ── Error (4) is DATA: an unknown action never crashes ────────────────────────

    func testUnknownActionCompletesErrorAsData() {
        installCapture()
        let bridge = AppleShellBridge()
        let rc = bridge.hostCallBegin(44, BnHostCallOp.secureStorage, "{\"action\":\"frobnicate\",\"key\":\"x\"}")
        XCTAssertEqual(rc, 0, "begin returns synchronously even for an unknown action")
        XCTAssertEqual(captured1()?.status, BnSecureStorageStatus.error)
    }

    // ── The op routes to the secure-storage handler (op=3) ───────────────────────

    func testHostCallBeginRoutesTheSecureStorageOpToTheHandler() {
        installCapture()
        let bridge = AppleShellBridge()
        // op=3 must reach BnSecureStorage (a get of an absent key → NotFound), NOT
        // geolocation(0)/notifications(1)/biometrics(2).
        _ = bridge.hostCallBegin(45, BnHostCallOp.secureStorage, "{\"action\":\"get\",\"key\":\"\(plainKey)\"}")
        XCTAssertEqual(captured1()?.status, BnSecureStorageStatus.notFound)
    }

    // ── BOOT: the pairing round-trips through the REAL host_call_complete (/secure) ─

    func testSetThenUnlockRoundTripsThePairingThroughTheRealHostCallComplete() throws {
        // The gate authorizes (the seam) so the demo's Unlock returns the stored value.
        BnSecureStorage.authGateHookForTest = { gate in gate.authenticate() }
        let form = try bootSecureDemo()

        try tapButton("Set", in: form) // SetAsync("demo-secret", "hunter2", requireAuth:true)
        XCTAssertTrue(pollUntil { self.echoLabel()?.text == "status:Ok" },
                      "Set never round-tripped Ok to the echo (a hang or mis-route)")

        try tapButton("Unlock", in: form) // GetWithAuthAsync → the pairing
        XCTAssertTrue(pollUntil { self.echoLabel()?.text == "value:hunter2" },
                      "the pairing never round-tripped the unlocked value to the echo (a hang or the OS-unlock model broke)")
        XCTAssertEqual(BnSecureStorage.lastHostCallCompleteRcForTest, 0,
                       "host_call_complete did not route to the in-flight .NET requestId")
    }

    func testUnlockAfterDeleteIsNotFoundWithinABoundedAwaitNoHang() throws {
        let form = try bootSecureDemo()

        try tapButton("Delete", in: form)
        XCTAssertTrue(pollUntil { self.echoLabel()?.text == "status:Ok" }, "Delete never echoed Ok")

        try tapButton("Unlock", in: form) // getWithAuth of the now-absent secret
        // NotFound is reached BEFORE any prompt — DATA within a bounded await, no hang.
        XCTAssertTrue(pollUntil { self.echoLabel()?.text == "status:NotFound" },
                      "Unlock of an absent secret never echoed NotFound (a HANG?)")
        XCTAssertEqual(BnSecureStorage.lastHostCallCompleteRcForTest, 0)
    }

    // ── Boot + tree accessors (the BnNotificationsTests house style) ──────────────

    struct BootTimeout: Error {}

    private func bootSecureDemo() throws -> UIView {
        root = UIView(frame: CGRect(x: 0, y: 0, width: 390, height: 844))
        let mapper = bnMapper(root: root)
        let rt = BnRuntime(mapper: mapper)
        rt.onError = { msg, err in NSLog("[BnSecureStorageTests] \(msg): \(err)") }
        self.runtime = rt
        try rt.start(component: "BnSecureDemo", os: "ios")
        guard pollUntil(deadline: 30, { self.probeForm() != nil }), let form = probeForm() else {
            XCTFail("BnSecureDemo never rendered its Authenticate/Set/Unlock/Delete/echo tree within 30s")
            throw BootTimeout()
        }
        return form
    }

    private func probeForm() -> UIView? {
        guard let form = root.subviews.first, form.subviews.count >= 5 else { return nil }
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
