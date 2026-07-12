// ─────────────────────────────────────────────────────────────────────────────
// BnFlatJson — Phase 5.3 (M5 DoD #3): the dispatch-args writer. The Swift twin of
// the Kotlin io.blazornative.jni.FlatJson.write + appendJsonString — the SAME
// flat-JSON escaping matrix every shell uses for `blazornative_dispatch_event`
// args, so the .NET FlatJson parser decodes iOS taps identically to Android's.
//
// Shape (design §1): a click emits {"name":"click"} — the payload key is OMITTED
// (a NULL/absent payload maps to a null EventArgs payload .NET-side); a change
// emits {"name":"change","payload":"<raw text>"} — the RAW typed text, escaped
// per JSON, in insertion order (name first, then payload — the Kotlin
// LinkedHashMap order the .NET parser does not depend on but which we match).
// ─────────────────────────────────────────────────────────────────────────────

import Foundation

enum BnFlatJson {

    /// Builds the dispatch args. `payload == nil` omits the payload key (click);
    /// non-nil emits `"payload":"<escaped>"` (change).
    static func args(name: String, payload: String?) -> String {
        var s = "{"
        appendString(&s, "name")
        s += ":"
        appendString(&s, name)
        if let payload = payload {
            s += ","
            appendString(&s, "payload")
            s += ":"
            appendString(&s, payload)
        }
        s += "}"
        return s
    }

    /// Appends a JSON string literal — the exact escaping of the Kotlin
    /// `appendJsonString`: `"` `\` `\n` `\r` `\t` escaped; other control chars
    /// (< 0x20) as `\u00xx`; everything else (incl. non-ASCII like "世界→") passes
    /// through as raw UTF-8 (encoded when the String crosses the ABI via withCString).
    static func appendString(_ sb: inout String, _ value: String) {
        sb += "\""
        for scalar in value.unicodeScalars {
            switch scalar {
            case "\"": sb += "\\\""
            case "\\": sb += "\\\\"
            case "\n": sb += "\\n"
            case "\r": sb += "\\r"
            case "\t": sb += "\\t"
            default:
                if scalar.value < 0x20 {
                    sb += String(format: "\\u%04x", scalar.value)
                } else {
                    sb.unicodeScalars.append(scalar)
                }
            }
        }
        sb += "\""
    }
}
