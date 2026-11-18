using Godot;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel;

/// <summary>
/// The Scale setting as a slider, in the ☰ menu, quiet until the mouse is over it: from the smallest step to the
/// largest the screen allows. The window changes size when the slider is let go, not while it is dragged, so it
/// doesn't move under the mouse; the number beside it, shown only then and under the mouse, says where it would land.
/// </summary>
internal static class ScaleSlider
{
    private const float Dim = 0.5f;

    public static Control Make()
    {
        var row = Ui.Row(6, "Cs2ScaleSliderRow");
        row.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var slider = Ui.Add(
            row,
            new HSlider
            {
                Name = "Cs2ScaleSlider",
                MinValue = ScaleSteps.Smallest * 100,
                Step = 5,
                Scrollable = false,
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(110, 0),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                TooltipText = Tr.Get("Scale"),
            }
        );
        // The theme's resting knob is see-through and shows the bar it stands on; the one it shows under the mouse
        // is solid, and the dimming already tells the two apart.
        void Knob()
        {
            if (slider.HasThemeIcon("grabber_highlight", "HSlider"))
                slider.AddThemeIconOverride("grabber", slider.GetThemeIcon("grabber_highlight", "HSlider"));
        }
        Look.OnTheme(slider, Knob);
        Knob();
        var value = Ui.Add(row, Ui.Label("", TextStyle.Tiny, "Cs2ScaleSliderValue"));
        value.HorizontalAlignment = HorizontalAlignment.Right;
        Ui.HoldWidth(value, Percent(ScaleSteps.Largest * 100));

        var over = false;
        var dragging = false;
        // The number keeps its place while hidden, so the slider doesn't move when it shows.
        void Light()
        {
            // Darkened, not made see-through: the knob would show the bar it stands on through itself.
            row.Modulate = over || dragging ? Colors.White : new Color(Dim, Dim, Dim);
            value.Modulate = over || dragging ? Colors.White : Colors.Transparent;
        }
        void Show()
        {
            slider.MaxValue = WindowScale.Cap * 100;
            slider.SetValueNoSignal(WindowScale.Drawn * 100);
            value.Text = Percent(slider.Value);
        }
        slider.MouseEntered += () =>
        {
            over = true;
            Light();
        };
        slider.MouseExited += () =>
        {
            over = false;
            Light();
        };
        slider.ValueChanged += percent => value.Text = Percent(percent);
        slider.DragStarted += () =>
        {
            dragging = true;
            Light();
        };
        slider.DragEnded += _ =>
        {
            dragging = false;
            Light();
            WindowScale.Set(ScaleSteps.Snap(slider.Value / 100));
        };
        WindowScale.Changed += Show;
        row.TreeExiting += () => WindowScale.Changed -= Show;
        Light();
        Show();
        return row;
    }

    private static string Percent(double percent) => percent.ToString("0", Tr.Culture);
}
