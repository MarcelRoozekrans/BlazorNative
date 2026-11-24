using System.Globalization;
using System.Text.RegularExpressions;

namespace BlazorNative.PerfReport;

/// <summary>One parsed <c>BNPERF</c> mark. <see cref="Line"/> is 1-based, for diagnostics.</summary>
internal readonly record struct Mark(string Point, long Seq, long T, long? Id, int Line);

/// <summary>A malformed mark or log: the run fails loudly rather than skip the line.</summary>
internal sealed class PerfFormatException(string message) : Exception(message);

internal static class MarkLine
{
    public const string PostPoint = "dispatch.post";
    public const string EnterPoint = "dispatch.enter";
    public const string HandlerPoint = "dispatch.handler";
    public const string RecvPoint = "frame.recv";
    public const string AppliedPoint = "frame.applied";
    public const string BootStart = "boot.start";
    public const string BootContent = "boot.content";

    private static readonly HashSet<string> Points =
        [BootStart, BootContent, PostPoint, EnterPoint, HandlerPoint, RecvPoint, AppliedPoint];

    // The three dispatch points must carry id=<handlerId>: pairing keys on it, because the
    // .NET marks reach the log late and out of order against the shell marks. Others must not.
    private static readonly HashSet<string> IdRequired = [PostPoint, EnterPoint, HandlerPoint];

    private const string Marker = "BNPERF ";

    /// <summary>
    /// Finds <c>BNPERF </c> anywhere in the line and parses from there. Returns false for a line
    /// without a mark. Throws <see cref="PerfFormatException"/> for a line that has the marker
    /// but is not exactly <c>BNPERF point seq=n t=ns</c> plus <c>id=n</c> on dispatch points.
    /// </summary>
    public static bool TryParse(string line, int lineNumber, string where, out Mark mark)
    {
        mark = default;
        var at = line.IndexOf(Marker, StringComparison.Ordinal);
        if (at < 0) return false;

        var tokens = line[(at + Marker.Length)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string Bad(string why) => $"{where}:{lineNumber}: malformed BNPERF mark ({why}): {line.Trim()}";

        if (tokens.Length < 3) throw new PerfFormatException(Bad("too few fields"));
        var point = tokens[0];
        if (!Points.Contains(point)) throw new PerfFormatException(Bad($"unknown point '{point}'"));

        long? seq = null, t = null, id = null;
        foreach (var tok in tokens.Skip(1))
        {
            var eq = tok.IndexOf('=');
            if (eq <= 0) throw new PerfFormatException(Bad($"unexpected token '{tok}'"));
            var key = tok[..eq];
            if (!long.TryParse(tok[(eq + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                throw new PerfFormatException(Bad($"'{key}' is not a non-negative integer"));
            switch (key)
            {
                case "seq" when seq is null: seq = value; break;
                case "t" when t is null: t = value; break;
                case "id" when id is null: id = value; break;
                default: throw new PerfFormatException(Bad($"unexpected or repeated '{key}'"));
            }
        }
        if (seq is null || t is null) throw new PerfFormatException(Bad("seq and t are required"));
        if (IdRequired.Contains(point) && !id.HasValue) throw new PerfFormatException(Bad("dispatch point without id"));
        if (id.HasValue && !IdRequired.Contains(point))
            throw new PerfFormatException(Bad("id on a non-dispatch point"));
        mark = new Mark(point, seq.Value, t.Value, id, lineNumber);
        return true;
    }

    // Android: "Fully drawn pkg/.Activity: +1s234ms", also "+850ms", "+1m2s3ms".
    private static readonly Regex FullyDrawn = new(
        @"Fully drawn\s+\S+:\s*\+(?:(?<h>\d+)h)?(?:(?<m>\d+)m(?!s))?(?:(?<s>\d+)s)?(?:(?<ms>\d+)ms)?",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex TotalTime = new(
        @"\bTotalTime:\s*(?<n>\d+)\b", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// The Fully drawn time in ms, or null for a line without one. A value that overflows a long
    /// is a malformed ruler: <see cref="PerfFormatException"/> naming the file and line.
    /// </summary>
    public static long? TryParseFullyDrawnMs(string line, int lineNumber, string where)
    {
        var m = FullyDrawn.Match(line);
        if (!m.Success) return null;
        if (!(m.Groups["h"].Success || m.Groups["m"].Success || m.Groups["s"].Success || m.Groups["ms"].Success))
            return null;
        long Part(string g) => m.Groups[g].Success ? long.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture) : 0;
        try
        {
            // A part too long for a long and a sum that overflows both raise OverflowException.
            return checked(((Part("h") * 60 + Part("m")) * 60 + Part("s")) * 1000 + Part("ms"));
        }
        catch (OverflowException)
        {
            throw OutOfRange(line, lineNumber, where, "Fully drawn");
        }
    }

    /// <summary>The TotalTime in ms, or null for a line without one; out of range throws like <see cref="TryParseFullyDrawnMs"/>.</summary>
    public static long? TryParseTotalTimeMs(string line, int lineNumber, string where)
    {
        var m = TotalTime.Match(line);
        if (!m.Success) return null;
        return long.TryParse(m.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v
            : throw OutOfRange(line, lineNumber, where, "TotalTime");
    }

    private static PerfFormatException OutOfRange(string line, int lineNumber, string where, string ruler) =>
        new($"{where}:{lineNumber}: {ruler} value out of range: {line.Trim()}");
}
