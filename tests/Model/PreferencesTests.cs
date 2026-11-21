using System.Text.Json;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class PreferencesTests
{
    // As Core's config loader reads it.
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private static Preferences Load(string json)
    {
        var prefs = JsonSerializer.Deserialize<Preferences>(json, Json)!;
        prefs.Upgrade();
        return prefs;
    }

    [Fact]
    public void AVersion1FileLoadsWithTheNewDefaults()
    {
        var prefs = Load(
            """
            {
              "SelectedTarget": "MelvorBase:GolbinForest/MelvorBase:Golbin",
              "DisabledTargets": ["MelvorBase:HobgolbinCamp"],
              "Metric": "kills",
              "TimeUnit": "minute",
              "Trials": 500
            }
            """
        );
        Assert.Equal(Preferences.CurrentFormat, prefs.Format);
        Assert.True(prefs.ShowHeaderButton);
        Assert.Equal("simulate", prefs.View);
        Assert.Equal("chart", prefs.ResultsMode);
        Assert.Empty(prefs.FoldedAreas);
        Assert.Empty(prefs.Pins);
        Assert.Equal(Metrics.MainXp, prefs.MetricDefault);
        Assert.Equal(20, prefs.KeepRuns);
        // "kills" was version 1's default, not a choice: the new default takes over.
        Assert.Equal(Metrics.MainXp, prefs.Metric);
        Assert.Equal("minute", prefs.TimeUnit);
        Assert.Equal(500, prefs.Trials);
        Assert.Equal(["MelvorBase:HobgolbinCamp"], prefs.DisabledTargets);
    }

    [Fact]
    public void AVersion2FileKeepsTheMetricChosen()
    {
        var prefs = Load("""{ "Format": 2, "Metric": "kills", "MetricDefault": "xp:MelvorBase:Attack" }""");
        Assert.Equal("kills", prefs.Metric);
        Assert.Equal("xp:MelvorBase:Attack", prefs.MetricDefault);
    }

    [Fact]
    public void NewPreferencesUseTheDefaults()
    {
        var prefs = new Preferences();
        prefs.Upgrade();
        Assert.Equal(Metrics.MainXp, prefs.Metric);
        Assert.Equal(Preferences.CurrentFormat, prefs.Format);
    }

    [Fact]
    public void PinsHoldAtMostThreeAndUnknownKeysAreDropped()
    {
        var prefs = Load("""{ "Format": 2, "Pins": ["a", "b", "gone", "c", "d"] }""");
        Assert.Equal(["a", "b", "gone"], prefs.Pins);
        prefs.DropUnknown(new HashSet<string> { "a", "b", "c", "d" });
        Assert.Equal(["a", "b"], prefs.Pins);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(5, 5)]
    [InlineData(37, 37)]
    [InlineData(100, 100)]
    [InlineData(1000, 100)]
    public void KeepRunsIsClampedTo5Through100(int saved, int kept) =>
        Assert.Equal(kept, Load($$"""{ "Format": 2, "KeepRuns": {{saved}} }""").KeepRuns);

    [Fact]
    public void UnknownFoldedAreasAreDropped()
    {
        var prefs = new Preferences { FoldedAreas = ["MelvorBase:GolbinForest", "Gone:Area"] };
        Assert.True(
            prefs.DropUnknown(
                new HashSet<string> { "MelvorBase:GolbinForest/MelvorBase:Golbin" },
                new HashSet<string> { "MelvorBase:GolbinForest" }
            )
        );
        Assert.Equal(["MelvorBase:GolbinForest"], prefs.FoldedAreas);
    }

    [Fact]
    public void ARoundTripKeepsEveryField()
    {
        var prefs = new Preferences
        {
            SelectedTarget = "a/b",
            DisabledTargets = ["c"],
            Metric = "kills",
            TimeUnit = "day",
            Setup = "slot1",
            IgnoreRequirements = true,
            Copies = 3,
            Trials = 2500,
            Seed = 7,
            ShowHeaderButton = false,
            View = "settings",
            ResultsMode = "chart",
            FoldedAreas = ["x"],
            Pins = ["a/b", "d"],
            MetricDefault = "kills",
            KeepRuns = 42,
            Skin = "melvui",
        };
        prefs.Upgrade();
        var text = JsonSerializer.Serialize(prefs, Json);
        var back = Load(text);
        Assert.Equal(text, JsonSerializer.Serialize(back, Json));
        Assert.Equal("chart", back.ResultsMode);
        Assert.Equal(42, back.KeepRuns);
        Assert.Equal("melvui", back.Skin);
    }

    [Fact]
    public void SkinDefaultsToBaseGame()
    {
        Assert.Equal("base", new Preferences().Skin);
        Assert.Equal("base", Load("""{ "Format": 2 }""").Skin);
    }

    [Theory]
    [InlineData("melvui", "melvui")]
    [InlineData("base", "base")]
    [InlineData("MElvUI", "melvui")]
    [InlineData("fancy", "base")]
    [InlineData("", "base")]
    public void UnknownSkinsReadAsBase(string saved, string kept) =>
        Assert.Equal(kept, Load($$"""{ "Format": 2, "Skin": "{{saved}}" }""").Skin);

    [Fact]
    public void AnOldTextSizeIsIgnoredAndNotWrittenBack()
    {
        var prefs = Load("""{ "Format": 3, "TextScale": 1.3 }""");
        Assert.Equal(ScaleSteps.Default, prefs.Scale);
        Assert.DoesNotContain("TextScale", JsonSerializer.Serialize(prefs, Json), StringComparison.Ordinal);
    }

    [Fact]
    public void AScaleRoundTripsExactly()
    {
        var prefs = new Preferences { Scale = 1.1 };
        Assert.Equal(1.1, Load(JsonSerializer.Serialize(prefs, Json)).Scale);
    }

    [Theory]
    [InlineData(0.3, 0.70)]
    [InlineData(1.12, 1.10)]
    [InlineData(2.4, 1.50)]
    public void AnOutOfStepScaleSnapsToTheNearestStep(double saved, double kept) =>
        Assert.Equal(kept, Load($$"""{ "Format": 3, "Scale": {{saved}} }""").Scale);

    [Fact]
    public void SimulateInDefaultsToTheHost()
    {
        Assert.Equal("host", new Preferences().SimulateIn);
        Assert.Equal("host", Load("{}").SimulateIn);
    }

    [Theory]
    [InlineData("elsewhere")]
    [InlineData("")]
    [InlineData("HOST")]
    public void UnknownSimulateInValuesReadAsTheHost(string value)
    {
        Assert.Equal("host", Load($$"""{ "SimulateIn": "{{value}}" }""").SimulateIn);
    }

    [Fact]
    public void SimulateInGameRoundTrips()
    {
        var prefs = new Preferences { SimulateIn = "game" };
        var back = Load(JsonSerializer.Serialize(prefs, Json));
        Assert.Equal("game", back.SimulateIn);
    }

    [Fact]
    public void UnknownTargetsAreDroppedOnLoad()
    {
        var prefs = new Preferences
        {
            SelectedTarget = "Gone:Area/Gone:Enemy",
            Pins = ["MelvorBase:HobgolbinCamp", "Gone:Dungeon"],
        };
        var changed = prefs.DropUnknown(
            new HashSet<string> { "MelvorBase:HobgolbinCamp", "MelvorBase:A/MelvorBase:B" }
        );
        Assert.True(changed);
        Assert.Null(prefs.SelectedTarget);
        Assert.Equal(["MelvorBase:HobgolbinCamp"], prefs.Pins);
        Assert.False(prefs.DropUnknown(new HashSet<string> { "MelvorBase:HobgolbinCamp" }));
    }

    [Fact]
    public void DefaultsMatchTheDocumentedOnes()
    {
        var prefs = new Preferences();
        Assert.Equal(Metrics.MainXp, prefs.Metric);
        Assert.Equal(TimeUnit.Hour, prefs.Unit);
        Assert.Null(prefs.Setup);
        Assert.Null(prefs.IgnoreRequirements);
        Assert.Equal(1000, prefs.Trials);
        Assert.Null(prefs.Copies);
        Assert.Equal(1UL, prefs.Seed);
        Assert.Null(prefs.DisabledTargets);
    }

    [Fact]
    public void AnUnknownTimeUnitFallsBackToHour()
    {
        var prefs = new Preferences { TimeUnit = "fortnight" };
        Assert.Equal(TimeUnit.Hour, prefs.Unit);
    }

    [Fact]
    public void AFormat2FileReadsWithTheNewFieldsAtTheirDefaults()
    {
        var prefs = Load(
            """
            {
              "Format": 2,
              "DisabledTargets": ["MelvorBase:HobgolbinCamp"],
              "Setup": "MelvorBase:Loadout2",
              "IgnoreRequirements": true,
              "Metric": "kills"
            }
            """
        );
        Assert.Equal(3, prefs.Format);
        Assert.Null(prefs.Motion);
        Assert.Equal(new TargetFilter(), prefs.Filter);
        Assert.Equal("kills", prefs.Metric);
        // Still read, so each character's own state can start from them.
        Assert.Equal(["MelvorBase:HobgolbinCamp"], prefs.DisabledTargets);
        Assert.Equal("MelvorBase:Loadout2", prefs.Setup);
        Assert.True(prefs.IgnoreRequirements);
    }

    [Fact]
    public void Format3RoundTripsMotionAndTheFilterWithoutItsSearch()
    {
        var prefs = new Preferences
        {
            Motion = MotionLevel.Reduced,
            Filter = new TargetFilter
            {
                Search = "golbin",
                CanFightOnly = false,
                Kinds = TargetTags.Monsters | TargetTags.Slayer,
                Areas = ["MelvorBase:GolbinForest"],
            },
        };
        prefs.Upgrade();
        var text = JsonSerializer.Serialize(prefs, Json);
        Assert.Contains("\"Reduced\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("golbin", text, StringComparison.Ordinal);
        var back = Load(text);
        Assert.Equal(MotionLevel.Reduced, back.Motion);
        Assert.Equal(prefs.Filter with { Search = "" }, back.Filter);
        Assert.Equal(text, JsonSerializer.Serialize(back, Json));
    }

    [Fact]
    public void AMissingFilterReadsAsTheDefault() =>
        Assert.Equal(new TargetFilter(), Load("""{ "Format": 3, "Filter": null }""").Filter);

    [Fact]
    public void TheTieShareDefaultsToOnePercent()
    {
        Assert.Equal(0.01, new Preferences().TieShare);
        Assert.Equal(0.01, Load("""{ "Format": 3 }""").TieShare);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("0.025", 0.025)]
    [InlineData("0.05", 0.05)]
    [InlineData("0.2", 0.05)]
    [InlineData("-1", 0)]
    public void TheTieShareIsClampedToZeroThroughFivePercent(string saved, double kept) =>
        Assert.Equal(kept, Load($$"""{ "Format": 3, "TieShare": {{saved}} }""").TieShare);

    [Fact]
    public void SearchKindsStartOnTheDefaultsWithFoodOff()
    {
        var prefs = Load("{}");
        Assert.Equal(ChoiceKinds.Defaults, prefs.SearchKindList);
        Assert.False(prefs.SearchIncludeUnowned);
    }

    [Fact]
    public void SearchKindsRoundTripAndDropUnknownNames()
    {
        var prefs = Load("""{ "SearchKinds": ["food", "prayers", "tarot", "food"], "SearchIncludeUnowned": true }""");
        Assert.Equal([ChoiceKind.Prayers, ChoiceKind.Food], prefs.SearchKindList);
        var again = Load(JsonSerializer.Serialize(prefs, Json));
        Assert.Equal(["prayers", "food"], again.SearchKinds);
        Assert.True(again.SearchIncludeUnowned);
    }

    [Fact]
    public void TheSearchPoolStartsOnOwnedWithNothingNarrowed()
    {
        var prefs = Load("{}");
        Assert.Equal(ItemPool.Owned, prefs.SearchPool);
        Assert.False(prefs.SearchPoolWearable);
        Assert.False(prefs.SearchPoolReached);
        Assert.Empty(prefs.SearchLeftOut);
    }

    [Fact]
    public void TheSearchPoolRoundTripsAndDropsRepeatedSources()
    {
        var prefs = Load(
            """{ "SearchPool": 1, "SearchPoolWearable": true, "SearchPoolReached": true, "SearchLeftOut": ["Shop", "", "Shop", "Golbin"] }"""
        );
        Assert.Equal(["Shop", "Golbin"], prefs.SearchLeftOut);
        var again = Load(JsonSerializer.Serialize(prefs, Json));
        Assert.Equal(ItemPool.Every, again.SearchPool);
        Assert.True(again.SearchPoolWearable);
        Assert.True(again.SearchPoolReached);
        Assert.Equal(["Shop", "Golbin"], again.SearchLeftOut);
    }

    [Fact]
    public void AnUnknownSearchPoolIsOwned() =>
        Assert.Equal(ItemPool.Owned, Load("""{ "SearchPool": 7 }""").SearchPool);

    [Fact]
    public void NoSearchKindsStaysNone() => Assert.Empty(Load("""{ "SearchKinds": [] }""").SearchKindList);

    [Fact]
    public void ALanguageIsKeptAndAFileWithoutOneFollowsTheGame()
    {
        Assert.Null(Load("""{"format":3}""").Language);
        var prefs = Load("""{"format":3,"language":"pt_BR"}""");
        Assert.Equal("pt_BR", prefs.Language);
        Assert.Equal(Preferences.CurrentFormat, prefs.Format);
        Assert.Contains("\"Language\": \"pt_BR\"", JsonSerializer.Serialize(prefs, Json), StringComparison.Ordinal);
        Assert.DoesNotContain(
            "\"Language\"",
            JsonSerializer.Serialize(new Preferences(), Json),
            StringComparison.Ordinal
        );
    }
}
