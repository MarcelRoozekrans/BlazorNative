package io.blazornative.jni

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Assertions.fail
import org.junit.jupiter.api.Test
import java.io.BufferedReader
import java.net.HttpURLConnection
import java.net.URL

/**
 * Phase 4.4 Gate 1 — the load-bearing end-to-end HTTP test: InspectorServer
 * on an EPHEMERAL port over a REAL booted session (the BnDemoTest harness),
 * riding the Phase 3.5 navigation as the proof — GET /api/tree shows BnDemo,
 * POST /api/dispatch with the "Settings →" click handlerId HARVESTED FROM THE
 * TREE JSON navigates, and the next GET /api/tree shows BnSettingsPage.
 *
 * The whole journey lives in ONE test method: the steps are strictly ordered
 * against one live session (SSE must connect before the dispatch it observes;
 * the settings tree only exists after the dispatch). The malformed-POST 400
 * matrix runs separately against a stub dispatcher — no session needed, the
 * requests must be rejected before any dispatch happens.
 *
 * Single-JVM safety (the BnDemoTest conventions): init is idempotent,
 * frame-callback registration is last-wins, each start() mounts a FRESH
 * BnDemo, and every id (node/handler) is harvested from THIS test's own tree
 * JSON — never hardcoded (ids are process-global monotonic counters).
 */
class InspectorServerTest {

    private companion object {
        const val SSE_DEADLINE_MS = 10_000
    }

    // ── HTTP helpers (JDK-only, matching the zero-dependency posture) ────────

    private fun httpGet(url: String): Pair<Int, String> {
        val conn = URL(url).openConnection() as HttpURLConnection
        conn.connectTimeout = 5_000
        conn.readTimeout = 10_000
        val code = conn.responseCode
        val body = (if (code < 400) conn.inputStream else conn.errorStream)
            ?.bufferedReader(Charsets.UTF_8)?.use(BufferedReader::readText) ?: ""
        conn.disconnect()
        return code to body
    }

    private fun httpPost(url: String, body: String): Pair<Int, String> {
        val conn = URL(url).openConnection() as HttpURLConnection
        conn.connectTimeout = 5_000
        conn.readTimeout = 10_000
        conn.requestMethod = "POST"
        conn.doOutput = true
        conn.setRequestProperty("Content-Type", "application/json; charset=utf-8")
        conn.outputStream.use { it.write(body.toByteArray(Charsets.UTF_8)) }
        val code = conn.responseCode
        val response = (if (code < 400) conn.inputStream else conn.errorStream)
            ?.bufferedReader(Charsets.UTF_8)?.use(BufferedReader::readText) ?: ""
        conn.disconnect()
        return code to response
    }

    /** Blocking SSE client: connects, verifies the server's ": connected"
     * handshake comment (proof the per-client queue is registered), then
     * [awaitEvent] reads lines until the named event or the deadline
     * (HttpURLConnection readTimeout is the hard stop). */
    private class SseClient(url: String) {
        private val conn = URL(url).openConnection() as HttpURLConnection
        private val reader: BufferedReader

        init {
            conn.connectTimeout = 5_000
            conn.readTimeout = SSE_DEADLINE_MS
            assertEquals(200, conn.responseCode, "SSE endpoint must answer 200")
            assertEquals(
                "text/event-stream; charset=utf-8", conn.getHeaderField("Content-Type"),
                "SSE content type"
            )
            reader = conn.inputStream.bufferedReader(Charsets.UTF_8)
            assertEquals(": connected", reader.readLine(), "SSE handshake comment")
        }

        fun awaitEvent(name: String) {
            val deadline = System.nanoTime() + SSE_DEADLINE_MS * 1_000_000L
            while (System.nanoTime() < deadline) {
                val line = reader.readLine()
                    ?: fail<Nothing>("SSE stream closed before 'event: $name' arrived")
                if (line == "event: $name") return
            }
            fail<Nothing>("SSE 'event: $name' not received within $SSE_DEADLINE_MS ms")
        }

        fun close() = conn.disconnect()
    }

    /** HandlerId of the click event on the node whose (collapsed) text is
     * [label] — harvested from the tree JSON, the way the Gate 2 page will.
     * Relies on the pinned stable key order: the node's "events" follows its
     * "text" before any following node's "id". */
    private fun harvestClickHandler(treeJson: String, label: String): Int {
        val textKey = "\"text\":${InspectorJson.string(label)}"
        val ti = treeJson.indexOf(textKey)
        assertTrue(ti >= 0, "node with text '$label' not found in tree JSON: $treeJson")
        val eventsKey = "\"events\":{\"click\":"
        val ei = treeJson.indexOf(eventsKey, startIndex = ti)
        assertTrue(ei >= 0, "click event for '$label' not found in tree JSON: $treeJson")
        val nextNode = treeJson.indexOf("{\"id\":", startIndex = ti)
        assertTrue(
            nextNode == -1 || ei < nextNode,
            "the click handler must belong to the '$label' node itself"
        )
        val digits = treeJson.substring(ei + eventsKey.length).takeWhile { it.isDigit() }
        assertTrue(digits.isNotEmpty(), "handlerId digits missing after $eventsKey")
        return digits.toInt()
    }

    private fun count(haystack: String, needle: String): Int {
        var n = 0
        var i = haystack.indexOf(needle)
        while (i >= 0) {
            n++
            i = haystack.indexOf(needle, i + needle.length)
        }
        return n
    }

    // ── The journey: tree → dispatch → navigated tree → SSE → patches ────────

    /** The Settings click dispatches Navigate through the shell bridge — a
     * live host must be registered or the handler faults with rc 2 (the
     * NavigationTest harness shape, storage/fetch inert). */
    private class InertBridge : ShellBridgeHandlers {
        @Volatile private var route: String = "/"
        override fun navigate(route: String) { this.route = route }
        override fun currentRoute(): String = route
        override fun storageRead(key: String): String? = null
        override fun storageWrite(key: String, value: String) {}
        override fun storageDelete(key: String) {}
        override fun fetchBegin(requestId: Long, request: BridgeFetchRequest) {
            BridgeFetchCompleter.completeFailure(requestId, "InspectorServerTest performs no fetch")
        }
    }

    @Test
    fun end_to_end_tree_dispatch_navigation_sse_and_logs() {
        val state = InspectorState()
        val runtime = BlazorNativeRuntime(onFrame = state::onFrame, onError = state::logError)
        runtime.start(componentName = "BnDemo", platformOs = "test-host", bridge = InertBridge())
        // The production dispatch shape: lane-marshalled, blocking, raw rc.
        val server = InspectorServer(state, dispatch = runtime::dispatchEventAndWait, requestedPort = 0)
        server.start()
        var sse: SseClient? = null
        try {
            val base = "http://127.0.0.1:${server.port}"

            // GET / — the self-contained inspector page: title, the SSE
            // wiring, the dispatch call, and the fast-restart honesty footer
            // (structure pins only — behavior is the manual browser smoke).
            val (rootCode, rootBody) = httpGet("$base/")
            assertEquals(200, rootCode)
            assertTrue(rootBody.contains("<title>BlazorNative Inspector</title>"), "page title missing")
            assertTrue(rootBody.contains("new EventSource('/sse')"), "page must wire SSE")
            assertTrue(rootBody.contains("fetch('/api/dispatch'"), "page must wire dispatch")
            assertTrue(rootBody.contains("fast-restart, not hot-reload"), "honesty footer missing")
            assertTrue(rootBody.contains("component <b>BnDemo</b>"), "component name missing from header")
            assertFalse(
                Regex("(src|href)\\s*=\\s*[\"']https?://").containsMatchIn(rootBody),
                "page must be self-contained — no external requests"
            )

            // GET /api/tree — the BnDemo shape: the bound input + 3 buttons.
            val (treeCode, tree) = httpGet("$base/api/tree")
            assertEquals(200, treeCode)
            assertEquals(1, count(tree, "\"type\":\"input\""), "exactly one input; got $tree")
            assertEquals(3, count(tree, "\"type\":\"button\""), "exactly three buttons; got $tree")
            for (label in listOf("Clear", "Theme", "Settings →")) {
                assertTrue(tree.contains("\"text\":${InspectorJson.string(label)}"), "'$label' button missing; got $tree")
            }

            // Harvest the "Settings →" click handlerId FROM THE TREE JSON.
            val settingsHandler = harvestClickHandler(tree, "Settings →")

            // SSE connects BEFORE the dispatch it must observe.
            sse = SseClient("$base/sse")

            // POST /api/dispatch — the blocking dispatch through the dll.
            val (rcCode, rcBody) = httpPost(
                "$base/api/dispatch",
                "{\"handlerId\":$settingsHandler,\"eventName\":\"click\"}"
            )
            assertEquals(200, rcCode)
            assertEquals("{\"rc\":0}", rcBody)

            // GET /api/tree — now the settings page (the 3.5 navigation as
            // the proof): title "Settings", the "← Back" button, NO input.
            val (_, settingsTree) = httpGet("$base/api/tree")
            assertTrue(settingsTree.contains("\"text\":\"Settings\""), "settings title missing; got $settingsTree")
            assertTrue(
                settingsTree.contains("\"text\":${InspectorJson.string("← Back")}"),
                "back button missing; got $settingsTree"
            )
            assertFalse(settingsTree.contains("\"type\":\"input\""), "BnSettingsPage must not have an input; got $settingsTree")

            // Event log contains the dispatch (with its rc).
            val (_, events) = httpGet("$base/api/events")
            assertTrue(events.contains("\"kind\":\"dispatch\""), "dispatch missing from event log; got $events")
            assertTrue(
                events.contains("handlerId=$settingsHandler event='click' rc=0"),
                "dispatch detail missing from event log; got $events"
            )

            // SSE heard tree-changed within the deadline (the frames the
            // dispatch delivered are already buffered on the socket).
            sse.awaitEvent("tree-changed")

            // /api/patches — the ring tail is non-empty and since= filters.
            val (patchesCode, patches) = httpGet("$base/api/patches")
            assertEquals(200, patchesCode)
            assertTrue(patches.contains("\"seq\":"), "patch ring must be non-empty; got $patches")
            assertTrue(patches.contains("RemoveNode"), "the navigation's removes must be ringed; got $patches")
            val (_, none) = httpGet("$base/api/patches?since=999999")
            assertEquals("{\"patches\":[]}", none)
        } finally {
            sse?.close()
            server.stop()
        }
    }

    // ── Malformed dispatches → 400 (rejected before any dispatch) ───────────

    @Test
    fun malformed_dispatch_posts_return_400() {
        val state = InspectorState()
        var dispatched = false
        val server = InspectorServer(
            state,
            dispatch = { _, _, _ ->
                dispatched = true
                0
            },
            requestedPort = 0,
        )
        server.start()
        try {
            val base = "http://127.0.0.1:${server.port}"
            for (bad in listOf(
                "not json",                                    // unparseable
                "{\"eventName\":\"click\"}",                   // handlerId missing
                "{\"handlerId\":\"abc\",\"eventName\":\"click\"}", // handlerId not a number
                "{\"handlerId\":7}",                           // eventName missing
                "{\"handlerId\":7,\"eventName\":\"\"}",        // eventName blank
            )) {
                val (code, body) = httpPost("$base/api/dispatch", bad)
                assertEquals(400, code, "expected 400 for body: $bad (got $code, $body)")
                assertTrue(body.contains("\"error\":"), "400 body must carry an error; got $body")
            }
            assertFalse(dispatched, "malformed requests must never reach the dispatcher")

            // Wrong method on the dispatch endpoint → 405.
            val (getCode, _) = httpGet("$base/api/dispatch")
            assertEquals(405, getCode)

            // Malformed since → 400.
            val (sinceCode, _) = httpGet("$base/api/patches?since=abc")
            assertEquals(400, sinceCode)

            // Unknown path → 404.
            val (nfCode, _) = httpGet("$base/nope")
            assertEquals(404, nfCode)
        } finally {
            server.stop()
        }
    }
}
