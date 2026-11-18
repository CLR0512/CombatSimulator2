using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Settings;

/// <summary>
/// The Scale setting: smaller and larger by one step, the scale drawn between them, and a note when the screen holds
/// less than the largest step.
/// </summary>
internal sealed class ScaleRow
{
    private readonly Button _down;
    private readonly Button _up;
    private readonly Label _value;
    private readonly Label _cap;

    /// <summary>Adds the Scale setting to <paramref name="section"/>.</summary>
    public ScaleRow(VBoxContainer section)
    {
        var stepper = Ui.Row(4, "Cs2Scale");
        _down = Ui.Button("−", Look.Tab, "Cs2ScaleDown", () => WindowScale.Set(ScaleSteps.Down(WindowScale.Drawn)));
        _down.TooltipText = Tr.Get("Smaller");
        stepper.AddChild(Look.Narrow(_down, 10));
        var box = Look.Panel(Art.SideBox, "Cs2ScaleValue");
        box.CustomMinimumSize = new Vector2(72, 0);
        _value = Ui.Label("", TextStyle.BodyStrong);
        _value.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(_value);
        stepper.AddChild(box);
        _up = Ui.Button(
            "+",
            Look.Tab,
            "Cs2ScaleUp",
            () => WindowScale.Set(ScaleSteps.Up(WindowScale.Drawn, WindowScale.Cap))
        );
        _up.TooltipText = Tr.Get("Larger");
        stepper.AddChild(Look.Narrow(_up, 10));
        var help = SettingRow.Setting(
            section,
            Tr.Get("Scale"),
            Tr.Get("Everything in this window. On top of the game's UI size."),
            stepper
        );
        _cap = Ui.Label("", TextStyle.Tiny, "Cs2ScaleCap", wrap: true);
        _cap.LabelSettings = CapStyle();
        help.GetParent().AddChild(_cap);
        WindowScale.Changed += Show;
        stepper.TreeExiting += () => WindowScale.Changed -= Show;
        Show();
    }

    /// <summary>Shows the scale drawn, which buttons can press, and the cap's note when the screen holds less.</summary>
    public void Show()
    {
        var drawn = WindowScale.Drawn;
        _value.Text = Tr.Format("{0:0}%", drawn * 100);
        _down.Disabled = drawn <= ScaleSteps.Smallest;
        _up.Disabled = drawn >= WindowScale.Cap;
        _cap.Visible = WindowScale.Cap < ScaleSteps.Largest;
        _cap.Text = Tr.Format("Up to {0:0}% on this screen.", WindowScale.Cap * 100);
    }

    private static LabelSettings CapStyle()
    {
        var settings = (LabelSettings)Look.Style(TextStyle.Tiny).Duplicate();
        Look.MarkSkipped(settings);
        settings.FontColor = Look.Best;
        return settings;
    }
}
