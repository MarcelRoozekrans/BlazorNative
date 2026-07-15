// ─────────────────────────────────────────────────────────────────────────────
// BnItemsJsonTests — Phase 7.3 Gate 3: the STRICT items-array parser, against
// the normative grammar (`BnItemsJson.cs`'s header — this file transcribes ITS
// vectors, plus the Gate 1 review's normative MALFORMED list from
// `BnItemsJsonTests.cs`; Kotlin's `ItemsJsonTest` pins the same acceptance set
// on the JVM lane — three parsers, one acceptance set).
//
// The strictness is the contract: the acceptance set is EXACTLY what
// `BnItemsJson.Write` emits. `BnItemsJson.swift`'s header records why
// `BnFlatJson`'s lenient reader was deliberately NOT reused — every leniency it
// has is an input one shell could accept and the other reject, i.e. a two-shell
// drift on wire data the .NET writer never produces.
//
// Escape-sequence vectors are BUILT (via [bs]/UnicodeScalar), not written
// literally: a control character or a decodable escape sitting raw in a source
// file is invisible to review — the same reason the .NET twin builds its
// torture strings in code.
//
// Pure Swift (no runtime boot, no UIKit tree) — fast and deterministic.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
@testable import BnHost

final class BnItemsJsonTests: XCTestCase {

    /// One backslash — so no vector below is itself an escape sequence in this
    /// source file.
    private let bs = "\\"

    // ── Acceptance: what the writer emits parses back, exactly ───────────────

    func testParsesTheDemoLiteral() throws {
        // THE wire literal every Gate 2/3 surface pins (BnFormDemoTests.ItemsJson).
        XCTAssertEqual(try BnItemsJson.parse(#"["Alpha","Bravo","Charlie"]"#),
                       ["Alpha", "Bravo", "Charlie"])
    }

    func testTheEmptyListIsExactlyTwoCharacters() throws {
        XCTAssertTrue(try BnItemsJson.parse("[]").isEmpty)
    }

    func testTheEscapingMatrixRoundTrips() throws {
        // The writer's matrix, item by item: the five short escapes, then raw
        // pass-through (non-ASCII + an emoji surrogate pair + a comma — items
        // are OPAQUE display strings; the grammar carries them verbatim).
        XCTAssertEqual(
            try BnItemsJson.parse(
                #"["say \"hi\"","back\\slash","a\nb","c\rd","e\tf","héllo→世界 🎉","a,b"]"#),
            ["say \"hi\"", "back\\slash", "a\nb", "c\rd", "e\tf", "héllo→世界 🎉", "a,b"])
        // …and the matrix's one non-short escape, built rather than written
        // literally: a backslash-u-escaped control (the writer's ONLY
        // backslash-u case) — lowercase, 4 digits, < U+0020.
        XCTAssertEqual(try BnItemsJson.parse("[\"be\(bs)u0007ll\"]"),
                       ["be\u{07}ll"])
        // The empty STRING is legal item content (the grammar's null is a throw
        // .NET-side; "" is its documented stand-in).
        XCTAssertEqual(try BnItemsJson.parse(#"["",""]"#), ["", ""])
    }

    func testTheControlBoundaryU001fEscapesAndU007fRidesRaw() throws {
        // BnItemsJsonTests.cs's boundary rows, mirrored: U+001F is the LAST
        // char the writer escapes; U+007F (DEL) rides RAW — "control" in this
        // grammar means < U+0020, nothing else.
        XCTAssertEqual(try BnItemsJson.parse("[\"a\(bs)u001fb\"]"), ["a\u{1F}b"])
        XCTAssertEqual(try BnItemsJson.parse("[\"c\u{7F}d\"]"), ["c\u{7F}d"])
    }

    // ── Rejection: the normative malformed vectors ───────────────────────────

    func testRejectsEveryNormativeMalformedVector() {
        let malformed: [String] = [
            // The Gate 1 review's list, verbatim:
            #"[ "a"]"#,          // whitespace between tokens — the writer emits none
            #"["a",]"#,          // trailing comma
            #"["a"]x"#,          // trailing garbage — whole-string consumption
            #"['a']"#,           // single quotes
            "[\"\(bs)b\"]",      // \b: the lenient reader takes it; this grammar does not
            #"["a"#,             // unterminated string
            // …and the rest of the strict acceptance boundary:
            "",                  // empty input is not an items array ([] is)
            #"["a""#,            // unterminated array
            #"[a]"#,             // unquoted item
            #"["a" ,"b"]"#,      // interior whitespace
            "[\"\(bs)/\"]",      // \/: lenient-JSON escape, not in the matrix
            "[\"\(bs)f\"]",      // \f: same
            "[\"\(bs)u00AB\"]",  // uppercase hex — the writer emits lowercase
            "[\"\(bs)u001F\"]",  // uppercase hex of a CONTROL (.NET's verbatim vector — S1-2): lowercase
                                 // u001f is ACCEPTED above, so casing is the ONLY reason this rejects;
                                 // the u00AB row also trips the non-control rule and cannot catch a
                                 // parser mutated to take uppercase hex
            "[\"\(bs)u0041\"]",  // well-formed \u of a NON-control — the writer never u-escapes those
            "[\"\(bs)u000a\"]",  // the LONG spelling of the newline short escape — one canonical spelling per input
            "[\"\(bs)u00\"]",    // truncated \u escape
            "[\"a\u{01}b\"]",    // a RAW control inside a string — the writer always escapes them
            #"{"a":"b"}"#,       // an OBJECT — the dispatch-args shape, not this grammar
            #""a""#,             // a bare string is not an items array
        ]
        for json in malformed {
            XCTAssertThrowsError(try BnItemsJson.parse(json),
                                 "should reject: \(json)")
        }
    }

    func testTheErrorCarriesTheIndexAndABoundedPrefixOnly() {
        let long = "[\"" + String(repeating: "x", count: 64) // unterminated, 66 chars
        XCTAssertThrowsError(try BnItemsJson.parse(long)) { error in
            let message = String(describing: error)
            XCTAssertTrue(message.contains("index"),
                          "message should carry the failing index (got '\(message)')")
            XCTAssertLessThan(message.count, long.count + 64,
                              "message must carry a bounded prefix, not the whole payload")
        }
    }
}
