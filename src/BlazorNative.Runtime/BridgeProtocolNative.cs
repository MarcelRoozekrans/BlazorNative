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
// Invariants both sides rely on:
//   (a) Any FUTURE error code must keep |code| well below 4096 (the default
//       buffer cap) — negative values whose magnitude exceeds the offered
//       cap are interpreted as -needed size demands, never as error codes.
//   (b) The host must NEVER return -needed when the value actually fits in
//       the offered cap — a negative return with |code| <= cap is always
//       treated as an error code by the .NET side.
//   (c) rc == 0 from a buffer callback is tolerated and decodes as the empty
//       string (deliberate .NET-side leniency; the contract minimum for a
//       successful write is 1 — the NUL terminator alone).
//
// Lifetime rules (mirror the frame protocol — each side copies inside the
// call it receives):
//   • Input strings (routeUtf8, keyUtf8, valueUtf8) are .NET-owned,
//     valid ONLY for the duration of the callback; the host copies.
//   • BlazorNativeFetchRequest + every string it references is .NET-owned,
//     valid ONLY during FetchBegin; the host copies before returning.
//   • BlazorNativeFetchResponse + every string it references is host-owned,
//     valid ONLY during blazornative_fetch_complete; .NET copies.
//   • The payloadJsonUtf8 string passed to blazornative_host_call_complete
//     (Phase 9.0) is host-owned, valid ONLY during that call; .NET copies it
//     (flat JSON → typed result) before returning. NULL = no payload (every
//     non-Granted status carries none).
//   • The callbacks struct itself is COPIED by blazornative_register_bridge —
//     the host may free its struct memory after the call (the function
//     pointers themselves must stay alive: JNA-side STRONG refs, same GC
//     rule as the frame callback).
//   • Re-registration (last wins) swaps an immutable snapshot atomically,
//     but an in-flight operation may still be invoking the PREVIOUS
//     snapshot's function pointers. A re-registering host must keep the
//     previous callback objects alive until in-flight operations have
//     drained — or simply keep them alive forever (recommended for this
//     POC: park superseded registrations in a list, never release them).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The host-implemented shell operations, registered once at boot via
/// <c>blazornative_register_bridge</c>. All pointers are cdecl <c>int</c>-returning
/// functions — see the return-code table above.
///
/// SIZE-NEGOTIATED GROWTH (Phase 5.4, DoD #6; Phase 9.0 grew it again 72→80): the
/// struct grows by APPENDING new slots at the end; the existing offsets (0…64) never
/// move. register_bridge takes a leading <c>structSize</c> and the runtime copies
/// <c>min(structSize, sizeof)</c> bytes + zero-fills the tail, so an OLD shell
/// (72-byte struct) and a NEW runtime (80-byte struct) interoperate: the un-supplied
/// slots read back as
/// <c>IntPtr.Zero</c> and the managed side surfaces them as "not supported"
/// (NotSupportedException) rather than crashing. A null slot is ALWAYS the
/// capability-unsupported signal — never dereferenced.
///
/// Phase 5.4 appended clipboard read/write + share (offsets 48/56/64). If you add a
/// slot, append it here, in the Kotlin @Structure.FieldOrder mirror, in the Swift
/// header, and pin its offset in BOTH drift tests (BridgeProtocolNativeTests.cs /
/// ShellBridgeTest.kt).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct BlazorNativeBridgeCallbacks           // 10 × IntPtr = 80 bytes
{
    public IntPtr Navigate;        // offset 0  — int (const char* routeUtf8)
    public IntPtr CurrentRoute;    // offset 8  — int (char* buf, int cap)
    public IntPtr StorageRead;     // offset 16 — int (const char* keyUtf8, char* buf, int cap)
    public IntPtr StorageWrite;    // offset 24 — int (const char* keyUtf8, const char* valueUtf8)
    public IntPtr StorageDelete;   // offset 32 — int (const char* keyUtf8)
    public IntPtr FetchBegin;      // offset 40 — int (long requestId, BlazorNativeFetchRequest* req)
    public IntPtr ClipboardRead;   // offset 48 — int (char* buf, int cap)   — null = unsupported
    public IntPtr ClipboardWrite;  // offset 56 — int (const char* textUtf8)  — null = unsupported
    public IntPtr Share;           // offset 64 — int (const char* textUtf8)  — null = unsupported
    // Phase 9.0 (M9 DoD #1): the GENERIC permission-gated async-begin. .NET assigns
    // an Interlocked requestId, parks a TCS, then calls this; the host runs the whole
    // permission dance (check → prompt → obtain/deny) and later PUSHES the tri-state
    // result via blazornative_host_call_complete. `op` selects the capability
    // (0 = Geolocation in 9.0); args cross as flat JSON. null = unsupported (an old
    // shell predating the slot — RequireSlot surfaces NotSupportedException).
    public IntPtr HostCallBegin;   // offset 72 — int (long requestId, int op, const char* argsJsonUtf8) — null = unsupported
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
