using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Engine.Search;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;

namespace MelvorMods.CombatSimulator2.Host;

/// <summary>
/// A search's stages fought in the simulator host: each stage is sent as its candidates, and its results come back
/// one per candidate. The host keeps the search's copies between stages, so the snapshot goes with the first stage
/// only, and again if the host says it no longer holds it. Everything here runs off the main thread.
/// </summary>
internal sealed class RemoteSearch : ISearchFighter, IHostListener
{
    private readonly string _searchId = "search-" + Guid.NewGuid().ToString("N")[..12];
    private readonly GenerationFilter _filter = new();
    private ISearchReport? _report;
    private SearchJob? _job;
    private bool _snapshotSent;
    private bool _resent;
    private int _ended = 1;

    public (string? RunId, char? Side, string[]? InFlight) Running => (_searchId, null, null);

    // The plan asked of the host, until its reply, its failure or the host's loss.
    private Action<PlanReply?, Exception?>? _planDone;
    private int _planGeneration;

    /// <summary>
    /// Sends the plan with the snapshot, which the host then keeps for the search's stages: the first stage needn't
    /// send it again.
    /// </summary>
    public bool Plan(SearchPlanJob job, Action<PlanReply?, Exception?> done)
    {
        new Thread(() =>
        {
            var client = HostClient.Instance;
            try
            {
                client.EnsureFor(this);
            }
            catch (HostException e)
            {
                MainThread.Post(() => done(null, e));
                return;
            }
            _planGeneration = job.Generation;
            Volatile.Write(ref _planDone, done);
            var message = new SearchPlanMessage(
                job.Generation,
                _searchId,
                job.Snapshot.Character,
                job.Snapshot.Sha256,
                job.Snapshot.GameBuild,
                job.LoadoutSlot,
                job.Request,
                job.What,
                job.PerTrial
            );
            _snapshotSent = true;
            if (
                !client.Send(MessageKind.SearchPlan, message)
                || !client.SendBytes(MessageKind.Snapshot, job.Snapshot.Bytes)
            )
            {
                PlanEnded(null, new HostException(Note.Of("the simulator closed unexpectedly")));
                return;
            }
            // A search cancelled while it plans frees the host at once, so the next search can start there.
            job.Token.Register(() =>
            {
                if (Volatile.Read(ref _planDone) == null)
                    return;
                client.Send(MessageKind.Cancel, new CancelMessage(job.Generation));
                PlanEnded(null, new OperationCanceledException());
            });
        })
        {
            IsBackground = true,
            Name = "cs2-remote-plan",
        }.Start();
        return true;
    }

    // The plan's answer, once: the host is free again and the game hears of it on its main thread.
    private void PlanEnded(PlanReply? reply, Exception? error)
    {
        if (Interlocked.Exchange(ref _planDone, null) is not { } done)
            return;
        HostClient.Instance.EndRun(this);
        MainThread.Post(() => done(reply, error));
    }

    public void Stage(SearchJob job, ISearchReport report, int generation, CancellationToken token) =>
        new Thread(() => Run(job, report, generation, token))
        {
            IsBackground = true,
            Name = "cs2-remote-search",
        }.Start();

    private void Run(SearchJob job, ISearchReport report, int generation, CancellationToken token)
    {
        _job = job;
        _report = report;
        _resent = false;
        Volatile.Write(ref _ended, 0);
        var client = HostClient.Instance;
        try
        {
            client.EnsureFor(this);
        }
        catch (HostException e)
        {
            if (End())
                report.Failed(new HostException(e.Reason, e) { Lost = true });
            return;
        }
        _filter.Current = generation;
        client.AskedCopies = job.Copies;
        if (token.IsCancellationRequested)
        {
            End();
            client.EndRun(this);
            return;
        }
        token.Register(() =>
        {
            if (End())
            {
                client.Send(MessageKind.Cancel, new CancelMessage(generation));
                client.EndRun(this);
            }
        });
        Send(generation);
    }

    private void Send(int generation)
    {
        var job = _job!;
        var client = HostClient.Instance;
        var withSnapshot = !_snapshotSent;
        var message = new SearchStartMessage(
            generation,
            _searchId,
            job.Snapshot.Character,
            job.Snapshot.Sha256,
            job.Snapshot.GameBuild,
            job.LoadoutSlot,
            job.Target.Key,
            new RunOptions(job.Options.Trials, job.Options.Limits, job.Options.IgnoreRequirements),
            job.Seed,
            job.Copies,
            client.ReserveBytes,
            [
                .. job.Candidates.Select(c => new SearchCandidate(
                    c.Index,
                    c.Changes.ShortForm,
                    c.Ignoring.IsEmpty ? null : c.Ignoring.ShortForm
                )),
            ],
            withSnapshot,
            job.BaseChanges?.ShortForm,
            job.OwnLoadouts,
            job.Checks
        );
        _snapshotSent = true;
        if (
            !client.Send(MessageKind.SearchStart, message)
            || (withSnapshot && !client.SendBytes(MessageKind.Snapshot, job.Snapshot.Bytes))
        )
            OnLost(Note.Of("the simulator closed unexpectedly"));
    }

    // True the first time only: a stage ends once, by finish, failure, loss or cancel.
    private bool End() => Interlocked.Exchange(ref _ended, 1) == 0;

    public void OnFrame(Frame frame)
    {
        if (Volatile.Read(ref _planDone) != null)
        {
            if (frame.Kind == MessageKind.SearchPlanned)
            {
                var planned = Protocol.Decode<SearchPlannedMessage>(frame);
                if (planned.Generation == _planGeneration)
                    PlanEnded(planned.Reply, null);
                return;
            }
            if (frame.Kind == MessageKind.Failed)
            {
                var failed = Protocol.Decode<FailedMessage>(frame);
                if (failed.Generation == _planGeneration)
                    PlanEnded(null, new HostException(Note.Of("the simulator failed: {0}", failed.Error)));
                return;
            }
        }
        switch (frame.Kind)
        {
            case MessageKind.CandidateDone:
                var done = Protocol.Decode<CandidateDoneMessage>(frame);
                if (_filter.Accept(done.Generation))
                    _report?.CandidateDone(done.Index, done.Result, done.Left, done.MainSkill);
                break;
            case MessageKind.Guards:
                var guards = Protocol.Decode<GuardsMessage>(frame);
                if (_filter.Accept(guards.Generation))
                    GuardLog.Merge(guards.Skipped, guards.Blocked);
                break;
            case MessageKind.Finished:
                var finished = Protocol.Decode<FinishedMessage>(frame);
                if (!_filter.Accept(finished.Generation) || !End())
                    break;
                HostClient.Instance.EndRun(this);
                _report?.Finished(finished.Stats);
                break;
            case MessageKind.Failed:
                var failed = Protocol.Decode<FailedMessage>(frame);
                if (!_filter.Accept(failed.Generation))
                    break;
                if (failed.Error == SearchStartMessage.SnapshotGone && !_resent)
                {
                    // The host dropped the search's copies between stages: the same stage again, with the snapshot.
                    _resent = true;
                    _snapshotSent = false;
                    Log.Info("the simulator no longer holds the search's snapshot: sending it again");
                    Send(failed.Generation);
                    break;
                }
                if (!End())
                    break;
                HostClient.Instance.EndRun(this);
                _report?.Failed(new HostException(Note.Of("the simulator failed: {0}", failed.Error)));
                break;
        }
    }

    public void OnLost(Note reason)
    {
        PlanEnded(null, new HostException(reason) { Lost = true });
        if (End())
            _report?.Failed(new HostException(reason) { Lost = true });
    }

    /// <summary>The search is over: the host drops its copies.</summary>
    public void Dispose()
    {
        if (_snapshotSent)
            HostClient.Instance.Send(MessageKind.SearchEnd, new SearchEndMessage(_searchId));
    }
}
