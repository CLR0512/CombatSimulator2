using System.Text.Json;
using MelvorMods.CombatSimulator2.Host;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class ProtocolSearchTests
{
    private static T RoundTrip<T>(MessageKind kind, T message)
    {
        using var stream = new MemoryStream();
        new FrameWriter(stream).Send(kind, message);
        stream.Position = 0;
        var frame = new FrameReader(stream).Read();
        Assert.Equal(kind, frame!.Value.Kind);
        var back = Protocol.Decode<T>(frame.Value);
        Assert.Equal(Json(message), Json(back));
        return back;
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, RunFile.Options);

    private static SearchStartMessage Stage(bool first, string? changes = "level=Attack:60") =>
        new(
            Generation: 9,
            SearchId: "search-20261112-200000-1",
            Character: "Dev",
            Sha256: Protocol.Sha256([1, 2, 3]),
            Build: "26316041 0.1.0",
            LoadoutSlot: null,
            Target: "MelvorBase:MistyThicket/MelvorBase:OakTreant",
            Options: new RunOptions(first ? 100 : 400, new TrialLimits(), false),
            Seed: ulong.MaxValue,
            Copies: 4,
            ReserveBytes: 1L << 30,
            Candidates: [new(0, ""), new(7, "equip=Weapon:IronBattleaxe stance=Slash")],
            SnapshotFollows: first,
            Changes: changes
        );

    [Fact]
    public void TheVersionIsEightAndTheNewKindsKeepTheirNumbers()
    {
        Assert.Equal(8, Protocol.Version);
        Assert.Equal(17, (int)MessageKind.SearchStart);
        Assert.Equal(18, (int)MessageKind.CandidateDone);
        Assert.Equal(19, (int)MessageKind.SearchEnd);
        Assert.Equal(20, (int)MessageKind.SearchPlan);
        Assert.Equal(21, (int)MessageKind.SearchPlanned);
        Assert.Equal(22, (int)MessageKind.CopyDropped);
    }

    [Fact]
    public void NoNumberIsUsedTwiceAndTheOldOnesStand()
    {
        var numbers = Enum.GetValues<MessageKind>().Select(k => (int)k).ToList();
        Assert.Equal(numbers.Count, numbers.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 22), numbers.Order());
        Assert.Equal(1, (int)MessageKind.Hello);
        Assert.Equal(2, (int)MessageKind.Start);
        Assert.Equal(7, (int)MessageKind.Done);
        Assert.Equal(16, (int)MessageKind.Release);
    }

    [Fact]
    public void AStageRoundTrips()
    {
        var first = RoundTrip(MessageKind.SearchStart, Stage(first: true));
        Assert.True(first.SnapshotFollows);
        Assert.Equal(100, first.Options.Trials);
        Assert.Equal(ulong.MaxValue, first.Seed);
        Assert.Equal([0, 7], first.Candidates.Select(c => c.Index));
        Assert.Equal("", first.Candidates[0].Changes);
        Assert.Equal(
            ChangeList.Parse("equip=Weapon:IronBattleaxe stance=Slash"),
            ChangeList.Parse(first.Candidates[1].Changes)
        );
        var later = RoundTrip(MessageKind.SearchStart, Stage(first: false));
        Assert.False(later.SnapshotFollows);
        Assert.Equal(first.SearchId, later.SearchId);
        Assert.Equal(first.Sha256, later.Sha256);
    }

    [Fact]
    public void ABaseWithoutChangesLeavesThemOut()
    {
        var json = Json(Stage(first: true, changes: null));
        Assert.DoesNotContain("\"changes\":null", json, StringComparison.Ordinal);
        Assert.Null(RoundTrip(MessageKind.SearchStart, Stage(first: true, changes: null)).Changes);
    }

    [Fact]
    public void TheSnapshotThatFollowsMustBeTheOneNamed()
    {
        Protocol.CheckSnapshot(Stage(first: true).Sha256, [1, 2, 3]);
        Assert.Throws<ProtocolException>(() => Protocol.CheckSnapshot(Stage(first: true).Sha256, [1, 2, 4]));
    }

    [Fact]
    public void ACandidatesResultRoundTrips()
    {
        var result = ResultsModelTests.SampleRun().Results.Values.First(r => r.HasEstimates);
        var done = RoundTrip(MessageKind.CandidateDone, new CandidateDoneMessage(9, 7, result));
        Assert.Equal(7, done.Index);
        Assert.Null(done.Left);
        Assert.Equal(result.Batches.Count, done.Result.Batches.Count);
        Assert.DoesNotContain("\"left\"", Json(new CandidateDoneMessage(9, 7, result)), StringComparison.Ordinal);
    }

    [Fact]
    public void ACandidateLeftOutSaysWhy()
    {
        var refused = new TargetResult
        {
            Key = "t",
            State = TargetState.Refused,
            Reasons = [Note.Text("Requires 40 Attack")],
        };
        var done = RoundTrip(
            MessageKind.CandidateDone,
            new CandidateDoneMessage(9, 3, refused, Note.Text("Requires 40 Attack"))
        );
        Assert.Equal("Requires 40 Attack", done.Left!.English());
        Assert.Equal(TargetState.Refused, done.Result.State);
    }

    [Fact]
    public void TheEndNamesTheSearch()
    {
        var end = RoundTrip(MessageKind.SearchEnd, new SearchEndMessage("search-20261112-200000-1"));
        Assert.Equal("search-20261112-200000-1", end.SearchId);
        Assert.Equal("snapshot gone", SearchStartMessage.SnapshotGone);
    }

    [Fact]
    public void ACandidateWithoutIgnoredRequirementsWritesNone()
    {
        var json = Json(new SearchCandidate(3, "food=Trout"));
        Assert.DoesNotContain("ignoring", json, StringComparison.Ordinal);
        var with = RoundTrip(
            MessageKind.SearchStart,
            Stage(first: false) with
            {
                Candidates = [new(1, "food=Shark", "food=Shark")],
            }
        );
        Assert.Equal("food=Shark", with.Candidates[0].Ignoring);
    }

    [Fact]
    public void ARequestWithoutKindsRoundTripsUnchanged()
    {
        var request = new SearchRequest(
            SearchKind.BestGear,
            new Setup { Character = "Dev" },
            "MelvorBase:Farm/MelvorBase:Cow",
            new Goal("kills"),
            1000,
            4
        );
        var message = new SearchPlanMessage(1, "s", "Dev", "sha", "b", null, request, PlanWhat.Search);
        var back = RoundTrip(MessageKind.SearchPlan, message);
        Assert.Empty(back.Request.Kinds);
        var kinds = RoundTrip(
            MessageKind.SearchPlan,
            message with
            {
                Request = request with { Kinds = [ChoiceKind.Prayers, ChoiceKind.Food], IncludeUnowned = true },
            }
        );
        Assert.Equal([ChoiceKind.Prayers, ChoiceKind.Food], kinds.Request.Kinds);
        Assert.True(kinds.Request.IncludeUnowned);
    }

    [Fact]
    public void APlanCarriesItsChoices()
    {
        var option = new ChoiceOption(
            ChoiceKind.Prayers,
            ["MelvorBase:RockSkin"],
            ChangeList.Parse("prayer=RockSkin:on")
        )
        {
            Unmet = [Note.Of("Prayer level {0}", 40)],
            Names = ["Rock Skin"],
        };
        var choice = new Choice(
            ChoiceKind.Prayers,
            null,
            new ChoiceOption(ChoiceKind.Prayers, [], ChangeList.Empty),
            [option],
            2
        );
        var reply = new PlanReply(SearchFixture.Catalogue()) { Choices = [choice], Supplies = "abc" };
        var back = RoundTrip(MessageKind.SearchPlanned, new SearchPlannedMessage(4, reply));
        var read = Assert.Single(back.Reply.Choices);
        Assert.Equal(2, read.Slots);
        Assert.Equal("Prayer level 40", read.Options[0].Unmet[0].English());
        Assert.Equal("prayer=RockSkin:on", read.Options[0].Changes.ShortForm);
        Assert.Equal("abc", back.Reply.Supplies);
    }
}
