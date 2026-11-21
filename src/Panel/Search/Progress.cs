using System.Diagnostics;
using Godot;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Search;

/// <summary>
/// A search under way: its goal, then the stage under way, one bar for the whole search, the time and Cancel, all kept in place, and
/// below them the stages that have ended, the latest first, so the bar never moves as they come in.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed class Progress
{
    private readonly Label _goal;

    // A setup search's best confirmed set so far, which Cancel keeps: its gain, then what it changes as icons.
    private readonly HBoxContainer _best;
    private readonly Label _bestGain;
    private readonly HFlowContainer _bestIcons;
    private ChangeList? _bestShown;

    // The stage under way in three cells of fixed width, so a number growing a digit moves nothing after it.
    private readonly Label _now;
    private readonly Label _trials;
    private readonly Label _sets;
    private readonly Control _bar;
    private readonly VBoxContainer _stages;
    private readonly Label _time;
    private readonly Stopwatch _clock = new();
    private long _tenthsShown = -1;
    private readonly Godot.Timer _beat;
    private object? _for;
    private int _lines = -1;
    private readonly Control _hoverOwner;
    private readonly HoverPanel _perfHover;
    private Vector2 _perfMouse;
    private (PerfHistory? History, int Count) _perfShown;

    public Progress(Control hoverOwner)
    {
        Root = Ui.Column(10, "GearSearchProgress");
        // Translators: Heading of a gear search under way.
        Root.AddChild(Ui.Label(Tr.Get("Simulating"), TextStyle.Heading));
        _goal = Ui.Add(Root, Ui.Label("", TextStyle.Small, wrap: true));
        var now = Ui.Add(Root, Ui.Column(4, "GearSearchNow"));
        var cells = Ui.Add(now, Ui.Row(12, "GearSearchStage"));
        _now = Ui.Add(cells, Cell(84));
        _trials = Ui.Add(cells, Cell(104));
        _sets = Ui.Add(cells, Cell(0));
        _sets.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _bar = Ui.Add(now, Bar.Make("GearSearchBar"));
        // Hovering the bar shows the search's performance, as the run's bar shows a run's.
        _hoverOwner = hoverOwner;
        _perfHover = new HoverPanel(hoverOwner);
        _bar.MouseFilter = Control.MouseFilterEnum.Pass;
        _bar.GuiInput += e =>
        {
            if (e is not InputEventMouseMotion motion)
                return;
            _perfMouse = _bar.GetGlobalTransform() * motion.Position;
            ShowPerfHover(force: !_perfHover.Visible);
        };
        _bar.MouseExited += () => _perfHover.Hide();
        var foot = Ui.Add(Root, Ui.Row(8));
        _time = Ui.Add(foot, Ui.Label("", TextStyle.Small, "GearSearchTime"));
        _time.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _time.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _time.LabelSettings = Look.EvenDigits(TextStyle.Small);
        // The time moves on its own beat, steadier than the search's updates: on theirs it would step unevenly and
        // look to run fast and slow by turns. It beats only while a search is shown, so a closed panel costs nothing;
        // a timer node is freed with the view, so nothing outlives it.
        _beat = new Godot.Timer { WaitTime = 0.05 };
        _beat.Timeout += ShowTime;
        _time.AddChild(_beat);
        foot.AddChild(
            SearchWords.Button(
                Tr.Get("Cancel"),
                "GearSearchCancel",
                () =>
                {
                    if (RunService.Instance.Search is { IsActive: true })
                        RunService.Instance.CancelSearch();
                }
            )
        );
        // Under the bar and Cancel, on one line that is always there, so a new find moves nothing above it.
        _best = Ui.Add(Root, Ui.Row(6, "GearSearchBestSoFar"));
        _best.CustomMinimumSize = new Vector2(0, 28);
        _bestGain = Ui.Add(_best, Ui.Label("", TextStyle.Small));
        _bestGain.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        // The icons wrap within the panel's width; a second line only moves the stages below, never the bar above.
        _bestIcons = Ui.Add(_best, new HFlowContainer { Name = "GearSearchBestIcons" });
        _bestIcons.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _bestIcons.AddThemeConstantOverride("h_separation", 2);
        _bestIcons.AddThemeConstantOverride("v_separation", 2);
        _stages = Ui.Add(Root, Ui.Column(6, "GearSearchStages"));
    }

    public VBoxContainer Root { get; }

    /// <summary>Shows the active search as it stands.</summary>
    public void Show()
    {
        if (RunService.Instance.Search is not { } search)
            return;
        if (!ReferenceEquals(_for, search))
        {
            _for = search;
            _bestShown = null;
            _lines = -1;
            _tenthsShown = -1;
            _clock.Restart();
        }
        _goal.Text = SearchWords.Goal(search.Request.Goal);
        _best.Visible = search.Request.Kinds.Count > 0;
        if (_best.Visible)
            ShowBest();
        // A search a slot at a time races a round per slot it improves: its lines are steps, each ending at its last
        // stage, and the stages are counted over all of them.
        var climb = search.Plan?.Climb == true;
        if (search.Stage > search.Stages.Count && search.StageTrials > 0)
        {
            _now.Text = climb
                // Translators: A gear search a slot at a time: the step under way.
                ? Tr.Format("Step {0}", SearchProgress.Step(true, search.Stages.Count, search.Stage))
                // Translators: A gear search's stage under way.
                : Tr.Format("Stage {0}", search.Stage);
            // Translators: A gear search's stage under way: how many trials each set fights in it.
            _trials.Text = Tr.Format("{0} trials", Ui.Number(search.StageTrials));
            // Translators: A gear search's stage under way: {0} sets fought of {1}.
            _sets.Text = Tr.Format("{0} of {1} sets", [search.StageFought, search.Left]);
        }
        else if (search.Stage == 0)
        {
            // Translators: A gear search reading what the character owns before its first fights.
            _now.Text = Tr.Get("Reading your gear…");
            _trials.Text = "";
            _sets.Text = "";
        }
        // One bar for the whole search, steps and stages alike: it only moves on.
        Bar.Set(_bar, search.Progress, Look.Running);
        if (_perfHover.Visible)
            ShowPerfHover(force: false);
        if (_lines != search.Stages.Count)
        {
            _lines = search.Stages.Count;
            Ui.Clear(_stages);
            for (var i = search.Stages.Count - 1; i >= 0; i--)
                _stages.AddChild(Line(i + 1, search.Stages[i], climb));
        }
        if (_beat.IsInsideTree() && _beat.IsStopped())
            _beat.Start();
        ShowTime();
    }

    // "Best so far: +6.3%" and an icon for each thing it changes, its name on hover; drawn again only on a new find.
    private void ShowBest()
    {
        var best = RunService.Instance.BestSoFar();
        _bestGain.Text = best switch
        {
            // Translators: While a setup search runs, before its first step has finished.
            null => Tr.Get("Best so far: none yet"),
            // Translators: While a setup search runs, when nothing found so far beats the setup as it is.
            { Changes.IsEmpty: true } => Tr.Get("Best so far: keep what you have"),
            // Translators: While a setup search runs: {0} its gain so far; icons of what it changes follow.
            { } found => Tr.Format("Best so far: {0}", SearchWords.Gain(found.Percent, null)),
        };
        var changes = best?.Changes ?? ChangeList.Empty;
        if (changes.Equals(_bestShown))
            return;
        _bestShown = changes;
        Ui.Clear(_bestIcons);
        var names = PanelState.Names();
        foreach (var change in changes.Where(c => c.Value != Change.Off && c.Kind != ChangeKind.Stance))
        {
            var id = change.Value == Change.On ? change.Subject! : change.Value;
            if (id == Change.None)
                continue;
            var icon = Ui.Add(_bestIcons, Slot.Make(Game.ChangeOptions.Icon(id), 24));
            icon.MouseFilter = Control.MouseFilterEnum.Stop;
            icon.TooltipText = names(id);
        }
    }

    // Tenths of a second under a minute, "7.3 s"; minutes and whole seconds after, "2m 05s".
    private void ShowTime()
    {
        if (!Root.IsVisibleInTree() || RunService.Instance.Search is not { IsActive: true })
        {
            _beat.Stop();
            return;
        }
        var tenths = (long)(_clock.Elapsed.TotalSeconds * 10);
        if (tenths == _tenthsShown)
            return;
        _tenthsShown = tenths;
        var seconds = tenths / 10;
        var spent =
            seconds < 60
                ? Words.Seconds((tenths / 10.0).ToString("0.0", Tr.Culture))
                // Translators: A gear search's time so far, in minutes and seconds: "2m 05s".
                : Tr.Format("{0}m {1}s", [seconds / 60, (seconds % 60).ToString("00", Tr.Culture)]);
        var search = RunService.Instance.Search;
        var left = SearchWords.TimeLeft(search?.SecondsLeft, search?.LeftIsLimit == true);
        _time.Text = left.Length == 0 ? spent : $"{spent} · {left}";
    }

    // A cell at least minWidth wide, its digits all one width where the font has such digits.
    private static Label Cell(float minWidth)
    {
        var cell = Ui.Label("", TextStyle.Body);
        cell.CustomMinimumSize = new Vector2(minWidth, 0);
        cell.LabelSettings = Look.EvenDigits(TextStyle.Body);
        return cell;
    }

    /// <summary>The stage and set counts, for <c>cs2 ui</c>.</summary>
    public static string Describe() =>
        RunService.Instance.Search is { } search
            ? $"stage={search.Stage} fought={search.StageFought} left={search.Left}"
            : "none";

    private static Label Line(int number, StageSummary summary, bool climb) =>
        Ui.Label(
            climb
                // Translators: A step of a gear search a slot at a time that has ended: {0} the step, {1} sets fought, {2} dropped.
                ? Tr.Format("Step {0} · {1} fought · {2} dropped", [number, summary.Fought, summary.Dropped])
                // Translators: A gear search's stage that has ended: {0} the stage, {1} trials per set, {2} sets fought, {3} dropped, {4} left.
                : Tr.Format(
                    "Stage {0} · {1} trials · {2} fought · {3} dropped · {4} left",
                    [number, Ui.Number(summary.Trials), summary.Fought, summary.Dropped, summary.Left]
                ),
            TextStyle.Small
        );

    // The performance hover, rebuilt only when a new sample arrives or it was hidden.
    private void ShowPerfHover(bool force)
    {
        var status = RunService.Instance.Status;
        var history = status.SearchPerf ?? status.LastSearchPerf;
        var shown = (history, history?.Samples.Count ?? 0);
        if (force || shown != _perfShown)
        {
            _perfShown = shown;
            _perfHover.Reset();
            Results.PerfView.FillSearchHover(_perfHover.Content, status);
        }
        _perfHover.ShowAt(_perfMouse, _hoverOwner.GetGlobalRect());
    }
}
