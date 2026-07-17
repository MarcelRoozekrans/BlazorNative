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

    /// Phase 9.0: a flat string→string object literal, insertion-ordered — the twin
    /// of the Kotlin `FlatJson.write` the geolocation fix crosses on (keys
    /// lat/lng/accuracy/altitude/timestamp, numbers already string-encoded). The .NET
    /// `ParseFlatJsonObject` reads exactly this. Empty pairs → `{}`.
    static func object(_ pairs: [(String, String)]) -> String {
        var s = "{"
        var first = true
        for (key, value) in pairs {
            if !first { s += "," }
            first = false
            appendString(&s, key)
            s += ":"
            appendString(&s, value)
        }
        s += "}"
        return s
    }

    /// Phase 9.0: the READER half — a flat JSON object of string→string, the twin of
    /// Kotlin `FlatJson.parse`. .NET WRITES the HostCallBegin args (`{"mode":"request"}`
    /// / `{"mode":"check"}`) and the shell reads them here; the same short escapes +
    /// strict `\u00xx` the writer emits are decoded. Malformed input → nil (the caller
    /// treats an unreadable arg as the default), never a throw across the op dispatch.
    static func parseObject(_ json: String) -> [String: String]? {
        var out: [String: String] = [:]
        let s = Array(json.unicodeScalars)
        var i = 0
        func skipWs() { while i < s.count, s[i] == " " || s[i] == "\n" || s[i] == "\r" || s[i] == "\t" { i += 1 } }
        func parseStr() -> String? {
            guard i < s.count, s[i] == "\"" else { return nil }
            i += 1
            var v = String.UnicodeScalarView()
            while i < s.count {
                let c = s[i]; i += 1
                if c == "\"" { return String(v) }
                if c != "\\" { v.append(c); continue }
                guard i < s.count else { return nil }
                let e = s[i]; i += 1
                switch e {
                case "\"": v.append("\"")
                case "\\": v.append("\\")
                case "/": v.append("/")
                case "n": v.append("\n")
                case "r": v.append("\r")
                case "t": v.append("\t")
                case "b": v.append(Unicode.Scalar(UInt8(8)))
                case "f": v.append(Unicode.Scalar(UInt8(12)))
                case "u":
                    guard i + 4 <= s.count else { return nil }
                    var code: UInt32 = 0
                    for _ in 0..<4 {
                        let h = s[i]; i += 1
                        let d: UInt32
                        switch h {
                        case "0"..."9": d = h.value - 48
                        case "a"..."f": d = h.value - 97 + 10
                        case "A"..."F": d = h.value - 65 + 10
                        default: return nil
                        }
                        code = (code << 4) | d
                    }
                    guard let scalar = Unicode.Scalar(code) else { return nil }
                    v.append(scalar)
                default: return nil
                }
            }
            return nil
        }
        skipWs()
        guard i < s.count, s[i] == "{" else { return nil }
        i += 1
        skipWs()
        if i < s.count, s[i] == "}" { return out }
        while true {
            skipWs()
            guard let key = parseStr() else { return nil }
            skipWs()
            guard i < s.count, s[i] == ":" else { return nil }
            i += 1
            skipWs()
            guard let value = parseStr() else { return nil }
            out[key] = value
            skipWs()
            guard i < s.count else { return nil }
            let c = s[i]; i += 1
            if c == "}" { return out }
            if c != "," { return nil }
        }
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
