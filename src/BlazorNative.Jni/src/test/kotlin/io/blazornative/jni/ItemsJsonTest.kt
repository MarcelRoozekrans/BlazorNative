package io.blazornative.jni

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertThrows
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/**
 * Phase 7.3 Gate 2 — the STRICT items-array parser, against the normative
 * grammar (`BnItemsJson.cs`'s header — this file transcribes ITS vectors, plus
 * the Gate 1 review's normative MALFORMED list; `BnItemsJsonTests.cs` pins the
 * same acceptance set from the writer's side).
 *
 * The strictness is the contract: the acceptance set is EXACTLY what
 * `BnItemsJson.Write` emits. [ItemsJson]'s KDoc records why `FlatJson.parse`'s
 * lenient reader (whitespace, `\b`, `\/`, uppercase hex) was deliberately NOT
 * reused — every leniency it has is an input one shell could accept and the
 * other reject, i.e. a two-shell drift on wire data the .NET writer never
 * produces.
 *
 * Escape-sequence vectors are BUILT (via [bs]/`toChar`), not written literally:
 * a control character or a decodable escape sitting raw in a source file is
 * invisible to review — the same reason the .NET twin builds its torture
 * strings in code.
 */
class ItemsJsonTest {

    /** One backslash — so no vector below is itself an escape sequence in this
     * source file. */
    private val bs = '\\'

    // ── Acceptance: what the writer emits parses back, exactly ───────────────

    @Test
    fun parses_the_demo_literal() {
        // THE wire literal every Gate 2/3 surface pins (BnFormDemoTests.ItemsJson).
        assertEquals(
            listOf("Alpha", "Bravo", "Charlie"),
            ItemsJson.parse("""["Alpha","Bravo","Charlie"]"""))
    }

    @Test
    fun the_empty_list_is_exactly_two_characters() {
        assertTrue(ItemsJson.parse("[]").isEmpty())
    }

    @Test
    fun the_escaping_matrix_round_trips() {
        // The writer's matrix, item by item: the five short escapes, then raw
        // pass-through (non-ASCII + an emoji surrogate pair + a comma — items
        // are OPAQUE display strings; the grammar carries them verbatim).
        assertEquals(
            listOf("say \"hi\"", "back\\slash", "a\nb", "c\rd", "e\tf", "héllo→世界 🎉", "a,b"),
            ItemsJson.parse(
                """["say \"hi\"","back\\slash","a\nb","c\rd","e\tf","héllo→世界 🎉","a,b"]"""))
        // …and the matrix's one non-short escape, built rather than written
        // literally: a backslash-u-escaped control (the writer's ONLY backslash-u
        // case) — lowercase, 4 digits, < U+0020.
        assertEquals(listOf("be" + 7.toChar() + "ll"),
            ItemsJson.parse("[\"be${bs}u0007ll\"]"))
        // The empty STRING is legal item content (the grammar's null is a throw
        // .NET-side; "" is its documented stand-in).
        assertEquals(listOf("", ""), ItemsJson.parse("""["",""]"""))
    }

    @Test
    fun the_control_boundary_u001f_escapes_and_u007f_rides_raw() {
        // BnItemsJsonTests' boundary rows, mirrored: U+001F is the LAST char the
        // writer escapes; U+007F (DEL) rides RAW — "control" in this grammar
        // means < U+0020, nothing else.
        assertEquals(listOf("a" + 0x1F.toChar() + "b"),
            ItemsJson.parse("[\"a${bs}u001fb\"]"))
        assertEquals(listOf("c" + 0x7F.toChar() + "d"),
            ItemsJson.parse("[\"c" + 0x7F.toChar() + "d\"]"))
    }

    // ── Rejection: the normative malformed vectors ───────────────────────────

    @Test
    fun rejects_every_normative_malformed_vector() {
        val malformed = listOf(
            // The Gate 1 review's list, verbatim:
            """[ "a"]""",        // whitespace between tokens — the writer emits none
            """["a",]""",        // trailing comma
            """["a"]x""",        // trailing garbage — whole-string consumption
            """['a']""",         // single quotes
            """["\b"]""",        // \b: FlatJson's reader takes it; this grammar does not
            """["a""",           // unterminated string
            // …and the rest of the strict acceptance boundary:
            "",                  // empty input is not an items array ([] is)
            """["a"""",          // unterminated array
            """[a]""",           // unquoted item
            """["a" ,"b"]""",    // interior whitespace
            """["\/"]""",        // \/: lenient-JSON escape, not in the matrix
            """["\f"]""",        // \f: same
            "[\"${bs}u00AB\"]",  // uppercase hex — the writer emits lowercase
            "[\"${bs}u001F\"]",  // uppercase hex of a CONTROL (.NET's verbatim vector — S1-2): lowercase
                                 // u001f is ACCEPTED above, so casing is the ONLY reason this rejects;
                                 // the u00AB row also trips the non-control rule and cannot catch a
                                 // parser mutated to take uppercase hex
            "[\"${bs}u0041\"]",  // well-formed backslash-u of a NON-control — the writer never u-escapes those
            "[\"${bs}u000a\"]",  // the LONG spelling of the newline short escape — one canonical spelling per input
            "[\"${bs}u00\"]",    // truncated backslash-u escape
            "[\"a" + 1.toChar() + "b\"]", // a RAW control inside a string — the writer always escapes them
            """{"a":"b"}""",     // an OBJECT — the dispatch-args shape, not this grammar
            """"a"""",           // a bare string is not an items array
        )
        for (json in malformed) {
            assertThrows(IllegalArgumentException::class.java, { ItemsJson.parse(json) },
                "should reject: $json")
        }
    }

    @Test
    fun the_error_carries_the_index_and_a_bounded_prefix_only() {
        val long = "[\"" + "x".repeat(64) // unterminated, 66 chars
        val ex = assertThrows(IllegalArgumentException::class.java) { ItemsJson.parse(long) }
        assertTrue(ex.message!!.contains("index"), "message should carry the failing index")
        assertTrue(ex.message!!.length < long.length + 64,
            "message must carry a bounded prefix, not the whole payload")
    }
}
