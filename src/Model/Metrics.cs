namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// A quantity the chart and details can show: its key, what it counts (<see cref="Kind"/>: "Kills", "XP") and of
/// what (<see cref="Subject"/>: a skill or item, when there is one), and whether it is a rate over time
/// (<see cref="Timed"/>, which takes a time unit).
/// </summary>
public sealed record Metric(string Key, Note Kind, bool Timed, Note? Subject = null)
{
    /// <summary>The metric in English, as replies and tests show it: "Kills", "XP: Attack".</summary>
    public string Label => Subject == null ? Kind.English() : Kind.English() + ": " + Subject.English();
}

public sealed record MetricValue(Metric Metric, Estimate Estimate);

/// <summary>An item's sell price: <see cref="Quantity"/> of <see cref="Currency"/>.</summary>
public sealed record Price(string Currency, long Quantity);

/// <summary>Sell prices by item id, read from the game's data.</summary>
public sealed class PriceTable(IReadOnlyDictionary<string, Price> prices)
{
    public static readonly PriceTable Empty = new(new Dictionary<string, Price>());

    public bool TryGet(string item, out Price price) => prices.TryGetValue(item, out price!);

    /// <summary>Every price, by item id.</summary>
    public IReadOnlyDictionary<string, Price> All => prices;
}

/// <summary>
/// The metric list, built from the keys present in results (never a fixed list), and estimates from batches.
/// Keys: <c>kills</c>, <c>deaths</c>, <c>deathRate</c>, <c>killTime</c>, <c>highestHit</c>, <c>lowestHp</c>,
/// <c>highestReflect</c>, <c>drops</c>, <c>xp:&lt;skill&gt;</c>, <c>item:&lt;id&gt;</c>, <c>currency:&lt;id&gt;</c>,
/// <c>value:&lt;currency&gt;</c>, <c>worth:&lt;currency&gt;</c>, <c>bury:&lt;currency&gt;</c>, <c>used:&lt;key&gt;</c>,
/// <c>rare:&lt;event&gt;</c>. Food, potions, consumables, runes and ammo are tallied as <c>used:food</c>,
/// <c>used:potions</c>, <c>used:consumables</c>, <c>used:runes</c> and <c>used:ammo</c> beside each item's own.
/// </summary>
public static partial class Metrics
{
    public const string Kills = "kills";
    public const string Deaths = "deaths";
    public const string DeathRate = "deathRate";
    public const string KillTime = "killTime";

    /// <summary>The biggest hit taken in any trial: one number over the trials, not a rate.</summary>
    public const string HighestHit = "highestHit";

    /// <summary>The lowest HP reached in a trial the player survived; none when every trial died.</summary>
    public const string LowestHp = "lowestHp";

    /// <summary>The largest damage reflected back at the player in any trial.</summary>
    public const string HighestReflect = "highestReflect";

    /// <summary>Every item dropped, counted together.</summary>
    public const string Drops = "drops";

    /// <summary>
    /// XP in the run's main skill: the stance's skill, else the non-Hitpoints skill with the most XP.
    /// </summary>
    public const string MainXp = "xp:main";

    private const string Hitpoints = "MelvorBase:Hitpoints";

    private const string SlayerSkill = "MelvorBase:Slayer";

    /// <summary>The currency prayers spend.</summary>
    public const string PrayerPoints = "MelvorBase:PrayerPoints";

    /// <summary>
    /// The metric key <paramref name="key"/> stands for in <paramref name="run"/>: <see cref="MainXp"/> becomes
    /// <c>xp:&lt;skill&gt;</c> for the run's <see cref="Run.MainSkill"/>, or, for runs saved without one, the skill
    /// other than Hitpoints with the most XP in the finished results. A run of Slayer masters alone stands for Slayer
    /// XP instead: their monsters can fight in other styles through their loadouts, and every task earns Slayer XP.
    /// Other keys are returned as they are.
    /// </summary>
    public static string Resolve(Run run, string key)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (key != MainXp)
            return key;
        if (run.Targets.Count > 0 && run.Targets.All(Ids.IsMaster))
            return "xp:" + SlayerSkill;
        if (!string.IsNullOrEmpty(run.MainSkill))
            return "xp:" + run.MainSkill;
        var totals = new Dictionary<string, double>(StringComparer.Ordinal);
        // A master's XP is its monsters'.
        foreach (var tally in Finished(run.Results.Values).SelectMany(r => r.Batches))
        foreach (var (skill, xp) in tally.Xp)
            if (skill != Hitpoints)
                totals[skill] = totals.GetValueOrDefault(skill) + xp;
        var top = totals.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).FirstOrDefault();
        return top.Key is { } skillId ? "xp:" + skillId : Kills;
    }

    /// <summary>
    /// Every metric present in the finished results, fixed metrics first, then by key; with XP in each of
    /// <paramref name="xpSkills"/> whether or not a result earned any, so a skill can be chosen before anything trains it.
    /// </summary>
    public static IReadOnlyList<Metric> For(
        IEnumerable<TargetResult> results,
        PriceTable? prices = null,
        Func<string, string>? names = null,
        IEnumerable<string>? xpSkills = null
    )
    {
        prices ??= PriceTable.Empty;
        names ??= id => id;
        var given = results.ToList();
        var finished = Finished(given);
        if (finished.Count == 0)
            return [];
        var dungeons = finished.Count(r => r.Kind == TargetKind.Dungeon);
        var killsLabel =
            dungeons == 0 ? Note.Of("Kills")
            : dungeons == finished.Count ? Note.Of("Completions")
            : Note.Of("Kills or completions");
        var timeLabel =
            dungeons == 0 ? Note.Of("Kill time (s)")
            : dungeons == finished.Count ? Note.Of("Clear time (s)")
            : Note.Of("Kill or clear time (s)");
        var list = new List<Metric>
        {
            new(Kills, killsLabel, true),
            new(Deaths, Note.Of("Deaths"), true),
            new(DeathRate, Note.Of("Death rate"), false),
            new(KillTime, timeLabel, false),
        };
        if (given.All(r => r.Kind == TargetKind.Master))
            list.RemoveAll(m => m.Key == DeathRate);
        if (finished.Any(r => r.HasFightCounts))
            list.AddRange([Describe(FightNumbers.Hit), Describe(FightNumbers.Dps), Describe(FightNumbers.Taken)]);
        if (finished.Any(r => Risk.Of(r) is { Recorded: true }))
            list.AddRange([Describe(HighestHit), Describe(LowestHp)]);
        if (finished.Any(r => r.Batches.Any(b => b.MaxReflect > 0)))
            list.Add(Describe(HighestReflect));
        if (finished.Any(r => r.Batches.Any(b => b.Gained.Count > 0)))
            list.Add(Describe(Drops));
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var skill in xpSkills ?? [])
            keys.Add("xp:" + skill);
        foreach (var tally in finished.SelectMany(r => r.Batches))
        {
            foreach (var skill in tally.Xp.Keys)
                keys.Add("xp:" + skill);
            foreach (var item in tally.Gained.Keys)
            {
                keys.Add("item:" + item);
                if (prices.TryGet(item, out var price))
                {
                    keys.Add("value:" + price.Currency);
                    keys.Add("worth:" + price.Currency);
                }
            }
            foreach (var currency in tally.Currency.Keys)
                keys.Add("currency:" + currency);
            foreach (var used in tally.Used.Keys)
                keys.Add("used:" + used);
            foreach (var rare in tally.RareEvents.Keys)
                keys.Add("rare:" + rare);
            foreach (var currency in tally.Buryable?.Keys ?? Enumerable.Empty<string>())
                keys.Add("bury:" + currency);
        }
        // A rare item the game's chances cover has a rate whether or not a fight happened to drop it.
        foreach (var result in finished)
        foreach (var drop in result.Drops ?? [])
            if (drop.Source != DropSource.RareEvent && RareDrops.ExpectedPerKill(result, drop.Id) != null)
                keys.Add("item:" + drop.Id);
        list.AddRange(
            keys.OrderBy(Order)
                .ThenBy(CurrencyOf, StringComparer.Ordinal)
                .ThenBy(MoneyOrder)
                .ThenBy(k => k, StringComparer.Ordinal)
                .Select(k => Describe(k, names))
        );
        return list;
    }

    /// <summary>A metric for <paramref name="key"/> with its label, whether or not any result has it.</summary>
    public static Metric Describe(string key, Func<string, string>? names = null)
    {
        names ??= id => id;
        return key switch
        {
            Kills => new(Kills, Note.Of("Kills"), true),
            Deaths => new(Deaths, Note.Of("Deaths"), true),
            DeathRate => new(DeathRate, Note.Of("Death rate"), false),
            KillTime => new(KillTime, Note.Of("Kill time (s)"), false),
            HighestHit => new(HighestHit, Note.Of("Highest hit taken"), false),
            LowestHp => new(LowestHp, Note.Of("Lowest HP"), false),
            HighestReflect => new(HighestReflect, Note.Of("Highest reflect taken"), false),
            Drops => new(Drops, Note.Of("Drops"), true),
            FightNumbers.Hit => new(key, Note.Of("Chance to hit"), false),
            FightNumbers.PerHit => new(key, Note.Of("Damage per hit"), false),
            FightNumbers.Dps => new(key, Note.Of("Damage per second"), false),
            FightNumbers.EnemyHit => new(key, Note.Of("Enemy's chance to hit"), false),
            FightNumbers.EnemyPerHit => new(key, Note.Of("Enemy's damage per hit"), false),
            FightNumbers.Taken => new(key, Note.Of("Damage taken per second"), false),
            FightNumbers.Overkill => new(key, Note.Of("Overkill"), false),
            FightNumbers.AttacksPerKill => new(key, Note.Of("Attacks per kill"), false),
            FightNumbers.MissedPerKill => new(key, Note.Of("Missed per kill"), false),
            // Translators: A stat: the share of trials that passed the time limit.
            FightNumbers.RanOver => new(key, Note.Of("Ran over"), false),
            _ => new(key, Prefix(key), true, Name(key, names)),
        };
    }

    /// <summary>
    /// The estimate of one metric for a finished result in <paramref name="unit"/>; null when it has none.
    /// </summary>
    public static Estimate? Estimate(TargetResult result, string key, TimeUnit unit, PriceTable? prices = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Kind == TargetKind.Master)
            return MasterEstimate(result, key, unit, prices);
        if (result.Kind == TargetKind.Set)
            return SetSeries(result, key, unit, prices) is { } set
                ? Estimators.RatioOfBatches(set.Values, set.Denominators)
                : null;
        if (!result.HasEstimates)
            return null;
        if (FightNumbers.IsFightKey(key))
            return FightNumbers.Estimate(result, key);
        var batches = result.Batches;
        switch (key)
        {
            case DeathRate:
                return Estimators.Wilson(batches.Sum(b => b.Deaths), batches.Sum(b => b.Trials));
            case KillTime:
                // No kills at all: the time per kill is unbounded, never zero.
                if (batches.Sum(b => b.Kills) == 0)
                    return new Estimate(
                        double.PositiveInfinity,
                        double.PositiveInfinity,
                        double.PositiveInfinity,
                        batches.Count,
                        "ratio"
                    );
                return Estimators.RatioOfBatches(Series(batches, b => b.GameSeconds), Series(batches, b => b.Kills));
            case HighestHit or LowestHp:
                return Extreme(result, key);
            case HighestReflect:
                return batches.Max(b => b.MaxReflect) is { } reflect
                    ? new Estimate(reflect, reflect, reflect, batches.Count, "extreme")
                    : null;
        }
        var values = Values(result, key, prices ?? PriceTable.Empty);
        if (values == null)
            return null;
        return Estimators.RatioOfBatches(values, Series(batches, b => TimeUnits.Denominator(b, unit)));
    }

    /// <summary>
    /// The per-batch sums behind a ratio metric's estimate: values over denominators (time in
    /// <paramref name="unit"/>, or kills for time per kill). Null for death rate (a proportion), unknown keys and
    /// results without estimates.
    /// </summary>
    public static (double[] Values, double[] Denominators)? Series(
        TargetResult result,
        string key,
        TimeUnit unit,
        PriceTable? prices = null
    )
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(key);
        if (result.Kind == TargetKind.Master)
            return MasterSeries(result, key, unit, prices);
        if (result.Kind == TargetKind.Set)
            return SetSeries(result, key, unit, prices);
        if (!result.HasEstimates || key is DeathRate or HighestHit or LowestHp or HighestReflect)
            return null;
        if (FightNumbers.IsFightKey(key))
            return FightNumbers.Series(result, key);
        var batches = result.Batches;
        if (key == KillTime)
            return (Series(batches, b => b.GameSeconds), Series(batches, b => b.Kills));
        var values = Values(result, key, prices ?? PriceTable.Empty);
        return values == null ? null : (values, Series(batches, b => TimeUnits.Denominator(b, unit)));
    }

    /// <summary>Every metric of one result in <paramref name="unit"/>, in list order.</summary>
    public static IReadOnlyList<MetricValue> All(
        TargetResult result,
        TimeUnit unit,
        PriceTable? prices = null,
        Func<string, string>? names = null
    ) =>
        [
            .. For([result], prices, names)
                .Select(m => (m, e: Estimate(result, m.Key, unit, prices)))
                .Where(p => p.e != null)
                .Select(p => new MetricValue(p.m, p.e!)),
        ];

    /// <summary>The trials needed for ±5% on kills (or completions) per time.</summary>
    public static int TrialsFor5Percent(TargetResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.HasEstimates)
            return int.MaxValue;
        var batches = result.Batches;
        var perBatch = batches.Sum(b => b.Trials) / (double)batches.Count;
        return Estimators.TrialsFor(0.05, Series(batches, b => b.Kills), Series(batches, b => b.GameSeconds), perBatch);
    }

    private static double[]? Values(TargetResult result, string key, PriceTable prices)
    {
        var batches = result.Batches;
        switch (key)
        {
            case Kills:
                return Series(batches, b => b.Kills);
            case Deaths:
                return Series(batches, b => b.Deaths);
            case Drops:
                return Series(batches, b => b.Gained.Values.Sum(q => (double)q));
        }
        var colon = key.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
            return null;
        var id = key[(colon + 1)..];
        return key[..colon] switch
        {
            "xp" => Series(batches, b => b.Xp.GetValueOrDefault(id)),
            "item" => ItemSeries(result, id),
            "currency" => Series(batches, b => b.Currency.GetValueOrDefault(id)),
            "used" => Series(batches, b => b.Used.GetValueOrDefault(id)),
            "rare" => Series(batches, b => b.RareEvents.GetValueOrDefault(id)),
            "value" => Series(batches, b => ValueIn(b, id, prices)),
            "worth" => Series(batches, b => ValueIn(b, id, prices) + b.Currency.GetValueOrDefault(id)),
            "bury" => Series(batches, b => b.Buryable?.GetValueOrDefault(id) ?? 0),
            _ => null,
        };
    }

    // A rare item drops a handful of times in a run, so its count says more about luck than about the setup: where
    // the game's chances cover every way it drops, it is the kills (or completions) times the items each one gives
    // on average.
    private static double[] ItemSeries(TargetResult result, string id) =>
        RareDrops.ExpectedPerKill(result, id) is { } each
            ? Series(result.Batches, b => b.Kills * each)
            : Series(result.Batches, b => b.Gained.GetValueOrDefault(id));

    // The worst over every trial: one value, so its range is the value itself.
    private static Estimate? Extreme(TargetResult result, string key)
    {
        if (Risk.Of(result) is not { Recorded: true } risk)
            return null;
        double? value = key == HighestHit ? risk.HighestHit : risk.LowestHp;
        return value is { } v ? new Estimate(v, v, v, result.Batches.Count, "extreme") : null;
    }

    private static double ValueIn(Tally tally, string currency, PriceTable prices)
    {
        double sum = 0;
        foreach (var (item, quantity) in tally.Gained)
            if (prices.TryGet(item, out var price) && price.Currency == currency)
                sum += quantity * (double)price.Quantity;
        return sum;
    }

    private static double[] Series(List<Tally> batches, Func<Tally, double> pick)
    {
        var series = new double[batches.Count];
        for (var i = 0; i < series.Length; i++)
            series[i] = pick(batches[i]);
        return series;
    }

    // Items come last: a run can drop hundreds, and the shorter lists after them would be out of reach. What is used
    // comes before them, its totals (food, consumables) first.
    private static int Order(string key) =>
        key[..key.IndexOf(':', StringComparison.Ordinal)] switch
        {
            "xp" => 0,
            "worth" or "value" or "currency" or "bury" => 1,
            "used" when key.IndexOf(':', 5) < 0 => 2,
            "used" => 3,
            "item" => 5,
            _ => 4,
        };

    // The money rows are kept together per currency, so every Gold Pieces row sits beside the others.
    private static string CurrencyOf(string key) =>
        Order(key) == 1 ? key[(key.IndexOf(':', StringComparison.Ordinal) + 1)..] : "";

    private static int MoneyOrder(string key) =>
        key[..key.IndexOf(':', StringComparison.Ordinal)] switch
        {
            "worth" => 0,
            "value" => 1,
            "currency" => 2,
            "bury" => 3,
            _ => 0,
        };

    private static Note Prefix(string key) =>
        key[..Math.Max(0, key.IndexOf(':', StringComparison.Ordinal))] switch
        {
            "xp" => Note.Of("XP"),
            "item" => Note.Of("Item"),
            "currency" => Note.Of("Dropped"),
            "value" => Note.Of("Value"),
            // Translators: A metric: dropped items at their sell price plus the currency dropped itself.
            "worth" => Note.Of("Total value"),
            // Translators: A metric: what the bones dropped would give if buried, such as prayer points.
            "bury" => Note.Of("If buried"),
            "used" => Note.Of("Used"),
            "rare" => Note.Of("Rare event"),
            var other => Note.Text(other),
        };

    // The game's name for the id in a key; `used:` keys carry their own kind (`item:`, `ammo:`, `currency:`, …).
    private static Note Name(string key, Func<string, string> names)
    {
        var rest = key[(key.IndexOf(':', StringComparison.Ordinal) + 1)..];
        if (!key.StartsWith("used:", StringComparison.Ordinal))
            return Note.Text(names(rest));
        var colon = rest.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
            return rest switch
            {
                "food" => Note.Of("Food"),
                "consumables" => Note.Of("Consumables"),
                "potions" => Note.Of("Potions"),
                "runes" => Note.Of("Runes"),
                "ammo" => Note.Of("Ammo"),
                _ => Note.Text(rest),
            };
        var kind = rest[..colon];
        var id = rest[(colon + 1)..];
        if (kind == "currency" && id == PrayerPoints)
            return Note.Of("Prayer points");
        var name = names(id);
        return kind switch
        {
            "ammo" => Note.Of("{0} (ammo)", name),
            "charges" => Note.Of("{0} (charges)", name),
            _ => Note.Text(name),
        };
    }
}
