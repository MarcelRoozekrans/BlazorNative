package io.blazornative.jni

import com.sun.jna.Library
import com.sun.jna.Native
import com.sun.jna.Pointer
import com.sun.jna.Structure

/**
 * JNA bindings for BlazorNative.Runtime — the NativeAOT-compiled replacement
 * for the wasmtime/.wasm path (Phase 3.0b+; final name since Phase 3.0e).
 *
 * Loaded lazily on first INSTANCE access; JNA searches the path declared in
 * the `jna.library.path` system property (set by build.gradle.kts to point at
 * the NativeAOT publish output directory).
 *
 * Phase 3.0b: minimum surface for boot smoke — init, shutdown, version.
 * Phase 3.0c adds the run_trim_probes diagnostic (Gate 4).
 * Phase 3.0d extends with frame callback registration + event dispatch.
 *
 * See docs/plans/2026-05-31-phase-3.0b-design.md for the C-ABI contract.
 */
interface NativeBindings : Library {

    fun blazornative_init(opts: BlazorNativeInitOptions.ByReference): BlazorNativeInitResult.ByValue
    fun blazornative_shutdown()
    fun blazornative_version(): Pointer

    /**
     * Phase 3.0c Gate 4 diagnostic export: mounts the IL2072 trim probes inside
     * the NativeAOT library. Status = failed probe count (0 = all pass, -1 =
     * runner crash); ErrorMessage carries per-probe failure detail.
     */
    fun blazornative_run_trim_probes(): BlazorNativeInitResult.ByValue

    /**
     * Phase 3.0d: registers the frame callback the runtime invokes (synchronously,
     * on the mounting thread) with a `BlazorNativeFrame*` per render frame.
     * Returns 0 on success; re-registration is allowed (last wins).
     *
     * LIFETIME: the [callback] object MUST be strongly referenced by the caller
     * for as long as it is registered — JNA callback trampolines are GC-eligible
     * once the Java object is unreachable, after which the native side calls a
     * dangling pointer. BlazorNativeRuntime holds it for the app lifetime (Gate 3).
     */
    fun blazornative_register_frame_callback(callback: FrameCallback): Int

    /**
     * Phase 3.0d: mounts a registered component by name. [componentName] is
     * NUL-terminated UTF-8 bytes (append a trailing 0 to the encoded string).
     * The first frame callback fires BEFORE this returns (sync mount contract).
     * Status: 0 ok / 1 unknown component / 2 mount threw (detail on the
     * process stderr) / 3 name pointer null.
     */
    fun blazornative_mount(componentName: ByteArray): Int

    /**
     * Cdecl `void (*)(BlazorNativeFrame*)`. The frame pointer (and every
     * string it references) is valid ONLY during the invocation — decode with
     * [NativeFrameAdapter.read] before returning (it copies everything).
     *
     * EXCEPTION POSTURE: if [invoke] throws, JNA's default callback exception
     * handler prints the stack trace to stderr and returns normally to native
     * code — the frame is SILENTLY DROPPED (no crash, nothing propagates to
     * the mount call). Gate 3's BlazorNativeRuntime therefore wraps the
     * callback body in try/catch routed to its pluggable onError sink
     * (android.util.Log only when the Activity wires it) so dropped frames
     * surface deliberately instead of vanishing into an untailed stderr.
     */
    interface FrameCallback : com.sun.jna.Callback {
        fun invoke(frame: Pointer)
    }

    companion object {
        // JNA library name "BlazorNative.Runtime" → maps to:
        //   Windows: BlazorNative.Runtime.dll
        //   Bionic:  libBlazorNative.Runtime.so (JNA prepends "lib" + appends ".so")
        // JNA's native loader strips/adds prefixes appropriately per platform.
        val INSTANCE: NativeBindings = Native.load("BlazorNative.Runtime", NativeBindings::class.java)
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
