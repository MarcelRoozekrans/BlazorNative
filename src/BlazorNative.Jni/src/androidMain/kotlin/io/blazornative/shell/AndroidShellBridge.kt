package io.blazornative.shell

import android.Manifest
import android.annotation.SuppressLint
import android.app.Activity
import android.app.AlarmManager
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.content.SharedPreferences
import android.content.pm.PackageManager
import android.location.Location
import android.location.LocationListener
import android.location.LocationManager
import android.net.Uri
import android.os.Build
import android.os.Handler
import android.os.HandlerThread
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyInfo
import android.security.keystore.KeyProperties
import android.util.Base64
import android.util.Log
import androidx.biometric.BiometricManager
import androidx.biometric.BiometricPrompt
import androidx.core.content.ContextCompat
import androidx.fragment.app.FragmentActivity
import io.blazornative.jni.BiometricStatus
import io.blazornative.jni.BridgeFetchCompleter
import io.blazornative.jni.BridgeFetchRequest
import io.blazornative.jni.BridgeHostCallCompleter
import io.blazornative.jni.FlatJson
import io.blazornative.jni.HostCallOp
import io.blazornative.jni.HostCallStatus
import io.blazornative.jni.NotificationStatus
import io.blazornative.jni.SecureStorageStatus
import io.blazornative.jni.ShellBridgeHandlers
import java.lang.ref.WeakReference
import java.net.HttpURLConnection
import java.net.URL
import java.security.KeyStore
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicInteger
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.SecretKeyFactory
import javax.crypto.spec.GCMParameterSpec

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
            HostCallOp.NOTIFICATIONS -> handleNotifications(requestId, argsJson)
            HostCallOp.BIOMETRICS -> handleBiometrics(requestId, argsJson)
            HostCallOp.SECURE_STORAGE -> handleSecureStorage(requestId, argsJson)
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

        // Phase 9.1: the SAME app-scoped requestCode→requestId machinery gates BOTH
        // capabilities — the parallel notification map says which arm this result
        // belongs to (absent ⇒ geolocation, the 9.0 path untouched). Notifications
        // reuse the recreation-survival split verbatim: both static maps survive the
        // Activity recreation the OS dialog can trigger, so a recreated Activity's
        // fresh bridge still routes the result to the in-flight .NET continuation.
        val notif = pendingNotificationRequests.remove(requestCode)
        if (notif != null) {
            if (granted) {
                runNotificationAction(requestId, notif) // posts/schedules then GRANTED
            } else {
                completeHostCall(requestId, notificationDenialStatus(permissions), null)
            }
            return true
        }

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

    /** Denied vs DeniedPermanently for a notification permission result — the
     * geolocation rationale rule, but mapped onto [NotificationStatus] (whose
     * DENIED/DENIED_PERMANENTLY happen to share geolocation's 1/2 by design). */
    private fun notificationDenialStatus(permissions: Array<out String>): Int {
        val activity = activityRef.get()
        val permanent = activity != null && permissions.isNotEmpty() &&
            permissions.none { activity.shouldShowRequestPermissionRationale(it) }
        return if (permanent) NotificationStatus.DENIED_PERMANENTLY else NotificationStatus.DENIED
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

    // ── Notifications (Phase 9.1 — the FIRST reuse of the 9.0 generic ABI) ────
    //
    // op=Notifications rides the SAME hostCallBegin slot geolocation uses; the
    // action lives INSIDE the flat JSON (geolocation's `mode` precedent). Every
    // completion is a NotificationStatus INTEGER via BridgeHostCallCompleter —
    // denial/restriction/error are DATA (a status 1..4 + null payload), never a
    // thrown exception across the JNA boundary and never a dropped completion (a
    // hang). show/schedule are permission-gated (POST_NOTIFICATIONS on API 33+;
    // implicitly granted below 33 — the majority fast path); cancel needs no
    // permission; check never prompts. The permission dance REUSES the geolocation
    // machinery verbatim — the same static requestCode→requestId map and
    // onRequestPermissionsResult forward — pointed at a different permission string.
    //
    // TAP-THROUGH (both halves, ZERO ABI): a shown notification carries a content
    // PendingIntent whose Intent is the 5.1 launch deep link `blazornative://<route>`
    // to MainActivity — a COLD tap relaunches and onCreate mounts the route by name.
    // A WARM tap (app alive, singleTop) lands in MainActivity.onNewIntent, which
    // dispatches the reserved "navigate" host event → .NET NavigateToAsync. Neither
    // grows the ABI (cold reuses the deep-link path; warm reuses host_event).

    /** The notification op. `action` selects show / schedule / cancel / request /
     * check (the flat-JSON vocabulary NativeShellBridge builds). show/schedule/
     * request are permission-gated; cancel and check never prompt. */
    private fun handleNotifications(requestId: Long, argsJson: String) {
        val args = FlatJson.parse(argsJson)
        val req = NotificationRequest.from(args)
        when (req.action) {
            "check" ->
                // Read-only permission peek — never prompts.
                completeHostCall(
                    requestId,
                    if (hasNotificationPermission()) NotificationStatus.GRANTED else NotificationStatus.DENIED,
                    null,
                )
            "cancel" ->
                // Idempotent and permission-free: drop a shown notification AND any
                // pending alarm by the same id, then GRANTED (nothing to deny).
                try {
                    cancelNotification(req.id)
                    completeHostCall(requestId, NotificationStatus.GRANTED, null)
                } catch (t: Throwable) {
                    onError("notifications cancel failed (request $requestId)", t)
                    completeHostCall(requestId, NotificationStatus.ERROR, null)
                }
            "show", "schedule", "request" ->
                if (hasNotificationPermission()) {
                    runNotificationAction(requestId, req)
                } else {
                    // API 33+ and not held → prompt (the geolocation machinery).
                    promptNotificationPermission(requestId, req)
                }
            else -> {
                onError("notifications: unknown action '${req.action}' (request $requestId)",
                    IllegalArgumentException("unsupported notification action ${req.action}"))
                completeHostCall(requestId, NotificationStatus.ERROR, null)
            }
        }
    }

    /** Runs the actual post/schedule (permission already held or just granted) and
     * completes GRANTED; a caught throw is DATA (Error), never a hang. `request` is
     * permission-only, so it posts nothing and simply confirms the grant. */
    private fun runNotificationAction(requestId: Long, req: NotificationRequest) {
        try {
            when (req.action) {
                "show" -> postNotification(appContext, req.id, req.title, req.body, req.route)
                "schedule" -> scheduleNotification(
                    req.id, req.title, req.body, req.whenMs ?: System.currentTimeMillis(), req.route)
                // "request" — permission-only; nothing to post.
            }
            completeHostCall(requestId, NotificationStatus.GRANTED, null)
        } catch (t: Throwable) {
            onError("notifications '${req.action}' failed (request $requestId)", t)
            completeHostCall(requestId, NotificationStatus.ERROR, null)
        }
    }

    /** Raises the POST_NOTIFICATIONS dialog, registering BOTH app-scoped maps first
     * (requestCode→requestId AND requestCode→the pending action) so a result landing
     * on a recreated Activity still routes and still knows what to post on grant. */
    private fun promptNotificationPermission(requestId: Long, req: NotificationRequest) {
        val activity = activityRef.get()
        if (activity == null) {
            onError("notifications request $requestId: no foreground Activity to prompt on",
                IllegalStateException("no Activity"))
            completeHostCall(requestId, NotificationStatus.ERROR, null)
            return
        }
        val requestCode = nextRequestCode.getAndIncrement() and 0xFFFF // low 16 bits (framework rule)
        pendingPermissionRequests[requestCode] = requestId
        pendingNotificationRequests[requestCode] = req

        val permissions = arrayOf(POST_NOTIFICATIONS_PERMISSION)
        val hook = permissionRequestHook
        if (hook != null) {
            // Test seam (instrumented only) — capture, do NOT pop the real dialog
            // (owner-phone territory, the geolocation split). The test drives the
            // result through onRequestPermissionsResult itself.
            hook(requestCode, permissions)
            return
        }
        activity.runOnUiThread { activity.requestPermissions(permissions, requestCode) }
    }

    /** POST_NOTIFICATIONS held? Below API 33 the permission did not exist and is
     * implicitly granted — the majority fast path, no manifest gate, no prompt. */
    private fun hasNotificationPermission(): Boolean =
        Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU ||
            appContext.checkSelfPermission(POST_NOTIFICATIONS_PERMISSION) == PackageManager.PERMISSION_GRANTED

    /** schedule → an INEXACT AlarmManager alarm (a notification needs no exact
     * timing, so no SCHEDULE_EXACT_ALARM permission) that fires [NotificationPublisher],
     * which reconstructs and posts the notification. The publish PendingIntent is
     * keyed by [id] so cancel can target it. */
    private fun scheduleNotification(id: Int, title: String, body: String, whenMs: Long, route: String?) {
        ensureNotificationChannel(appContext)
        val am = appContext.getSystemService(Context.ALARM_SERVICE) as AlarmManager
        val pi = PendingIntent.getBroadcast(
            appContext, id, publishIntent(appContext, id, title, body, route), publishFlags(create = true))
        am.set(AlarmManager.RTC_WAKEUP, whenMs, pi) // INEXACT — set, not setExact
    }

    /** cancel → both a shown notification AND a scheduled-but-unfired alarm, by id
     * (idempotent — a missing one is a no-op). */
    private fun cancelNotification(id: Int) {
        val nm = appContext.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        nm.cancel(id)
        val am = appContext.getSystemService(Context.ALARM_SERVICE) as AlarmManager
        // FLAG_NO_CREATE: null when no matching alarm is pending — nothing to cancel.
        val pi = PendingIntent.getBroadcast(
            appContext, id, publishIntent(appContext, id, "", "", null), publishFlags(create = false))
        if (pi != null) {
            am.cancel(pi)
            pi.cancel()
        }
    }

    /** Every host-call completion funnels here so the instrumented tests can
     * observe the host_call_complete return code ([lastHostCallCompleteRcForTest]):
     * 0 = the .NET continuation was found and resolved (proves recreation survival),
     * 1 = unknown/already-completed id (benign). */
    private fun completeHostCall(requestId: Long, status: Int, payload: Map<String, String>?) {
        val rc = BridgeHostCallCompleter.complete(requestId, status, payload)
        lastHostCallCompleteRcForTest = rc
    }

    // ── Biometrics + secure storage (Phase 9.2 — the SECOND reuse of the 9.0 ABI) ─
    //
    // op=Biometrics and op=SecureStorage ride the SAME hostCallBegin slot geolocation
    // opened; the action lives INSIDE the flat JSON (geolocation's `mode` precedent).
    // Every completion is a wire-mirrored status INTEGER (BiometricStatus /
    // SecureStorageStatus) via BridgeHostCallCompleter — denial / failure / cancel /
    // lockout / not-found / auth-failed are DATA, never a thrown exception across the
    // JNA boundary and never a dropped completion (a hang, the milestone law).
    //
    // SECURE STORAGE is raw AndroidKeyStore AES-256-GCM (no dependency; distinct from
    // the plain SharedPreferences store the storageRead/Write/Delete slots back). Each
    // value is encrypted with a per-key keystore key and stored as base64(iv):base64(ct)
    // in the "blazornative_secure" SharedPreferences — the CIPHERTEXT is safe at rest;
    // the prefs file is not itself a secret store.
    //
    // THE OS-KEY-LEVEL BINDING (the security crux): a requireAuth:true secret is
    // encrypted under a key generated with setUserAuthenticationRequired(true), so the
    // OS ITSELF refuses any crypto operation on it without a fresh auth (an AES per-use
    // auth key's doFinal throws UserNotAuthenticatedException — confirmed on device).
    // getWithAuth presents a BiometricPrompt wrapping the decrypt Cipher in a
    // CryptoObject; only the OS-unlocked cipher can decrypt. A plain get of the same
    // item CANNOT satisfy the key's auth requirement and returns AuthFailed — the OS
    // enforces the gate at the key, not the app, so a control-flow bypass still cannot
    // read the plaintext. (An AES per-use-auth key requires a fresh auth for ENCRYPT
    // too, so a requireAuth:true WRITE prompts as well — the honest consequence of AES.)
    //
    // BIOMETRICS is androidx BiometricPrompt (the CryptoObject-capable Jetpack API) run
    // on the main thread against MainActivity as the FragmentActivity host. The read-only
    // `check` maps BiometricManager.canAuthenticate(BIOMETRIC_STRONG) to a status and
    // never prompts.
    //
    // THE TEST SEAM (the geolocation/notifications split, verbatim): the REAL system
    // BiometricPrompt sheet + a real fingerprint are owner-phone territory — the
    // emulator's fingerprint is a synthetic path (adb emu finger touch injects a fake
    // event) and the sheet is not CI-drivable. So [biometricGateHook], when set
    // (instrumented only), REPLACES the real prompt: the test drives the outcome and CI
    // proves the wire, the CryptoObject binding, the status mapping and denial-as-data
    // no-hang. The deterministic OS-KEY BINDING (a plain get of an auth item AuthFails)
    // is proven directly against the keystore; the real fingerprint-driven decrypt is
    // the documented UNPROVEN-until-hardware half.

    /** SharedPreferences holding the encrypted secrets (base64(iv):base64(ct) per key).
     * The ciphertext is safe at rest — the encryption keys live in the AndroidKeyStore,
     * never here. Distinct from the plain "blazornative" store (storageRead/Write). */
    private val secretPrefs: SharedPreferences =
        appContext.getSharedPreferences(SECURE_PREFS_NAME, Context.MODE_PRIVATE)

    /** The biometrics op. action=check is the read-only availability peek (never
     * prompts — the geolocation `mode:check` sibling); action=authenticate shows the OS
     * BiometricPrompt (no CryptoObject) and returns a BiometricStatus. */
    private fun handleBiometrics(requestId: Long, argsJson: String) {
        val args = FlatJson.parse(argsJson)
        when (val action = args["action"] ?: "authenticate") {
            "check" -> completeHostCall(requestId, canAuthenticateStatus(), null)
            "authenticate" -> runBiometricGate(
                reason = args["reason"] ?: "Authenticate",
                crypto = null,
                onAuthenticated = { completeHostCall(requestId, BiometricStatus.AUTHENTICATED, null) },
                onDenied = { status -> completeHostCall(requestId, status, null) },
            )
            else -> {
                onError("biometrics: unknown action '$action' (request $requestId)",
                    IllegalArgumentException("unsupported biometric action $action"))
                completeHostCall(requestId, BiometricStatus.ERROR, null)
            }
        }
    }

    /** BiometricManager.canAuthenticate(BIOMETRIC_STRONG) → a BiometricStatus for the
     * read-only check: SUCCESS ⇒ Authenticated ("present + enrolled + ready"); no
     * hardware / none enrolled / unavailable ⇒ Unavailable (the design's check maps
     * every non-success to Unavailable — a status, never a throw). */
    internal fun canAuthenticateStatus(): Int =
        when (BiometricManager.from(appContext)
            .canAuthenticate(BiometricManager.Authenticators.BIOMETRIC_STRONG)) {
            BiometricManager.BIOMETRIC_SUCCESS -> BiometricStatus.AUTHENTICATED
            else -> BiometricStatus.UNAVAILABLE
        }

    /** The secure-storage op. set/get/getWithAuth/delete each complete with a
     * SecureStorageStatus; get/getWithAuth carry the value in the {"value":…} payload
     * on Ok. The synchronous arms (plain get / plain set / delete) compute a
     * [SecureOutcome] and complete inline; the auth arms (auth-bound set, getWithAuth of
     * an auth item) suspend behind a BiometricPrompt and complete from its callback. */
    private fun handleSecureStorage(requestId: Long, argsJson: String) {
        val args = FlatJson.parse(argsJson)
        val key = args["key"].orEmpty()
        when (val action = args["action"] ?: "get") {
            "set" ->
                if (args["auth"] == "1") secureSetAuth(requestId, key, args["value"].orEmpty())
                else complete(requestId, secureSetPlainCore(key, args["value"].orEmpty()))
            "get" -> complete(requestId, secureGetCore(key))
            "getWithAuth" -> secureGetWithAuth(requestId, key, args["reason"] ?: "Unlock")
            "delete" -> complete(requestId, secureDeleteCore(key))
            else -> {
                onError("secure storage: unknown action '$action' (request $requestId)",
                    IllegalArgumentException("unsupported secure-storage action $action"))
                completeHostCall(requestId, SecureStorageStatus.ERROR, null)
            }
        }
    }

    private fun complete(requestId: Long, outcome: SecureOutcome) =
        completeHostCall(requestId, outcome.status, outcome.value?.let { mapOf("value" to it) })

    /** A plain (non-auth) set: provision a non-auth keystore key, encrypt, persist.
     * Prompt-free. Returns Ok, or Error on a caught failure (a status, never a throw). */
    internal fun secureSetPlainCore(key: String, value: String): SecureOutcome =
        try {
            val secretKey = provisionKey(key, requireAuth = false)
            val cipher = Cipher.getInstance(AES_GCM).apply { init(Cipher.ENCRYPT_MODE, secretKey) }
            storeBlob(key, cipher.iv, cipher.doFinal(value.toByteArray(Charsets.UTF_8)))
            SecureOutcome(SecureStorageStatus.OK, null)
        } catch (t: Throwable) {
            onError("secure set('$key') failed", t)
            SecureOutcome(secureErrorStatus(t), null)
        }

    /** An auth-bound set: provision a user-auth-required key, then encrypt BEHIND a
     * BiometricPrompt (an AES per-use-auth key's encrypt doFinal also needs a fresh
     * auth — the honest consequence of AES-at-the-key). The CryptoObject wraps the
     * encrypt Cipher the OS unlocks. A no-biometric-enrolled device fails provisioning
     * with Unavailable; a denied prompt is AuthFailed — both DATA, never a hang. */
    private fun secureSetAuth(requestId: Long, key: String, value: String) {
        val cipher: Cipher
        val iv: ByteArray
        try {
            val secretKey = provisionKey(key, requireAuth = true)
            cipher = Cipher.getInstance(AES_GCM).apply { init(Cipher.ENCRYPT_MODE, secretKey) }
            iv = cipher.iv
        } catch (t: Throwable) {
            onError("secure set('$key', auth) provisioning failed (request $requestId)", t)
            completeHostCall(requestId, secureErrorStatus(t), null)
            return
        }
        runBiometricGate(
            reason = "Confirm to store a protected secret",
            crypto = BiometricPrompt.CryptoObject(cipher),
            onAuthenticated = { result ->
                try {
                    val c = result?.cryptoObject?.cipher ?: cipher
                    storeBlob(key, iv, c.doFinal(value.toByteArray(Charsets.UTF_8)))
                    completeHostCall(requestId, SecureStorageStatus.OK, null)
                } catch (t: Throwable) {
                    onError("secure set('$key', auth) encrypt failed after auth (request $requestId)", t)
                    completeHostCall(requestId, SecureStorageStatus.AUTH_FAILED, null)
                }
            },
            onDenied = { completeHostCall(requestId, SecureStorageStatus.AUTH_FAILED, null) },
        )
    }

    /** A plain get: NotFound when absent; AUTH-BOUND ITEMS RETURN AuthFailed (a plain
     * get cannot satisfy the key's user-auth requirement — the OS-key binding, read
     * from the keystore's own KeyInfo, and the OS would refuse the decrypt regardless);
     * otherwise decrypt and return the value on Ok. */
    internal fun secureGetCore(key: String): SecureOutcome =
        try {
            val blob = loadBlob(key)
            val secretKey = loadKey(key)
            when {
                blob == null || secretKey == null -> SecureOutcome(SecureStorageStatus.NOT_FOUND, null)
                keyRequiresAuth(secretKey) -> SecureOutcome(SecureStorageStatus.AUTH_FAILED, null)
                else -> SecureOutcome(SecureStorageStatus.OK, decrypt(secretKey, blob))
            }
        } catch (t: Throwable) {
            onError("secure get('$key') failed", t)
            SecureOutcome(secureErrorStatus(t), null)
        }

    /** getWithAuth: NotFound when absent; a NON-auth item is read directly (no prompt
     * to show); an AUTH-BOUND item inits the decrypt Cipher, wraps it in a CryptoObject
     * and lets the OS unlock it behind a fresh BiometricPrompt — only the OS-unlocked
     * cipher can decrypt (a plain get above correctly AuthFails). Denial/failure ⇒
     * AuthFailed, DATA, never a hang. */
    private fun secureGetWithAuth(requestId: Long, key: String, reason: String) {
        val blob: SecretBlob
        val secretKey: SecretKey
        val cipher: Cipher
        try {
            val b = loadBlob(key); val k = loadKey(key)
            if (b == null || k == null) { completeHostCall(requestId, SecureStorageStatus.NOT_FOUND, null); return }
            if (!keyRequiresAuth(k)) {
                completeHostCall(requestId, SecureStorageStatus.OK, mapOf("value" to decrypt(k, b)))
                return
            }
            blob = b; secretKey = k
            cipher = Cipher.getInstance(AES_GCM).apply {
                init(Cipher.DECRYPT_MODE, secretKey, GCMParameterSpec(GCM_TAG_BITS, blob.iv))
            }
        } catch (t: Throwable) {
            onError("secure getWithAuth('$key') failed (request $requestId)", t)
            completeHostCall(requestId, secureErrorStatus(t), null)
            return
        }
        runBiometricGate(
            reason = reason,
            crypto = BiometricPrompt.CryptoObject(cipher),
            onAuthenticated = { result ->
                try {
                    // The OS-unlocked cipher from the CryptoObject is the ONLY one that
                    // can decrypt — use it, never a fresh unbound cipher.
                    val c = result?.cryptoObject?.cipher ?: cipher
                    val plain = String(c.doFinal(blob.ct), Charsets.UTF_8)
                    completeHostCall(requestId, SecureStorageStatus.OK, mapOf("value" to plain))
                } catch (t: Throwable) {
                    onError("secure getWithAuth('$key') decrypt failed after auth (request $requestId)", t)
                    completeHostCall(requestId, SecureStorageStatus.AUTH_FAILED, null)
                }
            },
            onDenied = { completeHostCall(requestId, SecureStorageStatus.AUTH_FAILED, null) },
        )
    }

    /** delete: drop the keystore key AND the stored ciphertext. Idempotent — a missing
     * key is still Ok (nothing to remove). */
    internal fun secureDeleteCore(key: String): SecureOutcome =
        try {
            androidKeyStore().deleteEntry(aliasFor(key))
            secretPrefs.edit().remove(key).commit()
            SecureOutcome(SecureStorageStatus.OK, null)
        } catch (t: Throwable) {
            onError("secure delete('$key') failed", t)
            SecureOutcome(SecureStorageStatus.ERROR, null)
        }

    /**
     * Presents the OS BiometricPrompt on the main thread against MainActivity as the
     * FragmentActivity host (with [crypto] when this auth gates a keystore cipher), OR,
     * when [biometricGateHook] is set (instrumented only), hands a [BiometricGate] to
     * the test INSTEAD OF popping the real system sheet — the geolocation/notifications
     * split (the real prompt is owner-phone territory; the emulator's is synthetic).
     * [onAuthenticated] runs on a successful auth; [onDenied] gets a BiometricStatus for
     * every non-success terminal (cancel/lockout/unavailable/error) — all DATA.
     */
    private fun runBiometricGate(
        reason: String,
        crypto: BiometricPrompt.CryptoObject?,
        onAuthenticated: (BiometricPrompt.AuthenticationResult?) -> Unit,
        onDenied: (Int) -> Unit,
    ) {
        val hook = biometricGateHook
        if (hook != null) {
            hook(BiometricGate(crypto != null, onAuthenticated, onDenied))
            return
        }
        val activity = activityRef.get() as? FragmentActivity
        if (activity == null) {
            // No FragmentActivity to prompt on — DATA, not a hang.
            onError("biometric prompt: no foreground FragmentActivity",
                IllegalStateException("no FragmentActivity"))
            onDenied(if (crypto != null) SecureStorageStatus.AUTH_FAILED else BiometricStatus.ERROR)
            return
        }
        activity.runOnUiThread {
            val prompt = BiometricPrompt(
                activity, ContextCompat.getMainExecutor(appContext),
                object : BiometricPrompt.AuthenticationCallback() {
                    override fun onAuthenticationSucceeded(result: BiometricPrompt.AuthenticationResult) =
                        onAuthenticated(result)

                    // Non-terminal on device (retry allowed; the sheet stays up) — do
                    // NOT complete here. A terminal outcome arrives via onError
                    // (cancel/lockout) or a later success, so the await never hangs.
                    override fun onAuthenticationFailed() {}

                    override fun onAuthenticationError(code: Int, msg: CharSequence) =
                        onDenied(errorCodeToStatus(code, crypto != null))
                })
            val info = BiometricPrompt.PromptInfo.Builder()
                .setTitle(reason)
                .setNegativeButtonText("Cancel")
                .setAllowedAuthenticators(BiometricManager.Authenticators.BIOMETRIC_STRONG)
                .build()
            if (crypto != null) prompt.authenticate(info, crypto) else prompt.authenticate(info)
        }
    }

    /** A BiometricPrompt error code → a status. For a storage gate ([storage] true) it
     * folds into SecureStorageStatus (AuthFailed/Unavailable); for standalone
     * authenticate it is the full BiometricStatus (Cancelled/LockedOut/Unavailable). */
    private fun errorCodeToStatus(code: Int, storage: Boolean): Int = when (code) {
        BiometricPrompt.ERROR_USER_CANCELED, BiometricPrompt.ERROR_NEGATIVE_BUTTON,
        BiometricPrompt.ERROR_CANCELED ->
            if (storage) SecureStorageStatus.AUTH_FAILED else BiometricStatus.CANCELLED
        BiometricPrompt.ERROR_LOCKOUT, BiometricPrompt.ERROR_LOCKOUT_PERMANENT ->
            if (storage) SecureStorageStatus.AUTH_FAILED else BiometricStatus.LOCKED_OUT
        BiometricPrompt.ERROR_NO_BIOMETRICS, BiometricPrompt.ERROR_HW_NOT_PRESENT,
        BiometricPrompt.ERROR_HW_UNAVAILABLE, BiometricPrompt.ERROR_NO_DEVICE_CREDENTIAL ->
            if (storage) SecureStorageStatus.UNAVAILABLE else BiometricStatus.UNAVAILABLE
        else -> if (storage) SecureStorageStatus.AUTH_FAILED else BiometricStatus.ERROR
    }

    // ── Keystore helpers (raw AndroidKeyStore AES-256-GCM) ────────────────────

    private fun aliasFor(key: String): String = KEY_ALIAS_PREFIX + key

    private fun androidKeyStore(): KeyStore =
        KeyStore.getInstance(ANDROID_KEYSTORE).apply { load(null) }

    /** Generates a fresh AES-256-GCM key for [key] (deleting any prior). A
     * [requireAuth] key is generated with setUserAuthenticationRequired(true) — the
     * OS-KEY-LEVEL binding — so the OS refuses every crypto op on it without a fresh
     * biometric auth (API 30+ pins BIOMETRIC_STRONG via setUserAuthenticationParameters;
     * such a key can only be GENERATED on a device with an enrolled biometric — a
     * no-biometric device surfaces as Unavailable, DATA). [allowDeviceCredentialForTest]
     * additionally permits the device credential, used ONLY by the instrumented binding
     * test so an auth-bound key can be provisioned on a CI emulator that has a lock-screen
     * PIN but no scriptable fingerprint; production is biometric-only. */
    internal fun provisionKey(
        key: String,
        requireAuth: Boolean,
        allowDeviceCredentialForTest: Boolean = false,
    ): SecretKey {
        androidKeyStore().deleteEntry(aliasFor(key))
        val gen = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, ANDROID_KEYSTORE)
        val spec = KeyGenParameterSpec.Builder(
            aliasFor(key), KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
            .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
            .setKeySize(256)
        if (requireAuth) {
            spec.setUserAuthenticationRequired(true)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                val types = if (allowDeviceCredentialForTest)
                    KeyProperties.AUTH_BIOMETRIC_STRONG or KeyProperties.AUTH_DEVICE_CREDENTIAL
                else KeyProperties.AUTH_BIOMETRIC_STRONG
                spec.setUserAuthenticationParameters(0, types)
            }
        }
        gen.init(spec.build())
        return gen.generateKey()
    }

    private fun loadKey(key: String): SecretKey? =
        androidKeyStore().getKey(aliasFor(key), null) as? SecretKey

    /** Reads the keystore's OWN record of whether [secretKey] is user-auth-bound
     * (KeyInfo.isUserAuthenticationRequired) — the OS-key property, not an app flag. A
     * plain get of such a key cannot proceed (the OS would refuse the decrypt); this is
     * the deterministic tripwire the "drop setUserAuthenticationRequired" mutation reds. */
    private fun keyRequiresAuth(secretKey: SecretKey): Boolean =
        try {
            val factory = SecretKeyFactory.getInstance(secretKey.algorithm, ANDROID_KEYSTORE)
            (factory.getKeySpec(secretKey, KeyInfo::class.java) as KeyInfo).isUserAuthenticationRequired
        } catch (t: Throwable) {
            // A key whose auth binding we cannot read is treated as auth-bound (fail
            // closed — never hand back plaintext on an unreadable gate).
            true
        }

    private fun decrypt(secretKey: SecretKey, blob: SecretBlob): String {
        val cipher = Cipher.getInstance(AES_GCM).apply {
            init(Cipher.DECRYPT_MODE, secretKey, GCMParameterSpec(GCM_TAG_BITS, blob.iv))
        }
        return String(cipher.doFinal(blob.ct), Charsets.UTF_8)
    }

    private fun storeBlob(key: String, iv: ByteArray, ct: ByteArray) {
        val encoded = Base64.encodeToString(iv, Base64.NO_WRAP) + ":" +
            Base64.encodeToString(ct, Base64.NO_WRAP)
        check(secretPrefs.edit().putString(key, encoded).commit()) {
            "secure store commit failed for '$key'"
        }
    }

    private fun loadBlob(key: String): SecretBlob? {
        val encoded = secretPrefs.getString(key, null) ?: return null
        val parts = encoded.split(":")
        if (parts.size != 2) return null
        return SecretBlob(
            Base64.decode(parts[0], Base64.NO_WRAP), Base64.decode(parts[1], Base64.NO_WRAP))
    }

    /** A provisioning failure for an auth-bound key on a device with no enrolled
     * biometric (KeyGenerator throws) is Unavailable, not Error — the honest "no secure
     * biometric here" status. Everything else caught is Error. */
    private fun secureErrorStatus(t: Throwable): Int =
        if (t is java.security.InvalidAlgorithmParameterException || t is IllegalStateException)
            SecureStorageStatus.UNAVAILABLE
        else SecureStorageStatus.ERROR

    /** The parsed ciphertext blob (a fresh GCM IV per write; the tag rides the
     * ciphertext). */
    private class SecretBlob(val iv: ByteArray, val ct: ByteArray)

    /** The outcome of a synchronous secure-storage op — a status and, only on Ok get,
     * the value (the GeolocationResult/SecretResult twin). */
    internal data class SecureOutcome(val status: Int, val value: String?)

    /**
     * The test seam's handle on a pending biometric auth (instrumented only). [succeed]
     * drives a successful auth; [deny] drives a terminal denial with a status. For a
     * storage CryptoObject gate, a hook [succeed] CANNOT unlock the OS-bound cipher (no
     * real auth happened) so the shell's decrypt/encrypt still refuses — the seam proves
     * the wire + the CryptoObject binding + denial-as-data no-hang, never the real
     * OS-unlocked decrypt (owner-phone territory).
     */
    class BiometricGate internal constructor(
        val hasCryptoObject: Boolean,
        private val onAuthenticated: (BiometricPrompt.AuthenticationResult?) -> Unit,
        private val onDenied: (Int) -> Unit,
    ) {
        fun succeed() = onAuthenticated(null)
        fun deny(status: Int) = onDenied(status)
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

    /** The parsed notification request — the flat-JSON args NativeShellBridge builds
     * (every field a string; `id` decimal, `when` Unix epoch MILLISECONDS). Held
     * whole in the app-scoped pending map so a grant that lands on a recreated
     * Activity still knows exactly what to post. */
    private data class NotificationRequest(
        val action: String,
        val id: Int,
        val title: String,
        val body: String,
        val whenMs: Long?,
        val route: String?,
    ) {
        companion object {
            fun from(args: Map<String, String>): NotificationRequest = NotificationRequest(
                action = args["action"] ?: "show",
                id = args["id"]?.toIntOrNull() ?: 0,
                title = args["title"].orEmpty(),
                body = args["body"].orEmpty(),
                whenMs = args["when"]?.toLongOrNull(),
                route = args["route"],
            )
        }
    }

    /**
     * The scheduled-notification publisher (Phase 9.1). A MANIFEST-declared
     * receiver — not a runtime one — precisely so a `schedule` alarm still fires
     * after the app process is killed (a runtime receiver dies with the process).
     * The [AlarmManager] hands it the id/title/body/route as extras; it rebuilds the
     * notification through the SAME [postNotification] the immediate `show` path
     * uses (one builder, no drift). Declared in the manifest as the nested binary
     * name `io.blazornative.shell.AndroidShellBridge$NotificationPublisher`.
     */
    class NotificationPublisher : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) {
            postNotification(
                context.applicationContext,
                intent.getIntExtra(EXTRA_ID, 0),
                intent.getStringExtra(EXTRA_TITLE).orEmpty(),
                intent.getStringExtra(EXTRA_BODY).orEmpty(),
                intent.getStringExtra(EXTRA_ROUTE),
            )
        }
    }

    /** Test seam (instrumented only): provisions an AUTH-BOUND keystore key for [key]
     * and writes a stored blob WITHOUT encrypting (an AES per-use-auth key's encrypt
     * needs a real auth, which is not CI-scriptable). Lets the binding test set up an
     * auth-bound item so a plain [secureGetCore] can prove it returns AuthFailed. Uses
     * the device-credential fallback so the key generates on a CI emulator with a PIN
     * but no enrolled fingerprint. Returns true if provisioned. */
    internal fun writeAuthBoundSecretForTest(key: String): Boolean =
        try {
            provisionKey(key, requireAuth = true, allowDeviceCredentialForTest = true)
            // A dummy blob — a plain get AuthFails at the key BEFORE any decrypt, so the
            // bytes need not be valid ciphertext (the mutation that drops the auth flag
            // makes the get attempt the decrypt, which then fails the GCM tag → Error,
            // still ≠ AuthFailed, so the tripwire reds either way).
            storeBlob(key, ByteArray(12), ByteArray(32))
            true
        } catch (t: Throwable) {
            onError("writeAuthBoundSecretForTest('$key') failed", t)
            false
        }

    companion object {
        private const val TAG = "BlazorNative"
        private const val PREFS_NAME = "blazornative"

        // ── Secure storage (Phase 9.2) ───────────────────────────────────────
        private const val SECURE_PREFS_NAME = "blazornative_secure"
        private const val KEY_ALIAS_PREFIX = "bn_secret_"
        private const val ANDROID_KEYSTORE = "AndroidKeyStore"
        private const val AES_GCM = "AES/GCM/NoPadding"
        private const val GCM_TAG_BITS = 128

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

        /** Phase 9.1: the notification arm of the app-scoped pending state — the
         * requestCode→pending-action map that says a permission result belongs to a
         * notification (and what to post on grant), parallel to
         * [pendingPermissionRequests]. STATIC for the same recreation-survival reason:
         * the POST_NOTIFICATIONS dialog can recreate the Activity mid-request, and the
         * recreated Activity's fresh bridge must still know what to post. */
        private val pendingNotificationRequests = ConcurrentHashMap<Int, NotificationRequest>()

        // ── Notifications: the channel + the shared builder (Phase 9.1) ──────────

        /** The single notification channel (Android 8+ requires one), created once
         * and idempotently. One literal, mirrored in the design doc. */
        const val CHANNEL_ID = "blazornative_default"
        private const val CHANNEL_NAME = "Notifications"

        /** POST_NOTIFICATIONS as a bare string so it compiles below the API 33 that
         * added the constant (referenced only on 33+, where it exists). */
        private const val POST_NOTIFICATIONS_PERMISSION = "android.permission.POST_NOTIFICATIONS"

        /** Publish-alarm extras (schedule → [NotificationPublisher]). */
        private const val EXTRA_ID = "io.blazornative.shell.NP_ID"
        private const val EXTRA_TITLE = "io.blazornative.shell.NP_TITLE"
        private const val EXTRA_BODY = "io.blazornative.shell.NP_BODY"
        private const val EXTRA_ROUTE = "io.blazornative.shell.NP_ROUTE"

        /** Creates the channel if absent (idempotent). Android 8+ only — below 26 a
         * notification posts without a channel. */
        fun ensureNotificationChannel(context: Context) {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                val nm = context.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
                if (nm.getNotificationChannel(CHANNEL_ID) == null) {
                    nm.createNotificationChannel(
                        NotificationChannel(CHANNEL_ID, CHANNEL_NAME, NotificationManager.IMPORTANCE_DEFAULT))
                }
            }
        }

        /** Builds + posts the notification on [CHANNEL_ID] (the shared builder — the
         * immediate `show` AND the scheduled [NotificationPublisher] both land here).
         * A non-null [route] attaches the tap-through content PendingIntent. */
        fun postNotification(context: Context, id: Int, title: String, body: String, route: String?) {
            ensureNotificationChannel(context)
            val nm = context.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
            @Suppress("DEPRECATION")
            val builder = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O)
                Notification.Builder(context, CHANNEL_ID)
            else
                Notification.Builder(context)
            builder.setContentTitle(title)
                .setContentText(body)
                .setSmallIcon(android.R.drawable.ic_dialog_info) // a small icon is REQUIRED to post
                .setAutoCancel(true)
            if (route != null) {
                builder.setContentIntent(
                    PendingIntent.getActivity(
                        context, id, buildTapIntent(context, route),
                        PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE))
            }
            nm.notify(id, builder.build())
        }

        /** The tap-through Intent the content PendingIntent wraps — the EXACT 5.1
         * launch deep link (`blazornative://<route>` VIEW-intent to [MainActivity]),
         * so a cold tap relaunches into onCreate's deep-link mount and a warm tap
         * (singleTop) lands in onNewIntent. Test seam: instrumented tap-through
         * launches THIS intent (drop the data ⇒ the tap lands on the default page). */
        fun buildTapIntent(context: Context, route: String): Intent =
            Intent(
                Intent.ACTION_VIEW,
                Uri.parse("${MainActivity.DEEP_LINK_SCHEME}://${route.removePrefix("/")}"),
                context, MainActivity::class.java,
            ).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP)

        /** The publish-alarm Intent targeting [NotificationPublisher], carrying the
         * notification payload as extras (keyed by id via the PendingIntent). */
        private fun publishIntent(context: Context, id: Int, title: String, body: String, route: String?): Intent =
            Intent(context, NotificationPublisher::class.java).apply {
                putExtra(EXTRA_ID, id)
                putExtra(EXTRA_TITLE, title)
                putExtra(EXTRA_BODY, body)
                if (route != null) putExtra(EXTRA_ROUTE, route)
            }

        /** PendingIntent flags for the publish alarm: IMMUTABLE (targetSdk 34
         * requires a mutability flag); FLAG_NO_CREATE on the cancel path returns null
         * when no alarm is pending. */
        private fun publishFlags(create: Boolean): Int =
            (if (create) PendingIntent.FLAG_UPDATE_CURRENT else PendingIntent.FLAG_NO_CREATE) or
                PendingIntent.FLAG_IMMUTABLE

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

        /** Test seam: drain the app-scoped maps + reset the last-rc probe between
         * tests so a leftover entry never routes a later test's result. Drains the
         * notification map too — both arms share the requestCode space. */
        @JvmStatic fun resetGeolocationForTest() {
            pendingPermissionRequests.clear()
            pendingNotificationRequests.clear()
            lastHostCallCompleteRcForTest = Int.MIN_VALUE
        }

        /** Test seam (instrumented ClipboardAndroidTest): when non-null, [share]
         * hands the built ACTION_SEND Intent here and does NOT startActivity, so
         * the test can assert the Intent (EXTRA_TEXT + type) without popping the
         * system share sheet. Null in production. Process-static because the test
         * cannot reach MainActivity's private bridge instance; reset it in the
         * test's finally so it never leaks across tests. */
        @Volatile @JvmStatic var shareLaunchHook: ((Intent) -> Unit)? = null

        /** Test seam (instrumented BnSecureAndroidTest): when non-null, [runBiometricGate]
         * hands the pending auth to this hook INSTEAD OF popping the real system
         * BiometricPrompt sheet — the geolocation/notifications split (the real prompt +
         * a real fingerprint are owner-phone territory; the emulator's is synthetic). The
         * test drives BiometricGate.succeed()/deny(status) and CI proves the wire, the
         * CryptoObject binding (hasCryptoObject) and denial-as-data no-hang. Null in
         * production; reset in the test's finally so it never leaks across tests. */
        @Volatile @JvmStatic var biometricGateHook: ((BiometricGate) -> Unit)? = null
    }
}
