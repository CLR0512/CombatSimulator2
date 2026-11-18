using GamesByMalcs.Melvor2.Components.CombatAreas;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Game.Search;

internal static partial class TargetStatsKey
{
    /// <summary>
    /// What <paramref name="changes"/> leaves behind once undone as a search copy undoes a set: the key before, with
    /// the changes, after the gear alone is put back, and after the whole undo with the enemy spawned again; each
    /// later key as what differs from the first.
    /// </summary>
    public static string Leak(Copy copy, TargetSpec target, ChangeList changes, bool ignoreRequirements)
    {
        if (Targets.Area(copy.Game, target) is not CombatArea area)
            return "not a monster";
        var undo = CopyUndo.Of(copy);
        var gear = GearUndo.Of(copy);
        string Read()
        {
            using var scope = Guards.Enter();
            var loot = copy.Combat.Loot.Stats;
            var doubling =
                $"item-x2={loot.ItemDoublingChance.Total.Value};currency-x2={loot.CurrencyDoublingChance.Total.Value};bury-x2={loot.BuriableDoublingChance.Total.Value};";
            return doubling + (Spawn(copy, target, area) ? Of(copy.Combat) : "never spawned");
        }
        var before = Read();
        copy.Change(changes, ignoreRequirements);
        var worn = Read();
        copy.Reseed(1, target.Key);
        var fought = Driver.Fight(
            copy,
            target,
            Targets.Check(copy.Game, target),
            new DriveOptions(20, new TrialLimits(), ignoreRequirements),
            CancellationToken.None
        );
        var refused = gear.Restore();
        var gearBack = Read();
        var differing = undo.Undo();
        var undone = Read();
        var after = undo.Undo();
        return string.Join(
            "\n",
            "with the changes: " + Diff(before, worn),
            $"fought {fought.TrialsDone} trials, {fought.State}",
            "gear put back (" + refused.Count + " refused): " + Diff(before, gearBack),
            "undone (" + string.Join(", ", differing) + "), spawned again: " + Diff(before, undone),
            "undone again: " + string.Join(", ", after)
        );
    }

    private static string Diff(string a, string b)
    {
        var x = a.Split(';');
        var y = b.Split(';');
        var changed = x.Zip(y).Where(p => p.First != p.Second).Select(p => p.First + " → " + p.Second).ToList();
        if (x.Length != y.Length)
            changed.Add($"{x.Length} parts → {y.Length}");
        return changed.Count == 0 ? "same" : string.Join("; ", changed);
    }
}
