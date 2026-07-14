// ─────────────────────────────────────────────────────────────────────────────
// BnImageLoader — Phase 6.3 Gate 3 (M6 DoD #5): **THE ONLY FILE IN THIS REPO THAT
// IMPORTS KINGFISHER.** The iOS twin of Coil in `WidgetMapper.kt` — the same
// normative parity contract (design §"The parity contract"), the same numbers.
//
// The seam is deliberate and is the same discipline the shell already keeps around
// Yoga: the mapper talks to a plain Swift surface (`load` → `BnImageOutcome`), and
// the library lives behind it. That is what lets the XCTest bundle drive the whole
// image path through `@testable import BnHost` WITHOUT linking Kingfisher — and it
// is the one place a mutation can stub the loader to a constant size, which the
// tests' decoded-fixture assertions then redden.
//
// ── THE UNIT: ONE FILE PIXEL IS ONE POINT (normative — design §"The unit") ────
//
// The measured size of an image is **the pixel count of the decoded file**, read
// directly as points. A 160-pixel-wide PNG measures **160pt**, on every device, at
// every scale — because that is the only reading under which iOS and Android
// compute the SAME FRAME (Android reads `bitmap.width`, the raw pixel count).
//
// Two things enforce it here, and they are independent on purpose:
//
//  1. **[naturalPixelSize] reads `cgImage.width/height` — THE PIXEL BUFFER'S OWN
//     DIMENSIONS**, which is the exact twin of Android's `bitmap.width`. It is
//     NOT `image.size` (which is `pixels / image.scale`), so no option, processor
//     or future refactor can quietly divide it. This is a HARDENING of the
//     contract, not a departure from it: the contract says "the pixel count of the
//     decoded file", and this asks for exactly that.
//  2. **The request is configured with NO `scaleFactor` and NO processor** (see
//     [load]), so `UIImage(data:).scale` stays 1 and `image.size` AGREES with the
//     pixel count. `BnImageTests` asserts BOTH — the decoded image's `scale == 1`
//     and its `size`, and the measured FRAME against `BnImageDemo.cs`'s declared
//     pixel constants — because a shell that asserted only one of them would have
//     no way to notice the other drifting.
//
//   ✗ **DO NOT set `.scaleFactor(UIScreen.main.scale)`.** It is Kingfisher's own
//     documented idiom for crisp images and it is the first thing an implementer
//     reaches for. It would make a 160px fixture report **160/3 ≈ 53.3pt** on a 3×
//     simulator against Android's 160dp, and NO SINGLE-DEVICE TEST IN EITHER SUITE
//     COULD SEE IT — each shell stays internally consistent; only the two frame
//     tables side by side disagree, and nothing compares them automatically.
//   ✗ **DO NOT add a Downsampling/Resizing processor**, for the same reason and for
//     the contract's "no downsampling that changes the reported size" row.
//     (Android's mirror of that trap is Coil's `Size.ORIGINAL` — without it Coil
//     sizes the decode to the DISPLAY.)
//
// ── A MEMORY-CACHE HIT COMPLETES *SYNCHRONOUSLY* (normative) ──────────────────
//
// Kingfisher's callback queue defaults to `.mainCurrentOrAsync`, so a memory-cache
// hit issued FROM the main thread runs its completion **inside the `retrieveImage`
// call, before it returns** — and the shell issues every request from
// `UpdateProp("src", …)`, i.e. **inside the patch batch**. (Coil does the same on
// `Dispatchers.Main.immediate`; this is not an iOS quirk, it is the contract's
// un-numbered non-negotiable.) `.mainCurrentOrAsync` is therefore **stated
// explicitly** rather than inherited: the behaviour is load-bearing, and a default
// that changed under us would change WHEN the shell's completion runs.
//
// The two consequences live in `BnWidgetMapper` (`resolveLayout` must no-op inside
// a batch; the task is recorded only if it is STILL LIVE) — see them there.
//
// ── THE TIMEOUT IS SHORTENED ON PURPOSE ──────────────────────────────────────
//
// Gate 2's cancellation mutation reddens *by name* on Android in ~10s because
// OkHttp's read timeout is 10s: an UNCANCELLED request against the held `/slow.png`
// fixture times out and reports an ERROR, so the assertion fails on the OUTCOME
// (`expected CANCELLED but was ERROR`) rather than on a gate timeout. `URLSession`'s
// default is **60 seconds** and Kingfisher's own default is 15 — either would blow
// past the tests' 30s synchronization gate and turn a named contract failure into an
// anonymous timeout. So it is set, here, once, below both.
// ─────────────────────────────────────────────────────────────────────────────

import UIKit
import Kingfisher

/// What one image request DID. The twin of Kotlin's `WidgetMapper.ImageOutcome`.
///
/// `cancelled` is a NORMAL outcome of a `Src` change or a node removal — and a SETUP
/// FAILURE on `/image`, where nothing cancels anything. Kingfisher reports a cancel as
/// a `.failure` whose error `isTaskCancelled`; [BnImageLoader] separates the two here
/// so the mapper never has to know that.
enum BnImageOutcome: Hashable {
    case success
    case error
    case cancelled
}

/// One terminated image request. The `url` is the one **the wire carried**, which is
/// what makes the demo's outcome assertions a drift pin on `BnImageDemo.cs`'s three
/// `internal const` sources as well (a device-side test cannot read a `.cs` file; it
/// can read what the renderer put on the `UpdateProp` wire).
struct BnImageResult: Equatable {
    let nodeId: Int32
    let url: String
    let outcome: BnImageOutcome
}

/// What [BnImageLoader.load] hands back to its caller.
enum BnImageLoadOutcome {
    case success(UIImage)
    case failure(Error)
    case cancelled
}

/// A cancellation handle — Kingfisher's `DownloadTask`, behind the seam. Held in the
/// mapper's in-flight map; **cancelling is memory safety, not hygiene** (a completion
/// into a purged node touches a freed `YGNodeRef`).
final class BnImageTask {
    private let cancelAction: () -> Void
    init(_ cancelAction: @escaping () -> Void) { self.cancelAction = cancelAction }
    func cancel() { cancelAction() }
}

/// **A REAL `UIImageView`** — `image` has been one since 6.1's Gate 3 review, so an
/// EMPTY image measures `.zero` by construction rather than by accident, and that is
/// exactly the pre-load state 6.3 needs.
///
/// The subclass adds ONE thing: the **recorded natural size**, which is what the
/// image's Yoga measure function reports. It is deliberately NOT read off
/// `self.image` at measure time:
///
///  - `sizeThatFits` on a `UIImageView` answers with `image.size`, which is
///    `pixels / image.scale` — a number that is correct only as long as nothing ever
///    sets a scale factor. **`image` is the ONE measured nodeType whose widget can
///    answer in the wrong unit**, on BOTH platforms (Android's `ImageView` answers in
///    raw pixels, which the generic measure path would then divide by density — 61dp
///    for a 160px file on the Pixel 6 AVD). Both shells therefore give `image` a
///    MEASURE FUNCTION OF ITS OWN, fed by the decoded bytes' pixel count.
///  - and it makes "no bytes" and "bytes with no pixel buffer" (the GIF/SVG ledger)
///    the SAME state: `nil` → 0 × 0, which is what the contract's failure row says.
final class BnImageView: UIImageView {

    /// The decoded file's size **in PIXELS** — and therefore, by the unit rule, the
    /// POINTS this node measures to. `nil` = no bytes (never fetched, failed,
    /// cancelled, or cleared): the node measures **0 × 0**.
    var bnNaturalSize: CGSize?
}

/// The shell's image-loading surface. Everything Kingfisher is on the far side of it.
enum BnImageLoader {

    /// See the file header. Below the tests' 30s synchronization gate **and** below
    /// Kingfisher's own 15s default, so a request the shell failed to cancel dies on
    /// the OUTCOME (`ERROR`) rather than on the gate — which names the contract row
    /// that broke instead of blaming the harness.
    static let downloadTimeout: TimeInterval = 5

    /// Fetch + decode. The completion fires **on the main thread** — synchronously,
    /// from inside this call, on a memory-cache hit (see the file header).
    ///
    /// Returns the cancellation handle, or `nil` when the request never became one (a
    /// cache hit: Kingfisher has nothing to cancel). **The caller must record it only
    /// if the request is still live** — see `BnWidgetMapper.handleSrc`.
    @discardableResult
    static func load(url: URL,
                     completion: @escaping (BnImageLoadOutcome) -> Void) -> BnImageTask? {
        let task = KingfisherManager.shared.retrieveImage(
            with: url,
            options: [
                // THE UNIT (see the header). No `.scaleFactor`, no processor — the
                // options list is short on purpose, and every item in it is a decision.
                .downloadTimeout(downloadTimeout),
                // Stated, not inherited: the SYNCHRONOUS memory-cache completion the
                // shell is written against is a property of THIS value.
                .callbackQueue(.mainCurrentOrAsync),
            ]
        ) { result in
            switch result {
            case .success(let value):
                completion(.success(value.image))
            case .failure(let error):
                // Kingfisher has no separate "cancelled" case — a cancel is a failure
                // whose error says so. The mapper must not have to know that.
                completion(error.isTaskCancelled ? .cancelled : .failure(error))
            }
        }
        guard let task = task else { return nil }
        return BnImageTask { task.cancel() }
    }

    /// An image's natural size **in PIXELS** — the pixel buffer's own dimensions, the
    /// exact twin of Android's `bitmap.width` (design §"The unit"). Never `image.size`,
    /// which is `pixels / scale`.
    ///
    /// **A decoded image with no pixel buffer has NO natural size this shell knows how
    /// to read, so it measures 0 × 0 and says so** — the twin of Android's F3 fix. The
    /// thing that would reach it is an animated/vector format (GIF, SVG), both
    /// ledgered for a later phase, and both of which would otherwise land on a made-up
    /// number with no test anywhere to notice. A capability we have not designed
    /// measures ZERO; it does not silently get a plausible frame.
    static func naturalPixelSize(of image: UIImage) -> CGSize? {
        guard let cg = image.cgImage else { return nil }
        return CGSize(width: cg.width, height: cg.height)
    }

    /// **TEST SUPPORT ONLY** — Kingfisher's caches, cleared.
    ///
    /// It lives here rather than in the XCTest bundle because it is the ONE thing the
    /// tests need that only Kingfisher can do, and moving it across the seam would mean
    /// linking Kingfisher into the test bundle as well (see `project.yml`). The
    /// completion fires on the MAIN queue — the caller pumps the runloop for it.
    ///
    /// Every test that asserts the BEFORE table clears the caches first: a cached
    /// fixture completes **without touching the fixture server**, which would un-gate
    /// the BEFORE table and make it order-dependent on whichever test ran first (the
    /// disk cache also outlives the process). And exactly ONE test deliberately does
    /// NOT clear them — the WARM-CACHE double mount, which is the only thing that
    /// exercises the synchronous completion path.
    static func clearCaches(completion: @escaping () -> Void) {
        let cache = KingfisherManager.shared.cache
        cache.clearMemoryCache()
        cache.clearDiskCache(completion: completion)
    }
}
