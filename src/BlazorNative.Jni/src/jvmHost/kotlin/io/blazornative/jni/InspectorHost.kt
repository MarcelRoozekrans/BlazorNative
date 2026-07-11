package io.blazornative.jni

import java.net.BindException
import java.util.concurrent.CountDownLatch
import kotlin.system.exitProcess

/**
 * Phase 4.4 Gate 1 — PreviewHost's long-lived sibling: boots the NativeAOT
 * dll via [BlazorNativeRuntime], mounts one component (args[0], default
 * "BnDemo"), and serves the inspector API/page ([InspectorServer]) on args[1]
 * (default 5199) until the process is killed — run it via
 * `gradlew runInspectorHost [-Pcomponent=Name] [-Pport=NNNN]`.
 *
 * FAST-RESTART, NOT HOT-RELOAD (PreviewHost's survey-proven constraint,
 * unchanged): JNA's Native.load is process-lifetime, so a warm host can never
 * pick up a rebuilt dll — restart the host per publish cycle.
 *
 * ONERROR HONESTY (the PreviewHost Gate 1 review N1 posture, host-length):
 * a frame fault DURING MOUNT leaves the tree of record PARTIAL — the host
 * refuses to serve it and exits 1. Post-boot faults (a dropped re-render
 * frame) can't abort a live session; they are logged to stderr AND into the
 * inspector's own event log ([InspectorState.logError]), so the page shows
 * the session's dishonesty instead of hiding it.
 *
 * PLACEMENT (a FORCED deviation from PreviewHost's main/kotlin trade):
 * `com.sun.net.httpserver` does not exist in android.jar, so this file and
 * [InspectorServer] cannot compile in the shared main source set at all.
 * They live in src/jvmHost/kotlin, which build.gradle.kts wires into the
 * HOST-JVM UNIT-TEST compilation (full JDK on the classpath, friend access
 * to main's internals) — no new compilation arrangement, and unlike
 * PreviewHost these classes never ship as APK dead code. The Android-safe
 * inspector pieces ([InspectorState], [InspectorJson], TreeSnapshot's
 * renderJson) stay in main/kotlin, available to an on-device channel (M5).
 *
 * THREADING: boot (init/register/mount) runs on this main thread — the mount
 * frames arrive synchronously here (sync-mount contract). After boot, all
 * .NET entry happens through [BlazorNativeRuntime.dispatchEventAndWait] from
 * the POST handler — serialized on the runtime's single dispatch lane (see
 * InspectorServer's threading KDoc); this thread just parks on a latch.
 */
/**
 * The host's in-memory shell bridge — components resolving IMobileBridge
 * (BnDemo's "Settings →" → Navigate, the route round-trip, storage) need a
 * live host or their handlers fault with rc 2. Same minimal shape as
 * NavigationTest's RecordingHandlers: navigate/currentRoute round-trip,
 * HashMap storage, and fetch answered with an HONEST transport failure (the
 * inspector host does no network I/O today — a fetching component sees an
 * HttpRequestException, not a hang).
 */
private class InspectorHostBridge : ShellBridgeHandlers {
    @Volatile private var route: String = "/"

    private val storage = java.util.concurrent.ConcurrentHashMap<String, String>()

    override fun navigate(route: String) {
        this.route = route
        println("[BRIDGE] navigate → $route")
    }

    override fun currentRoute(): String = route
    override fun storageRead(key: String): String? = storage[key]
    override fun storageWrite(key: String, value: String) {
        storage[key] = value
    }
    override fun storageDelete(key: String) {
        storage.remove(key)
    }
    override fun fetchBegin(requestId: Long, request: BridgeFetchRequest) {
        BridgeFetchCompleter.completeFailure(requestId, "InspectorHost performs no network fetches")
    }
}

fun main(args: Array<String>) {
    val component = args.firstOrNull() ?: "BnDemo"
    val port = args.getOrNull(1)?.toIntOrNull() ?: InspectorServer.DEFAULT_PORT
    try {
        val state = InspectorState()

        // Mount-window fault flag — see ONERROR HONESTY above. @Volatile-free
        // on purpose: during the window the callback fires on THIS thread
        // (sync-mount contract), so the flag is same-thread visible.
        var mountFault = false
        var mounting = true
        val runtime = BlazorNativeRuntime(
            onFrame = state::onFrame,
            onError = { msg, t ->
                if (mounting) mountFault = true
                state.logError(msg, t)
                System.err.println("[InspectorHost] $msg: $t")
            },
        )
        val bootLines = runtime.start(
            componentName = component,
            platformOs = "inspector-host",
            bridge = InspectorHostBridge(),
        )
        mounting = false
        bootLines.forEach(::println)
        if (mountFault) {
            System.err.println(
                "[InspectorHost] tree is PARTIAL — a frame was dropped during mount " +
                    "(detail above); refusing to serve a partial tree"
            )
            exitProcess(1)
        }

        val server = try {
            InspectorServer(
                state,
                dispatch = runtime::dispatchEventAndWait,
                requestedPort = port,
                componentName = component,
            )
        } catch (e: BindException) {
            System.err.println(
                "[InspectorHost] cannot bind 127.0.0.1:$port (${e.message}) — is another " +
                    "inspector running? Override the port: gradlew runInspectorHost -Pport=<free port>"
            )
            exitProcess(1)
        }
        server.start()
        println("[HOST] inspecting '$component' at http://127.0.0.1:${server.port}/")
        println("[HOST] fast-restart, not hot-reload: restart this host to pick up a rebuilt dll (Ctrl+C to stop)")

        // Serve until the process is killed (Ctrl+C / task cancel).
        CountDownLatch(1).await()
    } catch (t: Throwable) {
        System.err.println("[InspectorHost] FAILED to serve '$component': $t")
        exitProcess(1)
    }
}
