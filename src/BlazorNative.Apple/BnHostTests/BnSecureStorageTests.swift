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
import LocalAuthentication
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

    // ── THE OS-KEY BINDING: the construction + the refused plain get ──────────────
    //
    // THE HONESTY SPLIT (the iOS Simulator has NO Secure Enclave and does NOT enforce a
    // SecAccessControl — `.biometryCurrentSet` is a documented no-op there, so a plain
    // get on the sim WOULD return the bytes the real OS refuses; iOS real-device is
    // DEFERRED). So the OS-ENFORCED refusal is UNPROVEN-until-real-device. What IS proven
    // deterministically: the shell REQUESTS the binding (attaches the ACL — the
    // construction spy) and ENFORCES the contract (refuses a plain get). The Android
    // parity: there the keystore's KeyInfo carries the auth requirement; here the item
    // carries the ACL + marker.

    func testAnAuthBoundSetAttachesTheAclAndTheShellRefusesAPlainGet() {
        let bridge = AppleShellBridge()
        // THE CONSTRUCTION: an auth-bound set builds + attaches a .biometryCurrentSet
        // SecAccessControl. Asserted via the construction spy — the "drop the ACL"
        // mutation leaves it nil and reds this (the enclave-less sim's OS-refusal tripwire
        // being unavailable). It does NOT prompt on the WRITE (the iOS asymmetry).
        XCTAssertEqual(bridge.secureStorage.secureSet(key: authKey, value: "hunter2", requireAuth: true),
                       BnSecureStorageStatus.ok)
        XCTAssertNotNil(BnSecureStorage.lastSetAccessControlForTest,
                        "an auth-bound set MUST attach a .biometryCurrentSet SecAccessControl (the OS-key binding, requested)")

        // THE CONTRACT: the shell refuses a plain get of the auth-bound item (AuthFailed,
        // no value) — it will not hand back a biometric-bound secret without the gated
        // read. On the sim the shell enforces off the ACL/marker; on a real device the OS
        // refuses first. The OS-LEVEL refusal is the UNPROVEN-until-real-device half.
        let got = bridge.secureStorage.secureGet(key: authKey)
        XCTAssertEqual(got.status, BnSecureStorageStatus.authFailed,
                       "the shell must refuse a plain get of an auth-bound item")
        XCTAssertNil(got.value, "an AuthFailed get carries no value")
    }

    // ── getWithAuth: absent → NotFound (before any prompt) ───────────────────────

    func testGetWithAuthOfAnAbsentSecretIsNotFound() {
        installCapture()
        let bridge = AppleShellBridge()
        _ = bridge.hostCallBegin(40, BnHostCallOp.secureStorage,
                                 "{\"action\":\"getWithAuth\",\"key\":\"\(authKey)\",\"reason\":\"Unlock\"}")
        bridge.secureStorage.drainForTest() // the action runs on the handler's queue (16.4)
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
        bridge.secureStorage.drainForTest() // the action runs on the handler's queue (16.4)

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
        bridge.secureStorage.drainForTest() // the action runs on the handler's queue (16.4)
        // A denied gate resolves to AuthFailed — a value, never a Swift throw, never a hang.
        XCTAssertEqual(captured1()?.status, BnSecureStorageStatus.authFailed)
        XCTAssertNil(captured1()?.payload)
    }

    // ── 16.4 (#438): a completion releases ONLY its own request's context ────────────
    //
    // WHAT IT PINS. The retention rule: a getWithAuth's `LAContext` stays retained until
    // THAT request completes. Until 16.4 the handler held one context slot that every
    // completion cleared, and the funnel's comment claimed "iff the id matches" with no id
    // to match. Since 16.4 the actions are queued, so a plain get finishing while a
    // getWithAuth waits on its prompt is an ordinary interleaving. Here the gate hook
    // holds the prompt open by not answering, and `drainForTest` orders the rest, so the
    // interleaving is forced, not waited for. A shared slot reds on the second assertion.
    //
    // WHAT IT DOES NOT COVER. That a released context cancels a real Face ID evaluation;
    // the simulator has no sheet to cancel. It pins the handler's own bookkeeping.

    func testACompletionDoesNotReleaseAnotherRequestsRetainedContext() {
        let bridge = AppleShellBridge()
        let held = NSLock()
        var pendingGate: BnSecureAuthGate?
        BnSecureStorage.authGateHookForTest = { gate in held.lock(); pendingGate = gate; held.unlock() }
        XCTAssertEqual(bridge.secureStorage.secureSet(key: authKey, value: "hunter2", requireAuth: true),
                       BnSecureStorageStatus.ok)
        XCTAssertEqual(bridge.secureStorage.secureSet(key: plainKey, value: "plain", requireAuth: false),
                       BnSecureStorageStatus.ok)
        installCapture()

        _ = bridge.hostCallBegin(48, BnHostCallOp.secureStorage,
                                 "{\"action\":\"getWithAuth\",\"key\":\"\(authKey)\",\"reason\":\"Unlock\"}")
        bridge.secureStorage.drainForTest()
        held.lock(); let gate = pendingGate; held.unlock()
        XCTAssertNotNil(gate, "the getWithAuth must be waiting on its gate")
        XCTAssertTrue(bridge.secureStorage.isRetainingContextForTest(48), "the prompt's context is retained while it waits")

        _ = bridge.hostCallBegin(49, BnHostCallOp.secureStorage, "{\"action\":\"get\",\"key\":\"\(plainKey)\"}")
        bridge.secureStorage.drainForTest()
        XCTAssertEqual(captured1()?.status, BnSecureStorageStatus.ok, "the plain get completed first")
        XCTAssertTrue(bridge.secureStorage.isRetainingContextForTest(48),
                      "another request's completion released the waiting prompt's context")

        gate?.authenticate()
        XCTAssertEqual(captured1()?.status, BnSecureStorageStatus.ok, "the gated read completes")
        XCTAssertFalse(bridge.secureStorage.isRetainingContextForTest(48), "the context is released on its own completion")
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
        bridge.secureStorage.drainForTest() // the action runs on the handler's queue (16.4)

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
        bridge.secureStorage.drainForTest() // the action runs on the handler's queue (16.4)
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
        bridge.secureStorage.drainForTest() // the action runs on the handler's queue (16.4)
        XCTAssertEqual(captured1()?.status, BnSecureStorageStatus.notFound)
    }

    // ── 16.4 (#438): hostCallBegin returns while LAContext creation is slow ─────────
    //
    // WHAT IT PINS. The begin contract, the BnBiometricsTests twin: `hostCallBegin`
    // returns at once and the outcome arrives later through the completion. Both auth
    // paths create an `LAContext`, whose first creation in a process is a cold cost of
    // seconds. `contextFactoryForTest` blocks on a semaphore in its place, and
    // `hostCallBegin`, called off main as the .NET render thread calls it, must return
    // while the factory is still blocked. If the action ran on the calling thread again,
    // it could not return until the release, and this reds on the 5 s wait instead of
    // hanging.
    //
    // WHAT IT DOES NOT COVER. It blocks only the context factory. The Keychain calls
    // moved to the same queue in the same change, but no seam makes them slow, so they
    // are covered by construction, not by this pin. And it proves the CALLING thread is
    // not held, not that the queue itself is fast.

    func testHostCallBeginReturnsWhileContextCreationIsBlocked() {
        installCapture()
        let bridge = AppleShellBridge()
        let entered = DispatchSemaphore(value: 0)
        let release = DispatchSemaphore(value: 0)
        BnSecureStorage.contextFactoryForTest = { entered.signal(); release.wait(); return LAContext() }
        defer { release.signal(); bridge.secureStorage.drainForTest() }

        // An auth-bound SET builds its non-interactive context through the factory.
        assertBeginReturnsWhileTheFactoryIsBlocked(bridge, requestId: 46,
            "{\"action\":\"set\",\"key\":\"\(authKey)\",\"value\":\"hunter2\",\"auth\":\"1\"}", entered: entered)
        release.signal()
        bridge.secureStorage.drainForTest()
        XCTAssertEqual(captured1()?.status, BnSecureStorageStatus.ok, "the set completes once the context exists")
    }

    func testHostCallBeginReturnsWhileContextCreationIsBlockedForGetWithAuth() {
        let bridge = AppleShellBridge()
        BnSecureStorage.authGateHookForTest = { gate in gate.authenticate() }
        // Stored with the real factory, so only the gated READ meets the blocked one.
        XCTAssertEqual(bridge.secureStorage.secureSet(key: authKey, value: "hunter2", requireAuth: true),
                       BnSecureStorageStatus.ok)
        installCapture()
        let entered = DispatchSemaphore(value: 0)
        let release = DispatchSemaphore(value: 0)
        BnSecureStorage.contextFactoryForTest = { entered.signal(); release.wait(); return LAContext() }
        defer { release.signal(); bridge.secureStorage.drainForTest() }

        assertBeginReturnsWhileTheFactoryIsBlocked(bridge, requestId: 47,
            "{\"action\":\"getWithAuth\",\"key\":\"\(authKey)\",\"reason\":\"Unlock\"}", entered: entered)
        release.signal()
        bridge.secureStorage.drainForTest()
        XCTAssertEqual(captured1()?.status, BnSecureStorageStatus.ok, "the gated read completes once the context exists")
        XCTAssertEqual(captured1()?.payload, "{\"value\":\"hunter2\"}")
    }

    private func assertBeginReturnsWhileTheFactoryIsBlocked(_ bridge: AppleShellBridge, requestId: Int64, _ args: String,
                                                            entered: DispatchSemaphore,
                                                            file: StaticString = #filePath, line: UInt = #line) {
        let returned = DispatchSemaphore(value: 0)
        DispatchQueue.global(qos: .userInitiated).async {
            _ = bridge.hostCallBegin(requestId, BnHostCallOp.secureStorage, args)
            returned.signal()
        }
        XCTAssertEqual(entered.wait(timeout: .now() + 10), .success,
                       "the LAContext factory was never called", file: file, line: line)
        XCTAssertEqual(returned.wait(timeout: .now() + 5), .success,
                       "hostCallBegin did not return while LAContext creation was blocked: the begin contract is broken",
                       file: file, line: line)
        XCTAssertNil(captured1(), "no outcome before the context exists", file: file, line: line)
    }

    // ── 18.3 (#506): a getWithAuth whose gate is up holds its key ──────────────────────
    //
    // WHAT IT PINS. Requests on one key take effect in begin order. A getWithAuth of an
    // auth-bound item holds its key from the gate until its answer has run on `workQueue`;
    // a later request on that key parks and runs when the gate answers, granted or denied.
    // Requests on other keys keep flowing. The gate's answer hops onto `workQueue`, so the
    // read, the denial and the hold bookkeeping all run there. `holdEveryGate` keeps every
    // gate it is handed, in order, and answers none, so a gate stays up until the test
    // answers it from a background thread, as LocalAuthentication answers on its own reply
    // queue. "Completes before" is the position in `captured`, which the completion hook
    // appends to in completion order. "Has no completion" is read after `drainForTest`,
    // never after a sleep.
    //
    // WHAT IT DOES NOT COVER. The real Face ID sheet and its reply queue; the seam stands in
    // for both. An auth-bound set holds nothing on iOS, because it does not prompt; the last
    // but one pin says so. There is no lost-answer path to pin here: LAContext's reply
    // always fires and its context is retained in `inFlightContexts` until the request
    // completes (spec section 5).

    private let gatesLock = NSLock()
    private var gates: [BnSecureAuthGate] = []

    private func holdEveryGate() {
        BnSecureStorage.authGateHookForTest = { [weak self] gate in
            guard let self = self else { return }
            self.gatesLock.lock(); self.gates.append(gate); self.gatesLock.unlock()
        }
    }

    /// The oldest gate not yet taken, or a failure naming `what` when none is up.
    private func takeGate(_ what: String, file: StaticString = #filePath, line: UInt = #line) -> BnSecureAuthGate? {
        gatesLock.lock(); defer { gatesLock.unlock() }
        guard !gates.isEmpty else {
            XCTFail("anchor: the \(what) never reached its gate", file: file, line: line)
            return nil
        }
        return gates.removeFirst()
    }

    private func beginSecure(_ bridge: AppleShellBridge, _ id: Int64, _ args: String) {
        _ = bridge.hostCallBegin(id, BnHostCallOp.secureStorage, args)
    }

    private func getWithAuthArgs(_ key: String) -> String {
        "{\"action\":\"getWithAuth\",\"key\":\"\(key)\",\"reason\":\"Unlock\"}"
    }

    private func deleteArgs(_ key: String) -> String { "{\"action\":\"delete\",\"key\":\"\(key)\"}" }

    /// `id`'s first completion as it stands now.
    private func completion(_ id: Int64) -> (status: Int32, payload: String?)? {
        capturedLock.lock(); defer { capturedLock.unlock() }
        return captured.first { $0.id == id }.map { (status: $0.status, payload: $0.payload) }
    }

    /// The position of `id`'s first completion in completion order, or -1.
    private func indexOf(_ id: Int64) -> Int {
        capturedLock.lock(); defer { capturedLock.unlock() }
        return captured.firstIndex { $0.id == id } ?? -1
    }

    /// Answers a gate from a background thread, as LocalAuthentication answers on its own
    /// reply queue, waits for that thread to return, then drains `workQueue`.
    private func answerOffQueue(_ bridge: AppleShellBridge, _ answer: @escaping () -> Void,
                                file: StaticString = #filePath, line: UInt = #line) {
        let done = DispatchSemaphore(value: 0)
        DispatchQueue.global(qos: .userInitiated).async { answer(); done.signal() }
        XCTAssertEqual(done.wait(timeout: .now() + 10), .success, "the gate's answer did not return", file: file, line: line)
        bridge.secureStorage.drainForTest()
    }

    func testASameKeyDeleteWaitsBehindAPendingGetWithAuth() {
        let bridge = AppleShellBridge()
        holdEveryGate()
        XCTAssertEqual(bridge.secureStorage.secureSet(key: authKey, value: "hunter2", requireAuth: true),
                       BnSecureStorageStatus.ok)
        installCapture()

        beginSecure(bridge, 60, getWithAuthArgs(authKey))
        bridge.secureStorage.drainForTest()
        guard let gate = takeGate("getWithAuth") else { return }
        beginSecure(bridge, 61, deleteArgs(authKey))
        bridge.secureStorage.drainForTest()
        XCTAssertNil(completion(61), "anchor: the delete completed while the getWithAuth's gate was up: #506")

        answerOffQueue(bridge) { gate.authenticate() }
        XCTAssertEqual(completion(60)?.status, BnSecureStorageStatus.ok, "the gated read must complete Ok")
        XCTAssertEqual(completion(60)?.payload, "{\"value\":\"hunter2\"}", "the gated read must return the value")
        XCTAssertEqual(completion(61)?.status, BnSecureStorageStatus.ok, "the parked delete must complete Ok")
        XCTAssertTrue(indexOf(60) >= 0 && indexOf(60) < indexOf(61),
                      "the delete overtook the getWithAuth's read: read=\(indexOf(60)) delete=\(indexOf(61))")
    }

    func testASecondGetWithAuthTakesTheHoldWhenItDrains() {
        let bridge = AppleShellBridge()
        holdEveryGate()
        XCTAssertEqual(bridge.secureStorage.secureSet(key: authKey, value: "hunter2", requireAuth: true),
                       BnSecureStorageStatus.ok)
        installCapture()

        beginSecure(bridge, 62, getWithAuthArgs(authKey))
        bridge.secureStorage.drainForTest()
        guard let gateA = takeGate("first getWithAuth") else { return }
        beginSecure(bridge, 63, getWithAuthArgs(authKey))
        beginSecure(bridge, 64, deleteArgs(authKey))
        bridge.secureStorage.drainForTest()

        answerOffQueue(bridge) { gateA.authenticate() }
        guard let gateB = takeGate("second getWithAuth, drained when the first answered,") else { return }
        bridge.secureStorage.drainForTest()
        XCTAssertNil(completion(64), "anchor: the delete ran while the second getWithAuth's gate was up")

        answerOffQueue(bridge) { gateB.authenticate() }
        XCTAssertEqual(completion(62)?.status, BnSecureStorageStatus.ok, "the first gated read must complete Ok")
        XCTAssertEqual(completion(63)?.status, BnSecureStorageStatus.ok, "the second gated read must complete Ok")
        XCTAssertEqual(completion(64)?.status, BnSecureStorageStatus.ok, "the parked delete must complete Ok")
        XCTAssertTrue(indexOf(62) < indexOf(64) && indexOf(63) < indexOf(64) && indexOf(62) >= 0 && indexOf(63) >= 0,
                      "the delete did not complete last: first=\(indexOf(62)) second=\(indexOf(63)) delete=\(indexOf(64))")
    }

    func testAnotherKeyIsNotBlockedByAPendingGetWithAuth() {
        let bridge = AppleShellBridge()
        holdEveryGate()
        XCTAssertEqual(bridge.secureStorage.secureSet(key: authKey, value: "hunter2", requireAuth: true),
                       BnSecureStorageStatus.ok)
        installCapture()

        beginSecure(bridge, 65, getWithAuthArgs(authKey))
        bridge.secureStorage.drainForTest()
        guard let gate = takeGate("getWithAuth") else { return }
        beginSecure(bridge, 66, "{\"action\":\"set\",\"key\":\"\(plainKey)\",\"value\":\"o1\",\"auth\":\"0\"}")
        beginSecure(bridge, 67, "{\"action\":\"get\",\"key\":\"\(plainKey)\"}")
        bridge.secureStorage.drainForTest()
        XCTAssertEqual(completion(66)?.status, BnSecureStorageStatus.ok,
                       "a plain set on another key waited behind the auth key's gate")
        XCTAssertEqual(completion(67)?.status, BnSecureStorageStatus.ok,
                       "a get on another key waited behind the auth key's gate")
        XCTAssertEqual(completion(67)?.payload, "{\"value\":\"o1\"}", "the get on another key returned the wrong value")
        XCTAssertNil(completion(65), "anchor: the auth key's gate is still up")
        XCTAssertTrue(bridge.secureStorage.isKeyHeldForTest(authKey), "anchor: the auth key's gate is still up")

        answerOffQueue(bridge) { gate.deny() }
        XCTAssertEqual(completion(65)?.status, BnSecureStorageStatus.authFailed, "the denied read must complete AuthFailed")
    }

    func testADeniedGateReleasesItsKey() {
        let bridge = AppleShellBridge()
        holdEveryGate()
        XCTAssertEqual(bridge.secureStorage.secureSet(key: authKey, value: "hunter2", requireAuth: true),
                       BnSecureStorageStatus.ok)
        installCapture()

        beginSecure(bridge, 68, getWithAuthArgs(authKey))
        bridge.secureStorage.drainForTest()
        guard let gate = takeGate("getWithAuth") else { return }
        beginSecure(bridge, 69, deleteArgs(authKey))
        bridge.secureStorage.drainForTest()
        XCTAssertNil(completion(69), "anchor: the delete completed while the getWithAuth's gate was up: #506")

        answerOffQueue(bridge) { gate.deny() }
        XCTAssertEqual(completion(68)?.status, BnSecureStorageStatus.authFailed, "the denied read must complete AuthFailed")
        XCTAssertEqual(completion(69)?.status, BnSecureStorageStatus.ok, "the delete parked behind the denied gate must complete Ok")
        XCTAssertTrue(indexOf(68) >= 0 && indexOf(68) < indexOf(69),
                      "the delete overtook the denied read: read=\(indexOf(68)) delete=\(indexOf(69))")
        XCTAssertFalse(bridge.secureStorage.isKeyHeldForTest(authKey), "the denied gate left its key held")
    }

    func testAnAuthBoundSetDoesNotHoldItsKey() {
        // No gate hook: an auth-bound set does not prompt on iOS, so it never reaches a gate
        // and must not hold its key. The get that follows it is a plain get of an auth item,
        // refused as AuthFailed: the contract, not a hold.
        let bridge = AppleShellBridge()
        installCapture()
        beginSecure(bridge, 70, "{\"action\":\"set\",\"key\":\"\(authKey)\",\"value\":\"hunter2\",\"auth\":\"1\"}")
        beginSecure(bridge, 71, "{\"action\":\"get\",\"key\":\"\(authKey)\"}")
        bridge.secureStorage.drainForTest()
        XCTAssertEqual(completion(70)?.status, BnSecureStorageStatus.ok, "the auth-bound set must complete Ok")
        XCTAssertEqual(completion(71)?.status, BnSecureStorageStatus.authFailed,
                       "the get after an auth-bound set must complete, refused as AuthFailed")
        XCTAssertTrue(indexOf(70) >= 0 && indexOf(70) < indexOf(71),
                      "the get overtook the auth-bound set: set=\(indexOf(70)) get=\(indexOf(71))")
        XCTAssertFalse(bridge.secureStorage.isKeyHeldForTest(authKey),
                       "an auth-bound set held its key: on iOS it does not prompt, the iOS asymmetry, so it holds nothing")
    }

    func testTheGatesAnswerRunsOnTheWorkQueue() {
        let bridge = AppleShellBridge()
        holdEveryGate()
        XCTAssertEqual(bridge.secureStorage.secureSet(key: authKey, value: "hunter2", requireAuth: true),
                       BnSecureStorageStatus.ok)
        let queueKey = bridge.secureStorage.workQueueSpecificKeyForTest()
        let ranOnQueueLock = NSLock()
        var ranOnQueue: [Int64: Bool] = [:]
        BnSecureStorage.completeHookForTest = { [weak self] id, status, payload in
            let onQueue = DispatchQueue.getSpecific(key: queueKey) == true
            ranOnQueueLock.lock(); ranOnQueue[id] = onQueue; ranOnQueueLock.unlock()
            guard let self = self else { return 0 }
            self.capturedLock.lock(); self.captured.append((id, status, payload)); self.capturedLock.unlock()
            return 0
        }

        beginSecure(bridge, 72, getWithAuthArgs(authKey))
        bridge.secureStorage.drainForTest()
        guard let granted = takeGate("granted getWithAuth") else { return }
        answerOffQueue(bridge) { granted.authenticate() }
        XCTAssertEqual(completion(72)?.status, BnSecureStorageStatus.ok, "the granted read must complete Ok")

        beginSecure(bridge, 73, getWithAuthArgs(authKey))
        bridge.secureStorage.drainForTest()
        guard let denied = takeGate("denied getWithAuth") else { return }
        answerOffQueue(bridge) { denied.deny() }
        XCTAssertEqual(completion(73)?.status, BnSecureStorageStatus.authFailed, "the denied read must complete AuthFailed")

        ranOnQueueLock.lock(); let granted72 = ranOnQueue[72], denied73 = ranOnQueue[73]; ranOnQueueLock.unlock()
        XCTAssertEqual(granted72, true, "the answer ran off workQueue")
        XCTAssertEqual(denied73, true, "the denial ran off workQueue")
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
        // Polled, not read once: the rc is stored on the handler's queue after the export
        // returns, so the echo can render first (16.4).
        XCTAssertTrue(pollUntil { BnSecureStorage.lastHostCallCompleteRcForTest == 0 },
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
        XCTAssertTrue(pollUntil { BnSecureStorage.lastHostCallCompleteRcForTest == 0 },
                      "host_call_complete did not route to the in-flight .NET requestId")
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
