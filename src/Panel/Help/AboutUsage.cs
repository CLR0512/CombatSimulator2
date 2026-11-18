using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Help;

/// <summary>
/// The About window's "Stats": time with the window open and what was simulated, this game launch and in
/// all, kept current while it shows.
/// </summary>
internal sealed class AboutUsage
{
    private readonly Label _sessionTime;
    private readonly Label _allTime;
    private readonly Label[] _counts = new Label[6];
    private long _shownSecond = -1;
    private long _shownCounted = -1;

    public VBoxContainer Root { get; }

    public AboutUsage()
    {
        var section = Root = Ui.Column(8, "Cs2AboutTime");
        section.AddChild(AboutParts.Heading(Tr.Get("Stats")));
        var box = Ui.Add(section, Look.Panel(Art.RowBox, "Cs2AboutTracker"));
        var column = Ui.Column(10);
        box.AddChild(Ui.Margin(column, 10, 8, 10, 8));

        var times = Ui.Add(column, Ui.Row(12));
        _sessionTime = Clock(times, Tr.Get("This session"), "Cs2AboutSessionTime");
        _allTime = Clock(times, Tr.Get("All time"), "Cs2AboutAllTime");
        column.AddChild(
            new ColorRect
            {
                Color = new Color(1, 1, 1, 0.08f),
                CustomMinimumSize = new Vector2(0, 1),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            }
        );

        var grid = Ui.Add(column, new GridContainer { Columns = 3, Name = "Cs2AboutCounts" });
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 4);
        grid.AddChild(Ui.Spacer());
        grid.AddChild(Number(Tr.Get("Session"), TextStyle.Tiny, ""));
        grid.AddChild(Number(Tr.Get("All time"), TextStyle.Tiny, ""));
        string[] rows = [Tr.Get("Runs"), Tr.Get("Fights simulated"), Tr.Get("Comparisons")];
        string[] names = ["Runs", "Fights", "Comparisons"];
        for (var i = 0; i < rows.Length; i++)
        {
            var name = Ui.Add(grid, Ui.Label(rows[i], TextStyle.Small));
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _counts[i * 2] = Ui.Add(grid, Number("", TextStyle.Label, $"Cs2AboutSession{names[i]}"));
            _counts[i * 2 + 1] = Ui.Add(grid, Number("", TextStyle.Label, $"Cs2AboutAll{names[i]}"));
        }

        var first = Usage.Stats.FirstOpened ?? DateTime.UtcNow;
        column.AddChild(
            Ui.Label(
                // Translators: {0} is a date, such as "6 Nov 2026".
                Tr.Format(
                    "First opened {0}. Counts while CS2 is open.",
                    first.ToLocalTime().ToString("d MMM yyyy", Tr.Culture)
                ),
                TextStyle.Tiny,
                "Cs2AboutFirstOpened",
                wrap: true
            )
        );
        Show();
    }

    private static Label Clock(HBoxContainer times, string title, string name)
    {
        var block = Ui.Add(times, Ui.Column(0));
        block.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        block.AddChild(Ui.Label(title, TextStyle.Label));
        return Ui.Add(block, Ui.Label("", TextStyle.Value, name));
    }

    private static Label Number(string text, TextStyle style, string name)
    {
        var label = Ui.Label(text, style, name);
        label.HorizontalAlignment = HorizontalAlignment.Right;
        label.CustomMinimumSize = new Vector2(70, 0);
        return label;
    }

    /// <summary>Runs once a frame while About shows: the times and counts as they are now.</summary>
    public void OnFrame()
    {
        var second = (long)Usage.Stats.Session.Seconds;
        if (second == _shownSecond && Counted == _shownCounted)
            return;
        Show();
    }

    // Everything counted so far: it only grows, so a change in it is a change in some count.
    private static long Counted =>
        Usage.Stats.AllTime.Runs + Usage.Stats.AllTime.Fights + Usage.Stats.AllTime.Comparisons;

    private void Show()
    {
        var stats = Usage.Stats;
        _shownSecond = (long)stats.Session.Seconds;
        _shownCounted = Counted;
        _sessionTime.Text = Duration(stats.Session.Seconds);
        _allTime.Text = Duration(stats.AllTime.Seconds);
        long[] counts =
        [
            stats.Session.Runs,
            stats.AllTime.Runs,
            stats.Session.Fights,
            stats.AllTime.Fights,
            stats.Session.Comparisons,
            stats.AllTime.Comparisons,
        ];
        for (var i = 0; i < counts.Length; i++)
            _counts[i].Text = Ui.Number(counts[i]);
    }

    /// <summary>A time spent, short: 40s, 42m, 31h 12m.</summary>
    public static string Duration(double seconds)
    {
        var minutes = (long)(seconds / 60);
        if (minutes < 1)
            // Translators: A time in seconds, as short as it can be said: "40s".
            return Tr.Format("{0}s", (long)seconds);
        if (minutes < 60)
            // Translators: A time in minutes, as short as it can be said: "42m".
            return Tr.Format("{0}m", minutes);
        // Translators: A time in hours and minutes, as short as it can be said: "31h 12m".
        return Tr.Format("{0}h {1}m", [minutes / 60, minutes % 60]);
    }
}
