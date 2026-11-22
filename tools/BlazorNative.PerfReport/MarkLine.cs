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

    // enter and handler must carry id=<handlerId>; the shell's post may carry one (the shell
    // knows the handler id of the button it clicked) or not; every other point must not.
    private static readonly HashSet<string> IdRequired = [EnterPoint, HandlerPoint];

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
        if (id.HasValue && !IdRequired.Contains(point) && point != PostPoint)
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

    public static long? TryParseFullyDrawnMs(string line)
    {
        var m = FullyDrawn.Match(line);
        if (!m.Success) return null;
        long Part(string g) => m.Groups[g].Success ? long.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture) : 0;
        if (!(m.Groups["h"].Success || m.Groups["m"].Success || m.Groups["s"].Success || m.Groups["ms"].Success))
            return null;
        return ((Part("h") * 60 + Part("m")) * 60 + Part("s")) * 1000 + Part("ms");
    }

    public static long? TryParseTotalTimeMs(string line)
    {
        var m = TotalTime.Match(line);
        return m.Success ? long.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture) : null;
    }
}
