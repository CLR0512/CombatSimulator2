using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Simulate;

/// <summary>
/// A setup card's lines beside its gear: the style, the food, a line per prayer that is on (and, while a slot is
/// left, a quiet one that adds another) and the spell, each with its icon. A click on a line opens the picker for
/// what it shows. Main thread.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed class CardLines
{
    private const float LineIcon = 16;

    private readonly Action<string> _pressed;
    private readonly Label _style;
    private readonly Label _food;
    private readonly Label _spell;
    private readonly TextureRect _styleIcon;
    private readonly TextureRect _foodIcon;
    private readonly TextureRect _spellIcon;
    private readonly VBoxContainer _prayers;

    // The lines a click opens the picker on, by their key, so the picker can stand under the one it is for.
    private readonly Dictionary<string, Control> _rows = new(StringComparer.Ordinal);

    /// <summary>The lines in <paramref name="parent"/>; <paramref name="pressed"/> gets a clicked line's key.</summary>
    public CardLines(Container parent, Action<string> pressed)
    {
        _pressed = pressed;
        (_styleIcon, _style) = IconLine(parent, "Cs2CardStyle", SlotPicker.StanceLine);
        (_foodIcon, _food) = IconLine(parent, "Cs2CardFood", SlotPicker.FoodLine);
        _prayers = Ui.Add(parent, Ui.Column(2, "Cs2CardPrayers"));
        (_spellIcon, _spell) = IconLine(parent, "Cs2CardSpell", SlotPicker.SpellLine);
    }

    /// <summary>The line drawn for <paramref name="key"/>; null when it isn't drawn.</summary>
    public Control? Row(string key) =>
        _rows.TryGetValue(key, out var row) && GodotObject.IsInstanceValid(row) && row.IsInsideTree() ? row : null;

    /// <summary>Nothing read yet: the style line says so, the others are blank.</summary>
    public void Checking()
    {
        Ui.SetClipped(_style, Tr.Get("Checking…"));
        foreach (var line in new[] { _food, _spell })
            Ui.SetClipped(line, "");
        foreach (var icon in new[] { _styleIcon, _foodIcon, _spellIcon })
            icon.Texture = null;
        Ui.Clear(_prayers);
    }

    /// <summary>Shows the side after its changes, from <paramref name="state"/> and its <paramref name="gear"/>.</summary>
    public void Show(SetupState state, SetupGear gear, Func<string, string> names)
    {
        Ui.SetClipped(
            _style,
            string.Join(" · ", new[] { gear.Style, gear.Stance }.Where(p => !string.IsNullOrEmpty(p)))
        );
        Icon(_styleIcon, state.Stance, LiveGame.CombatId);
        Ui.SetClipped(_food, state.Food is { } food ? names(food) : Tr.Get("No food"));
        Icon(_foodIcon, state.Food, Changes.HitpointsId);
        ShowPrayers(
            [.. state.Prayers.Where(p => p.On).Select(p => p.Prayer)],
            state.PrayerSlots.Count(s => s.Unlocked),
            names
        );
        Ui.SetClipped(_spell, state.Spell is { } spell ? names(spell) : Tr.Get("No spell"));
        Icon(_spellIcon, state.Spell, "MelvorBase:Magic");
    }

    // A line of the side's facts with a small icon before it; a click on it opens the picker for what it shows.
    private (TextureRect Icon, Label Text) IconLine(Container parent, string name, string key)
    {
        var row = Ui.Add(parent, Ui.Row(6, name + "Line"));
        var icon = Ui.Add(row, Ui.Picture(null, LineIcon));
        var text = Ui.Add(row, Ui.Clipped("", TextStyle.Small, name));
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.MouseFilter = Control.MouseFilterEnum.Stop;
        row.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        row.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                row.AcceptEvent();
                _pressed(key);
            }
        };
        _rows[key] = row;
        return (icon, text);
    }

    // The thing's own icon, or its kind's dimmed when there is none (no food, no spell).
    private static void Icon(TextureRect icon, string? id, string none)
    {
        icon.Texture = ChangeOptions.Icon(id ?? none);
        icon.Modulate = Colors.White with { A = id == null ? 0.35f : 1f };
    }

    // A line per prayer that is on, and while an unlocked slot is left, a quiet one that adds another.
    private void ShowPrayers(List<string> on, int slots, Func<string, string> names)
    {
        Ui.Clear(_prayers);
        foreach (var key in _rows.Keys.Where(SlotPicker.IsPrayerLine).ToList())
            _rows.Remove(key);
        if (on.Count == 0)
        {
            var (icon, text) = IconLine(_prayers, "Cs2CardPrayer", SlotPicker.PrayerLine(null));
            Ui.SetClipped(text, Tr.Get("No prayers"));
            Icon(icon, null, "MelvorBase:Prayer");
            return;
        }
        for (var i = 0; i < on.Count; i++)
        {
            var (icon, text) = IconLine(
                _prayers,
                "Cs2CardPrayer" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                SlotPicker.PrayerLine(on[i])
            );
            Ui.SetClipped(text, names(on[i]));
            Icon(icon, on[i], "MelvorBase:Prayer");
        }
        if (on.Count >= slots)
            return;
        var (add, more) = IconLine(_prayers, "Cs2CardPrayerAdd", SlotPicker.PrayerLine(null));
        Ui.SetClipped(more, Tr.Get("+ Prayer"));
        more.TooltipText = Tr.Get("Add prayer");
        more.Modulate = Colors.White with { A = 0.55f };
        Icon(add, null, "MelvorBase:Prayer");
    }
}
