using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Panel.Settings;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Simulate;

/// <summary>
/// The ☰ menu: the controls a run needs now and then but not on every look. Share copies the Simulate page as one
/// line or pastes one; How runs fight holds the same settings as the Settings tab, read again each time it opens;
/// Window holds the scale slider; Help and reports saves a bug report or goes to the history. Drawn in CS2's window under its button, as the Areas
/// list is; a click outside it or Esc closes it.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed class PanelMenu
{
    private const float Width = 400;
    private const float MaxHeight = 620;

    private readonly Control _owner;
    private readonly Control _catcher;
    private readonly PanelContainer _panel;
    private readonly ScrollContainer _scroll;
    private readonly VBoxContainer _column;
    private readonly RunSettings _settings;
    private readonly Button _copy;

    public PanelMenu(Control owner)
    {
        _owner = owner;
        _catcher = new Control
        {
            Name = "Cs2MenuOutside",
            TopLevel = true,
            Visible = false,
            ZIndex = 19,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _catcher.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true })
                Close();
        };
        owner.AddChild(_catcher);
        _panel = Look.Panel(Art.TooltipDarkBox, "Cs2Menu");
        _panel.TopLevel = true;
        _panel.Visible = false;
        _panel.ZIndex = 20;
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _panel.AddChild(Ui.Margin(ScrollFade.Wrap(_scroll), 10, 10, 8, 10));
        _column = Ui.Add(_scroll, Ui.Column(10));
        _column.CustomMinimumSize = new Vector2(Width, 0);
        _column.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        var head = Ui.Add(_column, Ui.Row(8));
        var title = Ui.Add(head, Ui.Label(Tr.Get("Menu"), TextStyle.Heading));
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(Ui.CloseButton("Cs2MenuClose", Close));

        Section(Tr.Get("Share"));
        var share = Ui.Add(_column, Ui.Row(8));
        _copy = Ui.Button(Tr.Get("Copy simulation"), Look.Button8, "Cs2MenuCopySim", Copy);
        share.AddChild(_copy);
        share.AddChild(Ui.Button(Tr.Get("Paste…"), Look.Button8, "Cs2MenuPasteSim", Paste));
        _column.AddChild(
            Ui.Label(
                Tr.Get("Both sides, the targets left out and the settings below, as one line."),
                TextStyle.Tiny,
                wrap: true
            )
        );

        Section(Tr.Get("How runs fight"));
        var settings = Ui.Add(_column, Ui.Column(10));
        _settings = new RunSettings(settings, "Cs2Menu", help: false);

        Section(Tr.Get("Window"));
        var window = Ui.Add(_column, Ui.Column(10));
        SettingRow.Setting(window, Tr.Get("Scale"), "", ScaleSlider.Make());

        Section(Tr.Get("Help and reports"));
        var help = Ui.Add(_column, Ui.Row(8));
        help.AddChild(
            Ui.Button(
                Tr.Get("Save bug report…"),
                Look.Button8,
                "Cs2MenuReport",
                () =>
                {
                    Close();
                    Report.ReportNote.Save();
                }
            )
        );
        help.AddChild(
            Ui.Button(
                Tr.Get("History"),
                Look.Button8,
                "Cs2MenuHistory",
                () =>
                {
                    Close();
                    Cs2Modal.Jump(Cs2Modal.History);
                }
            )
        );
        owner.AddChild(_panel);
    }

    /// <summary>
    /// The ☰ that opens a menu drawn over <paramref name="owner"/>: a bare white sign on the title's ribbon, no frame.
    /// </summary>
    public static Button Button(Control owner)
    {
        var button = Ui.Button("☰", "", "Cs2MenuButton");
        button.TooltipText = Tr.Get("Menu: share this simulation, how runs fight, bug reports and history");
        foreach (var box in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" })
            button.AddThemeStyleboxOverride(box, new StyleBoxEmpty());
        // White on the blue ribbon, so it reads at a glance; pressing dims it a little.
        foreach (var state in new[] { "font_color", "font_focus_color", "font_hover_color" })
            button.AddThemeColorOverride(state, Colors.White);
        foreach (var state in new[] { "font_pressed_color", "font_hover_pressed_color" })
            button.AddThemeColorOverride(state, Colors.White with { A = 0.75f });
        button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        // Built on the first press, not with the window, so opening CS2 doesn't pay for it.
        PanelMenu? menu = null;
        button.Pressed += () => (menu ??= new PanelMenu(owner)).Show(button);
        return button;
    }

    public bool Open => _panel.Visible;

    /// <summary>Raised once a pasted simulation is used, so the page can say so and offer Undo.</summary>
    public event Action? SimulationUsed;

    /// <summary>Shows the menu under <paramref name="button"/>, the settings read again.</summary>
    public void Show(Control button)
    {
        _settings.Show();
        Overlays.Opened(this, () => Open, Close);
        _panel.Scale = _owner.GetGlobalTransform().Scale;
        _catcher.GlobalPosition = _owner.GetGlobalRect().Position;
        _catcher.Size = _owner.GetGlobalRect().Size;
        _catcher.Visible = true;
        _panel.Visible = true;
        var below = button.GetGlobalRect();
        _panel.GlobalPosition = new Vector2(below.Position.X, below.End.Y + 4).Round();
        _scroll.ScrollVertical = 0;
        Fit(below);
        // Wrapped lines know their height once laid out at the menu's width: fitted again a frame later.
        Callable.From(() => Fit(below)).CallDeferred();
    }

    // As tall as its contents, up to what the window leaves below the button; past that, the menu scrolls.
    private void Fit(Rect2 below)
    {
        if (!_panel.Visible)
            return;
        var room = _owner.GetGlobalRect().End.Y - below.End.Y - 12;
        var scale = Math.Max(0.01f, _panel.Scale.Y);
        // The column's least height, not its size: the scroll stretches it to the height asked for before.
        _scroll.CustomMinimumSize = Vector2.Zero;
        var height = _column.GetCombinedMinimumSize().Y;
        _scroll.CustomMinimumSize = new Vector2(0, Math.Min(Math.Min(MaxHeight, room / scale), height));
        _panel.ResetSize();
    }

    public void Close()
    {
        _panel.Visible = false;
        _catcher.Visible = false;
    }

    private void Section(string title)
    {
        var label = Ui.Add(_column, Ui.Label(title, TextStyle.Small));
        label.Modulate = Colors.White with { A = 0.7f };
    }

    private void Copy() => FloatNote.Show(_copy, Sharing.ExportSimulation() ?? Sharing.Exported);

    private void Paste()
    {
        Close();
        PasteDialog.Open(_owner, () => SimulationUsed?.Invoke());
    }
}
