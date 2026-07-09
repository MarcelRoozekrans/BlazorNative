package io.blazornative.shell

import android.content.Context
import android.util.Log
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import io.blazornative.jni.BridgeRegistrar
import io.blazornative.jni.NativeBindings
import org.junit.AfterClass
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import java.io.File
import java.net.InetAddress
import java.net.ServerSocket

/**
 * Phase 3.1 Gate 3 — the shell bridge live on the device: the SAME
 * BridgeRegistrar/probes path Gate 2 proved on the desktop JVM, now with the
 * REAL Android backends — SharedPreferences storage and an HttpURLConnection
 * fetch against a local ServerSocket responder — inside the bionic .so from
 * the APK's jniLibs.
 *
 * Registration lives in the companion object: BridgeRegistrar is ONE-SHOT per
 * instance and every registration is parked for the process lifetime, so the
 * class registers exactly ONE registrar for the whole instrumentation process
 * (all test methods — and any other test class running after us — share it;
 * re-registration by another class is last-wins and safe). Probes need no
 * blazornative_init: they run purely against the registered callbacks.
 */
@RunWith(AndroidJUnit4::class)
class ShellBridgeAndroidTest {

    companion object {
        private const val TAG = "BlazorNativeTest"

        private val appContext: Context =
            InstrumentationRegistry.getInstrumentation().targetContext.applicationContext

        /** The handlers under test — the production AndroidShellBridge. */
        private val bridge = AndroidShellBridge(appContext) { msg, t -> Log.e(TAG, msg, t) }

        /** Once-per-process registration (one-shot registrar contract). */
        private val registered: Boolean by lazy {
            BridgeRegistrar(bridge) { msg, t -> Log.e(TAG, msg, t) }.register()
            true
        }

        /**
         * Minimal localhost HTTP/1.1 responder — the same ServerSocket fixture
         * shape as ShellBridgeTest's TinyHttpServer (JVM): fixed 200 +
         * "probe-ok" for every request, daemon accept-loop thread, closed in
         * [tearDown].
         */
        private val serverSocket = ServerSocket(0, 8, InetAddress.getByName("127.0.0.1"))
        private val serverPort: Int = serverSocket.localPort

        init {
            Thread {
                try {
                    while (true) {
                        val client = serverSocket.accept()
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
            }.apply { isDaemon = true; name = "ShellBridgeAndroidTest-Responder"; start() }
        }

        @JvmStatic
        @AfterClass
        fun tearDown() {
            serverSocket.close()
        }
    }

    @Before
    fun ensureRegistered() {
        check(registered) { "bridge registration failed" }
    }

    /**
     * The six ops end-to-end INSIDE the .so on the device: navigate
     * round-trip, SharedPreferences write/read/delete + absent-key null, and
     * one real HttpURLConnection fetch against the local responder, completed
     * asynchronously via blazornative_fetch_complete.
     */
    @Test
    fun bridge_probes_pass_on_device() {
        val url = "http://127.0.0.1:$serverPort/probe"
        val result = NativeBindings.INSTANCE
            .blazornative_run_bridge_probes(url.toByteArray(Charsets.UTF_8) + 0)
        val detail = result.errorMessage?.getString(0, "UTF-8") ?: "<null>"
        val label = result.versionString?.getString(0, "UTF-8") ?: "<null>"
        Log.i(TAG, "[ShellBridgeAndroidTest] probes status=${result.status} label='$label' detail='$detail'")

        assertEquals("bridge probes failed: $detail", 0, result.status)
        assertEquals("probes:navigate,storage,fetch", label)
    }

    /**
     * Storage lands in the REAL SharedPreferences store — written through the
     * registered handlers instance, read back through the SharedPreferences
     * API, and (the durability proof) already present in the backing on-disk
     * XML file: AndroidShellBridge uses commit(), which is synchronous-to-
     * disk, so the file assertion is deterministic.
     */
    @Test
    fun storage_persists_via_sharedpreferences() {
        try {
            bridge.storageWrite("test-key", "test-value")

            val prefs = appContext.getSharedPreferences("blazornative", Context.MODE_PRIVATE)
            assertEquals("test-value", prefs.getString("test-key", null))

            // A handle obtained fresh from the instrumentation's own context
            // object sees the same committed value.
            val freshHandle = InstrumentationRegistry.getInstrumentation()
                .targetContext.getSharedPreferences("blazornative", Context.MODE_PRIVATE)
            assertEquals("test-value", freshHandle.getString("test-key", null))

            // REAL durability: commit() has already flushed the backing XML
            // file — not just the process-wide in-memory map.
            val prefsFile = File(appContext.dataDir, "shared_prefs/blazornative.xml")
            assertTrue("SharedPreferences file missing: $prefsFile", prefsFile.exists())
            assertTrue(
                "committed key absent from the on-disk XML: $prefsFile",
                prefsFile.readText().contains("test-key")
            )
        } finally {
            bridge.storageDelete("test-key")
        }
        assertNull(
            "delete must remove the key from SharedPreferences",
            appContext.getSharedPreferences("blazornative", Context.MODE_PRIVATE)
                .getString("test-key", null)
        )
    }
}
