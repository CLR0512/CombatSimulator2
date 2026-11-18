using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Settings;

/// <summary>The motion levels as three buttons, the one in use pressed.</summary>
internal static class MotionRow
{
    /// <summary>Adds the Motion setting to <paramref name="section"/>.</summary>
    public static Segmented Add(Godot.VBoxContainer section)
    {
        var row = new Segmented(
            "Cs2Motion",
            [
                (MotionTimes.Name(MotionLevel.Full), Tr.Get("Full"), "Cs2MotionFull"),
                (MotionTimes.Name(MotionLevel.Reduced), Tr.Get("Reduced"), "Cs2MotionReduced"),
                (MotionTimes.Name(MotionLevel.Off), Tr.Get("Off"), "Cs2MotionOff"),
            ],
            id =>
            {
                if (MotionTimes.TryParse(id, out var level))
                    MotionChoice.Set(level);
            }
        );
        Show(row);
        SettingRow.Setting(
            section,
            Tr.Get("Motion"),
            Tr.Get("Reduced: short fades only. Off: nothing moves."),
            row.Root
        );
        return row;
    }

    /// <summary>Presses the level in use, which a command or the game's own setting may have changed.</summary>
    public static void Show(Segmented row) => row.Show(MotionTimes.Name(MotionChoice.Level));
}
