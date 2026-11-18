using GamesByMalcs.Melvor2.Entities.Astrology;
using MelvorMods.CombatSimulator2.Game.Pages;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// An astrology star, unlocked through its constellation's own call. The game has no call that removes one. Copies
/// only.
/// </summary>
internal static class StarChange
{
    public const string AstrologyId = "MelvorBase:Astrology";

    // Enough study steps for any constellation at one knowledge a step; a guard against a threshold never reached.
    private const int StudyStepsAtMost = 2_000_000;

    public static ChangeOutcome Apply(Copy copy, Change change, bool ignoreRequirements)
    {
        if (
            !copy.Game.Entities.TryGetObjectById(AstrologyId, out var entity) || entity is not AstrologyEntity astrology
        )
            return Changes.Missing(change, null);
        if (!copy.Game.Data.TryGetObject<AstrologyNode>(change.Subject!, out var node))
            return Changes.Missing(change, null);
        var action = astrology.Action;
        var minor = action.TryGetMinorNodeConstellation(node, out var constellation);
        if (!minor && !action.TryGetMajorNodeConstellation(node, out constellation))
            return Changes.Missing(change, null);
        var active = action.ActiveConstellations[constellation!];
        var unlocked = minor ? active.IsMinorNodeUnlocked(node) : active.IsMajorNodeUnlocked(node);
        var from = unlocked ? Change.On : Change.Off;
        if (unlocked)
            return new(change, from, ChangeState.NoChange, []);

        // The page's lock first, then the star's own requirements, then how many stars the knowledge allows.
        var gate = PageGates.Unmet(copy.Game, AstrologyId);
        var reasons = Targets.Unmet(node.UnlockRequirements);
        reasons.InsertRange(0, gate);
        if (reasons.Count > 0 && !ignoreRequirements)
            return Changes.Refused(change, from, [.. reasons]);
        if (!minor && active.MajorNodeUnlockCount >= active.AvailableMajorNodeCount)
            return Changes.Refused(
                change,
                from,
                Note.Of(
                    "needs {0} more minor stars in this constellation",
                    (active.MajorNodeUnlockCount + 1) * constellation!.MinorNodesPerMajorNode
                        - active.MinorNodeUnlockCount
                )
            );
        if (minor && active.MinorNodeUnlockCount >= active.AvailableMinorNodeCount)
        {
            var needed = KnowledgeFor(constellation!, active.MinorNodeUnlockCount + 1);
            if (!ignoreRequirements || needed == null)
                return Changes.Refused(
                    change,
                    from,
                    needed is { } knowledge
                        ? Note.Of("needs {0} knowledge", knowledge)
                        : Note.Of("no more stars can be unlocked in this constellation")
                );
            // Knowledge is no requirement the game can be told to ignore: it is raised as studying raises it.
            for (
                var step = 0;
                step < StudyStepsAtMost && active.MinorNodeUnlockCount >= active.AvailableMinorNodeCount;
                step++
            )
            {
                var before = active.Knowledge;
                active.AddStudyKnowledge();
                if (active.Knowledge <= before)
                    break;
            }
            if (active.MinorNodeUnlockCount >= active.AvailableMinorNodeCount)
                return Changes.Refused(change, from, Note.Of("needs {0} knowledge", needed.Value));
            reasons.Add(Note.Of("needs {0} knowledge", needed.Value));
        }
        bool applied;
        using (
            reasons.Count > 0 ? RequirementsIgnored.Enter(node.UnlockRequirements) : default(RequirementsIgnored.Scope?)
        )
            applied = minor ? active.UnlockMinorNode(node) : active.UnlockMajorNode(node);
        if (!applied)
            return Changes.Refused(change, from, Note.Of("the game didn't unlock it"));
        return reasons.Count > 0
            ? new(change, from, ChangeState.RequirementIgnored, reasons)
            : new(change, from, ChangeState.Accepted, []);
    }

    // The knowledge at which the constellation allows its count-th minor star; null past its last one.
    private static int? KnowledgeFor(AstrologyConstellation constellation, int count) =>
        count <= constellation.MinorNodeUnlockKnowledge.Count
            ? constellation.MinorNodeUnlockKnowledge[count - 1]
            : null;
}
