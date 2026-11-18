using System.Text.RegularExpressions;

namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// Where a lost host was, read back from the context block of its last log lines (<c>run=… phase=… target=… |</c>):
/// the phase of its run's last line, and the targets that had lines in the fight but no <c>fought</c> line after.
/// </summary>
public static partial class HostLineScope
{
    [GeneratedRegex(@"\] (?:WARN |ERROR )?((?:[a-z]+=\S+ ?)+)\| (.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex Line();

    /// <summary>
    /// The phase name and targets in flight of <paramref name="runId"/> (or, when null, of the last run named) in
    /// <paramref name="lines"/>, oldest first; a null phase when the run's last line has none.
    /// </summary>
    public static (string? Phase, string[] InFlight) Of(IReadOnlyList<string> lines, string? runId)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var parsed = new List<(Dictionary<string, string> Keys, string Text)>();
        foreach (var line in lines)
        {
            if (Line().Match(line) is not { Success: true } match)
                continue;
            var keys = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var equals = pair.IndexOf('=', StringComparison.Ordinal);
                if (equals > 0)
                    keys[pair[..equals]] = pair[(equals + 1)..];
            }
            parsed.Add((keys, match.Groups[2].Value));
        }
        var run = runId ?? parsed.LastOrDefault(p => p.Keys.ContainsKey("run")).Keys?.GetValueOrDefault("run");
        if (run == null)
            return (null, []);
        var ofRun = parsed.Where(p => p.Keys.GetValueOrDefault("run") == run).ToList();
        if (ofRun.Count == 0)
            return (null, []);
        var phase = ofRun[^1].Keys.GetValueOrDefault("phase");
        var fighting = new List<string>();
        foreach (var (keys, text) in ofRun)
        {
            if (keys.GetValueOrDefault("phase") != "fight" || keys.GetValueOrDefault("target") is not { } target)
                continue;
            fighting.Remove(target);
            if (!text.StartsWith("fought ", StringComparison.Ordinal))
                fighting.Add(target);
        }
        return (phase, phase == "fight" ? [.. fighting] : []);
    }
}
