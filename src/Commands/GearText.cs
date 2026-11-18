using System.Globalization;
using System.Text;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Commands;

/// <summary>A search's answer as <c>cs2 gear</c> prints it, in English. Every number in it is the answer's own.</summary>
internal static partial class GearText
{
    /// <summary>The whole reply: the heading, the counts, a line per stage, then how the search ended.</summary>
    public static string Of(Answer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        var text = new StringBuilder();
        text.Append(
            CultureInfo.InvariantCulture,
            $"gear {Ids.BriefKey(answer.Target)} by {answer.Key.Goal.Metric}, {Deaths(answer.Key.Goal.Deaths)}, margin {answer.Margin * 100:0.#}%, seed {answer.Seed}"
        );
        if (answer.StoppedAtStage is { } stopped)
            text.Append(CultureInfo.InvariantCulture, $", stopped at stage {stopped}");
        text.Append(
            CultureInfo.InvariantCulture,
            $"\nsets: {Count(answer.Possible)} possible, {Count(answer.RuledOut.Values.Sum())} ruled out, {Count(answer.Simulated)} simulated, {Count(answer.LeftOut.Count)} left out"
        );
        for (var i = 0; i < answer.Stages.Count; i++)
        {
            var stage = answer.Stages[i];
            text.Append(
                CultureInfo.InvariantCulture,
                $"\nstage {i + 1}: {stage.Trials} trials, {stage.Fought} fought, {stage.Dropped} dropped, {stage.Left} left"
            );
        }
        if (answer.Climbed)
            text.Append("\nfound a slot at a time; a better combination may exist");
        if (answer is { Reran: true, BehindAtStage: { } behind })
            text.Append(
                CultureInfo.InvariantCulture,
                $"\nthe leaders were fought again with seed {answer.Seed}: the best was behind at stage {behind}"
            );
        foreach (var skipped in answer.SkippedStyles)
            text.Append(CultureInfo.InvariantCulture, $"\nstyle {skipped.Style}: skipped, {skipped.Why.English()}");
        switch (answer.State)
        {
            case AnswerState.Found when answer.Best is { } best:
                text.Append("\nbest: ").Append(Gain(best, answer)).Append(For(answer));
                if (answer.Current is { Result.HasFigures: false } stuck)
                    text.Append("\n  current gear: ")
                        .Append(ResultText.Name(stuck.Result.State))
                        .Append(stuck.Result.Reasons.Count > 0 ? ": " + stuck.Result.Reasons[0].English() : "");
                Slots(text, best);
                Choices(text, best);
                Members(text, answer, best);
                if (answer.Current is { } current && Why(current, best) is { Length: > 0 } why)
                    text.Append("\n  why: ").Append(why);
                text.Append("\n  hash ").Append(ResultsHashInput.Brief([best.Result]));
                Ties(text, answer.Ties, IsSetup(answer) ? best : null);
                Efficiency(text, answer);
                break;
            case AnswerState.KeepCurrent:
                text.Append(
                    CultureInfo.InvariantCulture,
                    $"\nkeep what you have: no set is better by more than the margin ({answer.Ties.Count} as good)"
                );
                text.Append(For(answer));
                if (answer.Current is { } kept)
                {
                    if (HoursForOne(kept, answer) is { } hours)
                        text.Append(CultureInfo.InvariantCulture, $"\n  first drop {Fmt.N(hours)} h with current gear");
                    text.Append("\n  hash ").Append(ResultsHashInput.Brief([kept.Result]));
                }
                Ties(text, answer.Ties, null);
                break;
            case AnswerState.NoneWithinLimit:
                text.Append("\nno set within the limit");
                if (answer.Closest is { } closest)
                {
                    text.Append(": closest ").Append(Line(closest)).Append(" with ").Append(DeathShare(closest));
                }
                break;
            case AnswerState.NoneKills:
                text.Append("\nno set kills this target");
                if (answer.Reason is { } reason)
                    text.Append(": ").Append(reason.English());
                break;
            case AnswerState.NothingToSearch:
                text.Append("\nnothing to search: nothing else owned fits an unlocked slot");
                break;
            case AnswerState.Cancelled:
                text.Append(
                    answer.Kind == SearchKind.BestGear
                        ? "\nstopped before any set finished a stage: nothing kept"
                        : string.Create(
                            CultureInfo.InvariantCulture,
                            $"\ncancelled after stage {answer.CancelledAfter ?? 0}"
                        )
                );
                break;
            case AnswerState.Failed:
                text.Append("\nfailed: ").Append(answer.Reason?.English() ?? "no reason given");
                break;
        }
        foreach (var style in answer.ByStyle)
        {
            text.Append(CultureInfo.InvariantCulture, $"\nstyle {style.Style}: ");
            text.Append(style.Best is { } styleBest ? Line(styleBest) + "   " + Gain(styleBest, answer) : "no set");
            if (style.Ties.Count > 0)
                text.Append(CultureInfo.InvariantCulture, $" ({style.Ties.Count} as good)");
        }
        if (answer.ByStyle.Count > 0 && answer.Best is { } overall)
            text.Append("\nbest overall: ").Append(overall.Style);
        return text.ToString();
    }

    /// <summary>
    /// Droptimizer's reply: the heading, the counts, each gain under where its item comes from, then what was no
    /// gain and what can't be worn yet (the first ten, or all with <paramref name="full"/>).
    /// </summary>
    public static string OfDrops(Answer answer, bool full = false)
    {
        ArgumentNullException.ThrowIfNull(answer);
        var text = new StringBuilder();
        text.Append(
            CultureInfo.InvariantCulture,
            $"droptimizer {Ids.BriefKey(answer.Target)} by {answer.Key.Goal.Metric}, {Deaths(answer.Key.Goal.Deaths)}, margin {answer.Margin * 100:0.#}%, seed {answer.Seed}"
        );
        text.Append(
            CultureInfo.InvariantCulture,
            $"\nitems: {answer.Unowned} unowned, {answer.Wearable} wearable, {Count(answer.RuledOut.Values.Sum())} ruled out, {Math.Max(0, answer.Simulated - 1)} simulated; {answer.NotYetWearable.Count} not yet wearable"
        );
        for (var i = 0; i < answer.Stages.Count; i++)
        {
            var stage = answer.Stages[i];
            text.Append(
                CultureInfo.InvariantCulture,
                $"\nstage {i + 1}: {stage.Trials} trials, {stage.Fought} fought, {stage.Dropped} dropped, {stage.Left} left"
            );
        }
        switch (answer.State)
        {
            case AnswerState.Cancelled:
                text.Append(CultureInfo.InvariantCulture, $"\ncancelled after stage {answer.CancelledAfter ?? 0}");
                return text.ToString();
            case AnswerState.Failed:
                text.Append("\nfailed: ").Append(answer.Reason?.English() ?? "no reason given");
                return text.ToString();
            case AnswerState.NothingToSearch:
                text.Append("\nnothing to try: no unowned item can be worn now");
                break;
        }
        if (answer.Current is { Result.HasFigures: false } stuck)
            text.Append("\ncurrent gear: ")
                .Append(ResultText.Name(stuck.Result.State))
                .Append(stuck.Result.Reasons.Count > 0 ? ": " + stuck.Result.Reasons[0].English() : "");
        text.Append(CultureInfo.InvariantCulture, $"\ngains ({answer.Gains.Count}):");
        foreach (var gain in answer.Gains)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n  {gain.Source.English()}: {gain.Name} ({gain.SlotName}");
            if (gain.Displaced.Count > 0)
                text.Append(", replaces ").AppendJoin(" and ", gain.Displaced);
            if (gain.Changes.FirstOrDefault(c => c.Kind == ChangeKind.Stance) is { } stance)
                text.Append(", stance ").Append(Ids.Brief(stance.Value));
            text.Append(')');
            if (gain.Percent is { } percent)
                text.Append(
                    CultureInfo.InvariantCulture,
                    $"   {percent.Point * 100:+0.0;-0.0}% [{percent.Low * 100:0.0}, {percent.High * 100:0.0}]"
                );
            else if (gain.Gain is { } amount)
                text.Append(
                    CultureInfo.InvariantCulture,
                    $"   {amount.Point:+#,0.##;-#,0.##} [{amount.Low:#,0.##}, {amount.High:#,0.##}]"
                );
            text.Append(HelpsNote(answer, gain.Result, gain.Metric));
            text.Append("   hash ").Append(ResultsHashInput.Brief([gain.Result]));
        }
        if (answer.Safer.Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"\nsafer, same result ({answer.Safer.Count}):");
            foreach (var safer in answer.Safer)
                text.Append(
                    CultureInfo.InvariantCulture,
                    $"\n  {safer.Name} ({safer.SlotName})   damage/s {safer.TakenBefore?.Point:0.##} -> {safer.Taken?.Point:0.##}   hash {ResultsHashInput.Brief([safer.Result])}"
                );
        }
        text.Append(
            CultureInfo.InvariantCulture,
            $"\nno gain: {answer.NoGain}   over the death limit: {answer.OverLimit}"
        );
        if (answer.NotYetWearable.Count > 0)
        {
            var shown = full ? answer.NotYetWearable : [.. answer.NotYetWearable.Take(10)];
            text.Append(CultureInfo.InvariantCulture, $"\nnot yet wearable ({answer.NotYetWearable.Count}): ");
            text.AppendJoin(
                ", ",
                shown.Select(n => $"{n.Name} ({string.Join("; ", n.Missing.Select(m => m.English()))})")
            );
            if (shown.Count < answer.NotYetWearable.Count)
                text.Append("   (first 10; `full` lists all)");
        }
        return text.ToString();
    }

    /// <summary>The death limit in the words the reply uses.</summary>
    public static string Deaths(DeathLimit limit) =>
        limit switch
        {
            DeathLimit.None => "no deaths",
            DeathLimit.UnderFivePercent => "under 5% deaths",
            _ => "any deaths, if survived once",
        };

    // "+12.4% [9.8, 15.0] xp:main (41,200 → 46,310 /h), stance Slash": the gain over current gear with its range,
    // the two values it is between, and the stance when the set names one.
    private static string Gain(SetResult set, Answer answer)
    {
        var text = new StringBuilder();
        if (set.Percent is { } percent)
            text.Append(
                CultureInfo.InvariantCulture,
                $"{percent.Point * 100:+0.0;-0.0}% [{percent.Low * 100:0.0}, {percent.High * 100:0.0}] "
            );
        else if (set.Gain is { } gain)
            text.Append(
                CultureInfo.InvariantCulture,
                $"{gain.Point:+#,0.##;-#,0.##} [{gain.Low:#,0.##}, {gain.High:#,0.##}] "
            );
        text.Append(answer.Key.Goal.Metric);
        // A set of targets is scored as the mean of each one's gain: there is no rate of its own to show.
        if (set.Result.Kind == TargetKind.Set)
            text.Append(", the mean over its targets");
        else if (answer.Current is { } current && Value(current) is { } from && Value(set) is { } to)
            text.Append(CultureInfo.InvariantCulture, $" ({Count(from)} → {Count(to)} /h)");
        if (HoursForOne(set, answer) is { } hoursWith)
            text.Append(
                answer.Current is { } now && now != set && HoursForOne(now, answer) is { } hoursNow
                    ? $", first drop {Fmt.N(hoursNow)} → {Fmt.N(hoursWith)} h"
                    : $", first drop {Fmt.N(hoursWith)} h"
            );
        if (set.Stance != null)
            text.Append(", stance ").Append(Ids.Brief(set.Stance));
        // Where the limit lets a set die, how often it did is part of what it is.
        if (answer.Key.Goal.Deaths != DeathLimit.None)
            text.Append(", ").Append(DeathShare(set));
        return text.ToString();
    }

    // A rare item goal's average hours to the first drop with the set; null for other goals and items the chances don't cover.
    private static double? HoursForOne(SetResult set, Answer answer) =>
        answer.Key.Goal.Metric.StartsWith("item:", StringComparison.Ordinal)
        && ForOnes.Time(set.Result, answer.Key.Goal.Metric["item:".Length..]) is { } time
            ? time.Point / 3600
            : null;

    // "chance to hit 61.0% → 66.2%, damage per hit 14.1 → 15.9, damage taken per second 3.1 → 3.0": only the
    // numbers both sets' fights have.
    private static string Why(SetResult current, SetResult set) =>
        string.Join(
            ", ",
            Answers
                .Why(current, set)
                .Where(w => w.Current != null && w.Set != null)
                .Select(w =>
                {
                    var share = FightNumbers.IsShare(w.Key);
                    string Number(Estimate e) =>
                        share
                            ? (e.Point * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%"
                            : e.Point.ToString("#,0.0", CultureInfo.InvariantCulture);
                    return $"{Metrics.Describe(w.Key).Kind.English().ToLowerInvariant()} {Number(w.Current!)} → {Number(w.Set!)}";
                })
        );

    private static string DeathShare(SetResult set)
    {
        var (deaths, trials) = DeathLimits.Counts(set.Result);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(trials == 0 ? 0 : deaths * 100.0 / trials):0.#}% deaths"
        );
    }

    private static double? Value(SetResult set) =>
        Metrics.Estimate(set.Result, set.Metric, TimeUnit.Hour) is { } value && double.IsFinite(value.Point)
            ? value.Point
            : null;

    private static void Slots(StringBuilder text, SetResult set)
    {
        foreach (var slot in set.Slots)
            text.Append(
                CultureInfo.InvariantCulture,
                $"\n  {slot.SlotName}: {slot.OldName ?? "nothing"} → {slot.NewName ?? "nothing"}"
            );
        foreach (var item in set.Assumes)
            text.Append(CultureInfo.InvariantCulture, $"\n  assumes {item} lasts");
    }

    // A setup search can find many sets as good, the highest gains first: the first few are listed, each by what it
    // holds other than the best.
    private static void Ties(StringBuilder text, List<SetResult> ties, SetResult? best)
    {
        var setup = best != null;
        if (ties.Count == 0)
            return;
        text.Append(CultureInfo.InvariantCulture, $"\nas good ({ties.Count}):");
        foreach (var tie in setup ? ties.Take(MostTies) : ties)
        {
            text.Append("\n  ").Append(Line(best != null ? SetupAnswers.Differences(tie, best) : tie));
            if (tie.Percent is { } percent)
                text.Append(
                    CultureInfo.InvariantCulture,
                    $"   {percent.Point * 100:+0.0;-0.0}% [{percent.Low * 100:0.0}, {percent.High * 100:0.0}]"
                );
            text.Append("   hash ").Append(ResultsHashInput.Brief([tie.Result]));
        }
        if (setup && ties.Count > MostTies)
            text.Append(CultureInfo.InvariantCulture, $"\n  and {ties.Count - MostTies} more");
    }

    /// <summary>The most sets as good a setup search's answer lists.</summary>
    public const int MostTies = 10;

    // A set on one line: its changed slots, then its stance; "current gear" without any.
    private static string Line(SetResult set)
    {
        var parts = set
            .Slots.Select(s => $"{s.SlotName}: {s.OldName ?? "nothing"} → {s.NewName ?? "nothing"}")
            .ToList();
        if (set.Stance != null)
            parts.Add("stance " + Ids.Brief(set.Stance));
        parts.AddRange(set.Choices.Where(c => !c.Keep).Select(c => $"{Label(c)}: {Names(c.NewNames)}"));
        parts.AddRange(set.Assumes.Select(item => $"assumes {item} lasts"));
        return parts.Count == 0 ? "current gear" : string.Join("; ", parts);
    }

    private static string Count(double value) => value.ToString("#,0", CultureInfo.InvariantCulture);
}
