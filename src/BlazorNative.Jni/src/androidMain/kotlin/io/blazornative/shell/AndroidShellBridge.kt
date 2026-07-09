package io.blazornative.shell

import android.content.Context
import android.content.SharedPreferences
import android.util.Log
import io.blazornative.jni.BridgeFetchCompleter
import io.blazornative.jni.BridgeFetchRequest
import io.blazornative.jni.ShellBridgeHandlers
import java.net.HttpURLConnection
import java.net.URL
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors

/**
 * Phase 3.1 Gate 3 — the Android implementation of [ShellBridgeHandlers]:
 * route = in-memory var + logcat line, storage = SharedPreferences
 * ("blazornative"), fetch = HttpURLConnection on a single background executor
 * completed via [BridgeFetchCompleter] (the async completion pattern).
 *
 * THREADING (contract on [ShellBridgeHandlers]): the five sync handlers are
 * called with the .NET runtime BLOCKED on them — they stay
 * SharedPreferences-grade fast. Only fetch does real I/O, and it happens on
 * [fetchExecutor] AFTER fetchBegin returned.
 */
class AndroidShellBridge(
    context: Context,
    private val onError: (String, Throwable) -> Unit,
) : ShellBridgeHandlers {

    /**
     * applicationContext ONLY — never the incoming [context] itself (which
     * MainActivity passes as an Activity): this instance is transitively
     * retained for the PROCESS LIFETIME by BridgeRegistrar's park list (the
     * retention contract on [ShellBridgeHandlers]), so retaining an Activity
     * here would leak its entire view hierarchy on every recreation.
     */
    private val appContext: Context = context.applicationContext

    // ── Route ────────────────────────────────────────────────────────────────

    @Volatile private var route: String = "/"

    override fun navigate(route: String) {
        this.route = route
        Log.i(TAG, "[bridge] navigate → $route")
    }

    override fun currentRoute(): String = route

    // ── Storage — SharedPreferences("blazornative") ──────────────────────────

    private val prefs: SharedPreferences =
        appContext.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)

    override fun storageRead(key: String): String? = prefs.getString(key, null)

    // commit() (synchronous), NOT apply() (async write-behind): the sync
    // handler contract is completed-when-returned — the .NET runtime treats a
    // returned storageWrite as durable, and apply() would hand it a success
    // for a write still sitting in a pending queue. commit()'s Boolean also
    // lets a persistence failure surface as -1 (via the guarded throw) instead
    // of vanishing.
    override fun storageWrite(key: String, value: String) {
        check(prefs.edit().putString(key, value).commit()) {
            "SharedPreferences commit failed for storageWrite('$key')"
        }
    }

    override fun storageDelete(key: String) {
        check(prefs.edit().remove(key).commit()) {
            "SharedPreferences commit failed for storageDelete('$key')"
        }
    }

    // ── Fetch — HttpURLConnection on a single background thread ──────────────

    /** Single daemon thread: fetches complete in FIFO order; a hung request
     * can't pile up threads (POC posture — one in-flight fetch at a time). */
    private val fetchExecutor: ExecutorService = Executors.newSingleThreadExecutor { r ->
        Thread(r, "BlazorNative-Fetch").apply { isDaemon = true }
    }

    override fun fetchBegin(requestId: Long, request: BridgeFetchRequest) {
        // Returns immediately (fetchBegin contract); the request is already a
        // fully-detached copy, safe to use from the executor thread.
        fetchExecutor.execute { performFetch(requestId, request) }
    }

    private fun performFetch(requestId: Long, request: BridgeFetchRequest) {
        try {
            val conn = URL(request.url).openConnection() as HttpURLConnection
            try {
                conn.connectTimeout = 10_000
                conn.readTimeout = 10_000
                conn.requestMethod = request.method
                for ((name, value) in request.headers) conn.setRequestProperty(name, value)
                request.body?.let { body ->
                    conn.doOutput = true
                    conn.outputStream.use { it.write(body.toByteArray(Charsets.UTF_8)) }
                }

                val status = conn.responseCode
                // HTTP 4xx/5xx are SUCCESS (the transport worked; the status
                // code IS the answer) — but HttpURLConnection throws from
                // inputStream for them; the body lives on errorStream.
                val stream = if (status >= 400) conn.errorStream else conn.inputStream
                val body = stream?.use { it.readBytes().toString(Charsets.UTF_8) } ?: ""

                // First value per header name suffices for the flat-JSON ABI;
                // the null key is the HTTP status line, not a header.
                val headers = LinkedHashMap<String, String>()
                for ((name, values) in conn.headerFields) {
                    if (name == null) continue
                    values.firstOrNull()?.let { headers[name] = it }
                }

                BridgeFetchCompleter.completeSuccess(requestId, status, body, headers)
            } finally {
                conn.disconnect()
            }
        } catch (t: Throwable) {
            // Transport failure (DNS/connect/timeout/policy) — surfaces
            // .NET-side as an HttpRequestException carrying this message.
            onError("shell bridge fetch failed (completed request $requestId as failure)", t)
            BridgeFetchCompleter.completeFailure(
                requestId, "${request.method} ${request.url} failed: $t"
            )
        }
    }

    private companion object {
        const val TAG = "BlazorNative"
        const val PREFS_NAME = "blazornative"
    }
}
