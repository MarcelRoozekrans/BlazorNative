// ─────────────────────────────────────────────────────────────────────────────
// BnImageAssertions — Phase 6.3 Gate 3: the image suite's shared vocabulary.
//
// Three things live here, and each one exists because getting it wrong is the way
// this phase's suite goes GREEN HAVING PROVEN NOTHING:
//
//   1. **THE SYNCHRONIZATION GATE** ([bnAwaitImageResults]) — 6.3 non-negotiable #6.
//   2. **THE FIXTURE PRECONDITIONS** ([bnAssertFixtureContract]) — asserted on the
//      DECODED image, before any frame is read.
//   3. **THE CACHE CLEAR** ([bnClearImageCaches]) — every test that asserts a BEFORE
//      table needs it, and exactly one test must NOT have it.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit
@testable import BnHost

/// **THE SYNCHRONIZATION GATE** (6.3 non-negotiable #6). Waits for [count] image requests
/// to reach a TERMINAL state — **Kingfisher's own per-node `completionHandler` verdict**,
/// recorded by the mapper (`BnWidgetMapper.imageTerminalCount`, incremented in
/// `onImageLoaded`/`onImageFailed`/`onImageCancelled`) — with a timeout that **FAILS the
/// test** rather than letting it proceed.
///
/// ── WHY THIS, AND NOT A POLL ON A FRAME ─────────────────────────────────────────────
/// **Two of `/image`'s three cases assert that NOTHING MOVED** — [0]'s frame is definite
/// and [2]'s failure reserves nothing — and the wire carries **no completion signal** by
/// design (no `OnLoad`, no `OnError`: each would change measurement). So a suite that reads
/// the AFTER table straight after mount passes [0] and passes [2] having proven nothing;
/// only [1] reddens. And a poll on band I's movement is the same mistake wearing a
/// disguise: it witnesses ONLY case [1]. It cannot tell you [0]'s request finished (so
/// [0]'s "unchanged" would be unproven) and it cannot tell you [2]'s finished (so [2]'s
/// "reserved nothing" might simply be a request still in flight).
///
/// Counted on `imageTerminalCount` and NOT on `imageResults.count`, because the results log
/// is a bounded ring — a test that waited past the cap on the ring would wait forever.
///
/// The re-solve a completion triggers happens INSIDE that completion's main-thread unit of
/// work, so a count this function can see is a count whose layout has already been applied.
/// The extra runloop turn after it is for the batch a completion may itself have posted.
func bnAwaitImageResults(_ mapper: BnWidgetMapper, _ count: Int,
                         timeout: TimeInterval = 30,
                         file: StaticString = #filePath, line: UInt = #line) {
    let deadline = Date().addingTimeInterval(timeout)
    while Date() < deadline {
        RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.02))
        if mapper.imageTerminalCount >= count {
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.02))
            return
        }
    }
    XCTFail("only \(mapper.imageTerminalCount) of \(count) image request(s) terminated within "
            + "\(Int(timeout))s. A timeout here is a FAILURE, never a licence to proceed: the "
            + "AFTER frames may only be read once EVERY request has ended, because two of the "
            + "three cases assert that NOTHING MOVED and both pass on a device that fetched "
            + "nothing.", file: file, line: line)
}

/// Gives a completion that must **NOT** arrive every chance to arrive — the other half of
/// the cancellation proof. A test that merely never saw the completion has proven nothing;
/// this one waits for it and then asserts it did not paint.
func bnSettle(_ seconds: TimeInterval = 1.0) {
    let deadline = Date().addingTimeInterval(seconds)
    while Date() < deadline {
        RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.02))
    }
}

/// Kingfisher's caches, cleared — through the shell's own facade (the XCTest bundle does not
/// link Kingfisher; see `project.yml`). Synchronous from the test's point of view: the disk
/// clear completes on the main queue, so the runloop is pumped for it.
///
/// Every test that asserts a BEFORE table needs this: a cached fixture completes **without
/// touching the fixture server**, which would un-gate the BEFORE table and make it dependent
/// on whichever test happened to run first. The DISK cache outlives the process, so a second
/// run of the suite on the same simulator would do it too.
///
/// **Exactly one test must NOT call it** — the WARM-CACHE double mount, which is the only
/// thing in the suite that exercises the synchronous memory-hit completion.
func bnClearImageCaches(file: StaticString = #filePath, line: UInt = #line) {
    var cleared = false
    BnImageLoader.clearCaches { cleared = true }
    let deadline = Date().addingTimeInterval(10)
    while !cleared && Date() < deadline {
        RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.02))
    }
    XCTAssertTrue(cleared, "Kingfisher's caches were never cleared — every BEFORE table in this "
                  + "suite depends on the fixtures going to the WIRE", file: file, line: line)
}

/// **THE FIXTURE'S CONTRACT** (`BnImageDemo.cs` §"THE FIXTURE'S CONTRACT"), asserted on the
/// **DECODED** fixtures **before any frame is looked at**. An unasserted fixture constraint
/// is a coincidence waiting to happen — and these double as the probe that the bytes came
/// from **our** server (port 8099 is HOST-GLOBAL on the macOS runner: a foreign listener
/// cannot serve an image whose natural size is the one we assert).
///
/// It asserts, on the fixture's OWN bytes:
///
///  - the pixel buffer's own `cgImage.width/height` — the number the shell actually MEASURES
///    with (`BnImageLoader.naturalPixelSize`), and the exact twin of Android's `bitmap.width`;
///  - the same numbers in POINTS, which agree ONLY because `UIImage(data:)` has `scale == 1`
///    — asserting both is what catches a *fixture* that did not survive its PNG encode;
///  - …and both against **`BnImageDemo.cs`'s declared constants**, transcribed into
///    `BnImageFixtureServer` and pinned against the `.cs` by a .NET drift test. Three copies
///    of four numbers, pinned rather than trusted — because **no single-device test in either
///    suite can catch a breach of the unit rule**: each shell stays internally consistent, and
///    nothing compares the two frame tables automatically.
///
/// ── WHAT THIS FUNCTION CANNOT SEE, AND WHERE THAT IS ASSERTED INSTEAD ────────────────
/// **It never touches the `UIImage` Kingfisher handed the shell.** Everything here is decoded
/// by `UIImage(data:)`, in the test, from the fixture's own bytes — and `UIImage(data:)`
/// **cannot see a Kingfisher option**. So a `.scaleFactor(UIScreen.main.scale)` on the request
/// (Kingfisher's own documented idiom, and the first thing an implementer reaches for) leaves
/// every assertion below GREEN. This comment used to claim the opposite, which is worse than
/// silence.
///
/// The loader's configuration is pinned where it can be: **`BnImageTests
/// .testAnIntrinsicImageMeasuresZero…` reads `scale` off the `UIImage` in the shell's own
/// `BnImageView`** — the only object in the process that carries it.
func bnAssertFixtureContract(intrinsic: UIImage, fixed: UIImage,
                             file: StaticString = #filePath, line: UInt = #line) {
    let sectionWidth: CGFloat = 300

    XCTAssertEqual(intrinsic.scale, 1, accuracy: 0.001,
                   "UIImage(data:) has scale == 1, and THAT is what makes one FILE PIXEL one "
                   + "POINT — so the POINT assertions below are the same numbers as the PIXEL "
                   + "ones. (This says nothing about the LOADER: a `.scaleFactor` on the "
                   + "Kingfisher request cannot reach a UIImage the test decoded itself. That is "
                   + "asserted on the shell's own image — see the note above.)",
                   file: file, line: line)

    let cg = intrinsic.cgImage
    XCTAssertNotNil(cg, "the intrinsic fixture must decode to a real pixel buffer — that buffer's "
                    + "dimensions are what the shell MEASURES with", file: file, line: line)
    XCTAssertEqual(cg?.width, BnImageFixtureServer.INTRINSIC_W,
                   "Wi, in the PIXELS the shell reads (cgImage.width — the twin of Android's "
                   + "bitmap.width)", file: file, line: line)
    XCTAssertEqual(cg?.height, BnImageFixtureServer.INTRINSIC_H, "Hi, in pixels",
                   file: file, line: line)
    XCTAssertEqual(intrinsic.size.width, BnImageFixtureServer.INTRINSIC_W_PT, accuracy: 0.5,
                   "…and the same number in POINTS. The two agree ONLY because scale == 1, and "
                   + "asserting both is what catches a decode option that divided one of them",
                   file: file, line: line)
    XCTAssertEqual(intrinsic.size.height, BnImageFixtureServer.INTRINSIC_H_PT, accuracy: 0.5,
                   file: file, line: line)

    XCTAssertTrue(intrinsic.size.width > 0 && intrinsic.size.width <= sectionWidth,
                  "0 < Wi ≤ 300: a section is 300 wide, so the measure func is called with "
                  + "AT_MOST(300) — a wider fixture asks a clamping question this phase "
                  + "deliberately does not answer (no ContentMode). Got \(intrinsic.size.width)",
                  file: file, line: line)
    XCTAssertTrue(intrinsic.size.height > 0,
                  "Hi > 0, comfortably — HI *IS* THE REFLOW. A 0-high fixture would make the "
                  + "reflow assertion vacuously true. Got \(intrinsic.size.height)",
                  file: file, line: line)

    XCTAssertEqual(fixed.cgImage?.width, BnImageFixtureServer.FIXED_W, file: file, line: line)
    XCTAssertEqual(fixed.cgImage?.height, BnImageFixtureServer.FIXED_H, file: file, line: line)
    XCTAssertFalse(fixed.size.width == 200 && fixed.size.height == 120,
                   "(Wfixed, Hfixed) ≠ (200, 120): otherwise '[0] measures 200 × 120' is a "
                   + "COINCIDENCE, not a proof that a declared size short-circuits measurement "
                   + "entirely. Got \(fixed.size.width) × \(fixed.size.height)",
                   file: file, line: line)
    XCTAssertFalse(fixed.size.width == 40 && fixed.size.height == 40,
                   "…and ≠ (40, 40), BnScrollDemo's row image, which buys the same proof inside "
                   + "the scroll (it shares this fixture file)", file: file, line: line)
}

/// The image inside a `BnImageDemo`-shaped section — child [0]; the band is child [1].
func bnImageIn(_ section: UIView,
               file: StaticString = #filePath, line: UInt = #line) throws -> BnImageView {
    try XCTUnwrap(section.subviews.first as? BnImageView,
                  "an image node's view must be a BnImageView (got "
                  + "\(section.subviews.first.map { String(describing: type(of: $0)) } ?? "nothing"))",
                  file: file, line: line)
}
