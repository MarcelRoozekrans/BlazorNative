package io.blazornative.shell

import android.view.ViewGroup
import android.widget.ImageView
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import io.blazornative.jni.RenderPatch
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

/**
 * Phase 6.3 Gate 2 (Tasks 2.2 / 2.3) — **THE PARITY CONTRACT, AT THE MAPPER LEVEL.**
 *
 * The contract (design §"The parity contract") has seven rows. `/image` demonstrates three
 * of them on a real page; the rest have no demo affordance and never will — no page flips
 * a `Src` at run time (adding one would rewrite this phase's frame tables), and no page can
 * hold a request open long enough for a test to cancel it. They are asserted here, against
 * synthetic frames, which is exactly where this suite already asserts `UpdateProp`
 * behaviour.
 *
 * | contract row | pinned by |
 * |---|---|
 * | no `Width`/`Height` → 0×0, then the NATURAL size | [an_intrinsic_image_measures_zero_before_the_bytes_and_its_NATURAL_size_after] (+ `/image`) |
 * | `Width`/`Height` set → exactly those, always | [a_definite_image_is_never_measured_so_the_bytes_cannot_move_its_frame] (+ `/image`) |
 * | on failure → 0×0, reserves nothing, no retry | [a_failed_load_stays_zero_and_reserves_nothing] (+ `/image`) |
 * | on `Src` change → cancel; back to 0×0 | [a_src_change_cancels_the_request_in_flight] |
 * | on **`Src` → null** → cancel, CLEAR, collapse; **siblings move back UP** | [src_to_null_clears_the_image_and_the_sibling_moves_back_UP] |
 * | on node removal → **cancel** | [removing_the_node_cancels_the_request_IN_FLIGHT] |
 * | no downsampling that changes the reported size | every natural size below is asserted against the DECODED fixture's own pixel count |
 *
 * **Cancellation is memory safety, not hygiene** (non-negotiable #4): a completion firing
 * into a purged node would touch a freed `YGNodeRef` on iOS. Android's is the same bug with
 * a softer landing, and it is pinned here in the only honest direction — the request is
 * proven to have reached the fixture server ([ImageFixtureServer.awaitPath]) *before* it is
 * cancelled, so `CANCELLED` cannot be the trivially-true answer about a request that had
 * not started.
 */
@RunWith(AndroidJUnit4::class)
class WidgetMapperImageTest {

    private lateinit var server: ImageFixtureServer

    private companion object {
        const val SECTION = 1
        const val IMAGE = 2
        const val BAND = 3
        const val SECTION_W = 300f
        const val BAND_H = 20f
    }

    @Before fun startFixtureServer() {
        ImageFixtureServer.clearCoilCaches()
        server = ImageFixtureServer()
    }

    // isInitialized: a @Before that THREW (a taken port) must not have its cause masked by
    // an UninitializedPropertyAccessException out of the @After.
    @After fun stopFixtureServer() {
        if (::server.isInitialized) server.close()
    }

    // ── The two measurement paths ────────────────────────────────────────────────

    @Test fun an_intrinsic_image_measures_zero_before_the_bytes_and_its_NATURAL_size_after() {
        // THE FIXTURE CONTRACT — on the DECODED bytes, BEFORE any frame is read. An
        // unasserted fixture constraint is a coincidence waiting to happen.
        val fixture = server.decoded(server.intrinsicPng)
        val wi = fixture.width.toFloat()
        val hi = fixture.height.toFloat()
        assertTrue("0 < Wi ≤ 300 — a section is 300 wide, so the measure func is called with " +
            "AT_MOST(300); a wider fixture would ask a clamping question this phase " +
            "deliberately does not answer (no ContentMode). Got $wi", wi > 0f && wi <= SECTION_W)
        assertTrue("Hi > 0, comfortably: HI *IS* THE REFLOW. A 0-high fixture makes the reflow " +
            "assertion vacuously true. Got $hi", hi > 0f)

        val host = section(src = ImageFixtureServer.INTRINSIC_URL)

        // ── BEFORE THE BYTES ─────────────────────────────────────────────────────
        assertEquals("nothing has terminated yet — the fixture server is HOLDING every " +
            "response, which is the only thing that makes this 'before' honest",
            0, host.read { host.mapper.imageResults.size })
        host.read {
            assertFrame("the intrinsic image, BEFORE: a measured leaf with no bytes measures " +
                "0 × 0 — not 'small', ZERO", image(host), 0f, 0f, 0f, 0f)
            assertFrame("band I, BEFORE: y = 0", band(host), 0f, 0f, SECTION_W, BAND_H)
            assertFrame("the section HUGS 0 + 20", host.root.getChildAt(0),
                0f, 0f, SECTION_W, BAND_H)
        }

        // ── THE BYTES LAND ───────────────────────────────────────────────────────
        server.release()
        awaitResults(host, 1)

        assertEquals("Coil's own per-node TERMINAL callback said SUCCESS — the bytes really " +
            "arrived, over real HTTP, from the in-process loopback fixture",
            listOf(ImageFixtureServer.INTRINSIC_URL to WidgetMapper.ImageOutcome.SUCCESS),
            host.read { host.mapper.imageResults.map { it.url to it.outcome } })

        host.read {
            // POSITIVE, and against the DECODED FIXTURE's own pixel count — never a constant
            // this file invents. A loader stubbed to a fixed size reddens here; so does any
            // downsampling that changed the reported size (the contract's last row).
            assertFrame("the intrinsic image, AFTER: its NATURAL size, ${fixture.width} × " +
                "${fixture.height} PIXELS read as dp. ONE PIXEL IS ONE dp/pt — that is what " +
                "UIImage(data:).size hands iOS for free, so it is what Android must report or " +
                "the two shells cannot compute the same frame",
                image(host), 0f, 0f, wi, hi)
            assertTrue("POSITIVELY: Wi > 0 AND Hi > 0. Two of this phase's three demo cases " +
                "assert 'nothing moved', and a suite of negatives is a suite that a TOTAL " +
                "FAILURE satisfies", image(host).width > 0 && image(host).height > 0)
            assertFrame("THE REFLOW: band I moved from y = 0 to y = Hi. Only a genuine re-solve " +
                "moves the node BELOW the image — markDirty (6.1) then calculateAndApply (6.2)",
                band(host), 0f, hi, SECTION_W, BAND_H)
            assertFrame("…and the section grew by exactly Hi", host.root.getChildAt(0),
                0f, 0f, SECTION_W, hi + BAND_H)
            assertNotNull("the bytes were also PAINTED", image(host).drawable)
        }
        assertEquals("nothing is left in flight", 0, host.read { host.mapper.inFlightImageCount })
    }

    @Test fun a_definite_image_is_never_measured_so_the_bytes_cannot_move_its_frame() {
        val fixture = server.decoded(server.fixedPng)
        assertTrue("the FIXED case's fixture must NOT be 200 × 120 — otherwise 'it measures " +
            "200 × 120' is a coincidence rather than a proof that a declared size " +
            "short-circuits measurement. Got ${fixture.width} × ${fixture.height}",
            fixture.width != 200 || fixture.height != 120)

        val host = section(
            src = ImageFixtureServer.FIXED_URL,
            imageStyles = listOf(style(IMAGE, "width", "200"), style(IMAGE, "height", "120")),
        )

        host.read {
            assertFrame("the fixed image, BEFORE the bytes", image(host), 0f, 0f, 200f, 120f)
            assertFrame("band F, BEFORE: y = 120", band(host), 0f, 120f, SECTION_W, BAND_H)
        }

        server.release()
        awaitResults(host, 1)
        assertEquals("the request DID terminate — otherwise 'the frame did not move' is a " +
            "statement about a fetch that never happened",
            WidgetMapper.ImageOutcome.SUCCESS,
            host.read { host.mapper.imageResults.single().outcome })

        host.read {
            // THE IDENTITY. Both axes definite ⇒ Yoga never calls the measure func at all,
            // so the fixture's 64 × 48 is nowhere in this frame and could not be.
            assertFrame("the fixed image, AFTER: IDENTICAL. Width AND Height are definite, so " +
                "Yoga never calls its measure func — the bytes cannot move this frame even in " +
                "principle", image(host), 0f, 0f, 200f, 120f)
            assertFrame("band F, AFTER: y = 120, UNCHANGED — THE NO-REFLOW PROOF",
                band(host), 0f, 120f, SECTION_W, BAND_H)
            assertNotNull("…and the bytes were painted all the same", image(host).drawable)
        }
    }

    @Test fun a_failed_load_stays_zero_and_reserves_nothing() {
        val host = section(src = ImageFixtureServer.MISSING_URL)

        server.release()
        awaitResults(host, 1)

        assertEquals("a REAL 404 — from a REAL server, deterministic and offline",
            WidgetMapper.ImageOutcome.ERROR,
            host.read { host.mapper.imageResults.single().outcome })
        host.read {
            assertFrame("the failing image stays 0 × 0", image(host), 0f, 0f, 0f, 0f)
            assertFrame("band X did not move: THE FAILURE RESERVED NOTHING",
                band(host), 0f, 0f, SECTION_W, BAND_H)
            assertNull("nothing was painted", image(host).drawable)
        }
        assertEquals("…and it did NOT retry", 1, host.read { host.mapper.imageResults.size })
        assertEquals("nothing is left in flight", 0, host.read { host.mapper.inFlightImageCount })
    }

    // ── The reflow in the OTHER direction (design §"On `Src` → `null`") ──────────

    @Test fun src_to_null_clears_the_image_and_the_sibling_moves_back_UP() {
        val fixture = server.decoded(server.intrinsicPng)
        val hi = fixture.height.toFloat()
        val host = section(src = ImageFixtureServer.INTRINSIC_URL)
        server.release()
        awaitResults(host, 1)
        host.read {
            assertFrame("the reflow DOWN happened first", band(host), 0f, hi, SECTION_W, BAND_H)
        }

        // THE PATCH THE RENDERER ALREADY EMITS: a RemoveAttribute on a non-style name
        // (BnButton.Enabled's precedent), pinned in .NET by BnComponentTests
        // .BnImage_SrcGoesNull_EmitsUpdatePropNullOnThePropWire. A shell that NPEs on it,
        // or that keeps painting the old bytes, is wrong in the way two shells wrong
        // DIFFERENTLY is worst: silently, on one platform.
        host.render(listOf(RenderPatch.UpdateProp(nodeId = IMAGE, name = "src", value = null)))

        host.read {
            assertNull("the image was CLEARED — the pixels go", image(host).drawable)
            assertFrame("the node collapsed back to 0 × 0", image(host), 0f, 0f, 0f, 0f)
            assertFrame("THE SECOND REFLOW DIRECTION: band I moved back UP, to y = 0",
                band(host), 0f, 0f, SECTION_W, BAND_H)
            assertFrame("…and the section shrank back to hugging its band alone",
                host.root.getChildAt(0), 0f, 0f, SECTION_W, BAND_H)
        }
        assertEquals("nothing was left in flight", 0, host.read { host.mapper.inFlightImageCount })
    }

    // ── Cancellation: MEMORY SAFETY, NOT HYGIENE (non-negotiable #4) ─────────────

    @Test fun a_src_change_cancels_the_request_in_flight() {
        val fixture = server.decoded(server.intrinsicPng)
        val host = section(src = ImageFixtureServer.SLOW_URL)
        server.release() // the ordinary paths answer; /slow.png is held on its own gate

        assertTrue("the slow request never reached the fixture server — cancelling a request " +
            "that had not started proves nothing", server.awaitPath("/slow.png"))

        host.render(listOf(RenderPatch.UpdateProp(
            nodeId = IMAGE, name = "src", value = ImageFixtureServer.INTRINSIC_URL)))

        awaitResults(host, 2)
        server.releaseSlow() // and now the old bytes arrive at nobody

        assertEquals("the IN-FLIGHT request was CANCELLED, and the new one succeeded",
            listOf(
                ImageFixtureServer.SLOW_URL to WidgetMapper.ImageOutcome.CANCELLED,
                ImageFixtureServer.INTRINSIC_URL to WidgetMapper.ImageOutcome.SUCCESS,
            ),
            host.read { host.mapper.imageResults.map { it.url to it.outcome } })
        host.read {
            assertFrame("the node measures the NEW bytes", image(host),
                0f, 0f, fixture.width.toFloat(), fixture.height.toFloat())
        }
        assertEquals("nothing is left in flight", 0, host.read { host.mapper.inFlightImageCount })
    }

    @Test fun removing_the_node_cancels_the_request_IN_FLIGHT() {
        val host = section(src = ImageFixtureServer.SLOW_URL)
        assertEquals("the section, the image and the band", 3, host.read { host.mapper.nodeCount })
        assertTrue("the request never reached the fixture server", server.awaitPath("/slow.png"))
        val doomed = host.read { image(host) }

        // ONE RemoveNodePatch, naming the SECTION — the shape navigation actually emits (it
        // names the page's root; it never names the image). The image's request must be
        // cancelled as part of the SUBTREE purge, or a completion fires into a removed node:
        // on iOS that is a freed YGNodeRef — 6.2's dangling-pointer lesson in a new costume.
        host.render(listOf(RenderPatch.RemoveNode(nodeId = SECTION)))

        awaitResults(host, 1)
        assertEquals("THE PIN: the in-flight request was CANCELLED by the purge",
            WidgetMapper.ImageOutcome.CANCELLED,
            host.read { host.mapper.imageResults.single().outcome })
        assertEquals("nothing is left in flight", 0, host.read { host.mapper.inFlightImageCount })

        // …and NOW let the bytes arrive. A cancelled request must paint NOTHING — not into
        // the detached ImageView, and not into a Yoga node that no longer exists.
        server.releaseSlow()
        settle()

        assertEquals("A LATE COMPLETION PAINTED NOTHING: still exactly one terminal result, " +
            "and it is still CANCELLED", listOf(WidgetMapper.ImageOutcome.CANCELLED),
            host.read { host.mapper.imageResults.map { it.outcome } })
        assertNull("…and nothing was set on the removed ImageView", host.read { doomed.drawable })
        assertEquals("ONE RemoveNodePatch purged the whole subtree from BOTH trees — the image " +
            "node the completion would have painted into does not exist any more, in either. " +
            "(On iOS that node's YGNodeRef is FREED, which is why this is memory safety.)",
            0 to 0, host.read { host.mapper.nodeCount to host.mapper.yogaNodeCount })
        assertEquals("the section is detached from the host root",
            0, host.read { host.root.childCount })
    }

    // ── Robustness ───────────────────────────────────────────────────────────────

    @Test fun src_on_a_non_image_node_is_logged_and_ignored() {
        val host = SyntheticHost()
        host.render(listOf(
            create(1, "button", null),
            RenderPatch.UpdateProp(
                nodeId = 1, name = "src", value = ImageFixtureServer.INTRINSIC_URL),
        ))
        assertEquals("no request is issued for a node that cannot hold an image",
            0, host.read { host.mapper.inFlightImageCount })
        assertEquals("…and none ever terminates", 0, host.read { host.mapper.imageResults.size })
        assertEquals("the Button is still there", 1, host.read { host.root.childCount })
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /**
     * `BnImageDemo`'s case section, in one frame: a 300-wide column that HUGS its two
     * children, holding an image and a 20dp band beneath it.
     *
     * **`alignItems: flex-start` is load-bearing** (as it is on the demo page): a section is
     * a COLUMN, so its cross axis is WIDTH, and Yoga's default `alignItems` is **stretch** —
     * under which an intrinsic image would be stretched to 300 and its measured width would
     * never be seen at all.
     *
     * **The band's y is the proof.** An image's own frame is a bad witness: a shell could
     * paint the bytes and never re-solve, and the image would still look right. Only a
     * genuine re-solve moves the node BELOW it.
     */
    private fun section(src: String, imageStyles: List<RenderPatch> = emptyList()): SyntheticHost {
        val host = SyntheticHost()
        host.render(listOf(
            create(SECTION, "view", null),
            style(SECTION, "width", "300"),
            style(SECTION, "alignItems", "flex-start"),
            create(IMAGE, "image", SECTION),
        ) + imageStyles + listOf(
            RenderPatch.UpdateProp(nodeId = IMAGE, name = "src", value = src),
            create(BAND, "view", SECTION),
            style(BAND, "width", "300"),
            style(BAND, "height", "20"),
        ))
        return host
    }

    private fun image(host: SyntheticHost): ImageView =
        (host.root.getChildAt(0) as ViewGroup).getChildAt(0) as ImageView

    private fun band(host: SyntheticHost) =
        (host.root.getChildAt(0) as ViewGroup).getChildAt(1)

    /**
     * **THE SYNCHRONIZATION GATE** (non-negotiable #6) in its mapper-level form: waits on
     * Coil's own per-node TERMINAL callback (`ImageRequest.Listener` — `onSuccess` /
     * `onError` / `onCancel`), counted, with a timeout that **FAILS** rather than proceeds.
     *
     * Never a poll on a FRAME. Two of this phase's three cases assert "nothing moved", and a
     * frame-poll cannot tell "the request finished and changed nothing" from "the request
     * has not finished" — which is the whole reason the gate is normative.
     *
     * The re-solve a callback triggers happens INSIDE that callback's main-thread unit of
     * work, and this read is a LATER main-thread message: a result this method can see is a
     * result whose layout has already been applied.
     */
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
                "terminated within 30s. A timeout here is a FAILURE, never a licence to " +
                "proceed: the AFTER frames may only be read once EVERY request has ended.")
    }

    /** Gives a completion that must NOT arrive every chance to arrive. */
    private fun settle() {
        val instr = InstrumentationRegistry.getInstrumentation()
        instr.waitForIdleSync()
        Thread.sleep(750)
        instr.waitForIdleSync()
    }
}
