using GamesByMalcs.Melvor2.Entities.Quests;
using MelvorMods.CombatSimulator2.Model;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game.Pages;

/// <summary>
/// The quests, a group per quest category in the game's order. A quest is done or not; done means its rewards are
/// claimed, which is what the game's own conditions ask for.
/// </summary>
internal static class QuestsPage
{
    private static int Done(ActiveQuest quest) => quest.State == QuestState.Claimed ? 1 : 0;

    public static void Values(MelvorGame game, Dictionary<ChangeKey, int> values)
    {
        if (!Dependencies.IsAvailable(Dependencies.Quests) || QuestChange.Quests(game) is not { } quests)
            return;
        foreach (var (quest, active) in quests.ActiveQuests)
            values[new ChangeKey(ChangeKind.Quest, quest.Id)] = Done(active);
    }

    public static SetupPage Read(MelvorGame game, IReadOnlyDictionary<ChangeKey, int> save)
    {
        if (!Dependencies.IsAvailable(Dependencies.Quests))
            return PageReader.Unsupported(SetupPage.Quests);
        if (QuestChange.Quests(game) is not { } quests || quests.ActiveQuests.Count == 0)
            return PageReader.Page(SetupPage.Quests, PageState.NoData, Note.Of("Not in this build"));
        var groups = new List<PageGroup>();
        foreach (var (category, active) in quests.Categories)
        {
            var entries = new List<PageEntry>();
            foreach (var quest in active.Quests)
            {
                var key = new ChangeKey(ChangeKind.Quest, quest.Data.Id);
                var done = Done(quest);
                // A quest needing another is shown met or not from the side's own ticks; the rest as the game reads.
                var after = (quest.Requirements?.Conditions ?? [])
                    .OfType<QuestCompleteCondition>()
                    .Select(c => new EntryNeed(
                        Note.Text(Targets.Describe(c)),
                        new ChangeKey(ChangeKind.Quest, c.Quest.Id)
                    ))
                    .ToList();
                var hinged = after.Select(n => n.Text.English()).ToHashSet(StringComparer.Ordinal);
                entries.Add(
                    new PageEntry(
                        quest.Data.Id,
                        quest.DisplayName.Text,
                        null,
                        EntryKind.Toggle,
                        done,
                        1,
                        PageReader.Saved(save, key, done),
                        [.. Targets.Unmet(quest.Requirements).Where(n => !hinged.Contains(n.English()))],
                        [],
                        key,
                        null,
                        false,
                        after,
                        Targets.Plain(quest.Data.Description?.Text ?? "") is { Length: > 0 } text ? text : null
                    )
                );
            }
            if (entries.Count > 0)
                groups.Add(
                    new PageGroup(
                        category.Id,
                        active.DisplayName.Text,
                        category.Id,
                        Note.Of("{0} of {1} done", entries.Count(e => e.Value == 1), entries.Count),
                        entries
                    )
                );
        }
        return new SetupPage(SetupPage.Quests, PageSection.Account, PageState.Changeable, null, [], groups);
    }
}
