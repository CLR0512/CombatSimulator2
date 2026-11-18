using Godot;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.SetupTab;

/// <summary>
/// A strip saying what runs and how far it is, with Cancel, beside the Setup tab's title: the Simulate tab's run
/// control isn't on screen there. Hidden while nothing runs.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed class RunningStrip
{
    private readonly Label _text;
    private readonly Control _bar;

    public RunningStrip()
    {
        Root = Look.Panel(Art.RowBox, "Cs2SetupRunning");
        var line = Ui.Row(10);
        Root.AddChild(Ui.Margin(line, 8, 2, 8, 2));
        _text = Ui.Label("", TextStyle.Small, "Cs2SetupRunningText");
        _text.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        line.AddChild(_text);
        _bar = Bar.Make("Cs2SetupRunningBar");
        _bar.CustomMinimumSize = new Vector2(240, _bar.CustomMinimumSize.Y);
        _bar.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        line.AddChild(_bar);
        line.AddChild(Ui.Button(Tr.Get("Cancel"), Look.Button4, "Cs2SetupRunningCancel", Simulate.RunStarter.Cancel));
        Root.Visible = false;
    }

    public PanelContainer Root { get; }

    /// <summary>Shows what runs now; called each frame while the Setup tab shows.</summary>
    public void Show()
    {
        var service = RunService.Instance;
        Root.Visible = service.IsActive;
        if (!service.IsActive)
            return;
        var run = service.Current;
        var (share, text) =
            Simulate.RunControl.BestLoadoutsProgress(service)
            ?? (
                service.Search is { IsActive: true }
                    // Translators: On the Setup tab while a gear search runs.
                    ? (0, Tr.Get("Gear search running"))
                    // Translators: On the Setup tab while a run is under way: targets done of all.
                    : (
                        (double)(run?.Done ?? 0) / Math.Max(1, run?.Targets.Count ?? 1),
                        Tr.Format("Simulating · {0} of {1} targets", [run?.Done ?? 0, run?.Targets.Count ?? 0])
                    )
            );
        _text.Text = text;
        Bar.Set(_bar, share, Look.Engine);
    }
}
