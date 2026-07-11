package io.blazornative.jni

/**
 * Phase 4.4 Gate 1 — the inspector's hand-rolled JSON helpers (no kotlinx —
 * that dependency died in 3.0e and stays dead; design decision restated in
 * docs/plans/2026-07-11-phase-4.4-design.md).
 *
 * WRITER: [string]/[appendString] delegate to [FlatJson.appendJsonString] —
 * the Kotlin twin of NativeShellBridge.AppendJsonString — so the inspector
 * shares the frozen escaping contract instead of growing a drifting copy:
 * quote, backslash, \n \r \t as short escapes; every other char below U+0020
 * as a lowercase 4-hex-digit unicode escape; everything else (incl. non-ASCII
 * and surrogate pairs) passes through raw. Pinned in InspectorJsonTest.
 *
 * PARSER: [parseFlatObject] reads the POST /api/dispatch body — a single FLAT
 * JSON object. It is deliberately NOT [FlatJson.parse]: that parser is the
 * headers contract (string-only values, case-INsensitive RFC-9110 key map),
 * while the dispatch body carries a numeric handlerId and its keys are
 * case-SENSITIVE. Values may be strings (standard short escapes + strict
 * \uXXXX) or scalar literals — numbers and true/false are captured as their
 * literal text; null means "key absent" (so `"payload":null` equals omitting
 * the key, matching dispatchEvent's omitted-payload convention). Nested
 * objects/arrays are malformed by definition (flat only). Malformed input
 * throws [IllegalArgumentException] — the server's 400.
 *
 * PUBLIC (unlike [FlatJson]) because InspectorServer consumes it from the
 * jvmHost compilation — a separate Kotlin module without friend access.
 */
object InspectorJson {

    fun appendString(sb: StringBuilder, value: String) = FlatJson.appendJsonString(sb, value)

    /** The quoted, escaped JSON string literal for [value]. */
    fun string(value: String): String =
        StringBuilder(value.length + 2).also { appendString(it, value) }.toString()

    /** Parses one flat JSON object into key → value-text (see class KDoc). */
    fun parseFlatObject(json: String): Map<String, String> {
        val result = LinkedHashMap<String, String>()
        val p = Parser(json)
        p.parseInto(result)
        return result
    }

    private class Parser(private val json: String) {
        private var i = 0

        fun parseInto(result: MutableMap<String, String>) {
            skipWhitespace()
            expect('{')
            skipWhitespace()
            if (i < json.length && json[i] == '}') { i++; return }
            while (true) {
                skipWhitespace()
                val key = parseString()
                skipWhitespace()
                expect(':')
                skipWhitespace()
                parseValue(key, result)
                skipWhitespace()
                if (i >= json.length) throw malformed(i)
                val c = json[i++]
                if (c == '}') return
                if (c != ',') throw malformed(i - 1)
            }
        }

        /** String → decoded; number/true/false → literal text; null → key absent. */
        private fun parseValue(key: String, result: MutableMap<String, String>) {
            if (i >= json.length) throw malformed(i)
            when (json[i]) {
                '"' -> result[key] = parseString()
                't' -> { expectWord("true"); result[key] = "true" }
                'f' -> { expectWord("false"); result[key] = "false" }
                'n' -> expectWord("null") // absent-key convention
                else -> result[key] = parseNumber()
            }
        }

        private fun parseNumber(): String {
            val start = i
            if (i < json.length && json[i] == '-') i++
            while (i < json.length && (json[i].isDigit() || json[i] == '.' || json[i] == 'e' || json[i] == 'E' || json[i] == '+' || json[i] == '-')) i++
            if (i == start || !json[start].let { it == '-' || it.isDigit() }) throw malformed(start)
            if (i == start + 1 && json[start] == '-') throw malformed(start)
            return json.substring(start, i)
        }

        private fun expectWord(word: String) {
            if (!json.startsWith(word, i)) throw malformed(i)
            i += word.length
        }

        private fun parseString(): String {
            expect('"')
            val sb = StringBuilder()
            while (true) {
                if (i >= json.length) throw malformed(i)
                val c = json[i++]
                if (c == '"') return sb.toString()
                if (c != '\\') {
                    sb.append(c)
                    continue
                }
                if (i >= json.length) throw malformed(i)
                when (json[i++]) {
                    '"' -> sb.append('"')
                    '\\' -> sb.append('\\')
                    '/' -> sb.append('/')
                    'n' -> sb.append('\n')
                    'r' -> sb.append('\r')
                    't' -> sb.append('\t')
                    'b' -> sb.append('\b')
                    'f' -> sb.append(12.toChar()) // form feed — Kotlin has no \f escape
                    'u' -> sb.append(parseHex4())
                    else -> throw malformed(i - 1)
                }
            }
        }

        /** Strict 4-hex-digit run (the FlatJson.Parser strictness note). */
        private fun parseHex4(): Char {
            if (i + 4 > json.length) throw malformed(i)
            var value = 0
            for (k in 0 until 4) {
                val c = json[i + k]
                val digit = when (c) {
                    in '0'..'9' -> c - '0'
                    in 'a'..'f' -> c - 'a' + 10
                    in 'A'..'F' -> c - 'A' + 10
                    else -> throw malformed(i + k)
                }
                value = (value shl 4) or digit
            }
            i += 4
            return value.toChar()
        }

        private fun skipWhitespace() {
            while (i < json.length && json[i].isWhitespace()) i++
        }

        private fun expect(expected: Char) {
            if (i >= json.length || json[i] != expected) throw malformed(i)
            i++
        }

        /** Truncated prefix only — a dispatch payload may carry user text
         * that shouldn't be echoed wholesale into error responses/logs. */
        private fun malformed(index: Int): IllegalArgumentException {
            val prefix = if (json.length <= 32) json else json.substring(0, 32) + "…"
            return IllegalArgumentException(
                "malformed dispatch JSON at index $index (prefix: '$prefix')"
            )
        }
    }
}
