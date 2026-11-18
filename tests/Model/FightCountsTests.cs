using System.Text.Json;
using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class FightCountsTests
{
    internal static FightCounts Sample(int scale = 1) =>
        new()
        {
            Dealt = new SideCounts
            {
                Attacks = 100 * scale,
                Hits = 40 * scale,
                Attack = 600 * scale,
                Effect = 30 * scale,
                Reflect = 5 * scale,
                Overkill = 20 * scale,
                Resisted = 70 * scale,
                WardCut = 8 * scale,
                ArmourCut = 3 * scale,
                WardDamage = 2 * scale,
                Healed = 11 * scale,
            },
            Taken = new SideCounts
            {
                Attacks = 90 * scale,
                Hits = 60 * scale,
                Attack = 900 * scale,
                Effect = 100 * scale,
            },
            Seconds = 250.5 * scale,
        };

    [Fact]
    public void DamageIsEveryCause()
    {
        var counts = Sample();
        Assert.Equal(635, counts.Dealt.Damage);
        Assert.Equal(1000, counts.Taken.Damage);
    }

    [Fact]
    public void AddSumsEveryField()
    {
        var sum = Sample();
        sum.Add(Sample(2));
        var three = Sample(3);
        Assert.Equal(Json(three), Json(sum));
    }

    [Fact]
    public void ClearZeroesEveryField()
    {
        var counts = Sample();
        counts.Clear();
        Assert.Equal(Json(new FightCounts()), Json(counts));
    }

    [Fact]
    public void CopyIsIndependent()
    {
        var counts = Sample();
        var copy = counts.Copy();
        counts.Clear();
        Assert.Equal(Json(Sample()), Json(copy));
    }

    [Fact]
    public void RoundTripsWithTheFileNames()
    {
        var json = Json(Sample());
        foreach (
            var name in new[]
            {
                "\"dealt\"",
                "\"taken\"",
                "\"seconds\"",
                "\"attacks\"",
                "\"hits\"",
                "\"attack\"",
                "\"effect\"",
                "\"reflect\"",
                "\"overkill\"",
                "\"resisted\"",
                "\"wardCut\"",
                "\"armourCut\"",
                "\"wardDamage\"",
                "\"healed\"",
            }
        )
            Assert.Contains(name, json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"damage\"", json, StringComparison.Ordinal);
        var back = JsonSerializer.Deserialize<FightCounts>(json, RunFile.Options)!;
        Assert.Equal(json, Json(back));
    }

    private static string Json(FightCounts counts) => JsonSerializer.Serialize(counts, RunFile.Options);
}
