using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using MelvorMods.CombatSimulator2.Model.Slayer;

namespace MelvorMods.CombatSimulator2.Engine.Search;

/// <summary>
/// A search's fighter for a master whose monsters include some with a loadout of their own: those don't change with
/// the set tried, so before a stage they are fought once for its seed and trials (kept for the stages after), and
/// each candidate's master result, fought without them, gets them before it settles. Other stages pass through.
/// </summary>
internal sealed class OwnLoadoutsFighter(ISearchFighter inner, Func<int> nextGeneration) : ISearchFighter
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, List<TargetResult>> _fought = new(StringComparer.Ordinal);

    public bool Plan(SearchPlanJob job, Action<PlanReply?, Exception?> done) => inner.Plan(job, done);

    public void Dispose() => inner.Dispose();

    public void Stage(SearchJob job, ISearchReport report, int generation, CancellationToken token)
    {
        if (job.Target.Kind != TargetKind.Master || !HasOwnLoadouts(job))
        {
            inner.Stage(job, report, generation, token);
            return;
        }
        var key = KeyOf(job);
        List<TargetResult>? kept;
        lock (_lock)
            kept = _fought.GetValueOrDefault(key);
        if (kept != null)
        {
            inner.Stage(job, new Merging(report, kept), generation, token);
            return;
        }
        var own = job with { Candidates = [new Candidate(0, ChangeList.Empty, "")], Copies = 1, OwnLoadouts = true };
        inner.Stage(
            own,
            new OwnFought(
                report,
                parts =>
                {
                    lock (_lock)
                        _fought[key] = parts;
                    if (!token.IsCancellationRequested)
                        inner.Stage(job, new Merging(report, parts), generation, token);
                }
            ),
            nextGeneration(),
            token
        );
    }

    // What the shared monsters' figures depend on: the seed, how they are fought and the base.
    private static string KeyOf(SearchJob job)
    {
        var (options, limits) = (job.Options, job.Options.Limits);
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{job.Seed}|{options.Trials}|{options.IgnoreRequirements}|{options.StopPrecision}|{limits.EnemySeconds}|{limits.DungeonSeconds}|{limits.MasterPartSeconds}|{job.LoadoutSlot}|{job.BaseChanges?.ShortForm}"
        );
    }

    private static bool HasOwnLoadouts(SearchJob job) =>
        job.BaseChanges?.Find(new ChangeKey(ChangeKind.Loadouts, job.Target.Master!)) is { Loadouts.Count: > 0 };

    // The fight of the monsters with their own loadout: their finished results go on, or the stage fails.
    private sealed class OwnFought(ISearchReport outer, Action<List<TargetResult>> fought) : ISearchReport
    {
        private TargetResult? _result;

        public void CandidateDone(int index, TargetResult result, Note? left, string? mainSkill) => _result = result;

        public void Finished(PoolStats stats)
        {
            var parts = _result?.Parts?.Where(p => p.IsFinal).ToList();
            if (parts is not { Count: > 0 })
            {
                outer.Failed(new InvalidOperationException("the monsters with their own loadout weren't fought"));
                return;
            }
            fought(parts);
        }

        public void Failed(Exception error) => outer.Failed(error);
    }

    // Each candidate's master result with the shared monsters filled in, settled as a run settles a master.
    private sealed class Merging(ISearchReport outer, List<TargetResult> own) : ISearchReport
    {
        public void CandidateDone(int index, TargetResult result, Note? left, string? mainSkill)
        {
            if (result.Kind == TargetKind.Master && !result.IsFinal && result.Parts is { } parts)
            {
                foreach (var part in own)
                {
                    var i = parts.FindIndex(p => p.Key == part.Key);
                    if (i >= 0)
                        parts[i] = part;
                }
                MasterParts.Settle(result);
            }
            outer.CandidateDone(index, result, left, mainSkill);
        }

        public void Finished(PoolStats stats) => outer.Finished(stats);

        public void Failed(Exception error) => outer.Failed(error);
    }
}
