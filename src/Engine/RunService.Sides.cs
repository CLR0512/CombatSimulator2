using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine;

// A side is a setup: its base, its changes and its requirements flag. Either side may carry changes.
internal sealed partial class RunService
{
    private readonly SideChecks _sideChecks = new();
    private readonly SetupSheetCache _sheets = new();

    /// <summary>
    /// Main thread: reads the stats and modifiers of each of <paramref name="sides"/> from a check copy, as a run
    /// would fight with it; <paramref name="done"/> runs on the main thread with the sheets in the same order, or the
    /// error. A sheet read before is given again at once while the setup is unchanged in the live game.
    /// </summary>
    public static void ReadSetup(IReadOnlyList<Setup> sides, Action<IReadOnlyList<SetupSheet>?, Exception?> done) =>
        Instance._sheets.Read(sides, done);

    /// <summary>Forgets the sheets and checks read so far (their text is in the language they were read in).</summary>
    public void ClearSheets()
    {
        _sheets.Clear();
        _sideChecks.Clear();
    }

    /// <summary>
    /// Main thread: checks <paramref name="changes"/> on the side's base (<paramref name="side"/>'s loadout and
    /// requirements flag; its own changes are not read) in a check copy, without running; <paramref name="done"/> runs
    /// on the main thread with each change's outcome and the stats and modifiers of the setup they leave, or with the
    /// error. A check made before is given again at once while the setup is unchanged in the live game.
    /// </summary>
    public static void CheckSide(Setup side, ChangeList changes, Action<SideCheck?, Exception?> done)
    {
        ArgumentNullException.ThrowIfNull(side);
        MainThread.Assert("RunService.CheckSide");
        var game = LiveGame.Require();
        Instance._sideChecks.Check(side, CheckLoadout(side, () => LiveGame.Loadouts(game)), changes, done);
    }

    /// <summary>
    /// Starts one side, or two compared. Each side's changes (<see cref="Setup.Changes"/> of
    /// <paramref name="a"/>'s setup and of <paramref name="b"/>) are checked on that side's own base first, and the
    /// side runs with the ones that apply; every change as checked is kept on the side's run. Two sides that come
    /// out as the same setup start a single run. <paramref name="started"/> runs on the main thread with what was
    /// started. Throws while a run is active; a failed check or a refused start comes to <paramref name="failed"/>.
    /// </summary>
    public void StartSides(RunRequest a, Setup? b, Action<SidesStart> started, Action<Exception> failed)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(started);
        ArgumentNullException.ThrowIfNull(failed);
        MainThread.Assert("RunService.StartSides");
        if (IsStarting)
            throw new CommandException("a run is starting: wait for it");
        if (IsActive)
            throw new CommandException($"{Current!.Id} is still running: cancel it first (cs2 cancel)");
        // The checks take a moment before anything runs: a second start meanwhile would pass the check above and then
        // be refused by the run this one starts.
        IsStarting = true;
        void Failed(Exception e)
        {
            IsStarting = false;
            failed(e);
        }
        SidesStart.CheckThen(
            a.Setup,
            checkA =>
                SidesStart.CheckThen(
                    b,
                    checkB =>
                    {
                        IsStarting = false;
                        try
                        {
                            started(Begin(a, b, checkA, checkB));
                        }
                        catch (CommandException e)
                        {
                            failed(e);
                        }
                    },
                    Failed
                ),
            Failed
        );
    }

    /// <summary>Whether a start's setups are being checked, before its run begins.</summary>
    public bool IsStarting { get; private set; }

    private SidesStart Begin(RunRequest a, Setup? b, SideCheck? checkA, SideCheck? checkB)
    {
        var request = a with
        {
            Setup = SidesStart.AsChecked(a.Setup, checkA),
            ChangeOutcomes = checkA != null ? [.. checkA.Changes.Outcomes] : null,
        };
        if (b == null)
            return new(checkA, null, Start(request), null, false);
        var setupB = SidesStart.AsChecked(b, checkB);
        if (SidesStart.Same(request.Setup, setupB))
            return new(checkA, checkB, Start(request), null, true);
        var comparison = StartComparison(request, setupB, checkB != null ? [.. checkB.Changes.Outcomes] : null);
        return new(checkA, checkB, null, comparison, false);
    }
}
