using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine;

/// <summary>
/// The failures seen since CS2 loaded, newest first, at most <see cref="Limit"/>. Any thread may record; each record
/// is logged at once and announced on the main thread, where the panel shows it.
/// </summary>
internal sealed partial class FailureLog
{
    public const int Limit = 20;

    private readonly Lock _lock = new();
    private readonly List<FailureRecord> _records = [];

    /// <summary>A new failure, raised on the main thread.</summary>
    public event Action<FailureRecord>? Recorded;

    public IReadOnlyList<FailureRecord> Newest()
    {
        lock (_lock)
            return [.. _records];
    }

    public void Record(FailureRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        Stamp(ref record);
        lock (_lock)
        {
            _records.Insert(0, record);
            if (_records.Count > Limit)
                _records.RemoveAt(Limit);
        }
        using (LogScope.Push(run: record.RunId, side: record.Side, phase: record.Phase))
            Log.Warn($"failure {FailureRecord.Name(record.Kind)}: {record.Reason.English()}{Detail(record)}");
        MainThread.Post(() => Recorded?.Invoke(record));
    }

    // Debug builds only: names the dev lever that caused the failure.
    static partial void Stamp(ref FailureRecord record);

    private static string Detail(FailureRecord record)
    {
        var parts = new List<string>();
        if (record.Error != null)
            parts.Add(record.Error);
        if (record.HostPid is { } pid)
            parts.Add($"host pid {pid}");
        if (record.ExitCode is { } code)
            parts.Add($"exit code {code}");
        if (record.InFlight.Length > 0)
            parts.Add($"fighting {string.Join(", ", record.InFlight)}");
        if (record.Lever != null)
            parts.Add($"lever {record.Lever}");
        return parts.Count > 0 ? $" ({string.Join("; ", parts)})" : "";
    }
}
