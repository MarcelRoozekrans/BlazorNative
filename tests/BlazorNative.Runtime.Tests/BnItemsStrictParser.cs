using System.Text;

namespace BlazorNative.Runtime.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// BnItemsStrictParser — Phase 7.3 Gate 1 review (I1): the `items` grammar's
// REFERENCE PARSER, the executable form of BnItemsJson.cs's normative comment.
//
// THE ACCEPTANCE SET IS STRICT (the review's decision): a parser accepts
// EXACTLY what BnItemsJson.Write emits — every string has ONE canonical
// encoding, and anything else is malformed. Concretely:
//
//   • NO whitespace anywhere, NO trailing comma, and the WHOLE string must be
//     consumed (text after the closing ']' is malformed).
//   • Inside a string, the ONLY escapes are the char production's:
//     \" \\ \n \r \t, and \u00xx for a control the writer has no short escape
//     for — exactly 4 LOWERCASE hex digits, value < U+0020, and NOT one of
//     U+0009/U+000A/U+000D (the writer spells those \t \n \r, so the long
//     form is not in the image). \b, \f, \/, non-control \uXXXX and
//     uppercase hex are all malformed.
//   • A RAW control (< U+0020) inside a string is malformed (the writer
//     always escapes them); everything from U+0020 up EXCEPT '"' and '\'
//     rides raw — U+007F included.
//
// This is deliberately NOT a JSON parser. It is the mirror of the two parsers
// Gates 2/3 write in Kotlin and Swift (neither shell has an array parser
// today — Kotlin's FlatJson.parse is object-only); transcribe THIS file plus
// the malformed-vector theory in BnItemsJsonTests, not a JSON library's
// behaviour. Malformed → FormatException here; the shells log loudly and
// render an EMPTY picker (BnItemsJson.cs's normative comment).
// ─────────────────────────────────────────────────────────────────────────────

internal static class BnItemsStrictParser
{
    /// <summary>Parses a wire `items` value. Returns the item list, or throws
    /// <see cref="FormatException"/> on ANY deviation from the writer's image
    /// (see the file header for the full strictness list).</summary>
    internal static IReadOnlyList<string> Parse(string text)
    {
        var items = new List<string>();
        if (text.Length == 0 || text[0] != '[')
            throw Malformed(0, "expected '['");
        var i = 1;
        if (i < text.Length && text[i] == ']')
        {
            i++; // the empty list is exactly "[]"
        }
        else
        {
            while (true)
            {
                items.Add(ParseString(text, ref i));
                if (i >= text.Length)
                    throw Malformed(i, "unterminated array — expected ',' or ']'");
                char c = text[i++];
                if (c == ']') break;
                if (c != ',')
                    throw Malformed(i - 1, "expected ',' or ']' — the grammar has no whitespace");
            }
        }
        if (i != text.Length)
            throw Malformed(i, "trailing input after ']' — the whole string must be consumed");
        return items;
    }

    private static string ParseString(string text, ref int i)
    {
        if (i >= text.Length || text[i] != '"')
            throw Malformed(i, "expected '\"' — items are double-quoted strings only");
        i++;
        var sb = new StringBuilder();
        while (true)
        {
            if (i >= text.Length)
                throw Malformed(i, "unterminated string");
            char c = text[i++];
            if (c == '"')
                return sb.ToString();
            if (c == '\\')
            {
                sb.Append(ParseEscape(text, ref i));
            }
            else if (c < 0x20)
            {
                throw Malformed(i - 1, "raw control character — the writer always escapes controls");
            }
            else
            {
                sb.Append(c); // raw pass-through, U+007F and surrogate halves included
            }
        }
    }

    private static char ParseEscape(string text, ref int i)
    {
        if (i >= text.Length)
            throw Malformed(i, "unterminated escape");
        char e = text[i++];
        switch (e)
        {
            case '"': return '"';
            case '\\': return '\\';
            case 'n': return '\n';
            case 'r': return '\r';
            case 't': return '\t';
            case 'u':
                if (i + 4 > text.Length)
                    throw Malformed(i, "\\u escape needs 4 hex digits");
                var value = 0;
                for (var k = 0; k < 4; k++)
                {
                    char h = text[i + k];
                    int digit = h switch
                    {
                        >= '0' and <= '9' => h - '0',
                        >= 'a' and <= 'f' => h - 'a' + 10,
                        // Uppercase hex is NOT in the writer's image — malformed.
                        _ => throw Malformed(i + k, "\\u escape must be exactly 4 LOWERCASE hex digits"),
                    };
                    value = (value << 4) | digit;
                }
                i += 4;
                if (value >= 0x20)
                    throw Malformed(i - 4, "\\u escape for a non-control — non-controls ride raw");
                if (value is 0x09 or 0x0A or 0x0D)
                    throw Malformed(i - 4, "\\u spelling of a short-escape control — the writer spells \\t \\n \\r");
                return (char)value;
            default:
                throw Malformed(i - 1, $"escape '\\{e}' is not in the char production");
        }
    }

    private static FormatException Malformed(int at, string why)
        => new($"malformed items wire text at offset {at}: {why}");
}
