package io.blazornative.jni

import com.sun.jna.Callback
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
 * Phase 3.0d extends with frame callback registration + event dispatch.
 * Phase 3.5 (M3 close) deleted the two diagnostic probe exports
 * (run_trim_probes, run_bridge_probes) — superseded by real components under
 * strict mode + production bridge use. Eight exports remain.
 *
 * See docs/plans/2026-05-31-phase-3.0b-design.md for the C-ABI contract.
 */
interface NativeBindings : Library {

    fun blazornative_init(opts: BlazorNativeInitOptions.ByReference): BlazorNativeInitResult.ByValue
    fun blazornative_shutdown()
    fun blazornative_version(): Pointer

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
     * Phase 3.2: host→renderer event ingress — dispatches a UI event to the
     * Blazor handler registered under [handlerId] (harvested from an
     * AttachEvent patch). [argsJsonUtf8] is NUL-terminated UTF-8 flat JSON
     * (the 3.1 FlatJson pair): `{"name":"click"}` /
     * `{"name":"change","payload":"…"}`.
     *
     * Return codes:
     *   0 = dispatched (incl. stale-handler at-most-once)
     *   1 = no session/nothing mounted
     *   2 = dispatch faulted — the handler, the resulting re-render, or frame
     *       delivery threw (detail on native stderr)
     *   3 = malformed/NULL args OR handlerId > int.MaxValue
     *
     * SYNCHRONOUS: the handler, the re-render, AND the frame callback all
     * complete before this returns (InlineDispatcher contract in Exports.cs) —
     * frames still fire only inside host calls (mount OR dispatch).
     * THREADING: never call from the UI thread — all post-boot .NET entry
     * serializes through BlazorNativeRuntime's BlazorNative-Dispatch lane
     * (see the threading contract on [BlazorNativeRuntime.dispatchEvent]).
     */
    fun blazornative_dispatch_event(handlerId: Long, argsJsonUtf8: ByteArray): Int

    /**
     * Phase 3.1: copies the host's six-callback struct into the runtime's
     * shell bridge (the struct memory may be freed after this returns; the
     * CALLBACK OBJECTS must stay strongly referenced — see the lifetime note
     * on [BlazorNativeBridgeCallbacks]). Returns 0 on success, 2 on null
     * pointer / failure (detail on the process stderr). Re-registration is
     * allowed (last wins) — but the PREVIOUS registration's callback objects
     * must stay alive too (an in-flight .NET op may still hold the old
     * snapshot); BridgeRegistrar parks every registration forever (POC rule
     * from BridgeProtocolNative.cs). Call BEFORE blazornative_mount so
     * components resolving IMobileBridge find a live host.
     */
    fun blazornative_register_bridge(callbacks: BlazorNativeBridgeCallbacks): Int

    /**
     * Phase 3.1: delivers the async fetch response for a FetchBegin request
     * id. The response struct + every string it references are HOST-owned and
     * must stay valid ONLY for the duration of this call (.NET copies before
     * returning) — keep the backing JNA Memory objects referenced in locals
     * across the call ([BridgeFetchCompleter] does). Return codes:
     *   0 = delivered
     *   1 = unknown / already-completed id — benign cancellation race, ignore
     *   2 = invalid call or internal bridge failure — log LOUDLY; detail
     *       lands on the runtime's stderr
     */
    fun blazornative_fetch_complete(requestId: Long, response: BlazorNativeFetchResponse.ByReference): Int

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

    // ── Phase 3.1 shell-bridge callbacks ─────────────────────────────────────
    // Six cdecl int-returning function pointers, registered as one struct via
    // blazornative_register_bridge. Return-code protocol (from
    // BridgeProtocolNative.cs):
    //   >= 0     success — buffer-writing calls (CurrentRoute, StorageRead)
    //            return the byte count written INCLUDING the NUL terminator
    //   -needed  buffer too small; |value| = exact bytes required incl. NUL —
    //            ONLY when the value genuinely does not fit in cap (a negative
    //            with |value| <= cap is always read as an ERROR code)
    //   -1       host error (thrown Kotlin exceptions must be caught in the
    //            callback body and mapped to -1 — if a throw escapes into
    //            JNA's default handler the native side sees a garbage 0
    //            "success"; BridgeRegistrar guards every body)
    //   -2       key absent (StorageRead only — maps to null .NET-side)
    //
    // LIFETIME: input strings (route/key/value pointers) are .NET-owned and
    // valid ONLY during the callback — copy before returning (getString
    // copies). The FetchBegin request struct + every string it references is
    // likewise valid ONLY during FetchBegin. STRONG-ref rule: whoever
    // registers these callback objects must keep them strongly referenced
    // forever (same GC rule as FrameCallback) — BridgeRegistrar holds them as
    // fields and parks itself in a never-released list on register().

    /** Cdecl `int (const char* routeUtf8)`. */
    interface BridgeNavigateCallback : Callback {
        fun invoke(routeUtf8: Pointer): Int
    }

    /** Cdecl `int (char* buf, int cap)` — buffer protocol. */
    interface BridgeCurrentRouteCallback : Callback {
        fun invoke(buf: Pointer, cap: Int): Int
    }

    /** Cdecl `int (const char* keyUtf8, char* buf, int cap)` — buffer protocol, -2 = absent. */
    interface BridgeStorageReadCallback : Callback {
        fun invoke(keyUtf8: Pointer, buf: Pointer, cap: Int): Int
    }

    /** Cdecl `int (const char* keyUtf8, const char* valueUtf8)`. */
    interface BridgeStorageWriteCallback : Callback {
        fun invoke(keyUtf8: Pointer, valueUtf8: Pointer): Int
    }

    /** Cdecl `int (const char* keyUtf8)`. */
    interface BridgeStorageDeleteCallback : Callback {
        fun invoke(keyUtf8: Pointer): Int
    }

    /** Cdecl `int (long requestId, BlazorNativeFetchRequest* req)`. The
     * request memory is valid ONLY during this call — copy everything before
     * returning; the response arrives later via blazornative_fetch_complete. */
    interface BridgeFetchBeginCallback : Callback {
        fun invoke(requestId: Long, request: Pointer): Int
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

/**
 * Mirror of BridgeProtocolNative.cs BlazorNativeBridgeCallbacks — the six
 * host-implemented shell operations, registered once at boot via
 * `blazornative_register_bridge`. JNA maps Callback-typed Structure fields to
 * native function pointers automatically.
 *
 * x64 layout: 6 × 8-byte fn pointers = 48 bytes — asserted on both sides
 * (ShellBridgeTest.kt here, BridgeProtocolNativeTests.cs on .NET). If you
 * change ANY field, update the .NET mirror + both drift tests.
 *
 * The struct itself is COPIED by register_bridge (free after the call is
 * fine); the callback OBJECTS must stay strongly referenced — see the
 * lifetime note on the callback interfaces above.
 */
@Structure.FieldOrder("navigate", "currentRoute", "storageRead", "storageWrite", "storageDelete", "fetchBegin")
open class BlazorNativeBridgeCallbacks : Structure() {
    @JvmField var navigate: NativeBindings.BridgeNavigateCallback? = null           // offset 0
    @JvmField var currentRoute: NativeBindings.BridgeCurrentRouteCallback? = null   // offset 8
    @JvmField var storageRead: NativeBindings.BridgeStorageReadCallback? = null     // offset 16
    @JvmField var storageWrite: NativeBindings.BridgeStorageWriteCallback? = null   // offset 24
    @JvmField var storageDelete: NativeBindings.BridgeStorageDeleteCallback? = null // offset 32
    @JvmField var fetchBegin: NativeBindings.BridgeFetchBeginCallback? = null       // offset 40
}

/**
 * Mirror of BridgeProtocolNative.cs BlazorNativeFetchRequest — handed to the
 * host's FetchBegin callback. .NET-owned: the struct and every string it
 * references are valid ONLY during the FetchBegin call — copy before
 * returning (BridgeRegistrar decodes into an immutable [BridgeFetchRequest]
 * inside the callback).
 *
 * x64 layout: 4 × 8-byte pointers = 32 bytes. `body` NULL = no body;
 * `headersJson` NULL = no headers (flat `{"k":"v"}` JSON otherwise) —
 * NULL, not "{}".
 */
@Structure.FieldOrder("url", "method", "body", "headersJson")
open class BlazorNativeFetchRequest : Structure {
    constructor() : super()
    constructor(p: Pointer) : super(p)

    @JvmField var url: Pointer? = null         // offset 0  — never NULL
    @JvmField var method: Pointer? = null      // offset 8  — never NULL ("GET", ...)
    @JvmField var body: Pointer? = null        // offset 16 — NULL = no body
    @JvmField var headersJson: Pointer? = null // offset 24 — NULL = no headers
}

/**
 * Mirror of BridgeProtocolNative.cs BlazorNativeFetchResponse — the response
 * the host passes to `blazornative_fetch_complete`. HOST-owned: the struct +
 * every string it references must stay valid ONLY for the duration of that
 * call (.NET copies before returning) — [BridgeFetchCompleter] keeps the
 * backing JNA Memory objects referenced in locals across the call.
 *
 * x64 layout: 4 (statusCode) + 4 (ok) + 8 + 8 + 8 = 32 bytes.
 */
@Structure.FieldOrder("statusCode", "ok", "bodyUtf8", "errorMessage", "headersJson")
open class BlazorNativeFetchResponse : Structure() {
    @JvmField var statusCode: Int = 0          // offset 0  — HTTP status (meaningful when ok != 0)
    @JvmField var ok: Int = 0                  // offset 4  — 0 = transport error (see errorMessage)
    @JvmField var bodyUtf8: Pointer? = null    // offset 8  — NULL = empty body
    @JvmField var errorMessage: Pointer? = null// offset 16 — set when ok == 0
    @JvmField var headersJson: Pointer? = null // offset 24 — flat {"k":"v"}, NULL = none

    class ByReference : BlazorNativeFetchResponse(), Structure.ByReference
}
