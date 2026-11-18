using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Help;

/// <summary>
/// The popover of a guided tour: which step of how many, a way to skip the rest, the step's title and text, and Back
/// and Next (Done on the last step). It only raises events; the tour decides what each does.
/// </summary>
internal sealed class TourStep
{
    public const float Width = 340;

    private readonly Label _count;
    private readonly Label _title;
    private readonly Label _text;
    private readonly Button _back;
    private readonly Button _next;

    public TourStep(string skip, string back)
    {
        Root = Look.Panel(Art.TooltipDarkBox, "Cs2TourStep");
        Root.CustomMinimumSize = new Vector2(Width, 0);
        Root.MouseFilter = Control.MouseFilterEnum.Stop;
        var column = Ui.Add(Root, Ui.Column(8));
        var head = Ui.Add(column, Ui.Row(8));
        _count = Ui.Add(head, Ui.Label("", TextStyle.Gold, "Cs2TourCount"));
        head.AddChild(Ui.Spacer());
        var skipButton = Ui.Add(head, Ui.Button(skip, "", "Cs2TourSkip", () => Skip?.Invoke()));
        skipButton.Flat = true;
        skipButton.FocusMode = Control.FocusModeEnum.All;
        _title = Ui.Add(column, Ui.Label("", TextStyle.Heading, "Cs2TourTitle"));
        _text = Ui.Add(column, Ui.Label("", TextStyle.Body, "Cs2TourText", wrap: true));
        _text.CustomMinimumSize = new Vector2(Width - 40, 0);
        var buttons = Ui.Add(column, Ui.Row(8));
        buttons.AddChild(Ui.Spacer());
        _back = Ui.Add(buttons, Ui.Button(back, Look.Button8, "Cs2TourBack", () => Back?.Invoke()));
        _next = Ui.Add(buttons, Ui.Button("", Look.Action8, "Cs2TourNext", () => Next?.Invoke()));
        _back.FocusMode = Control.FocusModeEnum.All;
        _next.FocusMode = Control.FocusModeEnum.All;
    }

    public PanelContainer Root { get; }

    public Button NextButton => _next;

    public event Action? Next;
    public event Action? Back;
    public event Action? Skip;

    /// <summary>
    /// Shows a step: its count text ("2 of 4"), title and text, Back only when there is a step before, and Next's
    /// label.
    /// </summary>
    public void Show(string count, string title, string text, bool canGoBack, string next)
    {
        _count.Text = count;
        _title.Text = title;
        _text.Text = text;
        _back.Visible = canGoBack;
        _next.Text = next;
        Root.ResetSize();
    }
}
