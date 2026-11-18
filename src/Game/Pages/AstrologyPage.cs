using GamesByMalcs.Melvor2.Entities.Astrology;
using MelvorMods.CombatSimulator2.Model;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game.Pages;

/// <summary>
/// Astrology: a group per constellation with its knowledge, an entry per star in the game's order (minor stars, then
/// major ones). A star can be turned on; one the character has stays, since the game has no call that removes it.
/// </summary>
internal static class AstrologyPage
{
    private static AstrologyActionComponent? Action(MelvorGame game) =>
        Dependencies.IsAvailable(Dependencies.Astrology)
        && game.Entities.TryGetObjectById(StarChange.AstrologyId, out var entity)
        && entity is AstrologyEntity astrology
            ? astrology.Action
            : null;

    private static IEnumerable<(ActiveAstrologyConstellation Active, AstrologyNode Node, bool Minor)> Stars(
        AstrologyActionComponent action
    ) =>
        action.ActiveConstellations.Values.SelectMany(active =>
            active
                .Constellation.MinorNodes.Select(node => (active, node, true))
                .Concat(active.Constellation.MajorNodes.Select(node => (active, node, false)))
        );

    private static bool On((ActiveAstrologyConstellation Active, AstrologyNode Node, bool Minor) star) =>
        star.Minor ? star.Active.IsMinorNodeUnlocked(star.Node) : star.Active.IsMajorNodeUnlocked(star.Node);

    public static void Values(MelvorGame game, Dictionary<ChangeKey, int> values)
    {
        if (Action(game) is not { } action)
            return;
        foreach (var star in Stars(action))
            values[new ChangeKey(ChangeKind.Star, star.Node.Id)] = On(star) ? 1 : 0;
    }

    public static SetupPage Read(MelvorGame game, IReadOnlyDictionary<ChangeKey, int> save)
    {
        if (Action(game) is not { } action)
            return PageReader.Unsupported(SetupPage.Astrology);
        if (action.ActiveConstellations.Count == 0)
            return PageReader.Page(SetupPage.Astrology, PageState.NoData, Note.Of("Not in this build"));
        var gate = PageGates.Unmet(game, StarChange.AstrologyId);
        var groups = new List<PageGroup>();
        foreach (var active in action.ActiveConstellations.Values)
        {
            var constellation = active.Constellation;
            var name = constellation.DisplayName.Text;
            var entries = new List<PageEntry>();
            foreach (var star in Stars(action).Where(s => s.Active == active))
            {
                var key = new ChangeKey(ChangeKind.Star, star.Node.Id);
                var on = On(star) ? 1 : 0;
                var had = PageReader.Saved(save, key, on);
                entries.Add(
                    new PageEntry(
                        star.Node.Id,
                        star.Node.DisplayName.Text,
                        null,
                        EntryKind.Toggle,
                        on,
                        1,
                        had,
                        Targets.Unmet(star.Node.UnlockRequirements),
                        SetupSheets.Describe(ModifierGroup.Astrology, constellation.Id, name, star.Node.Bonuses),
                        had == 1 ? null : key,
                        had == 1 ? Note.Of("The game can't remove a star.") : null
                    )
                );
            }
            groups.Add(
                new PageGroup(
                    constellation.Id,
                    name,
                    constellation.Id,
                    Note.Of(
                        "{0} of {1} stars · {2} knowledge",
                        entries.Count(e => e.Value == 1),
                        entries.Count,
                        (long)active.Knowledge
                    ),
                    entries
                )
            );
        }
        return new SetupPage(SetupPage.Astrology, PageSection.Skills, PageState.Changeable, null, gate, groups);
    }
}
