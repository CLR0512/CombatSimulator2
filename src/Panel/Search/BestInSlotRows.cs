using Godot;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Search;

/// <summary>
/// What a gear search's answer over every item adds: the pool it chose from, each slot's mark and what decided it, the
/// items to get with where they come from, and the gap from now; and for a stopped search, what didn't run.
/// </summary>
internal static class BestInSlotRows
{
    // Under the gain, muted: the pool when it was every item, with its narrowing.
    public static void PoolLine(VBoxContainer body, Answer answer)
    {
        var key = answer.Key;
        if (key.Pool != ItemPool.Every)
            return;
        // Translators: Under a gear search's gain: it chose from every item in the game.
        var parts = new List<string> { Tr.Get("Every item") };
        if (key.WearableOnly)
            // Translators: Under a gear search's gain, after "Every item": only what the character's levels meet.
            parts.Add(Tr.Get("wearable at my levels"));
        if (key.ReachedOnly)
            // Translators: Under a gear search's gain, after "Every item": only content the character has reached.
            parts.Add(Tr.Get("content I've reached"));
        if (key.LeftOutSources is { Count: > 0 } left)
            // Translators: Under a gear search's gain, after "Every item": {0} the item sources left out.
            parts.Add(Tr.Format("leaving out {0}", string.Join(", ", left.Select(Spoilers.Current.Name))));
        var line = Ui.Add(body, Ui.Label(string.Join(" · ", parts), TextStyle.Small, "GearSearchPoolLine", wrap: true));
        line.Modulate = Colors.White with { A = 0.7f };
    }

    // A stopped search's answer says what of it didn't run.
    public static void NotRun(VBoxContainer body, Answer answer)
    {
        if (answer.Settled == false)
            Risky(
                body,
                "GearSearchNotSettled",
                // Translators: On a stopped gear search's answer: the slots the goal left tied weren't raced again.
                Tr.Get("Slots not settled: the search was stopped before tied slots were raced again.")
            );
        if (answer.GapFought == false)
            Risky(
                body,
                "GearSearchNoGap",
                // Translators: On a stopped gear search's answer: the new items weren't fought alone in current gear.
                Tr.Get("No gap figures: the search was stopped before the new items were fought alone.")
            );
    }

    /// <summary>Under the counts, when the search stopped at the player's longest search.</summary>
    public static void StoppedAtLimit(VBoxContainer column, Answer answer)
    {
        if (answer.StoppedAtLimit is not { } limit)
            return;
        column.AddChild(
            Ui.Label(
                // Translators: Under a gear search's answer when part of it was cut short to keep within the player's longest search; {0} in minutes.
                Tr.Format(
                    "Cut short to keep within your {0} min limit: the best set found so far.",
                    Ui.Number(limit / 60)
                ),
                TextStyle.Small,
                "GearSearchStoppedAtLimit",
                wrap: true
            )
        );
    }

    private static void Risky(VBoxContainer body, string name, string text)
    {
        var line = Ui.Add(body, Ui.Label(text, TextStyle.Small, name, wrap: true));
        line.LabelSettings = Compare.CompareText.Coloured(Look.Risky, TextStyle.Small);
    }

    // A changed slot's mark under its name: where an item not owned comes from, and what it needs.
    public static string MarkWords(Mark? mark)
    {
        if (mark is not { Kind: not MarkKind.None } marked)
            return "";
        var parts = new List<string>();
        if (marked.Kind == MarkKind.NotInBank)
            // Translators: Under an item of a gear search's answer that the character doesn't own: {0} where it comes from; in the item's hover, {0} says how it is got in full ("Dropped by Wind Drifter").
            parts.Add(Tr.Format("Not owned · {0}", SourceName(marked.Source)));
        if (marked.Needs is { } needs)
            parts.Add(Tr.Note(needs));
        return string.Join(" · ", parts);
    }

    // The mark as a hover gives it: how an item not owned is got in full, else as under its name.
    private static string HoverMark(Mark? mark)
    {
        if (mark is not { Kind: MarkKind.NotInBank, From: { } from })
            return MarkWords(mark);
        var line = Tr.Format("Not owned · {0}", Tr.Note(from));
        return mark.Needs is { } needs ? line + " · " + Tr.Note(needs) : line;
    }

    // Translators: Where an item comes from when the game's data names no source.
    private static string SourceName(Note? source) => source != null ? Tr.Note(source) : Tr.Get("Other");

    /// <summary>
    /// A changed slot's words: with the answer, its item's mark under its name; on hover, the item against the one it
    /// replaces, and with the answer its mark and what decided the slot.
    /// </summary>
    public static void Slot(VBoxContainer words, Answer? answer, SlotChange slot)
    {
        if (answer != null && MarkWords(slot.Mark) is { Length: > 0 } mark)
        {
            var tag = Ui.Add(
                words,
                Ui.Label(mark, TextStyle.Tiny, Ui.Id("GearSearchSlotMark", Ids.Brief(slot.Slot)), wrap: true)
            );
            tag.LabelSettings = Compare.CompareText.Coloured(Look.Risky, TextStyle.Tiny);
        }
        if (slot.New == null)
            return;
        words.MouseFilter = Control.MouseFilterEnum.Stop;
        InstantTooltips.Lazy(words, () => SlotHover(answer, slot));
    }

    // A changed slot's hover: the item against the one it replaces, its mark, and what decided the slot.
    private static string SlotHover(Answer? answer, SlotChange slot)
    {
        var lines = new List<string> { ItemPicture.About(slot.New!, slot.Old) };
        if (HoverMark(slot.Mark) is { Length: > 0 } mark)
            lines.Add(mark);
        if (answer?.Decisions.Find(d => d.Slot == slot.Slot) is { } decided)
            lines.Add(Measure(decided, answer.Metric));
        return string.Join("\n", lines);
    }

    // What decided a slot: the goal, or the defensive measure that broke its tie with the runner-up it beat.
    private static string Measure(SlotDecision slot, string metric)
    {
        var goal = SearchWords.Metric(metric);
        var by = slot.DecidedBy switch
        {
            // Translators: In a slot's hover in a gear search's answer: the goal decided it; {0} the goal's metric.
            DecidedBy.Goal => Tr.Format("Best for {0}", goal),
            // Translators: In a slot's hover: the goal left it tied and the fewest deaths decided it.
            DecidedBy.Deaths => Tr.Get("Fewest deaths"),
            // Translators: In a slot's hover: the goal left it tied and the least damage taken decided it.
            DecidedBy.DamageTaken => Tr.Get("Least damage taken"),
            // Translators: In a slot's hover: the goal left it tied and the least food eaten decided it.
            DecidedBy.Food => Tr.Get("Least food eaten"),
            // Translators: In a slot's hover: the slot was locked in the search's form, so nothing was tried there.
            DecidedBy.Locked => Tr.Get("Locked in the form"),
            // Translators: In a slot's hover: nothing measured told the tied items apart, so the tie order chose.
            _ => Tr.Get("Nothing told the items apart"),
        };
        if (slot.Difference is { } d && slot.Against is { } against)
            // Translators: In a slot's hover: {0} what decided it, {1} its difference, {2} the item it beat.
            by = Tr.Format(
                "{0} ({1} vs {2})",
                [by, d.Method == "relative" ? Words.Gain(d.Point) : Signed(d.Point), against]
            );
        return slot.DecidedBy is DecidedBy.Deaths or DecidedBy.DamageTaken or DecidedBy.Food
            // Translators: In a slot's hover: {0} the measure that decided it, {1} the goal's metric, which it left equal.
            ? Tr.Format("{0}; {1} the same", [by, goal])
            : by;
    }

    private static string Signed(double value) => (value >= 0 ? "+" : "−") + Ui.Number(Math.Abs(value));

    /// <summary>
    /// The folds for the best set: the items to get, whose sources <paramref name="leaveOut"/> (null without actions)
    /// leaves out, and the gap from now.
    /// </summary>
    public static void Folds(VBoxContainer body, Answer answer, SetResult best, Action<string>? leaveOut)
    {
        ItemsToGet(body, best, leaveOut);
        Gap(body, answer, best);
    }

    // The items the best set needs got or unlocked first, each with where it comes from and what it needs; an item
    // not owned offers to leave its source out and search again.
    private static void ItemsToGet(VBoxContainer body, SetResult best, Action<string>? leaveOut)
    {
        var items = best.Slots.Where(s => s.New != null && s.Mark is { Kind: not MarkKind.None }).ToList();
        if (items.Count == 0)
            return;
        // The item to get first is the gap's first row, tagged there: the heading has room for the count alone.
        // Translators: Heading summary of a gear search's items to get: how many.
        var summary = Tr.Plural("{0} item", "{0} items", items.Count, Ui.Number(items.Count));
        // Translators: Folded section of a gear search's answer: the items the set holds that the character doesn't have or can't wear yet.
        var fold = new Disclosure("GearSearchItemsToGet", Tr.Get("Items to get"), summary, false, _ => { });
        body.AddChild(fold.Root);
        foreach (var slot in items)
        {
            var row = Ui.Add(fold.Body, Ui.Row(6, Ui.Id("GearSearchToGet", Ids.Brief(slot.New!))));
            row.AddChild(ItemPicture.Of(slot.New, slot.Slot, slot.Old));
            var words = Ui.Add(row, Ui.Column(0));
            words.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            words.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            words.AddChild(Ui.Label(slot.NewName ?? "", TextStyle.Small, wrap: true));
            var mark = Ui.Add(words, Ui.Label(MarkWords(slot.Mark), TextStyle.Tiny, wrap: true));
            mark.Modulate = Colors.White with { A = 0.7f };
            words.MouseFilter = Control.MouseFilterEnum.Stop;
            InstantTooltips.Lazy(words, () => SlotHover(null, slot));
            if (leaveOut == null || slot.Mark is not { Kind: MarkKind.NotInBank, Source: { } source })
                continue;
            var leave = Ui.Button(
                Tr.Get("Leave out"),
                // Translators: Hover of "Leave out" beside an item to get: {0} its source.
                Tr.Format("Leave out {0} and search again", Tr.Note(source)),
                Ui.Id("GearSearchLeaveOut", Ids.Brief(slot.New!)),
                () => leaveOut(source.English())
            );
            leave.Flat = true;
            leave.AddThemeColorOverride("font_color", Look.Best);
            leave.AddThemeColorOverride("font_hover_color", Look.Best.Lightened(0.2f));
            leave.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(leave);
        }
    }

    // Each new item alone in current gear, largest gain first: the first to get first with its share of the gap; one
    // within the margin pays off only with the rest of the set. No sum: items that pay off together would count twice.
    private static void Gap(VBoxContainer body, Answer answer, SetResult best)
    {
        if (answer.Gap.Count == 0)
            return;
        var fold = new Disclosure(
            "GearSearchGapFromNow",
            // Translators: Folded section of a gear search's answer: what each new item gains alone over current gear.
            Tr.Get("Gap from now"),
            // Translators: Heading summary of a gear search's gap: {0} the whole set's gain over current gear.
            Tr.Format("{0} from now", SearchWords.Gain(best.Percent, best.Gain)),
            false,
            _ => { }
        );
        body.AddChild(fold.Root);
        for (var i = 0; i < answer.Gap.Count; i++)
        {
            var gap = answer.Gap[i];
            var row = Ui.Add(fold.Body, Ui.Row(8, Ui.Id("GearSearchGapRow", i.ToString(Tr.Culture))));
            if (i == 0 && !gap.OnlyWithSet)
            {
                // Translators: Tag on the first row of a gear search's gap: the item to get first.
                var tag = Ui.Add(row, Ui.Label(Tr.Get("get first"), TextStyle.Tiny));
                tag.LabelSettings = Compare.CompareText.Coloured(Look.Best, TextStyle.Tiny);
                tag.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
            }
            var names = Ui.Add(
                row,
                Ui.Label(string.Join(" + ", gap.Items.Select(item => item.Name)), TextStyle.Small, wrap: true)
            );
            names.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var value = Ui.Add(
                row,
                Ui.Label(
                    gap.OnlyWithSet
                        // Translators: In a gear search's gap: {0} an item's gain alone, too small to count without the rest of the set.
                        ? Tr.Format("{0} · only with the rest", SearchWords.Gain(gap.Gain, null))
                        // Translators: In a gear search's gap: {0} an item's gain alone, {1} its share of the whole set's gain.
                        : Tr.Format("{0} · {1}", [SearchWords.Gain(gap.Gain, null), Ui.Percent(gap.Share)]),
                    TextStyle.Small
                )
            );
            value.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
            if (gap.OnlyWithSet)
                row.Modulate = Colors.White with { A = 0.6f };
            else if (SearchWords.GainColour(gap.Gain, null) is { } colour)
                value.LabelSettings = Compare.CompareText.Coloured(colour, TextStyle.Small);
        }
    }
}
