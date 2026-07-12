// ─────────────────────────────────────────────────────────────────────────────
// BnBridgeTests — Phase 5.3 (M5 DoD #3): pins the two contract surfaces the design
// flags as risks, in pure Swift (no runtime boot):
//   • AppleShellBridge.writeUtf8 — the -needed buffer protocol (the ShellBridge.kt
//     writeUtf8 twin): fits → bytes+NUL, count returned; doesn't fit → -(bytes+1),
//     nothing written. A wrong protocol silently corrupts current-route reads.
//   • BnFlatJson.args — the dispatch-args escaping the .NET FlatJson parser decodes
//     (the shared flat-JSON matrix): click omits payload; change escapes the raw text.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
@testable import BnHost

final class BnBridgeTests: XCTestCase {

    func testWriteUtf8BufferProtocol() {
        let cap = 64

        // Fits: "/settings" = 9 bytes + NUL = 10; returns 10, writes bytes + NUL.
        var buf = [CChar](repeating: 0x7F, count: cap)
        let rc = buf.withUnsafeMutableBufferPointer {
            AppleShellBridge.writeUtf8("/settings", $0.baseAddress!, Int32(cap))
        }
        XCTAssertEqual(rc, 10, "return = utf8 bytes + NUL")
        XCTAssertEqual(String(cString: buf), "/settings")

        // Empty string: 0 bytes + NUL = 1; returns 1, writes just the NUL.
        var buf2 = [CChar](repeating: 0x7F, count: cap)
        let rcEmpty = buf2.withUnsafeMutableBufferPointer {
            AppleShellBridge.writeUtf8("", $0.baseAddress!, Int32(cap))
        }
        XCTAssertEqual(rcEmpty, 1)
        XCTAssertEqual(buf2[0], 0)

        // Multi-byte UTF-8 counts BYTES not characters: "→世界" = 3+3+3 = 9 + NUL = 10.
        var buf3 = [CChar](repeating: 0x7F, count: cap)
        let rcUtf8 = buf3.withUnsafeMutableBufferPointer {
            AppleShellBridge.writeUtf8("→世界", $0.baseAddress!, Int32(cap))
        }
        XCTAssertEqual(rcUtf8, 10, "3 three-byte scalars + NUL")
        XCTAssertEqual(String(cString: buf3), "→世界")

        // Does NOT fit: cap 4, "hello" (5 bytes + NUL = 6) → -6, nothing written.
        var small = [CChar](repeating: 0x7F, count: 4)
        let rcNeeded = small.withUnsafeMutableBufferPointer {
            AppleShellBridge.writeUtf8("hello", $0.baseAddress!, 4)
        }
        XCTAssertEqual(rcNeeded, -6, "the -needed size demand is -(utf8 bytes + 1)")
        XCTAssertEqual(small[0], 0x7F, "a non-fitting write must touch NOTHING")

        // Exact boundary: cap == needed fits ("abc" = 3 + NUL = 4, cap 4 → 4).
        var exact = [CChar](repeating: 0x7F, count: 4)
        let rcExact = exact.withUnsafeMutableBufferPointer {
            AppleShellBridge.writeUtf8("abc", $0.baseAddress!, 4)
        }
        XCTAssertEqual(rcExact, 4)
        XCTAssertEqual(String(cString: exact), "abc")
    }

    func testFlatJsonArgs() {
        // click → payload key OMITTED.
        XCTAssertEqual(BnFlatJson.args(name: "click", payload: nil), #"{"name":"click"}"#)

        // change → raw text in a "payload" field, name first, non-ASCII passes through.
        XCTAssertEqual(BnFlatJson.args(name: "change", payload: "héllo→世界"),
                       #"{"name":"change","payload":"héllo→世界"}"#)

        // JSON escaping matrix (twin of Kotlin appendJsonString): quote, backslash,
        // newline, tab.
        XCTAssertEqual(BnFlatJson.args(name: "change", payload: "a\"b\\c"),
                       #"{"name":"change","payload":"a\"b\\c"}"#)
        XCTAssertEqual(BnFlatJson.args(name: "change", payload: "line1\nline2\t!"),
                       #"{"name":"change","payload":"line1\nline2\t!"}"#)
    }
}
