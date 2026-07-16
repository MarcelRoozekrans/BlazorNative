package io.blazornative.shell

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.DisplayName
import org.junit.jupiter.api.Test

/**
 * Phase 7.5 — **THE CONTENT-MODE WIRE TABLE, PINNED ON THE JVM LANE** (design §Gates, Gate 2:
 * "pure decision tables (mode parse, dispatch-liveness) on the JVM lane").
 *
 * Four strict lowercase words, a null-restores-default row, and a diagnose-don't-apply row —
 * written down one per test so a swapped table row reddens BY NAME before any emulator boots
 * (the design's own JVM mutation: "swap `cover`↔`contain` in the pure mode table → red").
 * `WidgetMapperImagePolishTest` pins the per-word `ScaleType` spelling on the device; this
 * file pins the ACCEPTANCE SET, which is the half two shells must share verbatim.
 */
@DisplayName("the contentMode wire table: four strict words, null restores the default, unknown is not applied")
class ImageContentModeTableTest {

    @Test
    @DisplayName("'contain' → CONTAIN — the default, spelled out (FIT_CENTER / .scaleAspectFit)")
    fun contain_maps_to_CONTAIN() {
        assertEquals(ImageContentMode.CONTAIN, contentModeFor("contain"),
            "the wire word 'contain' is aspect-fit — the 6.3 default, now named")
    }

    @Test
    @DisplayName("'cover' → COVER (CENTER_CROP / .scaleAspectFill)")
    fun cover_maps_to_COVER() {
        assertEquals(ImageContentMode.COVER, contentModeFor("cover"),
            "the wire word 'cover' is aspect-fill — RN's default, NOT ours (the recorded " +
                "decision); a swap with 'contain' silently repaints every declared box " +
                "frame-neutrally, which is why this row has its own test")
    }

    @Test
    @DisplayName("'stretch' → STRETCH (FIT_XY / .scaleToFill)")
    fun stretch_maps_to_STRETCH() {
        assertEquals(ImageContentMode.STRETCH, contentModeFor("stretch"),
            "the wire word 'stretch' distorts to the box — UIImageView's framework default, " +
                "which is exactly why the mode is explicit on both shells")
    }

    @Test
    @DisplayName("'center' → CENTER (CENTER / .center)")
    fun center_maps_to_CENTER() {
        assertEquals(ImageContentMode.CENTER, contentModeFor("center"),
            "the wire word 'center' paints the natural size, unscaled, centered — and can " +
                "paint BIGGER than the box, which is the clipsToBounds corollary's reason")
    }

    @Test
    @DisplayName("null (the prop was REMOVED) restores the DEFAULT: CONTAIN — the Enabled-null precedent")
    fun null_restores_the_default_CONTAIN() {
        assertEquals(ImageContentMode.CONTAIN, contentModeFor(null),
            "null on the prop wire means 'the author took the parameter away' — what it " +
                "restores is the default, and the default is contain (the 6.3 row's value, " +
                "deliberately not RN's cover)")
    }

    @Test
    @DisplayName("an unknown word → null: DIAGNOSE, DON'T APPLY — and the grammar is STRICT (case, whitespace, emptiness)")
    fun unknown_words_are_diagnosed_not_applied() {
        val why = "an unknown wire word must map to NOTHING — the caller diagnoses loudly and " +
            "keeps the node's current mode (the modal style-ignore precedent). A lenient " +
            "accept here is a value one shell honours and the other ignores."
        assertNull(contentModeFor("Contain"), "$why ('Contain' — the grammar is lowercase, " +
            "exactly what ImageContentMode.ToWireValue writes)")
        assertNull(contentModeFor("COVER"), "$why ('COVER' — case-sensitive, ordinal)")
        assertNull(contentModeFor(" contain"), "$why (' contain' — no trimming: the whole " +
            "token must be consumed, the parseWireFloat anchoring discipline)")
        assertNull(contentModeFor(""), "$why ('' — an empty string names no mode)")
        assertNull(contentModeFor("fit"), "$why ('fit' — a plausible synonym is still not a " +
            "wire word; synonyms are how two shells drift)")
        assertNull(contentModeFor("repeat"), "$why ('repeat' — RN's fifth mode, deliberately " +
            "out of scope: ledger-on-request, not a silent alias)")
    }
}
