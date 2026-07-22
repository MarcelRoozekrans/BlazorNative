// ─────────────────────────────────────────────────────────────────────────────
// BnLog.h — Phase 11.4 Gate C: the plain-C declaration of the `@_cdecl` logging
// shim implemented in BnLog.swift, for the shell's Objective-C++ files.
//
// A SHELL-OWNED HEADER, DELIBERATELY NOT THE RUNTIME C-ABI MIRROR. The same
// discipline BnYogaLayout.h and BnYogaProbe.h already follow: BlazorNativeRuntimeC.h
// mirrors the NativeAOT runtime's frozen exports and nothing else, so a shell-internal
// call that has nothing to do with the ABI does not go in it. This header declares
// exactly one function and is included by exactly one file (BnYogaLayout.mm).
//
// WHY IT EXISTS AT ALL: `BnYogaLayout.mm` is Objective-C++ and cannot call Swift.
// The shell's C++ traffic is plain-C in BOTH directions — Swift → C++ through
// BnYogaLayout.h, C++ → Swift through this. `@_cdecl("BnLogC")` in BnLog.swift
// exports the unmangled symbol; both files are in the BnHost target, so it links
// with no extra flags.
//
// NOT in the bridging header (BlazorNativeRuntimeC.h): Swift does not need to see
// this declaration — Swift IS the definition, and importing a C declaration of a
// function Swift itself exports is how duplicate-declaration diagnostics start.
//
// See docs/plans/2026-07-21-phase-11.4-design.md §4.2.
// ─────────────────────────────────────────────────────────────────────────────

#ifndef BLAZORNATIVE_BNLOG_H
#define BLAZORNATIVE_BNLOG_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

// The BnLogLevel ordinals — mirrors of BlazorNative.Core.BnLogLevel, Swift's
// BnLogLevel and Kotlin's io.blazornative.jni.BnLogLevel. Wire values: never
// renumber. A message is emitted when its level is numerically <= the threshold.
#define BN_LOG_UNSET   0
#define BN_LOG_ERROR   1
#define BN_LOG_WARN    2
#define BN_LOG_INFO    3
#define BN_LOG_DEBUG   4
#define BN_LOG_VERBOSE 5

// Emits one line through the Swift seam (os_log / Logger, level-gated).
//
//   level    — BN_LOG_*; out of range is treated as BN_LOG_WARN.
//   category — NUL-terminated UTF-8, the unified-log category (NO brackets: the
//              seam owns the presentation). NULL → "native".
//   message  — NUL-terminated UTF-8. NULL → empty.
//
// The payload is ALWAYS logged with `private` privacy — a C caller cannot express
// the "compile-time constant only" rule that grants `.public`, and every current
// caller interpolates an app-supplied style value. The call is a no-op below the
// threshold, and the gate runs BEFORE the strings are copied into Swift.
void BnLogC(int32_t level, const char *category, const char *message);

#ifdef __cplusplus
}
#endif

#endif // BLAZORNATIVE_BNLOG_H
