using System.Globalization;
using System.Text;
using GamesByMalcs.Melvor2.Components.Characters;
using GamesByMalcs.Melvor2.Components.CombatAreas;
using GamesByMalcs.Melvor2.Entities.Combat;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Game.Search;

/// <summary>
/// Reads each candidate's stats against the target on a prepared copy, with nothing fought: the enemy is spawned
/// once, then per candidate the gear changes, both sides' numbers are read, and the gear goes back. The target
/// changes the character's own numbers too, so they are read with it present, never from the setup sheet.
/// </summary>
internal static partial class TargetStatsKey
{
    // An enemy spawns some ticks after it is selected (180 on the probe's target); one that hasn't by now never will.
    private const int MostTicks = 2000;

    /// <summary>
    /// How long the keys are read for: each takes milliseconds, so thousands of sets would hold the search's start
    /// for minutes. The sets past it keep no key and are fought on their own.
    /// </summary>
    public static readonly TimeSpan MostTime = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Each candidate's key, in order; null for one whose changes the game refused. Null altogether for a dungeon
    /// (one enemy doesn't stand for the others) or an enemy that didn't spawn: then nothing is merged. Past
    /// <see cref="MostTime"/> the rest are null too; <paramref name="token"/> stops the reading altogether. With
    /// <paramref name="times"/>, each set's milliseconds go there, by part. Sets that share a weapon step over from
    /// one to the next; without <paramref name="stepwise"/> each starts from current gear put back, as a check of the
    /// keys the steps read.
    /// </summary>
    public static IReadOnlyList<string?>? Read(
        Copy copy,
        TargetSpec target,
        GearCatalogue catalogue,
        IReadOnlyList<Candidate> candidates,
        bool ignoreRequirements,
        CancellationToken token,
        List<string>? times = null,
        bool stepwise = true
    )
    {
        ArgumentNullException.ThrowIfNull(copy);
        ArgumentNullException.ThrowIfNull(candidates);
        if (
            target.Kind is TargetKind.Dungeon or TargetKind.Master or TargetKind.Set
            || Targets.Area(copy.Game, target) is not CombatArea area
        )
            return null;
        using var scope = Guards.Enter();
        var combat = copy.Combat;
        if (!Spawn(copy, target, area))
            return null;
        var gear = GearUndo.Of(copy);
        var keys = new string?[candidates.Count];
        var clock = System.Diagnostics.Stopwatch.StartNew();
        double changing = 0,
            reading = 0,
            restoring = 0;
        var done = 0;
        // Sets of one weapon in a row, so each steps over from the last without the weapon coming off; a set of
        // another weapon starts from current gear put back.
        string? Weapon(Candidate c) =>
            c
                .Changes.FirstOrDefault(change =>
                    change.Kind == ChangeKind.Equip && catalogue.Item(change.Value)?.Weapon != null
                )
                ?.Value;
        Candidate? last = null;
        foreach (
            var index in Enumerable
                .Range(0, candidates.Count)
                .OrderBy(i => Weapon(candidates[i]) ?? "", StringComparer.Ordinal)
        )
        {
            token.ThrowIfCancellationRequested();
            if (clock.Elapsed > MostTime)
            {
                Log.Info(
                    $"stats keys: {done} of {candidates.Count} read in {MostTime.TotalSeconds:0} s, the rest fought apart"
                );
                break;
            }
            var candidate = candidates[index];
            var start = clock.Elapsed.TotalMilliseconds;
            bool applied;
            if (stepwise && last != null && Weapon(last) == Weapon(candidate))
                applied = gear.Toward(last.Changes, candidate.Changes, ignoreRequirements);
            else
            {
                if (last != null && gear.Restore().Count > 0)
                {
                    // The gear wouldn't go back: what is read from here on would be of the wrong gear.
                    break;
                }
                var back = clock.Elapsed.TotalMilliseconds;
                restoring += back - start;
                start = back;
                applied = true;
                if (!candidate.Changes.IsEmpty)
                {
                    copy.Change(candidate.Changes, ignoreRequirements);
                    applied = copy.ChangeOutcomes?.All(o => !o.LeftOut) ?? true;
                }
            }
            var changed = clock.Elapsed.TotalMilliseconds;
            keys[index] = applied ? Of(combat) + "\n" + StatsKeys.Rest(catalogue, candidate.Changes) : null;
            var read = clock.Elapsed.TotalMilliseconds;
            changing += changed - start;
            reading += read - changed;
            times?.Add(
                FormattableString.Invariant(
                    $"{candidate.Changes.ShortForm}: change {changed - start:0.0} ms, read {read - changed:0.0}"
                )
            );
            // A set the game refused leaves the gear part-changed: the next starts from current gear put back.
            last = applied ? candidate : null;
            if (!applied)
                gear.Restore();
            done++;
        }
        if (gear.Restore().Count > 0)
            Log.Warn("stats keys: the copy's gear wouldn't go back");
        Log.Info(
            FormattableString.Invariant(
                $"stats keys: {done} read in {clock.Elapsed.TotalSeconds:0.0} s: changing {changing / 1000:0.0} s, reading {reading / 1000:0.0} s, putting back {restoring / 1000:0.0} s"
            )
        );
        return keys;
    }

    // Selects the enemy and ticks until it is in front of the character; false when it never comes.
    private static bool Spawn(Copy copy, TargetSpec target, CombatArea area)
    {
        var combat = copy.Combat;
        combat.Areas.SelectEnemy(area, copy.Game.Data.GetObject<Enemy>(target.Enemy!));
        for (var tick = 0; tick < MostTicks && !Spawned(combat); tick++)
        {
            if (combat.Action.IsPaused)
                combat.Action.Unpause();
            combat.Tick();
        }
        return Spawned(combat);
    }

    private static bool Spawned(CombatEntity combat) => combat.Enemy.Enemy != null && combat.Player.TargetStats != null;

    // Both sides' numbers: each one's own, then each one's against the other.
    private static string Of(CombatEntity combat)
    {
        var text = new StringBuilder();
        Own(text, combat.Player.Stats);
        Own(text, combat.Enemy.Stats);
        Against(text, combat.Player.TargetStats);
        Against(text, combat.Enemy.TargetStats);
        return text.ToString();
    }

    private static void Own(StringBuilder text, CharacterStats stats)
    {
        Add(text, "stance", stats.AttackStance.Value.Id);
        Add(text, "element", stats.Element.Value.Id);
        Add(text, "min", stats.MinHit.Value);
        Add(text, "max", stats.MaxHit.Value);
        Add(text, "acc", stats.AccuracyRating.Value);
        Add(text, "interval", stats.AttackInterval.Value);
        Add(text, "hp", stats.MaxHitpoints.Value);
        Add(text, "ward", stats.MaxWard.Value);
        Add(text, "armour", stats.Armour.Value);
        Add(text, "lifesteal", stats.Lifesteal.Value);
        Add(text, "lifesteal-off", stats.DisableLifesteal.Total.Value);
        Add(text, "crit", stats.CriticalHitChance.Value);
        Add(text, "crit-x", stats.CriticalHitMultiplier.Value);
        Add(text, "reflect", stats.PercentAttackReflect.Total.Value);
        Add(text, "taken", stats.PercentDamageTaken.Total.Value);
        Add(text, "dealt", stats.PercentDamageDealt.Total.Value);
        Add(text, "negate", stats.AttackDamageNegationChance.Total.Value);
        Add(text, "ward-x2", stats.DoubleWardDamageChance.Total.Value);
        foreach (var (stance, node) in stats.EvasionRatings.Dict.OrderBy(e => e.Key.Id, StringComparer.Ordinal))
            Add(text, "evasion:" + stance.Id, node.Value);
        foreach (var (type, node) in stats.PowerRatings.Dict.OrderBy(e => e.Key.Id, StringComparer.Ordinal))
            Add(text, "power:" + type.Id, node.Value);
        foreach (var (type, node) in stats.ProtectionRatings.Dict.OrderBy(e => e.Key.Id, StringComparer.Ordinal))
            Add(text, "protection:" + type.Id, node.Value);
    }

    private static void Against(StringBuilder text, TargetCharacterStats? stats)
    {
        if (stats == null)
        {
            text.Append("none;");
            return;
        }
        Add(text, "hit", stats.HitChance.Value);
        Add(text, "hit+", stats.FlatHitChance.Total.Value);
        Add(text, "taken-from", stats.PercentDamageTakenFrom.Total.Value);
        Add(text, "taken-attacks", stats.PercentDamageTakenFromAttacks.Total.Value);
        Add(text, "immune", stats.ImmunityFrom.Total.Value);
        foreach (var (type, node) in stats.DefenderResistances.Dict.OrderBy(e => e.Key.Id, StringComparer.Ordinal))
            Add(text, "resist:" + type.Id, node.Value);
    }

    // Twelve digits: the game keeps running totals, whose last digits drift as gear comes and goes, so two sets with
    // the same numbers could otherwise read apart.
    private static void Add(StringBuilder text, string name, double value) =>
        text.Append(name).Append('=').Append(value.ToString("G12", CultureInfo.InvariantCulture)).Append(';');

    private static void Add(StringBuilder text, string name, string value) =>
        text.Append(name).Append('=').Append(value).Append(';');
}
