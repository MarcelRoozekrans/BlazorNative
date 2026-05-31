package io.blazornative.jni

import com.sun.jna.Library
import com.sun.jna.Native
import com.sun.jna.Pointer
import com.sun.jna.Structure

/**
 * JNA bindings for BlazorNative.NativeHost — the NativeAOT-compiled replacement
 * for the wasmtime/.wasm path (Phase 3.0b+).
 *
 * Loaded lazily on first INSTANCE access; JNA searches the path declared in
 * the `jna.library.path` system property (set by build.gradle.kts to point at
 * the NativeAOT publish output directory).
 *
 * Phase 3.0b: minimum surface for boot smoke — init, shutdown, version.
 * Phase 3.0c+ extends with frame callback registration + event dispatch.
 *
 * See docs/plans/2026-05-31-phase-3.0b-design.md for the C-ABI contract.
 */
interface NativeBindings : Library {

    fun blazornative_init(opts: BlazorNativeInitOptions.ByReference): BlazorNativeInitResult.ByValue
    fun blazornative_shutdown()
    fun blazornative_version(): Pointer

    companion object {
        // JNA library name "BlazorNative.NativeHost" → maps to:
        //   Windows: BlazorNative.NativeHost.dll
        //   Bionic:  libBlazorNative.NativeHost.so (JNA prepends "lib" + appends ".so")
        // JNA's native loader strips/adds prefixes appropriately per platform.
        val INSTANCE: NativeBindings = Native.load("BlazorNative.NativeHost", NativeBindings::class.java)
    }
}

/**
 * Mirror of:
 *   [StructLayout(LayoutKind.Sequential)]
 *   public struct BlazorNativeInitOptions {
 *       public IntPtr PlatformInfoOs;        // const char* — host-allocated UTF-8
 *       public int    PlatformInfoApiLevel;
 *       public IntPtr PlatformInfoNote;      // const char* — optional
 *   }
 *
 * x64 layout (16-byte aligned): 8 + 4 + (4 pad) + 8 = 24 bytes (when passed as
 * standalone struct). JNA computes size from FieldOrder + field types.
 */
@Structure.FieldOrder("platformInfoOs", "platformInfoApiLevel", "platformInfoNote")
open class BlazorNativeInitOptions : Structure() {
    @JvmField var platformInfoOs: Pointer? = null
    @JvmField var platformInfoApiLevel: Int = 0
    @JvmField var platformInfoNote: Pointer? = null

    class ByReference : BlazorNativeInitOptions(), Structure.ByReference
}

/**
 * Mirror of:
 *   [StructLayout(LayoutKind.Sequential)]
 *   public struct BlazorNativeInitResult {
 *       public int    Status;                // 0 = success
 *       public IntPtr ErrorMessage;          // const char* — set on Status != 0
 *       public IntPtr VersionString;         // const char* — static, never freed
 *   }
 *
 * x64 layout: 4 + (4 pad) + 8 + 8 = 24 bytes. The BootSmokeNativeTest asserts
 * Native.getNativeSize == 24 to catch struct-field-order drift early.
 */
@Structure.FieldOrder("status", "errorMessage", "versionString")
open class BlazorNativeInitResult : Structure() {
    @JvmField var status: Int = 0
    @JvmField var errorMessage: Pointer? = null
    @JvmField var versionString: Pointer? = null

    class ByValue : BlazorNativeInitResult(), Structure.ByValue
}
