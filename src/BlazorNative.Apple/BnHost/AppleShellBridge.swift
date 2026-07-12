// ─────────────────────────────────────────────────────────────────────────────
// AppleShellBridge — Phase 5.3 (M5 DoD #3): the host half of the shell bridge —
// the six `@convention(c)` callbacks .NET calls INTO through
// `blazornative_register_bridge`. The Swift twin of Android's AndroidShellBridge +
// ShellBridge.kt, register_bridge is all-or-nothing (all six supplied) and is
// registered BEFORE mount.
//
// No-capture, singleton-routed (the 5.2 frame-callback pattern): the six global
// `@convention(c)` trampolines forward to `AppleShellBridge.shared`; a nil
// singleton or a would-be throw returns -1 (nothing unwinds across the C ABI).
// Process-lifetime retention — app-scoped state only (route slot + storage dict),
// no view controllers.
//
// Threading: bridge ops are DATA, not UI — no main-thread hop. `navigate` (lane
// thread, during dispatch) and `currentRoute` (boot thread at mount AND the lane)
// can race on the route slot → it is lock-guarded (the @Volatile twin). storage is
// touched only on the dispatch lane (serial), so the in-memory dict is safe as-is.
//
// Return-code protocol (BridgeProtocolNative.cs): buffer-writing calls
// (currentRoute, storageRead) return the byte count written INCLUDING the NUL on
// success, or `-(utf8Bytes + 1)` when the value does not fit the offered cap
// (the -needed protocol — the .NET side retries once at that size); -1 = host
// error; -2 = key absent (storageRead).
// ─────────────────────────────────────────────────────────────────────────────

import Foundation

final class AppleShellBridge {

    /// Routes the six `@convention(c)` trampolines to the live instance (the
    /// frame-callback singleton pattern). Set before register_bridge + mount.
    static var shared: AppleShellBridge?

    private var route: String
    private let routeLock = NSLock()
    private var storage: [String: String] = [:]

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

// ── The six global @convention(c) trampolines (no capture; singleton-routed) ──
// Held as top-level `let`s for the process lifetime — the fn pointers must outlive
// register_bridge (the JNA strong-ref rule's Swift form). A nil singleton → -1.

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
