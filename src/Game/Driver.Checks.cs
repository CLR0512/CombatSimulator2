using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Game;

/// <summary>
/// The counts a fight is read at as it goes (<see cref="At"/>), and what to do there: <see cref="Reached"/> gets the
/// check's place in <see cref="At"/> and the result a fight of that many trials would have given, and says whether
/// the fight stops.
/// </summary>
internal sealed record DriveChecks(IReadOnlyList<int> At, Func<int, TargetResult, bool> Reached);

internal static partial class Driver
{
    private sealed partial class Bout
    {
        private readonly CheckTallies? _checks;

        // A check is read the way a fight of its count is batched, so each count keeps batches of its own, filled
        // trial by trial as the fight goes and handed over once its count is reached. A count whose own fight would
        // have given up for trials running over is handed over then, as that fight's result was.
        private sealed class CheckTallies
        {
            private readonly DriveChecks _checks;
            private readonly int[] _at;
            private readonly List<Tally>[] _batches;
            private readonly bool[] _done;

            public CheckTallies(DriveChecks checks, int trials)
            {
                _checks = checks;
                _at = [.. checks.At];
                _batches = new List<Tally>[_at.Length];
                _done = new bool[_at.Length];
                for (var i = 0; i < _at.Length; i++)
                {
                    _done[i] = _at[i] <= 0 || _at[i] >= trials;
                    _batches[i] = [.. Enumerable.Range(0, Batches.Count(Math.Max(0, _at[i]))).Select(_ => new Tally())];
                }
            }

            /// <summary>Adds trial <paramref name="trial"/> (from 0) to every count still open that it falls in.</summary>
            public void Count(Tally tally, int trial)
            {
                for (var i = 0; i < _at.Length; i++)
                    if (!_done[i] && trial < _at[i])
                        _batches[i][Batches.IndexOf(trial, _at[i])].Add(tally);
            }

            /// <summary>
            /// After trial <paramref name="done"/> ended: each count reached, or given up on, is handed over with
            /// its result from <paramref name="result"/>; true when the fight stops there.
            /// </summary>
            public bool Reached(int done, Func<int, List<Tally>, bool, int, TargetResult> result, Stall? stall)
            {
                for (var i = 0; i < _at.Length; i++)
                {
                    if (_done[i])
                        continue;
                    var gaveUp =
                        done < _at[i]
                        && stall != null
                        && Batches.IndexOf(done, _at[i]) != Batches.IndexOf(done - 1, _at[i])
                        && StallReasons.TooOftenOver(_batches[i].GetRange(0, Batches.IndexOf(done, _at[i])));
                    if (done != _at[i] && !gaveUp)
                        continue;
                    _done[i] = true;
                    if (_checks.Reached(i, result(_at[i], _batches[i], gaveUp, done)))
                        return true;
                }
                return false;
            }
        }

        // After a trial: the counts it reaches are read, and the fight stops when a check says so.
        private bool AtCheck() => _checks?.Reached(_result.TrialsDone, CheckResult, _result.Stall) == true;

        // The result a fight of `count` trials would have given, `done` trials in: the same target, its batches, the
        // time and the dungeon's fights so far, and what the fight noted about itself.
        private TargetResult CheckResult(int count, List<Tally> batches, bool gaveUp, int done)
        {
            var result = new TargetResult
            {
                Key = _result.Key,
                Name = _result.Name,
                Kind = _result.Kind,
                State = gaveUp ? TargetState.Unfinished : TargetState.Finished,
                TrialsDone = done,
                GameSeconds = _result.GameSeconds,
                Batches = batches,
                NotSimulated = _result.NotSimulated,
                Fights = [.. _result.Fights.Where(f => f.Trial < done)],
                MaxHp = _result.MaxHp,
                Drops = _result.Drops,
                Stages = _result.Stages,
                Task = _result.Task,
                TaskNote = _result.TaskNote,
            };
            result.Assumptions.AddRange(_result.Assumptions);
            result.Unsupported.AddRange(_result.Unsupported);
            if (gaveUp && _result.Stall is { } stall)
                result.Reasons.Add(
                    StallReasons.GaveUp(stall, result.RanOver, done, stall.Enemy is { } id ? EnemyNameOf(id) : null)
                );
            return result;
        }
    }
}
