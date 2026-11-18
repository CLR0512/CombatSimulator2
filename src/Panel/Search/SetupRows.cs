using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Search;

/// <summary>
/// What a setup search's answer shows beyond gear: the Best · Cheaper · Safer tabs and, on an alternative, what sets
/// it apart from the best; the loadout it was found for; each choice it changes, with what it uses and a mark where
/// the character lacks it; small marks for close calls left unsettled and what isn't simulated; and folds for what the
/// setup uses, the damage it takes and its close calls. A gear search's answer has none of it.
/// </summary>
internal static class SetupRows
{
    public const string Best = "best";
    public const string Cheaper = "cheaper";
    public const string Safer = "safer";

    public static bool IsSetup(Answer answer) => answer.Key.Kinds.Count > 0;

    /// <summary>The set the answer's tab shows: the best, or Cheaper or Safer.</summary>
    public static SetResult Shown(Answer answer, string tab, SetResult best) =>
        tab switch
        {
            Cheaper when answer.Alternatives?.Cheaper is { } cheaper => cheaper,
            Safer when answer.Alternatives?.Safer is { } safer => safer,
            _ => best,
        };

    /// <summary>
    /// Best · Cheaper · Safer with their hovers, each only when the answer has it; null with no alternative.
    /// </summary>
    public static List<(string Id, string Words, string Hover)>? Tabs(Answer answer)
    {
        if (answer.Alternatives is not { } alternatives)
            return null;
        // Translators: Answer tab: the best setup found.
        var tabs = new List<(string, string, string)> { (Best, Tr.Get("Best"), Tr.Get("The best setup found.")) };
        if (alternatives.Cheaper != null)
            tabs.Add(
                (
                    Cheaper,
                    // Translators: Answer tab: another setup, about as good, that uses less.
                    Tr.Get("Cheaper"),
                    Tr.Get("Another setup about as good as the best (within its 95% range) that uses less.")
                )
            );
        if (alternatives.Safer != null)
            tabs.Add(
                (
                    Safer,
                    // Translators: Answer tab: another setup, about as good, that takes the least damage.
                    Tr.Get("Safer"),
                    Tr.Get(
                        "Another setup about as good as the best (within its 95% range) that takes the least damage."
                    )
                )
            );
        return tabs;
    }

    /// <summary>On Cheaper or Safer: a banner saying it is another setup, what it gives up and what it saves.</summary>
    public static void Banner(VBoxContainer body, string tab, SetResult shown, SetResult best)
    {
        if (tab == Best || ReferenceEquals(shown, best))
            return;
        var parts = new List<string>();
        if (shown.Percent is { } percent && best.Percent is { } top)
            // Translators: On an alternative setup's banner: {0} its gain less the best's, such as "−0.8%".
            parts.Add(Tr.Format("{0} against Best", Signed(percent.Point - top.Point)));
        if (tab == Cheaper)
            foreach (var cost in best.Supplies)
            {
                var less = PerUnit(cost.PerHour.Point - SupplyCosts.Point(shown.Supplies, cost.Key));
                if (less > 0)
                    // Translators: On the Cheaper setup's banner: {0} how many, {1} of what, {2} the unit ("/h").
                    parts.Add(Tr.Format("{0} less {1}{2}", [Ui.Number(less), SupplyName(cost), Unit()]));
            }
        else if (shown.DamageTaken is { } taken && best.DamageTaken is { Point: > 0 } was)
            // Translators: On the Safer setup's banner: {0} a percentage.
            parts.Add(Tr.Format("{0} less damage taken", Ui.Percent(1 - taken.Point / was.Point)));
        body.AddChild(
            Widgets.Banner.Make(
                "GearSearchAlternative",
                Look.Alternative,
                null,
                // Translators: Over the Cheaper or Safer tab of a setup search's answer: another setup than the best.
                Tr.Get("Alternative"),
                // Translators: Ends an alternative setup's banner: its rows in blue are where it differs from Best.
                string.Join(" · ", parts)
                    + "\n"
                    + Tr.Get("Blue: not as in Best")
            )
        );
    }

    /// <summary>
    /// The rows a tab draws, each against current gear: on an alternative, what it holds other than the best is
    /// marked, and drawn in the alternative's colour.
    /// </summary>
    public static SetResult Rows(string tab, SetResult shown, SetResult best)
    {
        if (tab == Best || ReferenceEquals(shown, best))
            return shown;
        string? Worn(SetResult set, SlotChange slot) =>
            set.Slots.Find(s => s.Slot == slot.Slot) is { } own ? own.New : slot.Old;
        return new SetResult
        {
            Slots = [.. shown.Slots.Select(s => s with { DiffersFromBest = Worn(best, s) != s.New })],
            Choices =
            [
                .. shown.Choices.Select(c =>
                {
                    c.DiffersFromBest =
                        best.Choices.Find(b => b.Name == c.Name) is not { } held || !held.New.SequenceEqual(c.New);
                    return c;
                }),
            ],
            Stance = shown.Stance,
            Result = shown.Result,
            Metric = shown.Metric,
            Assumes = shown.Assumes,
        };
    }

    /// <summary>Draws a row's name in the alternative's colour when it differs from the best.</summary>
    public static void DiffersTint(Label name, bool differs)
    {
        if (differs)
            name.LabelSettings = Compare.CompareText.Coloured(Look.Alternative, TextStyle.Body);
    }

    /// <summary>
    /// Under the heading: the loadout when the search ran on one, and a small mark, saying its whole meaning on hover,
    /// when close calls were left unsettled.
    /// </summary>
    public static void Under(VBoxContainer body, Answer answer)
    {
        if (!IsSetup(answer))
            return;
        var row = Ui.Add(body, new HFlowContainer { Name = "GearSearchUnder" });
        row.AddThemeConstantOverride("h_separation", 10);
        if (answer.Loadout is { Length: > 0 } loadout && loadout != Setup.Current)
            // Translators: Under a setup search's answer: the loadout it was found for.
            row.AddChild(Ui.Label(Tr.Format("For {0}", loadout), TextStyle.Small, "GearSearchFor"));
        if (answer.StoppedBeforeCloseCalls)
            row.AddChild(
                Chip(
                    "GearSearchUnsettled",
                    // Translators: Short mark on a stopped setup search's answer.
                    Tr.Get("Close calls unsettled"),
                    Tr.Get("Stopped before its close calls were raced again.")
                )
            );
        row.Visible = row.GetChildCount() > 0;
    }

    /// <summary>The most sets as good a setup search's answer lists, the closest to the best first.</summary>
    public const int MostTies = 10;

    /// <summary>
    /// A setup search's sets as good, folded: what they are, then a row each for the closest to the best, by what it
    /// holds instead of the best's and how far it is from the best; <paramref name="open"/> opens one (from 1). False
    /// for a gear search's answer, which keeps its own list.
    /// </summary>
    public static bool Ties(VBoxContainer body, Answer answer, Action<int>? open)
    {
        if (!IsSetup(answer) || answer.Best is not { } best)
            return false;
        var fold = new Disclosure("GearSearchTies", Tr.Get("As good"), Ui.Number(answer.Ties.Count), false, _ => { });
        body.AddChild(fold.Root);
        fold.Body.AddChild(
            Ui.Label(
                // Translators: Opens a setup search's "As good" fold: what the setups in it are.
                Tr.Get("Setups the search can't tell apart from Best. Each shows only what it does differently."),
                TextStyle.Tiny,
                wrap: true
            )
        );
        var grid = Grid(fold.Body, "GearSearchTieList", open != null ? 3 : 2);
        // Past the first few, the rows are made hidden, for "Show n more" to show.
        var hidden = new List<Control>();
        for (var i = 0; i < answer.Ties.Count; i++)
        {
            var first = grid.GetChildCount();
            var tie = answer.Ties[i];
            var number = i + 1;
            var differs = SetupAnswers.Differences(tie, best);
            // The icons wrap within the width; what Best holds there goes under them, so neither squeezes the other.
            var what = Ui.Add(grid, Ui.Column(2, Ui.Id("GearSearchTie", number.ToString(Tr.Culture))));
            what.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var icons = Ui.Add(what, new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
            icons.AddThemeConstantOverride("h_separation", 2);
            icons.AddThemeConstantOverride("v_separation", 2);
            ItemPicture.AddIcons(icons, differs);
            if (InsteadOf(differs) is { Length: > 0 } instead)
                what.AddChild(Ui.Label(instead, TextStyle.Tiny, wrap: true));
            var from = Cell(grid, tie.Percent is { } p && best.Percent is { } b ? Signed(p.Point - b.Point) : "");
            from.MouseFilter = Control.MouseFilterEnum.Stop;
            // Translators: Hover of a set as good's difference from Best: {0} its own gain over your setup.
            from.TooltipText = Tr.Format("Against Best; {0} over your setup", SearchWords.Gain(tie.Percent, tie.Gain));
            if (open != null)
            {
                var button = SearchWords.Button(
                    Tr.Get("Open"),
                    Ui.Id("GearSearchTieOpen", number.ToString(Tr.Culture)),
                    () => open(number)
                );
                button.TooltipText = Tr.Get("Open as setup");
                // One size for every row's button, however tall the row.
                button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                grid.AddChild(button);
            }
            if (i < MostTies)
                continue;
            for (var c = first; c < grid.GetChildCount(); c++)
            {
                var cell = grid.GetChild<Control>(c);
                cell.Visible = false;
                hidden.Add(cell);
            }
        }
        if (hidden.Count > 0)
        {
            var more = answer.Ties.Count - MostTies;
            // Translators: Under the first setups as good as Best: shows the rest; {0} how many.
            string Shows(bool open) => open ? Tr.Get("Show fewer") : Tr.Format("Show {0} more", Ui.Number(more));
            var toggle = Ui.Button(Shows(false), "", "GearSearchTiesMore", null);
            toggle.Flat = true;
            toggle.AddThemeColorOverride("font_color", Look.Best);
            toggle.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
            toggle.Pressed += () =>
            {
                var open = !hidden[0].Visible;
                foreach (var cell in hidden)
                    cell.Visible = open;
                toggle.Text = Shows(open);
            };
            fold.Body.AddChild(toggle);
        }
        return true;
    }

    // "instead of Adamant Boots (u) · without Bone Casing": what Best holds where the set holds something else, and
    // what Best holds where the set holds nothing.
    private static string InsteadOf(SetResult differs)
    {
        var replaced = new List<string>();
        var dropped = new List<string>();
        foreach (var slot in differs.Slots.Where(s => s.OldName != null))
            (slot.New == null ? dropped : replaced).Add(slot.OldName!);
        foreach (var choice in differs.Choices.Where(c => c.OldNames.Count > 0))
            (choice.New.Count == 0 ? dropped : replaced).Add(string.Join(", ", choice.OldNames));
        var parts = new List<string>();
        if (replaced.Count > 0)
            // Translators: Beside a set as good's icons: what Best holds where the set holds something else.
            parts.Add(Tr.Format("instead of {0}", string.Join(", ", replaced)));
        if (dropped.Count > 0)
            // Translators: Under a set as good's icons: what Best holds that the set goes without.
            parts.Add(Tr.Format("without {0}", string.Join(", ", dropped)));
        return string.Join(" · ", parts);
    }

    /// <summary>A set's changed choices as a line's parts: "Prayers: Guidance".</summary>
    public static IEnumerable<string> ChoiceParts(SetResult set) =>
        set.Choices.Where(c => !c.Keep).Select(c => Tr.Format("{0}: {1}", [Label(c), Names(c.NewNames)]));

    /// <summary>A row per choice the set changes: old → new with icons, what it uses, and its marks.</summary>
    public static void Choices(VBoxContainer body, SetResult set)
    {
        var changed = set.Choices.Where(c => !c.Keep).ToList();
        if (changed.Count == 0)
            return;
        var column = Ui.Add(body, Ui.Column(6, "GearSearchChoices"));
        foreach (var choice in changed)
        {
            var row = Ui.Add(column, Ui.Row(6, Ui.Id("GearSearchChoice", choice.Name)));
            row.AddChild(Icons(choice.Old, choice.Kind));
            row.AddChild(Ui.Label("→", TextStyle.Small));
            row.AddChild(Icons(choice.New, choice.Kind));
            var words = Ui.Add(row, Ui.Column(0));
            words.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            words.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            DiffersTint(
                Ui.Add(words, Ui.Label(Names(choice.NewNames), TextStyle.Body, wrap: true)),
                choice.DiffersFromBest
            );
            var under = new List<string> { Label(choice) };
            under.AddRange(choice.Supplies.Select(Supply));
            words.AddChild(Ui.Label(string.Join(" · ", under), TextStyle.Tiny, wrap: true));
            // Under the name, wrapping: a game's reason can be long, and beside the name it would widen the row.
            if (MarkText(choice.Mark) is { Length: > 0 } mark)
            {
                var tag = Ui.Add(
                    words,
                    Ui.Label(mark, TextStyle.Tiny, Ui.Id("GearSearchMark", choice.Name), wrap: true)
                );
                tag.LabelSettings = Compare.CompareText.Coloured(Look.Risky, TextStyle.Tiny);
            }
            foreach (var id in choice.New)
                GapMark(row, set, id);
        }
    }

    /// <summary>
    /// A small warning mark at the end of <paramref name="row"/> when the fights leave out some of the set's part
    /// <paramref name="id"/>; its hover says what, and why.
    /// </summary>
    public static void GapMark(HBoxContainer row, SetResult set, string? id)
    {
        var gaps =
            id == null
                ? []
                : set.Result.NotSimulated?.Where(g => g.Owner.EndsWith(":" + id, StringComparison.Ordinal)).ToList()
                    ?? [];
        if (gaps.Count == 0)
            return;
        var mark = Ui.Add(row, Ui.Label("!", TextStyle.BodyStrong, Ui.Id("GearSearchGap", Ids.Brief(id!))));
        mark.LabelSettings = Compare.CompareText.Coloured(Look.Risky, TextStyle.BodyStrong);
        mark.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        mark.MouseFilter = Control.MouseFilterEnum.Stop;
        // Translators: Heading of the hover on the mark beside a part of a setup search's answer; what the fights left
        // out of the part follows, one per line.
        mark.TooltipText = Tr.Get("Not in this answer:") + "\n" + string.Join("\n", gaps.Select(g => Tr.Note(g.What)));
    }

    /// <summary>The folds: what the setup uses, the damage it takes, and its close calls.</summary>
    public static void Folds(VBoxContainer body, Answer answer, SetResult set)
    {
        if (!IsSetup(answer))
            return;
        if (set.Supplies.Count > 0)
        {
            // Translators: Folded section of a setup search's answer: what the setup uses up, each in its own units.
            var fold = Fold(body, "GearSearchUses", Tr.Get("Uses"), Ui.Number(set.Supplies.Count));
            var grid = Grid(fold.Body, "GearSearchUsesList", 2);
            foreach (var cost in set.Supplies)
            {
                Cell(grid, SupplyName(cost), expand: true);
                Cell(grid, Ui.Number(PerUnit(cost.PerHour.Point)) + Unit());
            }
        }
        if (set.DamageTaken is { } taken)
        {
            var perSecond = taken.Point;
            var summary = Ui.Number(PerUnit(perSecond * 3600)) + Unit();
            // Translators: Folded section of a setup search's answer: the damage it takes and the food it eats.
            var fold = Fold(body, "GearSearchDamage", Tr.Get("Damage taken"), summary);
            var grid = Grid(fold.Body, "GearSearchDamageList", 2);
            Cell(grid, Words.PerUnit(UnitShown()), expand: true);
            Cell(grid, summary);
            // Translators: In a setup search's damage taken: the damage taken per second of fighting.
            Cell(grid, Tr.Get("Per second"), expand: true);
            Cell(grid, Ui.Number(perSecond));
            if (set.Food is { Point: > 0 } food)
            {
                Cell(grid, Tr.Get("Food eaten"), expand: true);
                Cell(grid, Ui.Number(PerUnit(food.Point)) + Unit());
            }
        }
        var calls = answer
            .CloseCalls.Where(c => c.Outcome != CloseCallOutcome.Kept && c.Difference is not { Low: 0, High: 0 })
            .ToList();
        if (calls.Count == 0)
            return;
        // Translators: Folded section of a setup search's answer: options about as good, raced again to settle.
        var calling = Fold(body, "GearSearchCloseCalls", Tr.Get("Close calls"), Ui.Number(calls.Count));
        var rows = Grid(calling.Body, "GearSearchCloseCallList", 3);
        foreach (var call in calls)
        {
            var icons = Ui.Add(rows, Ui.Row(4));
            // As every row reads: what the answer holds, then the other option, named beside it.
            icons.AddChild(Icons(call.KeptIds, ChoiceKind.Prayers));
            icons.AddChild(Ui.Label("⇄", TextStyle.Small));
            icons.AddChild(Icons(call.RunnerUpIds, ChoiceKind.Prayers));
            var name = Cell(rows, call.RunnerUp, expand: true);
            name.MouseFilter = Control.MouseFilterEnum.Stop;
            name.TooltipText = CloseCall(call);
            Cell(rows, call.Difference is { } d ? Signed(d.Point) : "");
        }
    }

    private static Disclosure Fold(VBoxContainer body, string name, string title, string summary)
    {
        var fold = new Disclosure(name, title, summary, false, _ => { });
        body.AddChild(fold.Root);
        return fold;
    }

    private static GridContainer Grid(VBoxContainer parent, string name, int columns)
    {
        var grid = Ui.Add(parent, new GridContainer { Name = name, Columns = columns });
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 4);
        return grid;
    }

    private static Label Cell(GridContainer grid, string text, bool expand = false)
    {
        var label = Ui.Add(grid, expand ? Ui.Clipped(text, TextStyle.Small) : Ui.Label(text, TextStyle.Small));
        label.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        if (expand)
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        else
            label.HorizontalAlignment = HorizontalAlignment.Right;
        return label;
    }

    private static Label Chip(string name, string text, string hover)
    {
        var chip = Ui.Label(text, TextStyle.Tiny, name);
        chip.MouseFilter = Control.MouseFilterEnum.Stop;
        chip.TooltipText = hover;
        chip.Modulate = Colors.White with { A = 0.8f };
        return chip;
    }

    private static string CloseCall(CloseCall call)
    {
        var range = call.Difference is { } d ? ", " + Signed(d.Point) : "";
        return call.Outcome == CloseCallOutcome.Swapped
            // Translators: A close call the longer race settled: {0} won over {1}.
            ? Tr.Format("{0} won over {1}{2}", [call.RunnerUp, call.Kept, range])
            // Translators: A close call still too close: {0} is about as good as the kept {1}.
            : Tr.Format("Also fine: {0} instead of {1}{2}", [call.RunnerUp, call.Kept, range]);
    }

    // A share as a signed percentage; one that rounds to nothing has no sign.
    private static string Signed(double share) =>
        Math.Round(share * 1000) == 0 ? Ui.Percent(0)
        : share > 0 ? "+" + Ui.Percent(share)
        : "−" + Ui.Percent(-share);

    // The options' icons side by side; none as an empty slot.
    private static HBoxContainer Icons(List<string> ids, ChoiceKind kind)
    {
        var row = Ui.Row(2, "GearSearchChoiceIcons" + kind);
        if (ids.Count == 0)
            row.AddChild(Slot.Make(null, 32));
        foreach (var id in ids)
        {
            var picture = Slot.Make(ChangeOptions.Icon(id), 32);
            picture.MouseFilter = Control.MouseFilterEnum.Stop;
            picture.TooltipText = PanelState.Names()(id);
            row.AddChild(picture);
        }
        return row;
    }

    private static string Label(ChoiceChange choice) =>
        choice.Kind switch
        {
            ChoiceKind.Prayers => Tr.Get("Prayers"),
            ChoiceKind.Spell => Tr.Get("Spell"),
            ChoiceKind.Imbuement => Tr.Get("Imbuement"),
            ChoiceKind.Food => Tr.Get("Food"),
            ChoiceKind.Potion => Tr.Get("Potion"),
            _ when choice.Subject is { } slot => PanelState.Names()(slot),
            _ => Tr.Get("Toolbelt"),
        };

    private static string Names(List<string> names) => names.Count == 0 ? Tr.Get("None") : string.Join(", ", names);

    private static string MarkText(Mark mark) =>
        mark.Kind switch
        {
            // Translators: Mark on a choice of a setup search's answer that the bank holds none of.
            MarkKind.NotInBank => Tr.Get("Not in your bank"),
            MarkKind.Locked when mark.Needs is { } needs => Tr.Note(needs),
            // Translators: Mark on a choice the character can't use yet, when the game gives no reason.
            MarkKind.Locked => Tr.Get("Locked"),
            _ => "",
        };

    // "Prayer points 3,678 /h", in the unit the panel shows rates in.
    private static string Supply(SupplyCost cost) =>
        $"{SupplyName(cost)} {Ui.Number(PerUnit(cost.PerHour.Point))}{Unit()}";

    private static string SupplyName(SupplyCost cost) =>
        cost.Name is { } name ? Tr.Note(name) : PanelState.Names()(cost.Key);

    // The panel's unit, an hour when it is per kill: a supply's rate is by time.
    private static TimeUnit UnitShown() =>
        PanelState.Prefs.Unit == TimeUnit.Kill ? TimeUnit.Hour : PanelState.Prefs.Unit;

    private static double PerUnit(double perHour) => perHour * TimeUnits.Seconds(UnitShown()) / 3600;

    private static string Unit() => " " + Words.PerUnitShort(UnitShown());
}
