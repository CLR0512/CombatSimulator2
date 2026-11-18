using Godot;
using MelvorMods.CombatSimulator2.Game;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>
/// A tooltip CS2 places itself, in the game's tooltip style: for drawn things (chart bars) and rich content.
/// It sits on top of the modal, near the cursor, kept inside the given rect; a long line wraps.
/// </summary>
internal sealed class HoverPanel
{
    private readonly PanelContainer _panel;
    private readonly VBoxContainer _content;

    private readonly Control _owner;

    public HoverPanel(Control owner)
    {
        _owner = owner;
        _panel = Look.Panel(Art.TooltipDarkBox, "Cs2Hover");
        _panel.TopLevel = true;
        _panel.Visible = false;
        _panel.MouseFilter = Control.MouseFilterEnum.Ignore;
        // Over the popups (menu, dialogs and lists, at 20), whose own controls have tooltips too.
        _panel.ZIndex = 25;
        _content = Ui.Add(_panel, Ui.Column(4));
        _content.MouseFilter = Control.MouseFilterEnum.Ignore;
        owner.AddChild(_panel);
    }

    // Its node goes with the window it was drawn over (leaving to the main menu frees the game's screen): a panel
    // whose node is gone is hidden, and hiding it again does nothing.
    public bool Visible => GodotObject.IsInstanceValid(_panel) && _panel.Visible;

    public VBoxContainer Content => _content;

    /// <summary>Clears the content; the caller fills <see cref="Content"/> and calls <see cref="ShowAt"/>.</summary>
    public void Reset() => Ui.Clear(_content);

    public void ShowAt(Vector2 globalMouse, Rect2 bounds)
    {
        WrapLong(_content);
        // A top-level panel skips its owner's transform: it takes the window's size factor itself.
        var scale = _owner.GetGlobalTransform().Scale;
        _panel.Scale = scale;
        _panel.Visible = true;
        _panel.ResetSize();
        var size = _panel.GetCombinedMinimumSize() * scale;
        var position = globalMouse + new Vector2(18, 18);
        if (position.X + size.X > bounds.End.X)
            position.X = globalMouse.X - size.X - 12;
        if (position.Y + size.Y > bounds.End.Y)
            position.Y = globalMouse.Y - size.Y - 12;
        position.X = Math.Clamp(position.X, bounds.Position.X, Math.Max(bounds.Position.X, bounds.End.X - size.X));
        position.Y = Math.Clamp(position.Y, bounds.Position.Y, Math.Max(bounds.Position.Y, bounds.End.Y - size.Y));
        _panel.GlobalPosition = position.Round();
    }

    public void Hide()
    {
        if (GodotObject.IsInstanceValid(_panel))
            _panel.Visible = false;
    }

    private const float MaxLineWidth = 440;

    // A line longer than the panel's width wraps, so a long list (the areas a stub stands for) stays a readable block.
    private static void WrapLong(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is Label { AutowrapMode: TextServer.AutowrapMode.Off } label)
            {
                if (label.GetMinimumSize().X > MaxLineWidth)
                {
                    label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                    label.CustomMinimumSize = new Vector2(MaxLineWidth, 0);
                    // A wrapped label's height follows its width, which layout would only give it next frame: the
                    // panel is sized now.
                    label.Size = new Vector2(MaxLineWidth, 0);
                    var lines = label.GetLineCount();
                    var spacing = label.LabelSettings?.LineSpacing ?? label.GetThemeConstant("line_spacing");
                    label.CustomMinimumSize = new Vector2(
                        MaxLineWidth,
                        lines * label.GetLineHeight() + (lines - 1) * spacing
                    );
                }
            }
            else
                WrapLong(child);
        }
    }
}
