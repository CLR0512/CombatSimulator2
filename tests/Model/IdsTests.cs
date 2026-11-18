using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class IdsTests
{
    [Theory]
    [InlineData("master:Kuren", "master:MelvorBase:Kuren")]
    [InlineData("master:MelvorBase:Kuren", "master:MelvorBase:Kuren")]
    [InlineData("master:Mod:Someone", "master:Mod:Someone")]
    public void AMastersKeyTakesItsNamespace(string text, string full)
    {
        Assert.Equal(full, Ids.FullKey(text));
        Assert.True(Ids.IsMaster(full));
        Assert.Equal(full, Ids.FullKey(Ids.BriefKey(full)));
    }

    [Fact]
    public void AMastersShortKeyDropsTheBaseNamespace() =>
        Assert.Equal("master:Kuren", Ids.BriefKey("master:MelvorBase:Kuren"));

    [Theory]
    [InlineData("GolbinForest/Golbin", "MelvorBase:GolbinForest/MelvorBase:Golbin")]
    [InlineData("HobgolbinCamp", "MelvorBase:HobgolbinCamp")]
    public void OtherKeysAreAsBefore(string text, string full)
    {
        Assert.Equal(full, Ids.FullKey(text));
        Assert.Equal(text, Ids.BriefKey(full));
        Assert.False(Ids.IsMaster(full));
    }

    [Fact]
    public void APartIsKeyedByTheMasterThenItsMonster()
    {
        var key = Ids.PartKey("Kuren", "MelvorBase:CorruptedFarmlands/MelvorBase:Scarecrow");
        Assert.Equal("master:MelvorBase:Kuren/MelvorBase:CorruptedFarmlands/MelvorBase:Scarecrow", key);
        Assert.True(Ids.IsPart(key));
        Assert.False(Ids.IsMaster(key));
        Assert.Equal("MelvorBase:Kuren", Ids.MasterOf(key));
        Assert.Equal("MelvorBase:CorruptedFarmlands/MelvorBase:Scarecrow", Ids.PartTarget(key));
        Assert.Equal("master:Kuren/CorruptedFarmlands/Scarecrow", Ids.BriefKey(key));
        Assert.Equal(key, Ids.FullKey("master:Kuren/CorruptedFarmlands/Scarecrow"));
    }

    [Fact]
    public void ASetsKeyIsShortenedPartByPart()
    {
        Assert.Equal("area:CorruptedFarmlands", Ids.BriefKey("area:MelvorBase:CorruptedFarmlands"));
        var pinned = Ids.PinnedKey(["master:MelvorBase:Galan", "MelvorBase:GolbinForest/MelvorBase:Golbin"]);
        Assert.Equal("pinned:MelvorBase:GolbinForest/MelvorBase:Golbin,master:MelvorBase:Galan", pinned);
        Assert.Equal("pinned:GolbinForest/Golbin,master:Galan", Ids.BriefKey(pinned));
        Assert.True(Ids.IsSet(pinned));
        Assert.False(Ids.IsMaster(pinned));
    }
}
