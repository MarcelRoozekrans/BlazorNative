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
 * | on **`Src` → null** → cancel, CLEAR, collapse; **siblings move back UP** | [src_to_null_clears_the_image_and_the_sibling_moves_back_UP], and — **with a request genuinely IN FLIGHT** — [a_clear_while_the_request_is_IN_FLIGHT_cancels_it_and_nothing_re_inflates] |
 * | **every `src` write bumps the generation, INCLUDING a clear** | [every_src_write_bumps_the_generation_INCLUDING_a_clear] (a NUMBER: no frame can see it, and the FIFO main looper means no device test can stage the race it defends — see that test) |
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

            // THE CONTENT MODE — and THIS is the case where it bites: a 64 × 48 fixture
            // inside a DECLARED 200 × 120 frame. The two frameworks' defaults DISAGREE
            // (Android FIT_CENTER; UIImageView .scaleToFill — a STRETCH), the divergence is
            // FRAME-NEUTRAL (every number above survives it), and so no frame table on either
            // platform can see it: what breaks is "renders identically", silently, on one
            // platform. It is normative in the parity contract, and Gate 3 owes
            // `contentMode = .scaleAspectFit`. Deferring the ContentMode API (decision 3) does
            // not defer the default — 6.1's clipChildren precedent.
            assertEquals("the content mode is ASPECT-FIT, and it is set EXPLICITLY rather than " +
                "inherited from the framework — the two frameworks' defaults disagree, and the " +
                "disagreement is invisible to every frame assertion in this file",
                ImageView.ScaleType.FIT_CENTER, image(host).scaleType)
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

    /**
     * **THE CLEAR PATH, WITH A REQUEST ACTUALLY IN FLIGHT** (Gate 3 review, C1) — the coverage
     * gap the review found: [src_to_null_clears_the_image_and_the_sibling_moves_back_UP] **awaits
     * the load first**, so nothing was ever in flight when the clear arrived, and *no test in
     * either suite cancelled via the null path.*
     *
     * Here the request has genuinely REACHED the fixture server ([ImageFixtureServer.awaitPath])
     * and is held there when the clear lands. The contract's `Src` → `null` row in full:
     * **cancel**, CLEAR, collapse to 0 × 0, siblings move UP — and then the bytes are released
     * anyway, because a test that merely never saw the callback has proven nothing.
     */
    @Test fun a_clear_while_the_request_is_IN_FLIGHT_cancels_it_and_nothing_re_inflates() {
        val host = section(src = ImageFixtureServer.SLOW_URL)
        server.release() // the ordinary paths answer; /slow.png is held on its own gate

        assertTrue("the request never reached the fixture server — clearing a `Src` whose " +
            "request had not started proves nothing about the CANCEL",
            server.awaitPath("/slow.png"))
        assertEquals("…and the shell is holding its cancellation handle",
            1, host.read { host.mapper.inFlightImageCount })

        // THE CLEAR, ON A LIVE REQUEST.
        host.render(listOf(RenderPatch.UpdateProp(nodeId = IMAGE, name = "src", value = null)))

        assertEquals("the clear CANCELLED it — a `Src` → null is not 'stop painting', it is " +
            "'stop fetching' as well", 0, host.read { host.mapper.inFlightImageCount })

        // …and NOW let the bytes arrive, at nobody.
        server.releaseSlow()
        awaitResults(host, 1)
        settle()

        assertEquals("exactly one terminal result, and it is CANCELLED — the request the clear " +
            "cancelled did not then quietly succeed into the node behind it",
            listOf(WidgetMapper.ImageOutcome.CANCELLED),
            host.read { host.mapper.imageResults.map { it.outcome } })
        host.read {
            assertNull("NOTHING WAS PAINTED into the cleared node", image(host).drawable)
            assertFrame("the node is still 0 × 0", image(host), 0f, 0f, 0f, 0f)
            assertFrame("THE BAND IS STILL AT y = 0: the node the author cleared did NOT " +
                "re-inflate", band(host), 0f, 0f, SECTION_W, BAND_H)
            assertFrame("…and the section still hugs its band alone",
                host.root.getChildAt(0), 0f, 0f, SECTION_W, BAND_H)
        }
    }

    /**
     * **EVERY `src` WRITE BUMPS THE GENERATION — INCLUDING A CLEAR.** (Gate 3 review, C1.)
     *
     * `cancelImageRequest` is best-effort **by definition** — that is the whole reason the
     * generation exists. A clear whose dispose LOSES the race (the work finished, the callback is
     * already on its way to the main thread) delivers `onSuccess` carrying generation *N*; if the
     * clear did not bump, that callback finds `imageGenerations` still on *N* and the same view,
     * [isLiveImageRequest] says **LIVE**, and it paints the stale bytes, records their natural
     * size and re-solves — **the node the author just cleared re-inflates and its sibling moves
     * back down.**
     *
     * ── WHY THIS IS ASSERTED AS A NUMBER AND NOT AS A FRAME ─────────────────────────────
     * **No frame in this repo can see it, and no device test can stage it.** The main looper is
     * FIFO: a callback already posted runs BEFORE any batch a test posts after it, so the only
     * orderings a device test can produce are the ones where the clear WINS the race — which is
     * exactly the case the bump is not needed for ([a_clear_while_the_request_is_IN_FLIGHT_cancels_it_and_nothing_re_inflates]
     * is that case, and it is green with or without the bump). So the bump is pinned as the fact
     * it is, and `ImageRequestGuardTest.a_superseded_generation_is_not_live` (JVM lane) is what
     * that fact then BUYS. Two pinned facts, one contract row.
     *
     * **MUTATION EVIDENCE (RUN on a throwaway branch, against the 111/0 bar):** move the bump back
     * below `if (url.isNullOrEmpty()) return` ⇒ **1 red, here, and NOTHING ELSE** —
     * `A CLEAR BUMPS IT TOO … expected:<2> but was:<1>`. Not a frame table, not
     * [a_clear_while_the_request_is_IN_FLIGHT_cancels_it_and_nothing_re_inflates] one test above:
     * the dispose WINS that race in every ordering a device test can produce, so this number is
     * the only witness there can be. That is the finding, not a weakness of the pin. (The iOS
     * twin was run too, and reddens identically: `("Optional(1)") is not equal to
     * ("Optional(2)")`.)
     */
    @Test fun every_src_write_bumps_the_generation_INCLUDING_a_clear() {
        val host = section(src = ImageFixtureServer.SLOW_URL)
        assertGeneration(host, 1, "the first `src` write puts the node on generation 1")

        host.render(listOf(RenderPatch.UpdateProp(nodeId = IMAGE, name = "src", value = null)))
        assertGeneration(host, 2,
            "A CLEAR BUMPS IT TOO. The clear cancels, the cancel races its own callback, and " +
                "this number is the ONLY thing that stops the loser painting into a node that " +
                "has been cleared")

        host.render(listOf(RenderPatch.UpdateProp(nodeId = IMAGE, name = "src", value = "")))
        assertGeneration(host, 3, "…and so does an EMPTY string, which takes the same clear path")

        host.render(listOf(RenderPatch.UpdateProp(
            nodeId = IMAGE, name = "src", value = ImageFixtureServer.INTRINSIC_URL)))
        assertGeneration(host, 4,
            "…and a real URL bumps it exactly once, as it always did — the bump MOVED, it did " +
                "not multiply")

        server.release()
        server.releaseSlow()
        awaitResults(host, 2)
        assertEquals("and only the two REAL requests were ever issued: the clear and the empty " +
            "string fetched nothing",
            setOf(
                ImageFixtureServer.SLOW_URL to WidgetMapper.ImageOutcome.CANCELLED,
                ImageFixtureServer.INTRINSIC_URL to WidgetMapper.ImageOutcome.SUCCESS,
            ),
            host.read { host.mapper.imageResults.map { it.url to it.outcome }.toSet() })

        host.render(listOf(RenderPatch.RemoveNode(nodeId = SECTION)))
        assertGeneration(host, null,
            "THE PURGE TAKES THE GENERATION WITH IT — node ids RESTART at 1, so a generation " +
                "that outlived its node would be handed to the node that inherits its id, and a " +
                "stale callback carrying it would match")
    }

    /** The image node's CURRENT generation, read on the main thread. Typed `Int?` on both sides
     * so the null case is the same assertion as the others. */
    private fun assertGeneration(host: SyntheticHost, expected: Int?, what: String) {
        val actual: Int? = host.read { host.mapper.imageGeneration(IMAGE) }
        assertEquals(what, expected, actual)
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

        // A SET, not a list. What this test claims is *what happened to each request* — the
        // in-flight one was cancelled, the new one succeeded — and that claim is
        // order-independent. The ORDER (cancel before success) is a fact about the fixture
        // server's gate and about a cold Coil cache, not about the contract, and asserting it
        // here would make this test redden for a reason it is not about. (It is exactly the
        // ordering the WARM-cache path inverts: a memory hit completes SYNCHRONOUSLY, before
        // the cancelled request's own callback has been posted.)
        assertEquals("the IN-FLIGHT request was CANCELLED, and the new one succeeded",
            setOf(
                ImageFixtureServer.SLOW_URL to WidgetMapper.ImageOutcome.CANCELLED,
                ImageFixtureServer.INTRINSIC_URL to WidgetMapper.ImageOutcome.SUCCESS,
            ),
            host.read { host.mapper.imageResults.map { it.url to it.outcome }.toSet() })
        host.read {
            assertFrame("the node measures the NEW bytes", image(host),
                0f, 0f, fixture.width.toFloat(), fixture.height.toFloat())
        }
        assertEquals("nothing is left in flight", 0, host.read { host.mapper.inFlightImageCount })
    }

    /**
     * **THE MUTATION THIS TEST EXISTS FOR, AND WHAT IT ACTUALLY LOOKS LIKE** — recorded because
     * the Gate 2 review disputed it, and re-running it settled the question:
     *
     *   delete `cancelImageRequest(id)` from `WidgetMapper.handleRemove` ⇒ **1 red, here, with
     *   `expected:<CANCELLED> but was:<ERROR>`**, in ~10s.
     *
     * The **`ERROR` is `OkHttp`'s 10-second READ TIMEOUT.** `/slow.png` is held on its own gate for
     * the whole test, so an *uncancelled* request does not hang and does not reach
     * [awaitResults]' 30s deadline — it sits on the socket until OkHttp gives up, and Coil reports
     * `onError`. That is what makes the assertion below fail on the OUTCOME rather than on a
     * timeout, which is a far better failure: it names the contract row that broke.
     *
     * **GATE 3 MUST NOT ASSUME THIS.** `URLSession`'s default `timeoutIntervalForRequest` is
     * **60 seconds**, not 10 — so on iOS the same mutation would NOT terminate inside the test's
     * own gate, and the red would be the gate timing out instead. If Gate 3 wants the same clean
     * outcome-shaped failure, it must shorten its fixture's timeout (or its `KingfisherOptionsInfo`
     * `downloadTimeout`) to something below the test gate. Either way the mutation must be RUN, not
     * reasoned about — this note exists because reasoning about it got it wrong once.
     */
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

    /**
     * **THE RESULT LOG IS A BOUNDED RING** (Gate 2 review, I3) — because it is appended by every
     * terminal callback and it lives in PRODUCTION code.
     *
     * Unbounded, it grows one entry per image per navigation, for as long as the app runs, evicted
     * only by `destroy()`. [YogaLayout.diagnosed] is evicted on node removal for exactly this
     * reason, one file away, citing the 6.2 lesson by name — and eviction-on-removal is not
     * available here, because [removing_the_node_cancels_the_request_IN_FLIGHT] reads the log
     * *after* the purge that is its whole subject. So it is CAPPED, and the cap is asserted against
     * the shell's own number rather than one this test invented.
     *
     * `imageTerminalCount` is what makes the overflow observable at all: [imageResults] cannot
     * count past the cap, so a test that waits for "more than the cap" on IT would wait forever.
     */
    @Test fun the_result_log_is_a_BOUNDED_ring_it_cannot_grow_forever() {
        val host = SyntheticHost()
        val cap = host.read { host.mapper.imageResultCap }
        val overflow = cap + 6

        // One image node per request — all 404s, all from the loopback fixture, all terminal.
        val patches = mutableListOf<RenderPatch>()
        for (i in 1..overflow) {
            patches += create(i, "image", null)
            patches += RenderPatch.UpdateProp(
                nodeId = i, name = "src", value = ImageFixtureServer.MISSING_URL)
        }
        host.render(patches)
        server.release()

        // Wait on the TOTAL, not on the log — the log is exactly the thing that cannot count
        // this high, which is the property under test.
        val deadline = System.currentTimeMillis() + 60_000
        while (System.currentTimeMillis() < deadline &&
            host.read { host.mapper.imageTerminalCount } < overflow) {
            Thread.sleep(50)
        }
        assertEquals("all $overflow requests must terminate — otherwise 'the log is bounded' is a " +
            "statement about a log nothing filled",
            overflow, host.read { host.mapper.imageTerminalCount })

        assertEquals("THE BOUND: $overflow requests terminated and the log holds the last $cap. " +
            "An unbounded list here is a leak that grows with every image, on every navigation, " +
            "for the life of the process",
            cap, host.read { host.mapper.imageResults.size })
        assertEquals("…and nothing is left in flight", 0,
            host.read { host.mapper.inFlightImageCount })
    }

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
