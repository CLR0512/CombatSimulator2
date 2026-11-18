using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.Dev;

// What a bench changes of the player's own state to measure a view at its fullest, each put back by Restore: the
// target filter, the two sides, and the strip.
internal static partial class PanelDrive
{
    private static TargetFilter? _filter;
    private static (SideState A, SideState? B)? _sides;
    private static bool _everyEntry;

    /// <summary>Shows every target of the game in the strip, as the All filter does.</summary>
    public static void ShowEveryTarget()
    {
        _filter ??= PanelState.Prefs.Filter;
        PanelState.SetFilter(new TargetFilter { CanFightOnly = false });
    }

    /// <summary>Opens or closes the strip; false when the Simulate view isn't built.</summary>
    public static bool ShowStrip(bool open)
    {
        if (Cs2Modal.SimulateView is not { Ready: true } view)
            return false;
        view.Strip.SetOpen(open);
        return true;
    }

    /// <summary>Leaves side A alone, so "+ Compare…" and its picker can be opened.</summary>
    public static void OneSide()
    {
        _sides ??= (PanelState.A, PanelState.B);
        PanelState.SetSide(SideLetter.B, null);
    }

    /// <summary>Gives side A a side B to stand beside, a copy of itself, when it has none.</summary>
    public static void TwoSides()
    {
        _sides ??= (PanelState.A, PanelState.B);
        if (PanelState.B == null)
            PanelState.SetSide(SideLetter.B, PanelState.A);
    }

    /// <summary>
    /// Edits side A as a level's − or + does: Strength at <paramref name="level"/>, beside the changes it had.
    /// </summary>
    public static void EditLevel(int level)
    {
        _sides ??= (PanelState.A, PanelState.B);
        var edit = ChangeList.Parse(
            "level=Strength:" + level.ToString(System.Globalization.CultureInfo.InvariantCulture)
        );
        PanelState.SetSide(SideLetter.A, PanelState.A.WithChanges(PanelState.A.Changes.With(edit[0])));
    }

    /// <summary>Lists every entry of the Setup page shown, as its "All" filter does.</summary>
    public static void ShowEveryEntry()
    {
        _everyEntry = true;
        PressFilter("Cs2PageFilter0");
    }

    private static void PressFilter(string name)
    {
        if (Cs2Modal.Root?.FindChild(name, true, false) is Godot.Button { ButtonPressed: false } button)
        {
            button.ButtonPressed = true;
            button.EmitSignal(Godot.BaseButton.SignalName.Pressed);
        }
    }

    private static void RestoreState()
    {
        if (_everyEntry)
            PressFilter("Cs2PageFilter1");
        _everyEntry = false;
        if (_filter is { } filter)
            PanelState.SetFilter(filter);
        if (_sides is var (a, b))
        {
            PanelState.SetSide(SideLetter.A, a);
            PanelState.SetSide(SideLetter.B, b);
        }
        _filter = null;
        _sides = null;
        ShowStrip(false);
    }
}
