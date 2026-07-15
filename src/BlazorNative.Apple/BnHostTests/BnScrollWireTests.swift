// ─────────────────────────────────────────────────────────────────────────────
// BnScrollWireTests — Phase 7.2 Gate 3 Task 3.1 — **THE CONFLATION, AT THE
// MECHANISM** (the wire contract, docs/plans/2026-07-15-phase-7.2-design.md
// §"The wire contract" — NORMATIVE; the iOS mirror of Android's
// `WidgetMapperOnScrollTest`, behaviour for behaviour, over `UIScrollView`'s
// delegate instead of `setOnScrollChangeListener`).
//
// The contract's rows, each a test:
//
//  - **Event**: a scroll node with the `scroll` event attached dispatches its
//    content offset — in **points** (which ARE the density-independent unit; iOS
//    has NO conversion site where Android divides px by density), as the
//    invariant float payload `NativeRenderer.ParseScrollOffset` parses.
//  - **Conflation / Backpressure**: ONE pending offset per node; a new sample
//    REPLACES it; at most one dispatch in flight per node — a busy lane means
//    FEWER, FRESHER events, **never a queue**. The middle value of a burst is
//    NEVER dispatched; the freshest always is. (This is the test the required
//    mutations must redden: queue-instead-of-replace, dispatch-per-sample.)
//  - **Batch guard**: a sample arriving DURING a patch batch conflates and is
//    flushed ONCE at the batch end. The mid-batch sample here is REAL and it is
//    iOS's own echo path: the 6.2 shrink clamp (`applyScrollFrames`) writes
//    `contentOffset` inside `calculateAndApply`, and that write fires the
//    delegate SYNCHRONOUSLY. (Android's echo is `ScrollView.onLayout`'s
//    framework re-clamp — different mechanism, SAME observable, and both tests
//    pin the observable: the corrected offset reaches .NET, exactly once, after
//    the commit.)
//  - **Ordering**: preserved, NOT assumed — the last test pins that a scroll
//    dispatch submitted through `BnRuntime`'s onComplete overload rides the SAME
//    serial FIFO lane as fire-and-forget input dispatches, cannot overtake one
//    queued before it, and that its completion ALWAYS fires (a lost completion
//    wedges the conflation slot forever).
//  - **Detach / purge**: the 6.3 stale-callback discipline — a detached or
//    removed node's pending offset is DROPPED, never dispatched, and a late
//    completion is a no-op.
//
// The dispatcher here is a [RecordingLane]: it records every dispatch and can
// WITHHOLD the completion signal, which is how a test makes the lane "busy"
// deterministically — the same seam `BnRuntime` wires to
// `dispatchEvent(handlerId:eventName:"scroll":payload:onComplete:)`.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit
@testable import BnHost

final class BnScrollWireTests: BnHostTestCase {

    private static let scrollId: Int32 = 1
    private static let handler: Int32 = 77
    private static let rows = 10
    // Ten 80-high rows over a 300×200 viewport → content 800, range 600:
    // BnScrollDemo's shape, with the `scroll` event ATTACHED — the one thing
    // 6.2's trees never carried.

    /// The scroll lane, scripted: records (handlerId, payload) per dispatch and —
    /// when [withhold] — parks the completion for the test to release, which is a
    /// deterministic "the lane is busy" (a real lane is busy for exactly the
    /// duration between submit and the completion callback). Main-thread only —
    /// the mapper submits from the main thread and the tests release from it.
    private final class RecordingLane {
        var withhold = false
        var dispatches: [(handlerId: Int32, payload: String)] = []
        var parked: [() -> Void] = []
        init(withhold: Bool = false) { self.withhold = withhold }
        func dispatcher(_ handlerId: Int32, _ payload: String, _ done: @escaping () -> Void) {
            dispatches.append((handlerId, payload))
            if withhold { parked.append(done) } else { done() }
        }
    }

    private func wiredScrollTree(handlerId: Int32 = BnScrollWireTests.handler,
                                 wrapperId: Int32? = nil) -> [BnPatch] {
        var patches: [BnPatch] = []
        var parent: Int32? = nil
        if let wrapperId = wrapperId {
            patches.append(bnCreate(wrapperId, "view", nil))
            parent = wrapperId
        }
        patches.append(bnCreate(Self.scrollId, "scroll", parent))
        patches.append(bnStyle(Self.scrollId, "width", "300"))
        patches.append(bnStyle(Self.scrollId, "height", "200"))
        for i in 0..<Self.rows {
            patches.append(bnCreate(Int32(10 + i), "view", Self.scrollId))
            patches.append(bnStyle(Int32(10 + i), "height", "80"))
        }
        patches.append(.attachEvent(nodeId: Self.scrollId, eventName: "scroll", handlerId: handlerId))
        return patches
    }

    private func makeHost(lane: RecordingLane, wrapperId: Int32? = nil) throws
        -> (host: BnSyntheticHost, scroll: UIScrollView) {
        let host = BnSyntheticHost()
        host.mapper.onScrollEvent = { [weak lane] handlerId, payload, done in
            lane?.dispatcher(handlerId, payload, done)
        }
        host.render(wiredScrollTree(wrapperId: wrapperId))
        let first = host.root.subviews[0]
        let scroll = try XCTUnwrap((first as? UIScrollView) ?? (first.subviews.first as? UIScrollView),
                                   "the wired tree must produce a UIScrollView")
        return (host, scroll)
    }

    /// Pumps the main queue for [hops] enqueued markers — enough to let a posted
    /// completion→flush chain settle (each link is one `DispatchQueue.main.async`
    /// hop; the chains here are at most two links deep).
    private func drainMain(_ hops: Int = 3, file: StaticString = #filePath, line: UInt = #line) {
        for _ in 0..<hops {
            var done = false
            DispatchQueue.main.async { done = true }
            let deadline = Date().addingTimeInterval(5)
            while !done && Date() < deadline {
                RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.01))
            }
            XCTAssertTrue(done, "the main queue never drained", file: file, line: line)
        }
    }

    /// Drives the REAL delegate: a main-thread offset write, exactly what a drag
    /// tick, a deceleration tick and the shell's own clamp all reduce to.
    private func sample(_ scroll: UIScrollView, _ pt: CGFloat) {
        scroll.setContentOffset(CGPoint(x: 0, y: pt), animated: false)
        drainMain()
    }

    private func releaseOneCompletion(_ lane: RecordingLane) {
        lane.parked.removeFirst()()
        drainMain()
    }

    // ── Event: the offset crosses in points, on the attached handler ─────────

    func testAScrollSampleDispatchesTheOffsetInPointsOnTheAttachedHandler() throws {
        let lane = RecordingLane()
        let (host, scroll) = try makeHost(lane: lane)

        sample(scroll, 150)

        XCTAssertEqual(lane.dispatches.count, 1, "ONE sample, ONE dispatch — the lane was free")
        let dispatch = lane.dispatches[0]
        XCTAssertEqual(dispatch.handlerId, Self.handler,
                       "…on the handlerId the AttachEvent carried")
        XCTAssertEqual(Float(dispatch.payload), 150,
                       "THE UNIT RULE (6.1, one conversion site — and iOS's half of it is NO "
                       + "conversion at all): the payload is contentOffset.y read as points, the "
                       + "number Yoga speaks and Android asserts as dp")
        XCTAssertEqual(dispatch.payload, Float(150).description,
                       "…and it is the exact invariant string ParseScrollOffset parses (Swift's "
                       + "Float.description never localizes — a \"1,5\" would be a loud rc-2 fault)")
        XCTAssertEqual(host.mapper.scrollSamplesSeen, 1, "counters: 1 seen")
        XCTAssertEqual(host.mapper.scrollDispatchesSent, 1, "counters: 1 sent")
    }

    // ── Conflation: THE RULE — replace, never queue; freshest wins ────────────

    /// **THE PHASE'S ROW, AT THE MECHANISM** — and the test the required
    /// mutations must redden:
    ///
    ///  - *queue instead of replace* (a list in the slot) → the middle offset
    ///    gets dispatched → the `[first, freshest]` assertion fails;
    ///  - *dispatch per sample* (ignore `inFlight`) → 3 dispatches for 3 samples
    ///    while the lane is busy → the "exactly 1 while busy" and the "2 total"
    ///    assertions fail.
    ///
    /// A busy lane sees FEWER, FRESHER events: 3 samples → 2 dispatches, and the
    /// value that was superseded while the lane was busy is NEVER on the wire.
    /// That non-event is the whole difference between idempotent state and an
    /// event log.
    func testABusyLaneConflatesLatestWinsTheSupersededOffsetIsNeverDispatched() throws {
        let lane = RecordingLane(withhold: true)
        let (host, scroll) = try makeHost(lane: lane)

        sample(scroll, 100) // lane free → dispatches, completion PARKED
        sample(scroll, 200) // lane busy → conflates
        sample(scroll, 300) // lane busy → REPLACES 200 in the slot

        XCTAssertEqual(lane.dispatches.count, 1,
                       "while the dispatch is in flight, NOTHING else is submitted — at most one "
                       + "per node on the lane, ever (the never-queue proof)")
        XCTAssertEqual(host.mapper.scrollPendingOffsetPt(of: Self.scrollId), 300,
                       "…the slot holds ONE offset, the freshest: 300 replaced 200")
        XCTAssertEqual(host.mapper.scrollSamplesSeen, 3,
                       "…and all three samples were SEEN — they conflated, they were not lost at "
                       + "the delegate")

        releaseOneCompletion(lane) // the lane frees → the freshest value goes out

        XCTAssertEqual(lane.dispatches.map { $0.payload },
                       [Float(100).description, Float(300).description],
                       "the completion flushed exactly the freshest value: 3 samples, 2 dispatches")
        XCTAssertFalse(lane.dispatches.contains { $0.payload == Float(200).description },
                       "offset 200pt is NEVER on the wire — it was idempotent state that a "
                       + "fresher sample superseded, not an event to be queued")

        releaseOneCompletion(lane) // 300's completion: the slot is empty
        XCTAssertEqual(lane.dispatches.count, 2, "an empty slot dispatches nothing")
        XCTAssertEqual(host.mapper.scrollBusyWireCount, 0, "quiescent")
    }

    // ── Batch guard: a mid-batch sample conflates and flushes ONCE at batch end ──

    /// The sample here is REAL and it is iOS's OWN echo path: growing the
    /// viewport (SetStyle height 200 → 700) makes the current offset (600) exceed
    /// the new range (800 − 700 = 100), and the shell's 6.2 shrink clamp
    /// (`applyScrollFrames`) writes `contentOffset` — INSIDE `calculateAndApply`,
    /// inside the batch, firing the delegate synchronously. (Android's equivalent
    /// sample comes from `ScrollView.onLayout`'s framework re-clamp — a mechanism
    /// iOS must not go looking for; the OBSERVABLE both suites pin is the same.)
    /// The wire contract's answer: the sample CONFLATES (a batch is a busy lane)
    /// and the batch end is a lane-availability — ONE dispatch, the clamped
    /// offset, after the commit. Deleting the flush leaves the clamped offset
    /// stranded in the slot forever and .NET's window desynchronized from the
    /// glass — that is the mutation this test reddens on. It pins the batch guard
    /// AND the flush TOGETHER, per the Gate 2 honesty note.
    func testAMidBatchClampSampleConflatesAndFlushesOnceAtBatchEnd() throws {
        let lane = RecordingLane()
        let (host, scroll) = try makeHost(lane: lane)

        sample(scroll, 600) // to the bottom (range 600)
        XCTAssertEqual(lane.dispatches.count, 1)

        // The batch: the viewport grows past what the offset allows. The shell's
        // clamp fires the delegate DURING applyBatch's layout pass.
        host.render([bnStyle(Self.scrollId, "height", "700")])
        drainMain()

        XCTAssertEqual(lane.dispatches.count, 2,
                       "the mid-batch sample conflated and the batch end flushed it: exactly ONE "
                       + "more dispatch (at most one per committed frame)")
        let clampedPt = scroll.contentOffset.y
        XCTAssertEqual(lane.dispatches.last?.payload, Float(clampedPt).description,
                       "…carrying the offset the shell clamped to — the content (800) minus the "
                       + "new viewport (700), the 6.2 clamp's own number")
        XCTAssertLessThan(clampedPt, 600, "sanity: the clamp really happened (600pt is no longer "
                          + "reachable)")
        XCTAssertEqual(clampedPt, 100, accuracy: 0.5, "…and it is exactly 800 − 700")
        XCTAssertEqual(host.mapper.scrollBusyWireCount, 0,
                       "…and nothing is left stranded in the slot")
    }

    // ── Detach / purge: the 6.3 stale-callback discipline ────────────────────

    func testDetachDropsThePendingOffsetItIsNeverDispatched() throws {
        let lane = RecordingLane(withhold: true)
        let (host, scroll) = try makeHost(lane: lane)

        sample(scroll, 100) // in flight, completion parked
        sample(scroll, 250) // conflated into the slot

        host.render([.detachEvent(nodeId: Self.scrollId, handlerId: Self.handler, eventName: "scroll")])
        XCTAssertEqual(host.mapper.scrollWireCount, 0, "the wire died with the detach")
        XCTAssertNil(host.mapper.scrollPendingOffsetPt(of: Self.scrollId),
                     "…taking its pending offset with it")

        releaseOneCompletion(lane) // the in-flight dispatch's completion lands LATE

        XCTAssertEqual(lane.dispatches.count, 1,
                       "the detached node's pending offset was DROPPED, never dispatched — a "
                       + "completion into a dead wire is a no-op by construction (the 6.3 "
                       + "discipline)")

        // And a sample AFTER the detach reaches nothing: the delegate slot was
        // cleared, the wire is gone — no dispatch, no crash, not even a count.
        let seenBefore = host.mapper.scrollSamplesSeen
        sample(scroll, 400)
        XCTAssertEqual(lane.dispatches.count, 1,
                       "a post-detach scroll is the node's own business — nothing dispatched")
        XCTAssertEqual(host.mapper.scrollSamplesSeen, seenBefore,
                       "…and no sample counted against a dead wire")
    }

    func testRemovingTheScrollNodesSubtreePurgesTheWirePendingAndAll() throws {
        let lane = RecordingLane(withhold: true)
        // The scroll under a WRAPPER — the shape navigation actually emits (the
        // 6.2 lesson: the RemoveNode names the page's column, never the scroll).
        let (host, scroll) = try makeHost(lane: lane, wrapperId: 100)

        sample(scroll, 100) // in flight
        sample(scroll, 250) // pending

        host.render([.removeNode(nodeId: 100)])
        XCTAssertEqual(host.mapper.scrollWireCount, 0,
                       "ONE RemoveNodePatch stands for the subtree — the wire is part of it")

        releaseOneCompletion(lane)
        XCTAssertEqual(lane.dispatches.count, 1,
                       "the purged node's pending offset died unsent — a dispatch after the purge "
                       + "would enter a handler whose node no longer exists")
        XCTAssertEqual(host.mapper.scrollBusyWireCount, 0)
    }

    // ── Re-attach: last-wins on the LIVE wire, the 4.2 watcher discipline ─────

    func testAReattachSwapsTheHandlerOnTheLiveWireLastWinsNoStacking() throws {
        let lane = RecordingLane()
        let (host, scroll) = try makeHost(lane: lane)

        sample(scroll, 100)
        XCTAssertEqual(lane.dispatches.last?.handlerId, Self.handler)

        // Same node, NEW handlerId, NO preceding detach — Blazor's last-wins
        // re-attach (a re-rendered OnScroll delegate).
        host.render([.attachEvent(nodeId: Self.scrollId, eventName: "scroll", handlerId: 99)])
        sample(scroll, 200)

        XCTAssertEqual(lane.dispatches.last?.handlerId, 99,
                       "the new sample dispatches on the NEW handler")
        XCTAssertEqual(lane.dispatches.count, 2,
                       "…and each sample dispatched exactly ONCE — the wire was REUSED, not "
                       + "stacked (the 4.2 stale-watcher lesson, applied to scroll)")
        XCTAssertEqual(host.mapper.scrollWireCount, 1, "one node, one wire")
    }

    // ── And the widget guard: scroll on a non-viewport is ignored, loudly ─────

    func testAttachingScrollToANonScrollNodeIsIgnoredAndDispatchesNothing() {
        let lane = RecordingLane()
        let host = BnSyntheticHost()
        host.mapper.onScrollEvent = { [weak lane] handlerId, payload, done in
            lane?.dispatcher(handlerId, payload, done)
        }
        host.render([
            bnCreate(1, "view", nil),
            bnStyle(1, "height", "200"),
            .attachEvent(nodeId: 1, eventName: "scroll", handlerId: Self.handler),
        ])
        XCTAssertEqual(host.mapper.scrollWireCount, 0,
                       "no wire for a node that cannot scroll")
        XCTAssertEqual(lane.dispatches.count, 0)
        XCTAssertFalse(host.root.subviews[0] is UIScrollView,
                       "sanity: the node exists and simply is not a viewport")
    }

    // ── Ordering: PRESERVED, NOT ASSUMED — pinned on the lane itself ──────────

    /// **THE WIRE CONTRACT'S ORDERING ROW, AS A FACT ABOUT THIS SHELL'S LANE.**
    ///
    /// The property the conflation leans on: `BnRuntime.dispatchLane` is ONE
    /// serial DispatchQueue, both `dispatchEvent` overloads enqueue on it (the
    /// fire-and-forget one IS the onComplete one with an empty completion), and a
    /// serial queue never reorders — so a conflated scroll dispatch can never
    /// overtake a user-input event queued before it, and the completion marks the
    /// lane free BEFORE the next queued item runs.
    ///
    /// Driven with NEGATIVE handlerIds: a negative Int32 crosses the ABI as a
    /// UInt64 beyond the handler table's int range, which `DispatchEventCore`
    /// rejects with rc 3 BEFORE any session lookup — deterministic with or
    /// without a live session elsewhere in this process. rc ≠ 0 routes to
    /// `onError` ON THE LANE THREAD, in execution order, which is what makes the
    /// order observable; and the completion fires anyway, which is its own pin (a
    /// lost completion would WEDGE the conflation slot forever).
    func testAScrollDispatchRidesTheSameFifoLaneAsQueuedInputAndItsCompletionAlwaysFires() {
        let runtime = BnRuntime(mapper: bnMapper(root: UIView()))
        let lock = NSLock()
        var events: [String] = []
        let record: (String) -> Void = { e in lock.lock(); events.append(e); lock.unlock() }
        runtime.onError = { _, err in
            if let e = err as? BnDispatchError { record(e.eventName) }
        }

        runtime.dispatchEvent(handlerId: -1, eventName: "click", payload: nil)
        runtime.dispatchEvent(handlerId: -2, eventName: "scroll", payload: "1.0",
                              onComplete: { record("scroll-complete") })
        runtime.dispatchEvent(handlerId: -3, eventName: "change", payload: "x")

        let deadline = Date().addingTimeInterval(10)
        while Date() < deadline {
            lock.lock(); let count = events.count; lock.unlock()
            if count >= 4 { break }
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.01))
        }

        lock.lock(); let snapshot = events; lock.unlock()
        XCTAssertEqual(snapshot, ["click", "scroll", "scroll-complete", "change"],
                       "ONE serial lane, FIFO: the scroll dispatch ran AFTER the click queued "
                       + "before it and BEFORE the change queued after it; its completion fired "
                       + "on the lane (despite rc ≠ 0 — it ALWAYS fires) and before the next "
                       + "queued input ran. This is the ordering row of the wire contract, "
                       + "observed rather than assumed.")
    }
}
