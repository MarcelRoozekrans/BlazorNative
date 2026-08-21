// ─────────────────────────────────────────────────────────────────────────────
// HostViewController — Phase 5.2 (M5 DoD #2): the minimal host that boots the
// Swift shell into its root view (the seed of the Phase 5.3 demo app). The twin
// of Android's MainActivity: it constructs a BnWidgetMapper over its root view, a
// BnRuntime over the mapper, holds the runtime strongly (so the @convention(c)
// callback trampoline is never released), and boots BnDemo on a background thread.
//
// Phase 6.1: it is also the RESIZE hook. Yoga solved the tree against the host's
// bounds, so a rotation / split-screen / any bounds change must re-solve — no patch
// is involved (.NET never learns the host got wider, and nothing in the render tree
// changed; this is a pure host event). `viewDidLayoutSubviews` is the twin of
// Android's `OnLayoutChangeListener` on widget_root.
//
// Under XCTest it stays INERT — the test owns the single native session (see
// AppDelegate). Detection via NSClassFromString("XCTestCase").
// ─────────────────────────────────────────────────────────────────────────────

import UIKit

final class HostViewController: UIViewController {

    /// Strong ref for the callback's lifetime — the twin of MainActivity's
    /// `runtime` field (a local would let the trampoline be released).
    private var runtime: BnRuntime?

    /// Held so the resize hook can re-run the layout pass.
    private var mapper: BnWidgetMapper?

    /// The bounds size the tree was last SOLVED against — the guard in
    /// [viewDidLayoutSubviews]. `nil` until the first pass.
    private var lastSolvedSize: CGSize?

    deinit {
        // DETERMINISTIC teardown, on the main thread — the twin of Android's
        // `MainActivity.onDestroy → mapper.destroy()`. Leaving it to the mapper's own
        // `deinit` would free the Yoga tree on whatever thread dropped the last
        // reference, and this class boots on a BACKGROUND queue: a second boot would
        // race the previous mapper's subtree free (which mutates the .mm's
        // unsynchronised registry) against the new mapper's main-thread applyBatch.
        // A UIViewController's deinit is main-thread by UIKit's contract.
        mapper?.destroy()
    }

    override func viewDidLoad() {
        super.viewDidLoad()
        view.backgroundColor = .systemBackground

        // Do not boot under tests — the XCTest bundle owns the native session.
        guard NSClassFromString("XCTestCase") == nil else { return }

        // Phase 11.4 Gate C (#155/#164): THE TRANSPORT, and it must be installed
        // BEFORE anything calls into the runtime. Everything the shared .NET runtime
        // writes to `Console.Error` — its 31 diagnostic sites, the BCL's own output,
        // NativeAOT's TypeLoadException detail, and `blazornative_init`'s deliberate
        // full `ex.ToString()` on a trim failure — goes to process fd 2, which an
        // unattached iOS build surfaces nowhere. This points fd 2 at the unified log.
        //
        // Inside the XCTest guard ON PURPOSE: the test bundle is HOSTED in this
        // process and xcodebuild reads the runner's stdio, so capturing fd 2 under
        // XCTest would redirect the test output out of xcodebuild's hands. See
        // BnStderrPump.swift's header.
        BnStderrPump.install()

        // Phase 13.4 (#282) — THE COLD-LAUNCH ROUTE SEED, and it is a DIFFERENT
        // question from the mount component chosen further down.
        //
        // The route seeds .NET's router UNCONDITIONALLY, even when the component map
        // cannot resolve it. Android has always done this; iOS dropped unmapped routes
        // until Phase 13.4 (#282), so a cold deep link to a route the map did not know
        // left .NET believing it was at "/". Which routes exist is .NET's question, and
        // the shell must not answer it by silently discarding the link.
        //
        // It was in fact worse than "unmapped routes": iOS took the default
        // `AppleShellBridge()`, whose route is "/", and nothing on this path ever
        // replaced it — so `IMobileBridge.GetCurrentRouteAsync` read "/" even for a link the
        // map DID resolve. The right page was mounted and the router disagreed with
        // the screen, which is the quieter half of the same defect.
        //
        // The twin is `AndroidShellBridge`'s construction in MainActivity.onCreate,
        // whose route argument is `initialRoute = deepLinkRoute ?: "/"` — the same
        // expression, seeded from the same parse. (Quoted as the argument, not the
        // whole call: the Kotlin line also carries `onError`, and a reader grepping a
        // half-remembered full call would not find it.)
        //
        // Read HERE, synchronously, and not from the background boot below: AppDelegate
        // stashes a cold-launch URL inside `didFinishLaunchingWithOptions` BEFORE
        // `makeKeyAndVisible()`, and it is that call which drives this method — so the
        // stash is already in place, and the bridge must carry the route from the moment
        // it is constructed (it is registered with the runtime before mount).
        //
        // Only a DEEP LINK seeds the route, matching Android. A cold notification tap
        // still seeds only the mount: its stash lives on `runtime.bridge.notifications`,
        // which does not exist yet at this point, and Android's notification path seeds
        // no route either — closing that asymmetry would be a change to both shells, not
        // to this one.
        let launchRoute = BnDeepLink.shared.pendingLaunchRouteForTest() ?? "/"

        let mapper = BnWidgetMapper(root: view)
        let runtime = BnRuntime(mapper: mapper, bridge: AppleShellBridge(initialRoute: launchRoute))
        self.mapper = mapper
        self.runtime = runtime

        // Phase 9.1: install the UNUserNotificationCenter delegate BEFORE boot so a
        // notification tap (cold launch, or warm while alive) reaches the shell. A COLD
        // tap stashes its route; we resolve it to a mount component (deepLinkComponents —
        // iOS mounts by NAME) so the launch route SEEDS the initial mount, the way the
        // sim boot tests mount a routed component. Absent a tap, BnDemo is the default.
        // (The real cold-tap timing/UX is owner-device territory — the M9 iOS deferral.)
        runtime.installNotificationDelegate()

        // Boot off the main thread (init/mount are synchronous work); the mapper
        // hops its render batch back to the main queue on CommitFrame.
        DispatchQueue.global(qos: .userInitiated).async {
            // The COLD mount seed, now from EITHER launch surface. A deep link wins
            // over a notification tap because the two cannot both be the reason the
            // app launched, and a URL is the more explicit request — the user acted
            // on a link, not on something the app scheduled. Absent both, BnDemo.
            //
            // UNCHANGED by #282, deliberately: this lookup answers "which view do we
            // put on screen first?", which the shell genuinely has to decide before
            // .NET exists, and falling back to BnDemo for an unmapped route is the
            // right answer to THAT question. What #282 changed is the route seed
            // above — the shell no longer lets this map's silence overwrite the route
            // the user asked for.
            let component = BnDeepLink.shared.resolvedLaunchComponent()
                ?? runtime.bridge.notifications.resolvedLaunchComponent()
                ?? "BnDemo"
            do {
                try runtime.start(component: component, os: "ios")
            } catch {
                // A boot fault. Redacted by default: the error's description carries
                // the runtime's own detail (design §7's information-disclosure rule).
                BnLog.error("HostViewController", "boot failed: \(error)")
            }
        }
    }

    /// Phase 6.1 — RELAYOUT ON HOST RESIZE. Same calculate + apply as CommitFrame,
    /// with the new bounds; rotation therefore works for free. The mapper's own pass
    /// assigns subview frames, so this must run AFTER the framework's layout of
    /// `view` itself — which is exactly what `viewDidLayoutSubviews` is.
    ///
    /// **Guarded on a genuine bounds-SIZE change**, exactly as Android's twin is
    /// (`YogaLayout`'s OnLayoutChangeListener: "the listener also fires on passes that
    /// did not move the host"). `viewDidLayoutSubviews` runs on every layout pass, and
    /// every `addSubview`/`insertSubview` in a batch calls `setNeedsLayout` on the
    /// host — so each commit would be followed by a full, redundant re-solve of the
    /// whole tree for an identical answer.
    ///
    /// The guard lives HERE and not in `calculateAndApply()`: CommitFrame must ALWAYS
    /// re-solve (the tree changed, the bounds did not).
    override func viewDidLayoutSubviews() {
        super.viewDidLayoutSubviews()
        let size = view.bounds.size
        guard size != lastSolvedSize else { return }
        lastSolvedSize = size
        mapper?.calculateAndApply()
    }
}
