using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class CharacterPrefsTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "cs2-characters-" + Guid.NewGuid().ToString("N")
    );

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, true);
    }

    private static Preferences Format2() =>
        new()
        {
            Format = 2,
            DisabledTargets = ["MelvorBase:HobgolbinCamp", "MelvorBase:GolbinForest/MelvorBase:Golbin"],
            Setup = "MelvorBase:Loadout2",
            IgnoreRequirements = true,
        };

    [Fact]
    public void ACharacterSeenForTheFirstTimeStartsFromTheOldSettings()
    {
        var (prefs, problem) = CharacterPrefs.Read(_folder);
        Assert.Null(problem);
        var state = prefs.For("save-1", "Dev", Format2(), out _);
        Assert.Equal("Dev", state.Name);
        Assert.Equal(["MelvorBase:HobgolbinCamp", "MelvorBase:GolbinForest/MelvorBase:Golbin"], state.Ignored);
        Assert.Equal(new SideState(SideBase.Loadout("MelvorBase:Loadout2"), null, true), state.A);
        Assert.Null(state.B);
        Assert.Equal(SideLetter.A, state.Editing);
    }

    [Fact]
    public void OnlyTheFirstSightingIsSeededAndTheSettingsThenDropWhatWasTaken()
    {
        var (prefs, _) = CharacterPrefs.Read(_folder);
        var settings = Format2();
        prefs.For("save-1", "Dev", settings, out var first);
        prefs.For("save-1", "Dev", settings, out var second);
        Assert.True(first);
        Assert.False(second);
        Assert.True(settings.DropCharacterSeeds());
        Assert.False(settings.DropCharacterSeeds());
        Assert.Null(settings.DisabledTargets);
        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        Assert.DoesNotContain("DisabledTargets", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Setup\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("IgnoreRequirements", json, StringComparison.Ordinal);
        // A character first seen after that starts from the defaults.
        var later = prefs.For("save-2", "Alt", settings, out var third);
        Assert.True(third);
        Assert.Empty(later.Ignored);
        Assert.Equal(SideState.AsNow, later.A);
    }

    [Fact]
    public void ACharacterAlreadySeenKeepsItsOwnState()
    {
        var (prefs, _) = CharacterPrefs.Read(_folder);
        var state = prefs.For("save-1", "Dev", new Preferences(), out _);
        state.Ignored.Add("MelvorBase:VenomPit");
        var again = prefs.For("save-1", "Dev renamed", Format2(), out _);
        Assert.Same(state, again);
        Assert.Equal(["MelvorBase:VenomPit"], again.Ignored);
        Assert.Equal(SideState.AsNow, again.A);
        Assert.Equal("Dev renamed", again.Name);
        // Another character starts from the old settings on its own.
        Assert.Equal(2, prefs.For("save-2", "Alt", Format2(), out _).Ignored.Count);
    }

    [Fact]
    public void ARoundTripKeepsEachCharacter()
    {
        var (prefs, _) = CharacterPrefs.Read(_folder);
        var dev = prefs.For("save-1", "Dev", new Preferences(), out _);
        dev.Ignored = ["MelvorBase:VenomPit"];
        dev.A = new SideState(SideBase.Current, ChangeList.Parse("level=Defence:60 star=Terra_Minor_3:on"), true);
        dev.B = new SideState(SideBase.SavedRun("run-20261109-064515-1"));
        dev.Editing = SideLetter.B;
        prefs.For("save-2", "Alt", new Preferences(), out _).B = new SideState(SideBase.Loadout("MelvorBase:Loadout2"));
        prefs.Write(_folder);

        var text = File.ReadAllText(CharacterPrefs.PathIn(_folder));
        Assert.Contains("\"format\": 1", text, StringComparison.Ordinal);
        Assert.Contains("\"changes\": \"level=Defence:60 star=Terra_Minor_3:on\"", text, StringComparison.Ordinal);
        Assert.Contains("\"base\": \"run:run-20261109-064515-1\"", text, StringComparison.Ordinal);
        Assert.False(File.Exists(CharacterPrefs.PathIn(_folder) + ".tmp"));

        var (back, problem) = CharacterPrefs.Read(_folder);
        Assert.Null(problem);
        Assert.Equal(["save-1", "save-2"], back.Keys.Order(StringComparer.Ordinal));
        var read = back.For("save-1", "Dev", Format2(), out _);
        Assert.Equal(["MelvorBase:VenomPit"], read.Ignored);
        Assert.Equal(dev.A, read.A);
        Assert.Equal(dev.B, read.B);
        Assert.Equal(SideLetter.B, read.Editing);
        Assert.Equal(SideBase.Loadout("MelvorBase:Loadout2"), back.For("save-2", "Alt", Format2(), out _).B!.Base);
    }

    [Fact]
    public void IgnoresForTargetsTheBuildNoLongerHasAreDropped()
    {
        var (prefs, _) = CharacterPrefs.Read(_folder);
        prefs.For("save-1", "Dev", Format2(), out _);
        Assert.True(prefs.DropUnknown("save-1", new HashSet<string> { "MelvorBase:HobgolbinCamp" }));
        Assert.Equal(["MelvorBase:HobgolbinCamp"], prefs.For("save-1", "Dev", Format2(), out _).Ignored);
        Assert.False(prefs.DropUnknown("save-1", new HashSet<string> { "MelvorBase:HobgolbinCamp" }));
        Assert.False(prefs.DropUnknown("unseen", new HashSet<string>()));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{"format": 2, "characters": {}}""")]
    [InlineData("""{"format": 1, "characters": {"k": {"a": {"base": "run:r1", "changes": "level=Attack:5"}}}}""")]
    [InlineData("""{"format": 1, "characters": {"k": {"a": {"base": "current", "changes": "hat=On:1"}}}}""")]
    public void ADamagedFileReadsAsEmptyWithAProblem(string content)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(CharacterPrefs.PathIn(_folder), content);
        var (prefs, problem) = CharacterPrefs.Read(_folder);
        Assert.Empty(prefs.Keys);
        Assert.StartsWith(CharacterPrefs.FileName, problem, StringComparison.Ordinal);
        // The next write replaces it.
        prefs.For("save-1", "Dev", new Preferences(), out _);
        prefs.Write(_folder);
        Assert.Null(CharacterPrefs.Read(_folder).Problem);
    }
}
