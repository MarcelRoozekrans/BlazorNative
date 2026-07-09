using System.Runtime.InteropServices;

namespace BlazorNative.Runtime;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3.1 shell-bridge C-ABI — the reverse direction: .NET calls INTO the
// host through six function pointers the host registers once at boot via
// blazornative_register_bridge (exactly like the frame callback, ×6).
//
// Layout contract: mirrored by the JNA Structure in
// src/BlazorNative.Jni/src/main/kotlin/io/blazornative/jni/ShellBridge.kt.
// Sizes + per-field offsets asserted on both sides
// (BridgeProtocolNativeTests.cs / ShellBridgeTest.kt). If you change ANY
// field, update the Kotlin mirror + both drift tests.
//
// Return-code protocol (all six sync callbacks return int, cdecl):
//   >= 0     success — for buffer-writing calls (CurrentRoute, StorageRead)
//            this is the byte count written INCLUDING the NUL terminator
//   -needed  buffer too small — |value| is the exact byte count required
//            (incl. NUL); the .NET side retries ONCE at that exact size,
//            a second failure throws. Only meaningful when |value| > cap.
//   -1       host error (a throw inside a Kotlin callback is caught
//            host-side and surfaces as -1 — it never crosses the ABI)
//   -2       key absent (StorageRead only — maps to null)
//
// Lifetime rules (mirror the frame protocol — each side copies inside the
// call it receives):
//   • Input strings (routeUtf8, keyUtf8, valueUtf8) are .NET-owned,
//     valid ONLY for the duration of the callback; the host copies.
//   • BlazorNativeFetchRequest + every string it references is .NET-owned,
//     valid ONLY during FetchBegin; the host copies before returning.
//   • BlazorNativeFetchResponse + every string it references is host-owned,
//     valid ONLY during blazornative_fetch_complete; .NET copies.
//   • The callbacks struct itself is COPIED by blazornative_register_bridge —
//     the host may free its struct memory after the call (the function
//     pointers themselves must stay alive: JNA-side STRONG refs, same GC
//     rule as the frame callback).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The six host-implemented shell operations, registered once at
/// boot via <c>blazornative_register_bridge</c>. All pointers are cdecl
/// <c>int</c>-returning functions — see the return-code table above.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct BlazorNativeBridgeCallbacks           // 6 × IntPtr = 48 bytes
{
    public IntPtr Navigate;        // offset 0  — int (const char* routeUtf8)
    public IntPtr CurrentRoute;    // offset 8  — int (char* buf, int cap)
    public IntPtr StorageRead;     // offset 16 — int (const char* keyUtf8, char* buf, int cap)
    public IntPtr StorageWrite;    // offset 24 — int (const char* keyUtf8, const char* valueUtf8)
    public IntPtr StorageDelete;   // offset 32 — int (const char* keyUtf8)
    public IntPtr FetchBegin;      // offset 40 — int (long requestId, BlazorNativeFetchRequest* req)
}

/// <summary>Fetch request handed to the host's FetchBegin callback.
/// .NET-owned; the struct and every string it references are valid ONLY for
/// the duration of the FetchBegin call. Headers cross as a flat JSON object
/// string (<c>{"k":"v",...}</c>).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct BlazorNativeFetchRequest              // 4 × IntPtr = 32 bytes
{
    public IntPtr Url;             // offset 0  — const char*, never NULL
    public IntPtr Method;          // offset 8  — const char*, never NULL ("GET", ...)
    public IntPtr Body;            // offset 16 — const char*, NULL = no body
    public IntPtr HeadersJson;     // offset 24 — const char* flat {"k":"v"}, NULL = no headers
}

/// <summary>Fetch response the host passes to
/// <c>blazornative_fetch_complete</c>. Host-owned; valid ONLY during that
/// call — .NET copies everything before returning.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct BlazorNativeFetchResponse             // 2 × int + 3 × IntPtr = 32 bytes
{
    public int    StatusCode;      // offset 0  — HTTP status (meaningful when Ok != 0)
    public int    Ok;              // offset 4  — 0 = transport error (see ErrorMessage)
    public IntPtr BodyUtf8;        // offset 8  — const char*, NULL = empty body
    public IntPtr ErrorMessage;    // offset 16 — const char*, set when Ok == 0
    public IntPtr HeadersJson;     // offset 24 — const char* flat {"k":"v"}, NULL = none
}
