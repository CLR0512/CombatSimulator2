using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Slayer;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Slayer;

/// <summary>
/// A Slayer master's row in the results: named as its tasks, its line under the name its monsters and what blocks
/// would do, its deaths over its time.
/// </summary>
internal static class MasterRowLine
{
    /// <summary>"Kuren's tasks" and "Slayer master"; null for any other target.</summary>
    public static (string Name, string Area)? Names(string key, string? name)
    {
        if (!Ids.IsMaster(key))
            return null;
        var game = LiveGame.Find();
        name ??= game != null ? LiveGame.TargetName(game, TargetSpec.FromKey(key)) : Ids.Brief(Ids.MasterOf(key));
        // Translators: A Slayer master as a target in the results; {0} is the master's name.
        return (Tr.Format("{0}'s tasks", name), Tr.Get("Slayer master"));
    }

    /// <summary>
    /// The line under a master's name: how many monsters, then the suggestion's gain ("Best blocks: +38%") or
    /// "Blocks: as good".
    /// </summary>
    public static string Line(Run run, TargetResult master, string metric)
    {
        var count = MasterParts.Counted(master).Count;
        var parts = new List<string>
        {
            Tr.Get("Slayer master"),
            Tr.Plural("{0} monster", "{0} monsters", count, count),
        };
        if (RunCache.Blocks(run, master.Key, metric) is { } suggestion)
            parts.Add(
                suggestion.AsGood ? Tr.Get("Blocks: as good")
                : suggestion.Best?.Gain is { } gain ? Tr.Format("Best blocks: {0}", Words.Gain(gain.Point))
                : Tr.Get("Blocks help")
            );
        return string.Join(" · ", parts);
    }

    /// <summary>A master's line and deaths badge on its row; nothing for any other target.</summary>
    public static void Fill(Label area, PanelContainer deaths, Run run, TargetResult? result)
    {
        if (result is not { Kind: TargetKind.Master } master)
            return;
        Ui.SetClipped(area, Line(run, master, PanelState.Prefs.Metric));
        if (Metrics.Estimate(master, Metrics.Deaths, TimeUnit.Hour) is { } perHour)
            Badge.SetPerHour(deaths, perHour.Point);
    }
}
