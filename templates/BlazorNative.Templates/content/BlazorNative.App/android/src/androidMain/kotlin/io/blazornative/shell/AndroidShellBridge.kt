package io.blazornative.shell

import android.Manifest
import android.annotation.SuppressLint
import android.app.Activity
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.content.SharedPreferences
import android.content.pm.PackageManager
import android.location.Location
import android.location.LocationListener
import android.location.LocationManager
import android.os.Handler
import android.os.HandlerThread
import android.util.Log
import io.blazornative.jni.BridgeFetchCompleter
import io.blazornative.jni.BridgeFetchRequest
import io.blazornative.jni.BridgeHostCallCompleter
import io.blazornative.jni.FlatJson
import io.blazornative.jni.HostCallOp
import io.blazornative.jni.HostCallStatus
import io.blazornative.jni.ShellBridgeHandlers
import java.lang.ref.WeakReference
import java.net.HttpURLConnection
import java.net.URL
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicInteger

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

    /**
     * Phase 9.0: the CURRENT foreground Activity, held WEAKLY (the retention rule
     * — never a strong Activity ref: a strong one leaks the whole view hierarchy
     * for the process lifetime). Needed ONLY to raise the system permission dialog
     * (ActivityCompat/requestPermissions demands an Activity). MainActivity passes
     * `this` as [context]; a recreated Activity constructs a FRESH bridge with the
     * new Activity, so this always points at whoever is current. The pending
     * requestCode→requestId map, by contrast, is app-scoped (static) so it SURVIVES
     * that recreation — the design's split.
     */
    private val activityRef: WeakReference<Activity> = WeakReference(context as? Activity)

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

    // ── Geolocation (Phase 9.0 — the permission-gated async pattern) ──────────
    //
    // The GENERIC permission-gated begin (hostCallBegin) wired for op=Geolocation.
    // The whole permission dance is HOST-SIDE and the terminal outcome ALWAYS comes
    // back as a tri-state STATUS via BridgeHostCallCompleter — denial/restriction/
    // no-fix/error are DATA (a status value 1..5 + null payload), never a thrown
    // exception across the JNA boundary and never a dropped completion (a hang).
    //
    // THE RECREATION-SURVIVAL SPLIT (the phase's named risk on Android): the OS
    // permission dialog can recreate the Activity while the request is airborne.
    // The requestCode→requestId map is therefore APP-SCOPED (a companion/static
    // field, [pendingPermissionRequests]) — it survives the recreation. Only the
    // Activity ref (for raising the dialog) is per-instance and re-established by
    // the recreated Activity. The .NET pending registry is process-scoped and never
    // noticed the recreation; the recreated Activity's onRequestPermissionsResult
    // forwards to [onPermissionResult], which reads the surviving map and completes
    // the SAME .NET requestId.

    /** A single background thread for LocationManager work: getLastKnownLocation is
     * a Binder call and a single-update listener needs a Looper — never the .NET
     * dispatch lane and never the main thread. Daemon so it never blocks exit. */
    private val locationThread: HandlerThread =
        HandlerThread("BlazorNative-Location").apply { isDaemon = true; start() }
    private val locationHandler: Handler = Handler(locationThread.looper)

    override fun hostCallBegin(requestId: Long, op: Int, argsJson: String) {
        when (op) {
            HostCallOp.GEOLOCATION -> handleGeolocation(requestId, argsJson)
            // An unknown op is DATA, not a crash: complete with Error so the
            // awaiting .NET ValueTask resolves rather than leaking pending.
            else -> {
                onError("hostCallBegin: unknown op $op (request $requestId)",
                    IllegalArgumentException("unsupported host-call op $op"))
                completeHostCall(requestId, HostCallStatus.ERROR, null)
            }
        }
    }

    /** The geolocation op: mode=request runs the whole request-then-fetch dance
     * (may prompt); mode=check is the read-only permission peek (never prompts). */
    private fun handleGeolocation(requestId: Long, argsJson: String) {
        val mode = FlatJson.parse(argsJson)["mode"] ?: "request"
        val held = hasLocationPermission()

        if (mode == "check") {
            // Read-only: report GRANTED/DENIED without ever prompting. .NET's
            // CheckPermissionAsync ignores the payload — only the status matters.
            completeHostCall(requestId, if (held) HostCallStatus.GRANTED else HostCallStatus.DENIED, null)
            return
        }

        // mode == request
        if (held) {
            fetchFixAndComplete(requestId)
            return
        }

        // Not held → prompt. Register the app-scoped requestCode→requestId entry
        // BEFORE the dialog (it must already be routable when the result lands on a
        // possibly-recreated Activity), then raise the system dialog on the current
        // Activity's UI thread.
        val activity = activityRef.get()
        if (activity == null) {
            // No Activity to prompt on (e.g. backgrounded) — DATA, not a hang.
            onError("geolocation request $requestId: no foreground Activity to prompt on",
                IllegalStateException("no Activity"))
            completeHostCall(requestId, HostCallStatus.LOCATION_UNAVAILABLE, null)
            return
        }

        val requestCode = nextRequestCode.getAndIncrement() and 0xFFFF // low 16 bits (framework rule)
        pendingPermissionRequests[requestCode] = requestId

        val permissions = arrayOf(Manifest.permission.ACCESS_FINE_LOCATION, Manifest.permission.ACCESS_COARSE_LOCATION)
        val hook = permissionRequestHook
        if (hook != null) {
            // Test seam (instrumented only): capture the request, do NOT pop the
            // real system dialog (unassertable/flaky under instrumentation — the
            // real dialog UX is owner-phone territory per the design's honesty
            // split). The test then drives onRequestPermissionsResult itself.
            hook(requestCode, permissions)
            return
        }
        activity.runOnUiThread { activity.requestPermissions(permissions, requestCode) }
    }

    /**
     * The forwarding target for MainActivity.onRequestPermissionsResult — reads the
     * APP-SCOPED map (so a recreated Activity routes to the in-flight requestId) and
     * completes the .NET call: a grant fetches a fix; a denial is DATA (Denied vs
     * DeniedPermanently by the post-request rationale). Returns true when the
     * requestCode was one of ours (found + routed), false otherwise (a foreign
     * requestCode the shell never issued — the caller ignores it).
     */
    fun onPermissionResult(requestCode: Int, permissions: Array<out String>, grantResults: IntArray): Boolean {
        val requestId = pendingPermissionRequests.remove(requestCode) ?: return false
        val granted = grantResults.isNotEmpty() &&
            grantResults.any { it == PackageManager.PERMISSION_GRANTED }
        if (granted) {
            fetchFixAndComplete(requestId)
        } else {
            // Denied vs DeniedPermanently: after a denial, rationale-false on every
            // requested permission ⇒ "don't ask again" (re-request will not prompt);
            // rationale-true ⇒ a later request MAY prompt again.
            val activity = activityRef.get()
            val permanent = activity != null && permissions.isNotEmpty() &&
                permissions.none { activity.shouldShowRequestPermissionRationale(it) }
            completeHostCall(
                requestId,
                if (permanent) HostCallStatus.DENIED_PERMANENTLY else HostCallStatus.DENIED,
                null,
            )
        }
        return true
    }

    private fun hasLocationPermission(): Boolean =
        appContext.checkSelfPermission(Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED ||
            appContext.checkSelfPermission(Manifest.permission.ACCESS_COARSE_LOCATION) == PackageManager.PERMISSION_GRANTED

    /** Obtains a single fix off-lane and completes the call. Last-known first (fed
     * directly by `adb emu geo fix` / a mock test provider); else a bounded
     * single-update, and on timeout LocationUnavailable — a status, never a hang. */
    @SuppressLint("MissingPermission") // only reached after hasLocationPermission()/a grant
    private fun fetchFixAndComplete(requestId: Long) {
        locationHandler.post {
            try {
                val lm = appContext.getSystemService(Context.LOCATION_SERVICE) as? LocationManager
                if (lm == null) {
                    completeHostCall(requestId, HostCallStatus.LOCATION_UNAVAILABLE, null)
                    return@post
                }
                val best = bestLastKnownLocation(lm)
                if (best != null) {
                    completeHostCall(requestId, HostCallStatus.GRANTED, fixPayload(best))
                    return@post
                }
                requestSingleFix(lm, requestId)
            } catch (se: SecurityException) {
                onError("geolocation fix: permission missing at fetch time (request $requestId)", se)
                completeHostCall(requestId, HostCallStatus.LOCATION_UNAVAILABLE, null)
            } catch (t: Throwable) {
                onError("geolocation fix failed (request $requestId)", t)
                completeHostCall(requestId, HostCallStatus.ERROR, null)
            }
        }
    }

    @SuppressLint("MissingPermission")
    private fun bestLastKnownLocation(lm: LocationManager): Location? =
        listOf(LocationManager.GPS_PROVIDER, LocationManager.NETWORK_PROVIDER, LocationManager.PASSIVE_PROVIDER)
            .mapNotNull { p -> runCatching { lm.getLastKnownLocation(p) }.getOrNull() }
            .maxByOrNull { it.time }

    @SuppressLint("MissingPermission")
    private fun requestSingleFix(lm: LocationManager, requestId: Long) {
        val provider = when {
            runCatching { lm.isProviderEnabled(LocationManager.GPS_PROVIDER) }.getOrDefault(false) ->
                LocationManager.GPS_PROVIDER
            runCatching { lm.isProviderEnabled(LocationManager.NETWORK_PROVIDER) }.getOrDefault(false) ->
                LocationManager.NETWORK_PROVIDER
            else -> null
        }
        if (provider == null) {
            completeHostCall(requestId, HostCallStatus.LOCATION_UNAVAILABLE, null)
            return
        }
        val done = AtomicBoolean(false)
        // A named object (not a SAM lambda) so removeUpdates can target this exact
        // instance from both the callback and the timeout.
        val listener = object : LocationListener {
            override fun onLocationChanged(location: Location) {
                if (done.compareAndSet(false, true)) {
                    lm.removeUpdates(this)
                    completeHostCall(requestId, HostCallStatus.GRANTED, fixPayload(location))
                }
            }
        }
        lm.requestLocationUpdates(provider, 0L, 0f, listener, locationThread.looper)
        locationHandler.postDelayed({
            if (done.compareAndSet(false, true)) {
                lm.removeUpdates(listener)
                completeHostCall(requestId, HostCallStatus.LOCATION_UNAVAILABLE, null)
            }
        }, FIX_TIMEOUT_MS)
    }

    /** The fix as the flat string→string JSON the wire carries (numbers as
     * locale-independent Double/Long strings — Java's Double.toString always uses
     * '.', matching .NET's InvariantCulture parse). Keys mirror
     * NativeShellBridge.ParseGeolocationResult: lat/lng/accuracy/altitude/timestamp. */
    private fun fixPayload(loc: Location): Map<String, String> {
        val fix = LinkedHashMap<String, String>()
        fix["lat"] = loc.latitude.toString()
        fix["lng"] = loc.longitude.toString()
        fix["accuracy"] = (if (loc.hasAccuracy()) loc.accuracy.toDouble() else 0.0).toString()
        if (loc.hasAltitude()) fix["altitude"] = loc.altitude.toString()
        fix["timestamp"] = loc.time.toString()
        return fix
    }

    /** Every geolocation completion funnels here so the instrumented tests can
     * observe the host_call_complete return code ([lastHostCallCompleteRcForTest]):
     * 0 = the .NET continuation was found and resolved (proves recreation survival),
     * 1 = unknown/already-completed id (benign). */
    private fun completeHostCall(requestId: Long, status: Int, payload: Map<String, String>?) {
        val rc = BridgeHostCallCompleter.complete(requestId, status, payload)
        lastHostCallCompleteRcForTest = rc
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

        // ── Geolocation (Phase 9.0) ──────────────────────────────────────────

        /** Bounds the single-update fallback when there is no last-known fix — on
         * expiry the call completes LocationUnavailable (a status, NEVER a hang). */
        private const val FIX_TIMEOUT_MS = 15_000L

        /** The APP-SCOPED requestCode→requestId map — the design's app-side half of
         * the pending permission state, deliberately STATIC so it survives an
         * Activity recreation behind the system dialog (a per-instance map would die
         * with the Activity that opened the dialog; the recreated Activity builds a
         * fresh bridge). The .NET registry is the process-scoped other half. */
        private val pendingPermissionRequests = ConcurrentHashMap<Int, Long>()

        /** Monotonic requestCode source (masked to the low 16 bits per the framework
         * requestPermissions rule). Static, matching the static map above. */
        private val nextRequestCode = AtomicInteger(1)

        /** Test seam (instrumented BnGeolocationAndroidTest): when non-null, a
         * permission request CAPTURES (requestCode, permissions) here and SKIPS the
         * real system dialog — the real dialog UX is owner-phone territory (the
         * design's PROVEN/UNPROVEN split), so CI drives the deny/recreation result
         * through onRequestPermissionsResult itself. Null in production; reset in the
         * test's finally so it never leaks across tests. */
        @Volatile @JvmStatic var permissionRequestHook: ((Int, Array<String>) -> Unit)? = null

        /** Test seam (instrumented BnGeolocationAndroidTest): the return code of the
         * most recent blazornative_host_call_complete — 0 = delivered to a live .NET
         * continuation (proves the process-scoped registry survived recreation), 1 =
         * unknown/already-completed id. Int.MIN_VALUE before any completion. */
        @Volatile @JvmStatic var lastHostCallCompleteRcForTest: Int = Int.MIN_VALUE

        /** Test seam: how many permission requests are currently in flight in the
         * APP-SCOPED map — the recreation-survival test asserts an entry persists
         * across scenario.recreate() and is consumed by the recreated Activity. */
        @JvmStatic fun pendingPermissionRequestCountForTest(): Int = pendingPermissionRequests.size

        /** Test seam: is [requestCode] still awaiting a result? (survives recreation
         * until the recreated Activity's onRequestPermissionsResult consumes it). */
        @JvmStatic fun hasPendingPermissionRequestForTest(requestCode: Int): Boolean =
            pendingPermissionRequests.containsKey(requestCode)

        /** Test seam: drain the app-scoped map + reset the last-rc probe between
         * tests so a leftover entry never routes a later test's result. */
        @JvmStatic fun resetGeolocationForTest() {
            pendingPermissionRequests.clear()
            lastHostCallCompleteRcForTest = Int.MIN_VALUE
        }

        /** Test seam (instrumented ClipboardAndroidTest): when non-null, [share]
         * hands the built ACTION_SEND Intent here and does NOT startActivity, so
         * the test can assert the Intent (EXTRA_TEXT + type) without popping the
         * system share sheet. Null in production. Process-static because the test
         * cannot reach MainActivity's private bridge instance; reset it in the
         * test's finally so it never leaks across tests. */
        @Volatile @JvmStatic var shareLaunchHook: ((Intent) -> Unit)? = null
    }
}
