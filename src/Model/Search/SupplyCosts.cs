namespace MelvorMods.CombatSimulator2.Model.Search;

/// <summary>
/// One supply a setup uses an hour, in its own units: <see cref="Key"/> is the result's <c>used:</c> key (a rune or
/// other item, an ammo, a currency such as prayer points, a toolbelt item's charges).
/// </summary>
public sealed record SupplyCost(string Key, Estimate PerHour)
{
    /// <summary>The supply's name as the game gives it ("Air Rune", "Prayer points"); null until named.</summary>
    public Note? Name { get; init; }
}

/// <summary>
/// What a setup uses, read from its fights' tallies and never priced: supplies cost the skilling time it takes to
/// make them, not coins. Food is not a supply here; it is shown with the damage taken.
/// </summary>
public static class SupplyCosts
{
    public const string Used = "used:";

    // Kinds of used: key that name one supply. The others are totals over several (runes, ammo, food, consumables,
    // potions), shown beside a result's figures but never counted twice here.
    private static readonly string[] OneSupply = ["item:", "ammo:", "currency:", "charges:"];

    /// <summary>
    /// Each supply <paramref name="result"/> used, the most first; <paramref name="isFood"/> picks out eaten food's
    /// items. A master's or a set's are weighted as its other figures are.
    /// </summary>
    public static IReadOnlyList<SupplyCost> Of(TargetResult result, Func<string, bool> isFood)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(isFood);
        return
        [
            .. Keys(result)
                .Where(k => IsSupply(k, isFood))
                .Select(k => (k, e: PerHour(result, k)))
                .Where(p => p.e is { Point: > 0 })
                .Select(p => new SupplyCost(p.k, p.e!))
                .OrderByDescending(c => c.PerHour.Point)
                .ThenBy(c => c.Key, StringComparer.Ordinal),
        ];
    }

    /// <summary>Food eaten an hour; null without fights.</summary>
    public static Estimate? Food(TargetResult result) => PerHour(result, Used + "food");

    /// <summary>Damage taken a second of fighting; null where fights weren't counted.</summary>
    public static Estimate? DamageTaken(TargetResult result) => PerHour(result, FightNumbers.Taken);

    // A target's or master's rate as its figures give it. A set's figures are gains, each target weighted by one over
    // current gear's rate, which would make a supply a share of that: here each target counts the same, in the
    // supply's own units.
    private static Estimate? PerHour(TargetResult result, string key)
    {
        if (result.Kind != TargetKind.Set)
            return Metrics.Estimate(result, key, TimeUnit.Hour);
        var parts = (result.Parts ?? [])
            .Where(p => p.HasFigures)
            .Select(p => PerHour(p, key) ?? new Estimate(0, 0, 0, 0, "ratio"))
            .ToList();
        return parts.Count == 0
            ? null
            : new Estimate(
                parts.Average(p => p.Point),
                parts.Average(p => p.Low),
                parts.Average(p => p.High),
                parts.Min(p => p.N),
                "mean"
            );
    }

    /// <summary>
    /// Whether <paramref name="a"/> uses fewer supplies than <paramref name="b"/>: no more of any and less of at
    /// least one. Two that trade one supply for another are neither.
    /// </summary>
    public static bool FewerSupplies(IReadOnlyList<SupplyCost> a, IReadOnlyList<SupplyCost> b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var less = false;
        foreach (var key in a.Select(c => c.Key).Union(b.Select(c => c.Key), StringComparer.Ordinal))
        {
            var mine = Point(a, key);
            var theirs = Point(b, key);
            if (mine > theirs)
                return false;
            less |= mine < theirs;
        }
        return less;
    }

    /// <summary>How much of <paramref name="key"/> a set uses an hour; 0 when it uses none.</summary>
    public static double Point(IReadOnlyList<SupplyCost> costs, string key) =>
        costs.FirstOrDefault(c => c.Key == key)?.PerHour.Point ?? 0;

    private static bool IsSupply(string key, Func<string, bool> isFood)
    {
        if (!key.StartsWith(Used, StringComparison.Ordinal))
            return false;
        var rest = key[Used.Length..];
        var kind = Array.Find(OneSupply, k => rest.StartsWith(k, StringComparison.Ordinal));
        return kind != null && !(kind == "item:" && isFood(rest[kind.Length..]));
    }

    // Every used: key the result or its parts tallied.
    private static SortedSet<string> Keys(TargetResult result)
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        void Add(TargetResult each)
        {
            foreach (var batch in each.Batches)
            foreach (var used in batch.Used.Keys)
                keys.Add(Used + used);
            foreach (var part in each.Parts ?? [])
                Add(part);
        }
        Add(result);
        return keys;
    }
}
