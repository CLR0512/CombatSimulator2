using Godot;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Results;

/// <summary>
/// A list row's "✕ Ignore": ignores its target as the strip and the chart do, and on an ignored row reads "Return".
/// An ignored target keeps its row, greyed, so it can be brought back where it is; a right-click runs it alone.
/// </summary>
internal static class IgnoreButton
{
    private const float Greyed = 0.25f;
    private const float PortraitDimmed = 0.4f;
    private const string CrossName = "Cs2IgnoredCross";
    private const float Quiet = 0.6f;

    /// <summary>The button for the target <paramref name="key"/> gives; its list greys it while a run goes.</summary>
    public static Button Make(Func<string?> key)
    {
        var button = Ui.Button(Tr.Get("✕ Ignore"), "", "Cs2RowIgnore");
        button.Flat = true;
        button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        // As wide either way, so the row doesn't shift when it turns.
        var font = button.GetThemeFont("font");
        var size = button.GetThemeFontSize("font_size");
        var widest = Math.Max(
            font.GetStringSize(Tr.Get("✕ Ignore"), fontSize: size).X,
            font.GetStringSize(Tr.Get("Return"), fontSize: size).X
        );
        button.CustomMinimumSize = new Vector2(
            Mathf.Ceil(widest + button.GetThemeStylebox("normal").GetMinimumSize().X),
            0
        );
        button.Modulate = Colors.White with { A = Quiet };
        button.MouseEntered += () => button.Modulate = Colors.White;
        button.MouseExited += () => button.Modulate = Colors.White with { A = Quiet };
        button.Pressed += () =>
        {
            if (key() is { } target)
                PanelState.SetIgnored([target], !PanelState.Ignored.Contains(target));
        };
        button.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
            {
                RunOnly(key());
                button.AcceptEvent();
            }
        };
        return button;
    }

    /// <summary>A right-click on a row: runs its target alone, as on the strip and the chart; not while a run goes.</summary>
    public static void RunOnly(string? key)
    {
        if (key != null && !Engine.RunService.Instance.IsActive)
            PanelState.RunOnly([key]);
    }

    /// <summary>
    /// Shows whether <paramref name="key"/> is ignored: the button's words, <paramref name="portrait"/> crossed out in
    /// red as on the strip and the chart, and every other part of its row faded, except <paramref name="keep"/> (the
    /// pin, still in use).
    /// </summary>
    public static void Show(Button button, string key, Control portrait, Control? keep = null)
    {
        var ignored = PanelState.Ignored.Contains(key);
        Cross(portrait).Visible = ignored;
        button.Text = ignored ? Tr.Get("Return") : Tr.Get("✕ Ignore");
        button.TooltipText = ignored
            ? Tr.Get("Bring this target back into the next run")
            : Tr.Get("Leave this target out of the next run · right-click to run only it");
        foreach (var part in button.GetParent().GetChildren().OfType<CanvasItem>())
            if (part != button && part != keep && part != portrait)
                part.Modulate = Colors.White with { A = ignored ? Greyed : 1f };
        // The portrait dimmed under its cross, which stays bright, as the chart draws an ignored one.
        foreach (var part in portrait.GetChildren().OfType<CanvasItem>())
            if (part.Name != CrossName)
                part.Modulate = Colors.White with { A = ignored ? PortraitDimmed : 1f };
    }

    // The cross over a portrait, made the first time it is needed and kept with the row.
    private static Control Cross(Control portrait)
    {
        if (portrait.GetNodeOrNull<Control>(CrossName) is { } made)
            return made;
        var cross = new Control { Name = CrossName, MouseFilter = Control.MouseFilterEnum.Ignore };
        cross.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        cross.Draw += () =>
        {
            var rect = new Rect2(Vector2.Zero, cross.Size).Grow(-6);
            cross.PxLine(rect.Position, rect.End, Look.Deadly, 2);
            cross.PxLine(
                new Vector2(rect.End.X, rect.Position.Y),
                new Vector2(rect.Position.X, rect.End.Y),
                Look.Deadly,
                2
            );
        };
        portrait.AddChild(cross);
        return cross;
    }
}
