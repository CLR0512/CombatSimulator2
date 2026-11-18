using MelvorMods.CombatSimulator2.Model;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game;

// A simulation line's own ids: the targets the page leaves out, and the metric it ranks by.
internal static partial class LineIds
{
    private const int TargetBytes = 3;

    /// <summary>
    /// The ignored targets as ids: each key as its hash, or every key in full when one shares its hash with another
    /// target of this build.
    /// </summary>
    public static List<LineId> Ignored(MelvorGame game, IEnumerable<string> keys)
    {
        var all = TargetKeys(game);
        var chosen = keys.Order(StringComparer.Ordinal).ToList();
        var full = chosen.Any(k =>
            all.Any(other => other != k && IdHash.Of(other, TargetBytes) == IdHash.Of(k, TargetBytes))
        );
        return [.. chosen.Select(k => full ? new LineId(0, k) : new LineId(IdHash.Of(k, TargetBytes)))];
    }

    /// <summary>The target keys the ids name in this build, and how many name none.</summary>
    public static (List<string> Keys, int Unknown) IgnoredKeys(MelvorGame game, IEnumerable<LineId> ids)
    {
        var all = TargetKeys(game);
        var keys = new List<string>();
        var unknown = 0;
        foreach (var id in ids)
        {
            if (Find(all, id, TargetBytes) is { } key)
                keys.Add(key);
            else
                unknown++;
        }
        return (keys, unknown);
    }

    /// <summary>A metric key as a line carries it: always in full, since it is CS2's own and short.</summary>
    public static LineId Metric(string key) => new(0, key);

    private static List<string> TargetKeys(MelvorGame game) => [.. Targets.All(game).Select(t => t.Key)];
}
