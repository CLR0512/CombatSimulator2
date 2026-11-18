using Godot;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Results;

/// <summary>
/// The line under the results when a run of fewer targets replaced a wider one: how many of the earlier run's
/// targets are shown, and a button back to that run. Not the ignored ones: those were left out on purpose and keep
/// their rows, greyed; these simply weren't in this run.
/// </summary>
internal sealed class NarrowLine
{
    private readonly HBoxContainer _root;
    private readonly Label _text;
    private readonly Button _show;
    private string? _runId;

    /// <summary>Raised when showing the earlier run is refused, with the reason for the view to show.</summary>
    public event Action<string>? Message;

    public NarrowLine()
    {
        _root = Ui.Row(8, "Cs2NarrowLine");
        _text = Ui.Add(_root, Ui.Label("", TextStyle.Label));
        _text.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _show = Ui.Add(_root, Ui.Button(Tr.Get("Show", "earlier run"), Look.Button4, "Cs2NarrowShow", ShowEarlier));
        _show.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _root.Visible = false;
    }

    public Control Root => _root;

    /// <summary>Shows the line for <paramref name="shown"/> targets on screen, or hides it when nothing wider was replaced.</summary>
    public void Refresh(int shown)
    {
        var earlier = PanelPins.Earlier;
        _root.Visible = earlier != null;
        if (earlier == null)
            return;
        _runId = earlier.RunId;
        var saved = PanelPins.EarlierSaved;
        _show.Visible = saved;
        _text.Text = saved
            ? Tr.Plural(
                "Showing {0} of {1} target · previous run:",
                "Showing {0} of {1} targets · previous run:",
                earlier.Targets,
                Ui.Number(shown),
                Ui.Number(earlier.Targets)
            )
            : Tr.Plural(
                "Showing {0} of {1} target · previous run no longer saved",
                "Showing {0} of {1} targets · previous run no longer saved",
                earlier.Targets,
                Ui.Number(shown),
                Ui.Number(earlier.Targets)
            );
    }

    private void ShowEarlier()
    {
        if (_runId == null)
            return;
        try
        {
            RunService.Instance.ShowSaved(_runId);
            PanelState.ShowComparison(false);
        }
        catch (CommandException e)
        {
            Message?.Invoke(e.Message);
        }
    }
}
