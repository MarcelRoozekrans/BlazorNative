// ─────────────────────────────────────────────────────────────────────────────
// BnRuntime — Phase 5.2 boot lifecycle + Phase 5.3 (M5 DoD #3) interactivity: the
// Swift twin of the Kotlin io.blazornative.jni.BlazorNativeRuntime. Boot is
// init → register_frame_callback → register_bridge → mount; interactivity adds the
// dispatch lane (taps → `blazornative_dispatch_event`).
//
// The @convention(c) frame callback CANNOT capture context, so it routes through
// a singleton (`BnRuntime.shared`) — the Swift form of the JNA strong-ref hazard.
// The runtime instance + the trampoline must outlive registration; the host app
// holds BnRuntime for its lifetime (AppDelegate/HostViewController), exactly as
// MainActivity holds its runtime field so the callback trampoline is never GC'd.
//
// Dispatch lane (Phase 5.3, the `BlazorNative-Dispatch` twin): a SERIAL
// DispatchQueue. Every UI event enters .NET through it (never call the ABI from
// the main thread). `dispatch_event` is SYNCHRONOUS — the handler, the re-render,
// and the re-render's frame callback all complete on the lane thread before the
// export returns; the frame consumer (mapper) marshals the tree mutation to
// DispatchQueue.main (the 5.2 CommitFrame hop), so UIViews are only touched on main.
//
// Exception posture: the decode is wrapped in do/catch → the `onError` sink; a
// throw NEVER crosses back into the C callback. A non-zero dispatch rc routes to
// the same sink (the tap is dropped loudly).
// ─────────────────────────────────────────────────────────────────────────────

import Foundation

/// Global @convention(c) trampoline. It cannot capture, so it forwards to the
/// singleton. Held as a top-level `let` for the process lifetime — the twin of
/// BlazorNativeRuntime's strongly-held FrameCallback object.
private let bnFrameTrampoline: bn_frame_callback = { framePtr in
    guard let framePtr = framePtr else { return }
    BnRuntime.shared?.handleFrame(framePtr)
}

/// Boot failures — the twin of the Kotlin `IllegalStateException` throws.
enum BnRuntimeError: Error, CustomStringConvertible {
    case initFailed(status: Int32, detail: String)
    case registerFailed(rc: Int32)
    case bridgeRegisterFailed(rc: Int32)
    case mountFailed(rc: Int32, component: String)

    var description: String {
        switch self {
        case .initFailed(let s, let d): return "blazornative_init failed (status=\(s)): \(d)"
        case .registerFailed(let rc): return "blazornative_register_frame_callback failed (rc=\(rc))"
        case .bridgeRegisterFailed(let rc): return "blazornative_register_bridge failed (rc=\(rc))"
        case .mountFailed(let rc, let c):
            switch rc {
            case 1: return "unknown component '\(c)'"
            case 3: return "mount('\(c)') — name pointer null"
            default: return "mount('\(c)') failed with status \(rc) — detail went to native stderr"
            }
        }
    }
}

/// A non-zero dispatch rc as an Error for the onError sink (rc 1 no session /
/// 2 fault / 3 malformed). rc 0 (incl. a stale handler) is never surfaced.
struct BnDispatchError: Error, CustomStringConvertible {
    let rc: Int32
    let handlerId: Int32
    let eventName: String
    var description: String {
        switch rc {
        case 1: return "dispatch_event(handlerId=\(handlerId), '\(eventName)') → rc 1: no session/nothing mounted"
        case 2: return "dispatch_event(handlerId=\(handlerId), '\(eventName)') → rc 2: dispatch faulted (handler/re-render/frame delivery threw — detail on native stderr)"
        case 3: return "dispatch_event(handlerId=\(handlerId), '\(eventName)') → rc 3: malformed args or handlerId out of range"
        default: return "dispatch_event(handlerId=\(handlerId), '\(eventName)') → undocumented rc \(rc)"
        }
    }
}

final class BnRuntime {

    /// Routes the @convention(c) callback to the live instance (last boot wins,
    /// mirroring register_frame_callback's last-wins contract).
    static var shared: BnRuntime?

    private let mapper: BnWidgetMapper

    /// The host half of the shell bridge (navigate/current-route/storage/fetch).
    /// Owned here, registered before mount; app-lifetime like the runtime.
    let bridge: AppleShellBridge

    /// THE dispatch lane (the `BlazorNative-Dispatch` twin) — a SERIAL queue; all
    /// post-boot .NET entry serializes through it. Never call the ABI off it.
    private let dispatchLane = DispatchQueue(label: "BlazorNative-Dispatch")

    /// Frame-decode / dispatch error sink (drop loudly). Defaults to NSLog; the
    /// host/test may override to capture.
    var onError: ((String, Error) -> Void) = { msg, err in
        NSLog("[BnRuntime] \(msg): \(err)")
    }

    init(mapper: BnWidgetMapper, bridge: AppleShellBridge = AppleShellBridge()) {
        self.mapper = mapper
        self.bridge = bridge
        // Wire the mapper's UI-event seam to the dispatch lane (the twin of
        // MainActivity's `WidgetMapper(onUiEvent = { runtime.dispatchEvent(...) })`).
        // `self` weak → no retain cycle (runtime strongly holds the mapper).
        mapper.onUiEvent = { [weak self] handlerId, eventName, payload in
            self?.dispatchEvent(handlerId: handlerId, eventName: eventName, payload: payload)
        }
    }

    // ── Dispatch (Phase 5.3) ─────────────────────────────────────────────────

    /// Dispatches a UI event to the .NET handler on the serial dispatch lane
    /// (async-submit; call from UI control targets, never the ABI directly). A
    /// non-zero rc routes to `onError` (the tap is dropped). The re-render frame
    /// arrives synchronously on the lane thread inside the export and the mapper
    /// hops the tree mutation to main.
    func dispatchEvent(handlerId: Int32, eventName: String, payload: String?) {
        dispatchLane.async { [weak self] in
            guard let self = self else { return }
            let rc = self.dispatchCore(handlerId: handlerId, eventName: eventName, payload: payload)
            if rc != 0 {
                self.onError("dispatch_event rc \(rc)",
                             BnDispatchError(rc: rc, handlerId: handlerId, eventName: eventName))
            }
        }
    }

    /// Test seam: same marshalling, run INLINE on the calling thread, returns the
    /// raw rc (mirrors Kotlin `dispatchEventBlocking`). Production uses the async
    /// `dispatchEvent` — the lane is the threading contract.
    @discardableResult
    func dispatchEventBlocking(handlerId: Int32, eventName: String, payload: String? = nil) -> Int32 {
        dispatchLane.sync { dispatchCore(handlerId: handlerId, eventName: eventName, payload: payload) }
    }

    /// Builds the FlatJson args, NUL-terminates, and crosses the ABI.
    private func dispatchCore(handlerId: Int32, eventName: String, payload: String?) -> Int32 {
        let argsJson = BnFlatJson.args(name: eventName, payload: payload)
        return argsJson.withCString { blazornative_dispatch_event(UInt64(bitPattern: Int64(handlerId)), $0) }
    }

    /// Boots: init → register frame callback → mount. The first frame fires
    /// SYNCHRONOUSLY inside `blazornative_mount` (sync-mount contract), on the
    /// calling thread; the mapper buffers it and hops the batch to the main queue.
    /// Throws BnRuntimeError on any non-zero status.
    func start(component: String = "BnDemo", os: String = "ios", apiLevel: Int32 = 0) throws {
        // Route the C callback here BEFORE registering it, so a synchronous mount
        // frame can never find a nil singleton.
        BnRuntime.shared = self

        // init — nest withCString so the borrowed pointers stay alive across the
        // call (init copies the strings; it does not retain the pointers).
        let result: bn_init_result = os.withCString { osPtr in
            "ios-shell".withCString { notePtr in
                var opts = bn_init_options(
                    platformInfoOs: osPtr,
                    platformInfoApiLevel: apiLevel,
                    platformInfoNote: notePtr)
                return blazornative_init(&opts)
            }
        }
        guard result.status == 0 else {
            let detail = result.error.map { String(cString: $0) } ?? "<no detail>"
            throw BnRuntimeError.initFailed(status: result.status, detail: detail)
        }
        let version = result.version.map { String(cString: $0) } ?? "<null>"
        NSLog("[BnRuntime] native init ok — \(version)")

        // register the frame callback
        let regRc = blazornative_register_frame_callback(bnFrameTrampoline)
        guard regRc == 0 else { throw BnRuntimeError.registerFailed(rc: regRc) }
        NSLog("[BnRuntime] frame callback registered")

        // register the shell bridge BEFORE mount (components resolving the bridge
        // need a live host). Route the singleton first so a mount-time currentRoute
        // can never find a nil bridge; the trampolines are process-lifetime globals.
        AppleShellBridge.shared = bridge
        var callbacks = bn_bridge_callbacks(
            navigate: bnBridgeNavigate,
            currentRoute: bnBridgeCurrentRoute,
            storageRead: bnBridgeStorageRead,
            storageWrite: bnBridgeStorageWrite,
            storageDelete: bnBridgeStorageDelete,
            fetchBegin: bnBridgeFetchBegin,
            clipboardRead: bnBridgeClipboardRead,
            clipboardWrite: bnBridgeClipboardWrite,
            share: bnBridgeShare)
        // Phase 5.4 size negotiation: pass our full struct size (72); the runtime
        // min-copies + zero-fills.
        let bridgeRc = blazornative_register_bridge(Int32(MemoryLayout<bn_bridge_callbacks>.size), &callbacks)
        guard bridgeRc == 0 else { throw BnRuntimeError.bridgeRegisterFailed(rc: bridgeRc) }
        NSLog("[BnRuntime] shell bridge registered")

        // mount — the sync first frame fires inside this call
        let mountRc = component.withCString { blazornative_mount($0) }
        guard mountRc == 0 else { throw BnRuntimeError.mountFailed(rc: mountRc, component: component) }
        NSLog("[BnRuntime] mounted \(component)")
    }

    /// Invoked from the @convention(c) trampoline on the callback thread. Decodes
    /// + copies strings out of the arena, then hands the detached frame to the
    /// mapper (which buffers and flushes to main on CommitFrame). A decode throw
    /// is routed to `onError` — never rethrown into the C callback.
    func handleFrame(_ framePtr: UnsafeRawPointer) {
        do {
            let frame = try BnFrameAdapter.read(framePtr)
            mapper.apply(frame)
        } catch {
            onError("frame dropped (adapter/consumer threw)", error)
        }
    }
}
