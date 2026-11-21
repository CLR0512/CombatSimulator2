using System.Globalization;
using System.Text;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Commands;

/// <summary>What an answer over every item adds to <c>cs2 gear</c>'s reply, in English.</summary>
internal static class BestInSlotText
{
    /// <summary>The pool an answer was found in, when it isn't the character's own items: " · pool: every item, …".</summary>
    public static string Pool(AnswerKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key.Pool != ItemPool.Every
            ? ""
            : " · pool: every item"
                + (key.WearableOnly ? ", wearable at my levels" : "")
                + (key.ReachedOnly ? ", content reached" : "")
                + (key.LeftOutSources is { Count: > 0 } left ? ", leaving out " + string.Join(", ", left) : "");
    }

    /// <summary>
    /// What a changed slot's new item takes to get, in an answer over every item: <c>[in bank]</c>,
    /// <c>[needs …]</c>, <c>[not in your bank: …]</c> or both; empty for an answer over the character's own items or a
    /// slot emptied. A source <paramref name="spoilers"/> (the guard now, when null) hides shows as the guard shows it.
    /// </summary>
    public static string Mark(SlotChange slot, AnswerKey key, Spoilers? spoilers = null)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(key);
        if (key.Pool != ItemPool.Every || slot.New == null)
            return "";
        var mark = slot.Mark ?? Model.Search.Mark.None;
        var needs = mark.Needs is { } note ? $"  [needs {note.English()}]" : "";
        return mark.Kind switch
        {
            MarkKind.NotInBank => $"  [not in your bank: {Source(mark.Source, spoilers ?? Spoilers.Current)}]" + needs,
            MarkKind.Locked => needs,
            _ => "  [in bank]",
        };
    }

    /// <summary>
    /// A found set's slots: every slot with what decided it when the answer was settled, else the slots it changes,
    /// each with its item's mark over every item; then what it assumes lasts.
    /// </summary>
    public static void Slots(StringBuilder text, SetResult set, Answer answer)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(answer);
        if (answer.Decisions.Count > 0)
            Decisions(text, answer);
        else
            foreach (var slot in set.Slots)
                text.Append(
                        CultureInfo.InvariantCulture,
                        $"\n  {slot.SlotName}: {slot.OldName ?? "nothing"} → {slot.NewName ?? "nothing"}"
                    )
                    .Append(Mark(slot, answer.Key));
        foreach (var item in set.Assumes)
            text.Append(CultureInfo.InvariantCulture, $"\n  assumes {item} lasts");
    }

    /// <summary>
    /// Every slot of a settled answer's set, in the game's order: old → new or keep, the item's mark over every item,
    /// and what decided it; then a line when the search stopped before its slots were settled.
    /// </summary>
    public static void Decisions(StringBuilder text, Answer answer, Spoilers? spoilers = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(answer);
        // The closest set's slots when no set stayed within the death limit: settled as a best set is.
        var changed = (answer.Best ?? answer.Closest)?.Slots.ToDictionary(s => s.Slot, StringComparer.Ordinal) ?? [];
        foreach (var slot in answer.Decisions)
        {
            text.Append("\n  ").Append(slot.SlotName).Append(": ");
            if (changed.TryGetValue(slot.Slot, out var change))
                text.Append(change.OldName ?? "nothing").Append(" → ").Append(change.NewName ?? "nothing");
            else
                text.Append("keep ").Append(slot.ItemName ?? "nothing");
            if (answer.Key.Pool == ItemPool.Every && slot.Item != null)
                text.Append(Marked(slot, spoilers ?? Spoilers.Current));
            text.Append("  ").Append(Measure(slot));
        }
        if (answer.Settled == false)
            text.Append("\n  slots not settled: the search was stopped first");
    }

    /// <summary>
    /// The gap from now: the answer's gain, then each new item alone in current gear, largest first, the first to get
    /// first with its share of the gap; a row within the margin pays off only with the rest. No sum is shown.
    /// </summary>
    public static void Gap(StringBuilder text, Answer answer)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(answer);
        if (answer.GapFought == false)
        {
            text.Append("\ngap: not fought, the search was stopped first");
            return;
        }
        if (answer.Gap.Count == 0 || answer.Best?.Percent is not { } gap)
            return;
        text.Append("\ngap: ").Append(Percent(gap)).Append(" from now");
        for (var i = 0; i < answer.Gap.Count; i++)
        {
            var row = answer.Gap[i];
            text.Append("\n  ")
                .Append(i == 0 ? "get first: " : "")
                .Append(string.Join(" + ", row.Items.Select(item => item.Name)))
                .Append("  ")
                .Append(Percent(row.Gain));
            if (i == 0 && !row.OnlyWithSet)
                text.Append(CultureInfo.InvariantCulture, $"  ({row.Share * 100:0}% of the gap)");
            if (row.OnlyWithSet)
                text.Append("  only with the rest");
        }
    }

    private static string Percent(Estimate e) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{e.Point * 100:+0.0;−0.0;0.0}% [{e.Low * 100:0.0;−0.0;0.0}, {e.High * 100:0.0;−0.0;0.0}]"
        );

    /// <summary>What decided a slot, with the runner-up and the difference from it for a defensive measure.</summary>
    public static string Measure(SlotDecision slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        var name = slot.DecidedBy switch
        {
            DecidedBy.Goal => "goal",
            DecidedBy.Deaths => "fewest deaths",
            DecidedBy.DamageTaken => "least damage taken",
            DecidedBy.Food => "least food",
            DecidedBy.Locked => "locked",
            _ => "nothing separated them",
        };
        if (slot.Difference is not { } d || slot.Against is not { } against)
            return name;
        var relative = d.Method == "relative";
        string Of(double v) =>
            relative
                ? (v * 100).ToString("+0.0;−0.0", CultureInfo.InvariantCulture)
                : v.ToString("+0.####;−0.####", CultureInfo.InvariantCulture);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{name} ({Of(d.Point)}{(relative ? "%" : "")} [{Of(d.Low)}, {Of(d.High)}] vs {against})"
        );
    }

    // An item's mark in a settled slot: worn or in bank when the character has it.
    private static string Marked(SlotDecision slot, Spoilers spoilers)
    {
        var mark = slot.Mark ?? Model.Search.Mark.None;
        if (mark.Kind == MarkKind.None)
            return slot.Worn ? "  [worn]" : "  [in bank]";
        var needs = mark.Needs is { } note ? $"  [needs {note.English()}]" : "";
        return mark.Kind == MarkKind.NotInBank
            ? $"  [not in your bank: {Source(mark.Source, spoilers)}]" + needs
            : needs;
    }

    private static string Source(Note? source, Spoilers spoilers) =>
        source == null ? Planner.OtherSource : spoilers.Name(source.English());
}
