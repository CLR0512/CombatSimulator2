using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Help;

/// <summary>
/// The Gear tab's guide over CS2's dimmed content: what Find best setup and Find upgrades do and how they search, step by
/// step, with Close. Its body scrolls when it doesn't fit; the title and button stay visible.
/// </summary>
internal sealed class GearGuide
{
    private readonly Control _layer;
    private readonly ColorRect _dim;
    private readonly PanelContainer _window;
    private readonly ScrollContainer _scroll;
    private readonly VBoxContainer _body;

    public GearGuide(Control layer, Action close, Action hideButton)
    {
        _layer = layer;
        _dim = Ui.Add(layer, HelpLayer.Dim("Cs2GearGuideDim"));
        _window = Ui.Add(layer, Look.Panel(Art.FrameBox, "Cs2GearGuide"));
        _window.MouseFilter = Control.MouseFilterEnum.Stop;
        var column = Ui.Column(12);
        _window.AddChild(Ui.Margin(column, 16, 12, 16, 14));

        var top = Ui.Add(column, Ui.Row(8));
        var title = Ui.Add(top, Look.Panel(Art.RibbonBox, "Cs2GearGuideTitle"));
        title.CustomMinimumSize = new Vector2(360, 0);
        title.AddChild(Ui.Label(GearGuideText.Title, TextStyle.Title));
        top.AddChild(Ui.Spacer());
        var closeButton = Ui.Add(top, Ui.CloseButton("Cs2GearGuideClose", close));

        _scroll = Ui.Add(
            column,
            new ScrollContainer
            {
                Name = "Cs2GearGuideBody",
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            }
        );
        _body = Ui.Add(_scroll, Ui.Column(16));
        _body.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        FillBody(_body);

        var footer = Ui.Add(column, Ui.Row(10, "Cs2GearGuideFooter"));
        footer.AddChild(Ui.Spacer());
        var hide = Ui.Add(footer, Ui.Button(GearGuideText.HideButton, Look.Button8, "Cs2GearGuideHide", hideButton));
        hide.TooltipText = GearGuideText.HideButtonHover;
        var done = Ui.Add(footer, Ui.Button(HelpText.Close, Look.Button8, "Cs2GearGuideDone", close));

        // Keyboard focus goes round the three buttons and stays in the window.
        Button[] ring = [closeButton, hide, done];
        for (var i = 0; i < ring.Length; i++)
        {
            var next = ring[(i + 1) % ring.Length].GetPath();
            var previous = ring[(i + ring.Length - 1) % ring.Length].GetPath();
            var button = ring[i];
            button.FocusMode = Control.FocusModeEnum.All;
            button.FocusNext = next;
            button.FocusPrevious = previous;
            button.FocusNeighborRight = next;
            button.FocusNeighborBottom = next;
            button.FocusNeighborLeft = previous;
            button.FocusNeighborTop = previous;
        }
        done.CallDeferred(Control.MethodName.GrabFocus);
        Layout();
    }

    private static void FillBody(VBoxContainer body)
    {
        body.AddChild(Ui.Label(GearGuideText.Lead, TextStyle.BodyStrong, "Cs2GearGuideLead", wrap: true));

        var top = Section(body, "Cs2GearGuideTop", GearGuideText.TopGear, GearGuideText.TopGearWhat);
        top.AddChild(
            FoldStep.Make(
                Art.BankPath,
                GearGuideText.GatherTitle,
                GearGuideText.Gather,
                "Cs2GearGuideGather",
                GearGuideText.GatherDetail
            )
        );
        top.AddChild(
            FoldStep.Make(
                Art.StatisticsPath,
                GearGuideText.RuleOutTitle,
                GearGuideText.RuleOut,
                "Cs2GearGuideRuleOut",
                GearGuideText.RuleOutDetail
            )
        );
        top.AddChild(
            FoldStep.Make(
                Art.CombatPath,
                GearGuideText.RaceTitle,
                GearGuideText.Race(PanelState.Prefs.Trials),
                "Cs2GearGuideRace",
                GearGuideText.RaceDetail
            )
        );
        top.AddChild(
            FoldStep.Make(
                Art.MasteryPath,
                GearGuideText.ClimbTitle,
                GearGuideText.Climb,
                "Cs2GearGuideClimb",
                GearGuideText.ClimbDetail
            )
        );
        top.AddChild(
            FoldStep.Make(
                Art.PrayerPath,
                GearGuideText.BeyondTitle,
                GearGuideText.Beyond,
                "Cs2GearGuideBeyond",
                GearGuideText.BeyondDetail
            )
        );
        top.AddChild(
            FoldStep.Make(
                Art.TourPath,
                GearGuideText.AnswerTitle,
                GearGuideText.Answer,
                "Cs2GearGuideAnswer",
                GearGuideText.AnswerDetail
            )
        );

        var drops = Section(body, "Cs2GearGuideDrops", GearGuideText.Droptimizer, GearGuideText.DroptimizerWhat);
        drops.AddChild(
            FoldStep.Make(
                Art.ShopPath,
                GearGuideText.GatherTitle,
                GearGuideText.DropGather,
                "Cs2GearGuideDropGather",
                GearGuideText.DropGatherDetail
            )
        );
        drops.AddChild(
            FoldStep.Make(
                Art.StatisticsPath,
                GearGuideText.RuleOutTitle,
                GearGuideText.DropRuleOut,
                "Cs2GearGuideDropRuleOut",
                GearGuideText.DropRuleOutDetail
            )
        );
        drops.AddChild(
            FoldStep.Make(
                Art.CombatPath,
                GearGuideText.SwapTitle,
                GearGuideText.Swap,
                "Cs2GearGuideSwap",
                GearGuideText.SwapDetail
            )
        );
        drops.AddChild(
            FoldStep.Make(
                Art.TourPath,
                GearGuideText.AnswerTitle,
                GearGuideText.DropAnswer,
                "Cs2GearGuideDropAnswer",
                GearGuideText.DropAnswerDetail
            )
        );

        body.AddChild(Ui.Label(GearGuideText.Kept, TextStyle.Small, "Cs2GearGuideKept", wrap: true));
    }

    // A search's heading and what it finds, over its steps.
    private static VBoxContainer Section(VBoxContainer body, string name, string title, string what)
    {
        var section = Ui.Add(body, Ui.Column(8, name));
        var head = Ui.Add(section, Ui.Column(2));
        head.AddChild(Ui.Label(title, TextStyle.Heading));
        head.AddChild(Ui.Label(what, TextStyle.Small, wrap: true));
        return section;
    }

    /// <summary>
    /// Centres the window in the layer, at most as tall as the layer allows; the body scrolls beyond that.
    /// </summary>
    public void Layout() => Explainer.Fit(_layer, _dim, _window, _scroll, _body);
}
