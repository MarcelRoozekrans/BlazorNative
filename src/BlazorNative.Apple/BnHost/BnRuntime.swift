// ─────────────────────────────────────────────────────────────────────────────
// BnRuntime — Phase 5.2 (M5 DoD #2): the boot lifecycle for the NativeAOT
// BlazorNative.Runtime static archive. The Swift twin of the Kotlin
// io.blazornative.jni.BlazorNativeRuntime — init → register_frame_callback →
// mount. No dispatch lane, no bridge (Phase 5.3): boot + read-only render only.
//
// The @convention(c) frame callback CANNOT capture context, so it routes through
// a singleton (`BnRuntime.shared`) — the Swift form of the JNA strong-ref hazard.
// The runtime instance + the trampoline must outlive registration; the host app
// holds BnRuntime for its lifetime (AppDelegate/HostViewController), exactly as
// MainActivity holds its runtime field so the callback trampoline is never GC'd.
//
// Exception posture: the decode is wrapped in do/catch → the `onError` sink; a
// throw NEVER crosses back into the C callback (JNA would swallow it to stderr;
// here we drop the frame LOUDLY and keep the runtime alive).
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
    case mountFailed(rc: Int32, component: String)

    var description: String {
        switch self {
        case .initFailed(let s, let d): return "blazornative_init failed (status=\(s)): \(d)"
        case .registerFailed(let rc): return "blazornative_register_frame_callback failed (rc=\(rc))"
        case .mountFailed(let rc, let c):
            switch rc {
            case 1: return "unknown component '\(c)'"
            case 3: return "mount('\(c)') — name pointer null"
            default: return "mount('\(c)') failed with status \(rc) — detail went to native stderr"
            }
        }
    }
}

final class BnRuntime {

    /// Routes the @convention(c) callback to the live instance (last boot wins,
    /// mirroring register_frame_callback's last-wins contract).
    static var shared: BnRuntime?

    private let mapper: BnWidgetMapper

    /// Frame-decode error sink (drop the frame loudly). Defaults to NSLog; the
    /// host/test may override to capture.
    var onError: ((String, Error) -> Void) = { msg, err in
        NSLog("[BnRuntime] \(msg): \(err)")
    }

    init(mapper: BnWidgetMapper) {
        self.mapper = mapper
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
