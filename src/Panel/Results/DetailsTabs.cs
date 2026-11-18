using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Results;

/// <summary>
/// The details' tabs under the header: Rates (the ranked metrics, then drops per unit of time and rare drops), Fights
/// (risk, fight details and a dungeon's fight by fight) and, for a single run's target, Gear (the gear search). Flat tabs, the chosen one raised with
/// a gold underline; Fights carries the risk mark when a warning applies, so it is never hidden behind the tab. The
/// tab chosen stays from one target to the next. The tabs are words over a rule, the chosen one bright over a grey-blue
/// line; in the game's look the panel they open on is the game's ability card.
/// </summary>
internal sealed class DetailsTabs
{
    public const string Rates = "rates";
    public const string Fights = "fights";
    public const string Gear = "gear";

    private readonly Dictionary<string, (Button Tab, Label Title, VBoxContainer Page)> _tabs = [];
    private readonly PanelContainer _warning;

    public DetailsTabs(VBoxContainer body, bool gear)
    {
        // No gap between the tabs: the pointer passes from one to the next without the highlight dropping.
        var row = Ui.Add(body, Ui.Row(0, "Cs2DetailTabs"));
        var frame = Ui.Add(body, new PanelContainer { Name = "Cs2DetailPages" });
        frame.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        PixelBox.Panel(frame, Frame());
        var column = Ui.Add(frame, Ui.Column(8));
        // No scrollbar: the page fades out at an end with more past it, as the panel's other lists do.
        Scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        column.AddChild(ScrollFade.Wrap(Scroll));
        var pages = Ui.Add(Scroll, Ui.Column(0));
        pages.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        // Fixed at the panel's foot, outside the scroll: the same place on every tab.
        Foot = Ui.Add(column, Ui.Column(4, "Cs2DetailFoot"));
        foreach (
            var (key, title) in new[]
            {
                // Translators: Tab in a target's details: its rates against the run's other targets, then its drops.
                (Rates, Tr.Get("Rates")),
                // Translators: Tab in a target's details: risk, how the fights went and a dungeon's fight by fight.
                (Fights, Tr.Get("Fights")),
                // Translators: Tab in a target's details: find the best gear against this target.
                (Gear, Tr.Get("Gear")),
            }.Where(t => gear || t.Item1 != Gear)
        )
        {
            var name = char.ToUpperInvariant(key[0]) + key[1..];
            var tab = Ui.Button("", "", "Cs2DetailTab" + name, () => Choose(key));
            tab.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            tab.ToggleMode = true;
            row.AddChild(tab);
            // The title and any mark in one centred line, so the mark sits beside the word whatever the tab's width.
            var line = Ui.Add(tab, Ui.Row(6));
            line.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            line.Alignment = BoxContainer.AlignmentMode.Center;
            line.MouseFilter = Control.MouseFilterEnum.Ignore;
            var label = Ui.Add(line, Ui.Label(title, TextStyle.Body));
            label.MouseFilter = Control.MouseFilterEnum.Ignore;
            tab.CustomMinimumSize = new Vector2(0, label.GetMinimumSize().Y + 14);
            var page = Ui.Add(pages, Ui.Column(12, "Cs2DetailPage" + name));
            _tabs[key] = (tab, label, page);
        }
        _warning = RiskMark.Make("Cs2DetailTabWarning");
        _warning.Visible = false;
        _warning.MouseFilter = Control.MouseFilterEnum.Ignore;
        _tabs[Fights].Title.GetParent().AddChild(_warning);
        Choose(_tabs.ContainsKey(PanelState.Prefs.DetailTab) ? PanelState.Prefs.DetailTab : Rates, save: false);
    }

    public VBoxContainer Page(string key) => _tabs[key].Page;

    /// <summary>Whether this panel has the tab <paramref name="key"/>.</summary>
    public bool Has(string key) => _tabs.ContainsKey(key);

    /// <summary>The scroll of the tab's page, inside the panel.</summary>
    public ScrollContainer Scroll { get; }

    /// <summary>What every tab shows at the panel's foot, under its page (the assumptions).</summary>
    public VBoxContainer Foot { get; }

    /// <summary>
    /// Fills Fights: risk, how the fights went and, for a dungeon, fight by fight; the tab carries the risk mark.
    /// </summary>
    public void FillFights(TargetResult result, Func<string, string> names)
    {
        _tabs[Fights].Title.Text = Tr.Get("Fights");
        var page = Page(Fights);
        Ui.Clear(page);
        Mark(Risk.Of(result), result.NoneSurvived);
        if (result.HasEstimates)
            page.AddChild(RiskBlock.Single(result));
        page.AddChild(FightBlock.Make(result, names));
        if (result.HasEstimates && result.Kind == TargetKind.Dungeon && result.Fights.Count > 0)
            page.AddChild(FightsSection.Make(result, names).Root);
    }

    /// <summary>
    /// Fills Fights for a Slayer master, named Monsters there: its monsters, blocks and risk. Any other target puts the
    /// name back with <see cref="FillFights"/>.
    /// </summary>
    public void FillMaster(Run run, TargetResult master, string metric, TimeUnit unit)
    {
        // Translators: Tab in a Slayer master's details: its monsters, what blocking them would do, and its risk.
        _tabs[Fights].Title.Text = Tr.Get("Monsters");
        Mark(null, false);
        Slayer.MasterDetail.Fill(Page(Fights), run, master, metric, unit);
    }

    /// <summary>Shows the risk mark on the Fights tab when <paramref name="risk"/> warns or no trial survived.</summary>
    public void Mark(Risk? risk, bool died) => RiskMark.Set(_warning, risk, died);

    /// <summary>The tab whose page holds <paramref name="node"/>, if any.</summary>
    public string? TabOf(Node node) => _tabs.FirstOrDefault(t => t.Value.Page.IsAncestorOf(node)).Key;

    public string Shown { get; private set; } = Rates;

    public void Choose(string key, bool save = true)
    {
        Shown = key;
        foreach (var (name, (tab, title, page)) in _tabs)
        {
            var on = name == key;
            page.Visible = on;
            tab.SetPressedNoSignal(on);
            // One size for every tab: the chosen one is only brighter.
            title.LabelSettings = Look.Style(TextStyle.Body);
            title.Modulate = Colors.White with { A = on ? 1f : 0.6f };
            PixelBox.State(tab, "normal", Style(on));
            PixelBox.State(tab, "hover", Style(on, hover: true));
            PixelBox.State(tab, "pressed", Style(on));
            PixelBox.State(tab, "hover_pressed", Style(on));
            tab.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        }
        if (save)
        {
            PanelState.Prefs.DetailTab = key;
            PanelState.Save();
        }
    }

    // The grey-blue of the game's card edges, under the chosen tab.
    private static readonly Color Underline = Color.FromHtml("#8a9bb1");

    // Text tabs in both looks, no boxes: the chosen word bright over a grey-blue line, the others dim over a faint rule,
    // so the row reads as the panel's header rather than as buttons beside it.
    private static StyleBoxFlat Style(bool on, bool hover = false) =>
        new()
        {
            BgColor = hover && !on ? Colors.White with { A = 0.04f } : Colors.Transparent,
            BorderColor = on ? Underline : Colors.White with { A = 0.15f },
            BorderWidthBottom = 1,
            ContentMarginTop = 4,
            ContentMarginBottom = 6,
        };

    // The inner panel the tabs open on: the game's ability card in its look, a plain top margin in mElvUI's skin.
    private static StyleBox Frame() =>
        Look.IsGameLook ? Look.GameBox(Art.CardBox) : new StyleBoxEmpty { ContentMarginTop = 10 };
}
