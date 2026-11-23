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
        var n = 0;
        foreach (var line in lines)
        {
            n++;
            if (MarkLine.TryParseFullyDrawnMs(line, n, where) is { } fd)
            {
                if (fullyDrawn is not null) throw new PerfFormatException($"{where}: more than one Fully drawn line");
                fullyDrawn = fd;
            }
            if (MarkLine.TryParseTotalTimeMs(line, n, where) is { } tt)
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
    /// the k-th dispatch.post with id X takes the k-th dispatch.enter with id X, and that enter
    /// takes the k-th dispatch.handler with id X. When the counts differ, a mark is missing and
    /// order alone would shift every later occurrence onto the wrong partner; see <see cref="Align"/>
    /// for how the pairing resynchronizes without hiding a clock violation. The click's frame is the one frame.recv with handler.t le recv.t lt the next post's t;
    /// its frame.applied is the first applied mark with recv.t le applied.t lt the next recv's t. A click
    /// with a missing point, no frame, more than one frame, or a frame with no applied mark is a
    /// dropped sample.
    /// </summary>
    public static PairingResult PairClicks(IReadOnlyList<Mark> marks, int run, string where)
    {
        var r = new PairingResult();
        var posts = marks.Where(m => m.Point == MarkLine.PostPoint).ToList();
        var recvs = marks.Where(m => m.Point == MarkLine.RecvPoint).ToList();
        var applieds = marks.Where(m => m.Point == MarkLine.AppliedPoint).ToList();
        var enters = marks.Where(m => m.Point == MarkLine.EnterPoint).ToList();
        var handlers = marks.Where(m => m.Point == MarkLine.HandlerPoint).ToList();
        var enterOf = AlignById(posts, enters);
        var handlerOf = AlignById(enters, handlers);

        for (var k = 0; k < posts.Count; k++)
        {
            r.Attempted++;
            var post = posts[k];
            var id = post.Id!.Value;
            var tag = $"{where}:{post.Line} (click {k + 1}, id={id})";

            if (enterOf[k] < 0) { r.Drops.Add($"{tag}: no dispatch.enter with this id"); continue; }
            if (handlerOf[enterOf[k]] < 0) { r.Drops.Add($"{tag}: no dispatch.handler with this id"); continue; }
            var enter = enters[enterOf[k]];
            var handler = handlers[handlerOf[enterOf[k]]];

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
            var recvIndex = inWindow[0];
            var recv = recvs[recvIndex];
            // The frame's applied mark is the first one in [recv.t, next recv.t). Both are shell
            // marks on the same clock. A dropped frame or a throwing batch has no applied mark,
            // and that costs this one sample; an applied mark is never taken from another frame.
            var nextRecv = recvIndex + 1 < recvs.Count ? recvs[recvIndex + 1].T : long.MaxValue;
            var appliedIndex = applieds.FindIndex(a => a.T >= recv.T && a.T < nextRecv);
            if (appliedIndex < 0) { r.Drops.Add($"{tag}: no frame.applied for the frame"); continue; }
            var applied = applieds[appliedIndex];

            r.Samples.Add(new Sample(Metrics.Dispatch, Metrics.Total, run, Metrics.NsToMs(handler.T - post.T)));
            r.Samples.Add(new Sample(Metrics.Dispatch, Metrics.Lane, run, Metrics.NsToMs(enter.T - post.T)));
            r.Samples.Add(new Sample(Metrics.Dispatch, Metrics.DotNet, run, Metrics.NsToMs(handler.T - enter.T)));
            r.Samples.Add(new Sample(Metrics.FrameApply, Metrics.Total, run, Metrics.NsToMs(applied.T - recv.T)));
            r.Samples.Add(new Sample(Metrics.ClickToContent, Metrics.Total, run, Metrics.NsToMs(applied.T - post.T)));
        }
        return r;
    }

    /// <summary>
    /// For each mark of <paramref name="from"/>, the index in <paramref name="to"/> of its partner
    /// with the same id, or -1. Both lists are one point each, in log order.
    /// </summary>
    private static int[] AlignById(List<Mark> from, List<Mark> to)
    {
        var result = new int[from.Count];
        foreach (var id in from.Select(m => m.Id!.Value).Distinct())
        {
            var a = Enumerable.Range(0, from.Count).Where(i => from[i].Id == id).ToList();
            var b = Enumerable.Range(0, to.Count).Where(i => to[i].Id == id).ToList();
            // A from-mark's partner is stamped before the next from-mark of any id: the clicks are
            // serial, and the dispatch lane runs one event at a time.
            var aligned = Align(a.Select(i => from[i].T).ToList(),
                a.Select(i => i + 1 < from.Count ? from[i + 1].T : long.MaxValue).ToList(),
                b.Select(i => to[i].T).ToList());
            for (var x = 0; x < a.Count; x++) result[a[x]] = aligned[x] < 0 ? -1 : b[aligned[x]];
        }
        return result;
    }

    /// <summary>
    /// Pairs two same-id sequences. With equal counts it pairs by order alone and never looks at
    /// t, so a partner stamped out of order is reported as a clock violation by the caller. With
    /// unequal counts, |a - b| marks are missing on one side, and order alone would misalign every
    /// later occurrence: a lost middle enter would hand click j the enter of click j+1, and its
    /// handler would then precede that enter, a false violation. So it skips at most that many:
    /// an a whose next b is stamped at or after the a's upper bound has lost its partner, and a b
    /// stamped before the current a belongs to an a that was lost. Once the budget is spent it
    /// pairs by order again, so a real clock offset still shows on every other occurrence.
    /// </summary>
    internal static int[] Align(IReadOnlyList<long> aT, IReadOnlyList<long> aUpper, IReadOnlyList<long> bT)
    {
        var result = new int[aT.Count];
        var lostA = Math.Max(0, aT.Count - bT.Count);
        var lostB = Math.Max(0, bT.Count - aT.Count);
        int i = 0, j = 0;
        while (i < aT.Count)
        {
            if (j >= bT.Count) { result[i++] = -1; continue; }
            if (lostB > 0 && bT[j] < aT[i]) { j++; lostB--; continue; }
            if (lostA > 0 && bT[j] >= aUpper[i]) { result[i++] = -1; lostA--; continue; }
            result[i++] = j++;
        }
        return result;
    }
}
