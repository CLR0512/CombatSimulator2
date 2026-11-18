using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Settings;

/// <summary>Tags under a saved run's name, in the risky colour, each saying why on hover.</summary>
internal static class RunMarks
{
    /// <summary>"another game build", for a run made on <paramref name="gameBuild"/> when that isn't this one.</summary>
    public static Label? AnotherBuild(string gameBuild) =>
        gameBuild == LiveGame.GameBuild
            ? null
            : Mark(
                Tr.Get("another game build"),
                Tr.Format("The run comes from game build {0}; this is {1}.", [gameBuild, LiveGame.GameBuild]),
                ""
            );

    /// <summary>"not checked", for a run made on a game build CS2 wasn't checked with.</summary>
    public static Label Unchecked(string name) =>
        Mark(
            // Translators: A saved run made on a game version CS2 wasn't checked with.
            Tr.Get("not checked"),
            Tr.Get("Made on a game version CS2 wasn't checked with. Results may be wrong."),
            name
        );

    private static Label Mark(string text, string why, string name)
    {
        var mark = Ui.Label(text, TextStyle.Tiny, name);
        mark.Modulate = Look.Risky;
        mark.TooltipText = why;
        mark.MouseFilter = Control.MouseFilterEnum.Stop;
        return mark;
    }
}
