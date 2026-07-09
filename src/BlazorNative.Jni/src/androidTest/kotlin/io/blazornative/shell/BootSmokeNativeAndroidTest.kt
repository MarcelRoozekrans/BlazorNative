package io.blazornative.shell

import android.os.Build
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.sun.jna.Memory
import com.sun.jna.Native
import io.blazornative.jni.BlazorNativeInitOptions
import io.blazornative.jni.BlazorNativeInitResult
import io.blazornative.jni.NativeBindings
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

/**
 * Phase 3.0c Gate 3: the C-ABI-parity proof. The same NativeBindings interface
 * the desktop JVM uses — no wasmtime, no Java-side runtime bootstrap — loads
 * the NativeAOT .so from the APK's jniLibs and boots.
 *
 * Phase 3.0c Gate 4 adds trim_probes_pass_on_device: the 4 accepted IL2072
 * call paths from Phase 3.0a run INSIDE the trimmed .so on the device.
 */
@RunWith(AndroidJUnit4::class)
class BootSmokeNativeAndroidTest {

    /** Caller-allocated NUL-terminated UTF-8 cstring for input pointers. */
    private fun utf8CString(s: String): Memory {
        val bytes = s.toByteArray(Charsets.UTF_8) + 0
        return Memory(bytes.size.toLong()).apply { write(0, bytes, 0, bytes.size) }
    }

    @Test
    fun struct_sizes_match_c_abi() {
        assertEquals(24, Native.getNativeSize(BlazorNativeInitResult.ByValue::class.java))
        // Native.getNativeSize(Class) returns POINTER_SIZE for non-ByValue
        // Structures (they're passed by pointer) — measure the actual struct
        // layout via an instance instead.
        assertEquals(24, BlazorNativeInitOptions.ByReference().size())
    }

    @Test
    fun init_returns_status_zero_with_version() {
        // Gate 3 review follow-up: pass REAL platform-info strings instead of
        // null pointers. Init doesn't dereference PlatformInfoOs yet, so this
        // proves the options struct populates and the call survives with live
        // input pointers on bionic — full content round-trip lands when a real
        // consumer export exists (Phase 3.0d).
        val osMem = utf8CString("android-emulator")
        val noteMem = utf8CString("phase-3.0c-gate4-bootsmoke")
        val opts = BlazorNativeInitOptions.ByReference().apply {
            platformInfoOs = osMem
            platformInfoApiLevel = Build.VERSION.SDK_INT
            platformInfoNote = noteMem
        }

        val result = NativeBindings.INSTANCE.blazornative_init(opts)
        val error = result.errorMessage?.getString(0, "UTF-8") ?: ""
        assertEquals("init failed: $error", 0, result.status)
        val version = result.versionString?.getString(0, "UTF-8") ?: ""
        assertTrue("unexpected version: $version", version.contains("BlazorNative.Runtime"))
        assertTrue("unexpected version: $version", version.contains("phase-3.0e"))

        // Gate 3 review follow-up: cross-check the second export shape —
        // blazornative_version() (bare pointer return) must agree with the
        // struct-marshaled VersionString.
        val standaloneVersion = NativeBindings.INSTANCE.blazornative_version().getString(0, "UTF-8")
        assertEquals("blazornative_version() disagrees with init VersionString", version, standaloneVersion)
    }

    /**
     * Phase 3.0c Gate 4 — the 4 accepted IL2072 call paths (ComponentProperties
     * .SetProperties ×2, FindCascadingParameters, PerformPropertyInjection) run
     * INSIDE the NativeAOT-trimmed .so on the device via
     * blazornative_run_trim_probes. Status = failed probe count; ErrorMessage =
     * per-probe detail.
     */
    @Test
    fun trim_probes_pass_on_device() {
        val result = NativeBindings.INSTANCE.blazornative_run_trim_probes()
        val detail = result.errorMessage?.getString(0, "UTF-8") ?: ""
        assertEquals("failed probes: $detail", 0, result.status)
    }
}
