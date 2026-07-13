package io.blazornative.shell

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.facebook.soloader.SoLoader
import com.facebook.yoga.YogaAlign
import com.facebook.yoga.YogaConstants
import com.facebook.yoga.YogaDirection
import com.facebook.yoga.YogaFlexDirection
import com.facebook.yoga.YogaMeasureOutput
import com.facebook.yoga.YogaNodeFactory
import io.blazornative.jni.NativeBindings
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.BeforeClass
import org.junit.Test
import org.junit.runner.RunWith

/**
 * Phase 6.0 Yoga spike (M6 DoD #1, Android rung): proves Facebook's Yoga C++
 * flexbox engine loads on the device and that the native MEASURE CALLBACK
 * round-trip works — the load-bearing part (linking is table-stakes; measuring is
 * what makes flexbox usable). The Android twin of the iOS BnYogaTests.
 *
 * It builds the CANONICAL tree — byte-identical to the one BnYogaProbe.mm builds on
 * the iOS rung — and asserts the SAME twelve numbers (x/y/w/h × 3 nodes). That
 * pairing is what makes "identical frames from one engine on two platforms" an
 * ASSERTED result rather than a claim; frame parity is the whole architectural
 * reason for choosing Yoga over two native layout systems.
 *
 * COEXISTENCE: after the Yoga assertions the test reaches the NativeAOT runtime
 * through the shell's existing JNA binding, so ONE process demonstrably has BOTH
 * native libraries loaded — Yoga's libyoga.so (via SoLoader) and
 * libBlazorNative.Runtime.so (via JNA's dlopen). Without that touch nothing on the
 * Android side would prove the two coexist in a live process. (iOS gets this for
 * free: the hosted XCTest runs inside the app binary that links both archives.)
 *
 * Uses the prebuilt `com.facebook.yoga:yoga:3.2.1` JNI bindings (the YogaNode Java
 * API RN Android uses). The native lib loads via SoLoader — initialised once from
 * the instrumentation target context. A deterministic stub measure func returns a
 * fixed 80×20 (Phase 6.1 wires real TextView.measure); the spike proves the
 * mechanism, not real measurement.
 *
 * Runs under the module's BlazorNativeTestRunner (sets BLAZORNATIVE_STRICT) like
 * every instrumented test.
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
        // ── The canonical tree (same shape, same numbers, as BnYogaProbe.mm) ──
        // root — row container, 300 × 100, direction LTR (set EXPLICITLY, not left
        // to a platform default, so the two rungs cannot silently diverge).
        val root = YogaNodeFactory.create()
        root.setDirection(YogaDirection.LTR)
        root.setFlexDirection(YogaFlexDirection.ROW)
        root.setWidth(300f)
        root.setHeight(100f)

        // box1 — fixed 50 × 50.
        val box1 = YogaNodeFactory.create()
        box1.setWidth(50f)
        box1.setHeight(50f)
        root.addChildAt(box1, 0)

        // box2 — flexGrow 1, height 50 (fills the remaining width).
        val box2 = YogaNodeFactory.create()
        box2.setFlexGrow(1f)
        box2.setHeight(50f)
        root.addChildAt(box2, 1)

        // text — no width/height; sized by the measure callback (the load-bearing
        // round-trip). alignSelf FLEX_START is deliberate: under the default
        // alignItems:stretch the leaf's cross axis would take the row's 100 and the
        // MEASURED HEIGHT (20) would be discarded — only the width channel of the
        // round-trip would be proven. flex-start makes the frame height the measured
        // 20, so text.h == 20 proves the returned height reaches the frame too (the
        // channel Phase 6.1's text wrapping depends on).
        var measureFired = false
        val text = YogaNodeFactory.create()
        text.setAlignSelf(YogaAlign.FLEX_START)
        text.setMeasureFunction { _, _, _, _, _ ->
            measureFired = true
            YogaMeasureOutput.make(80f, 20f)
        }
        root.addChildAt(text, 2)

        root.calculateLayout(YogaConstants.UNDEFINED, YogaConstants.UNDEFINED)

        // ── The twelve numbers (identical to the iOS rung's) ──
        // box1: fixed, at the left.
        assertEquals("box1.x", 0f, box1.layoutX, 0.5f)
        assertEquals("box1.y", 0f, box1.layoutY, 0.5f)
        assertEquals("box1.w", 50f, box1.layoutWidth, 0.5f)
        assertEquals("box1.h", 50f, box1.layoutHeight, 0.5f)
        // box2: after box1; flexGrow fills 300 - 50 - 80 = 170.
        assertEquals("box2.x", 50f, box2.layoutX, 0.5f)
        assertEquals("box2.y", 0f, box2.layoutY, 0.5f)
        assertEquals("box2.w", 170f, box2.layoutWidth, 0.5f)
        assertEquals("box2.h", 50f, box2.layoutHeight, 0.5f)
        // text: after box2 (left 220). BOTH channels of the measure round-trip land
        // in the frame — main-axis width IS the measured 80, cross-axis height IS
        // the measured 20 (not stretched to the row's 100).
        assertEquals("text.x", 220f, text.layoutX, 0.5f)
        assertEquals("text.y", 0f, text.layoutY, 0.5f)
        assertEquals("text.w — the MEASURED width reaches the frame", 80f, text.layoutWidth, 0.5f)
        assertEquals("text.h — the MEASURED height reaches the frame", 20f, text.layoutHeight, 0.5f)
        // left-to-right placement.
        assertTrue("box1 left of box2", box1.layoutX < box2.layoutX)
        assertTrue("box2 left of text", box2.layoutX < text.layoutX)
        // THE round-trip: Yoga → shell measure func → Yoga used the returned size.
        assertTrue("the measure func must have fired", measureFired)

        // ── Coexistence, in THIS process ──
        // libyoga.so is loaded (everything above ran through it). Now load the
        // NativeAOT runtime .so through the shell's existing JNA binding — the same
        // NativeBindings.INSTANCE every other instrumented test uses. blazornative_
        // version() reads a static cstring built in the managed static ctor, so it
        // needs no init/mount: the cheapest possible touch that still forces the
        // dlopen. A non-empty version here means both native libs are live in one
        // process — which is exactly the claim the spike makes about the APK.
        val version = NativeBindings.INSTANCE.blazornative_version().getString(0, "UTF-8")
        assertTrue(
            "the runtime .so must be loaded in the SAME process as Yoga (version was '$version')",
            version.contains("BlazorNative.Runtime")
        )
    }
}
