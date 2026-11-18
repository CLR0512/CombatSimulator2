using Godot;
using MelvorMods.CombatSimulator2.Model.Search;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Search;

/// <summary>
/// What a setup search tries besides gear, in its start form: a switch per kind with how many options it adds, and
/// whether to include what the character doesn't have. Both are remembered.
/// </summary>
internal sealed class KindSwitches
{
    private readonly Dictionary<ChoiceKind, Button> _kindChecks = [];
    private readonly Control _kindsRow;
    private readonly Button _unowned;

    private static string KindName(ChoiceKind kind) =>
        kind switch
        {
            ChoiceKind.Prayers => Tr.Get("Prayers"),
            ChoiceKind.Spell => Tr.Get("Spell"),
            ChoiceKind.Imbuement => Tr.Get("Imbuement"),
            ChoiceKind.Food => Tr.Get("Food"),
            ChoiceKind.Toolbelt => Tr.Get("Toolbelt"),
            _ => Tr.Get("Potions"),
        };

    /// <summary>The switches; <paramref name="changed"/> hears of each change, to count the search again.</summary>
    public KindSwitches(Action changed)
    {
        var row = Ui.Row(8, "GearSearchKinds");
        // Translators: Label of the switches choosing what a setup search tries besides gear.
        var label = Ui.Add(row, Ui.Label(Tr.Get("Also try"), TextStyle.Label));
        label.CustomMinimumSize = new Vector2(76, 0);
        label.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        var flow = Ui.Add(row, new HFlowContainer { Name = "GearSearchKindsList" });
        flow.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        flow.AddThemeConstantOverride("h_separation", 10);
        flow.AddThemeConstantOverride("v_separation", 2);
        foreach (var kind in ChoiceKinds.All)
        {
            var check = Cs2Check.Make(
                "GearSearchKind" + kind,
                KindName(kind),
                false,
                on =>
                {
                    var kinds = PanelState.Prefs.SearchKindList.ToList();
                    kinds.Remove(kind);
                    if (on)
                        kinds.Add(kind);
                    PanelState.Prefs.SearchKinds = [.. kinds.Select(ChoiceKinds.Name)];
                    PanelState.Save();
                    changed();
                }
            );
            _kindChecks[kind] = check;
            flow.AddChild(check);
        }
        _kindsRow = row;
        _unowned = Cs2Check.Make(
            "GearSearchUnowned",
            // Translators: Tick in a setup search's form: also try prayers, food and the like the character lacks, and gear it owns but can't wear yet.
            Tr.Get("Include what I can't use yet"),
            false,
            on =>
            {
                PanelState.Prefs.SearchIncludeUnowned = on;
                PanelState.Save();
                changed();
            }
        );
        _unowned.TooltipText =
            Tr.Get(
                "Also tries prayers, spells, food and toolbelt items you don't have, and gear you own but can't wear yet."
            )
            + "\n"
            // Translators: Second line of the tooltip of "Include what I can't use yet".
            + Tr.Get("Gear you don't own: Find upgrades.");
    }

    /// <summary>The switches' row and the tick under it, which the form places.</summary>
    public IEnumerable<Control> Controls => [_kindsRow, _unowned];

    /// <summary>The switches as saved, for the best setup search only.</summary>
    public void Show(SearchKind kind)
    {
        _kindsRow.Visible = _unowned.Visible = kind == SearchKind.BestGear;
        var on = PanelState.Prefs.SearchKindList;
        foreach (var (each, check) in _kindChecks)
            Cs2Check.Set(check, on.Contains(each));
        Cs2Check.Set(_unowned, PanelState.Prefs.SearchIncludeUnowned);
    }

    /// <summary>Each switch with the options it adds; potions shown off while the build has none.</summary>
    public void Counts(PlanPreview preview)
    {
        foreach (var (kind, check) in _kindChecks)
        {
            var counts = preview.Kinds.Where(k => k.Kind == kind).ToList();
            var options = counts.Sum(k => k.Sets);
            var none = kind == ChoiceKind.Potion && !preview.PotionsInBuild;
            check.Disabled = none;
            check.Text = counts.Count == 0 ? KindName(kind) : $"{KindName(kind)} ({Ui.Number(options)})";
            check.TooltipText =
                none
                    // Translators: Hover of the Potions switch while the game has no combat potions.
                    ? Tr.Get("No combat potions in this build")
                : kind == ChoiceKind.Potion && counts.Count > 0 && options == 0
                    // Translators: Hover of the Potions switch while the character holds no combat potion to try.
                    ? Tr.Get("You have no combat potions")
                : "";
            if (none)
                Cs2Check.Set(check, false);
        }
    }

    /// <summary>The kinds a search of <paramref name="kind"/> tries.</summary>
    public static IReadOnlyList<ChoiceKind> For(SearchKind kind) =>
        kind == SearchKind.BestGear ? PanelState.Prefs.SearchKindList : [];
}
