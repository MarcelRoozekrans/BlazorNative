// ─────────────────────────────────────────────────────────────────────────────
// BnScrollCommandTests — #256, the iOS half of programmatic scroll.
//
// The Swift twin of Android's WidgetMapperScrollCommandTest and of the .NET
// ScrollCommandTests, behaviour for behaviour. Three things are pinned here, and
// a fourth is deliberately left to the device — §3 says why:
//
//  - **Decode**: wire kind 10, both modes. Shared with Kotlin.
//  - **Post-layout application**: the command must be honoured AFTER the frame it
//    arrived in has been laid out, or "the end" is the end of the PREVIOUS
//    content. This is the whole point of the feature and it is the one thing a
//    unit test can prove without a device.
//  - **The gesture gate** (production code, device-verified): honouring a command
//    while the user's finger is down would yank the content out from under them.
//    `applyScrollFrames` already has this gate for a weaker reason (correcting an
//    invalidated offset); a command is an explicit request and needs it MORE.
//    Android has no equivalent hazard — its overscroll is a glow, not a moved
//    offset. See §3 for why it is not unit-tested.
//  - **Clamping**: `UIScrollView` does NOT clamp an assigned `contentOffset` the
//    way `ScrollView.scrollTo` does, so the shell clamps — through the SAME
//    `clampedOffset` the 6.2 shrink path uses, so an out-of-range command and
//    "the end" cannot disagree.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit
@testable import BnHost

final class BnScrollCommandTests: BnHostTestCase {

    private static let scrollId: Int32 = 1
    private static let rows = 10
    // Ten 80-high rows in a 300×200 viewport → content 800, scroll range 600.
    // BnScrollDemo's shape, and the same numbers BnScrollWireTests uses, so the
    // two suites' expectations are readable against each other.
    private static let expectedEnd: CGFloat = 600

    private func scrollTree() -> [BnPatch] {
        var patches: [BnPatch] = [bnCreate(Self.scrollId, "scroll", nil),
                                  bnStyle(Self.scrollId, "width", "300"),
                                  bnStyle(Self.scrollId, "height", "200")]
        for i in 0..<Self.rows {
            patches.append(bnCreate(Int32(10 + i), "view", Self.scrollId))
            patches.append(bnStyle(Int32(10 + i), "height", "80"))
        }
        return patches
    }

    private func makeHost() throws -> (host: BnSyntheticHost, scroll: UIScrollView) {
        let host = BnSyntheticHost()
        host.render(scrollTree())
        let first = host.root.subviews[0]
        let scroll = try XCTUnwrap((first as? UIScrollView) ?? (first.subviews.first as? UIScrollView),
                                   "the tree must produce a UIScrollView")
        return (host, scroll)
    }

    // ── 1. Decode ────────────────────────────────────────────────────────────

    func testDecodesScrollToEndMode() throws {
        let patches = UnsafeMutableRawPointer.allocate(
            byteCount: BnFrameAdapter.patchSize, alignment: 8)
        defer { patches.deallocate() }
        memset(patches, 0, BnFrameAdapter.patchSize)
        patches.storeBytes(of: Int32(10), toByteOffset: BnFrameAdapter.patchKind, as: Int32.self)
        patches.storeBytes(of: Int32(42), toByteOffset: BnFrameAdapter.patchNodeId, as: Int32.self)
        patches.storeBytes(of: Int32(1), toByteOffset: BnFrameAdapter.patchAux, as: Int32.self)
        // PropValue deliberately left NULL — end mode carries no offset.

        let frame = try decodeSingle(patches)
        guard case .scrollTo(let nodeId, let toEnd, let offsetPt) = frame else {
            return XCTFail("expected a scrollTo, got \(frame)")
        }
        XCTAssertEqual(nodeId, 42)
        XCTAssertTrue(toEnd)
        XCTAssertEqual(offsetPt, 0)
    }

    func testDecodesScrollToOffsetMode_InvariantCulture() throws {
        let offset = strdup("123.5")!
        defer { free(offset) }

        let patches = UnsafeMutableRawPointer.allocate(
            byteCount: BnFrameAdapter.patchSize, alignment: 8)
        defer { patches.deallocate() }
        memset(patches, 0, BnFrameAdapter.patchSize)
        patches.storeBytes(of: Int32(10), toByteOffset: BnFrameAdapter.patchKind, as: Int32.self)
        patches.storeBytes(of: Int32(7), toByteOffset: BnFrameAdapter.patchNodeId, as: Int32.self)
        patches.storeBytes(of: Int32(0), toByteOffset: BnFrameAdapter.patchAux, as: Int32.self)
        patches.storeBytes(of: UnsafeRawPointer(offset),
                           toByteOffset: BnFrameAdapter.patchPropValue, as: UnsafeRawPointer.self)

        let frame = try decodeSingle(patches)
        guard case .scrollTo(let nodeId, let toEnd, let offsetPt) = frame else {
            return XCTFail("expected a scrollTo, got \(frame)")
        }
        XCTAssertEqual(nodeId, 7)
        XCTAssertFalse(toEnd)
        // The DOT is the point: .NET formats invariantly, so this shell must
        // parse invariantly. A NumberFormatter would honour the device locale and
        // read this as nil on a comma-decimal phone — a scroll to 0, on some
        // devices only.
        XCTAssertEqual(offsetPt, 123.5)
    }

    func testMalformedOffsetDegradesToZero_NeverThrows() throws {
        let junk = strdup("not-a-number")!
        defer { free(junk) }

        let patches = UnsafeMutableRawPointer.allocate(
            byteCount: BnFrameAdapter.patchSize, alignment: 8)
        defer { patches.deallocate() }
        memset(patches, 0, BnFrameAdapter.patchSize)
        patches.storeBytes(of: Int32(10), toByteOffset: BnFrameAdapter.patchKind, as: Int32.self)
        patches.storeBytes(of: Int32(3), toByteOffset: BnFrameAdapter.patchNodeId, as: Int32.self)
        patches.storeBytes(of: UnsafeRawPointer(junk),
                           toByteOffset: BnFrameAdapter.patchPropValue, as: UnsafeRawPointer.self)

        // A missing offset is not evidence of a corrupt frame, so — unlike the
        // contractual string fields — it must NOT throw and drop the whole frame.
        let frame = try decodeSingle(patches)
        guard case .scrollTo(_, _, let offsetPt) = frame else {
            return XCTFail("expected a scrollTo, got \(frame)")
        }
        XCTAssertEqual(offsetPt, 0)
    }

    private func decodeSingle(_ patches: UnsafeMutableRawPointer) throws -> BnPatch {
        let frame = UnsafeMutableRawPointer.allocate(
            byteCount: BnFrameAdapter.frameSize, alignment: 8)
        defer { frame.deallocate() }
        memset(frame, 0, BnFrameAdapter.frameSize)
        frame.storeBytes(of: UnsafeRawPointer(patches),
                         toByteOffset: BnFrameAdapter.framePatches, as: UnsafeRawPointer.self)
        frame.storeBytes(of: Int32(1),
                         toByteOffset: BnFrameAdapter.framePatchCount, as: Int32.self)
        let decoded = try BnFrameAdapter.read(frame)
        return try XCTUnwrap(decoded.patches.first)
    }

    // ── 2. Application, post-layout ──────────────────────────────────────────

    func testScrollToOffset_MovesTheViewport() throws {
        let (host, scroll) = try makeHost()
        XCTAssertEqual(scroll.contentOffset.y, 0)

        host.render([.scrollTo(nodeId: Self.scrollId, toEnd: false, offsetPt: 240)])

        XCTAssertEqual(scroll.contentOffset.y, 240)
        XCTAssertEqual(host.mapper.scrollCommandsApplied, 1)
    }

    func testScrollToEnd_LandsAtContentMinusViewport() throws {
        let (host, scroll) = try makeHost()

        host.render([.scrollTo(nodeId: Self.scrollId, toEnd: true, offsetPt: 0)])

        // 800 content − 200 viewport. The shell computes this, not .NET: content
        // height is a Yoga result that only exists on this side, and a
        // .NET-computed offset would be one frame stale.
        XCTAssertEqual(scroll.contentOffset.y, Self.expectedEnd)
        XCTAssertEqual(host.mapper.lastScrollCommandOffset, Self.expectedEnd)
    }

    func testScrollToEnd_InTheSameFrameThatAppendsRows_UsesTheNEWContent() throws {
        // THE REGRESSION THE WHOLE FEATURE IS FOR. The command and the rows that
        // grew the content arrive in ONE frame. A shell that scrolled when it
        // DECODED the command would use the pre-append content size and land at
        // the old end; queueing it until after CommitFrame's layout is what makes
        // the answer right.
        let (host, scroll) = try makeHost()
        host.render([.scrollTo(nodeId: Self.scrollId, toEnd: true, offsetPt: 0)])
        XCTAssertEqual(scroll.contentOffset.y, Self.expectedEnd, "precondition: at the old end")

        var appendAndScroll: [BnPatch] = []
        for i in 0..<5 {
            appendAndScroll.append(bnCreate(Int32(100 + i), "view", Self.scrollId))
            appendAndScroll.append(bnStyle(Int32(100 + i), "height", "80"))
        }
        // The command is emitted BEFORE the creates, exactly as Blazor's diff
        // orders it — the attribute belongs to the scroll element and the rows
        // are its children. Correctness must not depend on patch order.
        appendAndScroll.insert(.scrollTo(nodeId: Self.scrollId, toEnd: true, offsetPt: 0), at: 0)
        host.render(appendAndScroll)

        // 15 rows × 80 = 1200 content − 200 viewport = 1000.
        XCTAssertEqual(scroll.contentOffset.y, 1000,
                       "the command must be honoured against the content the SAME frame created")
    }

    func testAnOffsetPastTheEndIsClamped_NotRejected() throws {
        let (host, scroll) = try makeHost()

        host.render([.scrollTo(nodeId: Self.scrollId, toEnd: false, offsetPt: 99_999)])

        // UIScrollView does NOT clamp an assigned contentOffset (ScrollView.scrollTo
        // does, which is why only this shell needs the arithmetic) — so the shell
        // clamps, through the same clampedOffset the 6.2 shrink path uses.
        XCTAssertEqual(scroll.contentOffset.y, Self.expectedEnd)
    }

    func testANegativeOffsetIsClampedToTheTop() throws {
        let (host, scroll) = try makeHost()
        host.render([.scrollTo(nodeId: Self.scrollId, toEnd: false, offsetPt: 240)])

        host.render([.scrollTo(nodeId: Self.scrollId, toEnd: false, offsetPt: -500)])

        XCTAssertEqual(scroll.contentOffset.y, 0)
    }

    func testTwoCommandsInOneFrame_ApplyOnlyTheLast() throws {
        // Replaying both would be a visible double-jump. Two commands for one node
        // in one frame can only mean the later one.
        let (host, scroll) = try makeHost()

        host.render([.scrollTo(nodeId: Self.scrollId, toEnd: false, offsetPt: 100),
                     .scrollTo(nodeId: Self.scrollId, toEnd: false, offsetPt: 300)])

        XCTAssertEqual(scroll.contentOffset.y, 300)
        XCTAssertEqual(host.mapper.scrollCommandsApplied, 1,
                       "the superseded command must not be applied at all")
    }

    // ── 3. The gesture gate — asserted on DEVICE, not here, and why ─────────
    //
    // `applyPendingScrollCommands` drops a command while `scroll.isTracking` is
    // true: honouring one mid-drag would yank the content out from under a finger
    // that is actively holding it, and a dropped command is recoverable (the next
    // append re-issues one) where a fight with a live gesture is not.
    //
    // THERE IS NO UNIT TEST FOR IT HERE, deliberately rather than by omission.
    // `isTracking` is a read-only UIKit property driven by a real touch sequence;
    // the only ways to force it are swizzling the getter on `UIScrollView` — a
    // process-wide UIKit mutation inside a test bundle that also hosts the app —
    // or a production seam that exists solely to be overridden by a test. Both
    // cost more than the assertion is worth for a guard whose failure mode is
    // visible in one second of manual scrolling.
    //
    // It is on the device-verification checklist instead
    // (docs/ios-device-verification-handover.md): scroll a BnScroll with
    // AutoScrollToEnd on, hold the drag while new content arrives, and confirm the
    // content does not jump under your finger.

    // ── 4. A command on a node that is not a scroll view ─────────────────────

    func testACommandOnANonScrollNodeIsIgnored_NotFatal() throws {
        // Reachable through the raw-element hatch: OpenElement("view") +
        // AddAttribute("scrollTo", …). DATA, not a crash — the shell's standing
        // rule for a patch it cannot honour.
        let host = BnSyntheticHost()
        host.render([bnCreate(50, "view", nil), bnStyle(50, "height", "100")])

        host.render([.scrollTo(nodeId: 50, toEnd: true, offsetPt: 0)])

        XCTAssertEqual(host.mapper.scrollCommandsApplied, 0)
    }
}
