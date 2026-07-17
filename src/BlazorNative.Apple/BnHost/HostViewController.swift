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

        let mapper = BnWidgetMapper(root: view)
        let runtime = BnRuntime(mapper: mapper)
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
            let component = runtime.bridge.notifications.resolvedLaunchComponent() ?? "BnDemo"
            do {
                try runtime.start(component: component, os: "ios")
            } catch {
                NSLog("[HostViewController] boot failed: \(error)")
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
