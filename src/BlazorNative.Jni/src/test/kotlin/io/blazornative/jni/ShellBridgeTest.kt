package io.blazornative.jni

import com.sun.jna.Memory
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNotNull
import org.junit.jupiter.api.Assertions.assertThrows
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.util.concurrent.atomic.AtomicReference

/**
 * Phase 3.1 Gate 2 — the Kotlin half of the shell-bridge C-ABI: struct-layout
 * drift pins, the guarded-catch exception posture, the writeUtf8 buffer
 * protocol, and FlatJson writer/parser parity with the .NET side.
 *
 * Phase 3.5 (M3 close): the end-to-end probe tests that drove
 * blazornative_run_bridge_probes through the dll were deleted with that
 * export — the production bridge path (register_bridge → real components
 * navigating/fetching) is covered by NavigationTest/BnDemoTest via
 * BlazorNativeRuntime.start(bridge = …). The guarded-catch wire leg they
 * covered is pinned here (guarded_callback_maps_throw_to_host_error) with
 * its .NET counterpart in NativeShellBridgeTests.cs
 * (FetchBegin_HostErrorReturnCode_ThrowsHostError).
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

    // ── Guarded-catch exception posture (the ABI's -1 wire leg) ─────────────

    /**
     * The Kotlin leg of the ABI's exception posture: a throwing handler must
     * be caught by BridgeRegistrar's guarded() wrapper, routed to the onError
     * sink, and returned as -1 (HOST_ERROR) — never escape raw into JNA's
     * default callback handler (which would hand native a garbage 0
     * "success"). Invokes the callback object directly — no dll registration
     * needed, the wrapper IS the unit under test. The .NET leg (-1 →
     * HostError InvalidOperationException) is pinned by
     * NativeShellBridgeTests.FetchBegin_HostErrorReturnCode_ThrowsHostError.
     */
    @Test
    fun guarded_callback_maps_throw_to_host_error() {
        val errorSink = AtomicReference<Pair<String, Throwable>>()
        val handlers = object : ShellBridgeHandlers {
            override fun navigate(route: String) =
                throw IllegalStateException("navigate boom (ShellBridgeTest)")
            override fun currentRoute(): String = "/"
            override fun storageRead(key: String): String? = null
            override fun storageWrite(key: String, value: String) {}
            override fun storageDelete(key: String) {}
            override fun fetchBegin(requestId: Long, request: BridgeFetchRequest) {}
        }
        // NOT registered — the guarded() wrapper is exercised directly on the
        // callback object (registration/park semantics are BridgeRegistrar
        // .register()'s separate concern).
        val registrar = BridgeRegistrar(handlers) { msg, t -> errorSink.set(msg to t) }

        val routeBytes = "/x".toByteArray(Charsets.UTF_8) + 0
        val routeMem = Memory(routeBytes.size.toLong())
            .apply { write(0, routeBytes, 0, routeBytes.size) }
        val rc = registrar.navigateCallback.invoke(routeMem)

        assertEquals(-1, rc, "a throwing handler must surface as -1 (HOST_ERROR)")
        val captured = errorSink.get()
        assertNotNull(captured, "onError sink did not fire for the throwing handler")
        assertTrue(
            captured.first.contains("navigate"),
            "onError message should name the navigate handler; got: ${captured.first}"
        )
        assertEquals("navigate boom (ShellBridgeTest)", captured.second.message)
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
