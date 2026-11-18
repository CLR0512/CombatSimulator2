using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class DropGroupsTests
{
    private const string Map = "MelvorBase:MapToAshenLands";
    private const string Log = "MelvorBase:WillowLog";

    private static TargetResult Result(double? quantity)
    {
        var result = new TargetResult
        {
            Key = "MelvorBase:MistyThicket/MelvorBase:WillowTreant",
            Kind = TargetKind.Enemy,
            State = TargetState.Finished,
            TrialsDone = 100,
            Drops =
            [
                new DropChance
                {
                    Source = DropSource.Loot,
                    Id = Map,
                    Chance = 0.0012,
                    Per = ChancePer.Kill,
                    Quantity = quantity,
                },
            ],
        };
        for (var i = 0; i < 10; i++)
            result.Batches.Add(
                new Tally
                {
                    Trials = 10,
                    Kills = 10,
                    GameSeconds = 100,
                }
            );
        return result;
    }

    private static Difference PerKill(Verdict verdict) =>
        new(
            "item:" + Log,
            Log,
            null,
            null,
            null,
            null,
            verdict,
            false,
            false,
            TargetState.Finished,
            TargetState.Finished,
            null,
            null
        );

    [Fact]
    public void AnItemWithATimeForOneIsRare()
    {
        Assert.Equal(DropGroup.Rare, DropGroups.Of(Result(1), Result(1), Map, PerKill(Verdict.NoClearDifference)));
        Assert.Equal(DropGroup.Rare, DropGroups.Of(null, Result(1), Map, null));
    }

    [Fact]
    public void WithoutATimeForOneARareEntryGoesByItsChangePerKill()
    {
        Assert.Equal(
            DropGroup.WithKills,
            DropGroups.Of(Result(null), Result(null), Map, PerKill(Verdict.NoClearDifference))
        );
    }

    [Theory]
    [InlineData(Verdict.Better, DropGroup.NotWithKills)]
    [InlineData(Verdict.Worse, DropGroup.NotWithKills)]
    [InlineData(Verdict.NoClearDifference, DropGroup.WithKills)]
    [InlineData(Verdict.None, DropGroup.WithKills)]
    public void ADropFollowsTheKillRateUnlessItChangesClearlyPerKill(Verdict verdict, DropGroup group)
    {
        Assert.Equal(group, DropGroups.Of(Result(1), Result(1), Log, PerKill(verdict)));
    }
}
