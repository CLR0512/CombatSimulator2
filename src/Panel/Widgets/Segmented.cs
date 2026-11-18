using Godot;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>
/// A small fixed set of choices as a row of buttons, one pressed at a time: Check or Ignore, the motion levels, the
/// side being set up. <c>framed</c> draws them as <see cref="FramedButton"/>s.
/// </summary>
internal sealed class Segmented
{
    private readonly Dictionary<string, Button> _buttons = new(StringComparer.Ordinal);

    public HBoxContainer Root { get; }

    /// <summary>
    /// A row named <paramref name="name"/> with one button per choice (its id, its text, its node name);
    /// <paramref name="chosen"/> gets the id of a button the player presses.
    /// </summary>
    public Segmented(
        string name,
        IReadOnlyList<(string Id, string Text, string Name)> choices,
        Action<string> chosen,
        float side = 10,
        float? ends = null,
        bool framed = false
    )
    {
        // Framed buttons stand apart: each has its own frame.
        Root = Ui.Row(framed ? 4 : 0, name);
        var group = new ButtonGroup();
        foreach (var (id, text, node) in choices)
        {
            var button = Ui.Button(text, Look.Tab, node, () => chosen(id));
            if (text.Contains(SideLine.A[0]))
                SideLine.OnButton(button, text);
            button.ToggleMode = true;
            button.ButtonGroup = group;
            Root.AddChild(framed ? FramedButton.Make(button, side, ends ?? 3) : Look.Narrow(button, side, ends));
            _buttons[id] = button;
        }
    }

    /// <summary>Presses the button of <paramref name="id"/> without telling anyone.</summary>
    public void Show(string id)
    {
        foreach (var (key, button) in _buttons)
            button.SetPressedNoSignal(key == id);
    }

    /// <summary>The button of <paramref name="id"/>, to grey it or give it a tooltip.</summary>
    public Button Button(string id) => _buttons[id];

    public void SetDisabled(bool disabled)
    {
        foreach (var button in _buttons.Values)
            button.Disabled = disabled;
    }
}
