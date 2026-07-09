package io.blazornative.shell

import android.app.Activity
import android.os.Bundle
import android.util.Log
import android.widget.FrameLayout
import android.widget.TextView
import io.blazornative.jni.BlazorNativeRuntime
import kotlin.concurrent.thread

/**
 * Phase 3.0d Android shell entry point — boots the NativeAOT pipeline.
 *
 * On launch: spawns a background thread that runs [BlazorNativeRuntime.start]
 * (init → register frame callback → mount HelloComponent) against the
 * NativeAOT libBlazorNative.Runtime.so from the APK's jniLibs. Frames
 * arrive through the C-ABI struct path (NativeFrameAdapter) and render via
 * [WidgetMapper] into widget_root; [BOOT] status lines go to logcat and the
 * green-on-black console TextView.
 *
 * The wasmtime/.wasm boot path was retired from this Activity in Phase 3.0d,
 * and Phase 3.0e deleted the WASM era from the tree entirely — the NativeAOT
 * runtime is the only boot path.
 *
 * Threading/lifetime notes:
 *  - [runtime] is an Activity FIELD deliberately: it strongly holds the JNA
 *    frame callback; if it were a local, GC could collect the callback's
 *    trampoline while native code still points at it.
 *  - All throwables are caught → Log.e + "FAIL: ..." in the TextView so a
 *    boot crash is visible without attaching to logcat. Frame-level errors
 *    (adapter/consumer throws inside the JNA callback) route through
 *    onError → Log.e — JNA would otherwise swallow them to stderr.
 */
class MainActivity : Activity() {

    private val tag = "BlazorNative"

    /** Strong ref for the .so's lifetime — see class KDoc. */
    private lateinit var runtime: BlazorNativeRuntime

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.main)

        val view = findViewById<TextView>(R.id.markers)
        val widgetRoot = findViewById<FrameLayout>(R.id.widget_root)
        val mapper = WidgetMapper(this, widgetRoot)

        runtime = BlazorNativeRuntime(
            onFrame = { frame -> mapper.apply(frame) },
            onError = { msg, t -> Log.e(tag, msg, t) },
        )

        thread(name = "BlazorNative-Runtime-Boot") {
            try {
                val lines = runtime.start(
                    platformOs = "android",
                    apiLevel = android.os.Build.VERSION.SDK_INT,
                )
                // Emit each line as one Log.i call so logcat shows them as
                // atomic lines (filter via `adb logcat -s BlazorNative`).
                lines.forEach { Log.i(tag, it) }
                runOnUiThread { view.text = lines.joinToString("\n") }
            } catch (t: Throwable) {
                Log.e(tag, "Boot failed", t)
                runOnUiThread { view.text = "FAIL: ${t.javaClass.simpleName}: ${t.message}" }
            }
        }
    }
}
