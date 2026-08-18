// ─────────────────────────────────────────────────────────────────────────────
// BnDeepLinkTests + BnAppLifecycleTests — the iOS parity gaps, pinned.
//
// Both features were absent entirely, not broken: iOS had no URL scheme and
// dispatched no lifecycle events, while Android had both and pinned them on
// device. So these tests are mostly about the two things that make an ABSENT
// feature look present — a handler nothing can reach, and a mapping that is
// plausible but wrong.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
@testable import BnHost

final class BnDeepLinkTests: XCTestCase {

    override func tearDown() {
        BnDeepLink.shared.clearForTest()
        BnDeepLink.shared.navigateDispatcher = nil
        super.tearDown()
    }

    // ── The registration, without which the handler is dead code ─────────────

    /// THE PIN THAT MATTERS MOST, and the one a code-only test would miss: iOS
    /// never delivers a URL for a scheme the bundle does not declare, so
    /// `application(_:open:)` would simply never be called and every other test
    /// here would still pass. This reads the REAL bundle the app runs from.
    func testTheBundleActuallyDeclaresTheScheme() {
        let types = Bundle(for: HostViewController.self)
            .object(forInfoDictionaryKey: "CFBundleURLTypes") as? [[String: Any]]

        let schemes = (types ?? []).flatMap { $0["CFBundleURLSchemes"] as? [String] ?? [] }

        XCTAssertTrue(schemes.contains(BnDeepLink.scheme),
                      "Info.plist must declare CFBundleURLTypes with the '\(BnDeepLink.scheme)' "
                      + "scheme. Without it iOS never delivers the URL, AppDelegate's open-URL "
                      + "handler is unreachable, and the deep link silently does nothing — which "
                      + "is exactly the state this feature existed to leave.")
    }

    // ── Parsing ──────────────────────────────────────────────────────────────

    func testHostIsTheFirstPathSegment() {
        // `blazornative://about` parses with host "about" and an EMPTY path, which
        // is the shape a person actually types — reading only `url.path` yields "".
        XCTAssertEqual(BnDeepLink.route(from: URL(string: "blazornative://about")!), "/about")
        XCTAssertEqual(BnDeepLink.route(from: URL(string: "blazornative://geolocation")!), "/geolocation")
    }

    func testDeeperPathsAppend() {
        XCTAssertEqual(BnDeepLink.route(from: URL(string: "blazornative://settings/audio")!),
                       "/settings/audio")
    }

    func testBareSchemeIsTheRoot() {
        XCTAssertEqual(BnDeepLink.route(from: URL(string: "blazornative://")!), "/")
    }

    func testATrailingSlashIsTheSameRoute() {
        // .NET's route table is an exact-string lookup, so two spellings of one
        // route would be a silent miss on one of them.
        XCTAssertEqual(BnDeepLink.route(from: URL(string: "blazornative://about/")!), "/about")
    }

    func testAForeignSchemeIsNotARoute() {
        // iOS hands a delegate URLs it never registered for. Treating one as a
        // route would navigate the app somewhere the user did not ask for.
        XCTAssertNil(BnDeepLink.route(from: URL(string: "https://example.com/about")!))
        XCTAssertNil(BnDeepLink.route(from: URL(string: "file:///tmp/x")!))
    }

    func testSchemeMatchingIsCaseInsensitive() {
        // URL schemes are case-insensitive per RFC 3986, and iOS will hand over
        // whatever the caller typed.
        XCTAssertEqual(BnDeepLink.route(from: URL(string: "BlazorNative://about")!), "/about")
    }

    // ── Warm vs cold ─────────────────────────────────────────────────────────

    func testAWarmLinkDispatchesAndStashesNothing() {
        // Wire the REAL dispatcher rather than a bypass hook: a non-nil dispatcher
        // is precisely what "a live session exists" means to this type, so this
        // exercises the actual warm branch.
        var dispatched: [String] = []
        BnDeepLink.shared.navigateDispatcher = { dispatched.append($0); return 0 }

        XCTAssertTrue(BnDeepLink.shared.handle(url: URL(string: "blazornative://geolocation")!))

        XCTAssertEqual(dispatched, ["/geolocation"])
        XCTAssertNil(BnDeepLink.shared.pendingLaunchRouteForTest(),
                     "a live session re-routes; nothing should be waiting for a boot that already happened")
    }

    func testAColdLinkStashesForTheMountSeed() {
        // No dispatcher wired == no live session, which is the real cold-launch
        // state: AppDelegate sees the URL before HostViewController boots.
        XCTAssertTrue(BnDeepLink.shared.handle(url: URL(string: "blazornative://notifications")!))

        XCTAssertEqual(BnDeepLink.shared.pendingLaunchRouteForTest(), "/notifications")
        XCTAssertEqual(BnDeepLink.shared.resolvedLaunchComponent(), "BnNotificationsDemo",
                       "the stashed route must resolve to a mount NAME — that is how the cold path "
                       + "boots the right page instead of the default one")
    }

    func testAColdLinkWithNoComponentFallsBackRatherThanFailing() {
        XCTAssertTrue(BnDeepLink.shared.handle(url: URL(string: "blazornative://not-a-demo")!))

        XCTAssertEqual(BnDeepLink.shared.pendingLaunchRouteForTest(), "/not-a-demo")
        XCTAssertNil(BnDeepLink.shared.resolvedLaunchComponent(),
                     "an unmapped route mounts the DEFAULT and lets .NET navigate afterwards; it "
                     + "must not resolve to a wrong component or refuse to launch")
    }

    func testAForeignURLIsReportedUnhandledAndChangesNothing() {
        XCTAssertFalse(BnDeepLink.shared.handle(url: URL(string: "https://example.com/x")!))
        XCTAssertNil(BnDeepLink.shared.pendingLaunchRouteForTest())
    }

    /// The map is shared with notifications rather than copied — a second copy is
    /// how the two launch surfaces would come to disagree about where a route goes.
    func testTheRouteMapHasOneSource() {
        XCTAssertEqual(BnNotifications.deepLinkComponents, BnDeepLink.routeComponents)
    }
}

final class BnAppLifecycleTests: XCTestCase {

    override func tearDown() {
        BnAppLifecycle.sinkForTest = nil
        super.tearDown()
    }

    /// The names are the WIRE CONTRACT, shared with Android — an app subscribing to
    /// `NativeEvents` branches on these strings, so a well-meaning iOS-flavoured
    /// rename ("didBecomeActive") would break cross-platform code that reads
    /// correctly on both.
    func testTheEventNamesAreAndroidsExactly() {
        XCTAssertEqual(BnAppLifecycle.onResume, "onResume")
        XCTAssertEqual(BnAppLifecycle.onPause, "onPause")
        XCTAssertEqual(BnAppLifecycle.onDestroy, "onDestroy")
    }

    func testDispatchReachesTheSink() {
        var seen: [String] = []
        BnAppLifecycle.sinkForTest = { seen.append($0) }

        BnAppLifecycle.dispatch(BnAppLifecycle.onResume)
        BnAppLifecycle.dispatch(BnAppLifecycle.onPause)

        XCTAssertEqual(seen, ["onResume", "onPause"])
    }

    /// THE GUARD, and it is not defensive coding: UIKit delivers
    /// `didBecomeActive` during launch — before HostViewController has booted the
    /// runtime — and under XCTest it never boots at all. Without the nil check
    /// that first callback would call the ABI into a session that does not exist.
    /// This test runs in exactly that state, so reaching the end IS the assertion.
    func testDispatchIsANoOpWithNoLiveSession() {
        BnAppLifecycle.sinkForTest = nil
        XCTAssertNil(BnRuntime.current,
                     "the app stays inert under XCTest — the test bundle owns the native session")

        BnAppLifecycle.dispatch(BnAppLifecycle.onResume)   // must not trap
    }
}
