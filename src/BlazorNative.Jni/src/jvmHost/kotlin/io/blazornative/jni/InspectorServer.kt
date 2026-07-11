package io.blazornative.jni

import com.sun.net.httpserver.HttpExchange
import com.sun.net.httpserver.HttpServer
import java.io.IOException
import java.net.InetSocketAddress
import java.util.concurrent.CopyOnWriteArrayList
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit

/**
 * Phase 4.4 Gate 1 — the inspector's HTTP API over a live native session
 * (design §1), on the JDK's built-in `com.sun.net.httpserver` (zero new
 * dependencies), loopback-only. PLACEMENT: src/jvmHost/kotlin — android.jar
 * has no com.sun.net.httpserver, so this class can never join the shared main
 * source set (the full story lives on InspectorHost.kt's main KDoc):
 *
 *  - `GET  /`                    — the inspector page (Gate 2; placeholder today)
 *  - `GET  /api/tree`            — [InspectorState.treeJson]
 *  - `GET  /api/patches?since=`  — [InspectorState.patchesJson] (bad since → 400)
 *  - `GET  /api/events`          — [InspectorState.eventsJson]
 *  - `GET  /sse`                 — pull-model SSE (below)
 *  - `POST /api/dispatch`        — `{handlerId, eventName, payload?}` → the
 *    blocking dispatch → `{"rc":…}`; malformed → 400; an unknown handlerId
 *    rides the runtime's own rc contract (rc surfaces to the caller as data,
 *    never as an HTTP error).
 *
 * THREADING CONTRACT (documented against BlazorNativeRuntime's KDoc):
 *  - HTTP handlers run on [executor]'s daemon pool threads; SSE holds one
 *    thread per connected client for the connection's lifetime (fine at
 *    inspector scale).
 *  - POST /api/dispatch calls [dispatch] — production wires
 *    BlazorNativeRuntime.dispatchEventAndWait, which marshals through the
 *    runtime's single `BlazorNative-Dispatch` lane and blocks for the rc:
 *    post-boot .NET entry stays single-threaded (the renderer's documented
 *    contract) with NO extra locking here — concurrent POSTs simply queue on
 *    the lane. The [dispatch] lambda must therefore be safe for concurrent
 *    callers (the production one is; test stubs trivially are).
 *  - The POST handler does NOT hold the STATE lock while dispatching (the
 *    design's deadlock mitigation): the state lock is only taken by the frame
 *    callback INSIDE the dispatch (on the lane thread), and by
 *    [InspectorState.logDispatch] AFTER the dispatch returned. JSON reads
 *    take the state lock briefly per request. No code path nests locks.
 *
 * SSE (pull model — the design's mitigation for chunked keep-alive being
 * fiddly on this server): the payload is only ever `event: tree-changed` /
 * `event: event-logged` + `data: {"seq":N}` — clients re-fetch `/api/tree`
 * etc. themselves. Each client gets a bounded [LinkedBlockingQueue]; fan-out
 * is a non-blocking `offer` (never blocks a state writer). SLOW-CLIENT DROP:
 * a client whose queue is full gets its queue cleared and a close sentinel
 * enqueued — its writer thread ends the response; the client must reconnect
 * and re-fetch (it lost nothing but notifications, and the pull model makes
 * any fetch self-healing). A 15 s `: keep-alive` comment rides each idle
 * cycle so half-dead connections surface as write failures.
 */
class InspectorServer(
    private val state: InspectorState,
    /** The blocking dispatch (production: BlazorNativeRuntime.dispatchEventAndWait
     * — see the threading contract above; must tolerate concurrent callers). */
    private val dispatch: (handlerId: Int, eventName: String, payload: String?) -> Int,
    requestedPort: Int = DEFAULT_PORT,
) {
    companion object {
        const val DEFAULT_PORT = 5199
        private const val SSE_QUEUE_CAPACITY = 64
        private const val SSE_KEEPALIVE_SECONDS = 15L

        /** Identity-compared close sentinel for SSE writer threads (a fresh
         * String instance — never equal-by-reference to any real message). */
        private val CLOSE_SENTINEL = StringBuilder("close").toString()
    }

    /** Loopback only — the inspector inspects a local session; bind failure
     * (port in use) throws java.net.BindException out of the constructor
     * (InspectorHost catches it and names the -Pport= override). */
    private val server: HttpServer = HttpServer.create(InetSocketAddress("127.0.0.1", requestedPort), 0)

    /** The BOUND port — pass requestedPort 0 for an ephemeral one (tests). */
    val port: Int get() = server.address.port

    private val executor: ExecutorService = Executors.newCachedThreadPool { r ->
        Thread(r, "InspectorHttp").apply { isDaemon = true }
    }

    private val sseClients = CopyOnWriteArrayList<LinkedBlockingQueue<String>>()

    init {
        server.executor = executor
        // Longest-prefix matching: "/" is the fallback context.
        server.createContext("/", ::handleRoot)
        server.createContext("/api/tree", ::handleTree)
        server.createContext("/api/patches", ::handlePatches)
        server.createContext("/api/events", ::handleEvents)
        server.createContext("/api/dispatch", ::handleDispatch)
        server.createContext("/sse", ::handleSse)
        state.addListener { kind, seq -> broadcast(kind, seq) }
    }

    fun start() = server.start()

    /** Stops accepting, ends every SSE writer via the close sentinel, and
     * releases the pool. Idempotent enough for test teardown. */
    fun stop() {
        for (q in sseClients) {
            q.clear()
            q.offer(CLOSE_SENTINEL)
        }
        server.stop(0)
        executor.shutdownNow()
    }

    // ── Handlers ─────────────────────────────────────────────────────────────

    private fun handleRoot(ex: HttpExchange) = guarded(ex) {
        if (ex.requestURI.path != "/") return@guarded respondJson(ex, 404, "{\"error\":\"not found\"}")
        if (ex.requestMethod != "GET") return@guarded methodNotAllowed(ex, "GET")
        respond(
            ex, 200, "text/html; charset=utf-8",
            "<!doctype html><html><head><title>BlazorNative Inspector</title></head><body>" +
                "<h1>BlazorNative Inspector</h1>" +
                "<p>The inspector page lands in Gate 2. The API is live now: " +
                "<code>GET /api/tree</code>, <code>GET /api/patches?since=</code>, " +
                "<code>GET /api/events</code>, <code>GET /sse</code>, " +
                "<code>POST /api/dispatch</code>.</p>" +
                "</body></html>"
        )
    }

    private fun handleTree(ex: HttpExchange) = guarded(ex) {
        if (ex.requestMethod != "GET") return@guarded methodNotAllowed(ex, "GET")
        respondJson(ex, 200, state.treeJson())
    }

    private fun handlePatches(ex: HttpExchange) = guarded(ex) {
        if (ex.requestMethod != "GET") return@guarded methodNotAllowed(ex, "GET")
        val raw = ex.requestURI.query
            ?.split('&')?.firstOrNull { it.startsWith("since=") }
            ?.substringAfter('=')
        val since = if (raw == null) 0L
        else raw.toLongOrNull()
            ?: return@guarded respondJson(ex, 400, "{\"error\":\"since must be a number\"}")
        respondJson(ex, 200, state.patchesJson(since))
    }

    private fun handleEvents(ex: HttpExchange) = guarded(ex) {
        if (ex.requestMethod != "GET") return@guarded methodNotAllowed(ex, "GET")
        respondJson(ex, 200, state.eventsJson())
    }

    private fun handleDispatch(ex: HttpExchange) = guarded(ex) {
        if (ex.requestMethod != "POST") return@guarded methodNotAllowed(ex, "POST")
        val body = ex.requestBody.readBytes().toString(Charsets.UTF_8)
        val fields = try {
            InspectorJson.parseFlatObject(body)
        } catch (e: IllegalArgumentException) {
            return@guarded respondJson(ex, 400, "{\"error\":${InspectorJson.string(e.message ?: "malformed JSON")}}")
        }
        val handlerId = fields["handlerId"]?.toIntOrNull()
            ?: return@guarded respondJson(ex, 400, "{\"error\":\"handlerId (number) is required\"}")
        val eventName = fields["eventName"]?.takeIf { it.isNotBlank() }
            ?: return@guarded respondJson(ex, 400, "{\"error\":\"eventName (non-empty string) is required\"}")
        val payload = fields["payload"]

        // The blocking dispatch — serialized by the runtime's dispatch lane,
        // STATE lock NOT held (the frame callback inside the dispatch takes
        // it on the lane thread; see the class KDoc).
        val rc = dispatch(handlerId, eventName, payload)
        state.logDispatch(handlerId, eventName, payload, rc)
        respondJson(ex, 200, "{\"rc\":$rc}")
    }

    private fun handleSse(ex: HttpExchange) {
        if (ex.requestMethod != "GET") return methodNotAllowed(ex, "GET")
        val queue = LinkedBlockingQueue<String>(SSE_QUEUE_CAPACITY)
        try {
            ex.responseHeaders.add("Content-Type", "text/event-stream; charset=utf-8")
            ex.responseHeaders.add("Cache-Control", "no-cache")
            ex.sendResponseHeaders(200, 0) // length 0 = chunked stream
            val out = ex.responseBody
            // Register BEFORE the handshake comment: once the client reads
            // ": connected", its queue is guaranteed to hear later broadcasts.
            sseClients.add(queue)
            out.write(": connected\n\n".toByteArray(Charsets.UTF_8))
            out.flush()
            while (true) {
                val msg = queue.poll(SSE_KEEPALIVE_SECONDS, TimeUnit.SECONDS) ?: ": keep-alive\n\n"
                if (msg === CLOSE_SENTINEL) break
                out.write(msg.toByteArray(Charsets.UTF_8))
                out.flush()
            }
        } catch (_: IOException) {
            // Client went away — normal SSE teardown.
        } catch (_: InterruptedException) {
            // Executor shutdown — server stopping.
        } finally {
            sseClients.remove(queue)
            ex.close()
        }
    }

    /** State-listener fan-out (runs on the writer's thread, OUTSIDE the state
     * lock): non-blocking offer per client; a full queue = slow client → its
     * pending notifications are replaced by the close sentinel (see KDoc). */
    private fun broadcast(kind: String, seq: Long) {
        val msg = "event: $kind\ndata: {\"seq\":$seq}\n\n"
        for (q in sseClients) {
            if (!q.offer(msg)) {
                q.clear()
                q.offer(CLOSE_SENTINEL)
            }
        }
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────

    /** Wraps a handler body: an unexpected throw becomes a 500 with detail
     * (com.sun.net.httpserver would otherwise swallow it and stall the
     * exchange). */
    private inline fun guarded(ex: HttpExchange, body: () -> Unit) {
        try {
            body()
        } catch (t: Throwable) {
            try {
                respondJson(ex, 500, "{\"error\":${InspectorJson.string(t.toString())}}")
            } catch (_: IOException) {
                // Response already committed or client gone — nothing to say.
            }
        }
    }

    private fun methodNotAllowed(ex: HttpExchange, allowed: String) {
        ex.responseHeaders.add("Allow", allowed)
        respondJson(ex, 405, "{\"error\":\"method not allowed; use $allowed\"}")
    }

    private fun respondJson(ex: HttpExchange, status: Int, json: String) =
        respond(ex, status, "application/json; charset=utf-8", json)

    private fun respond(ex: HttpExchange, status: Int, contentType: String, body: String) {
        val bytes = body.toByteArray(Charsets.UTF_8)
        ex.responseHeaders.add("Content-Type", contentType)
        ex.sendResponseHeaders(status, bytes.size.toLong())
        ex.responseBody.use { it.write(bytes) }
        ex.close()
    }
}
