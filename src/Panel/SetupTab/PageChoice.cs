using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.SetupTab;

/// <summary>
/// Entries that are one of many under a single change (the potion, the bonfire's tier): each is a toggle, ticking one
/// picks it and unticks the others, and unticking the picked one leaves none (no potion, the bonfire out).
/// </summary>
internal static class PageChoice
{
    public static bool Is(ChangeKey key) => key.Kind is ChangeKind.Potion or ChangeKind.Bonfire;

    /// <summary>1 when the side's change picks <paramref name="entry"/>.</summary>
    public static int Value(Change change, PageEntry entry) => change.Value == entry.Id ? 1 : 0;

    /// <summary>
    /// The side's changes with <paramref name="entry"/> picked (value above 0) or with none picked; without the
    /// change when that is the character's own choice.
    /// </summary>
    public static ChangeList With(SetupPage page, ChangeList changes, PageEntry entry, int value)
    {
        var key = entry.Change!.Value;
        var none = key.Kind == ChangeKind.Bonfire ? Change.Out : Change.None;
        var picked = value > 0 ? entry.Id : none;
        var saved =
            page.Groups.SelectMany(g => g.Entries).FirstOrDefault(e => e.Change == key && e.SaveValue > 0)?.Id ?? none;
        return picked == saved ? changes.Without(key) : changes.With(new Change(key.Kind, key.Subject, picked));
    }
}
