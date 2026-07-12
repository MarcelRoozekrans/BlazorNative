package io.blazornative.jni

import com.sun.jna.Memory
import com.sun.jna.Native
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNotNull
import org.junit.jupiter.api.Assertions.assertTrue

/**
 * Phase 3.0b Gate 2 — load BlazorNative.Runtime.dll via JNA on Windows JVM,
 * call blazornative_init, assert Status == 0 + non-null version string.
 * (Began life alongside the wasmtime-era BootSmokeTest, deleted in 3.0c;
 * the library carries its final name since the Phase 3.0e rename.)
 */
class BootSmokeNativeTest {

    /**
     * Sanity check: BlazorNativeInitResult must be 24 bytes on x64
     * (4-byte Status + 4 padding + 8 ErrorMessage + 8 VersionString).
     * If JNA reports a different size, struct field order is off and
     * versionString will read garbage — catch it early.
     */
    @Test
    fun blazor_native_init_result_struct_size_is_24_bytes() {
        val size = Native.getNativeSize(BlazorNativeInitResult.ByValue::class.java)
        assertEquals(
            24, size,
            "Expected BlazorNativeInitResult.ByValue to be 24 bytes on x64 " +
                "(4 Status + 4 pad + 8 ErrorMessage + 8 VersionString); got $size. " +
                "If wrong, the C-side StructLayout drifted from the Kotlin FieldOrder."
        )
    }

    @Test
    fun nativeaot_init_returns_status_zero_and_nonnull_version() {
        // Allocate caller-side platform info strings — we pass an "test-host"
        // shape so the host side's BlazorInterop.EnsureInitialized is the
        // load-bearing thing being tested, not platform-info passing.
        val osBytes = "test-host".toByteArray(Charsets.UTF_8) + 0
        val osMem = Memory(osBytes.size.toLong()).apply { write(0, osBytes, 0, osBytes.size) }
        val noteBytes = "phase-3.0b-bootsmoke".toByteArray(Charsets.UTF_8) + 0
        val noteMem = Memory(noteBytes.size.toLong()).apply { write(0, noteBytes, 0, noteBytes.size) }

        val opts = BlazorNativeInitOptions.ByReference().apply {
            platformInfoOs = osMem
            platformInfoApiLevel = 0
            platformInfoNote = noteMem
        }

        val result = NativeBindings.INSTANCE.blazornative_init(opts)

        // Diagnostic: print error message even on success (it should be "").
        val errorMessage = result.errorMessage?.getString(0L) ?: "<null>"
        val versionString = result.versionString?.getString(0L) ?: "<null>"
        println("[BootSmokeNativeTest] status=${result.status} version='$versionString' error='$errorMessage'")

        assertEquals(
            0, result.status,
            "Expected blazornative_init to return Status=0; got ${result.status} with errorMessage='$errorMessage'"
        )
        assertNotNull(result.versionString, "Expected non-null VersionString pointer")
        assertTrue(
            versionString.contains("BlazorNative.Runtime"),
            "Expected version string to mention 'BlazorNative.Runtime'; got '$versionString'"
        )
        assertTrue(
            versionString.contains("phase-5.1"),
            "Expected version string to mention 'phase-5.1'; got '$versionString'"
        )
    }
}
