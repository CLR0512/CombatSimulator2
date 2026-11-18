using System.Text.Json.Serialization;

namespace MelvorMods.CombatSimulator2.Model;

/// <summary>Where a rare drop comes from.</summary>
public enum DropSource
{
    /// <summary>An enemy's loot table, rolled per kill.</summary>
    Loot,

    /// <summary>A dungeon's reward table, rolled per completion.</summary>
    Reward,

    /// <summary>One of the game's rare events, rolled once per minute in combat.</summary>
    RareEvent,
}

/// <summary>What a drop's chance is per.</summary>
public enum ChancePer
{
    Kill,
    Completion,
    MinuteInCombat,
}

/// <summary>A rare drop the game defines for a target, with the game's own chance for it.</summary>
public sealed class DropChance
{
    public DropSource Source { get; set; }

    /// <summary>The item's id (loot, rewards), or the rare event's id (as the run counts it).</summary>
    public string Id { get; set; } = "";

    /// <summary>The item a rare item event gives; null otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Item { get; set; }

    /// <summary>
    /// The game's chance: for loot and rewards, per kill or completion (the table's chance times the item's share of
    /// it); for a rare event, per roll. Null when the game gives none.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Chance { get; set; }

    public ChancePer Per { get; set; }

    /// <summary>
    /// The items one drop of a loot or reward entry gives on average with the setup fought: the middle of the entry's
    /// quantity range, doubled at the setup's chance to double loot, and again at its chance to double buryable
    /// items. Null for rare events, for an item the target also drops some other way (a fixed drop, a common entry,
    /// a rare event), and in older runs: the run can't tell those drops apart, so only what it counted stands.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Quantity { get; set; }

    /// <summary>
    /// A rare event's requirements the setup doesn't meet, in the game's words; empty when it can happen.
    /// </summary>
    public List<Note> Unmet { get; set; } = [];
}

/// <summary>
/// One rare drop as shown: the game's chance with what it means in the chosen unit, and what the run saw.
/// <c>Expected</c>: loot and rewards only, the chance times the simulated kills (or completions) rate, its range the
/// rate's; null for rare events, whose chance stays per minute in combat as the game rolls it. <c>ObservedRate</c>:
/// rare events only, from the run's own count. <c>ObservedOnly</c>: seen in the run but not in the game's list.
/// <c>ForOne</c>: what one takes; null without the game's chance.
/// </summary>
public sealed record RareDropRow(
    DropSource Source,
    string Id,
    double? Chance,
    ChancePer Per,
    Estimate? Expected,
    long Observed,
    Estimate? ObservedRate,
    IReadOnlyList<Note> Unmet,
    bool ObservedOnly,
    ForOne? ForOne = null
)
{
    public bool Possible => Unmet.Count == 0;
}

/// <summary>
/// Rare drops for display: loot and reward entries below <see cref="Threshold"/>, and every rare event.
/// </summary>
public static class RareDrops
{
    /// <summary>
    /// The items of <paramref name="item"/> one kill (or completion) of <paramref name="result"/> gives on average,
    /// from the game's chances: every listed entry for it, each chance times its <see cref="DropChance.Quantity"/>.
    /// Null when an entry has no quantity or none lists the item, so the item's rate is what the run counted.
    /// </summary>
    public static double? ExpectedPerKill(TargetResult result, string item)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Drops is not { } drops)
            return null;
        double? sum = null;
        foreach (var drop in drops)
        {
            if (drop.Source == DropSource.RareEvent || drop.Id != item)
                continue;
            if (drop.Chance is not { } chance || drop.Quantity is not { } quantity)
                return null;
            sum = (sum ?? 0) + chance * quantity;
        }
        return sum;
    }

    /// <summary>A loot or reward entry is rare below this chance per kill (or completion).</summary>
    public const double Threshold = 0.01;

    /// <summary>
    /// The rows of <paramref name="result"/> in <paramref name="unit"/>: its listed drops in their order, then rare
    /// events the run saw that the list doesn't name. None for results without estimates or saved before drops were
    /// kept.
    /// </summary>
    public static IReadOnlyList<RareDropRow> Rows(TargetResult result, TimeUnit unit, PriceTable? prices = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.HasEstimates || result.Drops is not { } drops)
            return [];
        var kills = Metrics.Estimate(result, Metrics.Kills, unit, prices);
        var rows = new List<RareDropRow>();
        foreach (var drop in drops)
        {
            if (drop.Source == DropSource.RareEvent)
            {
                rows.Add(Event(result, drop.Id, drop, unit, prices));
                continue;
            }
            if (drop.Chance is not { } chance || chance >= Threshold)
                continue;
            rows.Add(
                new RareDropRow(
                    drop.Source,
                    drop.Id,
                    chance,
                    drop.Per,
                    kills?.Scale(chance),
                    result.Batches.Sum(b => b.Gained.GetValueOrDefault(drop.Id)),
                    null,
                    drop.Unmet,
                    false,
                    ForOnes.Of(result, drop)
                )
            );
        }
        var listed = drops
            .Where(d => d.Source == DropSource.RareEvent)
            .Select(d => d.Id)
            .ToHashSet(StringComparer.Ordinal);
        var seen = result.Batches.SelectMany(b => b.RareEvents.Keys).Distinct().Order(StringComparer.Ordinal);
        foreach (var id in seen.Where(id => !listed.Contains(id)))
            rows.Add(Event(result, id, null, unit, prices));
        return rows;
    }

    private static RareDropRow Event(
        TargetResult result,
        string id,
        DropChance? drop,
        TimeUnit unit,
        PriceTable? prices
    ) =>
        new(
            DropSource.RareEvent,
            id,
            drop?.Chance,
            ChancePer.MinuteInCombat,
            null,
            result.Batches.Sum(b => (long)b.RareEvents.GetValueOrDefault(id)),
            Metrics.Estimate(result, "rare:" + id, unit, prices),
            drop?.Unmet ?? [],
            drop == null,
            drop != null ? ForOnes.Of(result, drop) : null
        );
}
