using System.Globalization;

namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// One second of a run: the simulating process's CPU and memory, the machine's free memory, the game's fps.
/// </summary>
internal sealed record PerfSample(double Seconds, double CpuPercent, long RamBytes, long FreeBytes, double Fps);

internal enum PerfMetric
{
    Cpu,
    Ram,
    Free,
    Fps,
}

/// <summary>A metric over a run: the latest value, the average, the lowest and the highest.</summary>
internal sealed record PerfStats(double Now, double Average, double Low, double Peak);

/// <summary>Memory sizes as the performance line shows them.</summary>
internal static class Sizes
{
    /// <summary>A size as "500 MB" below a gigabyte and "1.5 GB" from there, in English.</summary>
    public static string Bytes(long bytes) => Bytes(bytes, CultureInfo.InvariantCulture, "{0} MB", "{0} GB");

    /// <summary>
    /// A size in <paramref name="culture"/>, with the caller's unit formats (<c>{0} MB</c>, <c>{0} GB</c>, or their
    /// translations).
    /// </summary>
    public static string Bytes(long bytes, CultureInfo culture, string megabytes, string gigabytes)
    {
        const double MB = 1024 * 1024;
        return bytes < 1024 * MB
            ? string.Format(culture, megabytes, (bytes / MB).ToString("0", culture))
            : string.Format(culture, gigabytes, (bytes / (1024 * MB)).ToString("0.0", culture));
    }
}

/// <summary>
/// A run's performance, one sample a second. Past <see cref="MaxSamples"/> it halves its resolution (keeping every
/// other sample), so a long run keeps its whole span in bounded memory. Godot-free.
/// </summary>
internal sealed class PerfHistory(string runId, string where)
{
    public const int MaxSamples = 600;

    private readonly List<PerfSample> _samples = [];
    private int _stride = 1;
    private int _skipped;

    public string RunId { get; } = runId;

    /// <summary>Which process the CPU and memory are of: <c>host</c> or <c>game</c>.</summary>
    public string Where { get; } = where;

    public IReadOnlyList<PerfSample> Samples => _samples;

    // The latest sample always counts for "now", even when the stride leaves it out of the series.
    private PerfSample? _latest;

    public void Add(PerfSample sample)
    {
        _latest = sample;
        if (++_skipped < _stride)
            return;
        _skipped = 0;
        _samples.Add(sample);
        if (_samples.Count <= MaxSamples)
            return;
        for (var i = 1; i < _samples.Count; i++)
            _samples.RemoveAt(i);
        _stride *= 2;
    }

    public static double Value(PerfSample sample, PerfMetric metric) =>
        metric switch
        {
            PerfMetric.Cpu => sample.CpuPercent,
            PerfMetric.Ram => sample.RamBytes,
            PerfMetric.Free => sample.FreeBytes,
            _ => sample.Fps,
        };

    public IReadOnlyList<double> Series(PerfMetric metric) => [.. _samples.Select(s => Value(s, metric))];

    public PerfStats? Stats(PerfMetric metric)
    {
        if (_latest == null)
            return null;
        var values = Series(metric);
        return new PerfStats(Value(_latest, metric), values.Average(), values.Min(), values.Max());
    }
}
