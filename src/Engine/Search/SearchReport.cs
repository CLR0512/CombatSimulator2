using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Engine.Search;

/// <summary>A stage's output, called on worker threads.</summary>
internal interface ISearchReport
{
    /// <summary>
    /// One candidate's result; every candidate of a stage gets exactly one unless the stage is cancelled or fails.
    /// <paramref name="left"/> says why a candidate wasn't fought, beside a refused result.
    /// <paramref name="mainSkill"/> is the skill the set trains, which <c>xp:main</c> stands for.
    /// </summary>
    void CandidateDone(int index, TargetResult result, Note? left, string? mainSkill);

    void Finished(PoolStats stats);

    void Failed(Exception error);
}

/// <summary>
/// Worker output on its way to the main thread: each call is posted with the generation it was made for, so
/// output of a cancelled or replaced search is dropped when the queue is drained.
/// </summary>
internal sealed class SearchReport(
    ResultQueue queue,
    int generation,
    Action<int, TargetResult, Note?, string?> candidateDone,
    Action<PoolStats> finished,
    Action<Exception> failed
) : ISearchReport
{
    public void CandidateDone(int index, TargetResult result, Note? left, string? mainSkill) =>
        queue.Post(generation, () => candidateDone(index, result, left, mainSkill));

    public void Finished(PoolStats stats) => queue.Post(generation, () => finished(stats));

    public void Failed(Exception error) => queue.Post(generation, () => failed(error));
}
