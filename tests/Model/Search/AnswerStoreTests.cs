using System.IO.Compression;
using System.Text;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class AnswerStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cs2-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private static Answer At(string target, int minute, SearchKind kind = SearchKind.BestGear)
    {
        var answer = AnswerTests.Sample();
        answer.Kind = kind;
        answer.Target = target;
        answer.FoundAt = new DateTime(2026, 11, 12, 20, 0, 0, DateTimeKind.Utc).AddMinutes(minute);
        return answer;
    }

    private AnswerStore Loaded()
    {
        var store = new AnswerStore(_dir);
        Assert.Null(store.Load());
        return store;
    }

    private void WriteRaw(string json)
    {
        Directory.CreateDirectory(_dir);
        using var file = File.Create(Path.Combine(_dir, AnswerStore.Name));
        using var gzip = new GZipStream(file, CompressionLevel.Fastest);
        gzip.Write(Encoding.UTF8.GetBytes(json));
    }

    [Fact]
    public void WithoutAFileTheStoreIsEmpty()
    {
        var store = Loaded();
        Assert.Equal(0, store.Count);
        Assert.Null(store.Find(SearchKind.BestGear, "a", new AnswerKey()));
        Assert.Null(store.Latest(SearchKind.BestGear, ""));
    }

    [Fact]
    public void AKeptAnswerIsFoundAgainAfterALoad()
    {
        Loaded().Keep(At("a", 1));
        var found = Loaded().Find(SearchKind.BestGear, "a", At("a", 1).Key);
        Assert.NotNull(found);
        Assert.Empty(found.Value.Changed);
        Assert.Equal(AnswerState.Found, found.Value.Answer.State);
        Assert.Equal(At("a", 1).Best!.Changes, found.Value.Answer.Best!.Changes);
        Assert.Equal(At("a", 1).Best!.Result.Batches.Count, found.Value.Answer.Best!.Result.Batches.Count);
    }

    private static Answer Every(string target, int minute, params string[] leftOut)
    {
        var answer = At(target, minute);
        answer.Key = answer.Key.WithPool(
            SearchRequestTests.Request() with
            {
                Pool = ItemPool.Every,
                LeftOutSources = leftOut.ToHashSet(),
            }
        );
        return answer;
    }

    [Fact]
    public void AnswersOfDifferentPoolsAreKeptApart()
    {
        var store = Loaded();
        store.Keep(At("a", 1));
        store.Keep(Every("a", 2));
        store.Keep(Every("a", 3, "Shop"));
        Assert.Equal(3, store.Count);
        var again = Loaded();
        Assert.Equal(1, again.Find(SearchKind.BestGear, "a", At("a", 1).Key)!.Value.Answer.FoundAt.Minute);
        Assert.Equal(2, again.Find(SearchKind.BestGear, "a", Every("a", 0).Key)!.Value.Answer.FoundAt.Minute);
        var shop = again.Find(SearchKind.BestGear, "a", Every("a", 0, "shop").Key)!.Value.Answer;
        Assert.Equal(3, shop.FoundAt.Minute);
        Assert.Equal(ItemPool.Every, shop.Key.Pool);
        Assert.Equal(["Shop"], shop.Key.LeftOutSources!);
        // A newer answer of one pool replaces only that pool's.
        again.Keep(Every("a", 4));
        Assert.Equal(3, again.Count);
        Assert.Equal(1, again.Find(SearchKind.BestGear, "a", At("a", 1).Key)!.Value.Answer.FoundAt.Minute);
    }

    [Fact]
    public void ASettledAnswersSlotsAreKept()
    {
        var answer = At("a", 1);
        answer.Decisions =
        [
            new SlotDecision(
                "MelvorBase:Gloves",
                "Gloves",
                "MelvorBase:BlackstoneGauntlet",
                "Blackstone Gauntlet",
                false,
                new Mark(MarkKind.NotInBank, null, Note.Text("Blackstone Chest")),
                DecidedBy.DamageTaken,
                "Kelsarin Gloves",
                new Estimate(-0.068, -0.113, -0.023, 4000, "relative")
            ),
        ];
        answer.Settled = true;
        Loaded().Keep(answer);
        var back = Loaded().Find(SearchKind.BestGear, "a", answer.Key)!.Value.Answer;
        var slot = Assert.Single(back.Decisions);
        Assert.Equal(DecidedBy.DamageTaken, slot.DecidedBy);
        Assert.Equal("Blackstone Chest", slot.Mark!.Source!.English());
        Assert.Equal(-0.068, slot.Difference!.Point, 6);
        Assert.True(back.Settled);
    }

    [Fact]
    public void AnAnswerSavedBeforePoolsReadsAsOwnItemsAndSavesAsBefore()
    {
        var owned = At("a", 1).Key;
        var json = System.Text.Json.JsonSerializer.Serialize(owned, RunFile.Options);
        foreach (var field in new[] { "pool", "wearableOnly", "reachedOnly", "leftOutSources" })
            Assert.DoesNotContain("\"" + field + "\"", json, StringComparison.Ordinal);
        var read = System.Text.Json.JsonSerializer.Deserialize<AnswerKey>(json, RunFile.Options)!;
        Assert.Equal(ItemPool.Owned, read.Pool);
        Assert.True(read.SamePool(new AnswerKey()));
        Assert.False(read.SamePool(Every("a", 1).Key));
    }

    [Fact]
    public void AnAnswerUnderAnotherKeyIsFoundWithWhatChanged()
    {
        var store = Loaded();
        store.Keep(At("a", 1));
        var found = store.Find(SearchKind.BestGear, "a", At("a", 1).Key with { Gear = "other", Share = 0.03 });
        Assert.Equal(["gear changed", "margin changed"], found!.Value.Changed.Select(n => n.English()));
    }

    [Fact]
    public void OneAnswerIsKeptPerKindAndTarget()
    {
        var store = Loaded();
        store.Keep(At("a", 1));
        store.Keep(At("a", 2, SearchKind.Droptimizer));
        store.Keep(At("b", 3));
        var again = At("a", 4);
        again.State = AnswerState.KeepCurrent;
        store.Keep(again);
        Assert.Equal(3, store.Count);
        Assert.Equal(AnswerState.KeepCurrent, store.Find(SearchKind.BestGear, "a", again.Key)!.Value.Answer.State);
        Assert.Equal("a", store.Latest(SearchKind.BestGear, "")!.Target);
        Assert.Equal("a", store.Latest(SearchKind.Droptimizer, "")!.Target);
        Assert.NotNull(Loaded().Find(SearchKind.Droptimizer, "a", again.Key));
    }

    [Fact]
    public void ACancelledOrFailedSearchLeavesWhatWasKept()
    {
        var store = Loaded();
        store.Keep(At("a", 1));
        var cancelled = At("a", 2);
        cancelled.State = AnswerState.Cancelled;
        store.Keep(cancelled);
        var failed = At("a", 3);
        failed.State = AnswerState.Failed;
        store.Keep(failed);
        Assert.Equal(AnswerState.Found, Loaded().Find(SearchKind.BestGear, "a", cancelled.Key)!.Value.Answer.State);
    }

    [Fact]
    public void PastFiftyTheOldestGoesFirst()
    {
        var store = Loaded();
        // Kept out of order: the oldest is the one found first, not the one kept first.
        store.Keep(At("t0", 5));
        store.Keep(At("oldest", 0));
        for (var i = 1; i < AnswerStore.Most; i++)
            store.Keep(At("t" + i, 10 + i));
        Assert.Equal(AnswerStore.Most, store.Count);
        Assert.Null(store.Find(SearchKind.BestGear, "oldest", new AnswerKey()));
        Assert.NotNull(store.Find(SearchKind.BestGear, "t0", new AnswerKey()));
        Assert.Equal(AnswerStore.Most, Loaded().Count);
    }

    [Fact]
    public void AnUnknownFormatIsReadAsEmptyAndLeftAlone()
    {
        const string newer = """{"format":3,"answers":[{"kind":"gear","target":"a","later":true}]}""";
        WriteRaw(newer);
        var store = new AnswerStore(_dir);
        Assert.Contains("format 3", store.Load(), StringComparison.Ordinal);
        Assert.Equal(0, store.Count);
        var before = File.ReadAllBytes(store.FilePath);
        store.Keep(At("a", 1));
        Assert.Equal(before, File.ReadAllBytes(store.FilePath));
        // The answer is still there for this session.
        Assert.NotNull(store.Find(SearchKind.BestGear, "a", new AnswerKey()));
    }

    [Fact]
    public void AnUnreadableFileIsReadAsEmpty()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, AnswerStore.Name), "not gzip");
        var store = new AnswerStore(_dir);
        Assert.NotNull(store.Load());
        Assert.Equal(0, store.Count);
        store.Keep(At("a", 1));
        Assert.Equal(1, Loaded().Count);
    }

    [Fact]
    public void EachCharacterKeepsAndSeesOnlyItsOwnAnswers()
    {
        var store = Loaded();
        var mine = At("a", 1);
        mine.Key = mine.Key with { Character = "Dev" };
        var theirs = At("a", 2);
        theirs.Key = theirs.Key with { Character = "Dev2" };
        store.Keep(mine);
        store.Keep(theirs);
        Assert.Equal(2, store.Count);
        Assert.Equal(
            mine.FoundAt,
            store.Find(SearchKind.BestGear, "a", new AnswerKey { Character = "Dev" })!.Value.Answer.FoundAt
        );
        Assert.Null(store.Find(SearchKind.BestGear, "a", new AnswerKey { Character = "Other" }));
        Assert.Equal(mine.FoundAt, store.Latest(SearchKind.BestGear, "Dev")!.FoundAt);
        Assert.Null(store.Latest(SearchKind.BestGear, "Other"));
    }

    [Fact]
    public void AnAnswerKeptLaterIsThereAtOnceAndInTheFileOnceWritten()
    {
        var store = Loaded();
        store.KeepLater(At("a", 1));
        store.KeepLater(At("b", 2));
        Assert.Equal(2, store.Count);
        store.Flush();
        Assert.Equal(2, Loaded().Count);
    }

    [Fact]
    public void ClearRemovesTheFile()
    {
        var store = Loaded();
        store.Keep(At("a", 1));
        Assert.True(File.Exists(store.FilePath));
        store.Clear();
        Assert.False(File.Exists(store.FilePath));
        Assert.Equal(0, store.Count);
        store.Clear();
    }

    [Fact]
    public void AReportSummaryHoldsWhatWasSearchedAndNoResult()
    {
        var store = Loaded();
        store.Keep(At("a", 1));
        store.Keep(At("b", 2, SearchKind.Droptimizer));
        var summaries = store.Summaries();
        Assert.Equal(["droptimizer", "gear"], summaries.Select(s => s["kind"]));
        Assert.Equal("a", summaries[1]["target"]);
        Assert.Equal("Found", summaries[1]["state"]);
        Assert.Equal(At("a", 1).Best!.Changes.ShortForm, summaries[1]["best"]);
        Assert.DoesNotContain(summaries, s => s.Values.OfType<TargetResult>().Any());
    }

    [Fact]
    public void TheFileNamesTheKindsAsTheContractDoes()
    {
        Assert.Equal("gear", AnswerStore.KindName(SearchKind.BestGear));
        Assert.Equal("droptimizer", AnswerStore.KindName(SearchKind.Droptimizer));
    }
}
