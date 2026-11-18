using System.Globalization;
using GamesByMalcs.Melvor2.Components.MasteryGuild;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// A mastery guild's rank, reached as the game reaches it: by completing the guild's own tasks, whose points make
/// the rank. Points are never written: the game counts them from the tasks. Copies only.
/// </summary>
internal static class GuildChange
{
    /// <summary>The guild of the skill <paramref name="skillId"/> in <paramref name="game"/>; null when it has none.</summary>
    public static MasteryGuildComponent? Guild(GamesByMalcs.Melvor2.Game game, string skillId) =>
        game.Entities.TryGetObjectById(skillId, out var entity)
        && entity is GamesByMalcs.Melvor2.Entities.GameEntity skill
            ? skill.Components.Values.OfType<MasteryGuildComponent>().FirstOrDefault()
            : null;

    // A task a call can complete or put back: one with a goal and nothing else to wait for.
    private static bool ByGoalOnly(ActiveMasteryGuildTask task) =>
        task is { ActiveGoal: not null, ConditionGoal: null, TriggerGoal: null };

    public static ChangeOutcome Apply(Copy copy, Change change)
    {
        if (Guild(copy.Game, change.Subject!) is not { } guild)
            return Changes.Missing(change, null);
        var ranks = guild._orderedRanks;
        var current = ranks.IndexOf(guild.CurrentRank);
        var from = current.ToString(CultureInfo.InvariantCulture);
        var target = change.Number!.Value;
        if (target == current)
            return new(change, from, ChangeState.NoChange, []);
        if (target >= ranks.Count)
            return Changes.Refused(change, from, Note.Of("this guild has ranks 0–{0}", ranks.Count - 1));
        var tasks = guild.ActiveTasks.Values.Where(ByGoalOnly).ToList();
        if (target > current)
        {
            // In the game's task order, as many as the rank needs and no more.
            var open = tasks.Where(t => !t.IsComplete).ToList();
            var needed = guild.GetPointsForRank(ranks[target]);
            var points = guild.CurrentPoints;
            var complete = 0;
            while (complete < open.Count && ranks.IndexOf(guild.GetRankForPoints(points)) < target)
                points += open[complete++].Category.BasePoints;
            if (ranks.IndexOf(guild.GetRankForPoints(points)) != target)
                return Changes.Refused(
                    change,
                    from,
                    Note.Of("rank needs {1} points, completable tasks only give {0}", points, needed)
                );
            foreach (var task in open.Take(complete))
                task.ActiveGoal!.SetComplete();
        }
        else
        {
            // The latest completed first, until the points fall below the next rank's.
            var done = tasks.Where(t => t.IsComplete).Reverse().ToList();
            var points = guild.CurrentPoints;
            var reset = 0;
            while (reset < done.Count && ranks.IndexOf(guild.GetRankForPoints(points)) > target)
                points -= done[reset++].Category.BasePoints;
            if (ranks.IndexOf(guild.GetRankForPoints(points)) != target)
                return Changes.Refused(
                    change,
                    from,
                    Note.Of(
                        "rank needs {1} points taken back, undoable tasks only give {0}",
                        guild.CurrentPoints - points,
                        guild.CurrentPoints - guild.GetPointsForRank(ranks[target + 1]) + 1
                    )
                );
            foreach (var task in done.Take(reset))
                task.ActiveGoal!.Reset();
        }
        var reached = ranks.IndexOf(guild.CurrentRank);
        return reached == target
            ? new(change, from, ChangeState.Accepted, [])
            : Changes.Refused(change, from, Note.Of("the game kept rank {0}", reached));
    }
}
