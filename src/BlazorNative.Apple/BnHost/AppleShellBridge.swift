// ─────────────────────────────────────────────────────────────────────────────
// AppleShellBridge — Phase 5.3 (M5 DoD #3) + Phase 5.4 (DoD #6): the host half of
// the shell bridge — the `@convention(c)` callbacks .NET calls INTO through
// `blazornative_register_bridge`. The Swift twin of Android's AndroidShellBridge +
// ShellBridge.kt; register_bridge is size-negotiated (structSize + min-copy) and
// registered BEFORE mount. Phase 5.4 appended clipboard read/write + share (offsets
// 48/56/64) — REAL since Gate 3: clipboard → UIPasteboard.general, share →
// UIActivityViewController.
//
// No-capture, singleton-routed (the 5.2 frame-callback pattern): the ten global
// `@convention(c)` trampolines forward to `AppleShellBridge.shared`. Nothing can
// throw across the C ABI BY CONSTRUCTION — `@convention(c)` closures are
// non-throwing (compiler-enforced) and the bridge methods they call don't throw —
// so -1 is returned ONLY on the nil-singleton guard, never from a caught exception.
// Process-lifetime retention — app-scoped state only (route slot + storage dict).
//
// Threading: bridge ops are DATA, not UI — the sync handlers (navigate, storage,
// clipboard) run on the .NET dispatch lane. `navigate` (lane) and `currentRoute`
// (boot thread at mount AND the lane) can race on the route slot → it is
// lock-guarded (the @Volatile twin). UIPasteboard.general get/set is safe off the
// main thread (cross-process, not a UIView), so clipboard stays on the lane
// (Android reads/writes ClipboardManager on its lane too). Share is the ONE UI
// affordance: it MUST build/present its UIActivityViewController on the MAIN thread,
// so `share` hops to DispatchQueue.main and presents from the key window's root VC —
// unless the `shareHook` seam is set (test only), which captures the content
// synchronously and skips the sheet (the Android `shareLaunchHook` twin).
//
// Return-code protocol (BridgeProtocolNative.cs): buffer-writing calls
// (currentRoute, storageRead, clipboardRead) return the byte count written
// INCLUDING the NUL on success, or `-(utf8Bytes + 1)` when the value does not fit
// the offered cap (the -needed protocol — the .NET side retries once at that size);
// -1 = host error; -2 = key absent (storageRead).
// ─────────────────────────────────────────────────────────────────────────────

import UIKit

final class AppleShellBridge {

    /// Routes the nine `@convention(c)` trampolines to the live instance (the
    /// frame-callback singleton pattern). Set before register_bridge + mount.
    static var shared: AppleShellBridge?

    /// Share test seam (the AndroidShellBridge.shareLaunchHook twin): when set, a
    /// `share(_:)` hands the activityItems here and does NOT present the sheet, so a
    /// hosted XCTest can assert the share content without popping the system UI.
    /// Null in production. Process-static (the test cannot reach the app's bridge
    /// instance directly); reset it in the test's teardown so it never leaks.
    static var shareHook: (([Any]) -> Void)?

    private var route: String
    private let routeLock = NSLock()
    private var storage: [String: String] = [:]

    /// Phase 9.0: the geolocation half of the generic permission-gated host-call op.
    /// Owned here (app-lifetime, like the rest of the bridge) so it holds the
    /// CLLocationManager + delegate + in-flight requestId across the async permission
    /// prompt — the delegate-retention lesson (see BnGeolocation). One handler, one
    /// in-flight request; a later capability adds an op case, not a second export.
    let geolocation = BnGeolocation()

    /// Phase 9.1: the notifications half of the SAME generic permission-gated host-call
    /// op — the FIRST reuse of the 9.0 ABI, riding the same hostCallBegin slot (op=1, no
    /// new export). Owned here (app-lifetime) so it holds the UNUserNotificationCenter
    /// DELEGATE across a tap delivery — the weak-delegate retention lesson (see
    /// BnNotifications). A second op case, not a second export.
    let notifications = BnNotifications()

    /// Phase 9.2: the biometrics half of the SAME generic permission-gated host-call op
    /// — the SECOND reuse of the 9.0 ABI, riding the same hostCallBegin slot (op=2, no
    /// new export). Owned here (app-lifetime) so it holds the LAContext across the async
    /// evaluatePolicy — the CLLocationManager/LAContext retention lesson (see BnBiometrics).
    let biometrics = BnBiometrics()

    /// Phase 9.2: the secure-storage half of the SAME op (op=3, no new export) — the
    /// Keychain-backed encrypted store whose auth-bound items are OS-key gated
    /// (.biometryCurrentSet). Owned here (app-lifetime) so it holds the LAContext across a
    /// getWithAuth evaluation (see BnSecureStorage). A third op case, not a third export.
    let secureStorage = BnSecureStorage()

    init(initialRoute: String = "/") {
        self.route = initialRoute
    }

    // ── navigate + current-route (REAL) ──────────────────────────────────────

    /// Stores the new route (Settings/Back navigation notify). Returns 0.
    func navigate(_ newRoute: String) -> Int32 {
        routeLock.lock()
        route = newRoute
        routeLock.unlock()
        NSLog("[AppleShellBridge] navigate → \(newRoute)")
        return 0
    }

    /// Returns the current route via the -needed buffer protocol.
    func currentRoute(_ buf: UnsafeMutablePointer<CChar>, _ cap: Int32) -> Int32 {
        routeLock.lock()
        let r = route
        routeLock.unlock()
        return AppleShellBridge.writeUtf8(r, buf, cap)
    }

    /// Test seam: the current route without the buffer crossing.
    var currentRouteValue: String {
        routeLock.lock(); defer { routeLock.unlock() }
        return route
    }

    // ── storage ×3 (honest in-memory stubs — hermetic, not UserDefaults) ─────

    func storageRead(_ key: String, _ buf: UnsafeMutablePointer<CChar>, _ cap: Int32) -> Int32 {
        guard let value = storage[key] else { return -2 } // KEY_ABSENT
        return AppleShellBridge.writeUtf8(value, buf, cap)
    }

    func storageWrite(_ key: String, _ value: String) -> Int32 {
        storage[key] = value
        return 0
    }

    func storageDelete(_ key: String) -> Int32 {
        storage.removeValue(forKey: key)
        return 0
    }

    // ── fetch (honest stub — fails synchronously) ────────────────────────────

    /// BnDemo does no fetch. The honest stub FAILS the request synchronously
    /// (-1 = host error) rather than accepting it and never completing — so a
    /// stray fetch surfaces immediately instead of hanging. Wire a real
    /// URLSession + `blazornative_fetch_complete` when a component needs it.
    func fetchBegin(_ requestId: Int64) -> Int32 {
        NSLog("[AppleShellBridge] fetchBegin id=\(requestId) — unsupported (5.3 stub), returning -1")
        return -1
    }

    // ── clipboard + share (Phase 5.4 Gate 3 — real UIPasteboard / share sheet) ─
    //
    // Clipboard → the system UIPasteboard.general (the ClipboardManager twin). read/
    // write run on the .NET dispatch lane (the sync-handler contract); UIPasteboard
    // string get/set is a cross-process op safe off the main thread. The buffer
    // -needed protocol is applied by writeUtf8 (the storageRead twin) — the handler
    // just returns the current string (empty when the pasteboard has no string).
    //
    // Share → a UIActivityViewController over [text], presented on the MAIN thread
    // from the key window's root VC (the bridge is off-main, so `share` hops). The
    // [shareHook] seam, when set (test only), captures the activityItems and SKIPS
    // present(...), so the XCTest asserts the share content without popping the
    // system sheet (the Android shareLaunchHook twin). "No presenter available" is
    // handled gracefully (log + return) — never a crash.

    func clipboardRead(_ buf: UnsafeMutablePointer<CChar>, _ cap: Int32) -> Int32 {
        let text = UIPasteboard.general.string ?? ""
        return AppleShellBridge.writeUtf8(text, buf, cap)
    }

    func clipboardWrite(_ text: String) -> Int32 {
        UIPasteboard.general.string = text
        return 0
    }

    func share(_ text: String) -> Int32 {
        let items: [Any] = [text]
        // Test seam (checked synchronously on the calling/lane thread): capture the
        // content and do NOT present. Mirrors AndroidShellBridge.shareLaunchHook.
        if let hook = AppleShellBridge.shareHook {
            hook(items)
            return 0
        }
        // Production: build + present the share sheet on the MAIN thread.
        DispatchQueue.main.async {
            guard let presenter = AppleShellBridge.topPresenter() else {
                NSLog("[AppleShellBridge] share: no presenter available — skipped")
                return
            }
            let avc = UIActivityViewController(activityItems: items, applicationActivities: nil)
            // iPad: a nil popover source would crash — anchor to the presenter's view.
            avc.popoverPresentationController?.sourceView = presenter.view
            presenter.present(avc, animated: true)
        }
        return 0
    }

    /// The top-most presenting view controller (key window's root, walking any
    /// presented chain). nil when no foreground window/root exists — share then
    /// logs and returns (graceful). Avoids the iOS-15-deprecated
    /// `UIApplication.shared.windows` by going through the active window scene.
    private static func topPresenter() -> UIViewController? {
        let keyWindow = UIApplication.shared.connectedScenes
            .compactMap { $0 as? UIWindowScene }
            .flatMap { $0.windows }
            .first { $0.isKeyWindow }
        var top = keyWindow?.rootViewController
        while let presented = top?.presentedViewController { top = presented }
        return top
    }

    // ── Geolocation (Phase 9.0 — the generic permission-gated async op) ───────
    //
    // The GENERIC begin (hostCallBegin, offset 72) wired for op=Geolocation, the
    // AndroidShellBridge.hostCallBegin twin. Returns FAST (the begin contract); the
    // tri-state result is pushed LATER via blazornative_host_call_complete. An unknown
    // op is DATA, not a crash — it completes with Error so the awaiting .NET ValueTask
    // resolves rather than leaking a pending entry. The real work + the CLLocationManager
    // retention live in BnGeolocation.

    func hostCallBegin(_ requestId: Int64, _ op: Int32, _ argsJson: String) -> Int32 {
        switch op {
        case BnHostCallOp.geolocation:
            geolocation.begin(requestId: requestId, argsJson: argsJson)
        case BnHostCallOp.notifications:
            notifications.begin(requestId: requestId, argsJson: argsJson)
        case BnHostCallOp.biometrics:
            biometrics.begin(requestId: requestId, argsJson: argsJson)
        case BnHostCallOp.secureStorage:
            secureStorage.begin(requestId: requestId, argsJson: argsJson)
        default:
            NSLog("[AppleShellBridge] hostCallBegin: unknown op \(op) (request \(requestId)) — completing Error")
            geolocation.completeUnknownOp(requestId: requestId)
        }
        return 0
    }

    // ── the -needed buffer-write helper (twin of ShellBridge.writeUtf8) ──────

    /// UTF-8-encode `value`; when bytes + 1 (NUL) fits in `cap`, write bytes + NUL
    /// and return bytes + 1; otherwise write NOTHING and return `-(bytes + 1)`.
    /// Never returns -needed for a value that fits (invariant b).
    static func writeUtf8(_ value: String, _ buf: UnsafeMutablePointer<CChar>, _ cap: Int32) -> Int32 {
        let bytes = Array(value.utf8)
        let needed = bytes.count + 1
        if needed > Int(cap) { return Int32(-needed) }
        for i in 0..<bytes.count {
            buf[i] = CChar(bitPattern: bytes[i])
        }
        buf[bytes.count] = 0
        return Int32(needed)
    }
}

// ── The ten global @convention(c) trampolines (no capture; singleton-routed) ──
// Held as top-level `let`s for the process lifetime — the fn pointers must outlive
// register_bridge (the JNA strong-ref rule's Swift form). A nil singleton → -1.
// (Phase 9.0 appended hostCallBegin at offset 72.)

let bnBridgeNavigate: bn_navigate_cb = { routePtr in
    guard let routePtr = routePtr, let bridge = AppleShellBridge.shared else { return -1 }
    return bridge.navigate(String(cString: routePtr))
}

let bnBridgeCurrentRoute: bn_current_route_cb = { buf, cap in
    guard let buf = buf, let bridge = AppleShellBridge.shared else { return -1 }
    return bridge.currentRoute(buf, cap)
}

let bnBridgeStorageRead: bn_storage_read_cb = { keyPtr, buf, cap in
    guard let keyPtr = keyPtr, let buf = buf, let bridge = AppleShellBridge.shared else { return -1 }
    return bridge.storageRead(String(cString: keyPtr), buf, cap)
}

let bnBridgeStorageWrite: bn_storage_write_cb = { keyPtr, valuePtr in
    guard let keyPtr = keyPtr, let valuePtr = valuePtr, let bridge = AppleShellBridge.shared else { return -1 }
    return bridge.storageWrite(String(cString: keyPtr), String(cString: valuePtr))
}

let bnBridgeStorageDelete: bn_storage_delete_cb = { keyPtr in
    guard let keyPtr = keyPtr, let bridge = AppleShellBridge.shared else { return -1 }
    return bridge.storageDelete(String(cString: keyPtr))
}

let bnBridgeFetchBegin: bn_fetch_begin_cb = { requestId, _ in
    guard let bridge = AppleShellBridge.shared else { return -1 }
    return bridge.fetchBegin(requestId)
}

// Phase 5.4 clipboard/share trampolines — route to the REAL UIPasteboard /
// UIActivityViewController methods (Gate 3). A nil singleton → -1.
let bnBridgeClipboardRead: bn_clipboard_read_cb = { buf, cap in
    guard let buf = buf, let bridge = AppleShellBridge.shared else { return -1 }
    return bridge.clipboardRead(buf, cap)
}

let bnBridgeClipboardWrite: bn_clipboard_write_cb = { textPtr in
    guard let textPtr = textPtr, let bridge = AppleShellBridge.shared else { return -1 }
    return bridge.clipboardWrite(String(cString: textPtr))
}

let bnBridgeShare: bn_share_cb = { textPtr in
    guard let textPtr = textPtr, let bridge = AppleShellBridge.shared else { return -1 }
    return bridge.share(String(cString: textPtr))
}

// Phase 9.0 host-call-begin trampoline (offset 72) — the GENERIC permission-gated
// async-begin. A NULL args pointer is tolerated as an empty arg string (the op default
// applies); a nil singleton → -1 (host error, per the ABI's <0 begin-failure rule).
let bnBridgeHostCallBegin: bn_host_call_begin_cb = { requestId, op, argsPtr in
    guard let bridge = AppleShellBridge.shared else { return -1 }
    let args = argsPtr.map { String(cString: $0) } ?? "{}"
    return bridge.hostCallBegin(requestId, op, args)
}
