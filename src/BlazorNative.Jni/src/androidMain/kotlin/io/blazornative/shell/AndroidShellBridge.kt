package io.blazornative.shell

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
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
    // Phase 5.1 (M5 DoD #5): the startup route slot for deep links. MainActivity
    // parses a VIEW-intent `blazornative://<route>` and passes it here BEFORE
    // boot, so the .NET nav manager's QueryStartupRoute (which calls
    // currentRoute()) reads it and the 3.5 startup-honor mounts the linked
    // page. Default "/" = normal launch. NOT a navigation — just the seed the
    // first mount resolves against (design §4). BEFORE onError so the existing
    // trailing-lambda callers (AndroidShellBridge(ctx) { … }) keep binding the
    // lambda to onError.
    initialRoute: String = "/",
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

    @Volatile private var route: String = initialRoute

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
    //
    // KNOWN SEMANTICS (POC posture — the M4 Swift mirror must match these):
    //  (a) TEXT BODIES ONLY: request and response bodies cross the ABI as
    //      UTF-8 STRINGS — a binary response (image, gzip, protobuf) is
    //      corrupted by the bytes→String decode. By design until the ABI
    //      grows a byte-array lane.
    //  (b) TRANSPARENT GZIP: Android's HttpURLConnection adds
    //      "Accept-Encoding: gzip" and decompresses transparently — but ONLY
    //      when the caller did NOT set Accept-Encoding itself. Forwarding a
    //      caller Accept-Encoding header disables that, so the raw gzip
    //      bytes then hit (a) as mojibake. Forwarded response
    //      Content-Length / Content-Encoding may likewise describe the WIRE
    //      body, not the decoded string we return.
    //  (c) HttpURLConnection QUIRKS: PATCH is rejected by setRequestMethod
    //      (ProtocolException → surfaces as a transport failure, not HTTP);
    //      a GET with a body silently becomes POST (doOutput flips the
    //      method); redirects are followed within a scheme but never cross
    //      http↔https (the 3xx comes back as the response instead).
    //  (d) MULTI-VALUE RESPONSE HEADERS collapse to a single value — the
    //      flat-JSON ABI is Map<String,String>; note headerFields lists a
    //      name's values in REVERSE arrival order, so "first" is the
    //      LAST-received value.

    /** Single daemon thread: fetches complete in FIFO order; a hung request
     * can't pile up threads (POC posture — one in-flight fetch at a time).
     * Worst-case stall for queued fetches: [CONNECT_TIMEOUT_MS] to connect
     * plus [READ_TIMEOUT_MS] per read() — a slow-drip server feeding a byte
     * every just-under-10-s can hold this thread far beyond 20 s. */
    private val fetchExecutor: ExecutorService = Executors.newSingleThreadExecutor { r ->
        Thread(r, "BlazorNative-Fetch").apply { isDaemon = true }
    }

    override fun fetchBegin(requestId: Long, request: BridgeFetchRequest) {
        // Returns immediately (fetchBegin contract); the request is already a
        // fully-detached copy, safe to use from the executor thread.
        fetchExecutor.execute { performFetch(requestId, request) }
    }

    // ── Clipboard + Share (Phase 5.4 Gate 2 — real Android backends) ──────────
    //
    // Clipboard → the system ClipboardManager (appContext service). read/write run
    // on the .NET dispatch lane (the sync-handler contract) — get/setPrimaryClip
    // are Binder calls that need no Looper (we register no clip listener, which is
    // the only ClipboardManager path that would). Android 10+ only returns clip
    // data to the FOREGROUND app; the shell reads while its own Activity has focus,
    // so this holds. The buffer -needed protocol is applied by BridgeRegistrar's
    // writeUtf8 wrapper — the handler just returns the String (the storageRead twin).
    //
    // Share → an ACTION_SEND text/plain Intent (built by [buildShareIntent], the
    // test seam) wrapped in a chooser and launched from appContext with
    // FLAG_ACTIVITY_NEW_TASK: appContext is NOT an Activity (the retention rule —
    // ShellBridgeHandlers KDoc — forbids retaining one), and starting an Activity
    // from a non-Activity context REQUIRES NEW_TASK. Caveat: with no source
    // Activity the chooser opens as its own task (acceptable for the POC share
    // affordance). [shareLaunchHook], when set (instrumented test only), captures
    // the built Intent and SKIPS the launch so the test asserts the Intent without
    // popping the system share sheet.

    private val clipboardManager: ClipboardManager by lazy {
        appContext.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
    }

    override fun clipboardRead(): String =
        clipboardManager.primaryClip?.takeIf { it.itemCount > 0 }?.getItemAt(0)?.text?.toString() ?: ""

    override fun clipboardWrite(text: String) {
        clipboardManager.setPrimaryClip(ClipData.newPlainText(CLIP_LABEL, text))
    }

    override fun share(text: String) {
        val send = buildShareIntent(text)
        shareLaunchHook?.let { it(send); return } // test seam — assert, do not launch
        appContext.startActivity(
            Intent.createChooser(send, null).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        )
    }

    /** The ACTION_SEND text/plain share Intent (the seam the instrumented test
     * asserts — EXTRA_TEXT + type — without launching the chooser). */
    internal fun buildShareIntent(text: String): Intent =
        Intent(Intent.ACTION_SEND).apply {
            type = SHARE_MIME
            putExtra(Intent.EXTRA_TEXT, text)
        }

    private fun performFetch(requestId: Long, request: BridgeFetchRequest) {
        try {
            val conn = URL(request.url).openConnection() as HttpURLConnection
            try {
                conn.connectTimeout = CONNECT_TIMEOUT_MS
                conn.readTimeout = READ_TIMEOUT_MS
                conn.requestMethod = request.method // PATCH throws here — quirk (c)
                // Forwarded verbatim — incl. Accept-Encoding, see (b).
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

                // One value per header name — semantics note (d): collapse is
                // firstOrNull over headerFields' REVERSE-arrival-order lists.
                // The null key is the HTTP status line, not a header.
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

    companion object {
        private const val TAG = "BlazorNative"
        private const val PREFS_NAME = "blazornative"

        /** ClipData label for clipboard writes + the share Intent MIME type. */
        private const val CLIP_LABEL = "BlazorNative"
        private const val SHARE_MIME = "text/plain"

        /** HttpURLConnection timeouts. READ_TIMEOUT_MS bounds each read(),
         * not the whole response — see the slow-drip note on [fetchExecutor]. */
        private const val CONNECT_TIMEOUT_MS = 10_000
        private const val READ_TIMEOUT_MS = 10_000

        /** Test seam (instrumented ClipboardAndroidTest): when non-null, [share]
         * hands the built ACTION_SEND Intent here and does NOT startActivity, so
         * the test can assert the Intent (EXTRA_TEXT + type) without popping the
         * system share sheet. Null in production. Process-static because the test
         * cannot reach MainActivity's private bridge instance; reset it in the
         * test's finally so it never leaks across tests. */
        @Volatile @JvmStatic var shareLaunchHook: ((Intent) -> Unit)? = null
    }
}
