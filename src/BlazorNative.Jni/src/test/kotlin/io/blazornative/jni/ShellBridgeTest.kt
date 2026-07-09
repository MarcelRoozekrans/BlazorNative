package io.blazornative.jni

import com.sun.jna.Memory
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertThrows
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.net.HttpURLConnection
import java.net.InetAddress
import java.net.ServerSocket
import java.net.URL
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference

/**
 * Phase 3.1 Gate 2 — the Kotlin half of the shell-bridge C-ABI, proven on the
 * desktop JVM against the win-x64 NativeAOT dll:
 *
 *   register_bridge(6 callbacks) → blazornative_run_bridge_probes(url) → 0
 *
 * The probes (BridgeProbeRunner inside the dll) exercise navigate round-trip,
 * storage write/read/delete + absent-key null, and ONE fetch against the URL
 * we pass — served here by a tiny ServerSocket responder (com.sun.net
 * .httpserver is absent from the AGP unit-test COMPILE classpath, which is
 * android.jar; the responder also mirrors what Gate 3's instrumented test
 * uses on-device), completed asynchronously via BridgeFetchCompleter on an
 * executor thread (the async completion pattern).
 *
 * Safe alongside the other native tests in one JVM process: bridge
 * re-registration is last-wins and every test registers its own handlers
 * first (superseded registrations are parked forever by BridgeRegistrar —
 * the POC liveness rule from BridgeProtocolNative.cs).
 */
class ShellBridgeTest {

    private companion object {
        /** Single backslash — escape-sequence fixtures are built from char
         * codes so no literal control chars / unicode escapes hide in this
         * source file. */
        val BS: String = 92.toChar().toString()

        /** U+0001 — a control char the writer must escape as backslash-u0001. */
        val CTRL: String = 1.toChar().toString()
    }

    // ── Struct drift tests ───────────────────────────────────────────────────

    /**
     * BlazorNativeBridgeCallbacks must be 48 bytes on x64 (6 × 8-byte fn
     * pointers); both fetch structs 32 bytes (request: 4 pointers; response:
     * 2 × int + 3 pointers). Mirrors BridgeProtocolNativeTests.cs — if either
     * side drifts, this catches it before pointers read garbage.
     */
    @Test
    fun callbacks_struct_is_48_bytes() {
        // NOTE: Native.getNativeSize(cls) reports POINTER size (8) for
        // non-ByValue Structure classes (struct-by-reference IS a pointer);
        // Structure.size() computes the actual layout.
        assertEquals(
            48, BlazorNativeBridgeCallbacks().size(),
            "BlazorNativeBridgeCallbacks must be 6 × 8-byte fn pointers = 48 bytes; " +
                "FieldOrder drifted from BridgeProtocolNative.cs"
        )
        assertEquals(
            32, BlazorNativeFetchRequest().size(),
            "BlazorNativeFetchRequest must be 4 × 8-byte pointers = 32 bytes"
        )
        assertEquals(
            32, BlazorNativeFetchResponse().size(),
            "BlazorNativeFetchResponse must be 4+4+8+8+8 = 32 bytes"
        )
    }

    // ── In-memory handlers fixture ───────────────────────────────────────────

    /** Route var + HashMap storage; fetch behavior injected per test. */
    private class InMemoryHandlers(
        private val onFetch: (Long, BridgeFetchRequest) -> Unit,
    ) : ShellBridgeHandlers {
        @Volatile private var route: String = "/"
        private val storage = ConcurrentHashMap<String, String>()

        override fun navigate(route: String) { this.route = route }
        override fun currentRoute(): String = route
        override fun storageRead(key: String): String? = storage[key]
        override fun storageWrite(key: String, value: String) { storage[key] = value }
        override fun storageDelete(key: String) { storage.remove(key) }
        override fun fetchBegin(requestId: Long, request: BridgeFetchRequest) =
            onFetch(requestId, request)
    }

    // ── End-to-end probes through the dll ────────────────────────────────────

    /** Minimal localhost HTTP/1.1 responder: fixed 200 + "probe-ok" for every
     * request. Daemon accept-loop thread; close() unblocks it. */
    private class TinyHttpServer : AutoCloseable {
        private val socket = ServerSocket(0, 8, InetAddress.getByName("127.0.0.1"))
        val port: Int get() = socket.localPort

        private val acceptThread = Thread {
            try {
                while (true) {
                    val client = socket.accept()
                    client.use {
                        val reader = it.getInputStream().bufferedReader(Charsets.ISO_8859_1)
                        // Drain the request head (up to the blank line).
                        while (true) {
                            val line = reader.readLine() ?: break
                            if (line.isEmpty()) break
                        }
                        val body = "probe-ok".toByteArray(Charsets.UTF_8)
                        val head = "HTTP/1.1 200 OK\r\n" +
                            "Content-Type: text/plain\r\n" +
                            "Content-Length: ${body.size}\r\n" +
                            "Connection: close\r\n\r\n"
                        val out = it.getOutputStream()
                        out.write(head.toByteArray(Charsets.ISO_8859_1))
                        out.write(body)
                        out.flush()
                    }
                }
            } catch (_: Throwable) {
                // socket closed — normal shutdown
            }
        }.apply { isDaemon = true; start() }

        override fun close() = socket.close()
    }

    @Test
    fun bridge_probes_pass_via_dll() {
        // Local HTTP fixture the probe's fetch will hit (via our handlers).
        val server = TinyHttpServer()
        val executor = Executors.newSingleThreadExecutor()
        try {
            // fetchBegin performs the real HTTP GET on an executor thread and
            // answers via blazornative_fetch_complete — the async completion
            // pattern (the probe's thread is blocked inside the dll awaiting).
            val handlers = InMemoryHandlers { requestId, request ->
                executor.execute {
                    try {
                        val conn = URL(request.url).openConnection() as HttpURLConnection
                        conn.requestMethod = request.method
                        conn.connectTimeout = 5_000
                        conn.readTimeout = 5_000
                        val status = conn.responseCode
                        val body = conn.inputStream.use { it.readBytes().toString(Charsets.UTF_8) }
                        BridgeFetchCompleter.completeSuccess(
                            requestId, status, body = body,
                            headers = mapOf("Content-Type" to "text/plain"),
                        )
                    } catch (t: Throwable) {
                        BridgeFetchCompleter.completeFailure(requestId, t.toString())
                    }
                }
            }

            // Boot through BlazorNativeRuntime so the bridge-before-mount
            // integration path is what's under test.
            val runtime = BlazorNativeRuntime(onFrame = {})
            val lines = runtime.start(platformOs = "test-host", bridge = handlers)
            assertEquals(4, lines.size, "expected 4 [BOOT] lines with a bridge; got $lines")
            assertTrue(
                lines.any { it.contains("shell bridge registered") },
                "missing '[BOOT] shell bridge registered' line in $lines"
            )

            val url = "http://127.0.0.1:${server.port}/probe"
            val result = NativeBindings.INSTANCE
                .blazornative_run_bridge_probes(url.toByteArray(Charsets.UTF_8) + 0)
            val detail = result.errorMessage?.getString(0, "UTF-8") ?: "<null>"
            val label = result.versionString?.getString(0, "UTF-8") ?: "<null>"
            println("[ShellBridgeTest] probes status=${result.status} label='$label' detail='$detail'")

            assertEquals(0, result.status, "bridge probes failed: $detail")
            assertEquals("probes:navigate,storage,fetch", label)
        } finally {
            executor.shutdown()
            assertTrue(
                executor.awaitTermination(5, TimeUnit.SECONDS),
                "fetch executor did not drain within 5 s"
            )
            server.close()
        }
    }

    @Test
    fun bridge_probes_report_fetch_failure() {
        // Handlers whose fetch always completes as a transport error —
        // navigate + storage probes still pass, so exactly ONE probe fails
        // and the detail must name it.
        val handlers = InMemoryHandlers { requestId, _ ->
            BridgeFetchCompleter.completeFailure(
                requestId, "deliberate transport failure (ShellBridgeTest)"
            )
        }
        BridgeRegistrar(handlers) { msg, t -> System.err.println("$msg: $t") }.register()

        val result = NativeBindings.INSTANCE.blazornative_run_bridge_probes(
            "http://127.0.0.1:9/unused".toByteArray(Charsets.UTF_8) + 0
        )
        val detail = result.errorMessage?.getString(0, "UTF-8") ?: "<null>"
        println("[ShellBridgeTest] failure-path probes status=${result.status} detail='$detail'")

        assertEquals(
            1, result.status,
            "expected exactly the fetch probe to fail (navigate + storage pass); detail: $detail"
        )
        assertTrue(detail.contains("fetch:"), "detail must name the fetch probe; got: $detail")
        assertTrue(
            detail.contains("deliberate transport failure"),
            "detail must carry the handler's error message; got: $detail"
        )
    }

    @Test
    fun guarded_handler_throw_surfaces_as_host_error_via_dll() {
        // Proves the full wire path for a throwing handler: Kotlin throw →
        // guarded catch (onError fired) → -1 across the ABI → .NET HostError
        // → probe failure detail. storageRead throws; navigate passes; the
        // fetch probe is satisfied locally (no network) so exactly the
        // storage probe fails.
        val errorSink = AtomicReference<Pair<String, Throwable>>()
        val handlers = object : ShellBridgeHandlers {
            @Volatile private var route = "/"
            override fun navigate(route: String) { this.route = route }
            override fun currentRoute(): String = route
            override fun storageRead(key: String): String? =
                throw IllegalStateException("storageRead boom (ShellBridgeTest)")
            override fun storageWrite(key: String, value: String) {}
            override fun storageDelete(key: String) {}
            override fun fetchBegin(requestId: Long, request: BridgeFetchRequest) {
                BridgeFetchCompleter.completeSuccess(requestId, 200, body = "probe-ok")
            }
        }
        BridgeRegistrar(handlers) { msg, t -> errorSink.set(msg to t) }.register()

        val result = NativeBindings.INSTANCE.blazornative_run_bridge_probes(
            "http://127.0.0.1:9/unused".toByteArray(Charsets.UTF_8) + 0
        )
        val detail = result.errorMessage?.getString(0, "UTF-8") ?: "<null>"
        println("[ShellBridgeTest] guarded-throw probes status=${result.status} detail='$detail'")

        assertEquals(
            1, result.status,
            "expected exactly the storage probe to fail (navigate + fetch pass); detail: $detail"
        )
        assertTrue(detail.contains("storage:"), "detail must name the storage probe; got: $detail")
        assertTrue(
            detail.contains("return code -1"),
            "detail must show the .NET HostError for the guarded -1; got: $detail"
        )

        val captured = errorSink.get()
        assertTrue(captured != null, "onError sink did not fire for the throwing handler")
        assertTrue(
            captured!!.first.contains("storageRead"),
            "onError message should name the storageRead handler; got: ${captured.first}"
        )
        assertEquals("storageRead boom (ShellBridgeTest)", captured.second.message)
    }

    // ── Buffer-write helper (the host half of the buffer protocol) ──────────

    @Test
    fun writeUtf8_buffer_protocol() {
        val buf = Memory(16)

        // Fits: returns bytes + 1 (incl. NUL), NUL present.
        assertEquals(6, BridgeRegistrar.writeUtf8("hello", buf, 16))
        assertEquals("hello", buf.getString(0, "UTF-8"))
        assertEquals(0, buf.getByte(5L).toInt(), "NUL terminator missing")

        // Exact fit is a fit (invariant b: never -needed when it fits).
        assertEquals(6, BridgeRegistrar.writeUtf8("hello", buf, 6))

        // One byte short: -(bytes + 1).
        assertEquals(-6, BridgeRegistrar.writeUtf8("hello", buf, 5))

        // Non-ASCII: "héllo" is 6 UTF-8 BYTES (é = 2), so 7 with NUL.
        val buf2 = Memory(16)
        assertEquals(7, BridgeRegistrar.writeUtf8("héllo", buf2, 16))
        assertEquals("héllo", buf2.getString(0, "UTF-8"))
        assertEquals(-7, BridgeRegistrar.writeUtf8("héllo", buf2, 6))

        // Empty string: the NUL alone (contract minimum for success is 1).
        assertEquals(1, BridgeRegistrar.writeUtf8("", buf, 16))
        assertEquals(0, buf.getByte(0L).toInt())
    }

    // ── Flat headers-JSON writer/parser vs the .NET matrix ──────────────────
    // Mirrors tests/BlazorNative.Runtime.Tests/FlatJsonTests.cs — the content
    // half of the ABI. If either side's escaping drifts, headers corrupt.

    @Test
    fun flat_json_writer_escapes_like_dotnet() {
        // Input: quote, backslash, LF, U+0001. The .NET writer produces
        // exactly: open-brace "k": quote backslash-quote backslash-backslash
        // backslash-n backslash-u0001 quote close-brace
        val input = "\"" + BS + "\n" + CTRL
        val expected = "{\"k\":\"" + BS + "\"" + BS + BS + BS + "n" + BS + "u0001\"}"
        assertEquals(expected, FlatJson.write(mapOf("k" to input)))

        assertEquals("{}", FlatJson.write(emptyMap()))

        // \r and \t use the short escapes; emoji passes through raw.
        assertEquals(
            "{\"k\":\"a" + BS + "r" + BS + "tb\"}",
            FlatJson.write(mapOf("k" to "a\r\tb"))
        )
        assertEquals("{\"X-Emoji\":\"🎉\"}", FlatJson.write(mapOf("X-Emoji" to "🎉")))
    }

    @Test
    fun headers_flat_json_round_trips_with_dotnet_matrix() {
        // The FlatJsonTests.cs WriteThenParse_RoundTrips matrix, verbatim.
        val matrix = mapOf(
            "plain" to "value",
            "quote" to "say \"hi\"",
            "backslash" to ("C:" + BS + "temp" + BS + "x"),
            "newline" to "line1\nline2",
            "carriage-return-tab" to "a\r\tb",
            "control-char" to ("pre" + CTRL + "post"),
            "non-ascii" to "héllo wörld — æøå 日本語",
            "emoji-surrogate-pair" to "party 🎉 face 😀",
            "empty-value" to "",
        )
        for ((key, value) in matrix) {
            val parsed = FlatJson.parse(FlatJson.write(mapOf(key to value)))
            assertEquals(value, parsed[key], "round-trip broke for key '$key'")
            assertEquals(1, parsed.size)
        }

        // Multi-pair map survives whole.
        val multi = mapOf(
            "Content-Type" to "text/plain; charset=\"utf-8\"",
            "X-Path" to ("a" + BS + "b\nc"),
            "X-Emoji" to "🎉",
        )
        val parsedMulti = FlatJson.parse(FlatJson.write(multi))
        assertEquals(3, parsedMulti.size)
        for ((key, value) in multi) assertEquals(value, parsedMulti[key])
    }

    @Test
    fun flat_json_parser_matches_dotnet_semantics() {
        // Literal six-char backslash-uXXXX sequences must decode, incl.
        // surrogate-pair escapes → emoji (mirror of Parse_UnicodeEscape_Decodes
        // + Parse_SurrogatePairEscapes_DecodeToEmoji).
        val unicodeJson = "{\"k\":\"" + BS + "u0041" + BS + "u00e9\"}"
        assertEquals("A" + 0xE9.toChar(), FlatJson.parse(unicodeJson)["k"])

        val surrogateJson = "{\"k\":\"" + BS + "ud83c" + BS + "udf89\"}"
        assertEquals(String(Character.toChars(0x1F389)), FlatJson.parse(surrogateJson)["k"])

        // Whitespace tolerated between tokens.
        val ws = FlatJson.parse("  { \"a\" : \"b\" ,\n\t\"c\" : \"d\" }  ")
        assertEquals("b", ws["a"])
        assertEquals("d", ws["c"])

        // null / empty / empty object → empty map.
        assertTrue(FlatJson.parse(null).isEmpty())
        assertTrue(FlatJson.parse("").isEmpty())
        assertTrue(FlatJson.parse("{}").isEmpty())
        assertTrue(FlatJson.parse("  { }  ").isEmpty())

        // Malformed inputs throw (mirror of the .NET FormatException matrix)
        // and never leak the payload into the message.
        val malformed = listOf(
            "not json",                                  // no object at all
            "{",                                         // truncated after brace
            "{\"a\"}",                                   // missing colon + value
            "{\"a\":1}",                                 // non-string value
            "{'a':'b'}",                                 // single quotes
            "{\"a\":\"b\",}",                            // trailing comma
            "{\"a\":\"b\" \"c\":\"d\"}",                 // missing comma
            "{\"a\":\"b\"",                              // unterminated object
            "{\"a\":\"b" + BS,                           // dangling escape at end
            "{\"k\":\"" + BS + "x41\"}",                 // unknown escape
            "{\"k\":\"" + BS + "u12\"}",                 // short hex run
            "{\"k\":\"" + BS + "u12G4\"}",               // non-hex digit
            "{\"k\":\"" + BS + "u 123\"}",               // whitespace in hex (strictness)
            "{\"k\":\"" + BS + "u+123\"}",               // sign in hex (strictness)
        )
        for (json in malformed) {
            assertThrows(IllegalArgumentException::class.java, { FlatJson.parse(json) }, "should reject: $json")
        }

        val leaky = "{\"padding-padding-padding-padding\":\"SECRET-TOKEN-VALUE\""
        val ex = assertThrows(IllegalArgumentException::class.java) { FlatJson.parse(leaky) }
        assertFalse(ex.message!!.contains("SECRET-TOKEN-VALUE"), "error message leaked the payload")
        assertTrue(ex.message!!.contains("index"), "error message should carry the failing index")
    }
}
