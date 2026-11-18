using Godot;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Help;

/// <summary>
/// The Guide window over CS2's dimmed content: what CS2 does, how sure a result is and what every fight
/// assumes, with Start tour and Close. Its body scrolls when it doesn't fit; the title and buttons stay visible.
/// </summary>
internal sealed class Explainer
{
    public const float Width = 640;
    private const float Margin = 16;

    private readonly Control _layer;
    private readonly ColorRect _dim;
    private readonly PanelContainer _window;
    private readonly ScrollContainer _scroll;
    private readonly VBoxContainer _body;
    private readonly Button _start;

    public Explainer(Control layer, Action close, Action startTour)
    {
        _layer = layer;
        _dim = Ui.Add(layer, HelpLayer.Dim("Cs2Dim"));
        _window = Ui.Add(layer, Look.Panel(Art.FrameBox, "Cs2Explainer"));
        _window.MouseFilter = Control.MouseFilterEnum.Stop;
        var column = Ui.Column(12);
        _window.AddChild(Ui.Margin(column, 16, 12, 16, 14));

        var top = Ui.Add(column, Ui.Row(8));
        var title = Ui.Add(top, Look.Panel(Art.RibbonBox, "Cs2ExplainerTitle"));
        title.CustomMinimumSize = new Vector2(360, 0);
        title.AddChild(Ui.Label(HelpText.Title, TextStyle.Title));
        top.AddChild(Ui.Spacer());
        var closeButton = Ui.Add(top, Ui.CloseButton("Cs2ExplainerClose", close));

        _scroll = Ui.Add(
            column,
            new ScrollContainer
            {
                Name = "Cs2ExplainerBody",
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            }
        );
        _body = Ui.Add(_scroll, Ui.Column(14));
        _body.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        FillBody(_body);

        var footer = Ui.Add(column, Ui.Row(10, "Cs2ExplainerFooter"));
        footer.AddChild(Ui.Picture(Art.File(Art.LockPath) ?? Art.Missing, 20));
        footer.AddChild(Ui.Label(HelpText.Untouched, TextStyle.Small, "Cs2Untouched", wrap: true));
        _start = Ui.Add(footer, Ui.Button(HelpText.StartTour, Look.Action8, "Cs2StartTour", startTour));
        var done = Ui.Add(footer, Ui.Button(HelpText.Close, Look.Button8, "Cs2ExplainerDone", close));

        // Keyboard focus goes round the three buttons and stays in the window.
        Button[] ring = [closeButton, _start, done];
        for (var i = 0; i < ring.Length; i++)
        {
            var button = ring[i];
            var next = ring[(i + 1) % ring.Length].GetPath();
            var previous = ring[(i + ring.Length - 1) % ring.Length].GetPath();
            button.FocusMode = Control.FocusModeEnum.All;
            button.FocusNext = next;
            button.FocusPrevious = previous;
            button.FocusNeighborRight = next;
            button.FocusNeighborBottom = next;
            button.FocusNeighborLeft = previous;
            button.FocusNeighborTop = previous;
        }
        _start.CallDeferred(Control.MethodName.GrabFocus);
        Layout();
    }

    private static void FillBody(VBoxContainer body)
    {
        var prefs = PanelState.Prefs;
        var unit = prefs.Unit;
        body.AddChild(Ui.Label(HelpText.Lead, TextStyle.BodyStrong, "Cs2ExplainerLead", wrap: true));

        var steps = Ui.Add(body, Ui.Column(10));
        steps.AddChild(TimelineStep.Make(Icon(Art.SavePath), HelpText.SnapshotTitle, HelpText.Snapshot, "Cs2Step1"));
        steps.AddChild(TimelineStep.Make(Icon(Art.CombatPath), HelpText.FightTitle, HelpText.Fight(prefs), "Cs2Step2"));
        steps.AddChild(
            TimelineStep.Make(Icon(Art.StatisticsPath), HelpText.MeasureTitle, HelpText.Measure(unit), "Cs2Step3")
        );

        var sample = Ui.Add(body, Look.Panel(Art.RowBox, "Cs2Sample"));
        var sampleColumn = Ui.Add(sample, Ui.Column(4));
        var sampleRow = Ui.Add(sampleColumn, Ui.Row(10));
        var (value, low, high) = HelpText.Sample(unit);
        sampleRow.AddChild(Ui.Label(HelpText.SampleLabel(unit), TextStyle.Body, "Cs2SampleLabel"));
        sampleRow.AddChild(Ui.Spacer());
        sampleRow.AddChild(Ui.Label(Ui.Number(value), TextStyle.Value, "Cs2SampleValue"));
        sampleRow.AddChild(Ui.Label(Ui.Range(low, high), TextStyle.Gold, "Cs2SampleRange"));
        var note = Ui.Add(sampleColumn, Ui.Label(HelpText.SampleRange, TextStyle.Small, "Cs2SampleNote", wrap: true));
        note.HorizontalAlignment = HorizontalAlignment.Right;

        var every = Ui.Add(body, Ui.Column(8, "Cs2EveryFight"));
        every.AddChild(Ui.Label(HelpText.EveryFight, TextStyle.Heading));
        var grid = Ui.Add(every, new GridContainer { Columns = 2, Name = "Cs2EveryFightLines" });
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 8);
        foreach (var key in Lines())
        {
            if (HelpText.Line(key) is not { } line)
                continue;
            grid.AddChild(IconLine.Make(Icon(line.Icon), line.Text, Ui.Id("Cs2Assumption", key)));
        }
        every.AddChild(Ui.Label(HelpText.AllListed, TextStyle.Small, "Cs2AllListed", wrap: true));

        var features = Ui.Add(body, Ui.Column(8, "Cs2Features"));
        features.AddChild(Ui.Label(HelpText.WhatYouCanDo, TextStyle.Heading));
        features.AddChild(
            FoldStep.Make(
                Art.CombatPath,
                HelpText.SimulateTitle,
                HelpText.SimulateLine,
                "Cs2HelpSimulate",
                HelpText.SimulateDetail
            )
        );
        features.AddChild(
            FoldStep.Make(
                Art.DuplicatePath,
                HelpText.CompareTitle,
                HelpText.CompareLine,
                "Cs2HelpCompare",
                HelpText.CompareDetail
            )
        );
        features.AddChild(
            FoldStep.Make(Art.BankPath, HelpText.GearTitle, HelpText.GearLine, "Cs2HelpGear", HelpText.GearDetail)
        );
        features.AddChild(
            FoldStep.Make(
                Art.SlayerPath,
                HelpText.SlayerTitle,
                HelpText.SlayerLine,
                "Cs2HelpSlayer",
                HelpText.SlayerDetail
            )
        );
    }

    // The assumptions every result lists that are shown here, in the explainer's order. One shown in results but
    // without a line here is logged, not shown: the explainer never invents its wording.
    private static IEnumerable<string> Lines()
    {
        var flagged = Assumption.All.Where(a => a.InEveryFight).Select(a => a.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in flagged.Where(k => HelpText.Line(k) == null))
            Log.Info($"help: no explainer line for assumption '{key}'");
        return HelpText.LineOrder.Where(flagged.Contains);
    }

    private static Texture2D? Icon(string path) => Art.File(path) ?? Art.Missing;

    /// <summary>
    /// Centres the window in the layer, at most as tall as the layer allows; the body scrolls beyond that.
    /// </summary>
    public void Layout() => Fit(_layer, _dim, _window, _scroll, _body);

    /// <summary>
    /// Lays out a help window: <paramref name="dim"/> over the whole layer, <paramref name="window"/> centred in it,
    /// at most as tall as the layer allows, with <paramref name="scroll"/> showing what of <paramref name="body"/>
    /// fits.
    /// </summary>
    public static void Fit(Control layer, ColorRect dim, PanelContainer window, ScrollContainer scroll, Control body)
    {
        var room = layer.Size;
        dim.Position = Vector2.Zero;
        dim.Size = room;
        var bodyHeight = body.GetCombinedMinimumSize().Y;
        scroll.CustomMinimumSize = Vector2.Zero;
        var chrome = window.GetCombinedMinimumSize().Y;
        var height = Math.Min(chrome + bodyHeight, Math.Max(chrome, room.Y - 2 * Margin));
        scroll.CustomMinimumSize = new Vector2(0, Math.Max(0, height - chrome));
        var width = Math.Min(Width, Math.Max(320, room.X - 2 * Margin));
        window.Size = new Vector2(width, height);
        window.Position = ((room - window.Size) / 2).Round();
    }
}
