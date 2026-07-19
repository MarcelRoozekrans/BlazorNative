// ─────────────────────────────────────────────────────────────────────────────
// BlazorNative.Runtime C-ABI — Swift bridging header (Phase 5.2 + 5.3).
//
// The Swift/UIKit shell's native-interop surface. Phase 5.2 declared the
// boot+render subset (init / register_frame_callback / mount / version / shutdown
// + the init structs + the frame-callback typedef). Phase 5.3 (M5 DoD #3,
// interactivity) adds the INPUT direction: dispatch_event (taps→renderer) +
// register_bridge (the shell bridge, .NET→host; 10 callbacks since Phase 9.0's
// size-negotiated permission-pattern growth — 9 since Phase 5.4's clipboard/share).
// This is the Swift twin
// of the Kotlin JNA `NativeBindings` interface; Swift's native C interop replaces
// the JNA layer entirely (no reflection, no Structure classes — direct externs).
//
// Phase 9.0 (M9 DoD #1+#2) grows the ABI once, generically: a HostCallBegin slot at
// offset 72 (the async-begin the shell's op-dispatch resolves) takes the struct to
// 80 bytes, and blazornative_host_call_complete is now DECLARED here because the
// Swift shell CALLS it (the fetch_complete twin) to push an async permission-gated
// result — a tri-state status + optional flat-JSON payload — back to .NET.
//
// fetch_complete is the only export still undeclared — the Swift shell does not
// drive fetch yet (the fetch bridge stub fails synchronously; see AppleShellBridge).
// Phase 9.1 declares host_event because the Swift shell starts CALLING it for WARM
// notification tap-through (the reserved "navigate" name) — the 9.0 precedent, where
// iOS started calling the pre-existing host_call_complete for the first time. It adds
// NO export: host_event has been in the archive since Phase 5.1 (ios.yml's nm gate
// already lists it among the ten). All ten blazornative_* exports are present in the
// linked static archive (ios.yml asserts them via `nm -gU`).
//
// Struct-layout contract — mirror of src/BlazorNative.Runtime/Exports.cs
// (BlazorNativeInitOptions / BlazorNativeInitResult, [StructLayout(Sequential)],
// little-endian, 8-byte pointers). The C compiler reproduces the same layout:
//   bn_init_options : os@0(ptr) apiLevel@8(int) pad@12 note@16(ptr) kind@24(int) → 32 bytes  (Phase 10.0 #121)
//   bn_init_result  : status@0(int) pad@4 error@8(ptr) version@16(ptr) → 24 bytes
// (This mirrors the proven 5.0 spike stub, recorded in the Phase 5.2 conclusion:
// docs/plans/2026-07-12-phase-5.2-conclusion.md.)
//
// String ownership: input strings (os/note/mount name) are caller-allocated
// NUL-terminated UTF-8, callee-borrowed during the call. The version/error
// output cstrings are static native memory (never freed). Frame payloads handed
// to the callback are arena-owned and valid ONLY for the duration of the
// callback — BnFrameAdapter copies out before returning (PatchProtocolNative.cs).
// ─────────────────────────────────────────────────────────────────────────────

#ifndef BLAZORNATIVE_RUNTIME_C_H
#define BLAZORNATIVE_RUNTIME_C_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

// Mirror of BlazorNativeInitOptions (Exports.cs). Host-owned during the call.
// Phase 10.0 (#121): platformInfoKind carries the shell's real PlatformKind ordinal
// (DevHost=0, Android=1, iOS=2, Windows=3, Mac=4) so an iOS app stops reporting
// Android. Appended AFTER the three original fields — offsets 0/8/16 are unchanged;
// the struct grows 24 → 32 bytes (kind@24 + 4 bytes tail padding to the 8-byte
// pointer alignment). This is the init-INPUT struct, NOT the frozen 80-byte
// callbacks bridge (bn_bridge_callbacks below — unchanged).
typedef struct {
    const char* platformInfoOs;    // offset 0  — host-allocated UTF-8
    int32_t     platformInfoApiLevel; // offset 8
    const char* platformInfoNote;  // offset 16 — optional (may be NULL)
    int32_t     platformInfoKind;  // offset 24 — PlatformKind ordinal (Phase 10.0)
} bn_init_options;                 // 32 bytes

// Mirror of BlazorNativeInitResult (Exports.cs). Returned BY VALUE.
typedef struct {
    int32_t     status;   // offset 0  — 0 = success
    const char* error;    // offset 8  — set on status != 0 (per-failure leak; one-shot boot)
    const char* version;  // offset 16 — static, never freed
} bn_init_result;

// Cdecl `void (*)(BlazorNativeFrame*)`. The frame pointer (and every string it
// references) is valid ONLY during the invocation — decode with BnFrameAdapter
// (which copies everything) before returning. Imports into Swift as
// `@convention(c) (UnsafeRawPointer?) -> Void`.
typedef void (*bn_frame_callback)(const void* frame);

// ── The four boot+render exports + shutdown (Exports.cs UnmanagedCallersOnly) ──

// Loads the runtime + verifies Blazor accessors + stores platform info. The
// first frame does NOT fire here (that is mount). Returns the result by value.
bn_init_result blazornative_init(bn_init_options* opts);

// Stores the host frame callback (last-wins; NULL disables). Returns 0 on success.
int32_t blazornative_register_frame_callback(bn_frame_callback callback);

// Mounts a registered component by NUL-terminated UTF-8 name. The first render
// completes SYNCHRONOUSLY, so the registered frame callback has already fired
// when this returns. Status: 0 ok / 1 unknown / 2 threw / 3 name null.
int32_t blazornative_mount(const char* componentNameUtf8);

// Static version cstring (never freed); NULL on fault.
const char* blazornative_version(void);

// Clears the frame callback. Reserved for genuine process-exit paths.
void blazornative_shutdown(void);

// ── Phase 5.3: input direction (dispatch_event + the shell bridge) ────────────

// Host→renderer UI event ingress (Exports.cs DispatchEvent). handlerId is the
// AttachEvent's aux field (widened to u64); argsJsonUtf8 is NUL-terminated flat
// JSON ({"name":"click"} / {"name":"change","payload":"<raw>"}). SYNCHRONOUS: the
// handler, the re-render, and the frame callback all complete before this returns
// (so the re-render frame arrives on the CALLING thread — the dispatch lane).
// Return: 0 dispatched (incl. stale handler) / 1 no session / 2 dispatch faulted /
// 3 malformed args or handlerId out of int range.
int32_t blazornative_dispatch_event(uint64_t handlerId, const char* argsJsonUtf8);

// The host-implemented shell callbacks (BridgeProtocolNative.cs
// BlazorNativeBridgeCallbacks — 10 × 8-byte fn pointers = 80 bytes since Phase 9.0;
// 9 × 8 = 72 from Phase 5.4). All cdecl, int-returning. Buffer-writing calls
// (currentRoute, storageRead, clipboardRead) use the -needed protocol: return the
// byte count written INCLUDING NUL on success, or -(utf8Bytes+1) when the value does
// not fit the offered cap; -1 = host error; -2 = key absent (storageRead only). Input
// strings are .NET-owned, valid only during the callback (copy before returning).
//
// A NULL slot = capability unsupported (the .NET null-slot guard surfaces
// NotSupportedException). Phase 5.4 appended clipboardRead/Write + share at 48/56/64;
// Phase 9.0 appends hostCallBegin at 72 (the generic permission-gated async-begin) —
// each with the size-negotiated register (structSize below); existing offsets (0…64)
// are unchanged.
typedef int32_t (*bn_navigate_cb)(const char* routeUtf8);                    // offset 0
typedef int32_t (*bn_current_route_cb)(char* buf, int32_t cap);              // offset 8
typedef int32_t (*bn_storage_read_cb)(const char* keyUtf8, char* buf, int32_t cap); // offset 16
typedef int32_t (*bn_storage_write_cb)(const char* keyUtf8, const char* valueUtf8); // offset 24
typedef int32_t (*bn_storage_delete_cb)(const char* keyUtf8);               // offset 32
// FetchBegin: the request struct is ignored by the shell's honest stub, so it is
// typed as an opaque pointer here (BlazorNativeFetchRequest* on the .NET side).
typedef int32_t (*bn_fetch_begin_cb)(int64_t requestId, const void* request); // offset 40
typedef int32_t (*bn_clipboard_read_cb)(char* buf, int32_t cap);            // offset 48
typedef int32_t (*bn_clipboard_write_cb)(const char* textUtf8);            // offset 56
typedef int32_t (*bn_share_cb)(const char* textUtf8);                       // offset 64
// HostCallBegin (Phase 9.0): the GENERIC permission-gated async-begin — an op enum
// (0 = Geolocation in 9.0) + a flat-JSON arg string. Returns synchronously (0 ok,
// <0 host error); the tri-state RESULT arrives LATER via blazornative_host_call_complete.
// The FetchBegin shape, generalized. A NULL slot surfaces NotSupportedException .NET-side.
typedef int32_t (*bn_host_call_begin_cb)(int64_t requestId, int32_t op, const char* argsJsonUtf8); // offset 72

typedef struct {
    bn_navigate_cb        navigate;       // offset 0
    bn_current_route_cb   currentRoute;   // offset 8
    bn_storage_read_cb    storageRead;    // offset 16
    bn_storage_write_cb   storageWrite;   // offset 24
    bn_storage_delete_cb  storageDelete;  // offset 32
    bn_fetch_begin_cb     fetchBegin;     // offset 40
    bn_clipboard_read_cb  clipboardRead;  // offset 48 — NULL = unsupported
    bn_clipboard_write_cb clipboardWrite; // offset 56 — NULL = unsupported
    bn_share_cb           share;          // offset 64 — NULL = unsupported
    bn_host_call_begin_cb hostCallBegin;  // offset 72 — NULL = unsupported (Phase 9.0)
} bn_bridge_callbacks;                     // 80 bytes

// COPIES min(structSize, sizeof(runtime's struct)) bytes of the callbacks struct
// and zero-fills the tail (Phase 5.4 size negotiation — pass sizeof(bn_bridge_callbacks)
// as structSize; the host may free the struct after; the fn pointers must stay
// alive). Call BEFORE mount so components resolving the bridge find a live host.
// Returns 0 on success, 2 on null pointer / bad size / failure.
int32_t blazornative_register_bridge(int32_t structSize, bn_bridge_callbacks* callbacks);

// ── Phase 9.0: the async permission-gated completion the SHELL CALLS (fetch_complete
// twin) ───────────────────────────────────────────────────────────────────────────
//
// The shell delivers a HostCallBegin's deferred result here, keyed by the same
// requestId, on whatever thread the host outcome arrived on (a CLLocationManager
// delegate callback, after a possibly-suspended permission prompt). status is the
// wire-mirrored tri-state (0 Granted / 1 Denied / 2 DeniedPermanently / 3 Restricted /
// 4 LocationUnavailable / 5 Error); payloadJsonUtf8 is the flat-JSON fix ONLY when
// Granted, NULL otherwise. DENIAL IS DATA — never a thrown error across this boundary
// and never a dropped call (a hang). Returns 0 = delivered, 1 = unknown/already-
// completed id (benign cancellation race, logged, never a throw), 2 = invalid call.
int32_t blazornative_host_call_complete(int64_t requestId, int32_t status, const char* payloadJsonUtf8);

// ── Phase 5.1 host-INITIATED event ingress — the SHELL CALLS it since Phase 9.1 ──
//
// A host→.NET event, keyed by NAME (not a handlerId): a reserved name maps to a
// navigation verb in .NET (DispatchHostEventCore), anything else fires the
// NativeShellBridge.NativeEvents multicast. The Swift shell CALLS this for the first
// time in Phase 9.1 — the WARM half of notification tap-through dispatches the
// reserved name "navigate" with the tap's route as the payload, and .NET maps it to
// NavigateToAsync (the "back" precedent, a new reserved name over the SAME export).
// name is required (NULL/empty → rc 3); payload may be NULL. SYNCHRONOUS: the re-route
// swap's frames are delivered before this returns. Returns 0 handled (navigated) /
// 1 not handled (no session / unknown route) / 2 fault / 3 malformed. NO new export —
// the symbol has been in the archive since Phase 5.1 (ios.yml's nm gate lists it).
int32_t blazornative_host_event(const char* nameUtf8, const char* payloadUtf8);

#ifdef __cplusplus
}
#endif

// ── Shell-owned C surfaces (NOT runtime exports — keep them out of the mirror) ─
// Everything ABOVE this line mirrors Exports.cs. Everything the SHELL itself
// implements in C/Objective-C++ and wants Swift to see gets its own header and is
// included here, so the mirror stays a mirror. Phase 6.0's Yoga probe was the first
// (bn_yoga_compute_flex_row / bn_yoga_warm_up, implemented in BnYogaProbe.mm);
// Phase 6.1 adds the real node-tree API (BnYogaLayout.{h,mm}) — the engine that
// places every view.
//
// Both are PLAIN-C headers, and that is the load-bearing part: Yoga's OWN headers
// must never become reachable from Swift, because Xcode's Swift explicit-module
// dependency scanner reads this bridging header with a path-less search that
// honours neither HEADER_SEARCH_PATHS nor `-Xcc -I` (six red CI runs in Phase 6.0
// proved it). All Yoga interop stays inside the .mm files.
#include "BnYogaProbe.h"
#include "BnYogaLayout.h"

#endif /* BLAZORNATIVE_RUNTIME_C_H */
