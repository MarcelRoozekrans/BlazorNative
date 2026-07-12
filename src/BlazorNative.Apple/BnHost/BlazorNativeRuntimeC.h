// ─────────────────────────────────────────────────────────────────────────────
// BlazorNative.Runtime C-ABI — Swift bridging header (Phase 5.2, M5 DoD #2).
//
// The Swift/UIKit shell's native-interop surface: it declares the subset of the
// nine-export blazornative_* C-ABI that the READ-ONLY boot+render shell needs —
// init / register_frame_callback / mount / version / shutdown — plus the two
// by-value structs and the frame-callback typedef. This is the Swift twin of the
// Kotlin JNA `NativeBindings` interface; Swift's native C interop replaces the JNA
// layer entirely (no reflection, no Structure classes — direct extern decls).
//
// The remaining four exports (dispatch_event, register_bridge, fetch_complete,
// host_event) are NOT declared here: Phase 5.2 is boot+render only, no bridge and
// no host→renderer event ingress (that is Phase 5.3). They are still present in
// the linked static archive (ios.yml asserts all nine via `nm -gU`); the shell
// simply does not call them yet.
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

#ifdef __cplusplus
}
#endif

#endif /* BLAZORNATIVE_RUNTIME_C_H */
