using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Results;

/// <summary>The results before any run: a few portraits, what to do, and buttons to simulate.</summary>
internal sealed class EmptyState
{
    private readonly VBoxContainer _empty;
    private readonly Label _emptyBody;
    private readonly HBoxContainer _emptyPortraits;
    private readonly Button _emptySelected;

    public EmptyState()
    {
        _empty = Ui.Column(14, "Cs2Empty");
        _empty.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _empty.Alignment = BoxContainer.AlignmentMode.Center;
        _emptyPortraits = Ui.Add(_empty, Ui.Row(10));
        _emptyPortraits.Alignment = BoxContainer.AlignmentMode.Center;
        var title = Ui.Label(Tr.Get("Nothing simulated yet"), TextStyle.Title);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.AddChild(title);
        _emptyBody = Ui.Label(
            Tr.Get("Pick targets on the left, or start with what you're fighting."),
            TextStyle.Body,
            wrap: true
        );
        _emptyBody.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.AddChild(_emptyBody);
        var buttons = Ui.Add(_empty, Ui.Row(10, "Buttons"));
        buttons.Alignment = BoxContainer.AlignmentMode.Center;
        buttons.AddChild(
            Ui.Button(
                Tr.Get("Simulate what I'm fighting"),
                Look.Button8,
                "Cs2EmptyFighting",
                () => SimulateFighting?.Invoke()
            )
        );
        _emptySelected = Ui.Button(
            Tr.Get("Simulate selected"),
            Look.Action8,
            "Cs2EmptySelected",
            () => SimulateSelected?.Invoke()
        );
        buttons.AddChild(_emptySelected);
    }

    public Control Root => _empty;

    public event Action? SimulateFighting;

    public event Action? SimulateSelected;

    /// <summary>
    /// Shows the empty state when <paramref name="empty"/>, with <paramref name="selected"/> on its button; portraits
    /// come from the catalog the first time it shows with a game.
    /// </summary>
    public void Show(bool empty, int selected, bool hasGame, Func<TargetSpec, Texture2D?> art)
    {
        _empty.Visible = empty;
        _emptySelected.Text = Tr.Format("Simulate {0} selected", selected);
        _emptySelected.Disabled = selected == 0 || !hasGame;
        if (empty && _emptyPortraits.GetChildCount() == 0 && hasGame && PanelState.Catalog is { } catalog)
            foreach (var target in catalog.List.Where(t => t.Spec.Enemy != null).Take(5))
                _emptyPortraits.AddChild(Ui.Picture(art(target.Spec), 56));
    }
}
