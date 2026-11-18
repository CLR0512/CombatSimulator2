using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Stats;

// The modifiers: one list of group headings and lines, of which only the rows in view exist. A heading folds its
// group; the search narrows the lines and opens the groups that have matches.
internal sealed partial class FullStatsView
{
    // A heading (no side) or a modifier line; a heading carries its group's summary.
    private readonly record struct Entry(ModifierGroup Group, ModifierSide? Side, string Summary);

    private Label _modCount = null!;
    private LineEdit _search = null!;
    private Control _legend = null!;
    private Label _noMatch = null!;
    private VirtualRows _list = null!;
    private IReadOnlyList<ModifierSide> _modifiers = [];
    private readonly List<Entry> _entries = [];
    private int _modTotal;

    public static string GroupName(ModifierGroup group) =>
        group switch
        {
            ModifierGroup.Equipment => Tr.Get("Equipment"),
            ModifierGroup.SetBonus => Tr.Get("Set bonuses"),
            ModifierGroup.Prayer => Tr.Get("Prayers"),
            ModifierGroup.Toolbelt => Tr.Get("Potions and toolbelt"),
            ModifierGroup.Food => Tr.Get("Food"),
            ModifierGroup.StanceSpell => Tr.Get("Stance and spell"),
            ModifierGroup.Pet => Tr.Get("Pets"),
            ModifierGroup.Shop => Tr.Get("Shop"),
            ModifierGroup.Mastery => Tr.Get("Mastery"),
            ModifierGroup.Agility => Tr.Get("Agility"),
            ModifierGroup.Astrology => Tr.Get("Astrology"),
            ModifierGroup.Area => Tr.Get("Area"),
            _ => Tr.Get("Other"),
        };

    /// <summary>The modifier lines of <paramref name="sheets"/>: one side's, or A's and B's marked by side.</summary>
    public static IReadOnlyList<ModifierSide> ModifiersOf(IReadOnlyList<SetupSheet> sheets) =>
        sheets.Count > 1
            ? SheetDiff.Of(sheets[0], sheets[1]).Modifiers
            : [.. sheets[0].Modifiers.Select(m => new ModifierSide(m, true, false))];

    /// <summary>"Stone Helmet: +5% Melee Accuracy (on the enemy)": a line as one piece of text.</summary>
    public static string LineText(ModifierLine line) =>
        line.SourceName + ": " + (line.OnEnemy ? Tr.Format("{0} (on the enemy)", line.Description) : line.Description);

    private PanelContainer BuildModifiers()
    {
        var mods = Look.Panel(Art.SideBox, "Cs2ModifiersPanel");
        var column = Ui.Add(mods, Ui.Column(8));
        var head = Ui.Add(column, Ui.Row(10));
        head.AddChild(Ui.Label(Tr.Get("Modifiers"), TextStyle.Heading));
        _modCount = Ui.Label("", TextStyle.Tiny);
        _modCount.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        head.AddChild(_modCount);
        _search = new LineEdit
        {
            Name = "Cs2ModSearch",
            PlaceholderText = Tr.Get("Search modifiers or sources"),
            ClearButtonEnabled = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        Look.DarkField(_search);
        _search.TextChanged += _ => ListEntries();
        column.AddChild(_search);
        var marks = Tr.Format("{0} {1}: both sides · {0} only, {1} only: one side", SideLine.A, SideLine.B);
        _legend = SideLine.Make(marks, TextStyle.Tiny, wrap: true);
        column.AddChild(_legend);
        _noMatch = Ui.Label("", TextStyle.Tiny, wrap: true);
        column.AddChild(_noMatch);
        _list = new VirtualRows("Cs2ModGroups", 30, MakeRow, BindRow);
        column.AddChild(_list.Root);
        return mods;
    }

    private void DrawModifiers()
    {
        _modifiers = ModifiersOf(_sheets);
        _modTotal = _modifiers.Count;
        var sources = _modifiers.Select(s => (s.Line.Group, s.Line.SourceId)).Distinct().Count();
        _modCount.Text = Tr.Format("{0} from {1} sources", [_modifiers.Count, sources]);
        _legend.Visible = _sheets.Count > 1;
        ListEntries();
    }

    // The headings and lines to list: Only differences keeps lines on one side; a search keeps matches and opens their
    // groups; otherwise a folded group lists its heading alone.
    private void ListEntries()
    {
        _entries.Clear();
        var text = _search.Text;
        var searching = !string.IsNullOrWhiteSpace(text);
        var only = _sheets.Count > 1 && PanelState.Prefs.StatsOnlyDifferences;
        var without = new List<string>();
        foreach (var group in Enum.GetValues<ModifierGroup>())
        {
            var lines = _modifiers.Where(s => s.Line.Group == group && (!only || s.OneSide)).ToList();
            if (lines.Count == 0)
                continue;
            var shown = lines.Where(s => ModifierSearch.Matches(s.Line, text)).ToList();
            if (searching && shown.Count == 0)
            {
                without.Add(GroupName(group));
                continue;
            }
            var oneSide = _sheets.Count > 1 ? shown.Count(s => s.OneSide) : 0;
            var count = searching
                ? Tr.Format("{0} of {1}", [shown.Count, lines.Count])
                : lines.Count.ToString(Tr.Culture);
            _entries.Add(new(group, null, oneSide > 0 ? Tr.Format("{0} · {1} on one side", [count, oneSide]) : count));
            if (searching || PanelState.Prefs.OpenModifierGroups.Contains(group.ToString()))
                foreach (var side in shown)
                    _entries.Add(new(group, side, ""));
        }
        _noMatch.Text = without.Count > 0 ? Tr.Format("No match in {0}.", string.Join(", ", without)) : "";
        _noMatch.Visible = without.Count > 0;
        _list.SetCount(_entries.Count);
    }

    private void Fold(ModifierGroup group)
    {
        var key = group.ToString();
        if (!PanelState.Prefs.OpenModifierGroups.Remove(key))
            PanelState.Prefs.OpenModifierGroups.Add(key);
        PanelState.Save();
        ListEntries();
    }

    // A row: a mark (the sign of a line, or a heading's fold), its text clipped to one line with the whole of it as a
    // tooltip, and the sides that have it.
    private HBoxContainer MakeRow()
    {
        var row = Ui.Row(6, "Cs2Modifier");
        row.MouseFilter = Control.MouseFilterEnum.Stop;
        var mark = Ui.Label("", TextStyle.Small, "Mark");
        mark.CustomMinimumSize = new Vector2(18, 0);
        row.AddChild(mark);
        row.AddChild(Ui.Clipped("", TextStyle.Small, "Text"));
        row.AddChild(Ui.Label("", TextStyle.Tiny, "Where"));
        row.GuiInput += e =>
        {
            if (
                e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }
                && row.GetMeta("cs2_entry", -1).AsInt32() is var i and >= 0
                && i < _entries.Count
                && _entries[i].Side == null
            )
                Fold(_entries[i].Group);
        };
        return row;
    }

    private void BindRow(Control row, int index)
    {
        row.SetMeta("cs2_entry", index);
        var entry = _entries[index];
        var mark = row.GetNode<Label>("Mark");
        var text = row.GetNode<Label>("Text");
        var where = row.GetNode<Label>("Where");
        if (entry.Side is not { } side)
        {
            var open =
                !string.IsNullOrWhiteSpace(_search.Text)
                || PanelState.Prefs.OpenModifierGroups.Contains(entry.Group.ToString());
            mark.Text = open ? "▾" : "▸";
            mark.Modulate = Colors.White;
            Ui.SetClipped(text, GroupName(entry.Group));
            text.LabelSettings = Look.Style(TextStyle.Label);
            text.Modulate = Colors.White;
            where.Text = entry.Summary;
            row.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
            return;
        }
        var (glyph, colour) = side.Line.Kind switch
        {
            ModifierSign.Positive => ("▲", Look.Safe),
            ModifierSign.Negative => ("▼", Look.Deadly),
            _ => ("▶", Look.Risky),
        };
        mark.Text = glyph;
        mark.Modulate = colour;
        Ui.SetClipped(text, LineText(side.Line));
        text.LabelSettings = Look.Style(TextStyle.Small);
        text.Modulate = Colors.White;
        where.Text =
            _sheets.Count < 2 ? ""
            : side.InA && side.InB ? Tr.Get("A B")
            : side.InA ? Tr.Get("A only")
            : Tr.Get("B only");
        // Only the side marks of a line on one side are coloured: that is the difference.
        where.Modulate = side.OneSide ? Look.Risky : Colors.White;
        row.MouseDefaultCursorShape = Control.CursorShape.Arrow;
    }
}
