using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Engine.Search;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Log = MelvorMods.CombatSimulator2.Common.Log;

namespace MelvorMods.CombatSimulator2.Host;

// A search on the host: its stages share one pool, so the copies prepared with the base are kept between them.
internal sealed partial class HostServer
{
    /// <summary>A search's copies are dropped when no stage of it has come for this long.</summary>
    private static readonly TimeSpan SearchIdle = TimeSpan.FromSeconds(60);

    private readonly Lock _searchLock = new();
    private SearchPool? _searchPool;
    private string? _searchId;
    private byte[]? _searchSnapshot;
    private int _searchGeneration;
    private Timer? _searchTimer;

    // A stage of a search: the first brings the snapshot, later ones name the search whose copies are kept.
    private void SearchStage(Frame frame)
    {
        var stage = Protocol.Decode<SearchStartMessage>(frame);
        byte[]? bytes = null;
        if (stage.SnapshotFollows)
        {
            bytes = _reader.Read() is { Kind: MessageKind.Snapshot } next
                ? next.Payload
                : throw new ProtocolException("a search's first stage must be followed by its snapshot");
            Protocol.CheckSnapshot(stage.Sha256, bytes);
        }
        try
        {
            BeginSearch(stage, bytes);
        }
        catch (Exception e) when (e is not (OutOfMemoryException or ProtocolException))
        {
            Log.Error($"search stage {stage.Generation} not started", e);
            Send(MessageKind.Failed, new FailedMessage(stage.Generation, $"{e.GetType().Name}: {e.Message}"));
            EndPeak(stage.Generation);
            _gate.End(stage.Generation);
            DropSearch();
        }
    }

    // A search's plan, with its snapshot: read on a copy of the host's own, so the game builds none. The snapshot is
    // kept for the search's stages, which then needn't send it.
    private void SearchPlan(Frame frame)
    {
        var ask = Protocol.Decode<SearchPlanMessage>(frame);
        var bytes = _reader.Read() is { Kind: MessageKind.Snapshot } next
            ? next.Payload
            : throw new ProtocolException("a search's plan must be followed by its snapshot");
        Protocol.CheckSnapshot(ask.Sha256, bytes);
        lock (_searchLock)
        {
            _searchTimer?.Dispose();
            _searchTimer = null;
            if (_searchId != ask.SearchId)
            {
                DropSearch();
                _searchId = ask.SearchId;
            }
            _searchSnapshot = bytes;
        }
        _gate.Start(ask.Generation);
        _cancel?.Dispose();
        _cancel = new CancellationTokenSource();
        var token = _cancel.Token;
        new Thread(() => PlanSearch(ask, bytes, token)) { IsBackground = true, Name = "cs2-plan" }.Start();
    }

    // A cancel frees the gate at once (the frame loop does), and the plan stops at its next set: nothing is sent.
    private void PlanSearch(SearchPlanMessage ask, byte[] bytes, CancellationToken token)
    {
        try
        {
            SharedCaches.Fill();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var snapshot = new Snapshot(ask.Character, bytes, ask.Sha256, DateTime.UtcNow, ask.Build, 0);
            var setup = ask.Request.Setup;
            PlanReply reply;
            using (var copy = _stock.Take() ?? Copy.Build())
            {
                copy.Prepare(snapshot, ask.LoadoutSlot);
                if (setup.Changes is { IsEmpty: false } changes)
                    copy.Change(changes, setup.IgnoreRequirements);
                reply = SearchPlanning.On(copy, ask.Request, ask.What, ask.PerTrial, token);
            }
            Log.Info($"search plan {ask.Generation} ({ask.What}) read in {watch.Elapsed.TotalSeconds:0.0} s");
            // The gate opens before the game hears of the plan: its first stage can come at once.
            SearchStageEnded(ask.Generation);
            Send(MessageKind.SearchPlanned, new SearchPlannedMessage(ask.Generation, reply));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Log.Info($"search plan {ask.Generation} cancelled");
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Error($"search plan {ask.Generation} failed", e);
            SearchStageEnded(ask.Generation);
            Send(MessageKind.Failed, new FailedMessage(ask.Generation, $"{e.GetType().Name}: {e.Message}"));
        }
    }

    private void BeginSearch(SearchStartMessage stage, byte[]? bytes)
    {
        SearchPool pool;
        byte[] snapshotBytes;
        lock (_searchLock)
        {
            _searchTimer?.Dispose();
            _searchTimer = null;
            if (_searchId != stage.SearchId)
            {
                DropSearch();
                _searchId = stage.SearchId;
            }
            if (bytes != null)
                _searchSnapshot = bytes;
            if (_searchSnapshot == null)
            {
                // The copies went with the snapshot (a run took the host, or the search sat idle): ask for it again.
                Send(MessageKind.Failed, new FailedMessage(stage.Generation, SearchStartMessage.SnapshotGone));
                return;
            }
            pool = _searchPool ??= new SearchPool(MemoryReserve.For(stage.ReserveBytes), _stock.Take);
            snapshotBytes = _searchSnapshot;
            _searchGeneration = stage.Generation;
        }
        _gate.Start(stage.Generation);
        _peak.Start(ProcessMeter.PrivateBytes());
        GuardLog.Reset();
        var snapshot = new Snapshot(stage.Character, snapshotBytes, stage.Sha256, DateTime.UtcNow, stage.Build, 0);
        var job = new SearchJob(
            snapshot,
            stage.LoadoutSlot,
            stage.Changes is { } changes ? ChangeList.Parse(changes) : null,
            TargetSpec.FromKey(stage.Target),
            new DriveOptions(stage.Options.Trials, stage.Options.Limits, stage.Options.IgnoreRequirements),
            stage.Seed,
            stage.Copies,
            [
                .. stage.Candidates.Select(c => new Candidate(c.Index, ChangeList.Parse(c.Changes), "")
                {
                    Ignoring = c.Ignoring == null ? ChangeList.Empty : ChangeList.Parse(c.Ignoring),
                }),
            ],
            stage.OwnLoadouts,
            stage.Checks
        );
        _cancel?.Dispose();
        _cancel = new CancellationTokenSource();
        pool.Start(job, new SearchStageReport(this, stage.Generation, stage.Copies), _cancel.Token);
        Log.Info(
            $"search stage {stage.Generation}: {stage.Candidates.Count} sets, {stage.Options.Trials} trials, seed {stage.Seed}, copies {stage.Copies}"
        );
    }

    /// <summary>Whether <paramref name="generation"/> is the search stage on the host now.</summary>
    private bool IsSearchStage(int generation)
    {
        lock (_searchLock)
            return _searchId != null && _searchGeneration == generation;
    }

    /// <summary>
    /// Drops the search's copies and its snapshot: at its end, a cancel, a run taking the host, or after it sat
    /// idle. With <paramref name="id"/>, only that search's.
    /// </summary>
    private void DropSearch(string? id = null)
    {
        lock (_searchLock)
        {
            if (id != null && id != _searchId)
                return;
            _searchTimer?.Dispose();
            _searchTimer = null;
            _searchPool?.Dispose();
            _searchPool = null;
            _searchSnapshot = null;
            _searchId = null;
        }
    }

    private void SearchStageEnded(int generation)
    {
        EndPeak(generation);
        _gate.End(generation);
        _stock.RunEnded();
        lock (_searchLock)
        {
            if (_searchId is not { } id)
                return;
            _searchTimer?.Dispose();
            _searchTimer = new Timer(_ => DropSearch(id), null, SearchIdle, Timeout.InfiniteTimeSpan);
        }
    }

    // Stage output → frames, each tagged with the stage it belongs to.
    private sealed class SearchStageReport(HostServer server, int generation, int copies) : ISearchReport
    {
        public void CandidateDone(int index, TargetResult result, Note? left, string? mainSkill) =>
            server.Send(
                MessageKind.CandidateDone,
                new CandidateDoneMessage(generation, index, result, left, mainSkill)
            );

        public void Finished(PoolStats stats)
        {
            SendGuards();
            var peak = server._gate.Active == generation ? server._peak.End(ProcessMeter.PrivateBytes()) : 0;
            // The gate opens before the game hears of the end: its next stage can arrive at once.
            server.SearchStageEnded(generation);
            server.Send(MessageKind.Finished, new FinishedMessage(generation, stats, peak, copies));
        }

        public void Failed(Exception error)
        {
            Log.Error($"search stage {generation} failed", error);
            SendGuards();
            var text = error is NotEnoughMemoryException ? error.Message : $"{error.GetType().Name}: {error.Message}";
            server.SearchStageEnded(generation);
            server.DropSearch();
            server.Send(MessageKind.Failed, new FailedMessage(generation, text));
        }

        private void SendGuards()
        {
            var (skipped, blocked) = GuardLog.Export();
            server.Send(MessageKind.Guards, new GuardsMessage(generation, skipped, blocked));
        }
    }
}
