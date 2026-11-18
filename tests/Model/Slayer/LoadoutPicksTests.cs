using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using MelvorMods.CombatSimulator2.Model.Slayer;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class LoadoutPicksTests
{
    private const string L1 = "MelvorBase:Loadout1";
    private const string L2 = "MelvorBase:Loadout2";
    private static readonly Goal SlayerXp = new("xp:MelvorBase:Slayer");

    private static string Key(string name) => Ids.PartKey("Kuren", "MelvorBase:Farm/MelvorBase:" + name);

    // One monster's fights: xp per second set by seconds a kill; the same seed on every choice pairs the batches.
    private static TargetResult Part(string name, double seconds, int deaths = 0, int kills = 10) =>
        MasterFiguresTests.Part(Key(name), seconds, 100, killsPerBatch: kills, deathsPerBatch: deaths, seed: 7);

    private static TargetResult Master(params TargetResult[] parts) =>
        new()
        {
            Key = "master:MelvorBase:Kuren",
            Kind = TargetKind.Master,
            State = TargetState.Finished,
            Parts = [.. parts],
        };

    private static LoadoutPick Pick(LoadoutPicksAnswer answer, string name) =>
        answer.Picks.Single(p => p.Monster == Key(name));

    [Fact]
    public void AClearlyFasterLoadoutIsPickedAndTheRestKeepTheSetup()
    {
        var answer = LoadoutPicks.For(
            [
                (null, Master(Part("Cow", 10), Part("Jack", 10))),
                (L1, Master(Part("Cow", 5), Part("Jack", 20))),
                (L2, Master(Part("Cow", 8), Part("Jack", 15))),
            ],
            SlayerXp
        )!;
        Assert.Equal(L1, Pick(answer, "Cow").Choice);
        Assert.True(Pick(answer, "Cow").Gain!.Low > 0);
        Assert.Null(Pick(answer, "Jack").Choice);
        Assert.Equal("loadouts=Kuren:Cow@Loadout1", answer.Change!.ShortForm);
        Assert.True(answer.Gain!.Low > 0);
    }

    [Fact]
    public void AChoiceWithinThePicksRangeIsATie()
    {
        var answer = LoadoutPicks.For(
            [(null, Master(Part("Cow", 10))), (L1, Master(Part("Cow", 10))), (L2, Master(Part("Cow", 30)))],
            SlayerXp
        )!;
        var cow = Pick(answer, "Cow");
        Assert.Null(cow.Choice);
        Assert.Equal([L1], cow.Ties);
        Assert.Null(answer.Change);
    }

    [Fact]
    public void ALoadoutThatDiesPastTheLimitIsNeverPicked()
    {
        var answer = LoadoutPicks.For(
            [(null, Master(Part("Cow", 10))), (L1, Master(Part("Cow", 2, deaths: 1)))],
            SlayerXp
        )!;
        var cow = Pick(answer, "Cow");
        Assert.Null(cow.Choice);
        Assert.Equal([L1], cow.OverLimit);
    }

    [Fact]
    public void AMonsterStuckOnTheSetupTakesALoadoutThatKillsIt()
    {
        var answer = LoadoutPicks.For(
            [
                (null, Master(Part("Cow", 10, kills: 0), Part("Jack", 10))),
                (L1, Master(Part("Cow", 20), Part("Jack", 10))),
            ],
            SlayerXp
        )!;
        Assert.Equal(L1, Pick(answer, "Cow").Choice);
        Assert.Null(Pick(answer, "Cow").Gain);
        Assert.NotNull(answer.Figure);
    }

    [Fact]
    public void AMonsterStuckOnEveryChoiceStaysStuck()
    {
        var answer = LoadoutPicks.For(
            [(null, Master(Part("Cow", 10, kills: 0))), (L1, Master(Part("Cow", 10, kills: 0)))],
            SlayerXp
        )!;
        Assert.True(Pick(answer, "Cow").Stuck);
        Assert.Null(answer.Figure);
    }

    [Fact]
    public void EveryMonsterKeepingTheSetupGivesNoChange()
    {
        var answer = LoadoutPicks.For(
            [(null, Master(Part("Cow", 5), Part("Jack", 5))), (L1, Master(Part("Cow", 10), Part("Jack", 10)))],
            SlayerXp
        )!;
        Assert.All(answer.Picks, p => Assert.Null(p.Choice));
        Assert.Null(answer.Change);
        Assert.Equal(0, answer.Gain!.Point, 6);
    }
}
