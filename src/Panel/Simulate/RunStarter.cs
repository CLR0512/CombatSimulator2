using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.Simulate;

/// <summary>
/// Starting runs from the Simulate view: the selection, what the character is fighting, a comparison of two setups
/// or against a saved run, and a what-if with the editor's changes.
/// </summary>
internal sealed class RunStarter(Action<string> message, SetupCheck check, ComparisonFollower follower)
{
    private readonly Action<string> _message = message;
    private readonly SetupCheck _check = check;
    private readonly ComparisonFollower _follower = follower;

    /// <summary>Simulates what the filter shows, less the ignored targets.</summary>
    public void StartSelected()
    {
        if (LiveGame.Find() != null && PanelState.Selection() is { } selection)
            Start([.. selection.ToRun.Select(t => TargetSpec.FromKey(t.Key))]);
    }

    /// <summary>Simulates what the character is fighting, and selects it.</summary>
    public void StartFighting()
    {
        if (LiveGame.Find() is not { } game)
            return;
        if (!Dependencies.IsAvailable(Dependencies.CurrentTarget) || LiveGame.Current(game) is not { } current)
        {
            _message(Tr.Get("Not fighting anything right now."));
            return;
        }
        PanelState.Select(current.Key);
        Start([TargetSpec.FromKey(current.Key)]);
    }

    /// <summary>
    /// Simulates <paramref name="targets"/> with the chosen setup, or compares them as the setup bar says.
    /// </summary>
    public void Start(List<TargetSpec> targets)
    {
        // A second press while the first is checking its setups: the first one's run is on its way.
        if (RunService.Instance.IsStarting)
            return;
        if (LiveGame.Find() is not { } game)
        {
            _message(Tr.Note(LiveGame.NoGame));
            return;
        }
        var prefs = PanelState.Prefs;
        // A run started: Undo would now undo more than the pasted simulation.
        SimulationPage.Forget();
        try
        {
            var request = new RunRequest(
                SetupCheck.Current(),
                targets,
                prefs.Trials,
                prefs.Seed,
                prefs.Copies ?? RunService.Instance.Status.DefaultCopies,
                Fresh: prefs.SimulateFresh,
                StopPrecision: prefs.StopEarly ? prefs.StopPrecision : null
            );
            var b = PanelState.B;
            if (b is { CanChange: false } || (b == null && PanelState.CompareWith == PanelState.SavedRun))
            {
                StartAgainstSaved(request);
                return;
            }
            if (b == null && request.Setup.Changes == null)
            {
                Started(RunService.Instance.Start(request));
                return;
            }
            StartSides(request, b?.ToSetup(""));
        }
        catch (CommandException e)
        {
            Log.Warn("run not started", e);
            if (e.Message.Contains("loadout", StringComparison.OrdinalIgnoreCase))
                _check.SetupError(Note.Text(e.Message));
            else
                _message(e.Message);
        }
    }

    private void Started(Run run)
    {
        PanelState.Ran = (run.Id, PanelState.A, PanelState.B);
        _message("");
        PanelPins.SetCompare(false);
        PanelPins.RunStarted();
    }

    // The setup now against the saved run that is side B, on that run's targets.
    private void StartAgainstSaved(RunRequest request)
    {
        if (PanelState.Baseline is not { } id)
        {
            PanelState.PickBaseline(true);
            return;
        }
        Run baseline;
        try
        {
            baseline = RunStore.Shared.Read(id);
        }
        catch (RunFileException e)
        {
            Log.Warn($"baseline {id} unreadable", e);
            _message(Tr.Note(ComparisonWarnings.BaselineGone));
            PanelState.PickBaseline(true);
            return;
        }
        Compared(RunService.Instance.StartAgainst(baseline, request));
    }

    // One side, or two compared: each side's changes are checked again as it starts, and it runs with the ones that
    // apply. Two sides that come out as one setup run once.
    private void StartSides(RunRequest request, Setup? b)
    {
        if (request.Targets.Count == 0)
            return;
        if (
            b is { Changes: null }
            && request.Setup.Changes == null
            && b.Loadout == request.Setup.Loadout
            && b.IgnoreRequirements == request.Setup.IgnoreRequirements
        )
        {
            SetupTab.SetupTabView.SetUp(SideLetter.B);
            _message(Tr.Get("No changes yet: change a level, an item, food, a prayer or a spell."));
            return;
        }
        RunService.Instance.StartSides(
            request,
            b,
            start =>
            {
                if (start.A != null)
                    PanelState.SetCheck(SideLetter.A, start.A);
                if (start.B != null)
                    PanelState.SetCheck(SideLetter.B, start.B);
                if (start.Comparison is { } comparison)
                {
                    Compared(comparison);
                    return;
                }
                if (start.SidesSame)
                    _message(Tr.Get("Both sides are the same setup, so it ran once."));
                else
                    _message("");
                PanelState.Ran = (start.Run!.Id, PanelState.A, PanelState.B);
                PanelPins.SetCompare(false);
                PanelPins.RunStarted();
            },
            error => _message(error.Message)
        );
    }

    private void Compared(ComparedRuns comparison)
    {
        PanelState.Ran = (comparison.A.Id, PanelState.A, PanelState.B);
        _follower.Seen(comparison);
        _message("");
        PanelPins.SetCompare(false);
        PanelState.ShowComparison(true);
    }

    /// <summary>Cancels the active run, if any.</summary>
    public static void Cancel()
    {
        if (!RunService.Instance.IsActive)
            return;
        // Best loadouts (one master or every one) is what the button cancels while it runs.
        if (RunService.Instance.BestLoadouts is { IsActive: true })
        {
            RunService.Instance.CancelBestLoadouts();
            return;
        }
        RunService.Instance.Cancel();
    }
}
