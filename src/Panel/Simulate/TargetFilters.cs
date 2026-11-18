using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Simulate;

/// <summary>
/// The filters of the row above the results: Can fight or All, the Monsters, Dungeons and Slayer toggles, and Areas ▾;
/// the search sits in the target strip. Each writes the target filter; what it shows is the strip's, the list's, the
/// chart's and the run's.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed class TargetFilters
{
    private const string CanFight = "can-fight";
    private const string Every = "all";

    private readonly HFlowContainer _root;
    private readonly LineEdit _search;
    private readonly Segmented _fight;
    private readonly (TargetTags Tag, Button Button)[] _kinds;
    private readonly Button _areas;
    private readonly AreaMenu _areaMenu;

    /// <summary>The row, with the Areas list drawn over <paramref name="owner"/> (CS2's window).</summary>
    public TargetFilters(Control owner)
    {
        // One row where it fits; where the text is longer (another language, a narrower room) it wraps to a
        // second row rather than push the window past the screen's edge.
        _root = new HFlowContainer { Name = "Cs2TargetFilters" };
        _root.AddThemeConstantOverride("h_separation", 4);
        _root.AddThemeConstantOverride("v_separation", 6);
        _search = new LineEdit
        {
            Name = "Cs2Search",
            PlaceholderText = Tr.Get("Search targets"),
            ClearButtonEnabled = true,
            CustomMinimumSize = new Vector2(170, 0),
            // The magnifier the game's own search bars carry, as the Bank's does.
            RightIcon = Art.File(Art.SearchBarPath),
        };
        Look.DarkField(_search);
        _search.TextChanged += text => PanelState.SetFilter(PanelState.Prefs.Filter with { Search = text });
        _fight = new Segmented(
            "Cs2FilterFight",
            [(CanFight, Tr.Get("Can fight"), "Cs2FilterCanFight"), (Every, Tr.Get("All", "targets"), "Cs2FilterAll")],
            chosen => PanelState.SetFilter(PanelState.Prefs.Filter with { CanFightOnly = chosen == CanFight }),
            side: 6,
            ends: 3
        );
        _fight.Button(CanFight).TooltipText = Tr.Get("The targets this setup can fight");
        _fight.Button(Every).TooltipText = Tr.Get("Every target in the game, locked ones included");
        _root.AddChild(_fight.Root);
        _kinds =
        [
            (TargetTags.Monsters, Kind(Tr.Get("Monsters"), "Cs2FilterMonsters", TargetTags.Monsters)),
            (TargetTags.Dungeons, Kind(Tr.Get("Dungeons"), "Cs2FilterDungeons", TargetTags.Dungeons)),
            (TargetTags.Slayer, Kind(Tr.Get("Slayer"), "Cs2FilterSlayer", TargetTags.Slayer)),
            // Translators: Filter toggle: the Slayer masters' tasks as targets in the results.
            (TargetTags.Masters, Kind(Tr.Get("Masters"), "Cs2FilterMasters", TargetTags.Masters)),
        ];
        foreach (var (_, button) in _kinds)
            _root.AddChild(button);
        // Styled as the filter buttons beside it, so the row's gaps are even; it opens the Areas list under it.
        _areaMenu = new AreaMenu(owner);
        _areaMenu.Picked += AreaPicked;
        _areas = Look.Narrow(Ui.Button("", Look.Tab, "Cs2FilterAreas", ToggleAreas), 6, 3);
        _root.AddChild(_areas);
        Refresh();
    }

    public HFlowContainer Root => _root;

    /// <summary>The search field, placed in the target strip with the targets it narrows.</summary>
    public LineEdit Search => _search;

    // A toggle for one kind of target: a target shows when any of its kinds is on.
    private static Button Kind(string text, string name, TargetTags tag)
    {
        var button = Look.Narrow(Ui.Button(text, Look.Tab, name), 6, 3);
        button.ToggleMode = true;
        button.Toggled += on =>
        {
            var kinds = PanelState.Prefs.Filter.Kinds;
            PanelState.SetFilter(PanelState.Prefs.Filter with { Kinds = on ? kinds | tag : kinds & ~tag });
        };
        return button;
    }

    /// <summary>Shows the filter as it is, without raising anything.</summary>
    public void Refresh()
    {
        var filter = PanelState.Prefs.Filter;
        if (_search.Text != filter.Search)
            _search.Text = filter.Search;
        _fight.Show(filter.CanFightOnly ? CanFight : Every);
        foreach (var (tag, button) in _kinds)
            button.SetPressedNoSignal(filter.Kinds.HasFlag(tag));
        _areas.Text = filter.Areas is { Count: > 0 } chosen
            ? Tr.Format("Areas ({0}) ▾", chosen.Count)
            : Tr.Get("Areas ▾");
    }

    private void ToggleAreas()
    {
        if (_areaMenu.Open)
            _areaMenu.Close();
        else
            _areaMenu.Show(_areas);
    }

    // With every area shown, picking one shows that one alone; after that each pick adds or takes away an area, and
    // taking away the last shows every area again. Null is All areas.
    private void AreaPicked(string? area)
    {
        var filter = PanelState.Prefs.Filter;
        if (area == null)
            PanelState.SetFilter(filter with { Areas = null });
        else
        {
            var areas = filter.Areas?.ToList() ?? [];
            if (!areas.Remove(area))
                areas.Add(area);
            PanelState.SetFilter(filter with { Areas = areas.Count > 0 ? areas : null });
        }
        _areaMenu.Tick();
    }
}
