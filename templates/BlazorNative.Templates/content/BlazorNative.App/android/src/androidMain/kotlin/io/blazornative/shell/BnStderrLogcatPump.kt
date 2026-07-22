package io.blazornative.shell

import android.system.Os
import android.util.Log
import io.blazornative.jni.BnLogFormat
import io.blazornative.jni.BnLogRecord
import io.blazornative.jni.BnStderrPump
import java.io.FileDescriptor
import java.io.FileInputStream

// ─────────────────────────────────────────────────────────────────────────────
// BnStderrLogcatPump — Phase 11.4 Gate B, design §5.1 (Option 1, ADOPTED) and
// §5.5. THE ANDROID TRANSPORT: everything the .NET runtime writes to
// `Console.Error` reaches logcat from here, and only from here.
//
// THE PROBLEM, RESTATED IN ONE LINE: process fd 2 on Android is /dev/null. The
// runtime's 31 diagnostic sites, the BCL's own output, NativeAOT's
// TypeLoadException detail, and JNA's swallow-to-stderr callback handler all
// write there. None of it has EVER been visible on the target platform.
//
// THE MECHANISM, AND WHY IT NEEDS NO NDK AND NO JNI: `android.system.Os` has
// exposed `pipe()` and `dup2(FileDescriptor, int)` since API 21; this module's
// floor is minSdk 24 (build.gradle.kts). So the whole transport is pure Kotlin —
// create a pipe, point fd 2 at its write end, read the read end forever.
//
// ⚠ INSTALL ORDER IS LOAD-BEARING, NOT HYGIENE. This must run BEFORE
// `blazornative_init`, and therefore before the `dlopen` that
// `NativeBindings.INSTANCE` triggers. `blazornative_init`'s failure path
// (Exports.cs) is the one place the framework deliberately emits a full
// `ex.ToString()` — the NativeAOT trim failures (TypeLoadException,
// MissingMethodException) whose `Message` alone hides the offending type. Catching
// THAT line is one of the two reasons this transport was chosen over routing logs
// through the bridge (design §5.2 reason 1); install it late and the highest-value
// diagnostic the framework ever writes is still lost. MainActivity.onCreate
// installs it as its FIRST statement, before SoLoader, before the mapper, and long
// before the boot thread calls `runtime.start()`.
//
// ⚠ IRREVERSIBLE AND PROCESS-GLOBAL (design R2). `dup2` over fd 2 cannot be
// meaningfully undone, and it captures the descriptor for the WHOLE process —
// including any third-party native library. That is mostly the point. It is a
// COLLISION if the consumer's app also redirects fd 2 (Crashlytics/Sentry NDK
// handlers do): last writer wins, and if theirs runs second the framework's output
// is silently gone again. This cannot be tested; it is written down here and in
// the shell docs. Hence [install] is idempotent and guarded — a second call must
// never re-dup or spawn a second reader.
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Redirects process stderr (fd 2) into `android.util.Log`.
 *
 * Install once, as early as possible. See the file header for why the ordering
 * matters and why it can never be undone.
 */
object BnStderrLogcatPump {

    /** The logcat tag prefix. Lines arrive under `BlazorNative/<category>` —
     * filter the lot with `adb logcat -s BlazorNative/…`. */
    const val TAG_PREFIX: String = "BlazorNative"

    /** The reader thread's name, so a thread dump names the transport. */
    const val THREAD_NAME: String = "BlazorNative-StderrPump"

    /**
     * THE SINK SEAM (design §8.1 pin 5). Production forwards to
     * `android.util.Log`; a test swaps in a collector and asserts the priority
     * and tag it observed.
     *
     * That is the HONEST pin: asserting against an injected sink is
     * deterministic, whereas shelling out to `logcat -d` and grepping is
     * timing-sensitive and slow — the design says so in as many words and
     * relegates the logcat observation to Phase 11.2's device runbook.
     *
     * `@Volatile` because the reader thread reads it per line while a test writes
     * it from the instrumentation thread.
     */
    @Volatile
    @JvmField
    var sink: (BnLogRecord) -> Unit = { record -> toLogcat(record) }

    /**
     * What has happened to this process's pump — TRI-STATE ON PURPOSE (#191).
     *
     * The predecessor was a single `installed` boolean set BEFORE the syscalls
     * and never reset by the catch, so a FAILED install reported itself as
     * installed forever: "attempted" and "running" were the same bit. That made
     * [isInstalled] useless as an assertion (it was true either way) and made a
     * dead transport indistinguishable from a live one in production.
     */
    enum class InstallState {
        /** No call to [install] has been made in this process. */
        NotAttempted,

        /** `dup2` took, the reader thread is running: fd 2 is ours. */
        Installed,

        /**
         * [install] ran and threw. fd 2 may be PARTIALLY redirected (the pipe
         * can exist with no reader), so a retry is still forbidden — this state
         * is terminal, exactly like [Installed], and only the report differs.
         */
        Failed,
    }

    /**
     * TEST-ONLY DIAGNOSTIC SEAM (#191) — what the syscalls actually returned.
     *
     * Not production API and not read by the shell. It exists because the
     * instrumented lane could only report "nothing reached the sink" and had no
     * way to say WHETHER the `dup2` took: the descriptors are locals inside
     * [install]. Descriptors are exposed as [FileDescriptor]s rather than ints
     * so this file needs no reflection; the test reads the ints.
     *
     * @property readFd the pipe's read end, owned by the reader thread.
     * @property writeFd the pipe's write end. CLOSED by [install] once fd 2
     *   holds the description, so its int reads back as -1 afterwards — that is
     *   correct, not a leak.
     * @property dup2Result what `Os.dup2(writeFd, 2)` returned; its int must be
     *   2, and if it is not, the transport never captured stderr at all.
     */
    class InstallDiagnostics(
        @JvmField val readFd: FileDescriptor,
        @JvmField val writeFd: FileDescriptor,
        @JvmField val dup2Result: FileDescriptor,
    )

    /** TEST-ONLY (#191): the [InstallDiagnostics] of the one successful install,
     * or null if none succeeded in this process. */
    @Volatile
    @JvmField
    var installDiagnostics: InstallDiagnostics? = null

    @Volatile
    private var state: InstallState = InstallState.NotAttempted

    /**
     * Creates the pipe, points fd 2 at it, and starts the reader.
     *
     * @return true if THIS call installed the pump; false if it was already
     *   installed (idempotent — the second caller is a no-op, not a second
     *   `dup2` and not a second reader thread) or if a previous call FAILED, or
     *   if this call failed. In short: true means "fd 2 is now ours because of
     *   me", and every other outcome is false — ask [installState] which.
     *
     * NEVER THROWS. A shell whose logging transport could abort `onCreate` would
     * be strictly worse than a shell with no transport: the failure mode of a
     * missing pump is "diagnostics go where they already went", and the failure
     * mode of a throw is a blank app. A failed install is reported to logcat at
     * `Log.w` — the one channel that is guaranteed to work — and swallowed.
     */
    @Synchronized
    fun install(): Boolean {
        if (state != InstallState.NotAttempted) return false
        // Set BEFORE the syscalls, and to Failed rather than Installed: a
        // partial install that threw must not leave the guard open for a second
        // attempt to re-dup fd 2, and until the syscalls return there is nothing
        // honest to call this but "failed". The success path promotes it below.
        state = InstallState.Failed

        return try {
            // Os.pipe() → [read, write].
            val fds = Os.pipe()
            val readFd = fds[0]
            val writeFd = fds[1]

            // fd 2 now refers to the pipe's write end. The original stderr
            // description is dropped — on Android it was /dev/null anyway.
            val dup2Result = Os.dup2(writeFd, 2)
            // The duplicate in `writeFd` is redundant now; fd 2 holds the pipe
            // open. Closing it avoids leaking a descriptor for the process
            // lifetime (and keeps the writer count at exactly one).
            Os.close(writeFd)

            // TEST-ONLY (#191). Published BEFORE the reader starts so a probe
            // that runs the instant install() returns already sees it.
            installDiagnostics = InstallDiagnostics(readFd, writeFd, dup2Result)

            val stream = FileInputStream(readFd)
            Thread({
                try {
                    BnStderrPump.drain(stream) { record -> sink(record) }
                } catch (t: Throwable) {
                    // Unreachable in practice — drain() swallows sink throws and
                    // treats a read failure as EOF. Belt and braces: an uncaught
                    // throw here would kill the transport for the process
                    // lifetime, silently.
                    Log.w(TAG_PREFIX, "stderr pump reader stopped", t)
                }
            }, THREAD_NAME).apply {
                // DAEMON, non-negotiable: the reader blocks on read() forever, so
                // a non-daemon thread would hold the process open past its last
                // Activity. Design R8 accepts one permanent thread; it must not
                // also be a shutdown hazard.
                isDaemon = true
                start()
            }
            state = InstallState.Installed
            true
        } catch (t: Throwable) {
            Log.w(TAG_PREFIX, "stderr → logcat pump could not be installed; the runtime's " +
                "diagnostics stay on a stderr Android discards", t)
            false
        }
    }

    /**
     * Is a pump RUNNING in this process — i.e. did a `dup2` over fd 2 take and
     * is a reader draining the pipe?
     *
     * FALSE FOR A FAILED INSTALL (#191). It answers "is the transport live", not
     * "has someone tried"; [installState] distinguishes never-tried from tried-
     * and-failed, and neither of those may be retried.
     */
    fun isInstalled(): Boolean = state == InstallState.Installed

    /** The full tri-state — see [InstallState]. */
    fun installState(): InstallState = state

    /** The production sink — one `Log.println` per line, tagged
     * `BlazorNative/<category>` (`BlazorNative/native` for anything the BCL,
     * NativeAOT or a third party wrote unprefixed — see
     * [BnLogFormat.FALLBACK_CATEGORY]). */
    private fun toLogcat(record: BnLogRecord) {
        Log.println(record.priority, "$TAG_PREFIX/${record.category}", record.message)
    }
}
