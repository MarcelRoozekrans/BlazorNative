package io.blazornative.jni

import com.sun.jna.Memory
import com.sun.jna.Pointer
import java.lang.ref.Reference
import java.util.TreeMap

/**
 * Phase 3.1 — the Kotlin half of the shell-bridge C-ABI: the host implements
 * [ShellBridgeHandlers], a [BridgeRegistrar] adapts it into the six cdecl
 * callbacks BridgeProtocolNative.cs defines and registers them via
 * `blazornative_register_bridge`, and async fetches are answered through
 * [BridgeFetchCompleter] → `blazornative_fetch_complete`.
 */

/**
 * A fully-copied fetch request (every string detached from the .NET-owned
 * native memory before the FetchBegin callback returned).
 *
 * @property body NULL body pointer decodes to null (no body).
 * @property headers empty when the native HeadersJson pointer was NULL.
 *   Lookups are CASE-INSENSITIVE (header names per RFC 9110 — backed by a
 *   TreeMap with a case-insensitive comparer, same choice as the .NET side).
 */
data class BridgeFetchRequest(
    val url: String,
    val method: String,
    val body: String?,
    val headers: Map<String, String>,
)

/**
 * The six shell operations a host implements for the .NET runtime
 * (IMobileBridge's native backend). Registered once at boot via
 * [BridgeRegistrar.register] — BEFORE `blazornative_mount`, so components
 * resolving IMobileBridge find a live host.
 *
 * PROCESS-LIFETIME RETENTION: the registrar's callbacks capture this
 * handlers instance, and every registration is parked for the PROCESS
 * lifetime — never released (the liveness rule in BridgeProtocolNative.cs).
 * Android implementers must therefore capture `applicationContext` (or other
 * process-scoped objects) ONLY — never an Activity, View, or Fragment: a
 * retained Activity would leak its whole view hierarchy and LeakCanary would
 * flag every rotation.
 *
 * THREADING: calls arrive on whatever thread the .NET runtime happens to be
 * running the bridge operation on (the mount thread today; any thread once
 * Phase 3.2 wires events) — implementations must be thread-safe. The runtime
 * BLOCKS SYNCHRONOUSLY on the five non-fetch handlers (navigate,
 * currentRoute, storageRead/Write/Delete) — keep them fast (in-memory /
 * SharedPreferences-grade work, no network or long I/O waits); only
 * [fetchBegin] has the async escape hatch.
 *
 * EXCEPTION POSTURE: throwing from any handler is caught by the registrar's
 * callback wrapper, routed to its onError sink, and surfaces .NET-side as
 * return code -1 (host error) — it never crosses the ABI raw. [storageRead]
 * returning null maps to -2 (key absent) → null .NET-side; that is NOT an
 * error.
 *
 * FETCH: [fetchBegin] must return quickly — perform the HTTP work on your own
 * executor/dispatcher and complete the request LATER by calling
 * [BridgeFetchCompleter.completeSuccess] / [BridgeFetchCompleter.completeFailure]
 * with the same requestId (the async completion pattern; completing
 * synchronously from inside fetchBegin is also legal). Every string in
 * [BridgeFetchRequest] is already copied — safe to use from any thread after
 * fetchBegin returns.
 */
interface ShellBridgeHandlers {
    /** The runtime asks the shell to show [route]. */
    fun navigate(route: String)

    /** Current shell route (round-tripped by the navigate probe). */
    fun currentRoute(): String

    /** Value for [key], or null when absent (maps to -2 / null, not an error). */
    fun storageRead(key: String): String?

    fun storageWrite(key: String, value: String)

    fun storageDelete(key: String)

    /** Begin an async fetch; answer later via [BridgeFetchCompleter]. */
    fun fetchBegin(requestId: Long, request: BridgeFetchRequest)
}

/**
 * Adapts a [ShellBridgeHandlers] into the six JNA callbacks and registers
 * them with the runtime.
 *
 * LIFETIME (the frame-callback GC rule, ×6): the callback objects are held as
 * FIELDS of this class — JNA callback trampolines are GC-eligible once the
 * Kotlin object is unreachable, after which the native side would invoke a
 * dangling pointer. [register] additionally parks this registrar in a
 * process-lifetime list: re-registration is last-wins, but an in-flight .NET
 * operation may still be invoking the PREVIOUS registration's pointers, so
 * superseded registrations are never released (the POC liveness rule from
 * BridgeProtocolNative.cs). That park list transitively retains [handlers]
 * for the PROCESS LIFETIME — see the retention warning on
 * [ShellBridgeHandlers]: hand this class application-scoped objects only.
 *
 * RETURN CODES produced here (the host half of the protocol):
 *   >= 0     success (buffer callbacks: bytes written incl. NUL)
 *   -needed  value doesn't fit in cap — |value| = exact bytes incl. NUL,
 *            returned ONLY when it genuinely doesn't fit (invariant b)
 *   -1       handler threw (routed to [onError] first)
 *   -2       storageRead: key absent
 */
class BridgeRegistrar(
    private val handlers: ShellBridgeHandlers,
    // (JVM-friendly default is deliberate NOT provided: callers choose the
    // sink — Android must pass android.util.Log, stderr is /dev/null there.)
    private val onError: (String, Throwable) -> Unit,
) {

    // ── The six callbacks — STRONG refs as fields (GC rule above) ───────────

    // internal (not private) purely so the guarded-catch wire-leg test can
    // invoke it directly — the ABI's exception posture (handler throw →
    // guarded() → onError → -1, never a raw throw into JNA's default handler)
    // must stay pinned without the deleted probe export. Same test-surface
    // posture as writeUtf8 below / HostSession.ReplaceRegistryEntryForTests.
    internal val navigateCallback = object : NativeBindings.BridgeNavigateCallback {
        override fun invoke(routeUtf8: Pointer): Int = guarded("navigate") {
            handlers.navigate(routeUtf8.getString(0, "UTF-8")) // getString copies
            0
        }
    }

    private val currentRouteCallback = object : NativeBindings.BridgeCurrentRouteCallback {
        override fun invoke(buf: Pointer, cap: Int): Int = guarded("currentRoute") {
            writeUtf8(handlers.currentRoute(), buf, cap)
        }
    }

    private val storageReadCallback = object : NativeBindings.BridgeStorageReadCallback {
        override fun invoke(keyUtf8: Pointer, buf: Pointer, cap: Int): Int = guarded("storageRead") {
            val value = handlers.storageRead(keyUtf8.getString(0, "UTF-8"))
                ?: return@guarded KEY_ABSENT
            writeUtf8(value, buf, cap)
        }
    }

    private val storageWriteCallback = object : NativeBindings.BridgeStorageWriteCallback {
        override fun invoke(keyUtf8: Pointer, valueUtf8: Pointer): Int = guarded("storageWrite") {
            handlers.storageWrite(keyUtf8.getString(0, "UTF-8"), valueUtf8.getString(0, "UTF-8"))
            0
        }
    }

    private val storageDeleteCallback = object : NativeBindings.BridgeStorageDeleteCallback {
        override fun invoke(keyUtf8: Pointer): Int = guarded("storageDelete") {
            handlers.storageDelete(keyUtf8.getString(0, "UTF-8"))
            0
        }
    }

    private val fetchBeginCallback = object : NativeBindings.BridgeFetchBeginCallback {
        override fun invoke(requestId: Long, request: Pointer): Int = guarded("fetchBegin") {
            // The request struct + strings are .NET-owned and valid ONLY
            // during this call — decode into a fully-detached copy NOW.
            val native = BlazorNativeFetchRequest(request).also { it.read() }
            val copied = BridgeFetchRequest(
                url = native.url?.getString(0, "UTF-8")
                    ?: error("FetchBegin: Url was NULL (ABI contract: never NULL)"),
                method = native.method?.getString(0, "UTF-8")
                    ?: error("FetchBegin: Method was NULL (ABI contract: never NULL)"),
                body = native.body?.getString(0, "UTF-8"),
                headers = FlatJson.parse(native.headersJson?.getString(0, "UTF-8")),
            )
            handlers.fetchBegin(requestId, copied)
            0
        }
    }

    /**
     * Wraps every callback body: a Kotlin throw must surface as -1, never
     * escape into JNA's default callback exception handler (which prints to
     * stderr and hands native a garbage default 0 — a silent fake SUCCESS).
     */
    private inline fun guarded(op: String, body: () -> Int): Int =
        try {
            body()
        } catch (t: Throwable) {
            onError("shell bridge '$op' handler threw (returned -1 to the runtime)", t)
            HOST_ERROR
        }

    /** Set once by [register] — guards accidental double-registration. */
    private var registered = false

    /**
     * Builds the callbacks struct and registers it with the runtime. The
     * struct memory is copied by the export (freeing it afterwards is fine);
     * `this` is parked in a process-lifetime list so the trampolines stay
     * alive forever. ONE-SHOT: a registrar registers once (each registration
     * is retained forever — re-registering the same instance would only park
     * duplicates); construct a fresh BridgeRegistrar to swap handlers.
     * Throws [IllegalStateException] on double-registration or a non-zero
     * status.
     */
    fun register() {
        check(!registered) {
            "BridgeRegistrar.register() called twice on the same instance — " +
                "registrations are one-shot and retained for the process lifetime; " +
                "construct a new BridgeRegistrar to register different handlers"
        }
        val struct = BlazorNativeBridgeCallbacks().apply {
            navigate = navigateCallback
            currentRoute = currentRouteCallback
            storageRead = storageReadCallback
            storageWrite = storageWriteCallback
            storageDelete = storageDeleteCallback
            fetchBegin = fetchBeginCallback
        }
        val rc = NativeBindings.INSTANCE.blazornative_register_bridge(struct)
        check(rc == 0) { "blazornative_register_bridge failed with status $rc" }
        registered = true
        synchronized(registeredForever) { registeredForever.add(this) }
    }

    companion object {
        private const val HOST_ERROR = -1
        private const val KEY_ABSENT = -2

        /** Every registrar that ever registered — never released (see class KDoc). */
        private val registeredForever = mutableListOf<BridgeRegistrar>()

        /**
         * The shared buffer-write helper (host half of the buffer protocol):
         * UTF-8-encode [value]; when bytes + 1 (NUL) fits in [cap], write
         * bytes + NUL and return bytes + 1; otherwise write NOTHING and
         * return -(bytes + 1). Never returns -needed for a value that fits
         * (invariant b in BridgeProtocolNative.cs).
         */
        internal fun writeUtf8(value: String, buf: Pointer, cap: Int): Int {
            val bytes = value.toByteArray(Charsets.UTF_8)
            val needed = bytes.size + 1
            if (needed > cap) return -needed
            if (bytes.isNotEmpty()) buf.write(0, bytes, 0, bytes.size)
            buf.setByte(bytes.size.toLong(), 0)
            return needed
        }
    }
}

/**
 * Answers an async fetch started by [ShellBridgeHandlers.fetchBegin] through
 * `blazornative_fetch_complete`, marshalling the response as JNA
 * Memory-backed UTF-8 strings.
 *
 * LIFETIME: the response struct + strings are host-owned and must stay valid
 * ONLY for the duration of the export call — the Memory objects live in
 * locals here and are pinned across the call with reachabilityFence, then
 * become collectible (.NET copied everything before returning).
 */
object BridgeFetchCompleter {

    // Two explicit factories instead of one flag-coupled positional call —
    // this shape is what the M4 Swift mirror copies, so the ok/errorMessage
    // coupling must not leak into the public API.

    /**
     * Delivers a successful HTTP outcome for [requestId] (any status code the
     * server actually answered with, including 4xx/5xx — "success" means the
     * transport worked).
     *
     * @param status HTTP status code.
     * @param body response body; null = empty body (NULL pointer).
     * @param headers response headers; an empty map crosses as a NULL
     *   HeadersJson pointer (not "{}"), matching the request-side convention.
     * @return the export's return code: 0 = delivered; 1 = unknown /
     *   already-completed id (benign cancellation race — ignored); 2 =
     *   invalid call / internal bridge failure (logged LOUDLY here; detail
     *   lands on the runtime's stderr).
     */
    fun completeSuccess(
        requestId: Long,
        status: Int,
        body: String? = null,
        headers: Map<String, String> = emptyMap(),
    ): Int = completeCore(requestId, status, ok = true, body = body, errorMessage = null, headers = headers)

    /**
     * Delivers a transport failure for [requestId] (DNS/connect/timeout —
     * the request never produced an HTTP response). Surfaces .NET-side as an
     * HttpRequestException carrying [errorMessage].
     *
     * @return same return-code table as [completeSuccess].
     */
    fun completeFailure(requestId: Long, errorMessage: String): Int =
        completeCore(requestId, status = 0, ok = false, body = null, errorMessage = errorMessage, headers = emptyMap())

    /** Shared marshalling core — see the class KDoc for the lifetime rule. */
    private fun completeCore(
        requestId: Long,
        status: Int,
        ok: Boolean,
        body: String?,
        errorMessage: String?,
        headers: Map<String, String>,
    ): Int {
        // Locals keep the Memory objects strongly reachable across the call.
        val bodyMem = body?.let(::utf8CString)
        val errorMem = errorMessage?.let(::utf8CString)
        val headersMem = if (headers.isEmpty()) null else utf8CString(FlatJson.write(headers))

        val response = BlazorNativeFetchResponse.ByReference().apply {
            statusCode = status
            this.ok = if (ok) 1 else 0
            bodyUtf8 = bodyMem
            this.errorMessage = errorMem
            headersJson = headersMem
        }

        val rc = try {
            NativeBindings.INSTANCE.blazornative_fetch_complete(requestId, response)
        } finally {
            // Pin the host-owned memory until the export has returned —
            // without these the JIT may deem the locals dead mid-call.
            Reference.reachabilityFence(response)
            if (bodyMem != null) Reference.reachabilityFence(bodyMem)
            if (errorMem != null) Reference.reachabilityFence(errorMem)
            if (headersMem != null) Reference.reachabilityFence(headersMem)
        }

        when (rc) {
            0 -> {} // delivered
            1 -> {} // unknown/already-completed id — benign cancellation race, ignore
            else -> System.err.println(
                "[BridgeFetchCompleter] blazornative_fetch_complete($requestId) returned $rc — " +
                    "invalid call or internal bridge failure; check the runtime's stderr for detail"
            )
        }
        return rc
    }

    /** Caller-owned NUL-terminated UTF-8 cstring. */
    private fun utf8CString(s: String): Memory {
        val bytes = s.toByteArray(Charsets.UTF_8) + 0
        return Memory(bytes.size.toLong()).apply { write(0, bytes, 0, bytes.size) }
    }
}

/**
 * The flat headers-JSON that crosses the bridge ABI — a single
 * `{"string":"string",...}` object, hand-rolled (no kotlinx) as the exact
 * mirror of NativeShellBridge.WriteFlatJsonObject / ParseFlatJsonObject.
 * Content contract (both sides assert the same matrix — FlatJsonTests.cs /
 * ShellBridgeTest.kt): the writer escapes quote, backslash, \n \r \t as the
 * short escapes and every other char below U+0020 as a lowercase 4-hex-digit
 * unicode escape; everything else (incl. non-ASCII + surrogate pairs) passes
 * through raw. The parser additionally accepts the standard short escapes,
 * slash, and strict 4-hex-digit unicode escapes.
 */
internal object FlatJson {

    fun write(map: Map<String, String>): String {
        val sb = StringBuilder(16 + map.size * 24)
        sb.append('{')
        var first = true
        for ((key, value) in map) {
            if (!first) sb.append(',')
            first = false
            appendJsonString(sb, key)
            sb.append(':')
            appendJsonString(sb, value)
        }
        sb.append('}')
        return sb.toString()
    }

    /**
     * Parses a flat JSON object; null/blank input yields an empty map (the
     * NULL-pointer = no-headers convention). Header names are
     * case-insensitive per RFC 9110 — same comparer choice as the .NET side.
     * Throws [IllegalArgumentException] on malformed input; the message
     * carries only the failing index + a 32-char prefix (header values may
     * hold Set-Cookie/Authorization material that must not leak into logs).
     */
    fun parse(json: String?): Map<String, String> {
        val result: MutableMap<String, String> = TreeMap(String.CASE_INSENSITIVE_ORDER)
        if (json.isNullOrBlank()) return result
        Parser(json).parseInto(result)
        return result
    }

    // internal (not private) since Phase 4.4: InspectorJson delegates here so
    // the inspector's JSON surface shares this exact escaping contract instead
    // of growing a drifting copy.
    internal fun appendJsonString(sb: StringBuilder, value: String) {
        sb.append('"')
        for (c in value) {
            when (c) {
                '"' -> sb.append("\\\"")
                '\\' -> sb.append("\\\\")
                '\n' -> sb.append("\\n")
                '\r' -> sb.append("\\r")
                '\t' -> sb.append("\\t")
                else ->
                    if (c < ' ') sb.append("\\u").append(String.format("%04x", c.code))
                    else sb.append(c)
            }
        }
        sb.append('"')
    }

    private class Parser(private val json: String) {
        private var i = 0

        fun parseInto(result: MutableMap<String, String>) {
            skipWhitespace()
            expect('{')
            skipWhitespace()
            if (i < json.length && json[i] == '}') return
            while (true) {
                skipWhitespace()
                val key = parseString()
                skipWhitespace()
                expect(':')
                skipWhitespace()
                val value = parseString()
                result[key] = value
                skipWhitespace()
                if (i >= json.length) throw malformed(i)
                val c = json[i++]
                if (c == '}') return
                if (c != ',') throw malformed(i - 1)
            }
        }

        private fun parseString(): String {
            expect('"')
            val sb = StringBuilder()
            while (true) {
                if (i >= json.length) throw malformed(i)
                val c = json[i++]
                if (c == '"') return sb.toString()
                if (c != '\\') {
                    sb.append(c)
                    continue
                }
                if (i >= json.length) throw malformed(i)
                when (json[i++]) {
                    '"' -> sb.append('"')
                    '\\' -> sb.append('\\')
                    '/' -> sb.append('/')
                    'n' -> sb.append('\n')
                    'r' -> sb.append('\r')
                    't' -> sb.append('\t')
                    'b' -> sb.append('\b')
                    'f' -> sb.append(12.toChar()) // form feed — Kotlin has no \f escape
                    'u' -> sb.append(parseHex4())
                    else -> throw malformed(i - 1)
                }
            }
        }

        /** Strict 4-hex-digit run: no sign/whitespace leniency (mirror of the
         * .NET ParseHex4 strictness note). */
        private fun parseHex4(): Char {
            if (i + 4 > json.length) throw malformed(i)
            var value = 0
            for (k in 0 until 4) {
                val c = json[i + k]
                val digit = when (c) {
                    in '0'..'9' -> c - '0'
                    in 'a'..'f' -> c - 'a' + 10
                    in 'A'..'F' -> c - 'A' + 10
                    else -> throw malformed(i + k)
                }
                value = (value shl 4) or digit
            }
            i += 4
            return value.toChar()
        }

        private fun skipWhitespace() {
            while (i < json.length && json[i].isWhitespace()) i++
        }

        private fun expect(expected: Char) {
            if (i >= json.length || json[i] != expected) throw malformed(i)
            i++
        }

        /** Deliberately does NOT embed the full JSON — see [parse]. */
        private fun malformed(index: Int): IllegalArgumentException {
            val prefix = if (json.length <= 32) json else json.substring(0, 32) + "…"
            return IllegalArgumentException(
                "malformed flat JSON object from the shell bridge at index $index (prefix: '$prefix')"
            )
        }
    }
}
