package io.blazornative.jni

/**
 * Phase 7.3 — the `items` prop's STRICT flat-JSON string-array parser (the
 * picker's item list; design decision 3, the state-owner precedent).
 *
 * THE GRAMMAR IS NORMATIVE and lives in ONE place —
 * `src/BlazorNative.Components/BnItemsJson.cs`'s header — and this parser is
 * written FROM it (the 6.3 strict-parse discipline):
 *
 *   items   := '[' ( string ( ',' string )* )? ']'
 *   string  := '"' char* '"'
 *   char    := any Unicode scalar EXCEPT '"' '\' and controls < U+0020,
 *              passed through as raw UTF-8
 *            | '\"' | '\\' | '\n' | '\r' | '\t'
 *            | '\u00XX' — any OTHER control < U+0020, lowercase hex, 4 digits
 *
 * **DELIBERATELY NOT [FlatJson.parse]/its `parseString`** (the Gate 1 review's
 * correction): that parser is object-only AND LENIENT — it skips whitespace and
 * accepts `\b`, `\f`, `\/` and UPPERCASE hex, escapes this grammar does not
 * define. The dispatch-args wire tolerates them (a permissive READER of its own
 * writer); the items wire is a TWO-SHELL parse contract whose acceptance set
 * must be EXACTLY what `BnItemsJson.Write` emits, or the two hand-written
 * shell parsers drift apart on inputs the .NET writer never produces. So:
 *
 *   - NO whitespace between tokens (`[ "a"]` is malformed).
 *   - NO trailing comma (`["a",]`), NO single quotes (`['a']`), NO unquoted
 *     items, NO trailing garbage (`["a"]x`) — WHOLE-STRING consumption.
 *   - Escapes are EXACTLY the writer's matrix: `\"` `\\` `\n` `\r` `\t`, and
 *     `\uXXXX` with four LOWERCASE hex digits decoding to a control < U+0020
 *     (the only thing the writer ever `\u`-escapes). `\b`, `\/`, uppercase
 *     hex, `A`: all malformed.
 *   - A RAW control < U+0020 inside a string is malformed (the writer always
 *     escapes them).
 *
 * The escaping MATRIX itself is still the one flat-JSON matrix every shell
 * already implements (`FlatJson.appendJsonString` / Swift `BnFlatJson` / .NET
 * `WriteFlatJsonObject`) — no new escaping rules; only the ACCEPTANCE is
 * strict where FlatJson's reader is lenient.
 *
 * Malformed → [IllegalArgumentException]; the CALLER (WidgetMapper's `items`
 * arm) logs loudly and renders an EMPTY picker rather than a wrong one — the
 * grammar's own posture. The error message carries the failing index and a
 * 32-char prefix (the FlatJson message discipline).
 *
 * Lives in the shared source set (not androidMain) so the JVM lane unit-tests
 * the acceptance set directly ([ItemsJsonTest]) — the same reason
 * `isLiveImageRequest` is a pure function.
 */
internal object ItemsJson {

    /** Parses [json] as the normative items array. Throws on ANYTHING the
     * grammar does not produce. */
    fun parse(json: String): List<String> = Parser(json).parseWholeArray()

    private class Parser(private val json: String) {
        private var i = 0

        fun parseWholeArray(): List<String> {
            val result = mutableListOf<String>()
            expect('[')
            if (peek() == ']') {
                i++
            } else {
                while (true) {
                    result.add(parseString())
                    when (val c = next()) {
                        ']' -> break
                        ',' -> Unit // the next string is REQUIRED (no trailing comma)
                        else -> throw malformed(i - 1, "expected ',' or ']', got '$c'")
                    }
                }
            }
            // WHOLE-STRING consumption: trailing garbage is malformed, not ignored.
            if (i != json.length) throw malformed(i, "trailing garbage after ']'")
            return result
        }

        private fun parseString(): String {
            expect('"')
            val sb = StringBuilder()
            while (true) {
                when (val c = next()) {
                    '"' -> return sb.toString()
                    '\\' -> when (val e = next()) {
                        '"' -> sb.append('"')
                        '\\' -> sb.append('\\')
                        'n' -> sb.append('\n')
                        'r' -> sb.append('\r')
                        't' -> sb.append('\t')
                        'u' -> sb.append(parseControlHex4())
                        // `\b`, `\f`, `\/`, `\A`, …: NOT in the grammar's char
                        // production — the writer never emits them.
                        else -> throw malformed(i - 1, "escape '\\$e' is not in the items grammar")
                    }
                    else -> {
                        // The writer escapes EVERY control; a raw one is malformed.
                        if (c < ' ') throw malformed(i - 1, "raw control U+%04X".format(c.code))
                        sb.append(c)
                    }
                }
            }
        }

        /** `\uXXXX`: four LOWERCASE hex digits, and the value must be a control
         * the writer actually `\u`-escapes: < U+0020 and NOT one of the three
         * short-escape controls (`\n` `\r` `\t` — the writer spells those
         * short, so their long `\u` spelling is outside the acceptance set:
         * ONE canonical spelling per input — `BnItemsJsonTests`' normative
         * long-spelling-of-newline vector). U+007F is NOT a control to this
         * grammar — "control" means < U+0020, nothing else (the boundary rows). */
        private fun parseControlHex4(): Char {
            if (i + 4 > json.length) throw malformed(i, "truncated \\u escape")
            var value = 0
            for (k in 0 until 4) {
                val c = json[i + k]
                val digit = when (c) {
                    in '0'..'9' -> c - '0'
                    in 'a'..'f' -> c - 'a' + 10
                    // Uppercase hex: the writer emits lowercase ("x4"), so it is
                    // outside the acceptance set — one canonical spelling per input.
                    else -> throw malformed(i + k, "'\\u' escape requires lowercase hex")
                }
                value = (value shl 4) or digit
            }
            if (value >= 0x20) {
                throw malformed(i, "'\\u%04x' is not a control — the grammar only \\u-escapes controls < U+0020".format(value))
            }
            if (value == 0x09 || value == 0x0A || value == 0x0D) {
                throw malformed(i, "'\\u%04x' is the long spelling of a short escape — the writer emits the short form".format(value))
            }
            i += 4
            return value.toChar()
        }

        private fun peek(): Char {
            if (i >= json.length) throw malformed(i, "unexpected end of input")
            return json[i]
        }

        private fun next(): Char {
            if (i >= json.length) throw malformed(i, "unexpected end of input")
            return json[i++]
        }

        private fun expect(expected: Char) {
            if (i >= json.length || json[i] != expected) {
                throw malformed(i, "expected '$expected'")
            }
            i++
        }

        private fun malformed(index: Int, why: String): IllegalArgumentException {
            val prefix = if (json.length <= 32) json else json.substring(0, 32) + "…"
            return IllegalArgumentException(
                "malformed items array at index $index ($why) (prefix: '$prefix')"
            )
        }
    }
}
