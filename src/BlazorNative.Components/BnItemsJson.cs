using System.Text;

namespace BlazorNative.Components;

// ─────────────────────────────────────────────────────────────────────────────
// BnItemsJson — Phase 7.3 Task 1.2 (design decision 3: the picker is the
// state-owner precedent, and its items ride the wire as data).
//
// THE `items` WIRE GRAMMAR (NORMATIVE — the shells' parsers are written FROM
// this comment, the 6.3 strict-parse discipline; a drift between the two
// hand-written parsers is exactly what one normative grammar exists to
// prevent):
//
//   items   := '[' ( string ( ',' string )* )? ']'
//   string  := '"' char* '"'
//   char    := any Unicode scalar EXCEPT '"' '\' and controls < U+0020,
//              passed through as raw UTF-8
//            | '\"' | '\\' | '\n' | '\r' | '\t'
//            | '\u00XX'  — any OTHER control < U+0020, lowercase hex, 4 digits
//
//   • NO whitespace between tokens — the writer emits none, and the shells'
//     parsers are STRICT whole-string parsers (trailing garbage, single
//     quotes, unquoted items, a trailing comma: all malformed → the shell
//     logs loudly and renders an EMPTY picker rather than a wrong one).
//   • ACCEPTANCE IS STRICT (Gate 1 review, DECIDED): a parser accepts EXACTLY
//     what this writer emits; every escape not in the char production is
//     malformed (no \b, \f or \/; no \u for a non-control; no uppercase hex;
//     no long \u spelling of a control the writer spells short); no
//     whitespace, no trailing comma, and the WHOLE string must be consumed.
//     Every string has ONE canonical encoding. The executable form of this
//     bullet is BnItemsStrictParser + the normative malformed-vector theory
//     in BnItemsJsonTests (Runtime.Tests) — the shells' parsers (Gates 2/3)
//     transcribe BOTH, not a JSON library's behaviour.
//   • The empty list is exactly `[]` (two characters).
//   • The escaping matrix is EXACTLY the flat-JSON dispatch-args matrix every
//     shell already implements (Kotlin FlatJson.appendJsonString / Swift
//     BnFlatJson.appendString / .NET NativeShellBridge.WriteFlatJsonObject) —
//     one matrix, a fourth writer, ZERO new escaping rules to mirror.
//     BnItemsJsonTests pins this writer's escaping AGAINST
//     NativeShellBridge's, character for character, so the two .NET writers
//     cannot drift apart silently.
//   • Items are opaque display strings: commas, quotes, non-ASCII, emoji are
//     all legal item CONTENT — the grammar carries them, the shells display
//     them verbatim.
//
// Why not System.Text.Json: the dispatch-args path deliberately hand-rolls
// this exact matrix (Phase 3.1) so three runtimes stay mirror-provable; the
// items prop reuses that decision rather than introducing a second JSON
// dialect (property escaping, \uXXXX casing, surrogate handling) the shells
// would then have to accept BOTH of.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Serializes a picker's <c>Items</c> to the flat-JSON string-array
/// wire grammar (normative grammar in the file header). Internal: component
/// code calls it; consumers set <c>IReadOnlyList&lt;string&gt;</c> params and
/// never see the wire form.</summary>
internal static class BnItemsJson
{
    /// <summary>Writes <paramref name="items"/> as a flat-JSON string array —
    /// <c>[]</c> for an empty list. Null ITEMS (list entries) are a caller
    /// bug, not wire data: throw rather than invent an encoding for a value
    /// the grammar has no word for.</summary>
    internal static string Write(IReadOnlyList<string> items)
    {
        var sb = new StringBuilder(capacity: 2 + items.Count * 16);
        sb.Append('[');
        for (var i = 0; i < items.Count; i++)
        {
            if (i > 0) sb.Append(',');
            AppendString(sb,
                items[i] ?? throw new ArgumentException(
                    $"picker item {i} is null — the items grammar has no null; use an empty string",
                    nameof(items)));
        }
        sb.Append(']');
        return sb.ToString();
    }

    /// <summary>Appends one JSON string literal — the flat-JSON escaping
    /// matrix (see the header). Kept private: the GRAMMAR is the contract,
    /// not this helper.</summary>
    private static void AppendString(StringBuilder sb, string value)
    {
        sb.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 0x20)
                    {
                        sb.Append("\\u");
                        sb.Append(((int)ch).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        // Raw pass-through — surrogate halves included: the
                        // string is written char-by-char, so a surrogate PAIR
                        // lands adjacent and re-encodes to the same UTF-8 the
                        // ABI crossing produces (the FlatJson posture).
                        sb.Append(ch);
                    }
                    break;
            }
        }
        sb.Append('"');
    }
}
