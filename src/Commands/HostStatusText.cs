using MelvorMods.CombatSimulator2.Host;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Commands;

internal static class HostStatusText
{
    /// <summary>
    /// The host's state line, <c>host &lt;state&gt; [pid n] [copies in use/asked] [mem MB heap MB peak MB]
    /// [reason]</c>, as <c>cs2 status</c> shows it. The peak is the active run's so far, or the last run's.
    /// </summary>
    public static string Of(HostClient host)
    {
        var text = new System.Text.StringBuilder("host ").Append(HostHealth.Name(host.State));
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        if (host.Pid is { } pid)
            text.Append(ci, $" pid {pid}");
        var beat = host.LastBeat;
        if (host.State is HostState.Ready or HostState.Busy && beat != null)
        {
            if (host.State == HostState.Busy)
                text.Append(ci, $" copies {beat.Copies}/{host.AskedCopies}");
            var peak =
                host.State == HostState.Busy
                    ? beat.RunPeakPrivateBytes
                    : RemotePool.LastFinished?.PeakPrivateBytes ?? 0;
            text.Append(
                ci,
                $" mem {beat.PrivateBytes / 1048576} MB heap {beat.HeapBytes / 1048576} MB peak {peak / 1048576} MB"
            );
        }
        if (host.Reason != null)
            text.Append(' ').Append(host.Reason.English());
        return text.ToString();
    }
}
