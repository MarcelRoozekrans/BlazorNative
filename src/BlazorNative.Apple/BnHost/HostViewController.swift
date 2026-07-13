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

    override func viewDidLoad() {
        super.viewDidLoad()
        view.backgroundColor = .systemBackground

        // Do not boot under tests — the XCTest bundle owns the native session.
        guard NSClassFromString("XCTestCase") == nil else { return }

        let mapper = BnWidgetMapper(root: view)
        let runtime = BnRuntime(mapper: mapper)
        self.mapper = mapper
        self.runtime = runtime

        // Boot off the main thread (init/mount are synchronous work); the mapper
        // hops its render batch back to the main queue on CommitFrame.
        DispatchQueue.global(qos: .userInitiated).async {
            do {
                try runtime.start(component: "BnDemo", os: "ios")
            } catch {
                NSLog("[HostViewController] boot failed: \(error)")
            }
        }
    }

    /// Phase 6.1 — RELAYOUT ON HOST RESIZE. Same calculate + apply as CommitFrame,
    /// with the new bounds; rotation therefore works for free. The mapper's own pass
    /// assigns subview frames, so this must run AFTER the framework's layout of
    /// `view` itself — which is exactly what `viewDidLayoutSubviews` is.
    override func viewDidLayoutSubviews() {
        super.viewDidLayoutSubviews()
        mapper?.calculateAndApply()
    }
}
