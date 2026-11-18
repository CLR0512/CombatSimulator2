using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Simulate;

/// <summary>
/// The Areas list under its button: All areas, then each area the other filters leave (and any already chosen), the
/// dungeons after a line, each with its art and a tick. Drawn in CS2's window rather than as a popup window, which the
/// game draws unscaled and blurred; it scrolls past a fixed height, and a click outside it closes it.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed class AreaMenu
{
    private const float MaxHeight = 480;
    private const float Width = 300;
    private const float Art = 30;

    private readonly Control _owner;
    private readonly Control _catcher;
    private readonly PanelContainer _panel;
    private readonly ScrollContainer _scroll;
    private readonly VBoxContainer _rows;
    private readonly List<(string? Area, Button Check)> _checks = [];

    public AreaMenu(Control owner)
    {
        _owner = owner;
        // Over the window, under the list: a click anywhere outside the list closes it and goes no further.
        _catcher = new Control
        {
            Name = "Cs2AreaMenuOutside",
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
        // The game's dark tooltip panel, as CS2's hovers use: opaque, and made to stretch to any size.
        _panel = Look.Panel(Game.Art.TooltipDarkBox, "Cs2AreaMenu");
        _panel.TopLevel = true;
        _panel.Visible = false;
        _panel.ZIndex = 20;
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        // Rows and scrollbar inside the frame, and the fade over the foot while more areas are below.
        _panel.AddChild(Ui.Margin(ScrollFade.Wrap(_scroll), 6, 6, 4, 6));
        _rows = Ui.Add(_scroll, Ui.Column(4));
        _rows.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        owner.AddChild(_panel);
    }

    public bool Open => _panel.Visible;

    /// <summary>Raised with the area picked, or null for All areas.</summary>
    public event Action<string?>? Picked;

    /// <summary>Lists the areas and shows the list under <paramref name="button"/>, scrolled to its top.</summary>
    public void Show(Control button)
    {
        List();
        Overlays.Opened(this, () => Open, Close);
        var scale = _owner.GetGlobalTransform().Scale;
        _panel.Scale = scale;
        _catcher.GlobalPosition = _owner.GetGlobalRect().Position;
        _catcher.Size = _owner.GetGlobalRect().Size;
        _catcher.Visible = true;
        _panel.Visible = true;
        _panel.ResetSize();
        _scroll.CustomMinimumSize = new Vector2(Width, Math.Min(MaxHeight, _rows.GetCombinedMinimumSize().Y));
        _panel.ResetSize();
        var below = button.GetGlobalRect();
        _panel.GlobalPosition = new Vector2(below.Position.X, below.End.Y + 4).Round();
        _scroll.ScrollVertical = 0;
    }

    public void Close()
    {
        _panel.Visible = false;
        _catcher.Visible = false;
    }

    /// <summary>Ticks the areas the filter shows, without raising anything.</summary>
    public void Tick()
    {
        var areas = PanelState.Prefs.Filter.Areas;
        foreach (var (area, check) in _checks)
            Cs2Check.Set(check, area == null ? areas == null : areas?.Contains(area) == true);
    }

    private void List()
    {
        Ui.Clear(_rows);
        _checks.Clear();
        Row(null, Tr.Get("All areas"), null);
        if (PanelState.Catalog is { } catalog)
        {
            var filter = PanelState.Prefs.Filter;
            var chosen = filter.Areas ?? [];
            var ignore = PanelState.A.IgnoreRequirements;
            var facts = catalog.List.Select(t => t.Fact(ignore)).ToList();
            var left = TargetSelection
                .Of(facts, filter with { Areas = null, Search = "" }, PanelState.Ignored)
                .Shown.Select(t => t.Area)
                .ToHashSet(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var dungeon = false;
            var game = LiveGame.Find();
            // Masters have no area to pick: their own filter shows them.
            foreach (
                var target in facts
                    .Where(t => !t.Tags.HasFlag(TargetTags.Masters))
                    .OrderBy(t => t.Tags.HasFlag(TargetTags.Dungeons))
            )
            {
                if ((!left.Contains(target.Area) && !chosen.Contains(target.Area)) || !seen.Add(target.Area))
                    continue;
                if (target.Tags.HasFlag(TargetTags.Dungeons) && !dungeon)
                {
                    dungeon = true;
                    var line = Ui.Label(Tr.Get("Dungeons"), TextStyle.Tiny);
                    line.HorizontalAlignment = HorizontalAlignment.Center;
                    _rows.AddChild(line);
                }
                Row(target.Area, target.AreaName, game != null ? Game.Art.Area(game, target.Area) : null);
            }
        }
        Tick();
    }

    // A row: the tick, the area's art in the game's slot, and its name; a click anywhere on it picks it.
    private void Row(string? area, string name, Texture2D? art)
    {
        var row = Ui.Add(_rows, Ui.Row(8));
        row.MouseFilter = Control.MouseFilterEnum.Stop;
        row.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        row.CustomMinimumSize = new Vector2(0, Art + 6);
        var check = Cs2Check.Make(Ui.Id("Cs2AreaPick", area ?? "all"), "", false, _ => Picked?.Invoke(area));
        check.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(check);
        if (art != null)
            row.AddChild(Slot.Make(art, Art));
        else
            row.AddChild(Ui.Spacer(false, area == null ? 0 : Art));
        var label = Ui.Label(name, TextStyle.Body);
        label.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(label);
        row.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
                Picked?.Invoke(area);
        };
        _checks.Add((area, check));
    }
}
