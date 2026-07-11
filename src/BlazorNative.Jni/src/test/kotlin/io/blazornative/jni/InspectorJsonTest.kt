package io.blazornative.jni

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.assertThrows

/**
 * Phase 4.4 Gate 1 — the inspector's hand-rolled JSON surface (no kotlinx —
 * that dependency died in 3.0e and stays dead), pure model-level TDD:
 *
 *  1. [InspectorJson.string] escaping pins — the exact FlatJson posture
 *     (the Kotlin twin of NativeShellBridge.AppendJsonString, shared via
 *     [FlatJson]): quote, backslash, \n \r \t as short escapes; every other
 *     char below U+0020 as a lowercase 4-hex-digit unicode escape; everything
 *     else (incl. non-ASCII + surrogate pairs) passes through raw.
 *  2. [InspectorJson.parseFlatObject] — the POST /api/dispatch body parser:
 *     a single flat JSON object whose values are strings OR scalar literals
 *     (numbers/true/false; null = key absent). Case-SENSITIVE keys (unlike
 *     FlatJson.parse's RFC-9110 header map). Malformed → IllegalArgumentException
 *     (the server's 400).
 *  3. [TreeSnapshot.renderJson] shape pins via synthetic frames (the
 *     TreeSnapshotTest patterns): stable key order id/type/text/props/styles/
 *     events/children; text omitted when null, props/styles/events/children
 *     OMITTED when empty (the render()'s omit-empty posture, pinned here);
 *     null prop/style values render as JSON null; handler ids are numbers.
 */
class InspectorJsonTest {

    // ── 1. Escaping pins ─────────────────────────────────────────────────────

    @Test
    fun plain_text_is_quoted_unchanged() {
        assertEquals("\"hello\"", InspectorJson.string("hello"))
    }

    @Test
    fun quote_and_backslash_get_short_escapes() {
        assertEquals("\"say \\\"hi\\\"\"", InspectorJson.string("say \"hi\""))
        assertEquals("\"C:\\\\tmp\"", InspectorJson.string("C:\\tmp"))
    }

    @Test
    fun newline_return_tab_get_short_escapes() {
        assertEquals("\"a\\nb\\rc\\td\"", InspectorJson.string("a\nb\rc\td"))
    }

    @Test
    fun other_control_chars_become_lowercase_u00xx() {
        assertEquals("\"\\u0000\\u0001\\u001f\"", InspectorJson.string("\u0000\u0001\u001F"))
        // \b and \f deliberately take the unicode arm (writer emits only the
        // five short escapes — the FlatJson writer contract).
        assertEquals("\"\\u0008\\u000c\"", InspectorJson.string("\b\u000C"))
    }

    @Test
    fun non_ascii_passes_through_raw() {
        assertEquals("\"héllo→世界\"", InspectorJson.string("héllo→世界"))
        // Surrogate pair (emoji) passes through as-is.
        assertEquals("\"😀\"", InspectorJson.string("😀"))
    }

    // ── 2. parseFlatObject (the dispatch body) ───────────────────────────────

    @Test
    fun parses_numbers_and_strings_case_sensitively() {
        val map = InspectorJson.parseFlatObject("""{"handlerId":42,"eventName":"click"}""")
        assertEquals("42", map["handlerId"])
        assertEquals("click", map["eventName"])
        assertEquals(null, map["HANDLERID"], "dispatch body keys are case-sensitive")
    }

    @Test
    fun parses_string_escapes_and_whitespace() {
        val map = InspectorJson.parseFlatObject(
            "{ \"payload\" : \"a\\\"b\\\\c\\nd\\u0041\" , \"eventName\" : \"change\" }"
        )
        assertEquals("a\"b\\c\ndA", map["payload"])
        assertEquals("change", map["eventName"])
    }

    @Test
    fun negative_numbers_and_booleans_are_captured_as_literals() {
        val map = InspectorJson.parseFlatObject("""{"handlerId":-7,"flag":true,"other":false}""")
        assertEquals("-7", map["handlerId"])
        assertEquals("true", map["flag"])
        assertEquals("false", map["other"])
    }

    @Test
    fun null_value_means_key_absent() {
        val map = InspectorJson.parseFlatObject("""{"handlerId":1,"payload":null}""")
        assertEquals("1", map["handlerId"])
        assertEquals(false, map.containsKey("payload"), "JSON null must map to an absent key")
    }

    @Test
    fun empty_object_parses_to_empty_map() {
        assertEquals(emptyMap<String, String>(), InspectorJson.parseFlatObject("{}"))
    }

    @Test
    fun malformed_input_throws() {
        for (bad in listOf(
            "",                                  // no object at all
            "not json",                          // bare word
            "{\"a\":}",                          // missing value
            "{\"a\":\"unterminated",             // unterminated string
            "{\"a\":\"x\\q\"}",                  // bad escape
            "{\"a\":\"x\"",                      // missing closing brace
            "[1,2]",                             // not an object
            "{\"a\":{\"nested\":1}}",            // nested object — flat only
        )) {
            assertThrows<IllegalArgumentException>("expected IAE for: $bad") {
                InspectorJson.parseFlatObject(bad)
            }
        }
    }

    // ── 3. TreeSnapshot → JSON shape ─────────────────────────────────────────

    private var nextFrameId = 0

    /** Wraps patches in a [RenderFrame] with the CommitFrame boundary the
     * encoder always appends (the TreeSnapshotTest helper). */
    private fun frame(vararg patches: RenderPatch): RenderFrame {
        val id = ++nextFrameId
        return RenderFrame(
            frameId = id,
            timestampMs = id * 100L,
            patches = patches.toList() + RenderPatch.CommitFrame(frameId = id, timestampMs = id * 100L),
        )
    }

    private fun create(id: Int, type: String, parent: Int? = null, at: Int = -1) =
        RenderPatch.CreateNode(nodeId = id, nodeType = type, parentId = parent, insertIndex = at)

    @Test
    fun empty_snapshot_renders_empty_array() {
        assertEquals("[]", TreeSnapshot().renderJson())
    }

    @Test
    fun bndemo_shaped_frame_renders_stable_key_order_and_omits_empty_segments() {
        val snap = TreeSnapshot()
        snap.apply(
            frame(
                create(1, "view"),
                RenderPatch.SetStyle(1, "backgroundColor", "#FFEEAA"),
                create(2, "text", parent = 1),
                RenderPatch.ReplaceText(2, "Title"),
                create(3, "input", parent = 1),
                RenderPatch.UpdateProp(3, "value", ""),
                RenderPatch.AttachEvent(3, "change", handlerId = 11),
                create(4, "button", parent = 1),
                create(5, "text", parent = 4),          // collapses onto the button
                RenderPatch.ReplaceText(5, "Clear"),
                RenderPatch.AttachEvent(4, "click", handlerId = 12),
            )
        )
        assertEquals(
            "[{\"id\":1,\"type\":\"view\",\"styles\":{\"backgroundColor\":\"#FFEEAA\"},\"children\":[" +
                "{\"id\":2,\"type\":\"text\",\"text\":\"Title\"}," +
                "{\"id\":3,\"type\":\"input\",\"props\":{\"value\":\"\"},\"events\":{\"change\":11}}," +
                "{\"id\":4,\"type\":\"button\",\"text\":\"Clear\",\"events\":{\"click\":12}}" +
                "]}]",
            snap.renderJson()
        )
    }

    @Test
    fun null_prop_and_style_values_render_as_json_null() {
        val snap = TreeSnapshot()
        snap.apply(
            frame(
                create(1, "input"),
                RenderPatch.UpdateProp(1, "enabled", null),
                RenderPatch.SetStyle(1, "padding", null),
            )
        )
        assertEquals(
            "[{\"id\":1,\"type\":\"input\",\"props\":{\"enabled\":null},\"styles\":{\"padding\":null}}]",
            snap.renderJson()
        )
    }

    @Test
    fun text_and_prop_content_is_escaped() {
        val snap = TreeSnapshot()
        snap.apply(
            frame(
                create(1, "view"),
                create(2, "text", parent = 1),
                RenderPatch.ReplaceText(2, "say \"hi\"\nnow"),
                RenderPatch.UpdateProp(2, "no\"te", "a\\b"),
            )
        )
        assertEquals(
            "[{\"id\":1,\"type\":\"view\",\"children\":[" +
                "{\"id\":2,\"type\":\"text\",\"text\":\"say \\\"hi\\\"\\nnow\",\"props\":{\"no\\\"te\":\"a\\\\b\"}}" +
                "]}]",
            snap.renderJson()
        )
    }

    @Test
    fun multiple_roots_render_as_array_siblings() {
        val snap = TreeSnapshot()
        snap.apply(frame(create(1, "view"), create(9, "text", parent = 42))) // unknown parent → root bucket
        assertEquals(
            "[{\"id\":1,\"type\":\"view\"},{\"id\":9,\"type\":\"text\"}]",
            snap.renderJson()
        )
    }
}
