using System.Security.Cryptography;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine.Search;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Engine.Dev;

/// <summary>
/// A report's search fought again from its own snapshot, request and seed: its answer is matched and dropped, never
/// kept with the player's answers, and it isn't the search on screen. Main thread.
/// </summary>
internal sealed class ReplaySearch
{
    // The longest a search replay is driven before it is given up.
    private static readonly TimeSpan Longest = TimeSpan.FromHours(2);

    private readonly SearchRecord _record;
    private readonly List<string> _notes = [];
    private ReplayOutcome? _outcome;

    private ReplaySearch(SearchRecord record) => _record = record;

    public static ReplaySearch? Last { get; private set; }

    /// <summary>When it started, in the order replays of runs and searches share.</summary>
    public static int LastStarted { get; private set; }

    public static bool IsActive => Last is { _outcome: null };

    public IReadOnlyList<string> Lines() =>
        _outcome == null ? [] : [_outcome.Line(), .. _outcome.Notes.Select(n => "note: " + n)];

    public static ReplaySearch Start(ReportFolder report, SearchRecord record, ReplayOptions options)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(record);
        MainThread.Assert("ReplaySearch.Start");
        if (IsActive || Replay.IsActive || RunService.Instance.IsActive)
            throw new CommandException("a run, search or replay is still running: cancel it first");
        if (report.GameBuild != LiveGame.GameBuild)
            throw new CommandException(
                $"the report is from game build {report.GameBuild}, this is {LiveGame.GameBuild}"
            );
        if (record.Missing() is [_, ..])
            throw new CommandException($"search {record.Id} has no request in the report");
        var (bytes, note) = report.SnapshotFor(
            record.Id,
            record.Snapshot,
            record.SnapshotSha256,
            record.SetupHash,
            record.State == "failed",
            options.Anyway
        );
        var snapshot = new Snapshot(
            record.Setup.Character,
            bytes,
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            DateTime.UtcNow,
            record.GameBuild,
            0
        );
        var where = options.In ?? record.Where ?? RunService.InHost;
        var request = new SearchRequest(
            record.Kind == "droptimizer" ? SearchKind.Droptimizer : SearchKind.BestGear,
            new Setup
            {
                Character = record.Setup.Character,
                Loadout = record.Setup.Loadout,
                IgnoreRequirements = record.Setup.IgnoreRequirements,
                Changes = record.Setup.Changes,
            },
            record.Target,
            record.Goal,
            record.Trials,
            options.Copies ?? record.Copies
        )
        {
            Locks = record.Locks.ToHashSet(StringComparer.Ordinal),
            Excluded = record.Excluded.ToHashSet(StringComparer.Ordinal),
            AllStyles = record.AllStyles,
            Seed = record.Seed,
            Where = where,
            Limits = record.Limits,
            SlotAtATime = record.SlotAtATime,
        };
        var replay = new ReplaySearch(record);
        replay._notes.Add($"rerun on {note}");
        if (record.Where is { } ran && ran != where)
            replay._notes.Add($"the search was made in the {ran}; replayed in the {where}");
        if (report.Cs2Mvid != null && report.Cs2Mvid != RunService.Cs2Mvid)
            replay._notes.Add(
                $"CS2 build differs from the report's ({RunService.Cs2Mvid} here, {report.Cs2Mvid} there)"
            );
        var search = SearchStart.FromSnapshot(
            request,
            record.Share,
            where,
            record.Seed ?? 0,
            RunService.Instance.NextGeneration,
            snapshot,
            record.SetupHash
        );
        search.Ended += answer => replay.Ended(search, answer, where);
        Last = replay;
        LastStarted = Replay.Order();
        SearchStart.Plan(search);
        // The search on screen is driven by the run service; this one by its own frame check.
        MainThread.When(
            () =>
            {
                search.OnFrame();
                return !search.IsActive;
            },
            () => { },
            Longest,
            search.Cancel
        );
        return replay;
    }

    private void Ended(SearchRun search, Answer answer, string where)
    {
        var got = SearchOutcome.Of(answer);
        _outcome = _record.Failure is { } failure
            ? ReplayMatch.SearchFailure(
                _record.Id,
                where,
                failure,
                answer.State == AnswerState.Failed
                    ? new SearchFailure(search.Stage, FailureRecord.Name(FailureKind.Run), _record.Target)
                    : null,
                _notes
            )
            : ReplayMatch.Search(_record.Id, where, _record.Answer ?? new SearchOutcome(), got, _notes);
        Log.Info("search replay ended: " + _outcome.Line());
        search.Dispose();
    }
}
