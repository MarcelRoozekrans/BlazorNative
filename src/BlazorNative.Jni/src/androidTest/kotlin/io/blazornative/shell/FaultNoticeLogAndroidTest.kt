package io.blazornative.shell

import android.content.Intent
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.FrameLayout
import android.widget.TextView
import androidx.test.core.app.ActivityScenario
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.sun.jna.Memory
import io.blazornative.jni.BnLogFormat
import io.blazornative.jni.CameraStatus
import io.blazornative.jni.NativeBindings
import org.junit.After
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import java.util.Collections
import java.util.concurrent.atomic.AtomicReference

/**
 * Phase 17.4 (#426) — a FaultNotice's message is readable in logcat in a debuggable build.
 *
 * The instrumented APK is debuggable, so `MainActivity` sets [BnShellLog.reveal] and the
 * shell's error line carries the fault's text. BnCameraDemo's Take Photo awaits the camera
 * call; the test holds the capture, cancels it, and answers the completion with Captured and
 * a payload that is not flat JSON, as FaultNoticeTest does on the JVM. The demo's
 * continuation throws a FormatException after its await, which .NET sends as a FaultNotice,
 * and `MainActivity`'s onError writes it through [BnShellLog.error].
 *
 * DOES NOT COVER: the redacted half. A Release APK cannot run instrumented tests, so
 * `<private>` is pinned on the JVM by BnShellLogTest, and the reveal switch's wiring by
 * LogPrivacyDriftTests.
 */
@RunWith(AndroidJUnit4::class)
class FaultNoticeLogAndroidTest {

    private val lines = Collections.synchronizedList(mutableListOf<Triple<Int, String, String>>())
    private lateinit var originalSink: (Int, String, String) -> Unit
    private lateinit var originalThrowableSink: (Int, String, String, Throwable) -> Unit

    @Before
    fun setup() {
        AndroidShellBridge.resetCameraForTest()
        AndroidShellBridge.resetHostCallContractForTest()
        AndroidShellBridge.lastHostCallCompleteRcForTest = Int.MIN_VALUE
        originalSink = BnShellLog.sink
        originalThrowableSink = BnShellLog.throwableSink
        val sink = originalSink
        val throwableSink = originalThrowableSink
        BnShellLog.sink = { priority, tag, message ->
            lines.add(Triple(priority, tag, message))
            sink(priority, tag, message)
        }
        BnShellLog.throwableSink = { priority, tag, message, t ->
            lines.add(Triple(priority, tag, message))
            throwableSink(priority, tag, message, t)
        }
    }

    @After
    fun cleanup() {
        BnShellLog.sink = originalSink
        BnShellLog.throwableSink = originalThrowableSink
        AndroidShellBridge.resetCameraForTest()
        AndroidShellBridge.resetHostCallContractForTest()
    }

    @Test
    fun a_fault_notice_message_is_readable_in_a_debuggable_build() {
        val held = AtomicReference<AndroidShellBridge.CameraCapture?>(null)
        AndroidShellBridge.cameraCaptureHook = { capture -> held.set(capture) }
        // The cancel's completion is replaced by FaultNoticeTest's answer: Captured, with a
        // payload BnCameraDemo cannot parse.
        AndroidShellBridge.completeHookForTest = { requestId, _, _, _ ->
            val bad = Memory(6).apply { setString(0, "{bad", "UTF-8") }
            AndroidShellBridge.lastHostCallCompleteRcForTest =
                NativeBindings.INSTANCE.blazornative_host_call_complete(requestId, CameraStatus.CAPTURED, bad)
            java.lang.ref.Reference.reachabilityFence(bad)
        }

        launch("BnCameraDemo").use { scenario ->
            // Anchor: the APK under test is debuggable, so the switch is on and the line's text
            // is written as given. Without this the assertion below would be about `<private>`.
            assertTrue("anchor: BnShellLog.reveal is false in a debuggable instrumented APK",
                BnShellLog.reveal)

            assertTrue("BnCameraDemo never rendered within 60s", pollUntil(scenario, 60_000) { echoText(it) != null })
            tapButton(scenario, "Take Photo")
            assertTrue("anchor: the capture was never handed to the hook", pollTrue(10_000) { held.get() != null })

            held.get()!!.cancel()
            assertTrue("anchor: the malformed answer never reached a live .NET continuation (rc " +
                "${AndroidShellBridge.lastHostCallCompleteRcForTest})",
                pollTrue(10_000) { AndroidShellBridge.lastHostCallCompleteRcForTest == 0 })

            val faultLine = "handler faulted after it began a shell call or yielded: System.FormatException"
            assertTrue("no ERROR line containing '$faultLine' reached BnShellLog within 10s. Lines seen: " +
                synchronized(lines) { lines.toList() },
                pollTrue(10_000) {
                    synchronized(lines) {
                        lines.any { (priority, _, message) -> priority == BnLogFormat.ERROR && faultLine in message }
                    }
                })
        }
    }

    private fun launch(component: String): ActivityScenario<MainActivity> {
        val intent = Intent(ApplicationProvider.getApplicationContext(), MainActivity::class.java)
        intent.putExtra(MainActivity.EXTRA_COMPONENT, component)
        return ActivityScenario.launch<MainActivity>(intent).awaitBooted()
    }

    private fun widgetRoot(act: MainActivity): FrameLayout? = act.findViewById(R.id.widget_root)

    private fun echoText(act: MainActivity): TextView? {
        val root = widgetRoot(act)?.takeIf { it.childCount > 0 }?.getChildAt(0) as? ViewGroup ?: return null
        for (i in 0 until root.childCount) {
            val child = root.getChildAt(i)
            if (child is TextView && child !is Button) return child
        }
        return null
    }

    private fun tapButton(scenario: ActivityScenario<MainActivity>, label: String) {
        val clicked = AtomicReference(false)
        scenario.onActivity { act ->
            (widgetRoot(act)?.let { root -> firstMatch(root) { v -> v is Button && v.text.toString() == label } } as? Button)
                ?.let { it.performClick(); clicked.set(true) }
        }
        assertTrue("Button '$label' not found on screen", clicked.get())
    }

    private fun pollTrue(deadlineMs: Long, predicate: () -> Boolean): Boolean {
        val deadline = System.currentTimeMillis() + deadlineMs
        while (System.currentTimeMillis() < deadline) {
            if (predicate()) return true
            Thread.sleep(100)
        }
        return predicate()
    }

    private fun firstMatch(view: View, predicate: (View) -> Boolean): View? {
        if (predicate(view)) return view
        if (view is ViewGroup) for (i in 0 until view.childCount) firstMatch(view.getChildAt(i), predicate)?.let { return it }
        return null
    }
}
