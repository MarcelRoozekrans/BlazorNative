// ─────────────────────────────────────────────────────────────────────────────
// BnDeepLink — the iOS URL-scheme deep link, and the parity gap it closes.
//
// Android has had `blazornative://<path>` since Phase 3.x: an intent-filter, a
// COLD parse before the `.so` loads, and a WARM re-route through `onNewIntent`.
// iOS had **no deep-link surface of any kind** — no `CFBundleURLTypes`, no
// `openURL` handler, not even the custom scheme. An app could be opened by a
// link on one platform and not the other, with nothing failing anywhere.
//
// ── WHAT THIS IS NOT ─────────────────────────────────────────────────────────
// **This is the custom URL SCHEME, not universal links.** Universal links need
// an `associated-domains` entitlement, a real domain, and an
// `apple-app-site-association` file served from it — all gated on an Apple
// Developer account, which is the same thing blocking real-device iOS. The
// scheme half needs none of that and is testable on the simulator, so it ships
// now and universal links stay ledgered rather than half-built.
//
// ── THE TWO ARRIVAL PATHS, AND WHY THEY DIFFER ───────────────────────────────
// WARM (the app is alive): dispatch the reserved `navigate` host event with the
// route. .NET owns the route table and swaps the root — the shell resolves
// nothing. This is byte-for-byte the path a notification tap already takes.
//
// COLD (launched BY the link): there is no session yet, so there is nothing to
// dispatch to. The route is STASHED and `HostViewController` reads it at boot to
// choose the initial mount by NAME, exactly as a cold notification tap does. The
// alternative — boot the default page, then navigate — would show the wrong
// screen for a frame, which is precisely what Android avoids by resolving the
// component before it loads the runtime.
//
// ── WHY iOS NEEDS NO GENERATED ROUTE MAP AND ANDROID DOES ────────────────────
// Android must answer "which component?" at Intent-parse time, BEFORE the native
// library is loaded — so it reads a build-time-generated resource (RouteGen).
// iOS has no such ordering constraint for the WARM path: the session is already
// up, so `navigate` carries the route string and .NET resolves it. Only the COLD
// path needs a shell-side answer, and only to pick a mount name. That is what
// [routeComponents] is for, and it is why this file has a small hand-written
// mirror rather than a generator.
// ─────────────────────────────────────────────────────────────────────────────

import Foundation

/// The iOS deep-link surface.
final class BnDeepLink {

    /// One instance, because three unrelated places need the SAME one and none of
    /// them can be handed it: `AppDelegate` receives the opened URL and holds no
    /// runtime, `HostViewController` reads the cold stash at boot, and `BnRuntime`
    /// wires the warm dispatcher once a session exists. There is exactly one app.
    static let shared = BnDeepLink()

    /// The scheme registered in Info.plist's `CFBundleURLTypes`. Kept as a
    /// constant so the plist and the parser cannot disagree silently — the test
    /// asserts the plist actually declares this exact string.
    static let scheme = "blazornative"

    /// The route→mount-component mirror, MOVED HERE from `BnNotifications`
    /// (Phase 9.1 put it there because notifications needed it first; it was
    /// never a notifications concept). It answers the one question the COLD path
    /// has to answer shell-side: which component does this route mount?
    ///
    /// Sample-only, and hand-written on purpose: there is no iOS template tree,
    /// so unlike Android's generated map this has no consumer app to derive it
    /// for. That is a recorded gap, not a regression.
    static let routeComponents: [String: String] = [
        "/notifications": "BnNotificationsDemo",
        "/geolocation": "BnGeolocationDemo",
    ]

    private let lock = NSLock()
    private var pendingLaunchRoute: String?

    /// Wired by `BnRuntime` at boot to dispatch the WARM `navigate` host event on
    /// the serial lane — the ABI must not be called from UIKit's arbitrary thread
    /// directly. Its being non-nil is ALSO the "a live session exists" signal that
    /// chooses warm re-route over cold stash, exactly as `BnNotifications` does.
    var navigateDispatcher: ((String) -> Int32)?

    // ── Parsing ──────────────────────────────────────────────────────────────

    /// `blazornative://about` → `/about`; `blazornative://` → `/`.
    ///
    /// Returns nil for any other scheme, which is not paranoia: iOS will hand a
    /// delegate URLs it never registered for (a document type, a share
    /// extension), and treating one of those as a route would navigate the app
    /// somewhere the user did not ask for.
    ///
    /// The HOST is the first path segment because `blazornative://about` parses
    /// with host `about` and an empty path — the shape a person actually types.
    /// A deeper link (`blazornative://settings/audio`) appends the rest.
    static func route(from url: URL) -> String? {
        guard url.scheme?.lowercased() == scheme else { return nil }

        let host = url.host ?? ""
        let path = url.path                       // already begins with "/" when non-empty
        if host.isEmpty && path.isEmpty { return "/" }

        let joined = host.isEmpty ? path : "/" + host + path
        // Trim a trailing slash so `blazornative://about/` and `blazornative://about`
        // are the same route — .NET's table is an exact-string lookup, so two
        // spellings of one route would be a silent miss.
        if joined.count > 1 && joined.hasSuffix("/") { return String(joined.dropLast()) }
        return joined
    }

    // ── Arrival ──────────────────────────────────────────────────────────────

    /// Handles an opened URL. Returns true if it was a route we own — which is
    /// what `application(_:open:options:)` must return, so an unrecognised URL is
    /// reported unhandled rather than silently swallowed.
    @discardableResult
    func handle(url: URL) -> Bool {
        guard let route = Self.route(from: url) else {
            BnLog.warn("BnDeepLink", "ignoring a URL that is not a \(Self.scheme):// route", privacy: .safe)
            return false
        }

        if let dispatcher = navigateDispatcher {
            // WARM: a live session. .NET owns the route table.
            BnLog.info("BnDeepLink", "[deep-link] warm route → \(route)")   // route = app data → redacted (the default)
            _ = dispatcher(route)
        } else {
            // COLD: launched by the link. Seed the mount instead.
            BnLog.info("BnDeepLink", "[deep-link] startup route → \(route)")   // route = app data → redacted (the default)
            lock.lock(); pendingLaunchRoute = route; lock.unlock()
        }
        return true
    }

    /// The stashed cold-launch route, or nil.
    func pendingLaunchRouteForTest() -> String? {
        lock.lock(); defer { lock.unlock() }
        return pendingLaunchRoute
    }

    /// The COLD-launch mount name the stashed route resolves to. nil when no link
    /// launched the app, or when the route has no component of its own — in which
    /// case the caller falls back to the default mount and .NET can still navigate
    /// there afterwards. Read at boot by `HostViewController`.
    func resolvedLaunchComponent() -> String? {
        guard let route = pendingLaunchRouteForTest() else { return nil }
        return Self.routeComponents[route]
    }

    /// Test seam: drops any stashed route (the hosted bundle is one process, so a
    /// stash would otherwise leak between tests).
    func clearForTest() {
        lock.lock(); pendingLaunchRoute = nil; lock.unlock()
    }
}
