package io.blazornative.shell

import android.content.Intent
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.ScrollView
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import java.util.concurrent.atomic.AtomicReference

/**
 * Phase 6.3 Gate 2 Task 2.4 — **AN IMAGE INSIDE A SCROLL, AND THE 6.2 FRAME TABLE DOES NOT
 * MOVE** (6.3 non-negotiable #2, stated bluntly: *if a number in it moves, the change is
 * wrong*).
 *
 * Images inside a scroll viewport is the most common real usage of both features, and
 * leaving it unexercised until someone hits it is how you find out the hard way. But
 * `BnScrollDemo`'s frame table **is the 6.2 cross-platform parity contract**, so this test
 * asserts the image AND re-asserts every number of that table — *with the bytes loaded*,
 * which is the state [BnScrollDemoAndroidTest] never sees.
 *
 * ── WHY [BnScrollDemoAndroidTest] IS NOT THIS TEST, AND MUST NOT BECOME IT ──────────────
 * That class does not stand up the fixture server, so its row-0 image **fails to load**
 * (connection refused, immediately, offline). It asserts the same table and still passes —
 * which is not an accident to be tidied away but the *other half of the proof*: **a failed
 * load moves nothing either**, because the image's size is definite and there is no
 * measurement for a failure to change. Between the two classes, the 6.2 table is pinned
 * against an image that loaded and against one that did not.
 *
 * ── THE TWO INDEPENDENT REASONS THE TABLE CANNOT MOVE ───────────────────────────────────
 *  - **the row's height is DEFINITE (80)** — a child cannot grow a definite-height parent;
 *  - **the image's size is DEFINITE (40 × 40)** — both axes declared, so **Yoga never calls
 *    its measure func**. The bytes cannot move a frame even in principle, and the fixture's
 *    natural size (64 × 48, asserted) is nowhere in the answer.
 *
 * ── AND WHAT ONLY A SCROLL CAN PROVE ────────────────────────────────────────────────────
 * This image lives inside a **re-parented subtree under a SYNTHETIC content node** — a node
 * no patch ever names. It still fetches, still paints, and its in-flight request is still
 * cancelled when the page goes away (the subtree purge; pinned in [WidgetMapperImageTest]).
 */
@RunWith(AndroidJUnit4::class)
class BnScrollDemoImageAndroidTest {

    private lateinit var server: ImageFixtureServer

    private companion object {
        // BnScrollDemo.cs's constants and the two products it COMPUTES from them.
        const val ROWS = 10
        const val ROW_H = 80f
        const val VIEW_W = 300f
        const val VIEW_H = 200f
        const val CONTENT_H = ROWS * ROW_H          // 800
        const val SCROLL_RANGE = CONTENT_H - VIEW_H // 600
        const val IMAGE_ROW = 0
        const val IMAGE_W = 40f
        const val IMAGE_H = 40f
    }

    @Before fun startFixtureServer() {
        ImageFixtureServer.clearCoilCaches()
        server = ImageFixtureServer()
        server.release() // this page has no BEFORE table to protect — 6.2's is the contract
    }

    /**
     * isInitialized: a @Before that THREW (a taken port) must not have its cause masked.
     *
     * **AND THE SERVER'S OWN ERRORS ARE ASSERTED EMPTY** (Gate 2 review, I4): this class, like
     * [BnImageDemoAndroidTest], **cancels nothing** — no client here drops a connection — so an
     * `IOException` on a fixture-server worker thread is a real server bug, and the swallow that
     * (correctly) keeps a broken pipe from killing the app process would otherwise make it
     * completely silent.
     */
    @After fun stopFixtureServer() {
        if (!::server.isInitialized) return
        val errors = server.errors
        server.close()
        assertEquals("the fixture server hit an IOException, and NOTHING ON THIS PAGE CANCELS " +
            "ANYTHING — so it is a real server bug rather than a dropped client",
            emptyList<String>(), errors)
    }

    @Test fun the_row_image_loads_and_the_6_2_frame_table_is_UNCHANGED() {
        val fixture = server.decoded(server.fixedPng)
        assertTrue("the row image's fixture must NOT be 40 × 40 — otherwise 'it measures " +
            "40 × 40' is a coincidence rather than a proof that the declared size " +
            "short-circuits measurement. Got ${fixture.width} × ${fixture.height}",
            fixture.width != 40 || fixture.height != 40)

        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val intent = Intent(ctx, MainActivity::class.java)
            .putExtra(MainActivity.EXTRA_COMPONENT, "BnScrollDemo")

        ActivityScenario.launch<MainActivity>(intent).use { scenario ->
            assertTrue("BnScrollDemo never rendered a laid-out tree within 60s",
                pollForDemo(scenario))

            // THE SYNCHRONIZATION GATE, in its one-request form: the table below is only a
            // statement about an image that LOADED once Coil's own terminal callback says so.
            awaitTheRowImage(scenario)

            scenario.onActivity { act ->
                assertEquals("the row image's request succeeded — from the loopback fixture, " +
                    "over real HTTP, from inside a SCROLLED subtree",
                    listOf(ImageFixtureServer.FIXED_URL to WidgetMapper.ImageOutcome.SUCCESS),
                    act.mapper.imageResults.map { it.url to it.outcome })

                val d = act.resources.displayMetrics.density
                val root = act.findViewById<FrameLayout>(R.id.widget_root).getChildAt(0) as ViewGroup
                val scroll = root.getChildAt(0) as ScrollView
                val content = scroll.getChildAt(0) as ViewGroup

                // ── THE IMAGE ────────────────────────────────────────────────────
                val row0 = content.getChildAt(IMAGE_ROW) as ViewGroup
                assertEquals("row 0 has exactly one child: the image", 1, row0.childCount)
                val image = row0.getChildAt(0) as ImageView
                assertNotNull("THE BYTES LANDED, inside a scrolled, re-parented subtree under " +
                    "the SYNTHETIC content node", image.drawable)
                assertFrame("the row image: (0, 0, 40, 40) in the row's coordinates. Both axes " +
                    "declared ⇒ Yoga never called its measure func, so the fixture's own " +
                    "${fixture.width} × ${fixture.height} is nowhere in this frame",
                    image, 0f, 0f, IMAGE_W, IMAGE_H)
                assertTrue("…and it is strictly SMALLER than its row in both axes, so it cannot " +
                    "overflow and raise a clipping question the two shells would answer " +
                    "differently", image.width < row0.width && image.height < row0.height)

                // ── AND NOW: EVERY NUMBER OF THE 6.2 TABLE, UNCHANGED ────────────
                // Non-negotiable #2. If one of these moves, the change is wrong.
                assertFrame("the viewport", scroll, 0f, 0f, VIEW_W, VIEW_H)
                assertEquals("the ScrollView's ONLY child is still the synthetic content view",
                    1, scroll.childCount)
                assertFrame("THE CONTENT SIZE: still 800 — ten 80-high rows in a height:auto " +
                    "column, computed by Yoga. A child that measured could have grown row 0 " +
                    "and every number after it", content, 0f, 0f, VIEW_W, CONTENT_H)
                assertEquals("…and the scrollable range is still 800 − 200",
                    SCROLL_RANGE, (content.height - scroll.height) / d, 0.5f)

                assertEquals("still ten rows", ROWS, content.childCount)
                for (i in 0 until ROWS) {
                    assertFrame("row $i is still at y = 80×$i, 300 × 80",
                        content.getChildAt(i), 0f, ROW_H * i, VIEW_W, ROW_H)
                }

                val flexRow = (content.getChildAt(1) as ViewGroup).getChildAt(0) as ViewGroup
                assertFrame("the nested flex row", flexRow, 0f, 0f, VIEW_W, ROW_H)
                assertFrame("box A", flexRow.getChildAt(0), 0f, 0f, 50f, ROW_H)
                assertFrame("box B (Grow=1) — still absorbing 300 − 50 − 50",
                    flexRow.getChildAt(1), 50f, 0f, 200f, ROW_H)
                assertFrame("box C", flexRow.getChildAt(2), 250f, 0f, 50f, ROW_H)

                val backRow = root.getChildAt(1) as ViewGroup
                assertEquals("the back row still starts where the viewport ends (y = 200)",
                    VIEW_H, backRow.top / d, 0.5f)
                assertEquals("the root column still HUGS its two sections",
                    backRow.bottom, root.height)
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private fun awaitTheRowImage(scenario: ActivityScenario<MainActivity>) {
        val deadline = System.currentTimeMillis() + 30_000
        val seen = AtomicReference(0)
        while (System.currentTimeMillis() < deadline) {
            scenario.onActivity { act -> seen.set(act.mapper.imageResults.size) }
            if (seen.get() >= 1) {
                InstrumentationRegistry.getInstrumentation().waitForIdleSync()
                return
            }
            Thread.sleep(100)
        }
        throw AssertionError("the row image's request never terminated within 30s. A timeout " +
            "here FAILS: 'the table did not move' is only worth asserting about an image that " +
            "actually loaded.")
    }

    private fun pollForDemo(scenario: ActivityScenario<MainActivity>): Boolean {
        val deadline = System.currentTimeMillis() + 60_000
        val ready = AtomicReference(false)
        while (System.currentTimeMillis() < deadline) {
            scenario.onActivity { act ->
                val root = act.findViewById<FrameLayout>(R.id.widget_root)
                    ?.takeIf { it.childCount > 0 }?.getChildAt(0) as? ViewGroup
                val scroll = root?.takeIf { it.childCount == 2 }?.getChildAt(0) as? ScrollView
                val content = scroll?.takeIf { it.childCount == 1 }?.getChildAt(0) as? ViewGroup
                ready.set(content != null && content.childCount == ROWS && content.height > 0 &&
                    root.height > 0)
            }
            if (ready.get()) break
            Thread.sleep(250)
        }
        return ready.get()
    }
}
