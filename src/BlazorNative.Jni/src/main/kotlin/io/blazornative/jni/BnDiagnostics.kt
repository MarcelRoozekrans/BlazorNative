package io.blazornative.jni

/**
 * Whether a log line's text may be shown when the shell is NOT revealing.
 *
 * `REDACTED` is the default everywhere: the line is written as a placeholder.
 * `SAFE` is an explicit opt-in for text the shell itself wrote as a constant.
 * Lives in the shared package so `src/main` and the Android shell use one type.
 */
enum class BnPrivacy { REDACTED, SAFE }

/**
 * The diagnostics sink for the shared JVM code under `src/main`, which cannot
 * reference `android.util.Log`.
 *
 * Each report carries a [BnLogLevel] ordinal, [BnLogLevel.WARN] or
 * [BnLogLevel.ERROR]. The default writes to `System.err` at either level, as
 * those sites did before, so its output does not depend on the level. The
 * Android shell replaces [sink] at boot so the same lines go through its
 * privacy seam at their own level.
 */
object BnDiagnostics {
    @Volatile
    @JvmField
    var sink: (level: Int, message: String, t: Throwable?) -> Unit = { _, message, t ->
        if (t == null) System.err.println(message) else System.err.println("$message: $t")
    }
}
