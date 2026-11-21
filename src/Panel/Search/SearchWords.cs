using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Search;

/// <summary>How the Gear tab words a search's numbers: every one is from the answer's fights.</summary>
internal static class SearchWords
{
    /// <summary>A button of the Gear tab: in an equipment slot's frame.</summary>
    public static Godot.Button Button(string text, string name, Action pressed) =>
        FramedButton.Make(Ui.Button(text, "", name, pressed), 6, 2);

    /// <summary>
    /// A search's time left in whole seconds, "45s left", "2m 05s left", "1h 02m 05s left"; "finishing" once its steps
    /// are done; empty while too early to say.
    /// </summary>
    public static string TimeLeft(double? seconds, bool atMost = false)
    {
        if (seconds is not { } left)
            return "";
        if (left < 1)
            // Translators: A gear search with nothing left to fight but its last checks.
            return Tr.Get("finishing");
        var whole = (long)Math.Ceiling(left);
        var (h, m, sec) = (whole / 3600, whole / 60 % 60, (whole % 60).ToString("00", Tr.Culture));
        if (atMost)
            return h > 0
                    // Translators: A gear search's time left when only the player's limit is known: "at most 1h 02m 05s left".
                    ? Tr.Format("at most {0}h {1}m {2}s left", [h, m.ToString("00", Tr.Culture), sec])
                : m > 0
                    // Translators: A gear search's time left when only the player's limit is known: "at most 9m 59s left".
                    ? Tr.Format("at most {0}m {1}s left", [m, sec])
                // Translators: A gear search's time left when only the player's limit is known: "at most 45s left".
                : Tr.Format("at most {0}s left", whole);
        return h > 0
                // Translators: A gear search's estimated time left: "1h 02m 05s left".
                ? Tr.Format("{0}h {1}m {2}s left", [h, m.ToString("00", Tr.Culture), sec])
            : m > 0
                // Translators: A gear search's estimated time left: "2m 05s left".
                ? Tr.Format("{0}m {1}s left", [m, sec])
            // Translators: A gear search's estimated time left: "45s left".
            : Tr.Format("{0}s left", whole);
    }

    /// <summary>The death limit as its button says it.</summary>
    public static string Deaths(DeathLimit limit) =>
        limit switch
        {
            // Translators: Death limit of a gear search: no trial may end in a death.
            DeathLimit.None => Tr.Get("No deaths"),
            // Translators: Death limit of a gear search: fewer than 5% of trials may end in a death.
            DeathLimit.UnderFivePercent => Tr.Get("Under 5% deaths"),
            // Translators: Death limit of a gear search: any number of deaths, as long as one trial was survived.
            _ => Tr.Get("Any, if survived once"),
        };

    /// <summary>The goal in a line: "XP / h · Attack · No deaths".</summary>
    public static string Goal(Goal goal) => Metric(goal.Metric) + " · " + Deaths(goal.Deaths);

    /// <summary>A metric's name in the unit the results show.</summary>
    public static string Metric(string key)
    {
        // As "Rank by" lists it, so the goal reads the same in the form and the answer.
        var metric =
            RunCache.Metrics(Engine.RunService.Instance.Current).FirstOrDefault(m => m.Key == key)
            ?? Metrics.Describe(key, PanelState.Names());
        return key == Metrics.MainXp ? Words.Metric(metric) : Words.MetricName(metric, PanelState.Prefs.Unit);
    }

    /// <summary>"+12.4%", or the change itself when current gear's value is zero.</summary>
    public static string Gain(Estimate? percent, Estimate? gain) =>
        percent is { } p ? Share(p.Point) + "%"
        : gain is { } g ? Signed(g.Point)
        : "–";

    /// <summary>
    /// The heading number of a set: its gain over current gear, or, when current gear finishes no fight and there
    /// is nothing to compare, the set's own value.
    /// </summary>
    public static string Headline(Estimate? percent, Estimate? gain, TargetResult result, string metric) =>
        percent == null && gain == null && Value(result, metric) is { } value ? Ui.Number(value) : Gain(percent, gain);

    /// <summary>The line said when current gear finishes no fight: why, as the results word it.</summary>
    public static string? CurrentStuck(Answer answer) =>
        answer.Current is { Result: { HasFigures: false } result }
            ? Tr.Format(
                "Your gear now doesn't finish a fight: {0}",
                result.Reasons.Count > 0 ? Tr.Note(result.Reasons[0]) : Tr.Get("no result")
            )
            : null;

    /// <summary>"95% range +9.8% to +15.0%": the range of the gain.</summary>
    public static string Range(Estimate? percent, Estimate? gain) =>
        percent is { } p ? Tr.Format("95% range {0}% to {1}%", [Share(p.Low), Share(p.High)])
        : gain is { } g ? Tr.Format("95% range {0} to {1}", [Signed(g.Low), Signed(g.High)])
        : "";

    /// <summary>The set's value of the goal's metric in the results' unit, or null when its fights have none.</summary>
    public static double? Value(TargetResult result, string metric) =>
        Metrics.Estimate(result, metric, PanelState.Prefs.Unit, PanelState.Prices()) is { } value
        && double.IsFinite(value.Point)
            ? value.Point
            : null;

    /// <summary>"41,200 → 46,310": the goal's value with current gear, then with the set.</summary>
    public static string FromTo(SetResult? current, SetResult set, string metric) =>
        current != null && Value(current.Result, metric) is { } from && Value(set.Result, metric) is { } to
            ? Tr.Format("{0} → {1}", [Ui.Number(from), Ui.Number(to)])
            : "";

    /// <summary>
    /// For a rare item goal, "≈10.8 h to first drop · 1.4 h sooner": the time the first takes with
    /// <paramref name="set"/>, and how much sooner (or later) than with current gear; with no other set, "≈12.2 h to
    /// first drop with your gear". ""
    /// for other goals and items the game's chances don't cover.
    /// </summary>
    public static string ForOne(SetResult? current, SetResult set, string metric)
    {
        ArgumentNullException.ThrowIfNull(set);
        if (!metric.StartsWith("item:", StringComparison.Ordinal))
            return "";
        var item = metric["item:".Length..];
        if (ForOnes.Time(set.Result, item) is not { } time)
            return "";
        if (current == null || ReferenceEquals(current, set))
            // Translators: Gear search, a rare item goal: {0} how long the first takes with current gear, "12.2 h".
            return Tr.Format("≈{0} to first drop with your gear", Words.Duration(time.Point));
        // Translators: Gear search, a rare item goal: {0} how long the first takes with the set, "10.8 h".
        var line = Tr.Format("≈{0} to first drop", Words.Duration(time.Point));
        if (ForOnes.Time(current.Result, item) is not { } now)
            return line;
        var change = now.Point - time.Point;
        var by = Words.Duration(Math.Abs(change));
        return Tr.Format("{0} · {1}", [line, change >= 0 ? Tr.Format("{0} sooner", by) : Tr.Format("{0} later", by)]);
    }

    /// <summary>"3.2% deaths": how often the set died in its trials.</summary>
    public static string DeathShare(SetResult set)
    {
        var (deaths, trials) = DeathLimits.Counts(set.Result);
        return Tr.Format("{0} deaths", Ui.Percent(trials == 0 ? 0 : (double)deaths / trials));
    }

    /// <summary>
    /// The three numbers of why a set wins, each old → new in the fight's own words; only those both sets' fights
    /// have.
    /// </summary>
    public static IReadOnlyList<(string Name, string FromTo)> Why(SetResult current, SetResult set) =>
        [
            .. Answers
                .Why(current, set)
                .Where(w => w.Current != null && w.Set != null)
                .Select(w =>
                {
                    var share = FightNumbers.IsShare(w.Key);
                    string Number(Estimate e) => share ? Ui.Percent(e.Point) : Ui.Number(e.Point);
                    return (
                        Words.Metric(Metrics.Describe(w.Key, PanelState.Names())),
                        Tr.Format("{0} → {1}", [Number(w.Current!), Number(w.Set!)])
                    );
                }),
        ];

    /// <summary>Green for a gain, red for a loss, null when there is no change to colour.</summary>
    public static Godot.Color? GainColour(Estimate? percent, Estimate? gain) =>
        (percent ?? gain)?.Point switch
        {
            > 0 => Look.Safe,
            < 0 => Look.Deadly,
            _ => null,
        };

    /// <summary>
    /// A count as a player reads it: in full up to a million, then "30.5 billion", three figures and the word.
    /// </summary>
    public static string Count(double value)
    {
        var words = new (double Size, Func<string, string> Say)[]
        {
            // Translators: A very large count, {0} a number with up to three figures: "8.54 quadrillion".
            (1e15, n => Tr.Format("{0} quadrillion", n)),
            // Translators: A very large count, {0} a number with up to three figures: "8.54 trillion".
            (1e12, n => Tr.Format("{0} trillion", n)),
            // Translators: A large count, {0} a number with up to three figures: "30.5 billion".
            (1e9, n => Tr.Format("{0} billion", n)),
            // Translators: A large count, {0} a number with up to three figures: "4.68 million".
            (1e6, n => Tr.Format("{0} million", n)),
        };
        foreach (var (size, say) in words)
            if (value >= size)
            {
                var scaled = value / size;
                var format =
                    scaled >= 100 ? "#,0"
                    : scaled >= 10 ? "0.#"
                    : "0.##";
                return say(scaled.ToString(format, Tr.Culture));
            }
        return Whole(value);
    }

    private static string Whole(double value) => value.ToString("#,0", Tr.Culture);

    /// <summary>"About 40 s", "About 6 min".</summary>
    public static string About(double seconds) =>
        seconds < 90
            ? Tr.Format("About {0} s", Ui.Number(Math.Ceiling(seconds)))
            : Tr.Format("About {0} min", Ui.Number(Math.Ceiling(seconds / 60)));

    private static string Signed(double value) => (value >= 0 ? "+" : "") + Ui.Number(value);

    // A share as signed percent points with one decimal: "+6.5".
    private static string Share(double fraction) =>
        (fraction >= 0 ? "+" : "") + (fraction * 100).ToString("0.0", Tr.Culture);

    // How the session's search for this target ended without an answer to show.
    public static string? Ended(Answer? answer) =>
        answer?.State switch
        {
            AnswerState.Cancelled => Tr.Format("Cancelled after stage {0}.", answer.CancelledAfter ?? 0),
            AnswerState.Failed => Tr.Format("The search failed: {0}", answer.Reason is { } why ? Tr.Note(why) : "–"),
            _ => null,
        };

    // Another search or a run under way: one at a time.
    public static string? Busy() =>
        Engine.RunService.Instance.Search is { IsActive: true } active
            ? Tr.Format(
                "A search for {0} is running: wait for it or cancel it first.",
                Spoilers.Current.Name(active.TargetName)
            )
        : Engine.RunService.Instance.IsActive ? Tr.Get("A run is going: wait for it or cancel it first.")
        : null;

    // A set's changed slots on one line: "Weapon: Rune Sword → Dragon Sword; Stance: Slash".
    public static string SetLine(SetResult set)
    {
        var names = PanelState.Names();
        var parts = set
            .Slots.Select(s => Tr.Format("{0}: {1}", [names(s.Slot), s.NewName ?? Tr.Get("nothing")]))
            .ToList();
        if (set.Stance != null)
            parts.Add(Tr.Format("Stance: {0}", PanelState.Names()(set.Stance)));
        parts.AddRange(SetupRows.ChoiceParts(set));
        return parts.Count == 0 ? Tr.Get("current gear") : string.Join("; ", parts);
    }
}
