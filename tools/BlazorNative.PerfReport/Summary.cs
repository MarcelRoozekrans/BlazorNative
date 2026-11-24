using System.Globalization;
using System.Text;

namespace BlazorNative.PerfReport;

internal readonly record struct Quartiles(decimal Median, decimal Q1, decimal Q3)
{
    public decimal Iqr => Q3 - Q1;
}

internal static class Stats
{
    /// <summary>Median and quartiles by linear interpolation, numpy's default (type 7).</summary>
    public static Quartiles Compute(IReadOnlyList<decimal> values)
    {
        if (values.Count == 0) throw new ArgumentException("no values", nameof(values));
        var sorted = values.OrderBy(v => v).ToArray();
        return new Quartiles(Percentile(sorted, 0.5m), Percentile(sorted, 0.25m), Percentile(sorted, 0.75m));
    }

    private static decimal Percentile(decimal[] sorted, decimal p)
    {
        var pos = p * (sorted.Length - 1);
        var lo = (int)Math.Floor(pos);
        var hi = (int)Math.Ceiling(pos);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
    }
}

/// <summary>One side's pairing outcome, ready for the writers.</summary>
internal sealed class SideReport(string name, string sdk, string commit)
{
    public string Name { get; } = name;
    public string Sdk { get; } = sdk;
    public string Commit { get; } = commit;
    public List<Sample> Samples { get; } = [];
    public int ClicksAttempted { get; set; }
    public int ClicksDropped { get; set; }
    public int BootFiles { get; set; }
    public int ClickFiles { get; set; }
    public int BootsAttempted { get; set; }
    public int BootsDropped { get; set; }
    /// <summary>Boot and click input files above run 0, each one process, whether or not any of its samples paired.</summary>
    public int BootProcesses { get; set; }
    public int ClickProcesses { get; set; }
    public List<string> Drops { get; } = [];
}

internal static class Writers
{
    public const string CsvHeader = "side,sdk,commit,metric,part,run,value_ms";

    private static string Num(decimal v) => v.ToString("0.######", CultureInfo.InvariantCulture);
    private static string Fixed(decimal v) => v.ToString("0.000", CultureInfo.InvariantCulture);

    public static string Csv(IEnumerable<SideReport> sides)
    {
        var sb = new StringBuilder(CsvHeader).Append('\n');
        foreach (var s in sides)
            foreach (var x in s.Samples)
                sb.Append(s.Name).Append(',').Append(s.Sdk).Append(',').Append(s.Commit).Append(',')
                  .Append(x.Metric).Append(',').Append(x.Part).Append(',')
                  .Append(x.Run.ToString(CultureInfo.InvariantCulture)).Append(',').Append(Num(x.ValueMs)).Append('\n');
        return sb.ToString();
    }

    public static string Markdown(IReadOnlyList<SideReport> sides, int minRuns)
    {
        var sb = new StringBuilder();
        sb.Append("# Perf summary\n\n");
        sb.Append("Run 0 is warm-up and is excluded. Values are milliseconds. Quartiles use linear interpolation (numpy default).\n\n");
        sb.Append("| side | sdk | commit |\n|---|---|---|\n");
        foreach (var s in sides) sb.Append($"| {s.Name} | {s.Sdk} | {s.Commit} |\n");
        sb.Append("\n| side | metric | part | n | median | Q1 | Q3 | IQR |\n|---|---|---|---|---|---|---|---|\n");
        foreach (var s in sides)
        {
            var groups = s.Samples.Where(x => x.Run > 0)
                .GroupBy(x => (x.Metric, x.Part))
                .OrderBy(g => MetricOrder(g.Key.Metric)).ThenBy(g => PartOrder(g.Key.Part));
            foreach (var g in groups)
            {
                var q = Stats.Compute(g.Select(x => x.ValueMs).ToList());
                sb.Append($"| {s.Name} | {g.Key.Metric} | {g.Key.Part} | {g.Count()} | {Fixed(q.Median)} | {Fixed(q.Q1)} | {Fixed(q.Q3)} | {Fixed(q.Iqr)} |\n");
            }
        }
        AppendPerProcess(sb, sides);
        sb.Append("\n## Dropped samples\n\n| side | clicks dropped | boots dropped |\n|---|---|---|\n");
        foreach (var s in sides)
            sb.Append($"| {s.Name} | {s.ClicksDropped} of {s.ClicksAttempted} | {s.BootsDropped} of {s.BootsAttempted} |\n");
        foreach (var s in sides)
            foreach (var d in s.Drops) sb.Append($"\n- {s.Name}: dropped, {d}");
        sb.Append($"\n\nA run fails above 5% dropped or with fewer than {minRuns} samples (--min-runs).\n");
        return sb.ToString();
    }

    // Each <kind>-<run>.log is one process: one cold boot, or one test run of serial clicks. A
    // slow mode confined to some processes is averaged away in the pooled table, so the click
    // metrics, which have many samples per process, are also given per run file.
    private static void AppendPerProcess(StringBuilder sb, IReadOnlyList<SideReport> sides)
    {
        sb.Append("\n## Per process\n\n");
        sb.Append("Each `<kind>-<run>.log` is one process. Run 0 is excluded. The click metrics have one median per process.\n\n");
        sb.Append("| side | kind | processes |\n|---|---|---|\n");
        foreach (var s in sides)
        {
            sb.Append($"| {s.Name} | boot | {s.BootProcesses} |\n");
            sb.Append($"| {s.Name} | click | {s.ClickProcesses} |\n");
        }
        sb.Append("\n| side | metric | part | run | n | median |\n|---|---|---|---|---|---|\n");
        foreach (var s in sides)
        {
            var groups = s.Samples.Where(x => x.Run > 0 && !IsBootMetric(x.Metric))
                .GroupBy(x => (x.Metric, x.Part, x.Run))
                .OrderBy(g => MetricOrder(g.Key.Metric)).ThenBy(g => PartOrder(g.Key.Part)).ThenBy(g => g.Key.Run);
            foreach (var g in groups)
            {
                var q = Stats.Compute(g.Select(x => x.ValueMs).ToList());
                sb.Append($"| {s.Name} | {g.Key.Metric} | {g.Key.Part} | {g.Key.Run.ToString(CultureInfo.InvariantCulture)} | {g.Count()} | {Fixed(q.Median)} |\n");
            }
        }
    }

    private static bool IsBootMetric(string m) => m is Metrics.Boot or Metrics.BootFullyDrawn or Metrics.BootTotalTime;

    private static int MetricOrder(string m) => Array.IndexOf(
        [Metrics.Boot, Metrics.BootFullyDrawn, Metrics.BootTotalTime, Metrics.Dispatch, Metrics.FrameApply, Metrics.ClickToContent], m);

    private static int PartOrder(string p) => Array.IndexOf([Metrics.Total, Metrics.Lane, Metrics.DotNet], p);
}
