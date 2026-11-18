using GamesByMalcs.Melvor2.Entities.Quests;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// A quest, done the way playing it does: started, each task's goals completed and its costs paid in order, then its
/// rewards claimed, which is the state the game's own quest condition asks for. Quests it needs are done first.
/// Copies only.
/// </summary>
internal static class QuestChange
{
    // A quest's tasks, and the quests before it: far more than any quest has, against a quest that never ends.
    private const int QuestStepsAtMost = 1000;

    /// <summary>The quests of <paramref name="game"/>; null when the build has none.</summary>
    public static QuestsComponent? Quests(GamesByMalcs.Melvor2.Game game) =>
        game.Entities.GetAllObjects().OfType<QuestsEntity>().FirstOrDefault()?.Quests;

    public static ChangeOutcome Apply(Copy copy, Change change, bool ignoreRequirements)
    {
        if (
            Quests(copy.Game) is not { } quests
            || !copy.Game.Data.TryGetObject<Quest>(change.Subject!, out var data)
            || !quests.ActiveQuests.TryGetValue(data, out var quest)
        )
            return Changes.Missing(change, null);
        var from = quest.State == QuestState.Claimed ? Change.Done : Change.Open;
        if (from == change.Value)
            return new(change, from, ChangeState.NoChange, []);
        if (!change.Completes)
        {
            quest.Reset();
            return quest.State == QuestState.Unstarted
                ? new(change, from, ChangeState.Accepted, [])
                : Changes.Refused(change, from, Note.Of("the game kept it done"));
        }
        var ignored = new List<Note>();
        var steps = QuestStepsAtMost;
        if (Complete(copy, quests, quest, ignoreRequirements, ignored, ref steps) is { } problem)
            return Changes.Refused(change, from, problem);
        return ignored.Count > 0
            ? new(change, from, ChangeState.RequirementIgnored, ignored)
            : new(change, from, ChangeState.Accepted, []);
    }

    // Completes the quest and, first, each quest its requirements name; a reason when the game won't.
    private static Note[]? Complete(
        Copy copy,
        QuestsComponent quests,
        ActiveQuest quest,
        bool ignoreRequirements,
        List<Note> ignored,
        ref int steps
    )
    {
        if (quest.State == QuestState.Claimed)
            return null;
        foreach (var needed in quest.Requirements.Conditions.OfType<QuestCompleteCondition>())
        {
            if (
                !needed.IsMet
                && quests.ActiveQuests.TryGetValue(needed.Quest, out var before)
                && Complete(copy, quests, before, ignoreRequirements, ignored, ref steps) is { } earlier
            )
                return earlier;
        }
        var unmet = Targets.Unmet(quest.Requirements);
        if (unmet.Count > 0)
        {
            if (!ignoreRequirements)
                return [.. unmet];
            ignored.AddRange(unmet);
        }
        if (quest.State == QuestState.Unstarted)
            quest.Start();
        while (quest.State == QuestState.InProgress)
        {
            if (
                --steps < 0
                || quest.ActiveSteps.FirstOrDefault(s => s.State == QuestStepState.Active) is not { } step
                || step.ActiveTasks.FirstOrDefault(t => t.State == QuestStepState.Active) is not { } task
            )
                return [Note.Of("the game didn't complete the quest")];
            foreach (var goal in task.ActiveGoals.Where(g => !g.IsComplete).ToList())
                goal.SetComplete();
            if (task.State == QuestStepState.Active && !task.CostsMet && task.Costs is { } costs)
            {
                if (!Supplies.Ingredients(copy, costs, 1) || !quests.ExecuteTaskCosts(task))
                    return [Note.Of("bank full")];
            }
            if (task.State == QuestStepState.Active)
                return [Note.Of("the game didn't complete a task")];
        }
        return quest.State == QuestState.Completed && quests.ClaimRewards(quest)
            ? null
            : [Note.Of("the game didn't give the quest's rewards (bank full)")];
    }
}
