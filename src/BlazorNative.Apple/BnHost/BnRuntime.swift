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

/// Phase 10.0 (#121): the ABI-mirrored PlatformKind ordinals. These integers are
/// the contract the shared runtime decodes in `blazornative_init` (Exports.cs
/// ToPlatformKind), the byte-identical twin of BlazorNative.Core.PlatformKind's
/// ordinals (DevHost=0, Android=1, iOS=2, Windows=3, Mac=4). The iOS shell passes
/// `.iOS` so an iOS app no longer reports Android through the platform-info surface.
enum BnPlatformKind {
    static let devHost: Int32 = 0
    static let android: Int32 = 1
    static let iOS: Int32 = 2
    static let windows: Int32 = 3
    static let mac: Int32 = 4
}

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
            default: return "mount('\(c)') failed with status \(rc) — the detail is in the unified log (subsystem io.blazornative, category native)"
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
        case 2: return "dispatch_event(handlerId=\(handlerId), '\(eventName)') → rc 2: dispatch faulted (handler/re-render/frame delivery threw — the detail is in the unified log, subsystem io.blazornative, category native)"
        case 3: return "dispatch_event(handlerId=\(handlerId), '\(eventName)') → rc 3: malformed args or handlerId out of range"
        default: return "dispatch_event(handlerId=\(handlerId), '\(eventName)') → undocumented rc \(rc)"
        }
    }
}

final class BnRuntime {

    /// Routes the @convention(c) callback to the live instance (last boot wins,
    /// mirroring register_frame_callback's last-wins contract).
    static var shared: BnRuntime?

    /// The unified-log category for this file — the tag that used to be the
    /// bracketed `[BnRuntime]` prefix inside every `NSLog` string. The brackets are
    /// gone because the seam owns the presentation now; the CATEGORY is what
    /// `log stream --predicate 'category == "BnRuntime"'` filters on.
    static let logCategory = "BnRuntime"

    private let mapper: BnWidgetMapper

    /// The host half of the shell bridge (navigate/current-route/storage/fetch).
    /// Owned here, registered before mount; app-lifetime like the runtime.
    let bridge: AppleShellBridge

    /// THE dispatch lane (the `BlazorNative-Dispatch` twin) — a SERIAL queue; all
    /// post-boot .NET entry serializes through it. Never call the ABI off it.
    private let dispatchLane = DispatchQueue(label: "BlazorNative-Dispatch")

    /// Frame-decode / dispatch error sink (drop loudly). Defaults to [BnLog]; the
    /// host/test may override to capture.
    ///
    /// Phase 11.4: the error's description is REDACTED by default (BnLog.swift's
    /// privacy rule) — an `Error`'s text is exactly the "internal exception detail"
    /// #155 asks not to leak at Release verbosity, and `NSLog` had no way to say so.
    var onError: ((String, Error) -> Void) = { msg, err in
        BnLog.error(BnRuntime.logCategory, "\(msg): \(err)")
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
        // Phase 7.2: the scroll wire needs the COMPLETION signal — the conflation
        // submits at most one scroll dispatch per lane-availability, and only the
        // onComplete overload can say when the lane freed. Same lane, same FIFO:
        // scroll can never overtake a queued tap (the wire contract's ordering
        // row). A dead runtime still completes — a lost completion would WEDGE
        // the mapper's conflation slot (see the overload's doc).
        mapper.onScrollEvent = { [weak self] handlerId, offsetPayload, onComplete in
            guard let self = self else { onComplete(); return }
            self.dispatchEvent(handlerId: handlerId, eventName: "scroll",
                               payload: offsetPayload, onComplete: onComplete)
        }
    }

    // ── Dispatch (Phase 5.3) ─────────────────────────────────────────────────

    /// Dispatches a UI event to the .NET handler on the serial dispatch lane
    /// (async-submit; call from UI control targets, never the ABI directly). A
    /// non-zero rc routes to `onError` (the tap is dropped). The re-render frame
    /// arrives synchronously on the lane thread inside the export and the mapper
    /// hops the tree mutation to main.
    func dispatchEvent(handlerId: Int32, eventName: String, payload: String?) {
        dispatchEvent(handlerId: handlerId, eventName: eventName, payload: payload, onComplete: {})
    }

    /// Phase 7.2 (the onScroll wire) — [dispatchEvent] WITH A COMPLETION SIGNAL,
    /// the Swift twin of Kotlin's `BlazorNativeRuntime.dispatchEvent(h, n, p,
    /// onComplete)`: `onComplete` runs after the dispatch has LEFT the lane (the
    /// ABI call returned — successfully or with a non-zero rc), which is the
    /// moment the lane is available again.
    ///
    /// It exists for the shell-side scroll CONFLATION (the wire contract,
    /// docs/plans/2026-07-15-phase-7.2-design.md): the mapper keeps ONE pending
    /// offset per scroll node and submits at most one scroll dispatch per
    /// lane-availability — a new dispatch may not be submitted until the previous
    /// one has completed. Fire-and-forget [dispatchEvent] cannot say when that
    /// is; this overload can. **Swift-side sugar over the same
    /// `blazornative_dispatch_event` — NO ABI change**, and no new threading
    /// surface: the SAME single serial [dispatchLane], the same FIFO — which is
    /// also what keeps the ordering rule free ("a conflated scroll dispatch must
    /// not overtake an already-queued user-input event"): scroll dispatches enter
    /// the same queue tail as every tap and change, and a serial queue never
    /// reorders. `BnScrollWireTests` pins that FIFO rather than assuming it.
    ///
    /// `onComplete` is invoked on the LANE thread — callers marshal to their own
    /// thread before touching their state (the mapper hops to the main queue). It
    /// ALWAYS runs, including when the runtime was deallocated before the work
    /// item ran: a lost completion would WEDGE the caller's conflation slot — the
    /// pending offset would wait forever for a lane that already freed, and the
    /// list would stop following the finger.
    func dispatchEvent(handlerId: Int32, eventName: String, payload: String?,
                       onComplete: @escaping () -> Void) {
        dispatchLane.async { [weak self] in
            defer { onComplete() }
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

        // Phase 11.4 Gate C (#155): resolve the verbosity BEFORE the first line is
        // written, and apply it to BOTH sides of the boundary — the Swift seam here
        // and, through `bn_init_options.logLevel` at offset 28 below, the managed
        // one. One threshold, declared once, honoured by both shells.
        let logLevel = BnRuntime.resolveLogLevel()
        BnLog.setLevelFromOrdinal(logLevel)

        // init — nest withCString so the borrowed pointers stay alive across the
        // call (init copies the strings; it does not retain the pointers).
        // Phase 10.0 (#121): pass platformInfoKind = iOS (ordinal 2, mirroring
        // BlazorNative.Core.PlatformKind) so the shared runtime reports iOS through
        // IMobileBridge.PlatformInfo / GetPlatformInfoAsync instead of the old
        // hardcoded Android. os stays "ios" (the display-OS string, already correct).
        let result: bn_init_result = os.withCString { osPtr in
            "ios-shell".withCString { notePtr in
                var opts = bn_init_options(
                    platformInfoOs: osPtr,
                    platformInfoApiLevel: apiLevel,
                    platformInfoNote: notePtr,
                    platformInfoKind: BnPlatformKind.iOS,
                    logLevel: logLevel)  // Phase 11.4 (#155): offset 28, read before the first managed line
                // #213 item 3: pass our compiled struct size — the runtime size-negotiates,
                // so a header/runtime version skew reads safely instead of out of bounds.
                return blazornative_init(Int32(MemoryLayout<bn_init_options>.size), &opts)
            }
        }
        guard result.status == 0 else {
            let detail = result.error.map { String(cString: $0) } ?? "<no detail>"
            throw BnRuntimeError.initFailed(status: result.status, detail: detail)
        }
        let version = result.version.map { String(cString: $0) } ?? "<null>"
        // `.safe`: the framework's OWN version string, no app/user/OS data. One of
        // exactly three `.safe` sites in the shell — see BnLog.swift's privacy rule.
        BnLog.info(BnRuntime.logCategory, "native init ok — \(version)", privacy: .safe)

        // register the frame callback
        let regRc = blazornative_register_frame_callback(bnFrameTrampoline)
        guard regRc == 0 else { throw BnRuntimeError.registerFailed(rc: regRc) }
        BnLog.info(BnRuntime.logCategory, "frame callback registered", privacy: .safe)

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
            share: bnBridgeShare,
            hostCallBegin: bnBridgeHostCallBegin) // Phase 9.0 — offset 72
        // Phase 5.4/9.0 size negotiation: pass our full struct size (80 since 9.0);
        // the runtime min-copies + zero-fills.
        let bridgeRc = blazornative_register_bridge(Int32(MemoryLayout<bn_bridge_callbacks>.size), &callbacks)
        guard bridgeRc == 0 else { throw BnRuntimeError.bridgeRegisterFailed(rc: bridgeRc) }
        BnLog.info(BnRuntime.logCategory, "shell bridge registered", privacy: .safe)

        // mount — the sync first frame fires inside this call
        let mountRc = component.withCString { blazornative_mount($0) }
        guard mountRc == 0 else { throw BnRuntimeError.mountFailed(rc: mountRc, component: component) }
        BnLog.info(BnRuntime.logCategory, "mounted \(component)")

        // Phase 9.1: a live session now exists — wire the WARM notification tap-through so
        // the delegate's `didReceive` re-routes over host_event on the serial lane (the ABI
        // is never called from the delegate's main thread directly). The non-nil dispatcher
        // is also BnNotifications' "session is live" signal (warm re-route vs cold stash).
        bridge.notifications.navigateDispatcher = { [weak self] route in
            self?.dispatchHostEvent(name: BnNotifications.navigateEventName, payload: route) ?? 1
        }
    }

    /// Phase 9.1: dispatches a host-INITIATED event over the EXISTING
    /// `blazornative_host_event` export — the iOS shell's FIRST call of it (the 9.0
    /// host_call_complete precedent). Runs on the serial dispatch lane (the threading
    /// contract; a warm tap arrives on main, so this hops), synchronous so the re-route
    /// swap's frames are applied before it returns. Returns the rc (0 = navigated).
    @discardableResult
    func dispatchHostEvent(name: String, payload: String?) -> Int32 {
        dispatchLane.sync {
            name.withCString { n in
                if let payload = payload {
                    return payload.withCString { p in blazornative_host_event(n, p) }
                }
                return blazornative_host_event(n, nil)
            }
        }
    }

    /// Phase 9.1: installs the notifications handler as the UNUserNotificationCenter
    /// delegate so a tap (cold or warm) reaches the shell. Called by the host once the
    /// bridge is live; the bridge's strong hold on the handler keeps the weak delegate alive.
    func installNotificationDelegate() {
        bridge.notifications.installDelegate()
    }

    /// Phase 11.4 Gate C (#155): resolves the `BnLogLevel` ordinal this boot passes
    /// in `bn_init_options.logLevel` (offset 28) and applies to the Swift seam.
    ///
    /// The iOS twin of `MainActivity.resolveLogLevel`, with the same two-step
    /// precedence and the same "a typo must not turn logging OFF" rule — only the
    /// platform's idioms differ (Android reads an Intent extra then a manifest
    /// `<meta-data>`; iOS reads an environment variable then Info.plist):
    ///
    ///   1. the `BN_LOG_LEVEL` ENVIRONMENT VARIABLE — a per-launch override, set
    ///      from an Xcode scheme or `xcrun simctl launch --console --env`. This is
    ///      what a developer reaches for to get one verbose session without
    ///      rebuilding, and it is how a simulator run can be asked to prove the
    ///      Release build is quiet by turning the noise back on;
    ///   2. the `io.blazornative.logLevel` INFO.PLIST STRING — the APP AUTHOR'S
    ///      declaration, which ships with the build.
    ///
    /// Anything unrecognised — including both being absent — resolves to
    /// [BnLogLevel.unset] (0), which the runtime AND [BnLog] both read as "apply
    /// the default", i.e. Warn. A misspelled value therefore falls back to the
    /// quiet-but-not-silent default rather than to silence.
    static func resolveLogLevel() -> Int32 {
        let fromEnv = BnLogLevel.fromName(ProcessInfo.processInfo.environment["BN_LOG_LEVEL"])
        if fromEnv != BnLogLevel.unset { return fromEnv }

        let plist = Bundle.main.object(forInfoDictionaryKey: "io.blazornative.logLevel") as? String
        return BnLogLevel.fromName(plist)
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
