namespace BlazorNative.PerfReport;

/// <summary>One timed sample, in milliseconds. The Android rulers are whole milliseconds already; mark spans are converted from nanoseconds.</summary>
internal sealed record Sample(string Metric, string Part, int Run, decimal ValueMs);

/// <summary>What pairing one run file produced.</summary>
internal sealed class PairingResult
{
    public List<Sample> Samples { get; } = [];
    /// <summary>Order violations: each one fails the run.</summary>
    public List<string> Violations { get; } = [];
    /// <summary>Why each dropped sample was dropped, with its log line.</summary>
    public List<string> Drops { get; } = [];
    /// <summary>Clicks (or boots) attempted, dropped ones included.</summary>
    public int Attempted { get; set; }
    public int Dropped => Drops.Count;
}

internal static class Metrics
{
    public const string Boot = "boot";
    public const string BootFullyDrawn = "boot_fully_drawn";
    public const string BootTotalTime = "boot_total_time";
    public const string Dispatch = "dispatch";
    public const string FrameApply = "frame_apply";
    public const string ClickToContent = "click_to_content";

    // Part for every row without a split. Dispatch also has the split rows "lane" and "dotnet".
    public const string Total = "total";
    public const string Lane = "lane";
    public const string DotNet = "dotnet";

    public static decimal NsToMs(long ns) => ns / 1_000_000m;
}

internal static class Pairing
{
    /// <summary>
    /// Pairs one boot log. One boot per file: the first boot.start and the first boot.content
    /// after it. The Android rulers are read from the same file when present.
    /// </summary>
    public static PairingResult PairBoot(IReadOnlyList<Mark> marks, IEnumerable<string> lines, int run, string where)
    {
        var r = new PairingResult { Attempted = 1 };
        var start = marks.FirstOrDefault(m => m.Point == MarkLine.BootStart);
        var content = start.Point is null ? default : marks.FirstOrDefault(m => m.Point == MarkLine.BootContent && m.Line > start.Line);
        if (start.Point is null) r.Drops.Add($"{where}: no boot.start");
        else if (content.Point is null) r.Drops.Add($"{where}: no boot.content after boot.start");
        else if (content.T < start.T)
            r.Violations.Add($"{where}: boot.content (t={content.T}) precedes boot.start (t={start.T})");
        else r.Samples.Add(new Sample(Metrics.Boot, Metrics.Total, run, Metrics.NsToMs(content.T - start.T)));

        foreach (var line in lines)
        {
            if (MarkLine.TryParseFullyDrawnMs(line) is { } fd)
                r.Samples.Add(new Sample(Metrics.BootFullyDrawn, Metrics.Total, run, fd));
            if (MarkLine.TryParseTotalTimeMs(line) is { } tt)
                r.Samples.Add(new Sample(Metrics.BootTotalTime, Metrics.Total, run, tt));
        }
        return r;
    }

    /// <summary>
    /// Pairs one click log, in log order. The k-th dispatch.post owns the window up to the next
    /// post. Inside it: the first dispatch.enter, then the first dispatch.handler after it with
    /// the same id, then exactly one frame.recv and exactly one frame.applied after the handler.
    /// A window with a missing point, no frame, or more than one frame is a dropped sample.
    /// </summary>
    public static PairingResult PairClicks(IReadOnlyList<Mark> marks, int run, string where)
    {
        var r = new PairingResult();
        var posts = new List<int>();
        for (var i = 0; i < marks.Count; i++)
            if (marks[i].Point == MarkLine.PostPoint) posts.Add(i);

        for (var k = 0; k < posts.Count; k++)
        {
            r.Attempted++;
            var pi = posts[k];
            var end = k + 1 < posts.Count ? posts[k + 1] : marks.Count;
            var post = marks[pi];
            var tag = $"{where}:{post.Line} (click {k + 1})";

            var ei = IndexOf(marks, pi + 1, end, m => m.Point == MarkLine.EnterPoint && (post.Id is null || m.Id == post.Id));
            if (ei < 0) { r.Drops.Add($"{tag}: no dispatch.enter before the next post"); continue; }
            var enter = marks[ei];
            var hi = IndexOf(marks, ei + 1, end, m => m.Point == MarkLine.HandlerPoint && m.Id == enter.Id);
            if (hi < 0) { r.Drops.Add($"{tag}: no dispatch.handler with id={enter.Id} before the next post"); continue; }
            var handler = marks[hi];

            // The clock proof: a single violation fails the run, whatever happens to the frame.
            if (enter.T < post.T)
                r.Violations.Add($"{tag}: dispatch.enter (t={enter.T}) precedes dispatch.post (t={post.T})");
            if (handler.T < enter.T)
                r.Violations.Add($"{tag}: dispatch.handler (t={handler.T}) precedes dispatch.enter (t={enter.T})");
            if (enter.T < post.T || handler.T < enter.T) continue;

            var recvs = Count(marks, hi + 1, end, MarkLine.RecvPoint);
            var applieds = Count(marks, hi + 1, end, MarkLine.AppliedPoint);
            if (recvs == 0 || applieds == 0) { r.Drops.Add($"{tag}: no frame before the next post"); continue; }
            if (recvs > 1 || applieds > 1) { r.Drops.Add($"{tag}: more than one frame ({recvs} recv, {applieds} applied)"); continue; }
            var recv = marks[IndexOf(marks, hi + 1, end, m => m.Point == MarkLine.RecvPoint)];
            var applied = marks[IndexOf(marks, hi + 1, end, m => m.Point == MarkLine.AppliedPoint)];
            if (applied.Line < recv.Line) { r.Drops.Add($"{tag}: frame.applied logged before frame.recv"); continue; }
            if (applied.T < recv.T)
            {
                r.Violations.Add($"{tag}: frame.applied (t={applied.T}) precedes frame.recv (t={recv.T})");
                continue;
            }

            r.Samples.Add(new Sample(Metrics.Dispatch, Metrics.Total, run, Metrics.NsToMs(handler.T - post.T)));
            r.Samples.Add(new Sample(Metrics.Dispatch, Metrics.Lane, run, Metrics.NsToMs(enter.T - post.T)));
            r.Samples.Add(new Sample(Metrics.Dispatch, Metrics.DotNet, run, Metrics.NsToMs(handler.T - enter.T)));
            r.Samples.Add(new Sample(Metrics.FrameApply, Metrics.Total, run, Metrics.NsToMs(applied.T - recv.T)));
            r.Samples.Add(new Sample(Metrics.ClickToContent, Metrics.Total, run, Metrics.NsToMs(applied.T - post.T)));
        }
        return r;
    }

    private static int IndexOf(IReadOnlyList<Mark> marks, int from, int to, Func<Mark, bool> match)
    {
        for (var i = from; i < to; i++) if (match(marks[i])) return i;
        return -1;
    }

    private static int Count(IReadOnlyList<Mark> marks, int from, int to, string point)
    {
        var n = 0;
        for (var i = from; i < to; i++) if (marks[i].Point == point) n++;
        return n;
    }
}
