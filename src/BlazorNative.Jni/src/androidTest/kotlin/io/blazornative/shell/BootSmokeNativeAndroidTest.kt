package io.blazornative.shell

import androidx.test.ext.junit.runners.AndroidJUnit4
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
 */
@RunWith(AndroidJUnit4::class)
class BootSmokeNativeAndroidTest {

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
        val opts = BlazorNativeInitOptions.ByReference()
        val result = NativeBindings.INSTANCE.blazornative_init(opts)
        val error = result.errorMessage?.getString(0, "UTF-8") ?: ""
        assertEquals("init failed: $error", 0, result.status)
        val version = result.versionString?.getString(0, "UTF-8") ?: ""
        assertTrue("unexpected version: $version", version.contains("BlazorNative.NativeHost"))
    }
}
