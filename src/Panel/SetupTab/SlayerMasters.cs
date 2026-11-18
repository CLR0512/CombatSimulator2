using Godot;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Game.Slayer;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using MelvorMods.CombatSimulator2.Model.Slayer;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.SetupTab;

/// <summary>
/// The Slayer page's masters: every master with what the side shown gives it (blocks, a loadout per monster) and,
/// once Find best has run, its best setup ranked, with "Use on B" for that master alone. Find best runs on the ticked
/// masters by the goal chosen here and leaves side B as it was. A master's row opens its editor under it.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed class SlayerMasters
{
    private const string SlayerXp = "xp:MelvorBase:Slayer";

    // Kept for the session: the masters left out of Find best, its goal, and the master open for editing.
    private static readonly HashSet<string> _left = new(StringComparer.Ordinal);
    private static string? _metric;
    private static DeathLimit _limit = DeathLimit.None;
    private static string? _open;

    private readonly SlayerTasksNow _tasks = new();
    private readonly Label _status;
    private readonly Label _intro;
    private readonly Button _action;
    private readonly Dropdown _rankBy;
    private readonly Segmented _deaths;
    private readonly VBoxContainer _rows;
    private readonly ScrollContainer _scroll;

    // A master opened from elsewhere, scrolled to once its row is drawn.
    private static bool _scrollToOpen;

    // That master's editor, scrolled to on the next tick, once laid out.
    private Control? _scrollTo;
    private List<Metric> _metrics = [];
    private string? _goalFor;
    private SideLetter _letter;
    private SideState _side = SideState.AsNow;
    private string? _shownFor;

    // What the rows need from the game, read once per redraw: the loadouts' names by slot.
    private Dictionary<string, string> _loadoutNames = new(StringComparer.Ordinal);

    // A master as the page shows it: what the game says of it and its art.
    private sealed record Shown(MasterSpec Spec, Texture2D? Art, Texture2D? Coin)
    {
        public string Key => Ids.MasterKey(Spec.Master);
    }

    public SlayerMasters()
    {
        // The tasks and the masters scroll as one, so an open master has the page's height.
        _scroll = new ScrollContainer
        {
            Name = "Cs2SlayerScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        Root = Ui.Column(6, "Cs2SlayerPage");
        Root.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        QuietScrollBar.Apply(_scroll);
        var scrolled = ScrollFade.Wrap(_scroll);
        scrolled.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        Root.AddChild(scrolled);
        var content = Ui.Add(_scroll, Ui.Column(6, "Cs2SlayerMasters"));
        content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var head = Ui.Add(content, Ui.Row(12));
        var words = Ui.Add(head, Ui.Column(2));
        words.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        words.AddChild(Ui.Label(Tr.Get("Masters"), TextStyle.Heading));
        _intro = Ui.Clipped("", TextStyle.Small, "Cs2BestMastersIntro");
        words.AddChild(_intro);
        _action = Ui.Button("", Look.Button4, "Cs2BestMastersAction", Pressed);
        _action.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        // One goal for the page, which the tab shows on the page's subtitle row: the tasks are judged and the masters
        // ranked by it.
        var goal = Goal = Ui.Row(8, "Cs2BestMastersGoal");
        goal.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        head.AddChild(_action);

        // Translators: Label of the Slayer page's goal: what tasks are judged and masters ranked by.
        var rankLabel = Ui.Add(goal, Ui.Label(Tr.Get("Goal"), TextStyle.Label));
        rankLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        // As the results' own metric list: each metric with its icon.
        _rankBy = new Dropdown("Cs2BestMastersRankBy", Dropdown.Plain) { Ends = 3 };
        _rankBy.FixWidth(200);
        _rankBy.ItemSelected += i =>
        {
            if (i >= 0 && i < _metrics.Count)
                _metric = _metrics[i].Key;
            Refresh();
        };
        goal.AddChild(_rankBy.Root);
        var deathsLabel = Ui.Add(goal, Ui.Label(Tr.Get("Deaths"), TextStyle.Label));
        deathsLabel.TooltipText = Tr.Get("The deaths Find best allows when it picks loadouts and blocks");
        deathsLabel.MouseFilter = Control.MouseFilterEnum.Stop;
        deathsLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _deaths = new Segmented(
            "Cs2BestMastersDeaths",
            [
                ("none", Tr.Get("None"), "Cs2BestMastersDeathsNone"),
                ("five", Tr.Get("Under 5%"), "Cs2BestMastersDeathsFive"),
                ("any", Tr.Get("Any"), "Cs2BestMastersDeathsAny"),
            ],
            id =>
                _limit = id switch
                {
                    "five" => DeathLimit.UnderFivePercent,
                    "any" => DeathLimit.SurvivedOnce,
                    _ => DeathLimit.None,
                },
            side: 6,
            ends: 3
        );
        _deaths.Button("none").TooltipText = Search.SearchWords.Deaths(DeathLimit.None);
        _deaths.Button("five").TooltipText = Search.SearchWords.Deaths(DeathLimit.UnderFivePercent);
        _deaths.Button("any").TooltipText = Search.SearchWords.Deaths(DeathLimit.SurvivedOnce);
        goal.AddChild(_deaths.Root);
        _status = Ui.Label("", TextStyle.Small, "Cs2BestMastersStatus", wrap: true);
        words.AddChild(_status);

        _rows = Ui.Add(content, Ui.Column(4, "Cs2BestMastersRows"));
        // The current tasks under the masters.
        content.AddChild(_tasks.Root);
        _rows.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        // While shown, a run over the masters moves on without anything else asking the page to redraw.
        var timer = new Godot.Timer { WaitTime = 0.25, Autostart = true };
        timer.Timeout += () =>
        {
            if (!Root.IsVisibleInTree())
                return;
            if (_scrollTo is { } row && GodotObject.IsInstanceValid(row))
                _scroll.EnsureControlVisible(row);
            _scrollTo = null;
            Refresh();
        };
        Root.AddChild(timer);
    }

    public VBoxContainer Root { get; }

    /// <summary>The page's goal (metric and deaths), placed by the tab beside the page's subtitle.</summary>
    public HBoxContainer Goal { get; }

    // The page's metric; Slayer XP until the metric list is read.
    private static string GoalMetric => _metric ?? SlayerXp;

    // The side's letter, drawn gold.
    private string Letter => SideLine.Of(_letter == SideLetter.A ? "A" : "B");

    /// <summary>Opens <paramref name="master"/>'s editor (a master key) the next time the page shows.</summary>
    public static void OpenMaster(string master)
    {
        _open = master;
        _scrollToOpen = true;
    }

    /// <summary>Shows the masters for the side shown on the page.</summary>
    public void Show(SideLetter letter, SideState side)
    {
        _letter = letter;
        _side = side;
        Refresh();
    }

    private void Refresh()
    {
        FillGoal();
        _tasks.Refresh(
            GoalMetric,
            _metrics.Find(m => m.Key == GoalMetric) is { } shown ? Words.MetricName(shown, null) : GoalMetric
        );
        var service = RunService.Instance;
        var run = service.BestMasters;
        var key = string.Join(
            "|",
            _letter,
            _side.Changes.ShortForm,
            _side.IgnoreRequirements,
            PanelState.B?.Changes.ShortForm,
            run?.State,
            run?.Done.Count,
            service.BestByMaster.Count,
            service.BestGoal,
            service.IsActive,
            string.Join(",", SlayerTasksNow.Pending().Order(StringComparer.Ordinal)),
            PanelState.Look,
            _open,
            string.Join(",", _left.Order(StringComparer.Ordinal))
        );
        if (key == _shownFor)
            return;
        _shownFor = key;
        // Translators: Under the Slayer page's Masters heading; {0} is the side shown, A or B.
        SideLine.OnLabel(
            _intro,
            Tr.Format(
                "Blocks and loadouts per master on {0}, and each one's best. Click a master to change them.",
                Letter
            ),
            TextStyle.Small
        );
        Ui.Clear(_rows);
        if (LiveGame.Find() is not { } game)
            return;
        var names = PanelState.Names();
        _loadoutNames = LiveGame.Loadouts(game).ToDictionary(l => l.Slot, l => l.Name, StringComparer.Ordinal);
        var editor = new MasterEditor(
            _letter,
            _side,
            [.. LiveGame.Loadouts(game).Where(l => l.Unlocked).Select(l => l.Slot)],
            _loadoutNames,
            enemy => Art.Enemy(game, enemy),
            Start
        );
        var masters = SlayerTasks
            .Targets(game)
            .Select(t => SlayerTasks.Read(game, t.Master!, _side.Changes))
            .OfType<MasterSpec>()
            .Select(m => new Shown(
                m,
                Art.Target(game, TargetSpec.FromKey(Ids.MasterKey(m.Master))),
                Art.Currency(game, m.Facts.Currency)
            ))
            .ToList();
        var saved = masters.ToDictionary(
            m => m.Key,
            m => SlayerTasks.Read(game, m.Spec.Master, null)?.Blocked ?? [],
            StringComparer.Ordinal
        );
        var runnable = masters.Where(m => m.Spec.Unlocked || _side.IgnoreRequirements).Select(m => m.Key).ToList();
        var chosen = runnable.Count(m => !_left.Contains(m));
        var bests = service.BestByMaster.ToDictionary(b => b.Master, StringComparer.Ordinal);
        var pending = SlayerTasksNow.Pending();
        var ranked = service.BestGoal is { } bestGoal ? BestMasters.Ranked([.. bests.Values], bestGoal) : [];
        var place = 0;
        var places = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var best in ranked.Where(b => b.Figure != null))
            places[best.Master] = ++place;
        if (run is { IsActive: true })
        {
            _action.Text = Tr.Get("Cancel");
            _action.Disabled = false;
            _action.TooltipText = "";
            // The strip beside the page title says how far it is.
            _status.Text = "";
            _status.Visible = false;
        }
        else
        {
            _action.Text =
                chosen == runnable.Count
                    ? Tr.Get("Find best for every master")
                    : Tr.Plural("Find best for {0} master", "Find best for {0} masters", chosen, chosen);
            _action.Disabled = chosen == 0 || service.IsActive;
            _action.TooltipText = Tr.Get(
                "Finds each ticked master's best saved loadout per monster and best blocks, from side A. Use on B tries one."
            );
            _status.Text = run is { State: BestLoadoutsState.Failed, Error: { } error } ? Tr.Note(error) : "";
            // An empty line keeps its height: shown only with something to say.
            _status.Visible = _status.Text.Length > 0;
            Tint(_status, Look.Deadly);
        }
        // Ranked masters first, best first; then the rest in the game's order.
        var order = masters.OrderBy(m => places.TryGetValue(m.Key, out var p) ? p : int.MaxValue).ToList();
        foreach (var master in order)
        {
            var row = Row(
                master,
                bests.GetValueOrDefault(master.Key),
                places.TryGetValue(master.Key, out var p) ? p : null,
                runnable.Contains(master.Key),
                names
            );
            // Breathing while a run is working it out again, as the Gear page's skeleton does: it is about to change.
            if (pending.Contains(master.Key))
                Pending.Pulse(row, true);
            _rows.AddChild(row);
            if (_open != master.Key || !runnable.Contains(master.Key))
                continue;
            var opened = editor.Make(
                master.Spec,
                master.Coin,
                saved[master.Key],
                bests.GetValueOrDefault(master.Key),
                names
            );
            if (pending.Contains(master.Key))
                Pending.Pulse(opened, true);
            _rows.AddChild(opened);
            if (_scrollToOpen)
            {
                _scrollToOpen = false;
                _scrollTo = opened;
            }
        }
    }

    // The goal's list, remade only when the metrics on offer change: an open list stays open.
    private void FillGoal()
    {
        var metrics = RunCache.Metrics(RunService.Instance.Current).Where(m => m.Key != Metrics.MainXp).ToList();
        var listed = string.Join(",", metrics.Select(m => m.Key)) + "|" + Tr.Version + "|" + _metric;
        if (listed == _goalFor)
            return;
        _goalFor = listed;
        _metrics = metrics;
        // At first the results' own metric, main-skill XP standing as Slayer XP: masters have no main skill.
        _metric ??= PanelState.Prefs.Metric == Metrics.MainXp ? SlayerXp : PanelState.Prefs.Metric;
        if (_metrics.All(m => m.Key != _metric))
            _metric = _metrics.Any(m => m.Key == SlayerXp) ? SlayerXp : _metrics.FirstOrDefault()?.Key ?? SlayerXp;
        _rankBy.Clear();
        var run = RunService.Instance.Current;
        var game = LiveGame.Find();
        foreach (var metric in _metrics)
            _rankBy.AddItem(
                Words.MetricName(metric, null),
                game != null ? Art.Metric(game, run != null ? Metrics.Resolve(run, metric.Key) : metric.Key) : null
            );
        _rankBy.Select(Math.Max(0, _metrics.FindIndex(m => m.Key == _metric)));
        _deaths.Show(
            _limit switch
            {
                DeathLimit.UnderFivePercent => "five",
                DeathLimit.SurvivedOnce => "any",
                _ => "none",
            }
        );
    }

    private PanelContainer Row(Shown shown, BestMaster? best, int? place, bool runnable, Func<string, string> names)
    {
        var master = shown.Spec;
        var key = shown.Key;
        var brief = Ids.Brief(master.Master);
        var box = Look.Panel(Art.RowBox, Ui.Id("Cs2BestMaster", brief));
        var line = Ui.Row(10);
        box.AddChild(Ui.Margin(line, 0, 0, 6, 0));
        var tick = Cs2Check.Make(
            Ui.Id("Cs2BestMasterTick", brief),
            on: runnable && !_left.Contains(key),
            toggled: on =>
            {
                if (on)
                    _left.Remove(key);
                else
                    _left.Add(key);
                Refresh();
            }
        );
        tick.TooltipText = Tr.Get("Take this master in the next Find best");
        tick.Disabled = !runnable || RunService.Instance.IsActive;
        tick.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        line.AddChild(tick);
        var number = Ui.Label(place?.ToString(Tr.Culture) ?? "–", TextStyle.Value);
        Ui.HoldWidth(number, "8");
        line.AddChild(number);
        line.AddChild(Slot.Make(shown.Art, 40));
        var who = Ui.Add(line, Ui.Column(0));
        who.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        who.AddChild(Ui.Clipped(Tr.Format("{0}'s tasks", master.Name), TextStyle.BodyStrong));
        if (!runnable)
        {
            box.Modulate = Colors.White with { A = 0.55f };
            who.AddChild(Ui.Label("🔒 " + Tr.Notes(master.Reasons, "; "), TextStyle.Tiny, wrap: true));
            return box;
        }
        var sideText = Ui.Clipped("", TextStyle.Tiny);
        SideLine.OnLabel(sideText, SideText(master, names), TextStyle.Tiny);
        who.AddChild(sideText);
        // The best's line once Find best has found it; before, the row keeps to two lines.
        if (best != null)
            who.AddChild(MasterLines.Best(best, shown.Coin, names));
        if (best != null)
            MasterLines.Figures(line, best);
        box.MouseFilter = Control.MouseFilterEnum.Stop;
        box.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        box.TooltipText =
            _open == key ? Tr.Get("Close this master") : Tr.Get("Change this master's blocks and loadouts");
        box.GuiInput += input =>
        {
            if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                _open = _open == key ? null : key;
                Refresh();
            }
        };
        return box;
    }

    // "On B: blocks Golbin · loadouts Melee ×3": what the side shown gives the master.
    private string SideText(MasterSpec master, Func<string, string> names)
    {
        var blocks =
            master.Blocked.Count == 0
                ? Tr.Get("no blocks")
                : Tr.Format(
                    "blocks {0}",
                    string.Join(", ", master.Blocked.Select(k => names(TargetSpec.FromKey(k).Enemy ?? k)))
                );
        var pairs = _side.Changes.Find(new ChangeKey(ChangeKind.Loadouts, master.Master))?.Loadouts ?? [];
        var loadouts =
            pairs.Count == 0
                ? Tr.Get("no loadouts")
                : Tr.Format(
                    "loadouts {0}",
                    string.Join(
                        ", ",
                        pairs
                            .GroupBy(p => p.Slot)
                            .Select(g => $"{_loadoutNames.GetValueOrDefault(g.Key) ?? names(g.Key)} ×{g.Count()}")
                    )
                );
        // Translators: A master's row on the Slayer page: what the side shown gives it; {0} is A or B.
        return Tr.Format("On {0}: {1} · {2}", [Letter, blocks, loadouts]);
    }

    private void Pressed()
    {
        var service = RunService.Instance;
        if (service.BestMasters is { IsActive: true })
        {
            service.CancelBestLoadouts();
            return;
        }
        var masters = LiveGame.Find() is { } game
            ? SlayerTasks.Targets(game).Select(t => Ids.MasterKey(t.Master!)).Where(m => !_left.Contains(m)).ToList()
            : [];
        Start(masters);
    }

    // Find best on the masters given that side A can take; the message says why when it can't start.
    private void Start(IReadOnlyCollection<string> masters)
    {
        var service = RunService.Instance;
        var a = PanelState.A.ToSetup("");
        var offered = RunService.BestMastersOffered(a).Select(m => m.Master).ToHashSet(StringComparer.Ordinal);
        var prefs = PanelState.Prefs;
        try
        {
            service.StartBestMasters(
                a,
                new Goal(GoalMetric, _limit),
                new RunRequest(a, [], prefs.Trials, null, prefs.Copies ?? service.Status.DefaultCopies),
                [.. masters.Where(offered.Contains)]
            );
        }
        catch (Common.CommandException e)
        {
            _status.Text = e.Message;
            _status.Visible = true;
            Tint(_status, Look.Deadly);
        }
        _shownFor = null;
    }

    private static void Tint(Label label, Color colour)
    {
        var settings = (LabelSettings)label.LabelSettings.Duplicate();
        settings.FontColor = colour;
        label.LabelSettings = settings;
    }
}
