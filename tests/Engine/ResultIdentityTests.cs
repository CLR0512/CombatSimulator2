using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class ResultIdentityTests
{
    private const string Golbin = "MelvorBase:GolbinForest/MelvorBase:Golbin";

    private static Run RunWith(ChangeList? changes) =>
        new()
        {
            Id = "run-1",
            Setup = new Setup { Character = "Dev", Changes = changes },
            GameBuild = "26316041",
            Cs2Mvid = "cs2",
            SetupHash = "setup",
            Trials = 1000,
            Targets = [Golbin],
        };

    [Fact]
    public void ARunWithoutChangesHasNone()
    {
        var identity = ResultIdentity.Of(RunWith(null), Golbin);
        Assert.NotNull(identity);
        Assert.Equal("", identity.Changes);
        // As it was built before changes existed.
        Assert.Equal(
            new ResultIdentity("26316041", "cs2", "setup", "current", false, Golbin, 1000, 300, 1800, null, ""),
            identity
        );
    }

    [Fact]
    public void DifferentChangesNeverShareAnIdentity()
    {
        var none = ResultIdentity.Of(RunWith(null), Golbin);
        var strength = ResultIdentity.Of(RunWith(ChangeList.Parse("level=Strength:60")), Golbin);
        var both = ResultIdentity.Of(RunWith(ChangeList.Parse("level=Strength:60 food=Shrimp")), Golbin);
        Assert.NotEqual(none, strength);
        Assert.NotEqual(strength, both);
        Assert.NotEqual(none, both);
        Assert.Equal(strength, ResultIdentity.Of(RunWith(ChangeList.Parse("level=Strength:60")), Golbin));
    }

    // The setup hash holds every saved loadout's contents, so a loadout changed in the game keys a master's monsters
    // on it apart from their old results, though the change naming it reads the same.
    [Fact]
    public void ALoadoutChangedInTheGameKeysAMasterApart()
    {
        const string kuren = "master:MelvorBase:Kuren";
        var changes = ChangeList.Parse("loadouts=Kuren:Golbin@Loadout1");
        var before = RunWith(changes);
        var after = RunWith(changes);
        after.SetupHash = "setup-with-loadout1-changed";
        Assert.NotEqual(ResultIdentity.Of(before, kuren), ResultIdentity.Of(after, kuren));
        Assert.NotEqual(ResultIdentity.Of(RunWith(null), kuren), ResultIdentity.Of(before, kuren));
        Assert.Equal(ResultIdentity.Of(before, kuren), ResultIdentity.Of(RunWith(changes), kuren));
    }

    [Fact]
    public void TheIndexAnswersOnlyTheSameChanges()
    {
        var index = new ResultIndex();
        var changed = RunWith(ChangeList.Parse("level=Strength:60"));
        changed.State = RunState.Finished;
        changed.Results[Golbin] = new TargetResult
        {
            Key = Golbin,
            State = TargetState.Finished,
            TrialsDone = 1000,
        };
        index.Add(changed);
        Assert.NotNull(index.Find(ResultIdentity.Of(changed, Golbin)!, null));
        Assert.Null(index.Find(ResultIdentity.Of(RunWith(null), Golbin)!, null));
    }
}
