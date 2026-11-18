using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.SetupTab;

/// <summary>
/// What a side has on that it can't use: worn items and active prayers whose requirements its levels or unlocks don't
/// meet, as its check found them. The game only checks requirements when something is put on, so a lowered level
/// leaves them on; keeping to requirements, such a side doesn't run until they come off. Ignoring requirements, none.
/// </summary>
internal static class Unwearable
{
    /// <summary>Each item or prayer the side can't use, by id, with what it needs; empty when none or unchecked.</summary>
    public static IReadOnlyList<(string Id, IReadOnlyList<Note> Unmet)> Of(SideState side, SideCheck? check)
    {
        if (side.IgnoreRequirements || check?.Changes.After is not { } after)
            return [];
        return
        [
            .. after
                .Slots.Concat(after.Toolbelt)
                .Where(s => s.Item != null && s.Unmet.Count > 0)
                .Select(s => (s.Item!, s.Unmet))
                .DistinctBy(s => s.Item1, StringComparer.Ordinal), // a two-handed weapon fills two slots
            .. after.Prayers.Where(p => p.On && p.Locked.Count > 0).Select(p => (p.Prayer, p.Locked)),
        ];
    }

    /// <summary>Why the side can't run as it is, naming what it can't use; null when it can.</summary>
    public static string? Problem(SideState side, SideCheck? check, string? letter)
    {
        var found = Of(side, check);
        if (found.Count == 0)
            return null;
        var names = PanelState.Names();
        var list = string.Join(", ", found.Select(f => $"{names(f.Id)} ({Tr.Notes(f.Unmet, "; ")})"));
        // Translators: Why a setup can't be simulated; {0} lists what it has on but can't use, each with what it needs.
        var why = Tr.Format(
            "Can't be used at these levels: {0}. Take them off, or switch on Ignore requirements.",
            list
        );
        // Translators: Side letter before a reason the side can't be simulated: "Side B: …".
        return letter == null ? why : Tr.Format("Side {0}: {1}", [letter, why]);
    }
}
