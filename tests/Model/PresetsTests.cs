using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class PresetsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "cs2-presets-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, true);
    }

    private static readonly ChangeList Armour = ChangeList.Parse("equip=Torso:Mithril_Platebody level=Defence:60");

    private static Presets Empty() => Presets.Read(Path.Combine(Path.GetTempPath(), "cs2-no-such-folder")).Presets;

    [Fact]
    public void ARoundTripKeepsTheOrderSaved()
    {
        var presets = Empty();
        Assert.Equal(PresetResult.Saved, presets.Save("armour", Armour));
        Assert.Equal(PresetResult.Saved, presets.Save("Stars", ChangeList.Parse("star=A:on star=B:on")));
        presets.Write(_folder);
        Assert.False(File.Exists(Presets.PathIn(_folder) + ".tmp"));
        Assert.Contains("\"format\": 1", File.ReadAllText(Presets.PathIn(_folder)), StringComparison.Ordinal);

        var (read, problem) = Presets.Read(_folder);
        Assert.Null(problem);
        Assert.Equal(["armour", "Stars"], read.List.Select(p => p.Name));
        Assert.Equal(Armour, read.Find("ARMOUR")!.Read(out var why));
        Assert.Null(why);
        Assert.Equal(2, read.List[1].Count);
    }

    [Fact]
    public void NamesAreCleanedBoundedAndUniqueIgnoringCase()
    {
        var presets = Empty();
        Assert.Equal(PresetResult.Saved, presets.Save("  Arm\tour\n ", Armour));
        Assert.Equal("Armour", presets.List[0].Name);
        Assert.Equal(PresetResult.NameTaken, presets.Save("ARMOUR", ChangeList.Parse("food=Shrimp")));
        Assert.Equal(Armour.ShortForm, presets.List[0].Changes);
        Assert.Equal(PresetResult.Replaced, presets.Save("ARMOUR", ChangeList.Parse("food=Shrimp"), replace: true));
        Assert.Equal(("ARMOUR", "food=Shrimp"), (presets.List[0].Name, presets.List[0].Changes));
        Assert.Equal(PresetResult.NoName, presets.Save(" \t ", Armour));
        Assert.Equal(PresetResult.NoChanges, presets.Save("empty", ChangeList.Empty));
        Assert.Equal(PresetResult.Saved, presets.Save(new string('x', 60), Armour));
        Assert.Equal(RunHistory.MaxNameLength, presets.List[1].Name.Length);
        Assert.Single(presets.List, p => p.Name.StartsWith('x'));
    }

    [Fact]
    public void RenameAndDelete()
    {
        var presets = Empty();
        presets.Save("armour", Armour);
        presets.Save("stars", ChangeList.Parse("star=A:on"));
        Assert.Equal(PresetResult.NameTaken, presets.Rename("armour", "STARS"));
        Assert.Equal(PresetResult.NoName, presets.Rename("armour", "  "));
        Assert.Equal(PresetResult.NotFound, presets.Rename("gone", "new"));
        Assert.Equal(PresetResult.Saved, presets.Rename("ARMOUR", "Armour"));
        Assert.Equal(PresetResult.Saved, presets.Rename("armour", "mithril"));
        Assert.Equal(["mithril", "stars"], presets.List.Select(p => p.Name));
        Assert.Equal(Armour.ShortForm, presets.Find("mithril")!.Changes);
        Assert.True(presets.Delete("MITHRIL"));
        Assert.False(presets.Delete("mithril"));
        Assert.Null(presets.Find("mithril"));
        Assert.Equal(["stars"], presets.List.Select(p => p.Name));
    }

    [Fact]
    public void AtMostAHundredPresets()
    {
        var presets = Empty();
        for (var i = 0; i < Presets.Max; i++)
            Assert.Equal(
                PresetResult.Saved,
                presets.Save("p" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), Armour)
            );
        Assert.Equal(PresetResult.Full, presets.Save("one more", Armour));
        Assert.Equal(PresetResult.Replaced, presets.Save("p7", ChangeList.Parse("food=none"), replace: true));
        Assert.Equal(Presets.Max, presets.List.Count);
    }

    [Fact]
    public void APresetWithAnUnknownKindReadsAndIsReported()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(
            Presets.PathIn(_folder),
            """
            {"format": 1, "presets": [
              {"name": "future", "changes": "level=Attack:40 familiar=Wolf:on"},
              {"name": "armour", "changes": "level=Defence:60"},
              {"name": "ARMOUR", "changes": "food=none"},
              {"name": " ", "changes": "food=none"},
              {"name": "nothing"}
            ]}
            """
        );
        var (presets, problem) = Presets.Read(_folder);
        Assert.Null(problem);
        Assert.Equal(["future", "armour"], presets.List.Select(p => p.Name));
        var future = presets.Find("future")!;
        Assert.Null(future.Read(out var why));
        Assert.Contains("familiar", why, StringComparison.Ordinal);
        Assert.Equal(2, future.Count);
        // Saving again keeps what this version can't read.
        presets.Write(_folder);
        Assert.Equal("level=Attack:40 familiar=Wolf:on", Presets.Read(_folder).Presets.Find("future")!.Changes);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{"format": 2, "presets": []}""")]
    public void ADamagedFileReadsAsNoneWithAProblem(string content)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Presets.PathIn(_folder), content);
        var (presets, problem) = Presets.Read(_folder);
        Assert.Empty(presets.List);
        Assert.StartsWith(Presets.FileName, problem, StringComparison.Ordinal);
    }
}
