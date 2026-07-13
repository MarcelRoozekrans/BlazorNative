package io.blazornative.shell

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.facebook.soloader.SoLoader
import com.facebook.yoga.YogaConstants
import com.facebook.yoga.YogaFlexDirection
import com.facebook.yoga.YogaMeasureOutput
import com.facebook.yoga.YogaNodeFactory
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.BeforeClass
import org.junit.Test
import org.junit.runner.RunWith

/**
 * Phase 6.0 Yoga spike (M6 DoD #1, Android rung): proves Facebook's Yoga C++
 * flexbox engine loads on the device (its libyoga.so coexisting with
 * libBlazorNative.Runtime.so in the APK) and that the native MEASURE CALLBACK
 * round-trip works — the load-bearing part (linking is table-stakes; measuring is
 * what makes flexbox usable). The Android twin of the iOS BnYogaTests.
 *
 * Uses the prebuilt `com.facebook.yoga:yoga:3.2.1` JNI bindings (the YogaNode Java
 * API RN Android uses). The native lib loads via SoLoader — initialised once from
 * the instrumentation target context. A deterministic stub measure func returns a
 * fixed 80×20 (Phase 6.1 wires real TextView.measure); the spike proves the
 * mechanism, not real measurement.
 *
 * Runs under the module's BlazorNativeTestRunner (sets BLAZORNATIVE_STRICT) like
 * every instrumented test, but touches NOTHING of the runtime — no MainActivity,
 * no dll — so it is independent of the shell's boot path.
 */
@RunWith(AndroidJUnit4::class)
class YogaSpikeAndroidTest {

    companion object {
        @JvmStatic
        @BeforeClass
        fun initSoLoader() {
            // Yoga's native lib loads via SoLoader — init it with the app context
            // before the first YogaNodeFactory.create().
            SoLoader.init(InstrumentationRegistry.getInstrumentation().targetContext, false)
        }
    }

    @Test
    fun yoga_lays_out_flex_row_and_measure_fires() {
        // row container, 300 wide.
        val root = YogaNodeFactory.create()
        root.setFlexDirection(YogaFlexDirection.ROW)
        root.setWidth(300f)
        root.setHeight(100f)

        // box1 — fixed 50×50.
        val box1 = YogaNodeFactory.create()
        box1.setWidth(50f)
        box1.setHeight(50f)
        root.addChildAt(box1, 0)

        // box2 — flexGrow 1 (fills the remaining width).
        val box2 = YogaNodeFactory.create()
        box2.setFlexGrow(1f)
        box2.setHeight(50f)
        root.addChildAt(box2, 1)

        // text — auto size via the measure callback (the load-bearing round-trip).
        var measureFired = false
        val text = YogaNodeFactory.create()
        text.setMeasureFunction { _, _, _, _, _ ->
            measureFired = true
            YogaMeasureOutput.make(80f, 20f)
        }
        root.addChildAt(text, 2)

        root.calculateLayout(YogaConstants.UNDEFINED, YogaConstants.UNDEFINED)

        // box1: fixed, at the left.
        assertEquals(0f, box1.layoutX, 0.5f)
        assertEquals(50f, box1.layoutWidth, 0.5f)
        // box2: after box1, flexGrow fills 300 - 50 - 80 = 170.
        assertEquals(50f, box2.layoutX, 0.5f)
        assertEquals(170f, box2.layoutWidth, 0.5f)
        // text: after box2 (left 220); its MAIN-axis (width) is the measured 80 —
        // the load-bearing proof the measure func's returned size drives layout.
        assertEquals(220f, text.layoutX, 0.5f)
        assertEquals(80f, text.layoutWidth, 0.5f)
        // The CROSS-axis (height) stretches to the row's 100 (default alignItems:
        // stretch) — the measured 20 is the intrinsic size, overridden cross-axis.
        assertEquals(100f, text.layoutHeight, 0.5f)
        // left-to-right placement.
        assertTrue("box1 left of box2", box1.layoutX < box2.layoutX)
        assertTrue("box2 left of text", box2.layoutX < text.layoutX)
        // THE round-trip: Yoga → shell measure func → Yoga used the returned size.
        assertTrue("the measure func must have fired", measureFired)
    }
}
