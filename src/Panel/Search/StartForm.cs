using Godot;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Search;

/// <summary>
/// What a search is asked: the metric to rank by, the death limit as three buttons, whether to try other styles, the
/// gear with its locks and the items never to use; then what it would fight, counted from the plan before anything
/// starts, how long that is expected to take, and the button that starts it.
/// </summary>
internal sealed class StartForm
{
    private readonly Dropdown _rankBy;
    private readonly Segmented _deaths;
    private readonly Button _styles;
    private readonly StartGear _gear;
    private readonly KindSwitches _switches;
    private readonly GridContainer _counts;
    private readonly RichTextLabel _more;
    private readonly Label _note;
    private readonly Button _find;
    private readonly Action<SearchRequest> _start;
    private List<Metric> _metrics = [];
    private string _metric = Metrics.MainXp;
    private DeathLimit _limit = DeathLimit.None;
    private bool _allStyles;
    private SearchKind _kind = SearchKind.BestGear;
    private string? _target;
    private int _version;
    private PlanPreview? _preview;

    // The counts last shown, kept while they are counted again so that only their numbers fade.
    private PlanPreview? _shown;
    private bool _counting;
    private (string Target, SearchKind Kind, object? Check, string? Note, int Language)? _stamp;

    /// <summary>
    /// The form; <paramref name="pickItem"/> opens the item picker under a control; <paramref name="start"/> gets
    /// the request when the player starts the search.
    /// </summary>
    public StartForm(Action<Control, Action<string>> pickItem, Action<SearchRequest> start)
    {
        _start = start;
        _switches = new KindSwitches(Changed);
        Root = Ui.Column(12, "GearSearchStart");

        var rank = Ui.Add(Root, Ui.Row(8));
        var rankLabel = Ui.Add(rank, Ui.Label(Tr.Get("Rank by"), TextStyle.Label));
        rankLabel.CustomMinimumSize = new Vector2(76, 0);
        rankLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        // The bar and its list framed as the tab's buttons are.
        _rankBy = new Dropdown("GearSearchRankBy", "") { RightAligned = true, ListBox = Look.FramedListBox };
        FramedButton.Make(_rankBy.Root, 10, 3);
        _rankBy.FixWidth(200);
        _rankBy.ItemSelected += i =>
        {
            if (i >= 0 && i < _metrics.Count)
            {
                _metric = _metrics[i].Key;
                Changed();
            }
        };
        rank.AddChild(_rankBy.Root);

        var deaths = Ui.Add(Root, Ui.Row(8));
        // Translators: Label of the death limit's three buttons in the gear search's form.
        var deathsLabel = Ui.Add(deaths, Ui.Label(Tr.Get("Deaths"), TextStyle.Label));
        deathsLabel.CustomMinimumSize = new Vector2(76, 0);
        deathsLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        // Short words on the buttons, to fit the panel; each says the limit in full on hover.
        _deaths = new Segmented(
            "GearSearchDeaths",
            [
                ("none", Tr.Get("None"), "GearSearchDeathsNone"),
                // Translators: Short button of the gear search's death limit: fewer than 5% of trials die.
                ("five", Tr.Get("Under 5%"), "GearSearchDeathsFive"),
                // Translators: Short button of the gear search's death limit: any deaths, if one trial was survived.
                ("any", Tr.Get("Any"), "GearSearchDeathsAny"),
            ],
            id =>
            {
                _limit = id switch
                {
                    "five" => DeathLimit.UnderFivePercent,
                    "any" => DeathLimit.SurvivedOnce,
                    _ => DeathLimit.None,
                };
                Changed();
            },
            side: 6,
            ends: 3,
            framed: true
        );
        _deaths.Button("none").TooltipText = SearchWords.Deaths(DeathLimit.None);
        _deaths.Button("five").TooltipText = SearchWords.Deaths(DeathLimit.UnderFivePercent);
        _deaths.Button("any").TooltipText = SearchWords.Deaths(DeathLimit.SurvivedOnce);
        deaths.AddChild(_deaths.Root);

        _styles = Cs2Check.Make(
            "GearSearchStyles",
            // Translators: Tick in the gear search's form: also try the other combat styles' weapons.
            Tr.Get("Include other styles"),
            false,
            on =>
            {
                _allStyles = on;
                Changed();
            }
        );
        _styles.TooltipText = Tr.Get(
            "Also search with a weapon of each other combat style, and show each style's best."
        );
        Root.AddChild(_styles);
        foreach (var control in _switches.Controls)
            Root.AddChild(control);

        _gear = new StartGear(pickItem, Changed);
        Root.AddChild(_gear.Root);

        // The counts and the button stay in view at the details' foot, however long the form above them.
        Foot = Ui.Column(8, "GearSearchStartFoot");
        var counts = Ui.Add(Foot, Ui.Column(2, "GearSearchCounts"));
        _counts = Ui.Add(counts, new GridContainer { Name = "GearSearchCountsLine", Columns = 2 });
        _counts.AddThemeConstantOverride("h_separation", 12);
        _counts.AddThemeConstantOverride("v_separation", 2);
        _more = Ui.Add(counts, Wrapping(TextStyle.Small, "GearSearchCountsMore"));
        _note = Ui.Add(Foot, Ui.Label("", TextStyle.Small, "GearSearchNote", wrap: true));
        _note.LabelSettings = Compare.CompareText.Coloured(Look.Risky, TextStyle.Small);

        var foot = Ui.Add(Foot, Ui.Row(8));
        foot.Alignment = BoxContainer.AlignmentMode.End;
        _find = FramedButton.Make(Ui.Button("", "", "GearSearchFind", Start), 16, 6, main: true);
        _find.AddThemeFontOverride("font", Look.Font(700));
        _find.AddThemeFontSizeOverride("font_size", 18);
        foot.AddChild(_find);
    }

    public VBoxContainer Root { get; }

    /// <summary>The counts and the start button, which the tab puts where they stay in view.</summary>
    public VBoxContainer Foot { get; }

    /// <summary>
    /// The form for <paramref name="target"/> and a search of <paramref name="kind"/>, counted again when either or
    /// side A has changed since it was last shown.
    /// </summary>
    public void Show(string target, SearchKind kind, string? note = null)
    {
        var stamp = (target, kind, PanelState.CheckA, note, Tr.Version);
        if (stamp == _stamp)
            return;
        var fresh =
            _stamp is not { } last
            || last.Target != target
            || last.Kind != kind
            || !ReferenceEquals(last.Check, PanelState.CheckA);
        _stamp = stamp;
        if (_target != target)
            _shown = null;
        _target = target;
        _kind = kind;
        _styles.Visible = kind == SearchKind.BestGear;
        _gear.Locking = kind == SearchKind.BestGear;
        _find.Text =
            kind == SearchKind.BestGear
                // Translators: Start button of a gear search for the best setup of what the character has (gear, prayers, food…).
                ? Tr.Get("Find best setup")
                // Translators: Start button of a gear search for which unowned items would gain the most.
                : Tr.Get("Find upgrades");
        FillMetrics();
        _deaths.Show(
            _limit switch
            {
                DeathLimit.UnderFivePercent => "five",
                DeathLimit.SurvivedOnce => "any",
                _ => "none",
            }
        );
        Cs2Check.Set(_styles, _allStyles);
        _switches.Show(kind);
        _gear.Fill();
        _note.Text = note ?? "";
        _note.Visible = note != null;
        if (fresh || _preview == null)
            Count();
    }

    /// <summary>The form's choices and counts, for <c>cs2 ui</c>.</summary>
    public string Describe() =>
        $"metric={_metric} deaths={_limit} styles={(_allStyles ? "all" : "setup")} locks={_gear.Locks.Count} never={_gear.Excluded.Count} counts=\"{string.Join(" ", _counts.GetChildren().OfType<Label>().Select(l => l.Text))}\"";

    private void FillMetrics()
    {
        _metrics = [.. RunCache.Metrics(RunService.Instance.Current)];
        if (_metrics.All(m => m.Key != _metric))
            _metric = _metrics.Any(m => m.Key == PanelState.Prefs.Metric) ? PanelState.Prefs.Metric : Metrics.MainXp;
        _rankBy.Clear();
        var run = RunService.Instance.Current;
        var game = Game.LiveGame.Find();
        foreach (var metric in _metrics)
            _rankBy.AddItem(
                metric.Key == Metrics.MainXp ? Words.Metric(metric) : Words.MetricName(metric, null),
                game != null ? Game.Art.Metric(game, run != null ? Metrics.Resolve(run, metric.Key) : metric.Key) : null
            );
        _rankBy.Select(Math.Max(0, _metrics.FindIndex(m => m.Key == _metric)));
    }

    private void Changed()
    {
        _note.Visible = false;
        Count();
    }

    private SearchRequest? Request()
    {
        if (_target == null)
            return null;
        var prefs = PanelState.Prefs;
        return new SearchRequest(
            _kind,
            Simulate.SetupCheck.Current(),
            _target,
            new Goal(_metric, _limit),
            prefs.Trials,
            prefs.Copies ?? RunService.Instance.Status.DefaultCopies
        )
        {
            Locks = _kind == SearchKind.BestGear ? new HashSet<string>(_gear.Locks) : new HashSet<string>(),
            Excluded = new HashSet<string>(_gear.Excluded),
            AllStyles = _kind == SearchKind.BestGear && _allStyles,
            Kinds = KindSwitches.For(_kind),
            IncludeUnowned = _kind == SearchKind.BestGear && PanelState.Prefs.SearchIncludeUnowned,
        };
    }

    // The plan's counts, read from a check copy; a later change makes an earlier count's answer stale. The counts
    // shown before stay, their numbers faded, until the new ones come.
    private void Count()
    {
        _preview = null;
        _counting = true;
        _find.Disabled = true;
        if (_shown is { } shown && shown.Kind == _kind)
            Render(shown);
        else
            Say(Tr.Get("Counting combinations…"));
        if (Request() is not { } request || Game.LiveGame.Find() == null)
            return;
        var version = ++_version;
        try
        {
            RunService.PreviewSearch(
                request,
                (preview, error) =>
                {
                    if (version != _version || !GodotObject.IsInstanceValid(Root))
                        return;
                    _counting = false;
                    if (preview == null)
                    {
                        Say(error?.Message ?? Tr.Get("The sets couldn't be counted."));
                        return;
                    }
                    _preview = preview;
                    _shown = preview;
                    Render(preview);
                    _find.Disabled = preview.NothingToSearch;
                    if (preview.OverLimit && preview.Kind == SearchKind.BestGear)
                        // Translators: The start button when fighting every set would pass the long-search limit.
                        _find.Text = Tr.Get("Shorter search");
                }
            );
        }
        catch (CommandException e)
        {
            _counting = false;
            Say(e.Message);
        }
    }

    // A message in place of the counts, which are then no longer shown.
    private void Say(string message)
    {
        _shown = null;
        Ui.Clear(_counts);
        _more.Text = Escape(message);
        _more.Visible = true;
    }

    // A count's name and its value, the value faded while the counts are worked out again.
    private void Row(string name, string value)
    {
        var label = Ui.Add(_counts, Ui.Label(name, TextStyle.Small));
        label.Modulate = Colors.White with { A = 0.7f };
        var shown = Ui.Add(_counts, Ui.Label(value, TextStyle.Small, wrap: true));
        shown.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        if (_counting)
            shown.Modulate = Colors.White with { A = 0.45f };
    }

    private void Render(PlanPreview preview)
    {
        Restyle(_more, TextStyle.Small);
        Ui.Clear(_counts);
        _switches.Counts(preview);
        var lines = new List<string>();
        if (preview.Kind == SearchKind.Droptimizer)
        {
            // Translators: In Droptimizer's counts: items the character doesn't own.
            Row(Tr.Get("Not owned"), SearchWords.Count((double)preview.Unowned));
            // Translators: In Droptimizer's counts: of those, the ones the character can wear now.
            Row(Tr.Get("Wearable now"), SearchWords.Count((double)preview.Wearable));
            // Translators: In a gear search's counts before it starts: how many sets will be simulated.
            Row(Tr.Get("Simulation"), SearchWords.Count((double)preview.ToFight));
        }
        else
        {
            Row(Tr.Get("Combinations"), SearchWords.Count((double)preview.Possible));
            if (preview.RuledOut > 0)
                Row(Tr.Get("Ruled out"), SearchWords.Count((double)preview.RuledOut));
            Row(
                Tr.Get("Simulation"),
                preview.Climb
                    // Translators: In a gear search's counts, when there are too many sets to fight them all.
                    ? Tr.Get("Per slot")
                    : SearchWords.Count((double)preview.ToFight)
            );
            foreach (var skipped in preview.SkippedStyles)
                lines.Add(Escape(Tr.Format("{0}: skipped, {1}", [skipped.Style, Tr.Note(skipped.Why)])));
        }
        if (!preview.NothingToSearch && !preview.Climb && preview.Seconds is { } seconds)
            // Translators: In a gear search's counts before it starts: how long it is expected to take.
            Row(Tr.Get("Time"), SearchWords.About(seconds));
        if (preview.NothingToSearch)
            lines.Add(
                Escape(
                    preview.Kind == SearchKind.Droptimizer
                        ? Tr.Get("Nothing to try: no unowned item can be worn now.")
                        : Tr.Get("Nothing to search: nothing else you own fits an unlocked slot.")
                )
            );
        else if (preview.Climb)
            lines.Add(
                Escape(
                    // Translators: Under a gear search's counts before it starts, when there are too many sets to fight them all.
                    Tr.Get(
                        "Too many combinations to try: the search builds a set slot by slot. A better mix may exist."
                    )
                )
            );
        else if (preview.KindsOverLimit)
            lines.Add(
                Escape(
                    // Translators: Under a setup search's counts: {0} the long-search limit in minutes.
                    Tr.Format(
                        "Trying everything switched on may take over {0} min. Switch some off to make it shorter.",
                        Ui.Number(Math.Ceiling(preview.LimitSeconds / 60))
                    )
                )
            );
        else if (preview.Seconds is not null)
        {
            if (preview.OverLimit)
                lines.Add(
                    Escape(
                        // Translators: Under a gear search's counts: {0} the long-search limit in minutes.
                        Tr.Format(
                            "Simulating them all would take over {0} min, so the search builds a set slot by slot. A better mix may exist.",
                            Ui.Number(Math.Ceiling(preview.LimitSeconds / 60))
                        )
                    )
                );
        }
        else if (preview.OverLimit)
            lines.Add(
                Filled(
                    // Translators: Before any search of this kind of target has been timed; {0} is how many sets.
                    Tr.Format(
                        "{0} sets is too many to simulate before a search here has been timed, so the search builds a set slot by slot. A better mix may exist.",
                        Mark0
                    ),
                    TextStyle.Small,
                    SearchWords.Count((double)preview.ToFight)
                )
            );
        _more.Text = string.Join("\n", lines);
        _more.Visible = lines.Count > 0;
    }

    // A stand-in for a line's number, put in by Filled once the line is translated.
    private const string Mark0 = "\u00010\u0001";

    // A translated line with its number put in, faded while the counts are being worked out again.
    private string Filled(string translated, TextStyle style, string number) =>
        Escape(translated).Replace(Mark0, Number(Escape(number), style), StringComparison.Ordinal);

    private string Number(string text, TextStyle style) =>
        _counting ? $"[color=#{Look.Style(style).FontColor.ToHtml(false)}{Faded}]{text}[/color]" : text;

    // The alpha of a number being worked out again, as two hex digits: as faded as anything else pending.
    private const string Faded = "73";

    private static string Escape(string text) => text.Replace("[", "[lb]", StringComparison.Ordinal);

    // Text that wraps like a label, in a text style, with parts of it faded.
    private static RichTextLabel Wrapping(TextStyle style, string name)
    {
        var rich = new RichTextLabel
        {
            Name = name,
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            SelectionEnabled = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled,
        };
        rich.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        Restyle(rich, style);
        return rich;
    }

    // The style read again on each showing, so a change of look reaches the counts.
    private static void Restyle(RichTextLabel rich, TextStyle style)
    {
        var settings = Look.Style(style);
        if (settings.Font != null)
            rich.AddThemeFontOverride("normal_font", settings.Font);
        rich.AddThemeFontSizeOverride("normal_font_size", settings.FontSize);
        rich.AddThemeColorOverride("default_color", settings.FontColor);
    }

    private void Start()
    {
        if (Request() is { } request)
            _start(request);
    }
}
