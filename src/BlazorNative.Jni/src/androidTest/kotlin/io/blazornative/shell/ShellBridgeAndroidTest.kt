package io.blazornative.shell

import android.content.Context
import android.util.Log
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.io.File

/**
 * Phase 3.1 Gate 3 — the REAL Android storage backend behind the shell
 * bridge: AndroidShellBridge's SharedPreferences handlers driven directly,
 * with an on-disk durability assertion.
 *
 * Phase 3.5 (M3 close): bridge_probes_pass_on_device (and its localhost HTTP
 * responder + BridgeRegistrar fixture) was deleted with the
 * blazornative_run_bridge_probes export — the full register_bridge →
 * navigate/storage/fetch path inside the .so is covered by the real-component
 * instrumented tests (BnDemoAndroidTest / NavigationAndroidTest) booting via
 * BlazorNativeRuntime. This class keeps the storage test because it never
 * called the probe export: it drives the handlers instance directly.
 */
@RunWith(AndroidJUnit4::class)
class ShellBridgeAndroidTest {

    companion object {
        private const val TAG = "BlazorNativeTest"

        private val appContext: Context =
            InstrumentationRegistry.getInstrumentation().targetContext.applicationContext

        /** The handlers under test — the production AndroidShellBridge. */
        private val bridge = AndroidShellBridge(appContext) { msg, t -> Log.e(TAG, msg, t) }
    }

    /**
     * Storage lands in the REAL SharedPreferences store — written through the
     * handlers instance, read back through the SharedPreferences API, and
     * (the durability proof) already present in the backing on-disk XML file:
     * AndroidShellBridge uses commit(), which is synchronous-to-disk, so the
     * file assertion is deterministic.
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
