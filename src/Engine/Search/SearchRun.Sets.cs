using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Engine.Search;

// A search over targets scored together (an area, the pins) counts each target equally: each set's rate on a target
// is weighed against current gear's there. The weights are taken once, from current gear's first result, so every
// stage and step races on one scale.
internal sealed partial class SearchRun
{
    private List<double>? _weights;

    // For a search over several targets: those on which a set other than current gear and the best did better alone.
    private void WantsOwnSet(Answer answer)
    {
        if (_target.Kind is not (TargetKind.Master or TargetKind.Set) || answer.Best is not { } best)
            return;
        var others = _last.Where(p => p.Key != 0 && p.Value.Result != best.Result).Select(p => p.Value.Result);
        answer.WantsOwnSet =
        [
            .. SetScore.WantsOwnSet(best.Result, others, best.Metric, Ranking.LowerIsBetter(best.Metric), _prices),
        ];
    }

    // Gives every set fought so far the search's weights, once current gear's result has come in.
    private void Weigh(List<StageResult> results)
    {
        if (_target.Kind != TargetKind.Set)
            return;
        if (_weights == null && _last.TryGetValue(0, out var current) && current.Result.Parts is { } parts)
            _weights =
            [
                .. parts.Select(p =>
                    Metrics.Estimate(p, current.Metric, TimeUnit.Hour, _prices)?.Point is double rate && rate > 0
                        ? 1 / rate
                        : 0
                ),
            ];
        if (_weights == null)
            return;
        foreach (var set in _last.Values.Concat(results))
            set.Result.Weights ??= [.. _weights];
    }
}
