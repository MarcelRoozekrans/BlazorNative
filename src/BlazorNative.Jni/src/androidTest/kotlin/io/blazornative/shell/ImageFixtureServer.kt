package io.blazornative.shell

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Canvas
import android.graphics.Color
import androidx.test.platform.app.InstrumentationRegistry
import coil.imageLoader
import java.io.ByteArrayOutputStream
import java.io.IOException
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.net.Socket
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import kotlin.concurrent.thread

/**
 * Phase 6.3 Gate 2 Task 2.1 — **THE IN-PROCESS LOOPBACK FIXTURE SERVER.**
 *
 * 6.3 non-negotiable #5: **CI never touches the public internet.** A suite whose green
 * depends on a remote host is not a suite. So the three sources `BnImageDemo` names
 * point at a server the shell process itself stands up, and the failing case is a path
 * that server genuinely **404s** — a real HTTP fetch through Coil, a real failure,
 * deterministic and offline.
 *
 * Instrumented tests run **in the app's own process**, so a `ServerSocket` bound here is
 * bound in the process Coil fetches from. On the AVD `127.0.0.1` is the emulated device's
 * own loopback (it is the HOST MAC's in the iOS simulator — see `BnImageDemo.razor`'s header;
 * that difference is Gate 3's problem, not this one's).
 *
 * ── CLEARTEXT: COVERED BY INHERITANCE, AND VERIFIED RATHER THAN ASSUMED ─────────────
 * `targetSdk 34` blocks cleartext HTTP outright, and this repo ate that in Phase 3.1.
 * `src/debug/res/xml/network_security_config.xml` (a build-type res overlay) already
 * permits cleartext to `127.0.0.1` / `localhost`, and **instrumented tests run the DEBUG
 * build** — so Gate 2 needs no manifest change. The corollary is recorded and is NOT a
 * bug: `androidMain`'s config permits none, so a **release** build of the demo shows
 * three failed images on `/image`. The fix is never to weaken the release config.
 *
 * [fetch] is what turns "covered by inheritance" into a **checked** fact
 * (`BnImageDemoAndroidTest.cleartext_loopback_is_permitted_in_the_debug_build`): it pulls
 * a fixture over the same cleartext loopback Coil will use, through the platform's own
 * HTTP stack, so the network-security-config verdict is read directly. A blocked load
 * there names itself — instead of surfacing as this phase's most dangerous symptom, in
 * which a blocked load is INDISTINGUISHABLE from the 404 that case [2] expects and two of
 * three demo assertions stay green on a device that loaded nothing.
 *
 * ── THE GATE ────────────────────────────────────────────────────────────────────────
 * Every response is held until [release]. That is what makes `BnImageDemo`'s **"before
 * the bytes" frame table observable at all**: without it the loopback fetch wins the
 * race against the test's first look at the tree, and the BEFORE table is asserted on a
 * page that has already reflowed. The hold is milliseconds in practice (the test releases
 * as soon as it has read the BEFORE frames) — comfortably inside OkHttp's 10s read
 * timeout, and a hold that ever DID time out fails loudly (the intrinsic image's
 * positive `Wi > 0` assertion reddens), never silently.
 *
 * ── BIND EXCLUSIVELY, FAIL LOUDLY ───────────────────────────────────────────────────
 * No fallback: if 8099 is taken — a leaked server from an earlier class, anything else on
 * the device — the bind throws, and it throws a message that NAMES THE PORT and says why a
 * foreign server would be worse than a missing one. Never "someone is already listening,
 * good enough": a foreign server on 8099 serves foreign bytes, and a 200 on `/missing.png`
 * would fail case [2] *silently*. (The fixture-contract assertions in the tests are the
 * second half of that probe: a foreign server cannot serve an image whose natural size is
 * the one we assert.)
 *
 * **`reuseAddress = true` is what makes that exclusive — it is not a weakening.**
 * `SO_REUSEADDR` is NOT `SO_REUSEPORT`: it does not let a second process LISTEN on a port
 * someone is already listening on (that still throws `BindException`), it only permits
 * re-binding over the TIME_WAIT *connections* our own previous test class left behind. See
 * the inline comment on the socket.
 */
internal class ImageFixtureServer {

    companion object {
        /**
         * The origin and the three paths **`BnImageDemo.razor` declares** (`FixtureOrigin`,
         * `FixedSrc`, `IntrinsicSrc`, `FailingSrc`) and `BnScrollDemo.RowImageSrc` reuses.
         *
         * They are Kotlin constants because a device-side test cannot read a `.razor` file —
         * so the drift pin is asserted on the **WIRE** instead, which is stronger than a
         * transcription check anyway: `BnImageDemoAndroidTest` asserts the outcomes against
         * the URLs the renderer actually put on the `UpdateProp` wire, and they must be
         * exactly the three this server routes. Change a URL in `BnImageDemo.razor` and that
         * assertion reddens by name, rather than the page quietly 404ing three times.
         */
        const val ORIGIN = "http://127.0.0.1:8099"
        const val FIXED_URL = "$ORIGIN/fixed.png"
        const val INTRINSIC_URL = "$ORIGIN/intrinsic.png"
        const val MISSING_URL = "$ORIGIN/missing.png"

        /** Test-only, on no wire: a path whose response is held until [releaseSlow], so a
         * request can be observed **in flight** — which is the only way to prove a
         * cancellation cancelled anything. */
        const val SLOW_URL = "$ORIGIN/slow.png"

        private const val PORT = 8099

        /**
         * **THE FIXTURE CONTRACT** (`BnImageDemo.razor` §"THE FIXTURE'S CONTRACT"). Every one
         * of these is an ASSERTION in the tests, evaluated on the **decoded** fixture
         * before any frame is looked at — never a comment, and never a property of a file
         * someone once checked in. The fixtures are generated here rather than committed
         * as binaries precisely so the size is a fact of the code.
         *
         *  - `Wi = 160 ≤ 300` — a section is 300 wide, so the measure func is called with
         *    `AT_MOST(300)`; a wider fixture would ask a clamping question this phase
         *    deliberately does not answer (no `ContentMode` — design decision 3).
         *  - `Hi = 90 > 0`, comfortably: **Hi IS the reflow**. A 0-high fixture would make
         *    the reflow assertion vacuously true.
         *  - `(64, 48) ≠ (200, 120)` — the FIXED case's declared size. Otherwise "it
         *    measures 200 × 120" is a coincidence, not a proof that a declared size
         *    short-circuits measurement. It is also ≠ (40, 40), `BnScrollDemo`'s row
         *    image, which buys the same proof inside the scroll.
         *
         * ── THESE FOUR NUMBERS ARE NOT THIS FILE'S TO CHOOSE (Gate 2 review, the BLOCKER) ──
         * **`BnImageDemo.razor` owns them** — `IntrinsicNaturalWidthPx` / `…HeightPx` /
         * `FixedNaturalWidthPx` / `…HeightPx` — because **both shells must assert the SAME
         * numbers** and the `.razor` is the one file both gates read. They were "symbolic and
         * Gate-supplied", which meant Gate 3 could legitimately pick a different fixture,
         * apply a different unit rule, and be fully green: nothing enforced the phase's own
         * verification bar #1 ("the same frames on both devices").
         *
         * The transcription is **pinned by a drift test** — `BnImageDemoTests
         * .TheAndroidFixtureServer_ServesExactlyBnImageDemosNaturalPixelSizes` parses these
         * four `const val`s out of THIS file and asserts equality with the `.razor`. Keep them
         * as plain `const val NAME = <int>` declarations at the start of their line: that is
         * what the parser looks for, and a declaration it cannot find fails it LOUDLY.
         */
        const val INTRINSIC_W = 160
        const val INTRINSIC_H = 90
        const val FIXED_W = 64
        const val FIXED_H = 48

        /**
         * **ONE PIXEL OF THE FILE IS ONE dp/pt** — the parity contract's UNIT row (design
         * §"The parity contract"), and the rule that says these `Int`s of PIXELS are also the
         * `Float`s of dp Yoga computes with. It is what `UIImage(data:).size` gives iOS for
         * free (`scale == 1`), so it is what Android must report for the two shells to compute
         * the same frame — and it is why Gate 3 must NOT set Kingfisher's `scaleFactor`. See
         * [YogaLayout.setImageNaturalSize], which is where the shell states it.
         */
        const val INTRINSIC_W_DP = INTRINSIC_W.toFloat()
        const val INTRINSIC_H_DP = INTRINSIC_H.toFloat()

        /**
         * Coil caches, and a cached fixture completes **without touching this server** —
         * which would un-gate the BEFORE table and make it order-dependent on whichever
         * test ran first. The disk cache outlives the process, so a *second run* of the
         * suite on the same AVD would do it too. Cleared before every test that mounts.
         */
        @OptIn(coil.annotation.ExperimentalCoilApi::class) // DiskCache — test-only
        fun clearCoilCaches() {
            val loader = InstrumentationRegistry.getInstrumentation().targetContext.imageLoader
            loader.memoryCache?.clear()
            loader.diskCache?.clear()
        }

        /** A PNG of exactly [w] × [h] pixels. Android's PNG encoder writes no `pHYs`
         * density chunk, so the decoded bitmap's size is the raw pixel size on any
         * device — which is what makes the fixture contract a fact rather than a hope. */
        private fun png(w: Int, h: Int, color: Int): ByteArray {
            val bmp = Bitmap.createBitmap(w, h, Bitmap.Config.ARGB_8888)
            Canvas(bmp).drawColor(color)
            val out = ByteArrayOutputStream()
            check(bmp.compress(Bitmap.CompressFormat.PNG, 100, out)) { "PNG encode failed" }
            bmp.recycle()
            return out.toByteArray()
        }
    }

    /** [1]'s bytes — and the ones whose natural size IS the reflow. */
    val intrinsicPng: ByteArray = png(INTRINSIC_W, INTRINSIC_H, Color.rgb(0x21, 0x96, 0xF3))

    /** [0]'s bytes (and `BnScrollDemo`'s row image's) — natural size ≠ its declared one. */
    val fixedPng: ByteArray = png(FIXED_W, FIXED_H, Color.rgb(0xFF, 0x98, 0x00))

    /**
     * The fixture as the wire actually carries it: **decoded from the bytes this server
     * serves**, not from the Bitmap they were encoded from. That round trip is the point —
     * it is the same decode Coil performs, so a fixture that did not survive PNG encoding
     * at its stated size is caught here rather than mis-attributed to the shell.
     */
    fun decoded(bytes: ByteArray): Bitmap =
        requireNotNull(BitmapFactory.decodeByteArray(bytes, 0, bytes.size)) {
            "the fixture PNG did not decode — the fixture, not the shell, is broken"
        }

    /**
     * A plain-HTTP GET through the platform stack — the **cleartext probe**. Returns the
     * status code and the body. Any cleartext block surfaces here as the throw it is
     * (`java.io.IOException: Cleartext HTTP traffic to 127.0.0.1 not permitted`), naming
     * itself, rather than as a Coil failure indistinguishable from a 404.
     */
    fun fetch(url: String): Pair<Int, ByteArray> {
        val conn = java.net.URL(url).openConnection() as java.net.HttpURLConnection
        conn.connectTimeout = 10_000
        conn.readTimeout = 10_000
        return try {
            val code = conn.responseCode
            val body = (if (code in 200..299) conn.inputStream else conn.errorStream)
                ?.use { it.readBytes() } ?: ByteArray(0)
            code to body
        } finally {
            conn.disconnect()
        }
    }

    /** Every path this server has been ASKED for, in order — recorded before the gate.
     * [awaitPath] reads it, and it is what makes "cancelled **in flight**" an honest
     * claim: a request cancelled before Coil ever opened the socket would report
     * `CANCELLED` too, and would prove nothing about cancellation. */
    private val requested = java.util.Collections.synchronizedList(mutableListOf<String>())

    /** Blocks until [path] has actually reached this server. */
    fun awaitPath(path: String, timeoutMs: Long = 15_000): Boolean {
        val deadline = System.currentTimeMillis() + timeoutMs
        while (System.currentTimeMillis() < deadline) {
            if (requested.contains(path)) return true
            Thread.sleep(25)
        }
        return false
    }

    /**
     * **THE SERVER'S OWN FAILURES** — path + exception, in order (Gate 2 review, I4).
     *
     * [serveQuietly] swallows `IOException`, and the SCOPING of that swallow is right: when the
     * shell CANCELS a request, Coil closes the connection and this side's next write is a broken
     * pipe. That is cancellation observed from the other end of the socket — the very thing the
     * cancellation tests ask for, and uncaught on a background thread it would take the app
     * process down (Android's default handler), failing the run with a crash instead of a result.
     *
     * But it was SILENT and UNCONDITIONAL, and the classes that **cancel nothing**
     * ([BnImageDemoAndroidTest], [BnScrollDemoImageAndroidTest]) have no client that could drop a
     * connection — so an `IOException` there is a REAL SERVER BUG that produced no signal at all,
     * and the test merely timed out in its synchronization gate, blaming the gate. So every
     * swallowed exception is RECORDED, and the classes that cancel nothing **assert this list is
     * empty**. The classes that DO cancel (`WidgetMapperImageTest`) expect entries here and do
     * not assert it.
     */
    private val serverErrors = java.util.Collections.synchronizedList(mutableListOf<String>())

    /** See [serverErrors]. A snapshot, safe to read from the test thread. */
    val errors: List<String> get() = serverErrors.toList()

    private val gate = CountDownLatch(1)
    private val slowGate = CountDownLatch(1)
    private val closed = AtomicBoolean(false)
    private val server: ServerSocket = ServerSocket().apply {
        // ── EXCLUSIVE, AND `true` IS WHAT MAKES IT SO ────────────────────────────────
        // `SO_REUSEADDR` IS NOT `SO_REUSEPORT`. It does NOT let a second process LISTEN on
        // a port someone else is listening on — that still throws `BindException`, which is
        // the loud failure this server owes (a foreign server on 8099 would serve foreign
        // bytes, and a 200 on `/missing.png` would fail case [2] SILENTLY). All it permits
        // is re-binding over the TIME_WAIT *connections* our own previous test left behind
        // — and `false` refuses even that, so the second test class to want the port dies
        // for a reason that has nothing to do with anyone taking it.
        reuseAddress = true
        // …and the bind is wrapped so the failure NAMES THE PORT and says what it means. A
        // bare `BindException: bind failed: EADDRINUSE` at the top of a stack trace inside
        // a @Before reads as flake; it is not. It is the ONE outcome this server must never
        // paper over — a foreign listener on 8099 is strictly WORSE than no listener at all,
        // because a missing server 404s/refuses everything (loud), while a foreign one can
        // 200 `/missing.png` and quietly certify a contract nobody tested.
        try {
            bind(InetSocketAddress("127.0.0.1", PORT), 16)
        } catch (e: IOException) {
            throw IllegalStateException(
                "the image fixture server could not bind 127.0.0.1:$PORT — something else is " +
                    "already listening on it (a leaked server from an earlier class, or another " +
                    "process on the device). THIS IS NOT FLAKE AND MUST NOT BE RETRIED AROUND: a " +
                    "FOREIGN server on $PORT is worse than none, because it would serve foreign " +
                    "bytes to /fixed.png and /intrinsic.png and could answer /missing.png with a " +
                    "200 — and case [2] of /image asserts a FAILURE, so it would pass SILENTLY on " +
                    "a page that loaded someone else's images. Find the listener and kill it.", e)
        }
    }

    /** The accept loop's thread — kept so [close] can JOIN it. See [close]. */
    private val acceptThread: Thread = thread(name = "bn-image-fixture-accept", isDaemon = true) {
        while (!closed.get()) {
            val socket = try {
                server.accept()
            } catch (_: IOException) {
                break // closed
            }
            // A thread PER CONNECTION: the three demo requests are held on [gate] at
            // the same time, so a single-threaded responder would serialise them and
            // the third would never even reach the gate.
            thread(name = "bn-image-fixture-serve", isDaemon = true) { serveQuietly(socket) }
        }
    }

    /** Opens the gate: every held response — and every later one — goes out. */
    fun release() = gate.countDown()

    /** Opens the SLOW path's gate (the cancellation tests' "now complete it"). */
    fun releaseSlow() = slowGate.countDown()

    /**
     * **TEARDOWN IS SYNCHRONOUS, AND IT HAS TO BE: THE NEXT `@Before` BINDS THE SAME PORT.**
     *
     * `ServerSocket.close()` closes the descriptor and signals the thread blocked in `accept()`
     * — but it **returns before that thread has actually unwound**, and until it has, the
     * listener can still be holding :8099. The next test's `@Before` then binds within
     * milliseconds and gets `EADDRINUSE` from **our own previous instance**, which is a
     * pathology the bind message correctly refuses to retry around (a foreign listener on 8099
     * is a real and dangerous thing, and this teardown is what makes that message TRUE when it
     * fires).
     *
     * So `close()` **joins the accept thread**: when it returns, nothing of this server is
     * listening. It is bounded rather than indefinite — a join that timed out would mean the
     * accept loop ignored a closed socket, which is a bug in THIS file and should surface as the
     * next bind failing loudly, not as a hang.
     *
     * The per-connection SERVE threads are deliberately NOT joined: they hold *accepted* sockets
     * (whose local port is 8099 but which are `ESTABLISHED`/`TIME_WAIT`, not `LISTEN`), and
     * `SO_REUSEADDR` — set on the socket above — is precisely the option that lets a fresh
     * listener bind over those. Joining them would also mean waiting on the 30-second gate awaits
     * of a cancelled test.
     */
    fun close() {
        closed.set(true)
        release()
        releaseSlow()
        try { server.close() } catch (_: IOException) { /* already closed */ }
        acceptThread.join(5_000)
    }

    /**
     * **A DROPPED CLIENT IS THE NORMAL CASE HERE, NOT AN ERROR — BUT IT IS NEVER SILENT.**
     *
     * When the shell CANCELS a request — a `Src` change, a node removal — Coil closes the
     * connection, and this thread's next write gets an `IOException: Broken pipe`. That is
     * cancellation, observed from the other end of the socket; it is the very thing the
     * cancellation tests are asking for. Uncaught on a background thread it would take the
     * whole app process down with it (Android's default handler), failing the run with a
     * crash rather than a result — which is exactly what it did the first time this ran.
     *
     * So it is caught — and **RECORDED** ([serverErrors]), with the path it happened on.
     * `BnImageDemoAndroidTest` and `BnScrollDemoImageAndroidTest` cancel NOTHING, so an
     * `IOException` in either is a real server bug; unrecorded, its only symptom was the
     * synchronization gate timing out 30 seconds later and taking the blame for it.
     */
    private fun serveQuietly(socket: Socket) {
        val path = java.util.concurrent.atomic.AtomicReference("<no request line read>")
        try {
            serve(socket, path)
        } catch (e: IOException) {
            // The client went away (cancellation — see the KDoc), or the server broke. This
            // side cannot tell the two apart; the TEST can, because only some tests cancel.
            serverErrors.add("${path.get()}: ${e.javaClass.simpleName}: ${e.message}")
        }
    }

    private fun serve(socket: Socket, servedPath: java.util.concurrent.atomic.AtomicReference<String>) {
        socket.use {
            val input = it.getInputStream().bufferedReader()
            val requestLine = input.readLine() ?: return
            // Drain the headers; the body is irrelevant (every request is a GET).
            while (true) {
                val line = input.readLine() ?: break
                if (line.isEmpty()) break
            }
            val path = requestLine.split(' ').getOrNull(1) ?: "/"
            servedPath.set(path) // so a swallowed IOException can NAME the path it died on
            requested.add(path) // BEFORE the gate — see [requested]

            // THE GATE — every response, including the 404, waits here. A blocked case [2]
            // that answered immediately would let the test read the BEFORE table while the
            // 404 had already landed, and "the failure reserved nothing" would be asserted
            // against a request that had not happened.
            if (path == "/slow.png") slowGate.await(30, TimeUnit.SECONDS)
            else gate.await(30, TimeUnit.SECONDS)

            val out = it.getOutputStream()
            when (path) {
                "/fixed.png" -> respond(out, 200, "OK", "image/png", fixedPng)
                "/intrinsic.png", "/slow.png" -> respond(out, 200, "OK", "image/png", intrinsicPng)
                // THE FAILING CASE — a REAL 404 from a REAL server, offline and
                // deterministic. Not a dropped connection, not a timeout: the failure the
                // contract's failure row is written about.
                else -> respond(out, 404, "Not Found", "text/plain",
                    "no such fixture: $path".toByteArray())
            }
            out.flush()
        }
    }

    private fun respond(
        out: java.io.OutputStream,
        status: Int,
        reason: String,
        contentType: String,
        body: ByteArray,
    ) {
        val head = buildString {
            append("HTTP/1.1 $status $reason\r\n")
            append("Content-Type: $contentType\r\n")
            append("Content-Length: ${body.size}\r\n")
            // No keep-alive: one request per connection keeps the responder trivial, and
            // OkHttp opens as many as it needs.
            append("Connection: close\r\n")
            append("Cache-Control: no-store\r\n")
            append("\r\n")
        }
        out.write(head.toByteArray())
        out.write(body)
    }
}
