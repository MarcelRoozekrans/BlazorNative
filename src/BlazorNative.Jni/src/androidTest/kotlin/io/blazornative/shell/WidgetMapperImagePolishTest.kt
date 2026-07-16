package io.blazornative.shell

import android.graphics.Color
import android.graphics.drawable.BitmapDrawable
import android.graphics.drawable.ColorDrawable
import android.view.ViewGroup
import android.widget.ImageView
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import io.blazornative.jni.RenderPatch
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

/**
 * Phase 7.5 Gate 2 — **THE THREE POLISH FEATURES, AT THE MAPPER LEVEL.**
 *
 * The design's three normative tables — the placeholder STATE table (decision 1), the
 * dispatch DISCIPLINE (decision 2), the mode TABLE (decision 3) — asserted against synthetic
 * frames, exactly where this suite already asserts `UpdateProp` behaviour
 * ([WidgetMapperImageTest] is the template; `/imagepolish` demonstrates the same rows on a
 * real page). The rows with no demo affordance live ONLY here: src → null with a placeholder
 * present, an unknown mode word (unrepresentable from the component), a detached error wire,
 * a cancelled request's non-dispatch staged three ways.
 *
 * The phase's one sentence, restated as this file's throughline: **every assertion below
 * re-reads a 6.3 frame and finds it unchanged.** A placeholder never measures (the drawable
 * is not the measure func's input — [WidgetMapper.ImageState]'s KDoc), an error never
 * re-measures, a mode never consults measure. The frames are the 6.3 numbers, verbatim.
 */
@RunWith(AndroidJUnit4::class)
class WidgetMapperImagePolishTest {

    private lateinit var server: ImageFixtureServer

    private companion object {
        const val SECTION = 1
        const val IMAGE = 2
        const val BAND = 3
        const val SECTION_W = 300f
        const val BAND_H = 20f
        const val ERROR_HANDLER = 77

        /** BnImagePolishDemo.razor's `PlaceholderHex` — transcribed the way SECTION_W is
         * (a device test cannot read a .razor; the DEMO test asserts the same value off the
         * page the wire actually built, which is the drift pin). */
        const val PLACEHOLDER_HEX = "#FFCA28"

        /** The recolor target (7.6 H5) — any parseable color that is not [PLACEHOLDER_HEX]. */
        const val RECOLOR_HEX = "#3F51B5"
    }

    private val placeholderColor: Int get() = Color.parseColor(PLACEHOLDER_HEX)

    @Before fun startFixtureServer() {
        ImageFixtureServer.clearCoilCaches()
        server = ImageFixtureServer()
    }

    // isInitialized: a @Before that THREW (a taken port) must not have its cause masked.
    // No server-error emptiness assert: this class CANCELS (the WidgetMapperImageTest
    // posture) — broken pipes here are cancellation observed from the other end.
    @After fun stopFixtureServer() {
        if (::server.isInitialized) server.close()
    }

    // ── Decision 1: the placeholder state table, row by row ─────────────────────

    @Test fun the_placeholder_paints_while_in_flight_and_success_CLEARS_it() {
        val host = section(src = ImageFixtureServer.SLOW_URL, declared = true)

        assertTrue("the request must be genuinely IN FLIGHT — a placeholder asserted before " +
            "the request started proves nothing about the IN-FLIGHT row",
            server.awaitPath("/slow.png"))
        host.read {
            // THE IN-FLIGHT ROW: the placeholder color fills the box — as the DRAWABLE
            // (paint inside the box Yoga already gave the node), never as size.
            assertPlaceholderPainted("in flight", image(host))
            assertFrame("the declared box, while in flight — the placeholder never bought " +
                "a dp", image(host), 0f, 0f, 200f, 120f)
            assertFrame("band under it: y = 120, the declared height", band(host),
                0f, 120f, SECTION_W, BAND_H)
        }

        server.releaseSlow()
        awaitResults(host, 1)
        assertEquals(WidgetMapper.ImageOutcome.SUCCESS,
            host.read { host.mapper.imageResults.single().outcome })

        host.read {
            // THE SUCCESS ROW: the placeholder is CLEARED — the bytes ARE the drawable now.
            // Letterbox bars (FIT_CENTER, 64×48 in a 200×120 box) show the view BACKGROUND,
            // never the placeholder: the placeholder was the drawable, and it is gone.
            assertTrue("the placeholder must be REPLACED by the bytes on SUCCESS — a " +
                "surviving ColorDrawable here is the state table's SUCCESS row broken",
                image(host).drawable is BitmapDrawable)
            assertFrame("…and the frame did not move by a hair: paint, never size",
                image(host), 0f, 0f, 200f, 120f)
            assertFrame("…nor the band's y", band(host), 0f, 120f, SECTION_W, BAND_H)
        }
    }

    @Test fun the_placeholder_STAYS_on_error_and_the_declared_box_holds() {
        val host = section(src = ImageFixtureServer.MISSING_URL, declared = true)
        server.release()
        awaitResults(host, 1)
        assertEquals("a REAL 404", WidgetMapper.ImageOutcome.ERROR,
            host.read { host.mapper.imageResults.single().outcome })

        host.read {
            // THE ERROR ROW: the placeholder STAYS — it is the error state's visual.
            assertPlaceholderPainted("after the 404", image(host))
            assertFrame("the declared box HOLDS — because it was DECLARED, not because it " +
                "failed: Yoga never called its measure func, so the failure cannot move " +
                "this frame even in principle", image(host), 0f, 0f, 200f, 120f)
            assertFrame("band y = 120, identical before and after the failure",
                band(host), 0f, 120f, SECTION_W, BAND_H)
        }
        assertEquals("…and it did not retry", 1, host.read { host.mapper.imageResults.size })
    }

    @Test fun src_to_null_clears_the_placeholder_with_the_image_and_dispatches_nothing() {
        val host = section(src = ImageFixtureServer.SLOW_URL, declared = true,
            attachError = ERROR_HANDLER)
        assertTrue(server.awaitPath("/slow.png"))
        host.read { assertPlaceholderPainted("in flight, before the clear", image(host)) }

        // THE `src → null` ROW: no source names no pending image — the placeholder is
        // cleared WITH the image (the 6.3 clear), not left behind as a ghost of a load
        // that no longer exists.
        host.render(listOf(RenderPatch.UpdateProp(nodeId = IMAGE, name = "src", value = null)))
        host.read {
            assertNull("the placeholder went with the src it was waiting for",
                image(host).drawable)
        }

        // …and the cancel the clear caused dispatches NOTHING, even with the error wire
        // attached: CANCELLED is not an error (decision 2).
        server.releaseSlow()
        awaitResults(host, 1)
        settle()
        assertEquals(listOf(WidgetMapper.ImageOutcome.CANCELLED),
            host.read { host.mapper.imageResults.map { it.outcome } })
        assertEquals("a clear NEVER dispatches `error` — a cancel is the author's own act, " +
            "not a failure to report back", 0, host.read { host.mapper.errorDispatchesSent })
    }

    @Test fun placeholderColor_recolors_the_inflight_paint_and_null_CLEARS_it() {
        // Phase 7.6 (H5, the 7.5 G3 review ledger): the matched pair neither device
        // suite pinned — a RECOLOR repaints an on-screen placeholder, and
        // `placeholderColor → null` CLEARS one — both while the request is STILL
        // open. Same observable on both shells (BnImagePolishMapperTests holds the
        // iOS half, assertion for assertion).
        val host = section(src = ImageFixtureServer.SLOW_URL, declared = true)
        assertTrue("the request must be genuinely IN FLIGHT — both rows below are about " +
            "a placeholder that is ON SCREEN over an OPEN request",
            server.awaitPath("/slow.png"))
        host.read { assertPlaceholderPainted("in flight, before the recolor", image(host)) }

        // THE RECOLOR: a placeholderColor update while the paint is on screen
        // REPAINTS it — the prop arm repaints iff the node still awaits bytes.
        host.render(listOf(RenderPatch.UpdateProp(nodeId = IMAGE, name = "placeholderColor",
            value = RECOLOR_HEX)))
        host.read {
            val d = image(host).drawable
            assertTrue("the recolor must still be a PAINTED placeholder, got " +
                "${d?.javaClass?.simpleName}", d is ColorDrawable)
            assertEquals("…in exactly the NEW color — a recolor that kept the old paint " +
                "is the prop arm silently dead after the mount",
                Color.parseColor(RECOLOR_HEX), (d as ColorDrawable).color)
            assertFrame("…and the box never moved: paint, never size",
                image(host), 0f, 0f, 200f, 120f)
        }

        // THE NULL-CLEAR: the author took the parameter away with the placeholder ON
        // SCREEN — it goes with the setting that painted it, and the request's own
        // lifecycle is untouched (nothing terminated, nothing cancelled).
        host.render(listOf(RenderPatch.UpdateProp(nodeId = IMAGE, name = "placeholderColor",
            value = null)))
        host.read {
            assertNull("the on-screen placeholder goes with the setting that painted it",
                image(host).drawable)
        }
        assertEquals("…and the clear touched PAINT only: the request is still open",
            0, host.read { host.mapper.imageResults.size })

        // The still-open request then terminates normally: the bytes land anyway.
        server.releaseSlow()
        awaitResults(host, 1)
        assertEquals(WidgetMapper.ImageOutcome.SUCCESS,
            host.read { host.mapper.imageResults.single().outcome })
        host.read {
            assertTrue("the bytes still landed after the clear — the placeholder was " +
                "paint, never the request", image(host).drawable is BitmapDrawable)
        }
    }

    @Test fun an_intrinsic_placeholder_never_measures_the_failing_side() {
        val host = section(src = ImageFixtureServer.MISSING_URL, declared = false)

        host.read {
            // BEFORE: 0 × 0 — the placeholder is a 0 × 0 paint, invisible, CORRECT, and
            // not diagnosed (a zero-sized paint is a no-op, not an error). If a
            // placeholder measured as ANYTHING, this band moves: decision 1's red line.
            assertFrame("intrinsic + placeholder, BEFORE: still ZERO — the placeholder " +
                "does not measure", image(host), 0f, 0f, 0f, 0f)
            assertFrame("band at y = 0", band(host), 0f, 0f, SECTION_W, BAND_H)
        }

        server.release()
        awaitResults(host, 1)
        assertEquals(WidgetMapper.ImageOutcome.ERROR,
            host.read { host.mapper.imageResults.single().outcome })
        host.read {
            assertFrame("AFTER the 404: STILL zero — the failure reserved nothing, with a " +
                "placeholder present exactly as without one (6.3's failure row, re-run " +
                "against the feature it was afraid of)", image(host), 0f, 0f, 0f, 0f)
            assertFrame("band at y = 0, forever", band(host), 0f, 0f, SECTION_W, BAND_H)
        }
    }

    @Test fun an_intrinsic_placeholder_still_reflows_exactly_ONCE_the_loading_side() {
        val fixture = server.decoded(server.intrinsicPng)
        val wi = fixture.width.toFloat()
        val hi = fixture.height.toFloat()
        assertTrue("Hi > 0 — Hi IS the reflow", hi > 0f)

        val host = section(src = ImageFixtureServer.INTRINSIC_URL, declared = false)
        host.read {
            assertFrame("BEFORE: 0 × 0 with a placeholder present", image(host), 0f, 0f, 0f, 0f)
            assertFrame("band at y = 0", band(host), 0f, 0f, SECTION_W, BAND_H)
        }

        server.release()
        awaitResults(host, 1)
        host.read {
            assertFrame("AFTER: the NATURAL size — the placeholder changed nothing about " +
                "the 6.3 measurement contract", image(host), 0f, 0f, wi, hi)
            assertFrame("THE REFLOW, exactly once: band y 0 → Hi", band(host),
                0f, hi, SECTION_W, BAND_H)
            assertTrue("…and the bytes replaced the placeholder",
                image(host).drawable is BitmapDrawable)
        }
    }

    // ── Decision 3: the mode table, on the widget ────────────────────────────────

    @Test fun contentMode_maps_the_four_strict_words_to_the_four_scale_types() {
        // Four images, one per wire word, same declared 120 × 60 box, stacked in one
        // 300-wide section — the quartet's mapper-level half. The DEMO asserts the four
        // identical frames through the real wire; here the per-word ScaleType spelling is
        // pinned (the design's Android mutation: "the mode arm returns FIT_CENTER for
        // every value → the per-mode property pins red (4 of 4)").
        val modes = listOf(
            "contain" to ImageView.ScaleType.FIT_CENTER,
            "cover" to ImageView.ScaleType.CENTER_CROP,
            "stretch" to ImageView.ScaleType.FIT_XY,
            "center" to ImageView.ScaleType.CENTER,
        )
        val host = SyntheticHost()
        val patches = mutableListOf<RenderPatch>(
            create(SECTION, "view", null),
            style(SECTION, "width", "300"),
            style(SECTION, "alignItems", "flex-start"),
        )
        modes.forEachIndexed { i, (word, _) ->
            val id = 10 + i
            patches += create(id, "image", SECTION)
            patches += style(id, "width", "120")
            patches += style(id, "height", "60")
            patches += RenderPatch.UpdateProp(nodeId = id, name = "src",
                value = ImageFixtureServer.FIXED_URL)
            patches += RenderPatch.UpdateProp(nodeId = id, name = "contentMode", value = word)
        }
        host.render(patches)
        server.release()
        awaitResults(host, 4)

        host.read {
            val sectionView = host.root.getChildAt(0) as ViewGroup
            modes.forEachIndexed { i, (word, scaleType) ->
                val img = sectionView.getChildAt(i) as ImageView
                assertEquals("mode '$word' → $scaleType — the table's Android spelling, " +
                    "per word (a collapsed arm reddens all four)", scaleType, img.scaleType)
                // THE PARITY RULE: four identical layout frames under four modes — the
                // Yoga box never changes with mode. Mode is PAINT-ONLY.
                assertFrame("mode '$word': the frame is the declared 120 × 60 at y = " +
                    "${i * 60} — IDENTICAL box under a different mode", img,
                    0f, i * 60f, 120f, 60f)
                assertTrue("…with the bytes painted", img.drawable is BitmapDrawable)
            }
        }
    }

    @Test fun an_unknown_contentMode_is_diagnosed_and_NOT_applied() {
        val host = section(src = ImageFixtureServer.FIXED_URL, declared = true)
        host.render(listOf(RenderPatch.UpdateProp(nodeId = IMAGE, name = "contentMode",
            value = "cover")))
        host.read { assertEquals(ImageView.ScaleType.CENTER_CROP, image(host).scaleType) }

        // Reachable by hand-rolled wire only (the enum cannot write it) — and the node
        // KEEPS its current mode: a guessed fallback is how two shells guess differently.
        host.render(listOf(RenderPatch.UpdateProp(nodeId = IMAGE, name = "contentMode",
            value = "fill")))
        host.read {
            assertEquals("the unknown word applied NOTHING — the node keeps cover",
                ImageView.ScaleType.CENTER_CROP, image(host).scaleType)
            assertTrue("…and the ignore is DIAGNOSED where a test can read it (the modal " +
                "style-ignore precedent): logcat is not an assertion surface, and this " +
                "failure is invisible on every frame table by the mode-invariance rule " +
                "itself. Got: ${host.mapper.diagnostics}",
                host.mapper.diagnostics.any { it.contains("contentMode 'fill'") })
        }
        server.release()
        awaitResults(host, 1) // hygiene: let the fixture request terminate before teardown
    }

    @Test fun contentMode_null_restores_the_default_FIT_CENTER() {
        val host = section(src = ImageFixtureServer.FIXED_URL, declared = true)
        host.render(listOf(RenderPatch.UpdateProp(nodeId = IMAGE, name = "contentMode",
            value = "stretch")))
        host.read { assertEquals(ImageView.ScaleType.FIT_XY, image(host).scaleType) }

        // The Enabled-null precedent: null on the prop wire means "the author took the
        // parameter away", and what it restores is the DEFAULT — contain, the 6.3 row's
        // value, now named.
        host.render(listOf(RenderPatch.UpdateProp(nodeId = IMAGE, name = "contentMode",
            value = null)))
        host.read {
            assertEquals("contentMode → null restores the default: FIT_CENTER (contain)",
                ImageView.ScaleType.FIT_CENTER, image(host).scaleType)
        }
        server.release()
        awaitResults(host, 1)
    }

    // ── Decision 2: the error wire ───────────────────────────────────────────────

    @Test fun a_failure_dispatches_the_WIRE_src_exactly_once_into_the_attached_handler() {
        val events = mutableListOf<Triple<Int, String, String?>>()
        val host = section(src = ImageFixtureServer.MISSING_URL, declared = true,
            attachError = ERROR_HANDLER,
            onUiEvent = { h, name, payload -> events.add(Triple(h, name, payload)) })

        server.release()
        awaitResults(host, 1)
        settle() // a dispatch that must arrive exactly ONCE gets every chance to double

        assertEquals("EXACTLY ONE dispatch: the event name is `error`, the payload is the " +
            "WIRE's src, VERBATIM — the URL is the only fact two loaders share about the " +
            "same failure, so it is the only payload two shells can dispatch identically",
            listOf(Triple(ERROR_HANDLER, "error", ImageFixtureServer.MISSING_URL)),
            host.read { events.toList() })
        assertEquals("…and the counter agrees (the device pages' only honest observation " +
            "point — an /imagepolish dispatch that doubled would move no frame)",
            1, host.read { host.mapper.errorDispatchesSent })
    }

    @Test fun an_unbound_failure_dispatches_NOTHING() {
        val host = section(src = ImageFixtureServer.MISSING_URL, declared = true)
        server.release()
        awaitResults(host, 1)
        settle()
        assertEquals("no attach means no wire — attach-iff-HasDelegate's shell half: the " +
            "failure stays what it always was, a logged, painted-nothing 404",
            0, host.read { host.mapper.errorDispatchesSent })
    }

    @Test fun a_detached_error_wire_dispatches_NOTHING() {
        val host = section(src = ImageFixtureServer.MISSING_URL, declared = true,
            attachError = ERROR_HANDLER)
        host.render(listOf(RenderPatch.DetachEvent(nodeId = IMAGE, handlerId = ERROR_HANDLER,
            eventName = "error")))
        server.release()
        awaitResults(host, 1)
        settle()
        assertEquals("the detach killed the wire before the failure terminated — the " +
            "attach arm's mirror (the 3.3 symmetric-arms rule)",
            0, host.read { host.mapper.errorDispatchesSent })
    }

    @Test fun a_src_change_cancels_and_the_cancellation_dispatches_NOTHING() {
        val host = section(src = ImageFixtureServer.SLOW_URL, declared = true,
            attachError = ERROR_HANDLER)
        server.release() // ordinary paths answer; /slow.png held on its own gate
        assertTrue(server.awaitPath("/slow.png"))

        host.render(listOf(RenderPatch.UpdateProp(nodeId = IMAGE, name = "src",
            value = ImageFixtureServer.INTRINSIC_URL)))
        awaitResults(host, 2)
        server.releaseSlow()
        settle()

        assertEquals("the in-flight request was CANCELLED and the new one succeeded",
            setOf(
                ImageFixtureServer.SLOW_URL to WidgetMapper.ImageOutcome.CANCELLED,
                ImageFixtureServer.INTRINSIC_URL to WidgetMapper.ImageOutcome.SUCCESS,
            ),
            host.read { host.mapper.imageResults.map { it.url to it.outcome }.toSet() })
        assertEquals("CANCELLED IS NOT AN ERROR: a Src change dispatches nothing, with the " +
            "wire attached and live the whole time", 0,
            host.read { host.mapper.errorDispatchesSent })
    }

    @Test fun node_removal_cancels_and_the_cancellation_dispatches_NOTHING() {
        val host = section(src = ImageFixtureServer.SLOW_URL, declared = true,
            attachError = ERROR_HANDLER)
        assertTrue(server.awaitPath("/slow.png"))

        host.render(listOf(RenderPatch.RemoveNode(nodeId = SECTION)))
        awaitResults(host, 1)
        server.releaseSlow()
        settle()

        assertEquals(listOf(WidgetMapper.ImageOutcome.CANCELLED),
            host.read { host.mapper.imageResults.map { it.outcome } })
        assertEquals("node removal dispatches nothing — and could not even if it tried: " +
            "the purge took the error wire with the node (ids restart; a wire that " +
            "outlived its node would answer for the next node to inherit the id)",
            0, host.read { host.mapper.errorDispatchesSent })
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /**
     * The 6.3 case section, with the 7.5 props riding in WIRE ORDER: `src` (seq 24) lands
     * BEFORE `placeholderColor` (seq 25) — the order [WidgetMapper.ImageState]'s KDoc calls
     * load-bearing, and the one the renderer actually emits.
     */
    private fun section(
        src: String,
        declared: Boolean,
        attachError: Int? = null,
        onUiEvent: ((Int, String, String?) -> Unit)? = null,
    ): SyntheticHost {
        val host = if (onUiEvent != null) SyntheticHost(onUiEvent = onUiEvent) else SyntheticHost()
        val patches = mutableListOf<RenderPatch>(
            create(SECTION, "view", null),
            style(SECTION, "width", "300"),
            style(SECTION, "alignItems", "flex-start"),
            create(IMAGE, "image", SECTION),
        )
        if (declared) {
            patches += style(IMAGE, "width", "200")
            patches += style(IMAGE, "height", "120")
        }
        patches += RenderPatch.UpdateProp(nodeId = IMAGE, name = "src", value = src)
        patches += RenderPatch.UpdateProp(nodeId = IMAGE, name = "placeholderColor",
            value = PLACEHOLDER_HEX)
        if (attachError != null) {
            patches += RenderPatch.AttachEvent(nodeId = IMAGE, eventName = "error",
                handlerId = attachError)
        }
        patches += create(BAND, "view", SECTION)
        patches += style(BAND, "width", "300")
        patches += style(BAND, "height", "20")
        host.render(patches)
        return host
    }

    /** The view-state pin: the drawable IS the placeholder — a [ColorDrawable] of exactly
     * the prop's color. Main-thread callers only (inside host.read). */
    private fun assertPlaceholderPainted(whenWhat: String, image: ImageView) {
        val d = image.drawable
        assertTrue("the placeholder must be PAINTED $whenWhat — the drawable is the " +
            "placeholder (paint inside the box Yoga gave the node), got " +
            "${d?.javaClass?.simpleName}", d is ColorDrawable)
        assertEquals("…in exactly the prop's color", placeholderColor, (d as ColorDrawable).color)
    }

    private fun image(host: SyntheticHost): ImageView =
        (host.root.getChildAt(0) as ViewGroup).getChildAt(0) as ImageView

    private fun band(host: SyntheticHost) =
        (host.root.getChildAt(0) as ViewGroup).getChildAt(1)

    /** The synchronization gate, mapper form — [WidgetMapperImageTest]'s, verbatim. */
    private fun awaitResults(host: SyntheticHost, count: Int) {
        val deadline = System.currentTimeMillis() + 30_000
        while (System.currentTimeMillis() < deadline) {
            if (host.read { host.mapper.imageResults.size } >= count) {
                InstrumentationRegistry.getInstrumentation().waitForIdleSync()
                return
            }
            Thread.sleep(50)
        }
        throw AssertionError(
            "only ${host.read { host.mapper.imageResults.size }} of $count image request(s) " +
                "terminated within 30s. A timeout here is a FAILURE, never a licence to proceed.")
    }

    /** Gives a dispatch that must NOT arrive (or must not DOUBLE) every chance to do so. */
    private fun settle() {
        val instr = InstrumentationRegistry.getInstrumentation()
        instr.waitForIdleSync()
        Thread.sleep(750)
        instr.waitForIdleSync()
    }
}
