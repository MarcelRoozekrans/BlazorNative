// ─────────────────────────────────────────────────────────────────────────────
// BlazorNative.Runtime C-ABI — Swift bridging header (Phase 5.2 + 5.3).
//
// The Swift/UIKit shell's native-interop surface. Phase 5.2 declared the
// boot+render subset (init / register_frame_callback / mount / version / shutdown
// + the init structs + the frame-callback typedef). Phase 5.3 (M5 DoD #3,
// interactivity) adds the INPUT direction: dispatch_event (taps→renderer) +
// register_bridge (the shell bridge, .NET→host; 9 callbacks since Phase 5.4's
// size-negotiated clipboard/share growth). This is the Swift twin
// of the Kotlin JNA `NativeBindings` interface; Swift's native C interop replaces
// the JNA layer entirely (no reflection, no Structure classes — direct externs).
//
// host_event (Phase 5.1 host-initiated lifecycle) and fetch_complete are the only
// exports still undeclared — the Swift shell does not drive lifecycle/fetch yet
// (the fetch bridge stub fails synchronously; see AppleShellBridge). All nine are
// present in the linked static archive (ios.yml asserts them via `nm -gU`).
//
// Struct-layout contract — mirror of src/BlazorNative.Runtime/Exports.cs
// (BlazorNativeInitOptions / BlazorNativeInitResult, [StructLayout(Sequential)],
// little-endian, 8-byte pointers). The C compiler reproduces the same layout:
//   bn_init_options : os@0(ptr) apiLevel@8(int) pad@12 note@16(ptr)  → 24 bytes
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
typedef struct {
    const char* platformInfoOs;    // offset 0  — host-allocated UTF-8
    int32_t     platformInfoApiLevel; // offset 8
    const char* platformInfoNote;  // offset 16 — optional (may be NULL)
} bn_init_options;

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
// BlazorNativeBridgeCallbacks — 9 × 8-byte fn pointers = 72 bytes since Phase 5.4).
// All cdecl, int-returning. Buffer-writing calls (currentRoute, storageRead,
// clipboardRead) use the -needed protocol: return the byte count written INCLUDING
// NUL on success, or -(utf8Bytes+1) when the value does not fit the offered cap;
// -1 = host error; -2 = key absent (storageRead only). Input strings are .NET-owned,
// valid only during the callback (copy before returning).
//
// A NULL slot = capability unsupported (the .NET null-slot guard surfaces
// NotSupportedException). Phase 5.4 appended clipboardRead/Write + share at 48/56/64
// with the size-negotiated register (structSize below); existing offsets (0…40) are
// unchanged.
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
} bn_bridge_callbacks;                     // 72 bytes

// COPIES min(structSize, sizeof(runtime's struct)) bytes of the callbacks struct
// and zero-fills the tail (Phase 5.4 size negotiation — pass sizeof(bn_bridge_callbacks)
// as structSize; the host may free the struct after; the fn pointers must stay
// alive). Call BEFORE mount so components resolving the bridge find a live host.
// Returns 0 on success, 2 on null pointer / bad size / failure.
int32_t blazornative_register_bridge(int32_t structSize, bn_bridge_callbacks* callbacks);

// ── Phase 6.0 Yoga spike (M6): the flexbox probe surface ──────────────────────
// ALL Yoga (<yoga/Yoga.h>) interop lives in BnYogaProbe.mm (Objective-C++), NOT in
// this Swift-visible bridging header: HEADER_SEARCH_PATHS reaches the Clang compile
// of the .mm but NOT the Swift explicit-module dependency scanner, which chokes on
// <yoga/Yoga.h> ("file not found"). So this header exposes only plain-C results.

// The computed frames of the minimal flex-row proof (x/y/w/h per node) + whether
// the measure callback fired.
typedef struct {
    float box1X, box1Y, box1W, box1H;
    float box2X, box2Y, box2W, box2H;
    float textX, textY, textW, textH;
    int32_t measureFired;
} bn_yoga_result;

// Builds a row container (300) with box1 (fixed 50), box2 (flexGrow 1), and an auto
// text leaf with a measure func, computes the layout, and returns the frames.
bn_yoga_result bn_yoga_compute_flex_row(void);

// A launch-time smoke that keeps Yoga linked + callable in-process (AppDelegate).
void bn_yoga_warm_up(void);

#ifdef __cplusplus
}
#endif

#endif /* BLAZORNATIVE_RUNTIME_C_H */
