using GamesByMalcs.Melvor2.Components.CombatAreas;
using GamesByMalcs.Melvor2.Entities.Agility;
using GamesByMalcs.Melvor2.Entities.Agility.Statistics;
using MelvorMods.CombatSimulator2.Model;
using MelvorGame = GamesByMalcs.Melvor2.Game;

namespace MelvorMods.CombatSimulator2.Game.Pages;

/// <summary>
/// Agility: what it does for a fight in this build, read from its data. A group per technique holds its level, which
/// a side can set, and the areas and dungeons whose entry needs it; then, read-only, each location's courses with how
/// often they were completed and the shortcuts built.
/// </summary>
internal static class AgilityPage
{
    private const string CompletedStatistic = "MelvorBase:AgilityCourseCompleted";

    public static void Values(MelvorGame game, Dictionary<ChangeKey, int> values)
    {
        if (!Dependencies.IsAvailable(Dependencies.Agility))
            return;
        foreach (var technique in TechniqueChange.Techniques(game))
            values[new ChangeKey(ChangeKind.Technique, technique.Technique.Id)] = technique.Experience.Level;
    }

    public static SetupPage Read(MelvorGame game, IReadOnlyDictionary<ChangeKey, int> save)
    {
        if (!Dependencies.IsAvailable(Dependencies.Agility))
            return PageReader.Unsupported(SetupPage.Agility);
        var techniques = TechniqueChange.Techniques(game);
        if (techniques.Count == 0 || !game.Entities.TryGetObjectById(TechniqueChange.AgilityId, out var entity))
            return PageReader.Page(SetupPage.Agility, PageState.NoData, Note.Of("Not in this build"));
        var areas = game.Data.GetDataRegistryOutput<BaseCombatArea>().GetAllObjects().ToList();
        var groups = techniques.Select(t => TechniqueGroup(game, t, areas, save)).ToList();
        if (entity is AgilityEntity agility)
            groups.AddRange(CourseGroups(game, agility));
        // A technique's bonus is bound to the obstacles it is used on; courses and obstacles carry none.
        var reaches = techniques.Any(t =>
            t.Technique.DifficultyMetBonuses is { } template && CombatReach.Of(template.CreateModifiers(null))
        );
        return new SetupPage(
            SetupPage.Agility,
            PageSection.Skills,
            PageState.Changeable,
            reaches
                ? Note.Of("This build has it; CS2 can't show or change it yet.")
                : Note.Of(
                    "Nothing in Agility changes a fight in this build. Techniques decide which areas and dungeons you can enter."
                ),
            PageGates.Unmet(game, TechniqueChange.AgilityId),
            groups
        );
    }

    private static PageGroup TechniqueGroup(
        MelvorGame game,
        ActiveAgilityTechnique active,
        List<BaseCombatArea> areas,
        IReadOnlyDictionary<ChangeKey, int> save
    )
    {
        var technique = active.Technique;
        var key = new ChangeKey(ChangeKind.Technique, technique.Id);
        var name = technique.DisplayName.Text;
        var level = active.Experience.Level;
        var entries = new List<PageEntry>
        {
            new(
                technique.Id,
                name,
                technique.Id,
                EntryKind.Count,
                level,
                active.Experience.LevelCap,
                PageReader.Saved(save, key, level),
                Targets.Unmet(technique.UnlockRequirements),
                [],
                key,
                null
            ),
        };
        foreach (var area in areas)
        {
            var needs = area
                .UnlockRequirements?.Conditions.OfType<AgilityTechniqueLevelCondition>()
                .Where(c => c.Technique == technique)
                .ToList();
            if (needs is not { Count: > 0 })
                continue;
            var need = needs.MaxBy(c => c.Level)!;
            var open = level >= need.Level ? 1 : 0;
            entries.Add(
                new PageEntry(
                    area.Id,
                    LiveGame.NameOf(game, area.Id),
                    null,
                    // Ticked while the side can enter, as it follows the technique's level; never set itself.
                    EntryKind.Toggle,
                    open,
                    1,
                    open,
                    [],
                    [],
                    null,
                    null,
                    After: [new EntryNeed(Note.Text(Targets.Describe(need)), key, need.Level)],
                    Remark: Note.Of("Needs level {0}", need.Level)
                )
            );
        }
        return new PageGroup(technique.Id, name, technique.Id, null, entries);
    }

    // Read-only: the game's courses by location, as the save holds them.
    private static IEnumerable<PageGroup> CourseGroups(MelvorGame game, AgilityEntity agility)
    {
        var completed = game
            .Data.GetDataRegistryOutput<AgilityCourseStatistic>()
            .GetAllObjects()
            .FirstOrDefault(s => s.Id == CompletedStatistic);
        var courses = agility.Action.Courses;
        foreach (var location in game.Data.GetDataRegistryOutput<AgilityLocation>().GetAllObjects())
        {
            var entries = courses
                .Where(c => c.Key.Location == location)
                .Select(c =>
                {
                    var done = (int)Math.Min(completed?.GetValue(c.Key) ?? 0, int.MaxValue);
                    var shortcuts = c.Value.GetShortcutsCreatedCount();
                    return new PageEntry(
                        c.Key.Id,
                        c.Key.DisplayName.Text,
                        null,
                        EntryKind.Info,
                        done,
                        1,
                        done,
                        [],
                        [],
                        null,
                        null,
                        Remark: Note.Plural("{0} shortcut built", "{0} shortcuts built", shortcuts, shortcuts)
                    );
                })
                .ToList();
            if (entries.Count > 0)
                yield return new PageGroup(
                    location.Id,
                    location.DisplayName.Text,
                    null,
                    Note.Of("Times each course was completed, read only: courses change no fight"),
                    entries
                );
        }
    }
}
