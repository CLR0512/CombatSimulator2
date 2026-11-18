using System.Globalization;
using System.Text.RegularExpressions;

namespace MelvorMods.CombatSimulator2.Model;

/// <summary>Whether the runtime wrote a dump as the host died, or the game wrote it of a host that stopped answering.</summary>
internal enum DumpKind
{
    Crash,
    Hang,
}

/// <summary>A host dump kept in the dump folder.</summary>
/// <param name="File">The file's name, without its folder.</param>
/// <param name="At">The file's time, in UTC.</param>
internal sealed partial record HostDumpInfo(string File, int Pid, DateTime At, long Bytes, DumpKind Kind)
{
    /// <summary>The runtime fills in the pid and the time; <c>.hang</c> marks a dump the game wrote.</summary>
    public const string NamePattern = "CombatSimulator2.host.%p.%t.dmp";

    /// <summary>The name the game gives a hung host's dump.</summary>
    public static string HangName(int pid, DateTime utc) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"CombatSimulator2.host.{pid}.{new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeSeconds()}.hang.dmp"
        );

    /// <summary>The pid and kind a dump's name says, or null for a file that isn't a host dump.</summary>
    public static (int Pid, DumpKind Kind)? Parse(string name)
    {
        var match = Name().Match(name);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var pid))
            return null;
        return (pid, match.Groups[2].Success ? DumpKind.Hang : DumpKind.Crash);
    }

    [GeneratedRegex(@"^CombatSimulator2\.host\.(\d+)\.\d+(\.hang)?\.dmp$", RegexOptions.CultureInvariant)]
    private static partial Regex Name();
}
