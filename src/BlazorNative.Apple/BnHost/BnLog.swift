// ─────────────────────────────────────────────────────────────────────────────
// BnLog — Phase 11.4 Gate C: the iOS half of the ONE level-gated logging seam
// (M11 DoD #6, issues #155/#164), design §4.2 and §7.
//
// WHAT IT REPLACES: all 78 bare `NSLog` sites under BnHost/ — 54 in
// BnWidgetMapper, 6 in BnRuntime, 4 each in BnYogaLayout.mm / BnSecureStorage /
// AppleShellBridge, 3 in BnCamera, 1 each in HostViewController, BnYogaProbe and
// BnFrameAdapter. `BnHostTests/**`'s 24 stay: XCTest output is not shipped
// output, and the drift pin exempts that directory BY NAME.
//
// ⚠ WHY `os_log`/`Logger` AND NOT A LEVEL-CHECKED `NSLog` WRAPPER. A wrapper
// would deliver the noise half of #155 for a fraction of the work and forfeit
// the OTHER half — the one the issue closes with: "no internal exception detail
// / paths leaked at default Release verbosity". Three reasons, in the design's
// order (§4.2), and the second is the load-bearing one:
//
//   1. IT IS OS-FILTERABLE. Levels below `.default` are not even persisted
//      unless the user enables them for the subsystem. Quietness becomes the
//      platform's job rather than a `#if`'s.
//   2. IT REDACTS BY DEFAULT. In `Logger`'s string interpolation a non-static
//      value is `private` unless explicitly marked `.public`, and it renders as
//      `<private>` in any log collected off-device. `NSLog` IS ALWAYS PUBLIC —
//      every exception description, keychain key, image URL and file path this
//      shell logs today is readable by anyone who sysdiagnoses the device.
//      Level gating alone cannot fix that, because an Error SHIPS IN RELEASE by
//      design (design §7: "gating changes WHICH messages appear, not WHAT IS
//      INSIDE the ones that do").
//   3. IT IS CHEAP WHEN DISABLED. `os_log` defers formatting; `NSLog` formats
//      and then writes.
//
// THE PRIVACY RULE THIS FILE APPLIES, STATED ONCE SO EVERY CALL SITE CAN BE
// CHECKED AGAINST IT: **a message is `.redacted` unless its text is a compile-
// time constant plus, at most, the framework's own version string.** Nothing
// that came from the app, the user, the OS, the keychain, the network or an
// `Error` is ever marked `.safe` — which is why only three call sites in the
// whole shell (BnRuntime's "frame callback registered", "shell bridge
// registered" and "native init ok — <framework version>") pass `.safe`.
// Everything else, including every node id and every `ignored` diagnostic, is
// redacted. Redaction is NOT invisibility to the developer: private data is
// shown in full when a debugger is attached, which is where these lines are
// actually read.
//
// ⚠ DEPLOYMENT-TARGET GOTCHA, FOUND WHILE WRITING THIS. `os.Logger` — the
// struct whose *interpolation* carries the privacy specifiers — is iOS 14+, and
// project.yml's deploymentTarget is **iOS 13.0**. Rather than raise the floor
// (a shipping-policy change that has no business riding in a logging PR), this
// file uses `Logger` where available and falls back to `os_log` on the same
// `OSLog` object below it. The fallback is NOT a privacy downgrade: `os_log`'s
// `%{private}@` / `%{public}@` specifiers are the same mechanism `Logger`'s
// interpolation compiles down to, and they have been available since iOS 10.
// One `OSLog` per category is cached and shared by both paths.
//
// THE LEVELS AND THE THRESHOLD MIRROR `BlazorNative.Core.BnLogLevel` exactly —
// same five names, same ordinals, same "ordinal 0 = unset → the runtime default"
// rule, same "numerically lower is more severe" comparison. They are a WIRE
// CONTRACT (they cross the ABI in `bn_init_options.logLevel` at offset 28), so
// they are held equal to C# by BnLogFormatDriftTests in the .NET suite.
//
// See docs/plans/2026-07-21-phase-11.4-design.md §3.1 (levels), §4.2 (this
// file), §4.3 (which level each class of message gets) and §7 (redaction).
// ─────────────────────────────────────────────────────────────────────────────

import Foundation
import os

/// The ABI-mirrored `BnLogLevel` ordinals the shared runtime decodes in
/// `blazornative_init` (Exports.cs → `BnLog.SetLevelFromOrdinal`) —
/// byte-identical to `BlazorNative.Core.BnLogLevel` and to the Android shell's
/// `io.blazornative.jni.BnLogLevel`.
///
/// A message is emitted when its level is at or more severe than (numerically ≤)
/// the threshold, so the threshold is a CEILING on verbosity: `warn` ships errors
/// and warnings, `verbose` ships everything.
///
/// [unset] is the wire's "the shell said nothing" and resolves to the quiet
/// Release default ([BnLog.defaultLevel]). NEVER RENUMBER — these are wire values.
enum BnLogLevel {
    /// Reserved: the shell declared nothing → the runtime's default (warn).
    static let unset: Int32 = 0

    /// Faults only.
    static let error: Int32 = 1

    /// Faults + dropped wires / bent host contracts. THE RELEASE DEFAULT.
    static let warn: Int32 = 2

    /// …plus success narration (boot lines).
    static let info: Int32 = 3

    /// …plus developer detail, including full exception descriptions.
    static let debug: Int32 = 4

    /// …plus per-frame / per-patch tracing.
    static let verbose: Int32 = 5

    /// Case-insensitive name → ordinal, for the shell's app-facing knob (an
    /// Info.plist `io.blazornative.logLevel` string, or the `BN_LOG_LEVEL`
    /// environment variable). The twin of Kotlin's `BnLogLevel.fromName`.
    ///
    /// An unrecognised name — including nil — resolves to [unset], i.e. the
    /// runtime's default, because a typo in an Info.plist must not silently turn
    /// logging OFF.
    static func fromName(_ name: String?) -> Int32 {
        switch name?.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() {
        case "error": return error
        case "warn", "warning": return warn
        case "info", "information": return info
        case "debug": return debug
        case "verbose", "trace": return verbose
        default: return unset
        }
    }
}

/// Whether a message's TEXT may be written to the unified log in the clear.
///
/// The default everywhere is [redacted]; [safe] is opt-in per call site and the
/// rule for granting it is in the file header. This exists because the privacy
/// specifier `os_log` accepts must be a COMPILE-TIME constant at the
/// interpolation site — it cannot be a variable — so the choice is expressed as
/// two literal call sites inside [BnLog.emit] rather than a value passed through.
enum BnLogPrivacy {
    /// The payload is elided (`<private>`) in logs collected off-device. THE
    /// DEFAULT, and correct for anything carrying app, user, OS or `Error` data.
    case redacted

    /// The payload is a compile-time constant (plus, at most, the framework's own
    /// version). Written in the clear.
    case safe
}

/// The iOS shell's one logging seam: a level threshold and five level methods
/// over `os.Logger` / `os_log`.
enum BnLog {

    /// The unified-log subsystem every category hangs off. Filter the whole shell
    /// with `log stream --predicate 'subsystem == "io.blazornative"'`.
    static let subsystem = "io.blazornative"

    /// The threshold applied when nothing sets one — deliberately a RUNTIME
    /// default and deliberately not `#if DEBUG`, exactly as `BnLog.DefaultLevel`
    /// is on the managed side. Errors and warnings ship in Release; the boot
    /// narration does not.
    static let defaultLevel: Int32 = BnLogLevel.warn

    /// The current threshold. Set once at boot from `bn_init_options.logLevel`'s
    /// source (see `BnRuntime.resolveLogLevel`), before the first line is written.
    ///
    /// Not guarded by a lock ON PURPOSE: it is a word-sized store written once,
    /// before the boot thread starts, and read on every call from whatever thread
    /// is faulting. A lock on the read would defeat the one property the gate has
    /// — that a suppressed level costs a compare. This is the Swift shape of
    /// Kotlin's `@Volatile` int and C#'s `volatile int`.
    private(set) static var level: Int32 = defaultLevel

    /// Applies a raw ordinal — the value the shell puts in
    /// `bn_init_options.logLevel` at offset 28.
    ///
    /// [BnLogLevel.unset] (a shell, an Info.plist or an env var that said nothing)
    /// and any out-of-range value resolve to [defaultLevel]. Same "safe non-lying
    /// default" rule `Exports.ToPlatformKind` applies to the neighbouring ordinal
    /// at offset 24.
    static func setLevelFromOrdinal(_ ordinal: Int32) {
        level = (ordinal >= BnLogLevel.error && ordinal <= BnLogLevel.verbose) ? ordinal : defaultLevel
    }

    /// Would a message at `level` be emitted? Public because it is how a caller
    /// avoids work it will not use; the `@autoclosure` message parameters below
    /// already cover the string-building case.
    static func isEnabled(_ level: Int32) -> Bool {
        level != BnLogLevel.unset && level <= self.level
    }

    // ── the five levels ──────────────────────────────────────────────────────
    //
    // `@autoclosure` is the Swift answer to the C# side's "guard with IsEnabled
    // before interpolating": the message expression is not evaluated at all when
    // the level is suppressed, so a per-patch `BnLog.verbose(...)` costs one
    // integer compare and builds no string. That is why these take a closure and
    // not a String, and it is not decoration — BnWidgetMapper's sites run inside
    // the frame path.

    /// A fault.
    static func error(_ category: String,
                      _ message: @autoclosure () -> String,
                      privacy: BnLogPrivacy = .redacted) {
        write(BnLogLevel.error, category, message, privacy)
    }

    /// Something the author asked for was dropped, or a host contract was bent.
    /// SHIPS IN RELEASE — see the design's §4.3 finding: the mapper's
    /// `ignored`/`skipped` lines are the only record that a wire the app author
    /// wrote was silently discarded, so they are warnings on BOTH shells and are
    /// deliberately NOT demoted to debug.
    static func warn(_ category: String,
                     _ message: @autoclosure () -> String,
                     privacy: BnLogPrivacy = .redacted) {
        write(BnLogLevel.warn, category, message, privacy)
    }

    /// Success narration (boot lines). Suppressed in Release.
    static func info(_ category: String,
                     _ message: @autoclosure () -> String,
                     privacy: BnLogPrivacy = .redacted) {
        write(BnLogLevel.info, category, message, privacy)
    }

    /// Developer detail. Suppressed in Release.
    static func debug(_ category: String,
                      _ message: @autoclosure () -> String,
                      privacy: BnLogPrivacy = .redacted) {
        write(BnLogLevel.debug, category, message, privacy)
    }

    /// Per-frame / per-patch tracing. Suppressed in Release.
    static func verbose(_ category: String,
                        _ message: @autoclosure () -> String,
                        privacy: BnLogPrivacy = .redacted) {
        write(BnLogLevel.verbose, category, message, privacy)
    }

    /// Emits at an arbitrary ordinal — the entry point the `@_cdecl` [BnLogC]
    /// shim and the stderr pump use, since neither has a level known at compile
    /// time.
    static func write(_ level: Int32,
                      _ category: String,
                      _ message: @autoclosure () -> String,
                      _ privacy: BnLogPrivacy = .redacted) {
        guard isEnabled(level) else { return }
        emit(level, category, message(), privacy)
    }

    // ── the unified-log sink ─────────────────────────────────────────────────

    /// THE `OSLogType` MAPPING, AND IT IS NOT 1:1 — five `BnLogLevel` names onto
    /// four usable `OSLogType` values:
    ///
    /// | BnLogLevel | OSLogType  | why |
    /// |------------|------------|-----|
    /// | `error`    | `.error`   | a fault in THIS process. `.fault` is reserved by the platform for multi-process / system-level failures and gets a backtrace + a higher-cost persistence path; using it for "a component threw" would over-claim. |
    /// | `warn`     | `.default` | there IS no `.warn`. `.default` is the least-severe level that is PERSISTED without the user enabling the subsystem, which is precisely the property a warning needs — the design's §4.3 warnings must survive to a sysdiagnose taken after the fact. |
    /// | `info`     | `.info`    | captured to memory, persisted only when collecting with `--info`. Exactly the boot narration's worth. |
    /// | `debug`    | `.debug`   | not captured at all unless enabled for the subsystem. |
    /// | `verbose`  | `.debug`   | `OSLogType` has nothing below `.debug`, so verbose collapses onto it. The DISTINCTION IS NOT LOST — it is enforced one step earlier, by [isEnabled], so a `verbose` line is never written at a `debug` threshold. The collapse only means the OS cannot re-filter the two apart once both are enabled. |
    ///
    /// An unknown ordinal maps to `.default`, the same defensive non-lying rule
    /// `BnLog.Tag` applies to `Unset` on the managed side.
    static func osLogType(for level: Int32) -> OSLogType {
        switch level {
        case BnLogLevel.error: return .error
        case BnLogLevel.warn: return .default
        case BnLogLevel.info: return .info
        case BnLogLevel.debug, BnLogLevel.verbose: return .debug
        default: return .default
        }
    }

    private static let cacheLock = NSLock()
    private static var logs: [String: OSLog] = [:]

    /// One `OSLog` per category, cached. Creating one is not free (it registers
    /// the subsystem/category pair with the logging system), and the categories
    /// are a small fixed set derived from the bracketed tags the messages already
    /// carried — `BnWidgetMapper`, `BnRuntime`, `AppleShellBridge`, `BnCamera`,
    /// `BnSecureStorage`, `BnYogaLayout`, `BnFrameAdapter`, `HostViewController`,
    /// `BnYogaProbe`, `native`.
    static func log(for category: String) -> OSLog {
        cacheLock.lock()
        defer { cacheLock.unlock() }
        if let existing = logs[category] { return existing }
        let created = OSLog(subsystem: subsystem, category: category)
        logs[category] = created
        return created
    }

    /// The write itself. FOUR literal call sites, and they have to be literal:
    /// `os_log`'s privacy specifier is part of the format string / interpolation
    /// and must be a compile-time constant, so "private or public" cannot be a
    /// variable threaded through one call.
    private static func emit(_ level: Int32,
                             _ category: String,
                             _ message: String,
                             _ privacy: BnLogPrivacy) {
        let osLog = log(for: category)
        let type = osLogType(for: level)

        if #available(iOS 14.0, *) {
            let logger = Logger(osLog)
            switch privacy {
            case .redacted: logger.log(level: type, "\(message, privacy: .private)")
            case .safe: logger.log(level: type, "\(message, privacy: .public)")
            }
        } else {
            // iOS 13 (project.yml's deploymentTarget). Same unified log, same
            // privacy mechanism, older spelling — see the file header.
            switch privacy {
            case .redacted: os_log("%{private}@", log: osLog, type: type, message as NSString)
            case .safe: os_log("%{public}@", log: osLog, type: type, message as NSString)
            }
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// THE C SHIM — design §4.2's `@_cdecl BnLogC`, for BnYogaLayout.mm's 4 sites.
//
// `BnYogaLayout.mm` is Objective-C++ and cannot call a Swift API: the shell's
// C++ files see only plain C (project.yml records WHY at length — Yoga's headers
// must never reach Swift, so the traffic between the two goes through
// shell-owned plain-C headers like BnYogaLayout.h and BnYogaProbe.h, never
// through a generated Swift interface). The direction needed here is the
// opposite one, and `@_cdecl` is its exact counterpart: it exports a Swift
// global function under an unmangled C symbol that the .mm can call after
// `#include "BnLog.h"`.
//
// Four sites, all in the same app target, so the symbol resolves at link with no
// new header search path, no bridging-header entry (Swift does not need to SEE
// the declaration — it IS the definition) and no second design.
// ─────────────────────────────────────────────────────────────────────────────

/// C-callable `BnLog.write`. See BnLog.h for the declaration the `.mm` includes.
///
/// - Parameters:
///   - level: a `BnLogLevel` ordinal (1…5). Out of range is treated as warn.
///   - category: NUL-terminated UTF-8; nil → `native`.
///   - message: NUL-terminated UTF-8; nil → empty.
///
/// The payload is ALWAYS redacted: a C caller cannot express the file header's
/// "compile-time constant only" rule at the call site, and all four current
/// callers interpolate a style property name and an app-supplied value.
@_cdecl("BnLogC")
public func BnLogC(_ level: Int32,
                   _ category: UnsafePointer<CChar>?,
                   _ message: UnsafePointer<CChar>?) {
    // The gate BEFORE the two `String(cString:)` copies — the whole point of
    // gating early is not paying for the message you are about to drop.
    let resolved = (level >= BnLogLevel.error && level <= BnLogLevel.verbose) ? level : BnLogLevel.warn
    guard BnLog.isEnabled(resolved) else { return }

    let cat = category.map { String(cString: $0) } ?? BnLogFormat.fallbackCategory
    let msg = message.map { String(cString: $0) } ?? ""
    BnLog.write(resolved, cat, msg)
}

// ─────────────────────────────────────────────────────────────────────────────
// BnLogFormat — the Swift twin of Kotlin's `io.blazornative.jni.BnLogFormat`
// (design §5.5). PURE: no I/O, no state, so the round-trip can be PINNED.
//
// ⚠ THE FORMAT IS A STRINGLY-TYPED CONTRACT ACROSS THREE LANGUAGES (design R1).
// `BlazorNative.Core.BnLog.FormatLine` WRITES `[BN|E|category] message`; Kotlin's
// parser and this one READ it back. A ONE-CHARACTER DRIFT DOWNGRADES EVERY
// FRAMEWORK LINE TO THE UNPREFIXED FALLBACK AND NOTHING LOOKS BROKEN — the
// unified log keeps filling, errors just arrive as warnings under the wrong
// category, forever. The three copies are held equal by BnLogFormatDriftTests in
// the .NET suite, which READS THIS FILE'S SOURCE.
// ─────────────────────────────────────────────────────────────────────────────

/// One parsed stderr line: the `BnLogLevel` ordinal it should be emitted at, the
/// category, and the message.
struct BnLogRecord: Equatable {
    let level: Int32
    let category: String
    let message: String
}

enum BnLogFormat {

    /// The prefix marker. MUST equal `BlazorNative.Core.BnLog.LinePrefix` and
    /// Kotlin's `BnLogFormat.PREFIX`.
    static let prefix: String = "[BN|"

    /// THE FALLBACK, AND IT IS A DECISION, NOT A DEFAULT (design §5.5). A line
    /// without the prefix is NOT DROPPED: it is BCL output, a NativeAOT runtime
    /// dump, or a third-party native library — i.e. exactly the output this
    /// transport exists to rescue, and the half no bridge-based option could ever
    /// have carried.
    ///
    /// `warn`, chosen over `error` because unstructured runtime chatter is not
    /// self-evidently a fault, and over `info` because in Release the only thing
    /// that reaches stderr unannounced is usually a problem.
    static let fallbackLevel: Int32 = BnLogLevel.warn

    /// The category unprefixed lines are filed under.
    static let fallbackCategory: String = "native"

    /// The single-character level tag → `BnLogLevel` ordinal. `nil` for any other
    /// character, which the caller treats as "not our line". The letters MIRROR
    /// `BnLog.Tag(BnLogLevel)` in C# and `priorityForTag` in Kotlin.
    static func levelForTag(_ tag: Character) -> Int32? {
        switch tag {
        case "E": return BnLogLevel.error
        case "W": return BnLogLevel.warn
        case "I": return BnLogLevel.info
        case "D": return BnLogLevel.debug
        case "V": return BnLogLevel.verbose
        default: return nil
        }
    }

    /// The inverse of [levelForTag]. An unknown ordinal maps to `W`, defensively.
    static func tagForLevel(_ level: Int32) -> Character {
        switch level {
        case BnLogLevel.error: return "E"
        case BnLogLevel.warn: return "W"
        case BnLogLevel.info: return "I"
        case BnLogLevel.debug: return "D"
        case BnLogLevel.verbose: return "V"
        default: return "W"
        }
    }

    /// The Swift twin of `BnLog.FormatLine`. Not used in production — the
    /// framework's lines are written by the .NET side — but it is what makes the
    /// round-trip pin a ROUND TRIP instead of two hand-written literals.
    static func format(_ level: Int32, _ category: String, _ message: String) -> String {
        "\(prefix)\(tagForLevel(level))|\(category)] \(message)"
    }

    /// Recovers `(level, category, message)` from one stderr line. Anything that
    /// is not a well-formed prefixed line comes back as
    /// `(fallbackLevel, fallbackCategory, <the whole line verbatim>)`. The line is
    /// never dropped and never truncated by the parser.
    static func parse(_ line: String) -> BnLogRecord {
        guard line.hasPrefix(prefix) else { return fallback(line) }

        let chars = Array(line)
        let tagAt = prefix.count
        // Need at least "<tag>|" plus a closing ']'.
        guard chars.count >= tagAt + 3, chars[tagAt + 1] == "|" else { return fallback(line) }
        guard let level = levelForTag(chars[tagAt]) else { return fallback(line) }
        guard let close = chars[(tagAt + 2)...].firstIndex(of: "]") else { return fallback(line) }

        let category = String(chars[(tagAt + 2)..<close])
        guard !category.isEmpty else { return fallback(line) }

        // FormatLine puts exactly one space after the ']'. A line that ends at the
        // bracket (an empty message) is still ours.
        let messageStart = close + 2
        let message = messageStart <= chars.count ? String(chars[min(messageStart, chars.count)...]) : ""
        return BnLogRecord(level: level, category: category, message: message)
    }

    private static func fallback(_ line: String) -> BnLogRecord {
        BnLogRecord(level: fallbackLevel, category: fallbackCategory, message: line)
    }
}
