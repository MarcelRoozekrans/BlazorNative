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
    /// Pairs one boot log. One boot per file: the first boot.start and the first boot.content.
    /// The Android rulers are read from the same file when present, at most one value each.
    /// </summary>
    public static PairingResult PairBoot(IReadOnlyList<Mark> marks, IEnumerable<string> lines, int run, string where)
    {
        var r = new PairingResult { Attempted = 1 };
        var start = marks.FirstOrDefault(m => m.Point == MarkLine.BootStart);
        var content = marks.FirstOrDefault(m => m.Point == MarkLine.BootContent);
        if (start.Point is null) r.Drops.Add($"{where}: no boot.start");
        else if (content.Point is null) r.Drops.Add($"{where}: no boot.content");
        else if (content.T < start.T)
            r.Violations.Add($"{where}: boot.content (t={content.T}) precedes boot.start (t={start.T})");
        else r.Samples.Add(new Sample(Metrics.Boot, Metrics.Total, run, Metrics.NsToMs(content.T - start.T)));

        long? fullyDrawn = null, totalTime = null;
        foreach (var line in lines)
        {
            if (MarkLine.TryParseFullyDrawnMs(line) is { } fd)
            {
                if (fullyDrawn is not null) throw new PerfFormatException($"{where}: more than one Fully drawn line");
                fullyDrawn = fd;
            }
            if (MarkLine.TryParseTotalTimeMs(line) is { } tt)
            {
                if (totalTime is not null) throw new PerfFormatException($"{where}: more than one TotalTime line");
                totalTime = tt;
            }
        }
        if (fullyDrawn is { } f) r.Samples.Add(new Sample(Metrics.BootFullyDrawn, Metrics.Total, run, f));
        if (totalTime is { } t) r.Samples.Add(new Sample(Metrics.BootTotalTime, Metrics.Total, run, t));
        return r;
    }

    /// <summary>
    /// Pairs one click log. Log position across points means nothing: the .NET marks reach the
    /// log late, through a writer thread, the log queue and the stderr pump, while the shell
    /// marks are logged directly. Within ONE point, log order is emit order. So marks are split
    /// by point and paired by per-point order and id:
    /// the k-th dispatch.post with id X takes the k-th dispatch.enter and dispatch.handler with
    /// id X. The click's frame is the one frame.recv with handler.t le recv.t lt the next post's t;
    /// its frame.applied is the applied mark with the same per-point index. A click with a missing
    /// point, no frame, or more than one frame is a dropped sample.
    /// </summary>
    public static PairingResult PairClicks(IReadOnlyList<Mark> marks, int run, string where)
    {
        var r = new PairingResult();
        var posts = marks.Where(m => m.Point == MarkLine.PostPoint).ToList();
        var recvs = marks.Where(m => m.Point == MarkLine.RecvPoint).ToList();
        var applieds = marks.Where(m => m.Point == MarkLine.AppliedPoint).ToList();
        if (recvs.Count != applieds.Count)
        {
            r.Violations.Add($"{where}: {recvs.Count} frame.recv marks but {applieds.Count} frame.applied marks, the frames cannot be matched");
            return r;
        }
        var enters = ById(marks, MarkLine.EnterPoint);
        var handlers = ById(marks, MarkLine.HandlerPoint);
        var seen = new Dictionary<long, int>();

        for (var k = 0; k < posts.Count; k++)
        {
            r.Attempted++;
            var post = posts[k];
            var id = post.Id!.Value;
            var j = seen.GetValueOrDefault(id);
            seen[id] = j + 1;
            var tag = $"{where}:{post.Line} (click {k + 1}, id={id})";

            if (!enters.TryGetValue(id, out var e) || j >= e.Count) { r.Drops.Add($"{tag}: no dispatch.enter with this id"); continue; }
            if (!handlers.TryGetValue(id, out var h) || j >= h.Count) { r.Drops.Add($"{tag}: no dispatch.handler with this id"); continue; }
            var enter = e[j];
            var handler = h[j];

            // The clock proof. The triple was chosen by order and id, not by t, so a violation
            // means the two sides really do not share a clock.
            if (enter.T < post.T)
                r.Violations.Add($"{tag}: dispatch.enter (t={enter.T}) precedes dispatch.post (t={post.T})");
            if (handler.T < enter.T)
                r.Violations.Add($"{tag}: dispatch.handler (t={handler.T}) precedes dispatch.enter (t={enter.T})");
            if (enter.T < post.T || handler.T < enter.T) continue;

            var upper = k + 1 < posts.Count ? posts[k + 1].T : long.MaxValue;
            var inWindow = new List<int>();
            for (var i = 0; i < recvs.Count; i++)
                if (recvs[i].T >= handler.T && recvs[i].T < upper) inWindow.Add(i);
            if (inWindow.Count == 0) { r.Drops.Add($"{tag}: no frame before the next post"); continue; }
            if (inWindow.Count > 1) { r.Drops.Add($"{tag}: more than one frame ({inWindow.Count} recv)"); continue; }
            var recv = recvs[inWindow[0]];
            var applied = applieds[inWindow[0]];
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

    private static Dictionary<long, List<Mark>> ById(IReadOnlyList<Mark> marks, string point) =>
        marks.Where(m => m.Point == point).GroupBy(m => m.Id!.Value).ToDictionary(g => g.Key, g => g.ToList());
}
