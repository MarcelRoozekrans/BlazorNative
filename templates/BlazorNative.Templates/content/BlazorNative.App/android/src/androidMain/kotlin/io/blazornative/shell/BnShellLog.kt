package io.blazornative.shell

import android.util.Log
import io.blazornative.jni.BnLogFormat
import io.blazornative.jni.BnLogLevel
import io.blazornative.jni.BnPrivacy

// ─────────────────────────────────────────────────────────────────────────────
// BnShellLog — issue #200: the SHELL'S OWN narration, behind the SAME level gate
// as everything else.
//
// WHAT WENT WRONG, OBSERVED ON HARDWARE. Phase 11.4 gave the framework one
// threshold (BnLog, .NET), one destination on Android (BnStderrLogcatPump's
// dup2 over fd 2) and one on iOS (os_log). It never swept THE KOTLIN SHELL'S OWN
// `android.util.Log` CALLS, because they were never in scope. So on a RELEASED
// build at the DEFAULT `Warn` threshold, a cold launch still printed four Info
// lines to logcat:
//
//     I BlazorNative: [BOOT] native init ok — BlazorNative.Runtime <version>
//     I BlazorNative: [BOOT] frame callback registered
//     I BlazorNative: [BOOT] shell bridge registered
//     I BlazorNative: [BOOT] mounted BnDemo
//
// (The version is ELIDED on purpose. This file is a byte-identical TEMPLATE
// MIRROR, and `dotnet new` substitutes the pack's version BY STRING MATCH — a
// version literal in a COMMENT here is rewritten in every generated app, which
// is why TemplateDriftTests' collision pin reds on one. It is right to.)
//
// …while ZERO lines carried the pump's `BlazorNative/<category>` tag shape,
// which is the proof they never touched BnLog at all. A threshold cannot
// suppress a call that never asks it.
//
// ⚠ THIS IS NOT A SECOND LEVEL CONCEPT, AND THE STRUCTURE IS WHAT MAKES THAT
// TRUE RATHER THAN THE COMMENT. There is exactly ONE resolution of the level in
// the shell — `MainActivity.resolveLogLevel()`, which reads the documented knobs
// (the `io.blazornative.logLevel` manifest meta-data and the `EXTRA_LOG_LEVEL`
// Intent extra). Its result is used TWICE, from one local: it is installed here
// via [setLevelFromOrdinal] and it is passed to the runtime in
// `BlazorNativeInitOptions.logLevel` (offset 28). The same ordinal, from the same
// call, in the same statement pair — so the shell's narration and the framework's
// can never disagree about what the consumer asked for. There is no second knob,
// no second default, and no way to raise one without the other.
//
// WHY IT DOES NOT SIMPLY WRITE TO fd 2 AND RIDE THE PUMP. That was considered
// and rejected: it would make every shell line depend on the `dup2` having
// taken. `BnStderrLogcatPump.install()` can FAIL (it says so, tri-state, at
// length) — and the failure mode of the shell's boot narration must not be
// SILENCE. `android.util.Log` is the one channel that always works, which is why
// the pump's own failure report uses it too. The cost is that these lines keep
// the plain `BlazorNative` tag rather than `BlazorNative/<category>`; that is a
// deliberate keep, not an oversight — the tag is what `adb logcat -s
// BlazorNative` (and `scripts/devloop.ps1`'s boot-marker wait) filters on.
//
// WHAT IS *NOT* GATED, AND MUST NOT BE. `warn` and `error` are written at every
// threshold; only `info`, `debug` and `verbose` are level-gated. Warnings and
// errors ship in Release by design (BnLog's own rule, design §4.3: the mapper's
// `ignored`/`skipped` lines are the only record that a wire the app author wrote
// was silently dropped). The pin in `AndroidLogDriftTests` therefore forbids
// `Log.i`/`Log.d`/`Log.v` ONLY.
//
// PRIVACY IS A SEPARATE AXIS FROM LEVEL. Every call takes a `BnPrivacy` whose
// default is REDACTED. Unless [BnShellLog.reveal] is set, a REDACTED line is
// written as `<private>`, a SAFE line keeps its text, and a throwable is dropped
// from both.
// ─────────────────────────────────────────────────────────────────────────────

/**
 * The shell's level-gated logcat seam: a threshold set once at boot from the same
 * ordinal the runtime is given, and the three levels that are SUPPRESSED at the
 * Release default.
 *
 * `info` / `debug` / `verbose` are level gated. `warn` / `error` are written at
 * every threshold. Every call takes a [BnPrivacy], REDACTED by default; text is
 * written as given only when [reveal] is set or the call says SAFE, and a
 * throwable is written only when [reveal] is set.
 */
object BnShellLog {

    /**
     * The threshold applied when the shell resolved nothing — i.e. the ordinal
     * was [BnLogLevel.UNSET] or out of range.
     *
     * MIRRORS `BnLog.DefaultLevel` (C#) DELIBERATELY, and the mirror is pinned:
     * `BnLogFormatDriftTests` already asserts `BnLog.DefaultLevel == Warn` on the
     * grounds that "every KDoc in the Android shell tells the reader that resolves
     * to Warn", and `AndroidLogDriftTests` now reads THIS constant and holds it
     * equal to the C# one. If the framework's default ever moves, the shell's
     * narration moves with it in the same commit or the pin reds.
     */
    const val DEFAULT_LEVEL: Int = BnLogLevel.WARN

    /**
     * THE SINK SEAM — production writes one `Log.println` per line; a JVM unit
     * test swaps in a collector and asserts what the gate let through.
     *
     * Same shape and same reason as `BnStderrLogcatPump.sink`: asserting against
     * an injected sink is deterministic, whereas scraping `logcat -d` needs a
     * device and is timing-sensitive. It also keeps this file loadable in the AGP
     * unit-test lane, where every `android.jar` method throws "not mocked" —
     * the default lambda is CONSTRUCTED there, never CALLED.
     *
     * `@Volatile` because the boot thread writes lines while a test writes this.
     */
    @Volatile
    @JvmField
    var sink: (Int, String, String) -> Unit = { priority, tag, message ->
        Log.println(priority, tag, message)
    }

    /**
     * THE SINK FOR A LINE THAT CARRIES A THROWABLE. Only a revealed line reaches
     * it; production writes what `Log.e(tag, msg, t)` writes, the message and the
     * stack trace in one `Log.println`.
     */
    @Volatile
    @JvmField
    var throwableSink: (Int, String, String, Throwable) -> Unit = { priority, tag, message, t ->
        Log.println(priority, tag, message + '\n' + Log.getStackTraceString(t))
    }

    /** What an unrevealed line's text is written as. */
    const val PRIVATE: String = "<private>"

    /**
     * Whether text and throwables are written as given. False by default, so a
     * line is redacted until the shell sets this.
     */
    @Volatile
    @JvmField
    var reveal: Boolean = false

    // volatile: written once on the main thread in onCreate, read from the boot
    // thread ([BOOT] lines), the main thread (deep links) and the dispatch lane
    // (the bridge's navigate). An int compare is the whole gate.
    @Volatile
    private var threshold: Int = DEFAULT_LEVEL

    /**
     * Installs the threshold from a raw [BnLogLevel] ordinal — the value
     * `MainActivity.resolveLogLevel()` produced and the runtime is about to be
     * given at `BlazorNativeInitOptions.logLevel`.
     *
     * [BnLogLevel.UNSET] (the shell declared nothing) and any out-of-range value
     * resolve to [DEFAULT_LEVEL] — the exact rule `BnLog.SetLevelFromOrdinal`
     * applies on the .NET side, so "the config was wrong" can never mean "there
     * are no logs".
     */
    @JvmStatic
    fun setLevelFromOrdinal(ordinal: Int) {
        threshold = if (ordinal >= BnLogLevel.ERROR && ordinal <= BnLogLevel.VERBOSE)
            ordinal else DEFAULT_LEVEL
    }

    /** The current threshold — a [BnLogLevel] ordinal, never [BnLogLevel.UNSET]. */
    @JvmStatic
    fun level(): Int = threshold

    /**
     * Would a message at [level] be emitted? A message ships when its level is at
     * or MORE SEVERE THAN (numerically ≤) the threshold.
     *
     * Public because it is how a call site AVOIDS BUILDING a message it will not
     * emit: Kotlin interpolates before the call, so anything on a per-frame or
     * per-patch path must guard itself rather than pay for a string nobody reads.
     */
    @JvmStatic
    fun isEnabled(level: Int): Boolean =
        level != BnLogLevel.UNSET && level <= threshold

    /** Success narration — boot lines, deep-link routes, the bridge's navigate.
     * SUPPRESSED at the Release default; this is issue #200's whole subject. */
    @JvmStatic
    @JvmOverloads
    fun info(tag: String, message: String, privacy: BnPrivacy = BnPrivacy.REDACTED) =
        write(BnLogLevel.INFO, tag, message, null, privacy)

    /** Developer detail. Suppressed at the Release default. */
    @JvmStatic
    @JvmOverloads
    fun debug(tag: String, message: String, privacy: BnPrivacy = BnPrivacy.REDACTED) =
        write(BnLogLevel.DEBUG, tag, message, null, privacy)

    /** Per-frame / per-patch tracing. Suppressed at the Release default; guard
     * with [isEnabled] before building the message. */
    @JvmStatic
    @JvmOverloads
    fun verbose(tag: String, message: String, privacy: BnPrivacy = BnPrivacy.REDACTED) =
        write(BnLogLevel.VERBOSE, tag, message, null, privacy)

    /** A warning. NOT gated by level: it is written at every threshold. */
    @JvmStatic
    @JvmOverloads
    fun warn(tag: String, message: String, t: Throwable? = null, privacy: BnPrivacy = BnPrivacy.REDACTED) =
        writeUngated(BnLogFormat.WARN, tag, message, t, privacy)

    /** An error. NOT gated by level: it is written at every threshold. */
    @JvmStatic
    @JvmOverloads
    fun error(tag: String, message: String, t: Throwable? = null, privacy: BnPrivacy = BnPrivacy.REDACTED) =
        writeUngated(BnLogFormat.ERROR, tag, message, t, privacy)

    /**
     * Writes a report from the shared JVM code's `BnDiagnostics.sink`, REDACTED,
     * at the level it was reported with: a [BnLogLevel.WARN] report through
     * [warn], anything else through [error]. Ungated, as both are.
     */
    @JvmStatic
    fun diagnostic(tag: String, level: Int, message: String, t: Throwable?) =
        if (level == BnLogLevel.WARN) warn(tag, message, t) else error(tag, message, t)

    /**
     * Writes a line at a ready-made `android.util.Log` priority, ungated. The
     * stderr pump uses it, because the .NET side already applied the level.
     */
    @JvmStatic
    @JvmOverloads
    fun relay(priority: Int, tag: String, message: String, privacy: BnPrivacy = BnPrivacy.REDACTED) =
        writeUngated(priority, tag, message, null, privacy)

    private fun write(level: Int, tag: String, message: String, t: Throwable?, privacy: BnPrivacy) {
        if (!isEnabled(level)) return
        writeUngated(androidPriority(level), tag, message, t, privacy)
    }

    // Unrevealed: a REDACTED text becomes PRIVATE, a SAFE text is kept, and the
    // throwable is dropped either way. Revealed: text and throwable as given.
    private fun writeUngated(priority: Int, tag: String, message: String, t: Throwable?, privacy: BnPrivacy) {
        try {
            if (reveal) {
                if (t == null) sink(priority, tag, message) else throwableSink(priority, tag, message, t)
            } else {
                sink(priority, tag, if (privacy == BnPrivacy.SAFE) message else PRIVATE)
            }
        } catch (ignored: Throwable) {
            // A logger that faults its caller is worse than a quiet one — the
            // rule BnLog.Write and BnStderrPump.emit both already follow.
        }
    }

    /** [BnLogLevel] ordinal (1…5) → `android.util.Log` priority (2…6). TWO
     * numberings, deliberately; the android side is taken from [BnLogFormat]'s
     * mirrored constants rather than re-declared here, so there is still exactly
     * one place in Kotlin that claims to know what `Log.INFO` is. */
    private fun androidPriority(level: Int): Int = when (level) {
        BnLogLevel.ERROR -> BnLogFormat.ERROR
        BnLogLevel.WARN -> BnLogFormat.WARN
        BnLogLevel.INFO -> BnLogFormat.INFO
        BnLogLevel.DEBUG -> BnLogFormat.DEBUG
        else -> BnLogFormat.VERBOSE
    }
}
