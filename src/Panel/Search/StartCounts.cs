using Godot;
using MelvorMods.CombatSimulator2.Model.Search;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Search;

/// <summary>
/// What a search would fight, counted from its plan before it starts. A gear search's counts are one line of what
/// starting costs, whose hover gives every count and the lines that explain them; Droptimizer's few counts each have
/// a row.
/// </summary>
internal sealed class StartCounts
{
    private readonly GridContainer _counts;
    private readonly RichTextLabel _more;
    private readonly Label _line;
    private readonly List<(string Name, string Value)> _rows = [];
    private readonly List<string> _plain = [];
    private bool _counting;

    public StartCounts()
    {
        Root = Ui.Column(2, "GearSearchCounts");
        _line = Ui.Add(Root, Ui.Label("", TextStyle.Small, "GearSearchCountsShort", wrap: true));
        _line.MouseFilter = Control.MouseFilterEnum.Stop;
        _counts = Ui.Add(Root, new GridContainer { Name = "GearSearchCountsLine", Columns = 2 });
        _counts.AddThemeConstantOverride("h_separation", 12);
        _counts.AddThemeConstantOverride("v_separation", 2);
        _more = Ui.Add(Root, Wrapping(TextStyle.Small, "GearSearchCountsMore"));
    }

    public VBoxContainer Root { get; }

    /// <summary>The counts as shown, for <c>cs2 ui</c>.</summary>
    public string Describe() => string.Join(" ", _rows.Select(r => $"{r.Name} {r.Value}"));

    /// <summary>A message in place of the counts, which are then no longer shown.</summary>
    public void Say(string message)
    {
        _rows.Clear();
        Ui.Clear(_counts);
        _line.Visible = false;
        _more.Text = Escape(message);
        _more.Visible = true;
    }

    /// <summary>The counts of <paramref name="preview"/>, faded while <paramref name="counting"/> again.</summary>
    public void Render(PlanPreview preview, bool counting)
    {
        _counting = counting;
        Restyle(_more, TextStyle.Small);
        Ui.Clear(_counts);
        _rows.Clear();
        _plain.Clear();
        var lines = new List<string>();
        void Line(string escaped, string plain)
        {
            lines.Add(escaped);
            _plain.Add(plain);
        }
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
            if (preview.Pool == ItemPool.Every)
                Row(Tr.Get("Items"), PoolWords(preview));
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
            {
                var text = Tr.Format("{0}: skipped, {1}", [skipped.Style, Tr.Note(skipped.Why)]);
                Line(Escape(text), text);
            }
        }
        // Over the limit, the search goes slot by slot whatever fighting every set would take: its time is the climb's.
        var slotBySlot = preview.Climb || (preview.OverLimit && preview.Kind == SearchKind.BestGear);
        string? time = null;
        if (!preview.NothingToSearch && (slotBySlot ? ClimbTime(preview) : Estimate(preview.Seconds)) is { } shown)
            // Translators: In a gear search's counts before it starts: how long it is expected to take.
            Row(Tr.Get("Time"), time = shown);
        if (preview.NothingToSearch)
        {
            var text =
                preview.Kind == SearchKind.Droptimizer
                    ? Tr.Get("Nothing to try: no unowned item can be worn now.")
                    : Tr.Get("Nothing to search: nothing else you own fits an unlocked slot.");
            Line(Escape(text), text);
        }
        else if (preview.Climb)
        {
            // Translators: Under a gear search's counts before it starts, when there are too many sets to fight them all.
            var text = Tr.Get(
                "Too many combinations to try: the search builds a set slot by slot. A better mix may exist."
            );
            Line(Escape(text), text);
        }
        else if (slotBySlot && preview.Seconds is not null)
        {
            // Translators: Under a gear search's counts: {0} the long-search limit in minutes.
            var text = Tr.Format(
                "Simulating them all would take over {0} min, so the search builds a set slot by slot. A better mix may exist.",
                Ui.Number(Math.Ceiling(preview.LimitSeconds / 60))
            );
            Line(Escape(text), text);
        }
        else if (slotBySlot)
        {
            // Translators: Before any search of this kind of target has been timed; {0} is how many sets.
            var text = Tr.Format(
                "{0} sets is too many to simulate before a search here has been timed, so the search builds a set slot by slot. A better mix may exist.",
                Mark0
            );
            var count = SearchWords.Count((double)preview.ToFight);
            Line(Filled(text, TextStyle.Small, count), text.Replace(Mark0, count, StringComparison.Ordinal));
        }
        else if (preview.KindsOverLimit)
        {
            // Translators: Under a setup search's counts: {0} the long-search limit in minutes.
            var text = Tr.Format(
                "Trying everything switched on may take over {0} min. Switch some off to make it shorter.",
                Ui.Number(Math.Ceiling(preview.LimitSeconds / 60))
            );
            Line(Escape(text), text);
        }
        var oneLine = preview.Kind == SearchKind.BestGear && !preview.NothingToSearch;
        _counts.Visible = !oneLine;
        _line.Visible = oneLine;
        if (oneLine)
        {
            _line.Text = ShortLine(preview, slotBySlot, time) + "  ⓘ";
            _line.Modulate = Colors.White with { A = counting ? 0.45f : 1f };
            _line.TooltipText = string.Join(
                "\n",
                _rows.Select(r => Tr.Format("{0}: {1}", [r.Name, r.Value])).Concat(_plain.Select(p => "\n" + p))
            );
            lines.Clear();
        }
        _more.Text = string.Join("\n", lines);
        _more.Visible = lines.Count > 0;
    }

    // The counts that say what starting costs: how many sets, whether they are fought slot by slot, and the time.
    private static string ShortLine(PlanPreview preview, bool slotBySlot, string? time)
    {
        var parts = new List<string>
        {
            // Translators: The gear search form's short counts: {0} how many sets the character's choices make.
            Tr.Format("{0} sets", SearchWords.Count((double)preview.Possible)),
            slotBySlot
                // Translators: The gear search form's short counts: the search builds a set slot by slot.
                ? Tr.Get("slot by slot")
                // Translators: The gear search form's short counts: {0} how many sets will be simulated.
                : Tr.Format("{0} to simulate", SearchWords.Count((double)preview.ToFight)),
        };
        if (time != null)
            parts.Add(time);
        return string.Join(" · ", parts);
    }

    // A count's name and its value, the value faded while the counts are worked out again.
    private void Row(string name, string value)
    {
        _rows.Add((name, value));
        var label = Ui.Add(_counts, Ui.Label(name, TextStyle.Small));
        label.Modulate = Colors.White with { A = 0.7f };
        var shown = Ui.Add(_counts, Ui.Label(value, TextStyle.Small, wrap: true));
        shown.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        if (_counting)
            shown.Modulate = Colors.White with { A = 0.45f };
    }

    private static string? Estimate(double? seconds) => seconds is { } s ? SearchWords.About(s) : null;

    // A search slot by slot: as timed here, with the player's limit when it would run past it; before it was timed,
    // the limit; nothing without a limit or a timing.
    private static string? ClimbTime(PlanPreview preview)
    {
        var limit = preview.MaxSeconds is { } max ? Ui.Number(Math.Ceiling(max / 60)) : null;
        if (preview.ClimbSeconds is not { } climbing)
            // Translators: In a gear search's counts: a search slot by slot not yet timed runs at most {0} minutes, the player's limit.
            return limit == null ? null : Tr.Format("Up to {0} min", limit);
        return limit != null && climbing > preview.MaxSeconds
            // Translators: In a gear search's counts: {0} how long building a set slot by slot is expected to take, {1} the player's limit it stops at, in minutes.
            ? Tr.Format("{0} · stops at {1} min", [SearchWords.About(climbing), limit])
            : SearchWords.About(climbing);
    }

    // Every item's count over the pool's slot types, and how many of them the character doesn't own.
    private static string PoolWords(PlanPreview preview)
    {
        var items = preview.PoolCounts.Sum(c => c.Items);
        var owned = preview.PoolCounts.Sum(c => c.Owned);
        // Translators: In a gear search's counts over every item: {0} items in the pool, {1} of them not owned.
        return Tr.Format("{0} · {1} not owned", [Ui.Number(items), Ui.Number(items - owned)]);
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
}
