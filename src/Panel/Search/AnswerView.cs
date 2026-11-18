using Godot;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Search;

/// <summary>
/// A search's answer: the gain first with its range and the two values, then what to wear and why, the sets as good,
/// the counts, and "Open as setup"; or the ending that has no set to wear. A kept answer whose
/// setup has changed since shows dimmed under a banner saying what changed, with "Search again" as its button.
/// </summary>
internal sealed partial class AnswerView
{
    private readonly Action _again;
    private readonly Action<int?, string?> _open;
    private Answer? _shown;
    private (Answer, string, int)? _stamp;
    private IReadOnlyList<Note> _changed = [];
    private string _tab = SetupRows.Best;

    /// <summary>
    /// The view; <paramref name="again"/> goes to the start form; <paramref name="open"/> takes a set of the answer
    /// (null the best, else a tie from 1).
    /// </summary>
    public AnswerView(Action again, Action<int?, string?> open)
    {
        _again = again;
        _open = open;
        Root = Ui.Column(12, "GearSearchAnswer");
    }

    public VBoxContainer Root { get; }

    /// <summary>
    /// Shows <paramref name="answer"/>; <paramref name="changed"/> says why it is out of date (empty when it is
    /// current); only a current answer opens as a setup.
    /// </summary>
    public void Show(Answer answer, IReadOnlyList<Note> changed)
    {
        var stamp = (answer, string.Join(",", changed.Select(Tr.Note)), Tr.Version);
        if (stamp == _stamp)
            return;
        _stamp = stamp;
        if (!ReferenceEquals(answer, _shown))
            _tab = SetupRows.Best;
        _shown = answer;
        _changed = changed;
        Ui.Clear(Root);
        if (changed.Count > 0)
        {
            var again = // Translators: Button on the out-of-date notice: simulate the search again with the setup as it is now.
            SearchWords.Button(Tr.Get("Sim again"), "GearSearchAgainOutOfDate", _again);
            Root.AddChild(
                Banner.Make(
                    "GearSearchOutOfDate",
                    Look.Risky,
                    null,
                    // Translators: Over a kept gear search answer whose setup has changed since; what changed is under it.
                    Tr.Get("Out of date"),
                    Tr.Notes(changed, ", "),
                    again
                )
            );
        }
        var body = Ui.Add(Root, Ui.Column(12));
        // The kept answer stays readable under the banner, dimmed: it is what was true then.
        body.Modulate = Colors.White with
        {
            A = changed.Count > 0 ? 0.5f : 1f,
        };
        Fill(body, answer, changed.Count == 0, changed.Count == 0);
    }

    /// <summary>The answer as shown, for <c>cs2 ui</c>.</summary>
    public string Describe() =>
        _shown is { } a
            ? $"state={a.State} gain={SearchWords.Gain(a.Best?.Percent, a.Best?.Gain)} changed={a.Best?.Slots.Count ?? 0} ties={a.Ties.Count} styles={a.ByStyle.Count}"
            : "none";

    private void Fill(VBoxContainer body, Answer answer, bool actions, bool againLink)
    {
        Stopped(body, answer);
        switch (answer.State)
        {
            case AnswerState.Found when answer.Best is { } found:
                if (AnswerTabs.Text(answer, _tab, Tab) is { } tabs)
                    body.AddChild(tabs);
                var best = SetupRows.Shown(answer, _tab, found);
                SetupRows.Banner(body, _tab, best, found);
                Heading(body, answer, best, againLink);
                SetupRows.Under(body, answer);
                var rows = SetupRows.Rows(_tab, best, found);
                Wear(body, rows, open: actions ? OpenButton() : null);
                SetupRows.Choices(body, rows);
                Why(body, answer, best);
                Members(body, answer, best);
                SetupRows.Folds(body, answer, best);
                break;
            case AnswerState.KeepCurrent:
                // Translators: Gear search answer when no set is better than current gear by more than the margin.
                Title(body, Tr.Get("Keep what you have"), againLink);
                SetupRows.Under(body, answer);
                body.AddChild(
                    Ui.Label(
                        Tr.Format("No set is better by more than {0}.", Ui.Percent(answer.Margin)),
                        TextStyle.Body,
                        wrap: true
                    )
                );
                if (
                    answer.Current is { } current
                    && SearchWords.ForOne(null, current, answer.Metric) is { Length: > 0 } one
                )
                    body.AddChild(Ui.Label(one, TextStyle.Small, "GearSearchForOne", wrap: true));
                break;
            case AnswerState.NoneWithinLimit:
                // Translators: Gear search answer when every set dies more often than the death limit allows.
                Title(body, Tr.Get("No set stays within the death limit"), againLink);
                if (answer.Closest is { } closest)
                {
                    body.AddChild(
                        Ui.Label(
                            Tr.Format("Closest: {0}", SearchWords.DeathShare(closest)),
                            TextStyle.Body,
                            "GearSearchClosest"
                        )
                    );
                    Wear(body, closest, heading: false);
                }
                break;
            case AnswerState.NoneKills:
                // Translators: Gear search answer when no set finishes a fight against the target.
                Title(body, Tr.Get("No set kills this target"), againLink);
                if (answer.Reason is { } reason)
                    body.AddChild(Ui.Label(Tr.Note(reason), TextStyle.Body, "GearSearchReason", wrap: true));
                break;
            default:
                Title(body, Tr.Get("Nothing to search"), againLink);
                body.AddChild(
                    Ui.Label(Tr.Get("Nothing else you own fits an unlocked slot."), TextStyle.Body, wrap: true)
                );
                break;
        }
        Styles(body, answer);
        Ties(body, answer, actions);
        Notes(body, answer);
    }

    // Opens the set shown as a setup compared against current gear: the best, or the alternative of the tab.
    private Button OpenButton() =>
        // Translators: Opens the found set as a setup compared against current gear.
        SearchWords.Button(
            Tr.Get("Open as setup"),
            "GearSearchOpen",
            () => _open(null, _tab == SetupRows.Best ? null : _tab)
        );

    // The gain first, large, with "Search again" as a gold link at its right; then its range and the two values.
    private void Heading(VBoxContainer body, Answer answer, SetResult best, bool againLink)
    {
        var row = Ui.Add(body, Ui.Row(8));
        var gain = Ui.Add(row, Ui.Column(2, "GearSearchGain"));
        gain.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var headline = Ui.Add(
            gain,
            Ui.Label(SearchWords.Headline(best.Percent, best.Gain, best.Result, answer.Metric), TextStyle.Title)
        );
        // Only a gain is coloured: a set's own value, shown when there is nothing to compare, is neither.
        if (
            (best.Percent != null || best.Gain != null) && SearchWords.GainColour(best.Percent, best.Gain) is { } colour
        )
            headline.LabelSettings = Compare.CompareText.Coloured(colour, TextStyle.Title);
        gain.AddChild(Ui.Label(SearchWords.Metric(answer.Metric), TextStyle.Small, wrap: true));
        if (againLink)
            row.AddChild(AgainLink());
        var range = SearchWords.Range(best.Percent, best.Gain);
        // A set of targets is scored as the mean of their gains: it has no rate of its own to show.
        var values = best.Result.Kind == TargetKind.Set ? "" : SearchWords.FromTo(answer.Current, best, answer.Metric);
        var line = string.Join(
            " · ",
            new[] { values, range, SearchWords.ForOne(answer.Current, best, answer.Metric) }.Where(s => s.Length > 0)
        );
        if (answer.Key.Goal.Deaths != DeathLimit.None)
            line = string.Join(" · ", new[] { line, SearchWords.DeathShare(best) }.Where(s => s.Length > 0));
        if (line.Length > 0)
            body.AddChild(Ui.Label(line, TextStyle.Small, "GearSearchRange", wrap: true));
        if (SearchWords.CurrentStuck(answer) is { } stuck)
            body.AddChild(Ui.Label(stuck, TextStyle.Small, "GearSearchCurrentStuck", wrap: true));
    }

    private void Title(VBoxContainer body, string text, bool againLink)
    {
        var row = Ui.Add(body, Ui.Row(8));
        var title = Ui.Add(row, Ui.Label(text, TextStyle.Heading, "GearSearchHeading", wrap: true));
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        if (againLink)
            row.AddChild(AgainLink());
    }

    private Button AgainLink()
    {
        var again = Ui.Button(Tr.Get("Search again"), "", "GearSearchAgain", _again);
        again.Flat = true;
        again.AddThemeColorOverride("font_color", Look.Best);
        again.AddThemeColorOverride("font_hover_color", Look.Best.Lightened(0.2f));
        again.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        return again;
    }

    // Per changed slot, old → new with both icons; then the stance when it changes, and what the set uses up.
    // The heading carries "Open as setup" at its right, near the top where it stays in view.
    private static void Wear(VBoxContainer body, SetResult set, bool heading = true, Button? open = null)
    {
        var nothing = set.Slots.Count == 0 && set.Stance == null && set.Assumes.Count == 0;
        if (nothing && open == null)
            return;
        var column = Ui.Add(body, Ui.Column(6, "GearSearchWear"));
        if (heading || open != null)
        {
            var head = Ui.Add(column, Ui.Row(8));
            // Translators: Heading over the slots a gear search's answer changes.
            var title = Ui.Add(head, Ui.Label(nothing ? "" : Tr.Get("Wear"), TextStyle.Label));
            title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            title.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            if (open != null)
                head.AddChild(open);
        }
        foreach (var slot in set.Slots)
        {
            var row = Ui.Add(column, Ui.Row(6, Ui.Id("GearSearchWearSlot", Ids.Brief(slot.Slot))));
            row.AddChild(ItemPicture.Of(slot.Old, slot.Slot, null));
            row.AddChild(Ui.Label("→", TextStyle.Small));
            var next = Ui.Add(row, ItemPicture.Of(slot.New, slot.Slot, slot.Old));
            // The new item on the first line, wrapping when long; under it the slot and what it replaces.
            var words = Ui.Add(row, Ui.Column(0));
            words.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            words.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            SetupRows.DiffersTint(
                Ui.Add(words, Ui.Label(slot.NewName ?? Tr.Get("Nothing"), TextStyle.Body, wrap: true)),
                slot.DiffersFromBest
            );
            words.AddChild(
                Ui.Label(
                    slot.OldName is { } was
                        // Translators: Under the item a gear search puts on: {0} the slot, {1} the item it takes off.
                        ? Tr.Format("{0} · replaces {1}", [PanelState.Names()(slot.Slot), was])
                        : PanelState.Names()(slot.Slot),
                    TextStyle.Tiny,
                    wrap: true
                )
            );
            SetupRows.GapMark(row, set, slot.New);
            if (slot.New != null)
            {
                words.MouseFilter = Control.MouseFilterEnum.Stop;
                InstantTooltips.Lazy(words, () => ItemPicture.About(slot.New, slot.Old));
            }
        }
        if (set.Stance != null)
            column.AddChild(
                Ui.Label(Tr.Format("Stance: {0}", PanelState.Names()(set.Stance)), TextStyle.Body, "GearSearchStance")
            );
        foreach (var item in set.Assumes)
            column.AddChild(Ui.Label(Tr.Format("Assumes {0} lasts", item), TextStyle.Small, wrap: true));
    }

    // The three numbers of why, old → new, in the fight's own words.
    private static void Why(VBoxContainer body, Answer answer, SetResult best)
    {
        if (answer.Current is not { } current || SearchWords.Why(current, best) is not { Count: > 0 } why)
            return;
        var column = Ui.Add(body, Ui.Column(4, "GearSearchWhy"));
        // Translators: Heading over the fight numbers that explain a gear search's answer.
        column.AddChild(Ui.Label(Tr.Get("Why"), TextStyle.Label));
        foreach (var (name, fromTo) in why)
        {
            var row = Ui.Add(column, Ui.Row(8));
            var label = Ui.Add(row, Ui.Clipped(name, TextStyle.Body));
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(Ui.Label(fromTo, TextStyle.BodyStrong));
        }
    }

    // One block per style when the search covered several, the best overall marked; a skipped style is a line.
    private static void Styles(VBoxContainer body, Answer answer)
    {
        if (answer.ByStyle.Count == 0 && answer.SkippedStyles.Count == 0)
            return;
        var column = Ui.Add(body, Ui.Column(8, "GearSearchStyles"));
        // Translators: Heading over each combat style's best set.
        column.AddChild(Ui.Label(Tr.Get("By style"), TextStyle.Label));
        foreach (var style in answer.ByStyle)
        {
            var block = Ui.Add(column, Ui.Column(2, Ui.Id("GearSearchStyle", style.Style)));
            var head = Ui.Add(block, Ui.Row(6));
            head.AddChild(Ui.Label(style.Style, TextStyle.BodyStrong));
            if (style.Best != null && answer.Best?.Style == style.Style)
                head.AddChild(Results.BestTag.Make("GearSearchBestStyle", small: true));
            block.AddChild(
                style.Best is { } set ? ItemPicture.Strip(set) : Ui.Label(Tr.Get("No set"), TextStyle.Small)
            );
        }
        foreach (var skipped in answer.SkippedStyles)
            column.AddChild(
                Ui.Label(
                    Tr.Format("{0}: skipped, {1}", [skipped.Style, Tr.Note(skipped.Why)]),
                    TextStyle.Small,
                    wrap: true
                )
            );
    }

    // "As good", folded, with its count: each tie's differing slots and its gain.
    private void Ties(VBoxContainer body, Answer answer, bool actions)
    {
        if (answer.Ties.Count == 0)
            return;
        if (SetupRows.Ties(body, answer, actions ? n => _open(n, null) : null))
            return;
        var fold = new Disclosure(
            "GearSearchTies",
            // Translators: Folded list of sets as good as the best within the margin.
            Tr.Get("As good"),
            Ui.Number(answer.Ties.Count),
            false,
            _ => { }
        );
        body.AddChild(fold.Root);
        for (var i = 0; i < answer.Ties.Count; i++)
        {
            var tie = answer.Ties[i];
            var number = i + 1;
            var row = Ui.Add(fold.Body, Ui.Row(6, Ui.Id("GearSearchTie", number.ToString(Tr.Culture))));
            var gain = Ui.Add(row, Ui.Label(SearchWords.Gain(tie.Percent, tie.Gain), TextStyle.Small));
            gain.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
            if (SearchWords.GainColour(tie.Percent, tie.Gain) is { } colour)
                gain.LabelSettings = Compare.CompareText.Coloured(colour, TextStyle.Small);
            var text = Ui.Add(row, Ui.Label(SetLine(tie), TextStyle.Small, wrap: true));
            text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            if (actions && !tie.Changes.IsEmpty)
            {
                var open = SearchWords.Button(
                    Tr.Get("Open"),
                    Ui.Id("GearSearchTieOpen", number.ToString(Tr.Culture)),
                    () => _open(number, null)
                );
                open.TooltipText = Tr.Get("Open as setup");
                row.AddChild(open);
            }
        }
    }

    // The margin, the counts, and how the search went when it went a slot at a time or fought the leaders again.
    private static void Notes(VBoxContainer body, Answer answer)
    {
        var column = Ui.Add(body, Ui.Column(6, "GearSearchCountsLine"));
        var grid = Ui.Add(column, new GridContainer { Columns = 2, Name = "GearSearchCounts" });
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 2);
        void Row(string name, string value)
        {
            var label = Ui.Add(grid, Ui.Label(name, TextStyle.Small));
            label.Modulate = Colors.White with { A = 0.7f };
            var shown = Ui.Add(grid, Ui.Label(value, TextStyle.Small, wrap: true));
            shown.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        }
        // Translators: In a gear search's counts: how many sets could be worn from what is owned.
        Row(Tr.Get("Combinations"), SearchWords.Count(answer.Possible));
        // Translators: In a gear search's counts: sets left out because another set matches or beats them.
        Row(Tr.Get("Ruled out"), SearchWords.Count(answer.RuledOut.Values.Sum()));
        // Translators: In a gear search's counts: sets that were fought.
        Row(Tr.Get("Simulated"), SearchWords.Count(answer.Simulated));
        Row(Tr.Get("Rank by"), SearchWords.Metric(answer.Key.Goal.Metric));
        Row(Tr.Get("Deaths"), SearchWords.Deaths(answer.Key.Goal.Deaths));
        // Translators: In a gear search's counts: sets this close to the best count as just as good.
        Row(Tr.Get("Equal within"), Ui.Percent(answer.Margin));
        if (answer.Climbed)
            column.AddChild(
                Ui.Label(
                    // Translators: Under a gear search's answer when there were too many sets to fight them all.
                    Tr.Get("Built slot by slot, as there were too many combinations to try. A better mix may exist."),
                    TextStyle.Small,
                    wrap: true
                )
            );
        if (answer is { Reran: true, BehindAtStage: { } behind })
            column.AddChild(
                Ui.Label(
                    Tr.Format(
                        "The best was behind at stage {0}, so the leaders were fought again with another seed.",
                        behind
                    ),
                    TextStyle.Small,
                    wrap: true
                )
            );
    }

    // A set's changed slots on one line: "Weapon: Rune Sword → Dragon Sword; Stance: Slash".
    private static string SetLine(SetResult set)
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
