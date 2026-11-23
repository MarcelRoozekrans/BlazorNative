using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BlazorNative.PerfReport;

/// <summary>
/// Exit codes: 0 success; 1 the data fails the run (clock order violation, more than 5% dropped,
/// fewer samples than --min-runs, a malformed mark); 2 the invocation or the input layout is
/// wrong. Contract: scripts/perf/README.md.
/// </summary>
internal static class Program
{
    private const double MaxDropRate = 0.05;
    private static readonly char[] Forbidden = [',', '|', '\n', '\r'];
    private static readonly Regex LogName = new(@"^(?<kind>boot|click)-(?<run>\d+)\.log$", RegexOptions.CultureInvariant);

    public static int Main(string[] args) => Run(args, Console.Error, Console.Out);

    internal static int Run(string[] args, TextWriter err, TextWriter? info = null)
    {
        string? input = null, output = null;
        var minRuns = 20;
        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                string Value() => i + 1 < args.Length ? args[++i] : throw new UsageException($"{args[i]} needs a value");
                switch (args[i])
                {
                    case "--input": input = Value(); break;
                    case "--out": output = Value(); break;
                    case "--min-runs":
                        if (!int.TryParse(Value(), NumberStyles.None, CultureInfo.InvariantCulture, out minRuns))
                            throw new UsageException("--min-runs must be a non-negative integer");
                        break;
                    default: throw new UsageException($"unknown argument '{args[i]}'");
                }
            }
            if (input is null || output is null) throw new UsageException("--input and --out are required");
            // An earlier invocation's artifacts must never be read as this one's: they go now, so an
            // exit 2 below leaves none, and exit 0 or 1 writes fresh ones.
            if (Directory.Exists(output))
                foreach (var artifact in new[] { "samples.csv", "summary.md" })
                    File.Delete(Path.Combine(output, artifact));
            if (!Directory.Exists(input)) throw new UsageException($"--input directory not found: {input}");

            var sideDirs = Directory.GetDirectories(input).OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal).ToList();
            if (sideDirs.Count == 0) throw new UsageException($"--input has no side directories: {input}");

            var failures = new List<string>();
            var sides = new List<SideReport>();
            foreach (var dir in sideDirs) sides.Add(ReadSide(dir, minRuns, failures));

            // A kind present on one side only would compare a number with nothing.
            foreach (var kind in new[] { "boot", "click" })
            {
                var have = sides.Where(s => HasKind(s, kind)).Select(s => s.Name).ToList();
                if (have.Count > 0 && have.Count < sides.Count)
                    failures.Add($"{kind} logs exist for {string.Join(", ", have)} but not for every side");
            }

            // The same ruler set on every side, or the comparison has a number against nothing.
            foreach (var metric in new[] { Metrics.BootFullyDrawn, Metrics.BootTotalTime })
            {
                var have = sides.Where(s => s.Samples.Any(x => x.Run > 0 && x.Metric == metric)).Select(s => s.Name).ToList();
                if (have.Count > 0 && have.Count < sides.Count)
                    failures.Add($"{metric} is reported for {string.Join(", ", have)} but not for every side");
            }

            Directory.CreateDirectory(output);
            // Written even on failure: the partial files are what a person debugs from.
            File.WriteAllText(Path.Combine(output, "samples.csv"), Writers.Csv(sides));
            File.WriteAllText(Path.Combine(output, "summary.md"), Writers.Markdown(sides, minRuns));

            if (failures.Count > 0)
            {
                foreach (var f in failures) err.WriteLine("perf-report: FAIL: " + f);
                return 1;
            }
            info?.WriteLine($"perf-report: wrote samples.csv and summary.md to {output}");
            return 0;
        }
        catch (UsageException e)
        {
            err.WriteLine("perf-report: " + e.Message);
            err.WriteLine("usage: BlazorNative.PerfReport --input <dir> --out <dir> [--min-runs 20]");
            return 2;
        }
    }

    private static bool HasKind(SideReport s, string kind) => kind == "boot" ? s.BootFiles > 0 : s.ClickFiles > 0;

    private static SideReport ReadSide(string dir, int minRuns, List<string> failures)
    {
        var name = Path.GetFileName(dir);
        // The side name is a CSV field and a Markdown cell, both written without quoting: the same
        // rule as meta.json's values.
        if (name.IndexOfAny(Forbidden) >= 0)
            throw new UsageException($"{dir}: a side directory name must not contain commas, pipes or newlines");
        var (sdk, commit) = ReadMeta(dir);
        var side = new SideReport(name, sdk, commit);

        var files = new List<(string Kind, int Run, string Path)>();
        foreach (var path in Directory.GetFiles(dir, "*.log"))
        {
            var m = LogName.Match(Path.GetFileName(path));
            if (!m.Success) throw new UsageException($"{path}: expected <boot|click>-<run>.log");
            var kind = m.Groups["kind"].Value;
            var run = int.Parse(m.Groups["run"].Value, CultureInfo.InvariantCulture);
            var twin = files.FirstOrDefault(f => f.Kind == kind && f.Run == run);
            if (twin.Path is not null)
                throw new UsageException($"{name}: {Path.GetFileName(twin.Path)} and {Path.GetFileName(path)} are both {kind} run {run}");
            files.Add((kind, run, path));
        }
        foreach (var (kind, run, path) in files.OrderBy(f => f.Kind == "boot" ? 0 : 1).ThenBy(f => f.Run))
        {
            if (kind == "boot") side.BootFiles++; else side.ClickFiles++;
            if (run > 0) { if (kind == "boot") side.BootProcesses++; else side.ClickProcesses++; }
            var lines = File.ReadAllLines(path);
            var where = $"{name}/{Path.GetFileName(path)}";
            PairingResult r;
            try
            {
                var marks = new List<Mark>();
                for (var n = 0; n < lines.Length; n++)
                    if (MarkLine.TryParse(lines[n], n + 1, where, out var mark)) marks.Add(mark);
                r = kind == "boot" ? Pairing.PairBoot(marks, lines, run, where) : Pairing.PairClicks(marks, run, where);
            }
            catch (PerfFormatException e)
            {
                // A malformed file fails the run and contributes nothing; the other files are still
                // read, so the partial artifacts hold everything that did parse.
                failures.Add(e.Message);
                continue;
            }
            failures.AddRange(r.Violations);
            side.Samples.AddRange(r.Samples);
            if (run == 0) continue; // warm-up: its samples are kept in the CSV, its drops are not counted
            if (kind == "boot") { side.BootsAttempted += r.Attempted; side.BootsDropped += r.Dropped; }
            else { side.ClicksAttempted += r.Attempted; side.ClicksDropped += r.Dropped; }
            side.Drops.AddRange(r.Drops);
        }

        if (files.Count == 0) { failures.Add($"{name}: no boot-<run>.log or click-<run>.log files"); return side; }

        if (side.BootFiles > 0)
            Check(side, Metrics.Boot, side.BootsAttempted, side.BootsDropped,
                side.Samples.Count(s => s.Run > 0 && s.Metric == Metrics.Boot), minRuns, failures);
        if (side.ClickFiles > 0)
            Check(side, "click", side.ClicksAttempted, side.ClicksDropped,
                side.Samples.Count(s => s.Run > 0 && s.Metric == Metrics.Dispatch && s.Part == Metrics.Total), minRuns, failures);

        // An Android ruler: once any boot log of this side has it, every run above 0 must.
        var bootRuns = files.Where(f => f.Kind == "boot" && f.Run > 0).Select(f => f.Run).ToList();
        foreach (var metric in new[] { Metrics.BootFullyDrawn, Metrics.BootTotalTime })
        {
            // Presence is judged from runs above 0: a ruler seen only in warm-up is not reported.
            if (!side.Samples.Any(s => s.Run > 0 && s.Metric == metric)) continue;
            var have = side.Samples.Where(s => s.Run > 0 && s.Metric == metric).Select(s => s.Run).ToHashSet();
            var missing = bootRuns.Where(run => !have.Contains(run)).ToList();
            foreach (var run in missing) side.Drops.Add($"{name}/boot-{run}.log: no {metric}");
            Check(side, metric, bootRuns.Count, missing.Count, have.Count, minRuns, failures);
        }
        return side;
    }

    private static void Check(SideReport side, string label, int attempted, int dropped, int samples, int minRuns, List<string> failures)
    {
        if (attempted > 0 && dropped > MaxDropRate * attempted)
            failures.Add($"{side.Name}: {dropped} of {attempted} {label} samples dropped, over the 5% limit");
        if (samples < minRuns)
            failures.Add($"{side.Name}: {samples} {label} samples, fewer than --min-runs {minRuns}");
    }

    private static (string Sdk, string Commit) ReadMeta(string dir)
    {
        var path = Path.Combine(dir, "meta.json");
        if (!File.Exists(path)) throw new UsageException($"{path} is missing");
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            string Get(string key)
            {
                if (!doc.RootElement.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(v.GetString()) || v.GetString()!.IndexOfAny(Forbidden) >= 0)
                    throw new UsageException($"{path}: \"{key}\" must be a non-empty string without commas, pipes or newlines");
                return v.GetString()!;
            }
            return (Get("sdk"), Get("commit"));
        }
        catch (JsonException e)
        {
            throw new UsageException($"{path}: {e.Message}");
        }
    }

    private sealed class UsageException(string message) : Exception(message);
}
