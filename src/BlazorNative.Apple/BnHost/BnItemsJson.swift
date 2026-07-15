// ─────────────────────────────────────────────────────────────────────────────
// BnItemsJson — Phase 7.3: the `items` prop's STRICT flat-JSON string-array
// parser (the picker's item list; design decision 3, the state-owner precedent).
// The Swift transliteration of Kotlin's `ItemsJson`, both written FROM the same
// normative comment.
//
// THE GRAMMAR IS NORMATIVE and lives in ONE place —
// `src/BlazorNative.Components/BnItemsJson.cs`'s header — and this parser is
// written from it (the 6.3 strict-parse discipline; the executable reference is
// `tests/BlazorNative.Runtime.Tests/BnItemsStrictParser.cs`):
//
//   items   := '[' ( string ( ',' string )* )? ']'
//   string  := '"' char* '"'
//   char    := any Unicode scalar EXCEPT '"' '\' and controls < U+0020,
//              passed through as raw UTF-8
//            | '\"' | '\\' | '\n' | '\r' | '\t'
//            | '\u00XX' — any OTHER control < U+0020, lowercase hex, 4 digits
//
// **DELIBERATELY NOT [BnFlatJson]'s reader** (the Gate 1 review's correction,
// inherited from Gate 2): that parser is object-only AND LENIENT — it tolerates
// escapes and whitespace this grammar does not define. The dispatch-args wire is
// a permissive READER of its own writer; the items wire is a TWO-SHELL parse
// contract whose acceptance set must be EXACTLY what `BnItemsJson.Write` emits,
// or the two hand-written shell parsers drift apart on inputs the .NET writer
// never produces. So:
//
//   - NO whitespace between tokens (`[ "a"]` is malformed).
//   - NO trailing comma (`["a",]`), NO single quotes (`['a']`), NO unquoted
//     items, NO trailing garbage (`["a"]x`) — WHOLE-STRING consumption.
//   - Escapes are EXACTLY the writer's matrix: `\"` `\\` `\n` `\r` `\t`, and
//     `\uXXXX` with four LOWERCASE hex digits decoding to a control < U+0020
//     (the only thing the writer ever `\u`-escapes). `\b`, `\/`, uppercase hex:
//     all malformed.
//   - A RAW control < U+0020 inside a string is malformed (the writer always
//     escapes them). U+007F is NOT a control to this grammar — "control" means
//     < U+0020, nothing else (the boundary rows).
//
// The escaping MATRIX itself is still the one flat-JSON matrix every shell
// already implements (`BnFlatJson.appendEscaped` / Kotlin `appendJsonString` /
// .NET `WriteFlatJsonObject`) — no new escaping rules; only the ACCEPTANCE is
// strict where the dispatch-args reader is lenient.
//
// Malformed → [BnItemsJsonError]; the CALLER (BnWidgetMapper's `items` arm)
// logs loudly and renders an EMPTY picker rather than a wrong one — the
// grammar's own posture. The error carries the failing index and a 32-char
// prefix (the FlatJson message discipline).
//
// Pure Swift, no UIKit — unit-tested directly (`BnItemsJsonTests`), the same
// reason `bnIsLiveImageRequest` is a pure function.
// ─────────────────────────────────────────────────────────────────────────────

import Foundation

/// Thrown on ANYTHING the grammar does not produce.
struct BnItemsJsonError: Error, CustomStringConvertible {
    let index: Int
    let why: String
    let prefix: String
    var description: String {
        "malformed items array at index \(index) (\(why)) (prefix: '\(prefix)')"
    }
}

enum BnItemsJson {

    /// Parses [json] as the normative items array. Throws on ANY deviation
    /// from the writer's image (see the file header for the strictness list).
    static func parse(_ json: String) throws -> [String] {
        var parser = Parser(json)
        return try parser.parseWholeArray()
    }

    /// Operates on UTF-16 code units — the same unit the Kotlin (`Char`) and
    /// .NET (`char`) references index by, so "the failing index" means the
    /// same offset in all three error messages. Raw pass-through appends
    /// code units verbatim (surrogate halves included — the reference parsers
    /// carry them the same way, and a well-formed writer image always pairs
    /// them back up).
    private struct Parser {
        private let units: [UInt16]
        private let json: String
        private var i = 0

        init(_ json: String) {
            self.json = json
            self.units = Array(json.utf16)
        }

        mutating func parseWholeArray() throws -> [String] {
            var result: [String] = []
            try expect("[")
            if try peek() == unit("]") {
                i += 1
            } else {
                while true {
                    result.append(try parseString())
                    let c = try next()
                    if c == unit("]") { break }
                    if c != unit(",") {
                        throw malformed(i - 1, "expected ',' or ']' — the grammar has no whitespace")
                    }
                    // the next string is REQUIRED (no trailing comma)
                }
            }
            // WHOLE-STRING consumption: trailing garbage is malformed, not ignored.
            if i != units.count { throw malformed(i, "trailing garbage after ']'") }
            return result
        }

        private mutating func parseString() throws -> String {
            try expect("\"")
            var out: [UInt16] = []
            while true {
                let c = try next()
                if c == unit("\"") {
                    return String(utf16CodeUnits: out, count: out.count)
                }
                if c == unit("\\") {
                    let e = try next()
                    switch e {
                    case unit("\""): out.append(unit("\""))
                    case unit("\\"): out.append(unit("\\"))
                    case unit("n"): out.append(0x0A)
                    case unit("r"): out.append(0x0D)
                    case unit("t"): out.append(0x09)
                    case unit("u"): out.append(try parseControlHex4())
                    // `\b`, `\f`, `\/`, `\A`, …: NOT in the grammar's char
                    // production — the writer never emits them.
                    default:
                        throw malformed(i - 1, "escape '\\\(scalar(e))' is not in the items grammar")
                    }
                } else if c < 0x20 {
                    // The writer escapes EVERY control; a raw one is malformed.
                    throw malformed(i - 1, String(format: "raw control U+%04X", c))
                } else {
                    out.append(c)
                }
            }
        }

        /// `\uXXXX`: four LOWERCASE hex digits, and the value must be a control
        /// the writer actually `\u`-escapes: < U+0020 and NOT one of the three
        /// short-escape controls (`\n` `\r` `\t` — the writer spells those
        /// short, so their long `\u` spelling is outside the acceptance set:
        /// ONE canonical spelling per input — `BnItemsJsonTests`' normative
        /// long-spelling-of-newline vector).
        private mutating func parseControlHex4() throws -> UInt16 {
            if i + 4 > units.count { throw malformed(i, "truncated \\u escape") }
            var value: UInt16 = 0
            for k in 0..<4 {
                let c = units[i + k]
                let digit: UInt16
                switch c {
                case unit("0")...unit("9"): digit = c - unit("0")
                case unit("a")...unit("f"): digit = c - unit("a") + 10
                // Uppercase hex: the writer emits lowercase ("x4"), so it is
                // outside the acceptance set — one canonical spelling per input.
                default:
                    throw malformed(i + k, "'\\u' escape requires lowercase hex")
                }
                value = (value << 4) | digit
            }
            if value >= 0x20 {
                throw malformed(i, String(format:
                    "'\\u%04x' is not a control — the grammar only \\u-escapes controls < U+0020", value))
            }
            if value == 0x09 || value == 0x0A || value == 0x0D {
                throw malformed(i, String(format:
                    "'\\u%04x' is the long spelling of a short escape — the writer emits the short form", value))
            }
            i += 4
            return value
        }

        private func peek() throws -> UInt16 {
            if i >= units.count { throw malformed(i, "unexpected end of input") }
            return units[i]
        }

        private mutating func next() throws -> UInt16 {
            if i >= units.count { throw malformed(i, "unexpected end of input") }
            defer { i += 1 }
            return units[i]
        }

        private mutating func expect(_ expected: Character) throws {
            if i >= units.count || units[i] != unit(expected) {
                throw malformed(i, "expected '\(expected)'")
            }
            i += 1
        }

        private func unit(_ c: Character) -> UInt16 { c.utf16.first! }

        private func scalar(_ u: UInt16) -> String {
            String(utf16CodeUnits: [u], count: 1)
        }

        private func malformed(_ index: Int, _ why: String) -> BnItemsJsonError {
            let prefix = json.count <= 32 ? json : String(json.prefix(32)) + "…"
            return BnItemsJsonError(index: index, why: why, prefix: prefix)
        }
    }
}
