using Godot;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Simulate;

/// <summary>
/// Six of the game's own stats for the setup under its card: max hit, accuracy, attack interval, max hitpoints,
/// evasion and protection, with accuracy and evasion those of the setup's own stance. While comparing each reads
/// A → B and only the difference is coloured. "All stats" opens every stat and modifier.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed class KeyStats
{
    private const int Rows = 6;

    private readonly VBoxContainer _root;
    private readonly (Label Name, Label Value, Label Difference)[] _rows = new (Label, Label, Label)[Rows];
    private readonly SideLine _between;
    private string _shown = "";

    public KeyStats()
    {
        _root = Ui.Column(3, "Cs2KeyStats");
        var head = Ui.Add(_root, Ui.Row(6));
        head.AddChild(Ui.Label(Tr.Get("Key stats"), TextStyle.Label));
        // What the arrow in each row runs between: nothing else says whether it is two sides or one side's changes.
        _between = new SideLine(TextStyle.Tiny, "Cs2KeyStatsBetween");
        _between.Root.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _between.Root.Visible = false;
        head.AddChild(_between.Root);
        head.AddChild(Ui.Spacer());
        var all = Ui.Button(Tr.Get("All stats ▸"), Look.Button4, "Cs2AllStats", () => AllPressed?.Invoke());
        all.Flat = true;
        head.AddChild(all);
        for (var i = 0; i < Rows; i++)
        {
            var row = Ui.Add(
                _root,
                Ui.Row(6, "Cs2KeyStat" + i.ToString(System.Globalization.CultureInfo.InvariantCulture))
            );
            var name = Ui.Clipped("", TextStyle.Small);
            row.AddChild(name);
            var value = Ui.Label("", TextStyle.Small);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            row.AddChild(value);
            var difference = Ui.Label("", TextStyle.Small);
            difference.HorizontalAlignment = HorizontalAlignment.Right;
            difference.CustomMinimumSize = new Vector2(52, 0);
            row.AddChild(difference);
            _rows[i] = (name, value, difference);
        }
    }

    public Control Root => _root;

    public event Action? AllPressed;

    /// <summary>The rows as <c>cs2 ui</c> prints them; empty before a sheet is shown.</summary>
    public string Shown => _shown;

    /// <summary>
    /// Shows side A's stats, beside side B's when <paramref name="b"/> is given; dashes while a side's sheet hasn't
    /// been read. With <paramref name="ownChanges"/> the two are one side before and after its changes.
    /// </summary>
    public void Show(SetupSheet? a, SetupSheet? b, string? stance, bool ownChanges = false)
    {
        _between.Root.Visible = b != null;
        if (ownChanges)
            _between.Show(Tr.Get("start → with changes"));
        else if (b != null)
            _between.Show(Tr.Format("{0} → {1}", SideLine.A, SideLine.B));
        var keys = Keys(a ?? b, stance);
        var shown = new List<string>();
        for (var i = 0; i < Rows; i++)
        {
            var (name, value, difference) = _rows[i];
            var statA = i < keys.Count ? a?.Stats.FirstOrDefault(s => s.Key == keys[i]) : null;
            var statB = i < keys.Count ? b?.Stats.FirstOrDefault(s => s.Key == keys[i]) : null;
            var stat = statA ?? statB;
            // Before a sheet is read every row stands, with a dash: what is under the block keeps its place.
            _rows[i].Name.GetParent<Control>().Visible = keys.Count == 0 || i < keys.Count;
            Ui.SetClipped(name, stat == null ? "" : Tr.Note(ShortName(stat)));
            difference.Visible = b != null;
            if (b == null)
            {
                value.Text = statA?.Text ?? "–";
                shown.Add($"{(i < keys.Count ? keys[i] : "")}={value.Text}");
                continue;
            }
            // A stat both have alike is one number: the arrow is for what differs.
            value.Text =
                statA != null && statA.Text == statB?.Text
                    ? statA.Text
                    : Tr.Format("{0} → {1}", statA?.Text ?? "–", statB?.Text ?? "–");
            var delta = statA != null && statB != null ? SheetDiff.TextDelta(statA.Text, statB.Text) : null;
            difference.Text = delta ?? "";
            // Rows stay neutral; only the difference says whether B is better (a shorter interval is).
            var better =
                statA?.Value is { } x && statB?.Value is { } y
                    ? new StatDiff(keys[i], statA, statB, y - x).BBetter
                    : null;
            difference.Modulate = better switch
            {
                true => Look.Safe,
                false => Look.Deadly,
                null => Colors.White,
            };
            shown.Add($"{(i < keys.Count ? keys[i] : "")}={statA?.Text ?? "–"}>{statB?.Text ?? "–"}");
        }
        _shown = string.Join(' ', shown);
    }

    // The game's "Accuracy Rating (Slash)" leaves no room for "9,384 → 7,560" in this narrow column; All stats keeps
    // the game's name.
    private static Note ShortName(StatLine stat) =>
        stat.Key == "accuracy" && stat.Name.Args.Count == 1 ? Note.Of("Accuracy ({0})", stat.Name.Args[0]) : stat.Name;

    // The six keys in the sheet: evasion against the setup's own stance, the first protection the game lists.
    private static List<string> Keys(SetupSheet? sheet, string? stance)
    {
        if (sheet == null)
            return [];
        var keys = new List<string> { "max-hit", "accuracy", "attack-interval", "max-hp" };
        var evasion =
            sheet.Stats.FirstOrDefault(s => s.Key == "evasion:" + stance)
            ?? sheet.Stats.FirstOrDefault(s => s.Key.StartsWith("evasion:", StringComparison.Ordinal));
        if (evasion != null)
            keys.Add(evasion.Key);
        if (
            sheet.Stats.FirstOrDefault(s => s.Key.StartsWith("protection:", StringComparison.Ordinal)) is { } protection
        )
            keys.Add(protection.Key);
        return keys;
    }
}
