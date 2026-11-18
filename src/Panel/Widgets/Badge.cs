using Godot;

namespace MelvorMods.CombatSimulator2.Panel.Widgets;

/// <summary>The deaths badge: a status border and text on the status fill.</summary>
internal static class Badge
{
    public static PanelContainer Make(string name = "")
    {
        var panel = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Pass,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        if (name.Length > 0)
            panel.Name = name;
        panel.AddChild(Ui.Label("", TextStyle.Label));
        Set(panel, null);
        return panel;
    }

    /// <summary>Shows a death rate ("0% deaths"); null hides the badge's fill and shows a dash.</summary>
    public static void Set(PanelContainer badge, double? deathRate)
    {
        var label = (Label)badge.GetChild(0);
        var box = new StyleBoxFlat
        {
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
            ContentMarginTop = 2,
            ContentMarginBottom = 2,
            BorderWidthBottom = 1,
            BorderWidthTop = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
        };
        if (deathRate is { } rate)
        {
            box.BgColor = Look.StatusFill(rate);
            box.BorderColor = Look.Status(rate);
            label.Text = Tr.Format("{0} deaths", Ui.Percent(rate));
            label.LabelSettings = StatusText(rate);
            // Translators: Hover of the deaths badge on a results row ("0% deaths").
            badge.TooltipText = Tr.Get("Share of trials that ended in your death.");
        }
        else
        {
            box.DrawCenter = false;
            box.BorderColor = Colors.Transparent;
            label.Text = "–";
            badge.TooltipText = "";
            label.LabelSettings = Look.Style(TextStyle.Label);
        }
        PixelBox.Panel(badge, box);
    }

    /// <summary>
    /// Shows a master's deaths over its time ("1 in 5.2 h", "no deaths"): a session ends at its first death, so the
    /// time between deaths is what matters, not the share of fights.
    /// </summary>
    public static void SetPerHour(PanelContainer badge, double deathsPerHour)
    {
        // Coloured on the per-fight scale: none is green, one an hour or more is the red of 5% and over.
        Set(
            badge,
            deathsPerHour <= 0 ? 0
                : deathsPerHour >= 1 ? 0.05
                : 0.01
        );
        var label = (Label)badge.GetChild(0);
        label.Text =
            deathsPerHour <= 0 ? Tr.Get("no deaths")
            : deathsPerHour >= 1 ? Tr.Format("1 in {0} min", Ui.Number(60 / deathsPerHour))
            : Tr.Format("1 in {0} h", Ui.Number(1 / deathsPerHour));
        // Translators: Hover of a Slayer master's deaths badge on a results row ("1 in 5.2 h").
        badge.TooltipText = Tr.Get("How long its tasks go between deaths. The game stops at a death.");
    }

    private static readonly Dictionary<Color, LabelSettings> _status = [];

    /// <summary>Disposes the label settings made here when the game quits (they hold CS2's fonts).</summary>
    public static void Release()
    {
        foreach (var settings in _status.Values)
            settings.Dispose();
        _status.Clear();
    }

    private static LabelSettings StatusText(double rate)
    {
        var colour = Look.Status(rate);
        if (!_status.TryGetValue(colour, out var settings))
        {
            settings = (LabelSettings)Look.Style(TextStyle.Label).Duplicate();
            Look.MarkSkipped(settings);
            // A light tint of the status on its dark fill: the status colour itself on its fill is hard to read.
            settings.FontColor = colour.Lerp(Colors.White, 0.75f);
            _status[colour] = settings;
        }
        settings.FontSize = Look.Style(TextStyle.Label).FontSize;
        return settings;
    }

    public static void Clear() => _status.Clear();
}
