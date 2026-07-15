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
 * **both** canonical tables from `src/BlazorNative.Components/BnImageDemo.razor`'s file
 * header — *before* the bytes land and *after* — because the difference between them **is
 * the phase**. Same discipline and same pairing as [BnLayoutDemoAndroidTest] and
 * [BnScrollDemoAndroidTest]: **the iOS XCTest (Gate 3) asserts THE SAME NUMBERS.** Yoga
 * computes in density-independent units on both platforms, so every expectation here is in
 * **dp**, read back as `view.left / density`.
 *
 * Both tables are declared in [bnImageDemoBeforeFrames] / [bnImageDemoAfterFrames]
 * (BnDemoFrameTables.kt) — never inline here — and `ShellFrameTableDriftTests` demands the
 * iOS shell's twin declaration be equal to them, in the REQUIRED lane (M6 audit, F2). The
 * AFTER table is parameterised by the DECODED fixture's own `wi`/`hi`, which the drift test
 * compares AS SYMBOLS: it can check that both shells say `hi + 20` without knowing what `hi`
 * is, which is exactly the point — neither shell is allowed to write that number down.
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
        /** BnImageDemo.razor's `SectionWidthDp`. Every OTHER number this page's frames are
         * made of now lives in the canonical tables ([bnImageDemoBeforeFrames] /
         * [bnImageDemoAfterFrames], BnDemoFrameTables.kt), which the iOS shell declares
         * too and `ShellFrameTableDriftTests` pins the two against each other in the
         * REQUIRED lane (M6 audit, F2). This one survives because it is not a frame: it
         * bounds the fixture (`0 < Wi ≤ 300` — a section is 300 wide, so the measure func
         * is called with AT_MOST(300)). */
        const val SECTION_W = 300f
    }

    @Before fun startFixtureServer() {
        ImageFixtureServer.clearCoilCaches()
        server = ImageFixtureServer()
    }

    /**
     * isInitialized: a @Before that THREW (a taken port) must not have its cause masked.
     *
     * **AND THE SERVER'S OWN ERRORS ARE ASSERTED EMPTY** (Gate 2 review, I4). The fixture server
     * swallows `IOException` on its worker threads, and the scoping is right — a broken pipe IS
     * cancellation, seen from the other end of the socket, and uncaught it would take the app
     * process down. But **NOTHING ON THIS PAGE CANCELS ANYTHING**: there is no client here that
     * could drop a connection, so an `IOException` in this class is a REAL SERVER BUG. Unrecorded
     * it produced no signal at all — the test simply timed out 30 seconds later in
     * [awaitAllThreeTerminated] and blamed the synchronization gate. Now the server records them
     * and this class, which cancels nothing, demands the list be empty.
     * (`WidgetMapperImageTest` DOES cancel, expects entries, and asserts nothing here.)
     */
    @After fun stopFixtureServer() {
        if (!::server.isInitialized) return
        val errors = server.errors
        server.close()
        assertEquals("the fixture server hit an IOException, and NOTHING ON THIS PAGE CANCELS " +
            "ANYTHING — so this is a real server bug, not a dropped client. Without this " +
            "assertion its only symptom is the synchronization gate timing out and taking the " +
            "blame for it.", emptyList<String>(), errors)
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
     * BEFORE (`BnImageDemo.razor` §"THE FRAME TABLE, BEFORE THE BYTES LAND"):
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

                // THE CANONICAL BEFORE TABLE — BnDemoFrameTables.kt, pinned against the iOS
                // shell's twin in the REQUIRED lane (M6 audit, F2).
                val b = bnImageDemoBeforeFrames

                val host = act.findViewById<FrameLayout>(R.id.widget_root)
                val root = host.getChildAt(0) as ViewGroup
                assertEquals("four sections: fixed, intrinsic, failing, back", 4, root.childCount)

                // [0] FIXED — first on purpose: nothing above it can ever move it, so its
                // "did not move" is a fact about the IMAGE and not about the page.
                val fixedSection = root.getChildAt(0) as ViewGroup
                assertFrame(b, "[0] fixed section", fixedSection, "HUGS 120 + 20")
                assertFrame(b, "[0] fixed image", imageIn(fixedSection),
                    "Width AND Height declared")
                assertFrame(b, "[0] band F", fixedSection.getChildAt(1), "BEFORE: y = 120")
                fixedImageFrameBefore.set(frameOf(imageIn(fixedSection)))

                // [1] INTRINSIC — 0 × 0. Not "small": ZERO.
                val intrinsicSection = root.getChildAt(1) as ViewGroup
                assertFrame(b, "[1] intrinsic section", intrinsicSection, "HUGS 0 + 20")
                assertFrame(b, "[1] intrinsic image", imageIn(intrinsicSection),
                    "BEFORE: a measured leaf with no bytes measures 0 × 0")
                assertFrame(b, "[1] band I", intrinsicSection.getChildAt(1),
                    "BEFORE: y = 0 — THE REFLOW HAS NOT HAPPENED")
                assertNull("[1] nothing painted yet", imageIn(intrinsicSection).drawable)

                // [2] FAILING — structurally identical to [1]; only the URL differs.
                val failingSection = root.getChildAt(2) as ViewGroup
                assertFrame(b, "[2] failing section", failingSection, "HUGS 0 + 20")
                assertFrame(b, "[2] failing image", imageIn(failingSection), "BEFORE: 0 × 0")
                assertFrame(b, "[2] band X", failingSection.getChildAt(1),
                    "BEFORE: y = 0 in its parent")

                // [3] the back row — the page's only measured leaf, deliberately LAST. Its
                // HEIGHT is MEASURED in the table (a font metric is nobody's to invent); its
                // y = 180 is not.
                val backSection = root.getChildAt(3) as ViewGroup
                assertFrame(b, "[3] back section", backSection)

                assertRootFrame(act, root, backSection)
            }

            // ══ THE GATE OPENS ═══════════════════════════════════════════════════
            server.release()
            awaitAllThreeTerminated(scenario)

            scenario.onActivity { act ->
                // THE OUTCOMES, against the URLs the WIRE carried — which is also the drift
                // pin on BnImageDemo.razor's three `internal const` sources (a device-side test
                // cannot read a .razor file; it can read what the renderer put on the wire).
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
                // THE CANONICAL AFTER TABLE, parameterised by the DECODED fixture's own pixel
                // size — never a constant this file invents. Its iOS twin declares the same
                // rows with the same `wi`/`hi` symbols, and the drift test compares them AS
                // SYMBOLS: `hi + 20` on both shells is an equality it can check without
                // knowing the number.
                val a = bnImageDemoAfterFrames(wi, hi)
                val root = act.findViewById<FrameLayout>(R.id.widget_root).getChildAt(0) as ViewGroup

                // [0] THE NO-REFLOW PROOF — asserted as an IDENTITY between two frames of the
                // same node, not as a number. Both axes are definite, so Yoga never called
                // its measure func at all and the fixture's own size is nowhere in this frame.
                val fixedSection = root.getChildAt(0) as ViewGroup
                assertFrame(a, "[0] fixed section", fixedSection, "AFTER: UNCHANGED")
                assertEquals("[0] THE NO-REFLOW PROOF: the fixed image's frame is IDENTICAL, " +
                    "number for number, to the one it had before its bytes landed",
                    fixedImageFrameBefore.get(), frameOf(imageIn(fixedSection)))
                assertFrame(a, "[0] band F", fixedSection.getChildAt(1),
                    "AFTER: y = 120. IT DID NOT MOVE")
                assertNotNull("[0] …and its bytes DID land — which is what makes 'it did not " +
                    "move' mean something", imageIn(fixedSection).drawable)

                // [1] THE REFLOW.
                val intrinsicSection = root.getChildAt(1) as ViewGroup
                val intrinsicImage = imageIn(intrinsicSection)
                assertFrame(a, "[1] intrinsic image", intrinsicImage,
                    "AFTER: its NATURAL size — the DECODED FIXTURE's own ${intrinsic.width} × " +
                        "${intrinsic.height} PIXELS, read as dp. One file pixel is one dp/pt, " +
                        "which is the only reading under which Android and iOS compute the same " +
                        "frame (UIImage(data:).size is already in points)")
                assertTrue("[1] POSITIVELY: Wi > 0 AND Hi > 0. Two of this page's three cases " +
                    "assert 'nothing moved', and a suite of negatives is one that a TOTAL " +
                    "FAILURE satisfies", intrinsicImage.width > 0 && intrinsicImage.height > 0)
                assertFrame(a, "[1] band I", intrinsicSection.getChildAt(1),
                    "THE REFLOW PROOF: band I moved from y = 0 to y = Hi. The image's own frame " +
                        "could be faked by a shell that painted and never re-solved; the BAND's " +
                        "y could not")
                assertFrame(a, "[1] intrinsic section", intrinsicSection,
                    "…and the section grew by exactly Hi")

                // [2] THE FAILURE RESERVED NOTHING — two facts at once, and the frames are
                // parent-relative so both are visible: the SECTION slid down by Hi (because
                // [1] grew above it) while the band INSIDE it stayed at y = 0.
                val failingSection = root.getChildAt(2) as ViewGroup
                assertFrame(a, "[2] failing section", failingSection,
                    "moved down by Hi — [1]'s reflow, propagating downward")
                assertFrame(a, "[2] failing image", imageIn(failingSection),
                    "…but ITS image stayed 0 × 0")
                assertFrame(a, "[2] band X", failingSection.getChildAt(1),
                    "…so band X is still at y = 0 IN ITS PARENT: THE FAILURE RESERVED NOTHING")
                assertNull("[2] and nothing was painted", imageIn(failingSection).drawable)

                // [3]
                val backSection = root.getChildAt(3) as ViewGroup
                assertFrame(a, "[3] back section", backSection, "moved down by Hi too")

                assertRootFrame(act, root, backSection)
                assertEquals("nothing is left in flight", 0, act.mapper.inFlightImageCount)
            }
        }
    }

    /**
     * **THE SECOND MOUNT, WITH A WARM COIL CACHE — THE PATH EVERY OTHER TEST CLEARS AWAY**
     * (Gate 2 review, C2).
     *
     * Coil 2 dispatches on `Dispatchers.Main.immediate`, and the shell issues its request from
     * `UpdateProp("src", …)`, which runs **on the main thread inside `applyBatch`**. So on a
     * **memory-cache hit** the whole request — decode lookup, `onSuccess`, set-image, `markDirty`,
     * re-solve — runs **to completion inside the `enqueue` call**, before it returns. That is the
     * ordinary case on the SECOND mount of any page the process has already fetched (Coil's cache
     * is process-wide), and it is exactly what **`@Before`'s `clearCoilCaches()` hides** — every
     * other test in this repo mounts against a cold cache, so this path was **entirely
     * unexercised**.
     *
     * Two bugs lived in it, and both are two-shell landmines (**Kingfisher's `setImage` calls its
     * completionHandler synchronously on a memory hit too** — Gate 3 inherits this verbatim):
     *
     *  1. **The re-solve clobbered the re-entrancy guard.** `resolveLayout`'s `finally` set
     *     `applyingBatch = false` **for the rest of the batch** — the guard is a plain boolean and
     *     this is its first re-entrant caller — so every subsequent `setText`/`value`/focus change
     *     in that batch would dispatch back into .NET: the change → re-render → setText loop the
     *     3.2/4.2 guard exists to prevent. And Yoga re-solved against a HALF-APPLIED tree, then
     *     again at `CommitFrame` — **two reflows, where the contract says ONE.**
     *  2. **The completed request was recorded as in-flight, forever.** The completion's own
     *     bookkeeping ran *before* the map write, so it cleared nothing; the already-disposed
     *     handle was then stored and never removed. `inFlightImageCount` — an invariant **three**
     *     tests assert — never returned to 0.
     *
     * **This test reddens on (2) under the old code** (two stale entries, one per cached image),
     * and it is the only thing standing between Gate 3 and the same two bugs. The frames are
     * asserted too: the AFTER table must be identical whether the bytes came from the network or
     * from the cache — that is what "one reflow, never two" means when the reflow is synchronous.
     */
    @Test fun the_second_mount_with_a_WARM_cache_completes_inside_applyBatch() {
        val intrinsic = server.decoded(server.intrinsicPng)
        val hi = intrinsic.height.toFloat()
        val wi = intrinsic.width.toFloat()

        server.release() // both mounts fetch freely; there is no BEFORE table to protect here

        // ── MOUNT 1: cold cache (the @Before cleared it). This is what WARMS it. ──
        withDemo { act ->
            assertEquals("mount 1 fetched all three over real HTTP", 3, act.mapper.imageResults.size)
            assertEquals("…and left nothing in flight", 0, act.mapper.inFlightImageCount)
        }

        // ── MOUNT 2: NO clearCoilCaches(). fixed.png and intrinsic.png are MEMORY-CACHE
        //    HITS, so their completions run INSIDE applyBatch, synchronously, before
        //    `enqueue` even returns. missing.png still goes to the wire (a 404 is not
        //    cached), which is why the synchronization gate below still has work to do.
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val intent = Intent(ctx, MainActivity::class.java)
            .putExtra(MainActivity.EXTRA_COMPONENT, "BnImageDemo")

        ActivityScenario.launch<MainActivity>(intent).use { scenario ->
            assertTrue("BnImageDemo never rendered a laid-out tree within 60s",
                pollForDemo(scenario))
            awaitAllThreeTerminated(scenario)

            scenario.onActivity { act ->
                // THE SAME AFTER TABLE the network mount asserts — read a second time, not
                // transcribed a second time. "One reflow, never two, whether the bytes arrive
                // synchronously or not" is now a statement about one declaration.
                val a = bnImageDemoAfterFrames(wi, hi)
                val root = act.findViewById<FrameLayout>(R.id.widget_root).getChildAt(0) as ViewGroup

                // THE INVARIANT THAT BROKE. A synchronously-completed request that was recorded
                // anyway sits in the in-flight map forever: the completion's clear ran BEFORE the
                // entry existed. Two cached images ⇒ this was 2.
                assertEquals("NOTHING IS LEFT IN FLIGHT after a WARM-cache mount. A memory hit " +
                    "completes INSIDE enqueue(), so its bookkeeping runs BEFORE the shell has " +
                    "anything to record — and an unconditional record leaks the handle for the " +
                    "life of the mapper. On iOS that stale entry is a request nothing can cancel, " +
                    "whose completion runs against a freed YGNodeRef.",
                    0, act.mapper.inFlightImageCount)

                // …AND THE FRAMES ARE THE SAME ONES. A synchronous completion inside the batch
                // must produce the SAME AFTER table as an asynchronous one: the natural size and
                // the markDirty land mid-batch, and the batch's own CommitFrame is the ONE re-solve
                // that applies them.
                val fixedSection = root.getChildAt(0) as ViewGroup
                assertFrame(a, "[0] fixed section", fixedSection, "from cache: UNCHANGED")
                assertFrame(a, "[0] fixed image", imageIn(fixedSection),
                    "from cache: its DECLARED size, still")
                assertNotNull("[0] …and the cached bytes were painted", imageIn(fixedSection).drawable)

                val intrinsicSection = root.getChildAt(1) as ViewGroup
                assertFrame(a, "[1] intrinsic image", imageIn(intrinsicSection),
                    "from cache: its NATURAL size — the same $wi × $hi it measured from the " +
                        "network. One reflow, never two, whether the bytes arrive synchronously " +
                        "or not")
                assertFrame(a, "[1] band I", intrinsicSection.getChildAt(1),
                    "THE REFLOW STILL HAPPENED, from inside the batch: band I is at y = Hi")
                assertFrame(a, "[1] intrinsic section", intrinsicSection,
                    "…and the section grew by exactly Hi")

                // [2] still fails — a 404 is not cached, so this one really did go to the wire,
                // which is what keeps the synchronization gate above honest on this mount.
                val failingSection = root.getChildAt(2) as ViewGroup
                assertFrame(a, "[2] failing image", imageIn(failingSection),
                    "the failure still reserves nothing")
                assertFrame(a, "[2] band X", failingSection.getChildAt(1),
                    "…and its band is still at y = 0 in its parent")

                val backSection = root.getChildAt(3) as ViewGroup
                assertFrame(a, "[3] back section", backSection,
                    "the back row is where the reflow put it")
                assertRootFrame(act, root, backSection)
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
