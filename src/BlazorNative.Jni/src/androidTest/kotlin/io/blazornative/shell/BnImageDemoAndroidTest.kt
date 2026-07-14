package io.blazornative.shell

import android.content.Intent
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.FrameLayout
import android.widget.ImageView
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import java.util.concurrent.atomic.AtomicReference

/**
 * Phase 6.3 Gate 2 Task 2.4 — **THE IMAGE DEMO, ON THE DEVICE** (M6 DoD #5).
 *
 * Mounts `BnImageDemo` (the `/image` page) through the real NativeAOT boot and asserts
 * **both** canonical tables from `src/BlazorNative.Components/BnImageDemo.cs`'s file
 * header — *before* the bytes land and *after* — because the difference between them **is
 * the phase**. Same discipline and same pairing as [BnLayoutDemoAndroidTest] and
 * [BnScrollDemoAndroidTest]: **the iOS XCTest (Gate 3) asserts THE SAME NUMBERS.** Yoga
 * computes in density-independent units on both platforms, so every expectation here is in
 * **dp**, read back as `view.left / density`.
 *
 * ── THE TRAP THIS FILE EXISTS TO NOT FALL INTO ──────────────────────────────────────
 *
 * **Two of the three cases assert that NOTHING MOVED** — [0]'s frame is definite and [2]'s
 * failure reserves nothing — and the wire carries **no completion signal** by design (no
 * `OnLoad`, no `OnError`: each would change measurement). So:
 *
 *  - assert the AFTER table straight after mount and **[0] and [2] pass having proven
 *    nothing**; only [1] reddens.
 *  - and if cleartext HTTP were blocked, all three fetches fail — **a blocked load is
 *    INDISTINGUISHABLE from the 404 that [2] expects** — so [0] still passes, [2] still
 *    passes, and only [1] reddens, reading as a reflow bug. **A green suite is achievable
 *    on a device that loaded nothing.**
 *
 * Three things defend against that, and all three are load-bearing:
 *
 *  1. **THE SYNCHRONIZATION GATE.** The AFTER table is read only once **all three**
 *     requests have TERMINATED — awaited on Coil's own per-node `ImageRequest.Listener`
 *     ([WidgetMapper.imageResults]), counted to three, with a timeout that **FAILS**. Never
 *     on band I's movement: that witnesses only case [1] and says nothing about whether
 *     [0]'s or [2]'s request ever finished.
 *  2. **THE OUTCOMES ARE ASSERTED, NOT JUST THE COUNT** — two `SUCCESS` and one `ERROR`,
 *     against the URLs the WIRE carried. A blocked-cleartext device produces three
 *     `ERROR`s and reddens *here*, by name.
 *  3. **POSITIVE ASSERTIONS**: `Wi > 0`, `Hi > 0`, and the intrinsic image's frame equal to
 *     the **decoded fixture's own pixel count** — so "band F did not move" means "the bytes
 *     landed and did not move it" rather than "no bytes landed".
 *
 * And the BEFORE table is only observable at all because the fixture server **holds every
 * response** until the test has read it ([ImageFixtureServer]) — otherwise a loopback fetch
 * wins the race against the test's first look and the "before" table is asserted on a page
 * that has already reflowed.
 */
@RunWith(AndroidJUnit4::class)
class BnImageDemoAndroidTest {

    private lateinit var server: ImageFixtureServer

    private companion object {
        // BnImageDemo.cs's constants (SectionWidthDp, FixedWidthDp, FixedHeightDp,
        // SiblingHeightDp) and the four offsets it COMPUTES from them. Derived here too, not
        // transcribed: a changed band height must move both sides at once.
        const val SECTION_W = 300f
        const val FIXED_W = 200f
        const val FIXED_H = 120f
        const val BAND_H = 20f
        const val FIXED_SECTION_H = FIXED_H + BAND_H          // 140
        const val INTRINSIC_SECTION_Y = FIXED_SECTION_H       // 140
        const val FAILING_SECTION_Y = INTRINSIC_SECTION_Y + BAND_H // 160
        const val BACK_SECTION_Y = FAILING_SECTION_Y + BAND_H      // 180
    }

    @Before fun startFixtureServer() {
        ImageFixtureServer.clearCoilCaches()
        server = ImageFixtureServer()
    }

    // isInitialized: a @Before that THREW (a taken port) must not have its cause masked.
    @After fun stopFixtureServer() {
        if (::server.isInitialized) server.close()
    }

    /**
     * **CLEARTEXT, VERIFIED RATHER THAN ASSUMED** — and it is verified because its failure
     * mode is the silent one above.
     *
     * `targetSdk 34` blocks cleartext HTTP outright (this repo ate that in Phase 3.1).
     * Gate 2 is covered **by inheritance**: `src/debug/res/xml/network_security_config.xml`
     * permits cleartext to `127.0.0.1` / `localhost`, and instrumented tests run the DEBUG
     * build. This test is what turns that sentence into a checked fact — it pulls a fixture
     * over the very loopback Coil will use, and a block surfaces here as the `IOException`
     * it is, naming itself, instead of as three "failed" images that two of the demo's
     * three assertions would happily certify.
     *
     * **THE RELEASE COROLLARY, and it is not a bug:** `androidMain`'s config permits no
     * cleartext, so a **release** build of the demo shows three FAILED images on `/image`.
     * That is correct behaviour for a demo whose fixtures are loopback HTTP. The fix is
     * never to weaken the release config — it is to bundle the fixture as an asset.
     */
    @Test fun cleartext_loopback_is_permitted_in_the_debug_build() {
        server.release()

        val (okStatus, okBody) = server.fetch(ImageFixtureServer.FIXED_URL)
        assertEquals("cleartext HTTP to 127.0.0.1 must be PERMITTED in the debug build — if " +
            "this throws or 000s, every image on /image fails and TWO OF THE THREE DEMO " +
            "ASSERTIONS STILL PASS", 200, okStatus)
        val decoded = server.decoded(okBody)
        assertEquals("…and the bytes are OURS: the fixture's own width",
            ImageFixtureServer.FIXED_W, decoded.width)
        assertEquals("…and its own height", ImageFixtureServer.FIXED_H, decoded.height)

        val (missingStatus, _) = server.fetch(ImageFixtureServer.MISSING_URL)
        assertEquals("…and the failing case is a REAL 404 from a REAL server — not a dropped " +
            "connection, and (the point) not a blocked request wearing a 404's clothes",
            404, missingStatus)
    }

    /**
     * **THE TWO FRAME TABLES**, and the difference between them is the phase.
     *
     * BEFORE (`BnImageDemo.cs` §"THE FRAME TABLE, BEFORE THE BYTES LAND"):
     * ```
     * root BnColumn        (0, 0, Whost, 180+Hb)   width FILLS the host; height HUGS
     *  ├─ [0] fixed sect.  (0,   0, 300, 140)
     *  │    ├─ image       (0,   0, 200, 120)      declared → NEVER measured
     *  │    └─ band F      (0, 120, 300,  20)
     *  ├─ [1] intr. sect.  (0, 140, 300,  20)
     *  │    ├─ image       (0,   0,   0,   0)      no bytes → 0 × 0
     *  │    └─ band I      (0,   0, 300,  20)      ← y = 0
     *  ├─ [2] fail. sect.  (0, 160, 300,  20)
     *  │    ├─ image       (0,   0,   0,   0)
     *  │    └─ band X      (0,   0, 300,  20)
     *  └─ [3] back sect.   (0, 180, 300,  Hb)
     * ```
     * AFTER — the intrinsic image's bytes arrive, the shell marks its Yoga node dirty and
     * re-solves; ONE reflow, never two:
     * ```
     * root BnColumn        (0, 0, Whost, 180+Hi+Hb)  the height GREW by Hi
     *  ├─ [0] fixed sect.  (0,      0, 300, 140)     UNCHANGED ─┐ THE NO-REFLOW PROOF:
     *  │    ├─ image       (0,      0, 200, 120)      UNCHANGED │ asserted as an IDENTITY
     *  │    └─ band F      (0,    120, 300,  20)      UNCHANGED ┘ between two frames of
     *  ├─ [1] intr. sect.  (0,    140, 300, Hi+20)                the SAME node
     *  │    ├─ image       (0,      0,  Wi,  Hi)     ← the NATURAL size
     *  │    └─ band I      (0,     Hi, 300,  20)     ← y: 0 → Hi. THE REFLOW PROOF
     *  ├─ [2] fail. sect.  (0, 160+Hi, 300,  20)     moved down by [1]'s reflow…
     *  │    ├─ image       (0,      0,   0,   0)     …but ITS image stayed 0 × 0…
     *  │    └─ band X      (0,      0, 300,  20)     …so y = 0 IN ITS PARENT: THE FAILURE
     *  └─ [3] back sect.   (0, 180+Hi, 300,  Hb)        RESERVED NOTHING
     * ```
     * `Wi × Hi` is the fixture's natural pixel size, read off the **decoded fixture** —
     * never a constant this file invents.
     */
    @Test fun the_frame_tables_BEFORE_and_AFTER_the_bytes() {
        // ── THE FIXTURE CONTRACT, on the DECODED fixtures, BEFORE ANY FRAME ───────
        // An unasserted fixture constraint is a coincidence waiting to happen — and these
        // double as the probe that the bytes came from OUR server.
        val intrinsic = server.decoded(server.intrinsicPng)
        val fixed = server.decoded(server.fixedPng)
        val wi = intrinsic.width.toFloat()
        val hi = intrinsic.height.toFloat()
        assertTrue("0 < Wi ≤ 300: a section is 300 wide, so the measure func is called with " +
            "AT_MOST(300) — a wider fixture asks a clamping question this phase deliberately " +
            "does not answer (no ContentMode). Got $wi", wi > 0f && wi <= SECTION_W)
        assertTrue("Hi > 0, comfortably — HI *IS* THE REFLOW. Got $hi", hi > 0f)
        assertTrue("(Wfixed, Hfixed) ≠ (200, 120): otherwise '[0] measures 200 × 120' is a " +
            "COINCIDENCE, not a proof that a declared size short-circuits measurement. Got " +
            "${fixed.width} × ${fixed.height}", fixed.width != 200 || fixed.height != 120)

        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val intent = Intent(ctx, MainActivity::class.java)
            .putExtra(MainActivity.EXTRA_COMPONENT, "BnImageDemo")

        ActivityScenario.launch<MainActivity>(intent).use { scenario ->
            assertTrue("BnImageDemo never rendered a laid-out tree within 60s",
                pollForDemo(scenario))

            // ══ BEFORE THE BYTES ═════════════════════════════════════════════════
            val fixedImageFrameBefore = AtomicReference<List<Int>>()
            scenario.onActivity { act ->
                assertEquals("no request has terminated — the fixture server is HOLDING every " +
                    "response, which is the only thing that makes this 'before' honest. " +
                    "(Coil's caches were cleared, so all three go to the wire.)",
                    emptyList<WidgetMapper.ImageResult>(), act.mapper.imageResults)

                val d = act.resources.displayMetrics.density
                val host = act.findViewById<FrameLayout>(R.id.widget_root)
                val root = host.getChildAt(0) as ViewGroup
                assertEquals("four sections: fixed, intrinsic, failing, back", 4, root.childCount)

                // [0] FIXED — first on purpose: nothing above it can ever move it, so its
                // "did not move" is a fact about the IMAGE and not about the page.
                val fixedSection = root.getChildAt(0) as ViewGroup
                assertFrame("[0] the fixed section HUGS 120 + 20",
                    fixedSection, 0f, 0f, SECTION_W, FIXED_SECTION_H)
                assertFrame("[0] the fixed image: Width AND Height declared",
                    imageIn(fixedSection), 0f, 0f, FIXED_W, FIXED_H)
                assertFrame("[0] band F, BEFORE: y = 120",
                    fixedSection.getChildAt(1), 0f, FIXED_H, SECTION_W, BAND_H)
                fixedImageFrameBefore.set(frameOf(imageIn(fixedSection)))

                // [1] INTRINSIC — 0 × 0. Not "small": ZERO.
                val intrinsicSection = root.getChildAt(1) as ViewGroup
                assertFrame("[1] the intrinsic section HUGS 0 + 20",
                    intrinsicSection, 0f, INTRINSIC_SECTION_Y, SECTION_W, BAND_H)
                assertFrame("[1] the intrinsic image, BEFORE: a measured leaf with no bytes " +
                    "measures 0 × 0", imageIn(intrinsicSection), 0f, 0f, 0f, 0f)
                assertFrame("[1] band I, BEFORE: y = 0 — THE REFLOW HAS NOT HAPPENED",
                    intrinsicSection.getChildAt(1), 0f, 0f, SECTION_W, BAND_H)
                assertNull("[1] nothing painted yet", imageIn(intrinsicSection).drawable)

                // [2] FAILING — structurally identical to [1]; only the URL differs.
                val failingSection = root.getChildAt(2) as ViewGroup
                assertFrame("[2] the failing section HUGS 0 + 20",
                    failingSection, 0f, FAILING_SECTION_Y, SECTION_W, BAND_H)
                assertFrame("[2] the failing image, BEFORE: 0 × 0",
                    imageIn(failingSection), 0f, 0f, 0f, 0f)
                assertFrame("[2] band X, BEFORE: y = 0 in its parent",
                    failingSection.getChildAt(1), 0f, 0f, SECTION_W, BAND_H)

                // [3] the back row — the page's only measured leaf, deliberately LAST.
                val backSection = root.getChildAt(3) as ViewGroup
                assertEquals("[3] the back row starts at y = 180",
                    BACK_SECTION_Y, backSection.top / d, 0.5f)

                assertRootFrame(act, root, backSection)
            }

            // ══ THE GATE OPENS ═══════════════════════════════════════════════════
            server.release()
            awaitAllThreeTerminated(scenario)

            scenario.onActivity { act ->
                // THE OUTCOMES, against the URLs the WIRE carried — which is also the drift
                // pin on BnImageDemo.cs's three `internal const` sources (a device-side test
                // cannot read a .cs file; it can read what the renderer put on the wire).
                // A blocked-cleartext device produces three ERRORs and reddens HERE, by name,
                // instead of quietly passing two of three frame assertions.
                assertEquals("[0] and [1] SUCCEEDED and [2] genuinely 404'd — all three from " +
                    "OUR loopback fixture server, and not one of them CANCELLED (nothing on " +
                    "this page cancels anything: a cancel here is a setup failure)",
                    setOf(
                        ImageFixtureServer.FIXED_URL to WidgetMapper.ImageOutcome.SUCCESS,
                        ImageFixtureServer.INTRINSIC_URL to WidgetMapper.ImageOutcome.SUCCESS,
                        ImageFixtureServer.MISSING_URL to WidgetMapper.ImageOutcome.ERROR,
                    ),
                    act.mapper.imageResults.map { it.url to it.outcome }.toSet())
            }

            // ══ AFTER THE BYTES ══════════════════════════════════════════════════
            scenario.onActivity { act ->
                val d = act.resources.displayMetrics.density
                val root = act.findViewById<FrameLayout>(R.id.widget_root).getChildAt(0) as ViewGroup

                // [0] THE NO-REFLOW PROOF — asserted as an IDENTITY between two frames of the
                // same node, not as a number. Both axes are definite, so Yoga never called
                // its measure func at all and the fixture's own size is nowhere in this frame.
                val fixedSection = root.getChildAt(0) as ViewGroup
                assertFrame("[0] the fixed section, AFTER: UNCHANGED",
                    fixedSection, 0f, 0f, SECTION_W, FIXED_SECTION_H)
                assertEquals("[0] THE NO-REFLOW PROOF: the fixed image's frame is IDENTICAL, " +
                    "number for number, to the one it had before its bytes landed",
                    fixedImageFrameBefore.get(), frameOf(imageIn(fixedSection)))
                assertFrame("[0] band F, AFTER: y = 120. IT DID NOT MOVE",
                    fixedSection.getChildAt(1), 0f, FIXED_H, SECTION_W, BAND_H)
                assertNotNull("[0] …and its bytes DID land — which is what makes 'it did not " +
                    "move' mean something", imageIn(fixedSection).drawable)

                // [1] THE REFLOW.
                val intrinsicSection = root.getChildAt(1) as ViewGroup
                val intrinsicImage = imageIn(intrinsicSection)
                assertFrame("[1] the intrinsic image, AFTER: its NATURAL size — the DECODED " +
                    "FIXTURE's own ${intrinsic.width} × ${intrinsic.height} PIXELS, read as dp. " +
                    "One file pixel is one dp/pt, which is the only reading under which Android " +
                    "and iOS compute the same frame (UIImage(data:).size is already in points)",
                    intrinsicImage, 0f, 0f, wi, hi)
                assertTrue("[1] POSITIVELY: Wi > 0 AND Hi > 0. Two of this page's three cases " +
                    "assert 'nothing moved', and a suite of negatives is one that a TOTAL " +
                    "FAILURE satisfies", intrinsicImage.width > 0 && intrinsicImage.height > 0)
                assertFrame("[1] THE REFLOW PROOF: band I moved from y = 0 to y = Hi. The " +
                    "image's own frame could be faked by a shell that painted and never " +
                    "re-solved; the BAND's y could not",
                    intrinsicSection.getChildAt(1), 0f, hi, SECTION_W, BAND_H)
                assertFrame("[1] …and the section grew by exactly Hi",
                    intrinsicSection, 0f, INTRINSIC_SECTION_Y, SECTION_W, hi + BAND_H)

                // [2] THE FAILURE RESERVED NOTHING — two facts at once, and the frames are
                // parent-relative so both are visible: the SECTION slid down by Hi (because
                // [1] grew above it) while the band INSIDE it stayed at y = 0.
                val failingSection = root.getChildAt(2) as ViewGroup
                assertFrame("[2] the failing section moved down by Hi — [1]'s reflow, " +
                    "propagating downward", failingSection,
                    0f, FAILING_SECTION_Y + hi, SECTION_W, BAND_H)
                assertFrame("[2] …but ITS image stayed 0 × 0", imageIn(failingSection),
                    0f, 0f, 0f, 0f)
                assertFrame("[2] …so band X is still at y = 0 IN ITS PARENT: THE FAILURE " +
                    "RESERVED NOTHING", failingSection.getChildAt(1), 0f, 0f, SECTION_W, BAND_H)
                assertNull("[2] and nothing was painted", imageIn(failingSection).drawable)

                // [3]
                val backSection = root.getChildAt(3) as ViewGroup
                assertEquals("[3] the back row moved down by Hi too",
                    BACK_SECTION_Y + hi, backSection.top / d, 0.5f)

                assertRootFrame(act, root, backSection)
                assertEquals("nothing is left in flight", 0, act.mapper.inFlightImageCount)
            }
        }
    }

    /**
     * The back row: the page's only measured leaf, deliberately LAST so a font-dependent
     * height cannot shift the frames the parity assertion is built on. Asserted by ORACLE —
     * no font constant is anyone's to invent ([BnLayoutDemoAndroidTest]'s rule).
     */
    @Test fun the_back_row_is_the_pages_only_measured_leaf() {
        server.release()
        withDemo { act ->
            val backSection = (act.findViewById<FrameLayout>(R.id.widget_root)
                .getChildAt(0) as ViewGroup).getChildAt(3) as ViewGroup
            val back = backSection.getChildAt(0) as Button
            assertEquals("← Back", back.text.toString())
            assertEquals("the back row is 300dp wide",
                SECTION_W, backSection.width / act.resources.displayMetrics.density, 0.5f)
            assertEquals("it declares no height and HUGS the button's MEASURED height",
                back.height, backSection.height)
            assertOracle("the measured back button", back, availableWidthPx = backSection.width)
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /**
     * **THE ROOT'S OWN FRAME — it is ASYMMETRIC, and both halves are asserted.**
     *
     * The wire root (`BnImageDemo`'s `BnColumn`) is NOT the shell's Yoga root: the shell owns
     * a SYNTHETIC host root sized to the host view, and the wire root is its only child. That
     * host root is a column with Yoga's default `alignItems: stretch`, so the wire root
     * **FILLS the host's WIDTH** (it is NOT 300 — that is each SECTION's width) and **HUGS in
     * HEIGHT**. Asserting that it does *not* fill the height is the 6.1 host-root pin: it
     * catches a host that re-lays out top-level nodes behind Yoga's back.
     * [BnScrollDemoAndroidTest]'s two root assertions are the template.
     */
    private fun assertRootFrame(act: MainActivity, root: ViewGroup, backSection: ViewGroup) {
        val host = act.findViewById<FrameLayout>(R.id.widget_root)
        assertEquals("the root column FILLS THE HOST'S WIDTH (default alignItems: stretch on " +
            "the synthetic host root) — it is not 300, and it is not the sections' union",
            host.width, root.width)
        assertEquals("…and HUGS in HEIGHT: it ends where the back row ends. The pin that " +
            "catches a host root re-laying out top-level nodes behind Yoga's back",
            backSection.bottom, root.height)
        assertTrue("…so it does NOT fill the host's height", root.height < host.height)
    }

    private fun imageIn(section: ViewGroup): ImageView = section.getChildAt(0) as ImageView

    private fun frameOf(v: View): List<Int> = listOf(v.left, v.top, v.right, v.bottom)

    /**
     * **THE SYNCHRONIZATION GATE** (6.3 non-negotiable #6). Waits for **all three** requests
     * to reach a TERMINAL state — Coil's own per-node `ImageRequest.Listener` verdict —
     * counted to three, with a timeout that **FAILS the test** rather than proceeding.
     *
     * **NOT a poll on band I's movement.** That would witness only case [1]: it cannot tell
     * you [0]'s request finished (so [0]'s "unchanged" would be unproven) and it cannot tell
     * you [2]'s finished (so [2]'s "reserved nothing" might just be a request still in
     * flight).
     */
    private fun awaitAllThreeTerminated(scenario: ActivityScenario<MainActivity>) {
        val deadline = System.currentTimeMillis() + 30_000
        val seen = AtomicReference(0)
        while (System.currentTimeMillis() < deadline) {
            scenario.onActivity { act -> seen.set(act.mapper.imageResults.size) }
            if (seen.get() >= 3) {
                InstrumentationRegistry.getInstrumentation().waitForIdleSync()
                return
            }
            Thread.sleep(100)
        }
        throw AssertionError("only ${seen.get()} of 3 image requests terminated within 30s. A " +
            "timeout here FAILS: the AFTER table may only be asserted once ALL THREE requests " +
            "have ended, because two of the three cases assert that NOTHING MOVED and both " +
            "pass on a device that fetched nothing.")
    }

    private fun withDemo(block: (MainActivity) -> Unit) {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val intent = Intent(ctx, MainActivity::class.java)
            .putExtra(MainActivity.EXTRA_COMPONENT, "BnImageDemo")
        ActivityScenario.launch<MainActivity>(intent).use { scenario ->
            assertTrue("BnImageDemo never rendered a laid-out tree within 60s",
                pollForDemo(scenario))
            awaitAllThreeTerminated(scenario)
            scenario.onActivity(block)
        }
    }

    /** Polls until the mount frame has been applied AND laid out: four sections under the
     * root, and a root with a computed height. */
    private fun pollForDemo(scenario: ActivityScenario<MainActivity>): Boolean {
        val deadline = System.currentTimeMillis() + 60_000
        val ready = AtomicReference(false)
        while (System.currentTimeMillis() < deadline) {
            scenario.onActivity { act ->
                val root = act.findViewById<FrameLayout>(R.id.widget_root)
                    ?.takeIf { it.childCount > 0 }?.getChildAt(0) as? ViewGroup
                ready.set(root != null && root.childCount == 4 && root.height > 0)
            }
            if (ready.get()) break
            Thread.sleep(250)
        }
        return ready.get()
    }
}
