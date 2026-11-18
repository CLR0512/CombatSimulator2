using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.SetupTab;

/// <summary>
/// Keeps each side checked as the panel holds it: a check copy reads the side's gear, stats and pages with its
/// changes. A side is checked 300 ms after its last edit, so a run of clicks costs one check; its latest answer stays
/// shown meanwhile. Side A alone with changes is also checked without them: the Effect measures it against that. An
/// answer for a side edited since is dropped. Runs once a frame while the window is open; returns at once while
/// nothing changed. Main thread.
/// </summary>
internal static class SideChecker
{
    private const long Settle = 300;

    // Per side: the side last seen and since when, the side last asked for (a failed check is not asked again until the
    // side changes), and the latest answer with the base it was for.
    private sealed class Watch
    {
        public SideState? Seen;
        public long SeenAt;
        public SideState? Asked;
        public int AskedEpoch = -1;
        public int CheckedEpoch = -1;
        public SideCheck? Latest;
        public SideState? LatestFor;
        public string? LatestBase;
    }

    private static readonly Watch[] _sides = [new(), new()];
    private static readonly Watch _baseOfA = new();
    private static int _epoch;
    private static int _version = -1;
    private static bool _settling;

    /// <summary>Checks both sides again: the live character may have changed (the window opened, a run ended).</summary>
    public static void Recheck()
    {
        _epoch++;
        _version = -1;
    }

    /// <summary>
    /// The latest check of <paramref name="side"/>'s base, for its changes or for earlier ones; null before the first.
    /// </summary>
    public static SideCheck? Latest(SideLetter letter, SideState side)
    {
        var watch = _sides[(int)letter];
        return watch.LatestBase == BaseKey(side) ? watch.Latest : null;
    }

    /// <summary>
    /// Whether the two sides come out as one setup: the same base and requirements, and the same changes once each
    /// side's are checked (a change that changes nothing drops out). Unknown until both checks are in, so false.
    /// </summary>
    public static bool SidesSame()
    {
        var a = PanelState.A;
        if (PanelState.B is not { CanChange: true } b || !a.Base.Equals(b.Base))
            return false;
        if (a.IgnoreRequirements != b.IgnoreRequirements)
            return false;
        var appliedA = a.Changes.IsEmpty ? ChangeList.Empty : PanelState.CheckA?.Changes.Applied;
        var appliedB = b.Changes.IsEmpty ? ChangeList.Empty : PanelState.CheckB?.Changes.Applied;
        return appliedA != null && appliedA.Equals(appliedB);
    }

    /// <summary>Side A without its changes, checked while A stands alone with changes; null otherwise.</summary>
    public static SideCheck? BaseOfA =>
        PanelState.A is var a && _baseOfA.LatestBase == BaseKey(a) && !a.Changes.IsEmpty ? _baseOfA.Latest : null;

    /// <summary>Whether a side's check for its changes as they are now is still to come.</summary>
    public static bool Checking(SideLetter letter) =>
        (letter == SideLetter.A ? PanelState.CheckA : PanelState.CheckB) == null;

    public static void OnFrame()
    {
        if (PanelState.Version == _version && !_settling)
            return;
        _version = PanelState.Version;
        _settling = false;
        if (LiveGame.Find() == null)
            return;
        // A run holds the check copies' work back: look again on the frames after it.
        if (RunService.Instance.IsActive)
        {
            _settling = true;
            return;
        }
        var a = PanelState.A;
        Check(SideLetter.A, a, _sides[0]);
        if (PanelState.B is { CanChange: true } b)
            Check(SideLetter.B, b, _sides[1]);
        else if (!a.Changes.IsEmpty)
            Check(null, a.WithChanges(ChangeList.Empty), _baseOfA);
    }

    // A side's base and requirements: a check for earlier changes on the same base can stand in until the next answers.
    private static string BaseKey(SideState side) => side.Base.Text + "|" + side.IgnoreRequirements;

    // Asks for a check of the side once it has settled; null letter is A's base, kept apart from the panel's checks.
    private static void Check(SideLetter? letter, SideState side, Watch watch)
    {
        var now = Environment.TickCount64;
        if (!Equals(side, watch.Seen))
        {
            watch.Seen = side;
            watch.SeenAt = now;
        }
        var have = letter switch
        {
            SideLetter.A => PanelState.CheckA,
            SideLetter.B => PanelState.CheckB,
            _ => watch.LatestBase == BaseKey(side) ? watch.Latest : null,
        };
        if (have != null && watch.CheckedEpoch == _epoch)
            return;
        // The same side again (a comparison cleared and the same loadout picked): its answer stands, nothing to ask.
        if (Equals(side, watch.LatestFor) && watch.CheckedEpoch == _epoch)
        {
            if (letter is { } same && watch.Latest is { } known)
            {
                PanelState.SetCheck(same, known);
                // A change tried again is refused again: dropped and said once more.
                PanelState.DropRefused(same, side, known);
            }
            return;
        }
        if (Equals(side, watch.Asked) && watch.AskedEpoch == _epoch)
            return;
        // The first check of a side goes at once; an edit waits for the next 300 ms without one.
        if (watch.Latest != null && now - watch.SeenAt < Settle)
        {
            _settling = true;
            return;
        }
        watch.Asked = side;
        watch.AskedEpoch = _epoch;
        var epoch = _epoch;
        try
        {
            RunService.CheckSide(
                side.ToSetup(""),
                side.Changes,
                (check, error) =>
                {
                    if (!Equals(watch.Asked, side) || watch.AskedEpoch != epoch)
                        return;
                    if (check == null)
                    {
                        // The game wraps the cause of a failed copy in a generic "JSON is invalid": keep the whole chain.
                        Log.Warn($"side {letter?.ToString() ?? "A base"} not checked: {error}");
                        return;
                    }
                    watch.Latest = check;
                    watch.LatestFor = side;
                    watch.LatestBase = BaseKey(side);
                    watch.CheckedEpoch = epoch;
                    if (letter is { } l && Equals(l == SideLetter.A ? PanelState.A : PanelState.B, side))
                    {
                        PanelState.SetCheck(l, check);
                        PanelState.DropRefused(l, side, check);
                    }
                    else
                        PanelState.Notify();
                }
            );
        }
        catch (CommandException e)
        {
            Log.Warn($"side {letter?.ToString() ?? "A base"} not checked", e);
        }
    }
}
