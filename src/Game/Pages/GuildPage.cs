using GamesByMalcs.Melvor2.Components.MasteryGuild;
using GamesByMalcs.Melvor2.Entities;
using MelvorMods.CombatSimulator2.Model;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game.Pages;

/// <summary>
/// The mastery guilds: a group per skill that has one, with its rank as the one entry that can be changed, then
/// each of the guild's bonuses with the rank or points it needs and what it gives.
/// </summary>
internal static class GuildPage
{
    private static IEnumerable<(GameEntity Skill, MasteryGuildComponent Guild)> Guilds(MelvorGame game) =>
        Dependencies.IsAvailable(Dependencies.Guild)
            ? game
                .Entities.GetAllObjects()
                .SelectMany(entity =>
                    entity.Components.Values.OfType<MasteryGuildComponent>().Take(1).Select(guild => (entity, guild))
                )
            : [];

    public static void Values(MelvorGame game, Dictionary<ChangeKey, int> values)
    {
        foreach (var (skill, guild) in Guilds(game))
            values[new ChangeKey(ChangeKind.Guild, skill.Id)] = guild._orderedRanks.IndexOf(guild.CurrentRank);
    }

    public static SetupPage Read(MelvorGame game, IReadOnlyDictionary<ChangeKey, int> save)
    {
        if (!Dependencies.IsAvailable(Dependencies.Guild))
            return PageReader.Unsupported(SetupPage.Guild);
        var groups = new List<PageGroup>();
        foreach (var (skill, guild) in Guilds(game))
        {
            var ranks = guild._orderedRanks;
            if (ranks.Count == 0)
                continue;
            var name = skill.DisplayName.Text;
            var key = new ChangeKey(ChangeKind.Guild, skill.Id);
            var rank = ranks.IndexOf(guild.CurrentRank);
            var entries = new List<PageEntry>
            {
                new(
                    skill.Id,
                    guild.CurrentRank.DisplayName.Text,
                    skill.Id,
                    EntryKind.Rank,
                    rank,
                    ranks.Count - 1,
                    PageReader.Saved(save, key, rank),
                    [],
                    [],
                    key,
                    null,
                    Names: [.. ranks.Select(r => r.DisplayName.Text)]
                ),
            };
            foreach (var bonus in guild.Bonuses)
            {
                var gives = bonus.Rewards.Bonuses is { } bonuses
                    ? SetupSheets.Describe(ModifierGroup.Mastery, bonus.Id, name, bonuses)
                    : [];
                // A bonus that needs a rank is shown had or not from the side's own rank, as it is edited.
                var after = bonus
                    .Requirements.Conditions.OfType<MasteryGuildRankCondition>()
                    .Where(c => ranks.Contains(c.Rank))
                    .Select(c => new EntryNeed(Note.Text(Targets.Describe(c)), key, ranks.IndexOf(c.Rank)))
                    .ToList();
                var hinged = after.Select(n => n.Text.English()).ToHashSet(StringComparer.Ordinal);
                entries.Add(
                    new PageEntry(
                        bonus.Id,
                        bonus.DisplayName.Text,
                        null,
                        EntryKind.Info,
                        bonus.Requirements.AreMet ? 1 : 0,
                        1,
                        bonus.Requirements.AreMet ? 1 : 0,
                        [.. Targets.Unmet(bonus.Requirements).Where(n => !hinged.Contains(n.English()))],
                        gives,
                        null,
                        null,
                        gives.Count > 0,
                        after
                    )
                );
            }
            groups.Add(
                new PageGroup(
                    skill.Id,
                    name,
                    skill.Id,
                    Note.Of("{0} of {1} points", guild.CurrentPoints, guild.TotalTaskPoints),
                    entries
                )
            );
        }
        return groups.Count == 0
            ? PageReader.Page(SetupPage.Guild, PageState.NoData, Note.Of("Not in this build"))
            : new SetupPage(SetupPage.Guild, PageSection.Skills, PageState.Changeable, null, [], groups);
    }
}
