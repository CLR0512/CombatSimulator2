using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Engine.Search;

// How a search measures its sets and times its stages. "Main skill" is the skill current gear trains for every
// set of its style, as a comparison of the two would measure them; a set of another style is measured in its own.
internal sealed partial class SearchRun
{
    // The skill each set trained as fought, and current gear's skill and style once it has fought.
    private readonly Dictionary<int, string?> _skills = [];
    private (string? Skill, string Style)? _current;

    // The sets the stage under way fights, which time it: those it already has a result for take no time.
    private int _fighting;

    private void Measured(int index, string? mainSkill)
    {
        _skills[index] = mainSkill;
        if (_candidates.Find(c => c.Index == index) is { IsCurrent: true } current)
            _current = (mainSkill, current.Style);
    }

    private string MetricOf(int index) =>
        Answers.MetricFor(
            Request.Goal,
            _skills.GetValueOrDefault(index),
            _candidates.Find(c => c.Index == index)?.Style,
            _current?.Skill,
            _current?.Style
        );

    // A set finished before current gear, so measured in its own skill, is measured again in current gear's.
    private void Remeasure(int trials)
    {
        foreach (var index in _in)
            if (
                _fought.TryGetValue((index, trials), out var set)
                && MetricOf(index) is var metric
                && metric != set.Metric
            )
                _fought[(index, trials)] = _last[index] = set with { Metric = metric };
    }

    // Notes how long the stage just fought took, for the next search's estimate.
    private void Timed(int sets, int trials, bool whole) =>
        SearchTimes.Record(_target.Key, _target.Kind, sets, trials, _clock.Elapsed.TotalSeconds, whole);
}
