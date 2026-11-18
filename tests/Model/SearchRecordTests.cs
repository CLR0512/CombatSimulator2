using System.Text.Json;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Model.Search;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class SearchRecordTests
{
    [Fact]
    public void RoundTripsEveryField()
    {
        var request = new SearchRequest(
            SearchKind.BestGear,
            new Setup
            {
                Character = "Dev",
                Loadout = "1",
                LoadoutName = "my secret loadout",
            },
            "area:1/enemy:2",
            new Goal("xp:main", DeathLimit.UnderFivePercent),
            400,
            4
        )
        {
            Locks = new HashSet<string> { "melvorD:Weapon" },
            Excluded = new HashSet<string> { "melvorD:Bronze_Sword" },
            AllStyles = true,
            Limits = new TrialLimits { EnemySeconds = 60 },
            SlotAtATime = true,
        };
        var record = SearchRecord.Of("search-1", request, 0.02, 9, "host", "abc", "setup-1", "26316041", "mvid-1");
        record.State = "finished";
        record.Answer = new SearchOutcome
        {
            State = "Found",
            Best = "gear=Ring:melvorD:Gold_Ring",
            Ties = ["gear=Ring:melvorD:Silver_Ring"],
            Stages = ["200/41/6", "6/4/2"],
        };
        record.Failure = new SearchFailure(2, "target", "area:1/enemy:2");
        record.Snapshot = "snapshots/search-1.save";
        var json = JsonSerializer.SerializeToElement(new { searches = new[] { record.ToJson() } });

        var back = Assert.Single(SearchRecord.Read(json));

        Assert.DoesNotContain("my secret", json.GetRawText(), StringComparison.Ordinal);
        Assert.Equal("search-1", back.Id);
        Assert.Equal("search", back.Kind);
        Assert.Equal("area:1/enemy:2", back.Target);
        Assert.Equal(new Goal("xp:main", DeathLimit.UnderFivePercent), back.Goal);
        Assert.Equal(["melvorD:Weapon"], back.Locks);
        Assert.Equal(["melvorD:Bronze_Sword"], back.Excluded);
        Assert.True(back.AllStyles);
        Assert.Equal(0.02, back.Share);
        Assert.Equal(400, back.Trials);
        Assert.Equal(9UL, back.Seed);
        Assert.Equal(4, back.Copies);
        Assert.Equal("host", back.Where);
        Assert.Equal(60, back.Limits!.EnemySeconds);
        Assert.True(back.SlotAtATime);
        Assert.Equal("1", back.Setup.Loadout);
        Assert.Equal("abc", back.SnapshotSha256);
        Assert.Equal("setup-1", back.SetupHash);
        Assert.Equal("finished", back.State);
        Assert.Equal("gear=Ring:melvorD:Gold_Ring", back.Answer!.Best);
        Assert.Equal(["gear=Ring:melvorD:Silver_Ring"], back.Answer.Ties);
        Assert.Equal(["200/41/6", "6/4/2"], back.Answer.Stages);
        Assert.Equal(new SearchFailure(2, "target", "area:1/enemy:2"), back.Failure);
        Assert.Equal("snapshots/search-1.save", back.Snapshot);
        Assert.Empty(back.Missing());
    }

    [Fact]
    public void AnOlderReportHasNoRecords()
    {
        using var doc = JsonDocument.Parse("""[{ "target": "area:1/enemy:2" }]""");

        Assert.Empty(SearchRecord.Read(doc.RootElement));
    }
}
