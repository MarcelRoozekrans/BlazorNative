// ─────────────────────────────────────────────────────────────────────────────
// BnImageFixtureServer — Phase 6.3 Gate 3 Task 3.3: **THE IN-PROCESS LOOPBACK
// FIXTURE SERVER.** The Swift twin of `ImageFixtureServer.kt`, contract for
// contract: the same three paths, the same four pixel sizes, a REAL 404, and a
// held `/slow.png` so a request can be observed IN FLIGHT.
//
// 6.3 non-negotiable #5: **CI never touches the public internet.** A suite whose
// green depends on a remote host is not a suite. So the three sources
// `BnImageDemo` names point at a server the TEST TARGET stands up in the app's own
// process, and the failing case is a path that server genuinely **404s** — a real
// HTTP fetch through Kingfisher, a real failure, deterministic and offline.
//
// ── AND `127.0.0.1` IS NOT WHAT IT IS ON THE AVD ─────────────────────────────
// On the emulator, `127.0.0.1` is the emulated DEVICE's own loopback — a separate
// network stack. **In the iOS simulator it is the HOST MAC's**: the simulator is a
// process on macOS and shares the host's network stack. The offline guarantee
// holds either way. Two things do not:
//
//  1. **PORT 8099 IS HOST-GLOBAL ON THE CI RUNNER.** Any other process on 8099 — a
//     leaked server from an earlier run, another job on a shared runner — would
//     serve the fixtures INSTEAD of us. So this server BINDS EXCLUSIVELY and FAILS
//     LOUDLY, naming the port; never "someone is already listening, good enough".
//     A foreign server on 8099 is strictly WORSE than none: a missing one refuses
//     everything (loud), while a foreign one can answer `/missing.png` with a 200 —
//     and case [2] of `/image` asserts a FAILURE, so it would pass SILENTLY on a
//     page that loaded someone else's images. The fixture-contract assertions in
//     the tests are the second half of that probe: a foreign server cannot serve an
//     image whose natural size is the one we assert.
//  2. **ATS applies.** Kingfisher fetches through `URLSession`, so App Transport
//     Security governs `http://127.0.0.1:8099`. `BnHost/Info.plist` carries
//     `NSAllowsLocalNetworking` for it, and [fetch] is what turns that from an
//     assumption into a CHECKED fact (`BnImageDemoTests.testCleartext…`): it pulls
//     a fixture over the very same cleartext loopback, through `URLSession`, so a
//     block surfaces as the named error it is — instead of as this phase's most
//     dangerous symptom, in which a blocked load is INDISTINGUISHABLE from the 404
//     that case [2] expects and two of three demo assertions stay green on a device
//     that loaded nothing.
//
// ── THE GATE ────────────────────────────────────────────────────────────────
// Every response is held until [release]. That is what makes `BnImageDemo`'s
// "before the bytes" frame table observable at all: without it the loopback fetch
// wins the race against the test's first look at the tree, and the BEFORE table is
// asserted on a page that has already reflowed.
//
// ── THE CLOSE IS STRUCTURAL: `started(for:)` IS THE ONLY CONSTRUCTOR ─────────
// A server that is never closed is IMMORTAL — the accept thread holds a strong `self`
// and exits only when `close()` sets `closed`, so a missed `close()` leaks the object,
// spins the thread forever and KEEPS :8099 BOUND. No `deinit` backstop can exist (that
// same thread is what prevents `deinit` from ever running). And the symptom lands on a
// LATER class, as a `portTaken` whose message blames a foreign process that is not
// there. So `init` is private and [started] registers `addTeardownBlock { close() }` —
// which runs however the test ends, including a `setUp` that threw half-way.
//
// ── TEARDOWN JOINS, AND IT HAS TO: THE NEXT `setUp` BINDS THE SAME PORT ──────
// Gate 2 ate this one. Closing the listening socket signals the thread blocked in
// `accept()` — but it **returns before that thread has unwound**, and until it has,
// the listener can still hold :8099. The next test's `setUp` binds within
// milliseconds and gets `EADDRINUSE` **from our own previous instance** — a
// self-inflicted failure of the one condition the bind message correctly refuses to
// retry around.
//
// So the accept loop **POLLS** (`poll(2)`, 100ms) instead of blocking in `accept`,
// [close] flips the flag and WAITS for the loop to signal that it is out, and only
// THEN closes the descriptor. When [close] returns, nothing of this server is
// listening — by construction, not by hope.
//
// The per-connection SERVE threads are deliberately NOT joined: they hold ACCEPTED
// sockets (local port 8099, but `ESTABLISHED`/`TIME_WAIT`, never `LISTEN`), and
// `SO_REUSEADDR` — set on the listener — is precisely the option that lets a fresh
// listener bind over those. **`SO_REUSEADDR` is NOT `SO_REUSEPORT`**: it does not
// let a second process LISTEN on a port someone is already listening on (that still
// fails, which is the loud failure this server owes); it only permits re-binding
// over the connections our own previous test left behind.
// ─────────────────────────────────────────────────────────────────────────────

import Foundation
import Darwin
import UIKit
import XCTest

enum BnFixtureServerError: Error, CustomStringConvertible {
    case portTaken(port: UInt16, errno: Int32)
    case socketFailed(String, errno: Int32)

    var description: String {
        switch self {
        case .portTaken(let port, let err):
            return "the image fixture server could not bind 127.0.0.1:\(port) — something else "
                + "is already listening on it (a leaked server from an earlier test class, or "
                + "ANOTHER PROCESS ON THE macOS RUNNER: the simulator shares the host's network "
                + "stack, so this port is HOST-GLOBAL). THIS IS NOT FLAKE AND MUST NOT BE RETRIED "
                + "AROUND: a FOREIGN server on \(port) is worse than none, because it would serve "
                + "foreign bytes to /fixed.png and /intrinsic.png and could answer /missing.png "
                + "with a 200 — and case [2] of /image asserts a FAILURE, so it would pass "
                + "SILENTLY on a page that loaded someone else's images. Find the listener and "
                + "kill it. (errno \(err))"
        case .socketFailed(let what, let err):
            return "the image fixture server could not \(what) (errno \(err))"
        }
    }
}

final class BnImageFixtureServer {

    // ── The origin and the three paths BnImageDemo.razor DECLARES ────────────
    //
    // (`FixtureOrigin`, `FixedSrc`, `IntrinsicSrc`, `FailingSrc`; `BnScrollDemo.RowImageSrc`
    // reuses `FixedSrc`.) They are Swift constants because a device-side test cannot read a
    // `.razor` file — so the drift pin is asserted on the **WIRE** instead, which is stronger
    // than a transcription check anyway: `BnImageDemoTests` asserts the outcomes against the
    // URLs the RENDERER actually put on the `UpdateProp` wire, and they must be exactly the
    // three this server routes. Change a URL in `BnImageDemo.razor` and that assertion reddens
    // by name, rather than the page quietly 404ing three times.
    static let ORIGIN = "http://127.0.0.1:8099"
    static let FIXED_URL = ORIGIN + "/fixed.png"
    static let INTRINSIC_URL = ORIGIN + "/intrinsic.png"
    static let MISSING_URL = ORIGIN + "/missing.png"

    /// Test-only, on no wire: a path whose response is held until [releaseSlow], so a request
    /// can be observed **in flight** — which is the only way to prove a cancellation cancelled
    /// anything.
    static let SLOW_URL = ORIGIN + "/slow.png"

    private static let PORT: UInt16 = 8099

    // ── THE FIXTURE CONTRACT — and these four numbers are NOT this file's to choose ──
    //
    // **`BnImageDemo.razor` owns them** — `IntrinsicNaturalWidthPx` / `…HeightPx` /
    // `FixedNaturalWidthPx` / `…HeightPx` — because **both shells must assert the SAME
    // numbers**, and the `.razor` is the one file both gates read. Nothing else enforces the
    // phase's own verification bar #1 ("the same frames on both devices"): each shell is
    // internally consistent, and no test compares the two tables.
    //
    // The transcription is **pinned by a drift test** — `BnImageDemoTests
    // .TheIosFixtureServer_ServesExactlyBnImageDemosNaturalPixelSizes` (the .NET
    // `build-test` lane, next to its Android twin) parses these four `static let`s out of
    // THIS file and asserts equality with the `.razor`. Keep them as plain
    // `static let NAME = <int>` declarations at the start of their line: that is what the
    // parser looks for, and a declaration it cannot find fails it LOUDLY.
    //
    //  - `Wi = 160 ≤ 300` — a section is 300 wide, so the measure func is called with
    //    AT_MOST(300); a wider fixture would ask a clamping question this phase deliberately
    //    does not answer (no `ContentMode` — design decision 3).
    //  - `Hi = 90 > 0`, comfortably: **Hi IS the reflow.** A 0-high fixture would make the
    //    reflow assertion vacuously true.
    //  - `(64, 48) ≠ (200, 120)` — the FIXED case's declared size. Otherwise "it measures
    //    200 × 120" is a coincidence, not a proof that a declared size short-circuits
    //    measurement. It is also ≠ (40, 40), `BnScrollDemo`'s row image, which buys the same
    //    proof inside the scroll.
    static let INTRINSIC_W = 160
    static let INTRINSIC_H = 90
    static let FIXED_W = 64
    static let FIXED_H = 48

    /// **ONE PIXEL OF THE FILE IS ONE dp/pt** — the parity contract's UNIT row, and the rule
    /// that says these `Int`s of PIXELS are also the `CGFloat`s of POINTS Yoga computes with.
    /// It is what `UIImage(data:).size` gives iOS for free (`scale == 1`) — and what
    /// `BnImageLoader.naturalPixelSize` reads directly off the pixel buffer so that no option
    /// can take it away. Android must WORK for the same number (`bitmap.width`), or the two
    /// shells cannot compute the same frame.
    static let INTRINSIC_W_PT = CGFloat(INTRINSIC_W)
    static let INTRINSIC_H_PT = CGFloat(INTRINSIC_H)
    static let FIXED_W_PT = CGFloat(FIXED_W)
    static let FIXED_H_PT = CGFloat(FIXED_H)

    // ── The bytes ────────────────────────────────────────────────────────────

    /// [1]'s bytes — and the ones whose natural size IS the reflow.
    let intrinsicPng: Data = BnImageFixtureServer.png(INTRINSIC_W, INTRINSIC_H,
                                                      UIColor(red: 0.13, green: 0.59, blue: 0.95, alpha: 1))

    /// [0]'s bytes (and `BnScrollDemo`'s row image's) — natural size ≠ its declared one.
    let fixedPng: Data = BnImageFixtureServer.png(FIXED_W, FIXED_H,
                                                  UIColor(red: 1.0, green: 0.60, blue: 0.0, alpha: 1))

    /// A PNG of **exactly** [w] × [h] PIXELS. `format.scale = 1` is the whole of it: the
    /// renderer's default scale is the SCREEN's, which on a 3× simulator would emit a
    /// 480 × 270 file for a 160 × 90 request — and every frame in this phase's tables would
    /// then be three times the number Android asserts, with each suite internally consistent
    /// and neither able to see it.
    private static func png(_ w: Int, _ h: Int, _ color: UIColor) -> Data {
        let format = UIGraphicsImageRendererFormat()
        format.scale = 1
        format.opaque = true
        let renderer = UIGraphicsImageRenderer(size: CGSize(width: w, height: h), format: format)
        return renderer.pngData { ctx in
            color.setFill()
            ctx.fill(CGRect(x: 0, y: 0, width: w, height: h))
        }
    }

    /// The fixture as the wire actually carries it: **decoded from the bytes this server
    /// serves**, not from the image they were encoded from. That round trip is the point —
    /// it is the same decode Kingfisher performs, so a fixture that did not survive PNG
    /// encoding at its stated size is caught here rather than mis-attributed to the shell.
    func decoded(_ bytes: Data,
                 file: StaticString = #filePath, line: UInt = #line) throws -> UIImage {
        try XCTUnwrap(UIImage(data: bytes),
                      "the fixture PNG did not decode — the fixture, not the shell, is broken",
                      file: file, line: line)
    }

    // ── The cleartext probe ──────────────────────────────────────────────────

    /// A plain-HTTP GET through `URLSession` — **the cleartext probe**, and it goes through
    /// the very stack Kingfisher uses, so App Transport Security governs it identically.
    /// Returns the status code, the body, and any transport error. An ATS block surfaces
    /// here as the error it is, NAMING ITSELF, rather than as a Kingfisher failure that is
    /// indistinguishable from a 404.
    func fetch(_ urlString: String, timeout: TimeInterval = 10) -> (status: Int, body: Data, error: Error?) {
        guard let url = URL(string: urlString) else { return (-1, Data(), nil) }
        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = timeout
        config.requestCachePolicy = .reloadIgnoringLocalAndRemoteCacheData
        let session = URLSession(configuration: config)

        var status = -1
        var body = Data()
        var failure: Error?
        let done = DispatchSemaphore(value: 0)
        session.dataTask(with: url) { data, response, error in
            status = (response as? HTTPURLResponse)?.statusCode ?? -1
            body = data ?? Data()
            failure = error
            done.signal()
        }.resume()
        // The completion lands on URLSession's own delegate queue, never on main — so waiting
        // here cannot deadlock the test's runloop.
        _ = done.wait(timeout: .now() + timeout + 5)
        return (status, body, failure)
    }

    // ── Observability ────────────────────────────────────────────────────────

    private let lock = NSLock()
    private var requestedPaths: [String] = []
    private var serverErrors: [String] = []

    /// **THE SERVER'S OWN FAILURES** — path + reason, in order.
    ///
    /// [serve] swallows a failed write, and the SCOPING of that swallow is right: when the
    /// shell CANCELS a request, `URLSession` drops the connection and this side's next write
    /// is a broken pipe. That is cancellation observed from the other end of the socket — the
    /// very thing the cancellation tests ask for. (`SO_NOSIGPIPE` is set on every accepted
    /// socket so it is an `EPIPE` return rather than a **signal that kills the whole test
    /// host**.)
    ///
    /// But silent and unconditional, it would hide a REAL server bug in the classes that
    /// **cancel nothing** (`BnImageDemoTests`, `BnScrollDemoImageTests`) — there is no client
    /// there that could drop a connection, so a write failure is a bug that produced no signal
    /// at all, and the test would merely time out in its synchronization gate and blame the
    /// gate. So every one is RECORDED, and the classes that cancel nothing **assert this list
    /// is empty**. The classes that DO cancel expect entries and do not assert it.
    var errors: [String] {
        lock.lock(); defer { lock.unlock() }
        return serverErrors
    }

    /// Blocks until [path] has actually REACHED this server. It is what makes "cancelled **in
    /// flight**" an honest claim: a request cancelled before the socket was ever opened would
    /// report a cancellation too, and would prove nothing about cancellation.
    func awaitPath(_ path: String, timeout: TimeInterval = 15) -> Bool {
        let deadline = Date().addingTimeInterval(timeout)
        while Date() < deadline {
            lock.lock()
            let seen = requestedPaths.contains(path)
            lock.unlock()
            if seen { return true }
            // Pump the MAIN runloop: the caller is the test thread, and the request it is
            // waiting on was issued by a mapper batch that has to drain off this very queue.
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.02))
        }
        return false
    }

    // ── The gates ────────────────────────────────────────────────────────────

    /// A broadcast latch (Kotlin's `CountDownLatch(1)`): EVERY held responder wakes, not one.
    /// A `DispatchSemaphore` would wake exactly one, and the three demo requests are held at
    /// the same time.
    private final class Gate {
        private let condition = NSCondition()
        private var isOpen = false

        func release() {
            condition.lock()
            isOpen = true
            condition.broadcast()
            condition.unlock()
        }

        /// Deliberately NOT named `await` — that is a contextual keyword in Swift 5.5+ and
        /// `gate.await(30)` is exactly the shape the concurrency parser wants to claim.
        @discardableResult
        func waitUntilOpen(_ timeout: TimeInterval) -> Bool {
            condition.lock()
            defer { condition.unlock() }
            let deadline = Date().addingTimeInterval(timeout)
            while !isOpen {
                if !condition.wait(until: deadline) { return false }
            }
            return true
        }
    }

    private let gate = Gate()
    private let slowGate = Gate()

    /// Opens the gate: every held response — and every later one — goes out.
    func release() { gate.release() }

    /// Opens the SLOW path's gate (the cancellation tests' "now complete it").
    func releaseSlow() { slowGate.release() }

    // ── The socket ───────────────────────────────────────────────────────────

    private let listenFd: Int32
    private var closed = false
    private let acceptFinished = DispatchSemaphore(value: 0)

    /// **THE ONLY WAY THIS TARGET BUILDS A SERVER — AND THE CLOSE IS STRUCTURAL.**
    ///
    /// [init] is `private` precisely so it is. A server that is never closed is **IMMORTAL**:
    /// [acceptLoop] holds a strong `self` for its whole life and exits only when [close] flips
    /// `closed`, so the object leaks, the thread spins, and `listenFd` **keeps :8099 bound** —
    /// and a `deinit` backstop cannot help, because that thread is exactly what stops `deinit`
    /// ever running. Every later class then dies in `portTaken`, whose message points at a
    /// FOREIGN process that does not exist.
    ///
    /// `addTeardownBlock` and not `defer`: a `defer` in `setUp` closes it before the test body
    /// has run, and a `defer` in the test body is one `throw` away from being skipped. A
    /// teardown block runs after the test method **however it ended** — including a `setUp` that
    /// threw half-way through, and including an `XCTUnwrap` that threw mid-test.
    static func started(for testCase: XCTestCase) throws -> BnImageFixtureServer {
        let server = try BnImageFixtureServer()
        testCase.addTeardownBlock { server.close() }
        return server
    }

    private init() throws {
        let fd = Darwin.socket(AF_INET, SOCK_STREAM, 0)
        guard fd >= 0 else { throw BnFixtureServerError.socketFailed("open a socket", errno: errno) }

        // `SO_REUSEADDR` — see the file header. It does NOT weaken the exclusivity: a second
        // LISTEN on a port someone is listening on still fails (which is the loud failure this
        // server owes). It only permits re-binding over the TIME_WAIT connections our own
        // previous test class left behind — and without it, the second test class to want the
        // port dies for a reason that has nothing to do with anyone taking it.
        var yes: Int32 = 1
        _ = Darwin.setsockopt(fd, SOL_SOCKET, SO_REUSEADDR, &yes,
                              socklen_t(MemoryLayout<Int32>.size))

        var addr = sockaddr_in()
        addr.sin_len = UInt8(MemoryLayout<sockaddr_in>.size)
        addr.sin_family = sa_family_t(AF_INET)
        addr.sin_port = Self.PORT.bigEndian
        addr.sin_addr = in_addr(s_addr: inet_addr("127.0.0.1"))

        let bound = withUnsafePointer(to: &addr) { raw -> Int32 in
            raw.withMemoryRebound(to: sockaddr.self, capacity: 1) { sa in
                Darwin.bind(fd, sa, socklen_t(MemoryLayout<sockaddr_in>.size))
            }
        }
        guard bound == 0 else {
            let err = errno
            Darwin.close(fd)
            // FAIL LOUDLY, NAMING THE PORT — never fall back to "someone is already listening,
            // good enough". See BnFixtureServerError.portTaken for why a foreign server here is
            // worse than no server at all.
            throw BnFixtureServerError.portTaken(port: Self.PORT, errno: err)
        }
        // A generous backlog: the bounded-ring test opens 70 connections in a burst, and a
        // refused one would look exactly like a load failure — which is a legal outcome in
        // three of this suite's tests, so it would not even be loud.
        guard Darwin.listen(fd, 64) == 0 else {
            let err = errno
            Darwin.close(fd)
            throw BnFixtureServerError.socketFailed("listen on 127.0.0.1:\(Self.PORT)", errno: err)
        }
        listenFd = fd

        Thread.detachNewThread { [weak self] in self?.acceptLoop() }
    }

    /// **POLLS rather than blocking in `accept`** — see the file header's teardown note. A
    /// thread blocked in `accept(2)` does not reliably unwind when the descriptor is closed
    /// under it, and until it does, the listener can still hold :8099 — which the NEXT test's
    /// bind then reports as `EADDRINUSE`, a self-inflicted version of the one condition this
    /// server refuses to retry around.
    private func acceptLoop() {
        while !isClosed() {
            var pfd = pollfd(fd: listenFd, events: Int16(POLLIN), revents: 0)
            let ready = Darwin.poll(&pfd, 1, 100) // ms
            if ready <= 0 { continue }
            let client = Darwin.accept(listenFd, nil, nil)
            if client < 0 { continue }

            // **SO_NOSIGPIPE, AND IT IS NOT OPTIONAL.** A cancelled request means the client
            // drops the connection; this side's next `write` would then raise SIGPIPE, whose
            // default disposition **kills the test host process** — turning the cancellation
            // tests into a crash instead of a result. With it, the write returns EPIPE and
            // [serve] records it.
            var yes: Int32 = 1
            _ = Darwin.setsockopt(client, SOL_SOCKET, SO_NOSIGPIPE, &yes,
                                  socklen_t(MemoryLayout<Int32>.size))

            // A thread PER CONNECTION: the three demo requests are held on [gate] at the same
            // time, so a single-threaded responder would serialise them and the third would
            // never even reach the gate.
            Thread.detachNewThread { [weak self] in self?.serve(client) }
        }
        acceptFinished.signal()
    }

    private func serve(_ fd: Int32) {
        defer { Darwin.close(fd) }
        guard let path = readRequestPath(fd) else {
            record(error: "<no request line read>: the client sent nothing")
            return
        }

        lock.lock()
        requestedPaths.append(path) // BEFORE the gate — see [awaitPath]
        lock.unlock()

        // THE GATE — every response, including the 404, waits here. A 404 that answered
        // immediately would let the test read the BEFORE table while the failure had already
        // landed, and "the failure reserved nothing" would be asserted against a request that
        // had not happened.
        //
        // **AND ITS VERDICT IS NOT DISCARDED.** A gate that TIMED OUT means the test never
        // released it, and serving the bytes 30 seconds late is the worst of both worlds: the
        // synchronization gate has long since failed, and the response arrives anyway. It is a
        // server-side fact and it is RECORDED as one — the classes that cancel nothing assert
        // this list is empty, so it surfaces there by name instead of as a mystery timeout.
        let gateForPath = (path == "/slow.png") ? slowGate : gate
        if !gateForPath.waitUntilOpen(30) {
            record(error: "\(path): the response gate was NEVER OPENED (30s). The test did not "
                   + "call release()/releaseSlow(), so this response is being served 30 seconds "
                   + "late — long after the test's synchronization gate gave up on it.")
        }

        let status: Int
        let reason: String
        let contentType: String
        let body: Data
        switch path {
        case "/fixed.png":
            (status, reason, contentType, body) = (200, "OK", "image/png", fixedPng)
        case "/intrinsic.png", "/slow.png":
            (status, reason, contentType, body) = (200, "OK", "image/png", intrinsicPng)
        default:
            // THE FAILING CASE — a REAL 404 from a REAL server, offline and deterministic.
            // Not a dropped connection, not a timeout: the failure the contract's failure row
            // is written about.
            (status, reason, contentType, body) =
                (404, "Not Found", "text/plain", Data("no such fixture: \(path)".utf8))
        }

        var head = "HTTP/1.1 \(status) \(reason)\r\n"
        head += "Content-Type: \(contentType)\r\n"
        head += "Content-Length: \(body.count)\r\n"
        // No keep-alive: one request per connection keeps the responder trivial.
        head += "Connection: close\r\n"
        head += "Cache-Control: no-store\r\n"
        head += "\r\n"

        // **THE `errno` IS CAPTURED INSIDE [writeAll], WHERE IT IS STILL TRUE.** `errno` is a
        // per-thread global that ANY intervening libc call may overwrite, and reading it out
        // here — after `writeAll` has returned through a Swift call boundary — was reporting a
        // number with no relationship to the failure it names.
        if let failure = writeAll(fd, Data(head.utf8)) ?? writeAll(fd, body) {
            // The client went away (a CANCELLATION — see [errors]), or the server broke. This
            // side cannot tell the two apart; the TEST can, because only some tests cancel.
            record(error: "\(path): the write failed (errno \(failure)) — the client dropped the "
                   + "connection. That IS cancellation, seen from this end of the socket… in a "
                   + "test that cancels.")
        }
    }

    /// Reads the request head and returns the PATH. Reads until the blank line rather than
    /// assuming the whole head arrives in one segment (it virtually always does on loopback,
    /// and "virtually always" is what flake is made of).
    private func readRequestPath(_ fd: Int32) -> String? {
        var buffer = Data()
        var chunk = [UInt8](repeating: 0, count: 1024)
        let terminator = Data("\r\n\r\n".utf8)
        while buffer.range(of: terminator) == nil && buffer.count < 64 * 1024 {
            let n = Darwin.read(fd, &chunk, chunk.count)
            if n <= 0 { break }
            buffer.append(contentsOf: chunk[0..<n])
        }
        guard let text = String(data: buffer, encoding: .utf8),
              let requestLine = text.components(separatedBy: "\r\n").first
        else { return nil }
        let parts = requestLine.split(separator: " ")
        guard parts.count >= 2 else { return nil }
        return String(parts[1])
    }

    /// Writes every byte. Returns `nil` on success, or **the `errno` READ IMMEDIATELY AFTER THE
    /// FAILING `write(2)`** — the only place it is still the failure's own. (A cancelled request
    /// closes the socket under us, so this is normally `EPIPE`; `SO_NOSIGPIPE` is what makes it
    /// a return value rather than a signal that kills the test host.)
    private func writeAll(_ fd: Int32, _ data: Data) -> Int32? {
        if data.isEmpty { return nil }
        return data.withUnsafeBytes { (raw: UnsafeRawBufferPointer) -> Int32? in
            guard let base = raw.baseAddress else { return nil }
            var sent = 0
            while sent < raw.count {
                let n = Darwin.write(fd, base.advanced(by: sent), raw.count - sent)
                if n <= 0 { return errno }
                sent += n
            }
            return nil
        }
    }

    private func record(error: String) {
        lock.lock()
        serverErrors.append(error)
        lock.unlock()
    }

    private func isClosed() -> Bool {
        lock.lock(); defer { lock.unlock() }
        return closed
    }

    /// **SYNCHRONOUS TEARDOWN.** When this returns, nothing of this server is listening — see
    /// the file header. Bounded rather than indefinite: a wait that timed out would mean the
    /// accept loop ignored the flag, which is a bug in THIS file and should surface as the
    /// next bind failing loudly, not as a hang.
    func close() {
        lock.lock()
        if closed { lock.unlock(); return }
        closed = true
        lock.unlock()

        // Let every held responder go, or they sit on their gates for 30s each.
        gate.release()
        slowGate.release()

        _ = acceptFinished.wait(timeout: .now() + 5)
        Darwin.close(listenFd)
    }
}
