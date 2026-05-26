package io.blazornative.shell

import android.app.Activity
import android.os.Bundle
import android.util.Log
import android.widget.TextView
import io.blazornative.jni.WasiHost
import kotlin.concurrent.thread

/**
 * Phase 2.2 Android shell entry point.
 *
 * On launch: spawns a background thread that
 *   1. reads BlazorNative.WasiHost.wasm from app assets (~13 MB),
 *   2. invokes WasiHost.loadAndRun(bytes, cacheDir),
 *   3. emits each captured stdout line via Log.i("BlazorNative", line),
 *   4. displays the full captured stdout in the green-on-black console TextView.
 *
 * The background thread keeps the UI responsive during the ~500ms-1500ms
 * cold JIT compile + Mono-AOT init that runs the first time the .wasm boots.
 * All throwables are caught → Log.e + "FAIL: ..." in the TextView so a
 * runtime crash is visible without needing to attach to logcat.
 */
class MainActivity : Activity() {

    private val tag = "BlazorNative"

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.main)

        val view = findViewById<TextView>(R.id.markers)

        thread(name = "BlazorNative-WasiHost-Boot") {
            try {
                val wasmBytes = assets.open("BlazorNative.WasiHost.wasm").use { it.readBytes() }
                Log.i(tag, "Loaded ${wasmBytes.size} bytes of .wasm from assets; booting...")

                val stdout = WasiHost.loadAndRun(wasmBytes, cacheDir)

                // Emit each captured line as one Log.i call so logcat shows
                // them as atomic lines (filter via `adb logcat -s BlazorNative`).
                stdout.lineSequence().filter { it.isNotBlank() }.forEach { line ->
                    Log.i(tag, line)
                }

                runOnUiThread { view.text = stdout }
            } catch (t: Throwable) {
                Log.e(tag, "Boot failed", t)
                runOnUiThread { view.text = "FAIL: ${t.javaClass.simpleName}: ${t.message}" }
            }
        }
    }
}
