using MelvorMods.CombatSimulator2.Host.Dev;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Host;

// The host's fault levers and overrides, for cs2dev host.
internal sealed partial class HostClient
{
    /// <summary>The next start fails, as a missing game executable would.</summary>
    public bool FailNextStart { get; set; }

    /// <summary>Hosts started from now report another CS2 build, until the host stops.</summary>
    public bool Mismatch { get; set; }

    /// <summary>The host's hello, once it has said it.</summary>
    public Hello? Hello => _health.Hello;

    /// <summary>How long since the host's last heartbeat.</summary>
    public TimeSpan? HeartbeatAge => _health.HeartbeatAge;

    /// <summary>Overrides the memory the host keeps free for the game.</summary>
    public void SetReserveBytes(long bytes) => ReserveBytes = bytes;

    /// <summary>Terminates the host now (fault injection): reported like a crash.</summary>
    public void Kill() => _process?.Kill();

    /// <summary>Makes the host stop answering (fault injection).</summary>
    public bool Hang() => Send(MessageKind.Dev, new DevMessage("hang"));

    /// <summary>Makes the host crash natively, as a fault in Godot's own code would (fault injection).</summary>
    public bool Crash() => Send(MessageKind.Dev, new DevMessage("crash"));

    /// <summary>Pulls one of the host's own fault levers (see <c>HostServer.DevFrame</c>).</summary>
    public bool Lever(string action) => Send(MessageKind.Dev, new DevMessage(action));

    partial void LaunchEnvironment(Dictionary<string, string> environment)
    {
        EnvironmentOverrides.Apply(environment);
        if (Mismatch)
            environment[HostServer.FakeMvidVariable] = "mismatch-" + Guid.NewGuid().ToString("N")[..8];
    }

    partial void LaunchOptions(ref WorkerOptions options)
    {
        if (!FailNextStart)
            return;
        FailNextStart = false;
        options = options with { ExePathOverride = Path.Combine(Path.GetTempPath(), "cs2-missing", "Melvor2.exe") };
    }

    partial void Stopped() => Mismatch = false;

    /// <summary>Forced collections in the host, and what the last one freed.</summary>
    public CollectWatch Collections { get; } = new();

    partial void Beat(HeartbeatMessage beat)
    {
        Collections.Beat(beat);
        HostCores.Hold(_process?.Pid);
    }
}
