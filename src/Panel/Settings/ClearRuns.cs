using System.Globalization;
using Godot;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Settings;

/// <summary>
/// "Clear saved runs": the button and the line beside it that says what a clear will delete and what stays.
/// </summary>
internal sealed class ClearRuns
{
    private readonly Button _button;
    private readonly Label _line;
    private ulong _armedAt;
    private ClearPlan? _plan;
    private HistoryStamp? _checked;

    public ClearRuns()
    {
        _button = Ui.Button(Tr.Get("Clear saved runs"), Look.Cancel8, "Cs2SetClear", Pressed);
        _line = Ui.Label("", TextStyle.Body, "Cs2SetClearPlan");
        _line.Visible = false;
    }

    public Button Button => _button;

    /// <summary>The plan's line, shown while the button is armed.</summary>
    public Label Line => _line;

    /// <summary>Raised after the saved runs were cleared.</summary>
    public event Action? Cleared;

    /// <summary>The armed plan's counts, for <c>cs2 ui</c>.</summary>
    public void Describe(System.Text.StringBuilder text)
    {
        if (_plan is { } plan)
            text.Append(
                CultureInfo.InvariantCulture,
                $"\nclear armed runs={plan.Delete.Count} comparisons={plan.ComparisonsDelete.Count} pinned={plan.PinnedKept}"
            );
    }

    private const ulong ArmedMs = 8000;

    // Asks first: the first press shows what clearing will do and arms the button for a few seconds, the second
    // does exactly that. The plan shown is the plan carried out, so the counts can't differ from the outcome.
    private void Pressed()
    {
        var service = RunService.Instance;
        if (_plan is { } plan && Time.GetTicksMsec() - _armedAt <= ArmedMs)
        {
            Disarm();
            service.Clear(plan);
            Cleared?.Invoke();
            Refresh();
            return;
        }
        plan = service.ClearPlan();
        if (plan.Nothing)
        {
            Refresh();
            return;
        }
        _plan = plan;
        var armedAt = _armedAt = Time.GetTicksMsec();
        _button.Text = Tr.Get("Click again to clear");
        _line.Text = PlanText(plan);
        _line.Visible = true;
        _button.GetTree().CreateTimer(ArmedMs / 1000.0).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(_button) && _armedAt == armedAt)
            {
                Disarm();
                Refresh();
            }
        };
    }

    private void Disarm()
    {
        _plan = null;
        _armedAt = 0;
        _button.Text = Tr.Get("Clear saved runs");
        _line.Visible = false;
    }

    /// <summary>Switches the button off, saying why, when there is nothing to delete.</summary>
    public void Refresh()
    {
        // Working out the plan reads every run's header: only when the saved runs changed since it was last done.
        var stamp = HistoryStamp.Of(RunStore.Shared, expanded: false);
        if (stamp == _checked)
            return;
        _checked = stamp;
        var nothing = _plan == null && RunService.Instance.ClearPlan().Nothing;
        _button.Disabled = nothing;
        _button.TooltipText = nothing ? Tr.Get("Nothing to clear") : "";
    }

    // "Deletes 37 runs and 2 comparisons. 4 pinned runs stay.", with the parts that are zero left out.
    private static string PlanText(ClearPlan plan)
    {
        var runs = Tr.Plural("{0} run", "{0} runs", plan.Delete.Count, Ui.Number(plan.Delete.Count));
        var comparisons = Tr.Plural(
            "{0} comparison",
            "{0} comparisons",
            plan.ComparisonsDelete.Count,
            Ui.Number(plan.ComparisonsDelete.Count)
        );
        var what =
            plan.Delete.Count > 0 && plan.ComparisonsDelete.Count > 0 ? Tr.Format("{0} and {1}", [runs, comparisons])
            : plan.Delete.Count > 0 ? runs
            : comparisons;
        var deletes = Tr.Format("Deletes {0}.", what);
        return plan.PinnedKept > 0
            ? deletes
                + " "
                + Tr.Plural(
                    "{0} pinned run stays.",
                    "{0} pinned runs stay.",
                    plan.PinnedKept,
                    Ui.Number(plan.PinnedKept)
                )
            : deletes;
    }
}
