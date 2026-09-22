package io.blazornative.shell

import android.content.Intent
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import androidx.core.graphics.Insets
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.util.concurrent.atomic.AtomicReference
import kotlin.math.roundToInt

/**
 * Phase 14.2 Task 6 (#338) — **`BnSafeArea`, ON THE DEVICE, THROUGH THE REAL WIRE**.
 *
 * Mounts `BnSafeAreaDemo` (the `/safearea` page) through the real NativeAOT boot and
 * asserts the canonical table from [bnSafeAreaDemoFrames] — the same discipline as
 * [BnLayoutDemoAndroidTest] / [BnScrollDemoAndroidTest], and the same cross-shell
 * pairing: the iOS XCTest twin asserts THE SAME NUMBERS on the simulator, via
 * `ShellFrameTableDriftTests` in the required `build-test` lane.
 *
 * ── WHY THIS CLASS DISPATCHES THE INSETS ITSELF ─────────────────────────────────
 * An AVD and an iOS simulator do not report the same real insets — sometimes neither
 * reports any at all. A table built from whatever the device happens to say could
 * never agree number-for-number across shells, which is the ONE property this file
 * exists to prove. So instead of reading the device's own insets, this test drives
 * the PRODUCTION listener directly: `MainActivity` registers
 * `ViewCompat.setOnApplyWindowInsetsListener(widgetRoot) { … }` in `onCreate`, and
 * `ViewCompat.dispatchApplyWindowInsets(widgetRoot, insets)` invokes that listener
 * exactly as the platform would on a real inset change — same conversion
 * (px → dp via density), same dedup (`reportSafeAreaIfChanged`), same
 * fire-and-forget dispatch onto the runtime. Nothing here is a test-only seam: it is
 * the real `WindowInsets` entry point, fed a synthetic value instead of a real
 * display cutout.
 *
 * The four dp values are asserted directly against BOTH BnDemoFrameTables files —
 * changing one without the other reds [ShellFrameTableDriftTests].
 */
@RunWith(AndroidJUnit4::class)
class BnSafeAreaAndroidTest {

    private companion object {
        // The SAME four numbers bnSafeAreaDemoFrames.kt/.swift declare. Distinct and
        // nonzero on all four edges — see BnDemoFrameTables.kt's header for why.
        const val TOP_DP = 47f
        const val RIGHT_DP = 21f
        const val BOTTOM_DP = 34f
        const val LEFT_DP = 13f

        // BnSafeAreaDemo.razor: the SafeArea's own explicit box, and what the single
        // Grow="1" child's frame must equal once the insets above are applied.
        const val BOX_W = 300f
        const val BOX_H = 200f
        const val EXPECTED_X = LEFT_DP
        const val EXPECTED_Y = TOP_DP
        const val EXPECTED_W = BOX_W - LEFT_DP - RIGHT_DP
        const val EXPECTED_H = BOX_H - TOP_DP - BOTTOM_DP
    }

    /**
     * Mounts the page, dispatches the fixed insets through the REAL
     * `OnApplyWindowInsetsListener`, waits for the resulting (fire-and-forget)
     * re-render to land, then asserts the padded child's frame against the
     * canonical table — proving TopEdge/RightEdge/BottomEdge/LeftEdge all wired
     * correctly in one frame, since the child's width depends on left+right and
     * its height on top+bottom (see BnDemoFrameTables.kt's header for the
     * Grow="1" derivation).
     */
    @Test fun safe_area_demo_pads_by_the_dispatched_insets_on_all_four_edges() {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val intent = Intent(ctx, MainActivity::class.java)
            .putExtra(MainActivity.EXTRA_COMPONENT, "BnSafeAreaDemo")

        ActivityScenario.launch<MainActivity>(intent).use { scenario ->
            assertTrue("BnSafeAreaDemo never rendered a laid-out tree within 60s",
                pollForDemo(scenario))

            dispatchFixedInsets(scenario)

            assertTrue("the safe-area padding never reached the content box within 30s of " +
                "dispatching safeAreaChanged — the wire (listener → dispatchHostEvent → " +
                "DispatchHostSafeArea → BnSafeAreaInsets.Current → BnSafeArea) never landed",
                pollForPadding(scenario))

            scenario.onActivity { act ->
                val f = bnSafeAreaDemoFrames
                assertFrame(f, "content", contentBoxOf(act),
                    "left=$LEFT_DP top=$TOP_DP right=$RIGHT_DP bottom=$BOTTOM_DP, dispatched " +
                        "through the real WindowInsets listener; width/height PROVE right/bottom " +
                        "because the child is Grow=1 inside a fixed 300×200 box")
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /** The SafeArea's own BnView is `widget_root`'s first child; its single Grow="1"
     * child (the content box) is that view's only child. */
    private fun contentBoxOf(act: MainActivity): View {
        val host = act.findViewById<FrameLayout>(R.id.widget_root)
        val safeArea = host.getChildAt(0) as ViewGroup
        return safeArea.getChildAt(0)
    }

    /** Builds a synthetic [WindowInsetsCompat] carrying ONLY `systemBars()` insets
     * (never `displayCutout()`), and feeds it to `widget_root` through
     * `ViewCompat.dispatchApplyWindowInsets` — invoking `MainActivity`'s real
     * `OnApplyWindowInsetsListener` directly, exactly as the platform would.
     *
     * `systemBars()` alone is deliberate: `getInsets(systemBars() or displayCutout())`
     * (MainActivity's own read) returns the UNION of both types, so leaving
     * `displayCutout()` at its default (`Insets.NONE`) keeps the union exactly equal
     * to what this test sets — a second nonzero type would make the union something
     * neither this test nor the table wrote down. */
    private fun dispatchFixedInsets(scenario: ActivityScenario<MainActivity>) {
        scenario.onActivity { act ->
            val widgetRoot = act.findViewById<FrameLayout>(R.id.widget_root)
            val d = act.resources.displayMetrics.density
            val insets = WindowInsetsCompat.Builder()
                .setInsets(
                    WindowInsetsCompat.Type.systemBars(),
                    Insets.of(
                        (LEFT_DP * d).roundToInt(),
                        (TOP_DP * d).roundToInt(),
                        (RIGHT_DP * d).roundToInt(),
                        (BOTTOM_DP * d).roundToInt(),
                    ),
                )
                .build()
            ViewCompat.dispatchApplyWindowInsets(widgetRoot, insets)
        }
    }

    /** Polls until the content box's frame matches the dispatched insets — the
     * dispatch above is fire-and-forget (MainActivity uses `dispatchHostEvent`, not
     * `…AndWait`, per #346), so the resulting patch lands on the dispatch lane
     * asynchronously. 0.5dp slop matches [assertFrame]'s own tolerance. */
    private fun pollForPadding(scenario: ActivityScenario<MainActivity>): Boolean {
        val deadline = System.currentTimeMillis() + 30_000
        val ready = AtomicReference(false)
        while (System.currentTimeMillis() < deadline) {
            scenario.onActivity { act ->
                val d = act.resources.displayMetrics.density
                val box = contentBoxOf(act)
                ready.set(
                    kotlin.math.abs(box.left / d - EXPECTED_X) < 0.5f &&
                        kotlin.math.abs(box.top / d - EXPECTED_Y) < 0.5f &&
                        kotlin.math.abs(box.width / d - EXPECTED_W) < 0.5f &&
                        kotlin.math.abs(box.height / d - EXPECTED_H) < 0.5f,
                )
            }
            if (ready.get()) break
            Thread.sleep(100)
        }
        return ready.get()
    }

    /** Polls until the mount frame has been applied and laid out: `widget_root`'s
     * first child (the SafeArea's own view) has its one Grow="1" child, and both have
     * a computed size. */
    private fun pollForDemo(scenario: ActivityScenario<MainActivity>): Boolean {
        val deadline = System.currentTimeMillis() + 60_000
        val ready = AtomicReference(false)
        while (System.currentTimeMillis() < deadline) {
            scenario.onActivity { act ->
                val root = act.findViewById<FrameLayout>(R.id.widget_root)
                    ?.takeIf { it.childCount > 0 }?.getChildAt(0) as? ViewGroup
                val content = root?.takeIf { it.childCount == 1 }?.getChildAt(0)
                ready.set(content != null && content.height > 0 && root.height > 0)
            }
            if (ready.get()) break
            Thread.sleep(250)
        }
        return ready.get()
    }
}
