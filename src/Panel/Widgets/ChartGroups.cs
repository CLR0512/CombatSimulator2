using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Results;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>Builds the chart's areas and bars for a run: one group per area in run order, folded as set.</summary>
internal static class ChartGroups
{
    /// <summary>
    /// The groups for <paramref name="run"/>'s ranked targets. An area without results collapses unless it is in
    /// <paramref name="openedAreas"/>; one in <paramref name="foldedAreas"/> shows its best target only.
    /// </summary>
    public static List<Chart.Group> Build(
        Run run,
        RankTable table,
        Func<string, Texture2D?> areaArt,
        Func<TargetSpec, Texture2D?> targetArt,
        IEnumerable<string> foldedAreas,
        IEnumerable<string> openedAreas
    )
    {
        var rows = table.Rows.ToDictionary(r => r.Key, StringComparer.Ordinal);
        var folded = foldedAreas.ToHashSet(StringComparer.Ordinal);
        var opened = openedAreas.ToHashSet(StringComparer.Ordinal);
        var groups = new List<Chart.Group>();
        Chart.Group? current = null;
        foreach (var key in run.Targets.Distinct(StringComparer.Ordinal))
        {
            if (!rows.TryGetValue(key, out var rank))
                continue;
            var spec = TargetSpec.FromKey(key);
            var info = PanelState.Catalog?.Find(key);
            var (name, area) = RankList.Names(rank, info);
            // The area as replies give it: only a dungeon's differs, as its area is a word of CS2's.
            var areaEnglish = spec.Enemy == null ? RankList.Names(rank, info, english: true).Area : area;
            if (current == null || current.Id != spec.Area)
            {
                var groupName = spec.Enemy == null ? name : area;
                current = new Chart.Group(spec.Area, groupName, areaArt(spec.Area), [], folded.Contains(spec.Area));
                groups.Add(current);
            }
            var result = run.Results.GetValueOrDefault(key);
            var death = RunCache.DeathRate(run, key);
            current.Bars.Add(new Chart.BarItem(key, name, area, areaEnglish, rank, death, false, targetArt(spec)));
        }
        // An area without results collapses unless opened; a folded area shows its best survived target only, or
        // the stub of one that died every fight when none survived. Died targets stand last in their area.
        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            if (group.Bars.Any(b => b.Rank.Died))
                groups[i] = group = group with { Bars = [.. group.Bars.OrderBy(b => b.Rank.Died)] };
            if (group.AllEmpty)
            {
                groups[i] = group with { Collapsed = !opened.Contains(group.Id) };
                continue;
            }
            if (!group.Folded || group.Bars.Count == 0)
                continue;
            var best = group
                .Bars.OrderBy(b =>
                    b.Rank.Rank != null ? 0
                    : b.Rank.Died ? 1
                    : 2
                )
                .ThenByDescending(b => b.Rank.OfBest)
                .ThenBy(b => b.Rank.Rank ?? int.MaxValue)
                .First();
            groups[i] = group with { Bars = [best with { Folded = true }] };
        }
        return groups;
    }
}
