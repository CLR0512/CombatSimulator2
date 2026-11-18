using Godot;
using MelvorMods.CombatSimulator2.Game;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>The Settings view's rows: sections, a setting with its title and help, and dropdowns.</summary>
internal static class SettingRow
{
    /// <summary>A section of settings under its title, added to <paramref name="parent"/>.</summary>
    public static VBoxContainer Section(VBoxContainer parent, string title)
    {
        var ribbon = Look.Panel(Art.RibbonBox);
        ribbon.CustomMinimumSize = new Vector2(280, 0);
        ribbon.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        ribbon.AddChild(Ui.Label(title, TextStyle.Heading));
        parent.AddChild(ribbon);
        var panel = Look.Panel(Art.RowBox);
        parent.AddChild(panel);
        // Room on the right, so the controls at the rows' ends don't meet the box's edge.
        var column = Ui.Column(14);
        panel.AddChild(Ui.Margin(column, 0, 0, 12, 0));
        return column;
    }

    // A row: title and help on the left, the control on the right. Returns the help label.
    public static Label Setting(VBoxContainer section, string title, string help, Control control)
    {
        var row = Ui.Add(section, Ui.Row(12));
        var text = Ui.Add(row, Ui.Column(2));
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        text.AddChild(Ui.Label(title, TextStyle.BodyStrong));
        var helpLabel = Ui.Label(help, TextStyle.Tiny, wrap: true);
        helpLabel.Visible = help.Length > 0;
        text.AddChild(helpLabel);
        control.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(control);
        return helpLabel;
    }

    /// <summary>A row whose control is <paramref name="dropdown"/>.</summary>
    public static Label Setting(VBoxContainer section, string title, string help, Dropdown dropdown) =>
        Setting(section, title, help, dropdown.Root);

    /// <summary>A dropdown of <paramref name="items"/> with <paramref name="selected"/> chosen.</summary>
    public static Dropdown Options(string name, IEnumerable<string> items, int selected, Action<int> chosen)
    {
        var options = new Dropdown(name, Dropdown.Section);
        foreach (var item in items)
            options.AddItem(item);
        options.Select(Math.Max(0, selected));
        options.ItemSelected += chosen;
        return options;
    }
}
