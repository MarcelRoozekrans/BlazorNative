package io.blazornative.shell

import android.content.Intent
import android.graphics.Color
import android.graphics.drawable.BitmapDrawable
import android.graphics.drawable.ColorDrawable
import android.net.Uri
import android.view.ViewGroup
import android.widget.Button
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.TextView
import androidx.test.core.app.ActivityScenario
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import java.util.concurrent.atomic.AtomicReference

/**
 * Phase 7.5 Gate 2 — **THE IMAGE-POLISH DEMO, ON THE DEVICE** (M7 DoD #6).
 *
 * Mounts `BnImagePolishDemo` (`/imagepolish`) through the real NativeAOT boot and walks the
 * page through its THREE states — everything held / everything but `/slow.png` terminated /
 * everything terminated — asserting the `.razor` header's frame table in each. The
 * difference between the states is the phase: **the frames barely differ** (one band moves,
 * once), and that near-identity is the proof that a placeholder never measures, an error
 * never re-measures, and a mode never consults measure.
 *
 * ── THE SYNCHRONIZATION GATE COUNTS **EIGHT** ────────────────────────────────────────
 * The page carries EIGHT src-bearing image nodes: [0] slow ×1, [1] missing ×1, [2] fixed
 * ×4, [3] missing ×1, [4] intrinsic ×1 — count them off the razor's own markup. (The
 * `.razor` header's gate sentence says "= seven"; its arithmetic is wrong — 1+1+4+1+1 = 8 —
 * and a gate that awaited seven would let the AFTER assertions race the eighth request.
 * The count here is DERIVED from the page, and the positive outcome assertions below would
 * catch a page that grew or lost a node.) `OnError` joins the evidence — the counted
 * dispatch is case [1]'s — but does not replace the gate: success still has no wire signal.
 *
 * ── WHY SO MANY ASSERTIONS RIDE ONE TEST ─────────────────────────────────────────────
 * The three states are one mount's LIFECYCLE: "the placeholder was painted, then the same
 * node's placeholder survived the 404, then the still-held node released and cleared" is a
 * statement about ONE page instance, and three mounts would assert three different pages.
 * [BnImageDemoAndroidTest.the_frame_tables_BEFORE_and_AFTER_the_bytes] is the shape.
 */
@RunWith(AndroidJUnit4::class)
class BnImagePolishDemoAndroidTest {

    private lateinit var server: ImageFixtureServer

    private companion object {
        // BnImagePolishDemo.razor's constants (the SECTION_W transcription posture: a
        // device test cannot read a .razor; the outcome assertions against the WIRE's own
        // URLs are the drift pin, and the golden .NET test asserts the same numbers off
        // the wire). The section offsets are the razor's COMPUTED consts, recomputed here
        // from the same parts so a changed band height is one edit on each side.
        const val SECTION_W = 300f
        const val BAND_H = 20f
        const val DECLARED_W = 200f
        const val DECLARED_H = 120f
        const val ECHO_ROW_H = 24f
        const val MODE_W = 120f
        const val MODE_H = 60f
        const val PLACEHOLDER_HEX = "#FFCA28"

        const val LOADING_H = DECLARED_H + BAND_H                       // [0] hugs 140
        const val ERROR_Y = LOADING_H                                   // [1] at 140
        const val ERROR_H = DECLARED_H + BAND_H + ECHO_ROW_H            // [1] hugs 164
        const val QUARTET_Y = ERROR_Y + ERROR_H                         // [2] at 304
        const val MODE_STEP = MODE_H + BAND_H                           // 80
        const val QUARTET_H = 4 * MODE_STEP                             // 320
        const val INTRINSIC_FAILING_Y = QUARTET_Y + QUARTET_H           // [3] at 624
        const val INTRINSIC_LOADING_Y = INTRINSIC_FAILING_Y + BAND_H    // [4] at 644
        const val BACK_Y_BEFORE = INTRINSIC_LOADING_Y + BAND_H          // 664; after: +Hi

        /** The eight terminal callbacks the gate awaits — see the class KDoc (DERIVED:
         * 1 slow + 2 missing + 4 fixed + 1 intrinsic; the razor header's "seven" is a
         * miscount, recorded there and here). */
        const val ALL_REQUESTS = 8

        /** Everything except the held `/slow.png` — the MID state's gate. */
        const val ALL_BUT_SLOW = ALL_REQUESTS - 1

        const val ECHO_INITIAL = "err:0"
        val ECHO_AFTER_ONE = "err:1 ${ImageFixtureServer.MISSING_URL}"
    }

    private val placeholderColor: Int get() = Color.parseColor(PLACEHOLDER_HEX)

    @Before fun startFixtureServer() {
        ImageFixtureServer.clearCoilCaches()
        server = ImageFixtureServer()
    }

    // isInitialized: a @Before that THREW must not have its cause masked. No server-error
    // emptiness assert: closing a scenario with `/slow.png` still held (the deep-link and
    // back-row mounts) CANCELS it, and a broken pipe is that cancellation seen from the
    // other end of the socket — the WidgetMapperImageTest posture.
    @After fun stopFixtureServer() {
        if (::server.isInitialized) server.close()
    }

    /**
     * **THE FULL TABLE, THROUGH THE THREE STATES** — the razor header's six cases, each
     * band's y asserted in every state it is observable in.
     */
    @Test fun the_frame_table_through_held_then_released_then_slow_released() {
        // ── THE FIXTURE CONTRACT, on the DECODED bytes, BEFORE ANY FRAME ──────────
        val fixed = server.decoded(server.fixedPng)
        val intrinsic = server.decoded(server.intrinsicPng)
        val hi = intrinsic.height.toFloat()
        val wi = intrinsic.width.toFloat()
        assertTrue("Hi > 0 — case [4]'s band moves by exactly Hi", hi > 0f)
        assertTrue("0 < Wi ≤ 300", wi > 0f && wi <= SECTION_W)
        assertTrue("the mode fixture's aspect (${fixed.width}:${fixed.height}) must DISAGREE " +
            "with the 120 × 60 box (2:1) — otherwise the four modes paint identically and " +
            "the quartet proves nothing about paint",
            fixed.width * MODE_H.toInt() != fixed.height * MODE_W.toInt())

        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val intent = Intent(ctx, MainActivity::class.java)
            .putExtra(MainActivity.EXTRA_COMPONENT, "BnImagePolishDemo")

        ActivityScenario.launch<MainActivity>(intent).use { scenario ->
            assertTrue("BnImagePolishDemo never rendered a laid-out tree within 60s",
                pollForDemo(scenario))

            // ══ STATE 1: EVERYTHING HELD ════════════════════════════════════════
            scenario.onActivity { act ->
                assertEquals("no request has terminated — the fixture server is HOLDING " +
                    "every response (Coil's caches were cleared), which is what makes the " +
                    "held state an assertable STATE rather than a race",
                    0, act.mapper.imageResults.size)

                val root = root(act)
                assertEquals("six sections: loading, error, quartet, intrinsic-failing, " +
                    "intrinsic-loading, back", 6, root.childCount)

                // [0] PLACEHOLDER-WHILE-LOADING — held, so "while loading" is NOW.
                val s0 = root.getChildAt(0) as ViewGroup
                assertFrame("[0] section HUGS 120 + 20", s0, 0f, 0f, SECTION_W, LOADING_H)
                assertFrame("[0] image: the DECLARED box, while in flight", imageIn(s0),
                    0f, 0f, DECLARED_W, DECLARED_H)
                assertPlaceholder("[0] in flight", imageIn(s0))
                assertFrame("[0] band L at y = 120", s0.getChildAt(1),
                    0f, DECLARED_H, SECTION_W, BAND_H)

                // [1] ERROR, SPACE KEPT — before the 404 lands, it is just a declared box.
                val s1 = root.getChildAt(1) as ViewGroup
                assertFrame("[1] section at y = 140, HUGS 120 + 20 + 24", s1,
                    0f, ERROR_Y, SECTION_W, ERROR_H)
                assertFrame("[1] image: the declared box", imageIn(s1),
                    0f, 0f, DECLARED_W, DECLARED_H)
                assertPlaceholder("[1] in flight", imageIn(s1))
                assertFrame("[1] band E at y = 120", s1.getChildAt(1),
                    0f, DECLARED_H, SECTION_W, BAND_H)
                assertFrame("[1] the echo row: FIXED height, so the round-trip re-renders " +
                    "text inside a box that cannot move", s1.getChildAt(2),
                    0f, DECLARED_H + BAND_H, SECTION_W, ECHO_ROW_H)
                assertEquals("[1] the echo's mount state", ECHO_INITIAL, echoText(act))

                // [2] THE FOUR MODES — the modes are already APPLIED (props ride the mount
                // batch); only the bytes are missing.
                assertQuartet(act, bytesLanded = false)

                // [3] / [4] INTRINSIC — 0 × 0 with placeholders present: the placeholder
                // does not measure, from both sides.
                val s3 = root.getChildAt(3) as ViewGroup
                assertFrame("[3] section at y = 624: band-only, 20 high", s3,
                    0f, INTRINSIC_FAILING_Y, SECTION_W, BAND_H)
                assertFrame("[3] image: ZERO — a placeholder never measures", imageIn(s3),
                    0f, 0f, 0f, 0f)
                assertFrame("[3] band X at y = 0 in its parent", s3.getChildAt(1),
                    0f, 0f, SECTION_W, BAND_H)

                val s4 = root.getChildAt(4) as ViewGroup
                assertFrame("[4] section at y = 644: band-only before the bytes", s4,
                    0f, INTRINSIC_LOADING_Y, SECTION_W, BAND_H)
                assertFrame("[4] image: ZERO before the bytes", imageIn(s4), 0f, 0f, 0f, 0f)
                assertFrame("[4] band I at y = 0 — THE REFLOW HAS NOT HAPPENED",
                    s4.getChildAt(1), 0f, 0f, SECTION_W, BAND_H)

                // [5] the back row, LAST (the only font-measured leaf).
                val back = root.getChildAt(5) as ViewGroup
                assertEquals("[5] back row at y = 664 before the reflow",
                    BACK_Y_BEFORE, back.top / density(), 0.5f)
                assertEquals("← Back", (back.getChildAt(0) as Button).text.toString())

                assertEquals("nothing dispatched while everything is held",
                    0, act.mapper.errorDispatchesSent)
            }

            // ══ STATE 2: THE GATE OPENS — everything but /slow.png terminates ═════
            server.release()
            awaitTerminated(scenario, ALL_BUT_SLOW)
            assertTrue("the error round-trip never re-rendered the echo — the dispatch " +
                "crossed the wire, .NET counted it, and the re-render must come back",
                pollUntil(scenario, 15_000) { act -> echoText(act) == ECHO_AFTER_ONE })

            scenario.onActivity { act ->
                // THE OUTCOMES, against the URLs the WIRE carried — the drift pin on the
                // razor's sources (they alias BnImageDemo's constants + the new /slow.png).
                val outcomes = act.mapper.imageResults.groupingBy { it.url to it.outcome }
                    .eachCount()
                assertEquals("4× fixed SUCCESS + 2× missing ERROR + 1× intrinsic SUCCESS — " +
                    "and not one CANCELLED (nothing on this page cancels anything)",
                    mapOf(
                        (ImageFixtureServer.FIXED_URL to WidgetMapper.ImageOutcome.SUCCESS) to 4,
                        (ImageFixtureServer.MISSING_URL to WidgetMapper.ImageOutcome.ERROR) to 2,
                        (ImageFixtureServer.INTRINSIC_URL to WidgetMapper.ImageOutcome.SUCCESS) to 1,
                    ), outcomes)

                // THE COUNTED DISPATCH: two failures, ONE attach ([3] is deliberately
                // unbound — attach-iff-HasDelegate as a page-level fact), ONE dispatch.
                assertEquals("`error` dispatched EXACTLY ONCE: case [1]'s failure, counted " +
                    "on the wire — the unbound failure [3] moved no counter",
                    1, act.mapper.errorDispatchesSent)
                assertEquals("…and the echo carries the count AND the payload: the wire's " +
                    "src, verbatim", ECHO_AFTER_ONE, echoText(act))

                val root = root(act)

                // [0] STILL HELD: placeholder still painting, frame identical.
                val s0 = root.getChildAt(0) as ViewGroup
                assertFrame("[0] section: UNCHANGED while still in flight", s0,
                    0f, 0f, SECTION_W, LOADING_H)
                assertPlaceholder("[0] still in flight", imageIn(s0))

                // [1] ERROR, SPACE KEPT — the section's whole frame is IDENTICAL through
                // the failure AND the echo re-render.
                val s1 = root.getChildAt(1) as ViewGroup
                assertFrame("[1] section after the 404 + the echo re-render: IDENTICAL — " +
                    "the box held because it was DECLARED, and the echo row is fixed",
                    s1, 0f, ERROR_Y, SECTION_W, ERROR_H)
                assertFrame("[1] image: the declared box, held", imageIn(s1),
                    0f, 0f, DECLARED_W, DECLARED_H)
                assertPlaceholder("[1] after the 404 — the ERROR row KEEPS the placeholder",
                    imageIn(s1))
                assertFrame("[1] band E: y = 120, identical before/after the failure",
                    s1.getChildAt(1), 0f, DECLARED_H, SECTION_W, BAND_H)

                // [2] the quartet, bytes landed: FOUR IDENTICAL FRAMES, four modes.
                assertQuartet(act, bytesLanded = true)

                // [3] the failing intrinsic: 0 × 0 forever, band never moved.
                val s3 = root.getChildAt(3) as ViewGroup
                assertFrame("[3] section: STILL band-only at y = 624", s3,
                    0f, INTRINSIC_FAILING_Y, SECTION_W, BAND_H)
                assertFrame("[3] image: STILL zero — a placeholder measured NOTHING through " +
                    "a failure", imageIn(s3), 0f, 0f, 0f, 0f)
                assertFrame("[3] band X: y = 0, forever", s3.getChildAt(1),
                    0f, 0f, SECTION_W, BAND_H)

                // [4] THE REFLOW — once, by exactly Hi, with a placeholder present.
                val s4 = root.getChildAt(4) as ViewGroup
                assertFrame("[4] section grew by exactly Hi", s4,
                    0f, INTRINSIC_LOADING_Y, SECTION_W, hi + BAND_H)
                assertFrame("[4] image: the NATURAL size — the decoded fixture's own " +
                    "$wi × $hi pixels read as dp", imageIn(s4), 0f, 0f, wi, hi)
                assertTrue("[4] …and the bytes replaced the placeholder",
                    imageIn(s4).drawable is BitmapDrawable)
                assertFrame("[4] THE REFLOW PROOF: band I moved 0 → Hi, once",
                    s4.getChildAt(1), 0f, hi, SECTION_W, BAND_H)

                // [5] the back row: the page's ONLY moving number, moved by Hi.
                assertEquals("[5] back row at 664 + Hi", BACK_Y_BEFORE + hi,
                    root.getChildAt(5).top / density(), 0.5f)
            }

            // ══ STATE 3: /slow.png RELEASES — the SUCCESS row clears the placeholder ══
            server.releaseSlow()
            awaitTerminated(scenario, ALL_REQUESTS)

            scenario.onActivity { act ->
                assertEquals("the eighth outcome is SUCCESS — the held response was OURS",
                    1, act.mapper.imageResults.count {
                        it.url == ImageFixtureServer.SLOW_URL &&
                            it.outcome == WidgetMapper.ImageOutcome.SUCCESS
                    })
                val root = root(act)
                val s0 = root.getChildAt(0) as ViewGroup
                assertTrue("[0] SUCCESS CLEARED the placeholder: the bytes are the drawable " +
                    "now (letterbox bars show BackgroundColor, never the placeholder)",
                    imageIn(s0).drawable is BitmapDrawable)
                assertFrame("[0] and the frame is byte-for-byte what it was while held — " +
                    "the placeholder never bought or cost a single dp", s0,
                    0f, 0f, SECTION_W, LOADING_H)
                assertFrame("[0] image: still the declared box", imageIn(s0),
                    0f, 0f, DECLARED_W, DECLARED_H)
                assertFrame("[0] band L: still y = 120", s0.getChildAt(1),
                    0f, DECLARED_H, SECTION_W, BAND_H)

                assertEquals("STILL exactly one dispatch — a success dispatches nothing",
                    1, act.mapper.errorDispatchesSent)
                assertEquals("…and the echo never moved again", ECHO_AFTER_ONE, echoText(act))
                assertEquals("nothing is left in flight", 0, act.mapper.inFlightImageCount)
            }
        }
    }

    /** The DEEP_LINK_COMPONENTS "/imagepolish" row, asserted by mounting through it. */
    @Test fun mounting_by_deep_link_resolves_the_imagepolish_route() {
        server.release()
        server.releaseSlow() // nothing held: this mount only proves the route resolves
        val intent = Intent(Intent.ACTION_VIEW, Uri.parse("blazornative://imagepolish"))
            .setClass(ApplicationProvider.getApplicationContext(), MainActivity::class.java)
        ActivityScenario.launch<MainActivity>(intent).use { scenario ->
            assertTrue("BnImagePolishDemo never mounted from the deep link within 60s — the " +
                "DEEP_LINK_COMPONENTS '/imagepolish' row is what this launch asserts",
                pollForDemo(scenario))
            awaitTerminated(scenario, ALL_REQUESTS) // leave no request for teardown to cancel
            scenario.onActivity { act ->
                val root = root(act)
                assertEquals("the six-section page mounted through the ROUTE", 6, root.childCount)
                assertEquals("← Back",
                    ((root.getChildAt(5) as ViewGroup).getChildAt(0) as Button).text.toString())
            }
        }
    }

    /** The back row: the page's only measured leaf, deliberately LAST — by oracle, the
     * [BnImageDemoAndroidTest] rule (no font constant is anyone's to invent). */
    @Test fun the_back_row_is_the_pages_only_measured_leaf() {
        server.release()
        server.releaseSlow()
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val intent = Intent(ctx, MainActivity::class.java)
            .putExtra(MainActivity.EXTRA_COMPONENT, "BnImagePolishDemo")
        ActivityScenario.launch<MainActivity>(intent).use { scenario ->
            assertTrue(pollForDemo(scenario))
            awaitTerminated(scenario, ALL_REQUESTS)
            scenario.onActivity { act ->
                val backSection = root(act).getChildAt(5) as ViewGroup
                val back = backSection.getChildAt(0) as Button
                assertEquals("← Back", back.text.toString())
                assertEquals("the back row is 300dp wide",
                    SECTION_W, backSection.width / density(), 0.5f)
                assertEquals("it declares no height and HUGS the button's MEASURED height",
                    back.height, backSection.height)
                assertOracle("the measured back button", back,
                    availableWidthPx = backSection.width)
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private fun root(act: MainActivity): ViewGroup =
        act.findViewById<FrameLayout>(R.id.widget_root).getChildAt(0) as ViewGroup

    private fun imageIn(section: ViewGroup): ImageView = section.getChildAt(0) as ImageView

    /** [1]'s echo: section 1, child 2 (the fixed-height BnView row), child 0 (BnText's
     * TextView — a `view` parent is a ViewGroup, so the text node materializes its own). */
    private fun echoText(act: MainActivity): String? =
        (((root(act).getChildAt(1) as ViewGroup).getChildAt(2) as? ViewGroup)
            ?.getChildAt(0) as? TextView)?.text?.toString()

    /** The view-state pin: the drawable IS the placeholder, in exactly the razor's color. */
    private fun assertPlaceholder(whenWhat: String, image: ImageView) {
        val d = image.drawable
        assertTrue("the placeholder must be painted $whenWhat — got " +
            "${d?.javaClass?.simpleName}", d is ColorDrawable)
        assertEquals("…in the razor's PlaceholderHex", placeholderColor,
            (d as ColorDrawable).color)
    }

    /**
     * **[2] THE FOUR MODES** — THE assertion: four IDENTICAL layout frames (same x/w/h,
     * y's in fixed 60+20 steps) under four DIFFERENT modes, bands pinned; the per-node
     * `scaleType` pins carry the paint half (the 7.4 finding-4 lesson: assert the wiring
     * where the paint cannot be synthesized). Identical whether the bytes have landed or
     * not — the frames belong to Yoga, and Yoga never heard about the mode.
     */
    private fun assertQuartet(act: MainActivity, bytesLanded: Boolean) {
        val s2 = root(act).getChildAt(2) as ViewGroup
        assertFrame("[2] quartet section at y = 304, hugging 4 × 80", s2,
            0f, QUARTET_Y, SECTION_W, QUARTET_H)
        assertEquals("four images and four bands, alternating", 8, s2.childCount)
        val expected = listOf(
            ImageView.ScaleType.FIT_CENTER,  // contain — the default, spelled out
            ImageView.ScaleType.CENTER_CROP, // cover
            ImageView.ScaleType.FIT_XY,      // stretch
            ImageView.ScaleType.CENTER,      // center
        )
        for (i in 0..3) {
            val img = s2.getChildAt(2 * i) as ImageView
            assertFrame("[2] mode ${expected[i]}: frame #$i is the SAME 120 × 60 box at " +
                "y = ${i * MODE_STEP.toInt()} — the Yoga box never changes with mode",
                img, 0f, i * MODE_STEP, MODE_W, MODE_H)
            assertEquals("[2] …under scaleType ${expected[i]}", expected[i], img.scaleType)
            if (bytesLanded) {
                assertTrue("[2] …with the bytes painted", img.drawable is BitmapDrawable)
            }
            assertFrame("[2] band #$i at y = ${(60 + i * 80)}", s2.getChildAt(2 * i + 1),
                0f, MODE_H + i * MODE_STEP, SECTION_W, BAND_H)
        }
    }

    /**
     * **THE SYNCHRONIZATION GATE** — 6.3's, verbatim, counted to [count] of the page's
     * EIGHT requests (the class KDoc derives the eight and records the razor header's
     * miscount). A timeout FAILS; never a poll on a band.
     */
    private fun awaitTerminated(scenario: ActivityScenario<MainActivity>, count: Int) {
        val deadline = System.currentTimeMillis() + 30_000
        val seen = AtomicReference(0)
        while (System.currentTimeMillis() < deadline) {
            scenario.onActivity { act -> seen.set(act.mapper.imageResults.size) }
            if (seen.get() >= count) {
                InstrumentationRegistry.getInstrumentation().waitForIdleSync()
                return
            }
            Thread.sleep(100)
        }
        throw AssertionError("only ${seen.get()} of $count image requests terminated within " +
            "30s. A timeout here FAILS: most of this page's cases assert that NOTHING " +
            "MOVED, and all of those pass on a device that fetched nothing.")
    }

    private fun pollUntil(
        scenario: ActivityScenario<MainActivity>,
        timeoutMs: Long,
        predicate: (MainActivity) -> Boolean,
    ): Boolean {
        val deadline = System.currentTimeMillis() + timeoutMs
        val ok = AtomicReference(false)
        while (System.currentTimeMillis() < deadline) {
            scenario.onActivity { act -> ok.set(predicate(act)) }
            if (ok.get()) return true
            Thread.sleep(100)
        }
        return false
    }

    /** Polls until the mount frame has been applied AND laid out: six sections under the
     * root, and a root with a computed height. */
    private fun pollForDemo(scenario: ActivityScenario<MainActivity>): Boolean {
        val deadline = System.currentTimeMillis() + 60_000
        val ready = AtomicReference(false)
        while (System.currentTimeMillis() < deadline) {
            scenario.onActivity { act ->
                val root = act.findViewById<FrameLayout>(R.id.widget_root)
                    ?.takeIf { it.childCount > 0 }?.getChildAt(0) as? ViewGroup
                ready.set(root != null && root.childCount == 6 && root.height > 0)
            }
            if (ready.get()) break
            Thread.sleep(250)
        }
        return ready.get()
    }
}
