using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class ComparisonsFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cs2-cmp-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    internal static Comparison Sample(string a = "run-a", string b = "run-b") =>
        new()
        {
            Id = Comparison.IdFor(b),
            Kind = ComparisonKind.NowAgainstSaved,
            A = a,
            B = b,
            Targets = ["t:1", "t:2"],
            Seed = ulong.MaxValue,
            SeedSet = true,
            CreatedAt = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc),
            Cs2Mvid = "mvid",
        };

    [Fact]
    public void RoundTripsNewestFirst()
    {
        var whatIf = Sample("run-c", "run-d");
        whatIf.Kind = ComparisonKind.WhatIf;
        ComparisonsFile.Write(_dir, [whatIf, Sample()]);
        var (read, problem) = ComparisonsFile.Read(_dir);
        Assert.Null(problem);
        Assert.Equal(["cmp-run-d", "cmp-run-b"], read.Select(c => c.Id));
        Assert.Equal(ComparisonKind.WhatIf, read[0].Kind);
        Assert.Equal(ComparisonKind.NowAgainstSaved, read[1].Kind);
        Assert.Equal(["t:1", "t:2"], read[1].Targets);
        Assert.Equal(ulong.MaxValue, read[1].Seed);
        Assert.True(read[1].SeedSet);
        var text = File.ReadAllText(ComparisonsFile.PathIn(_dir));
        Assert.StartsWith("{\"format\":1,\"comparisons\":[", text, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"whatIf\"", text, StringComparison.Ordinal);
        Assert.False(File.Exists(ComparisonsFile.PathIn(_dir) + ".tmp"));
    }

    [Fact]
    public void AnEmptyListRoundTrips()
    {
        ComparisonsFile.Write(_dir, []);
        var (read, problem) = ComparisonsFile.Read(_dir);
        Assert.Empty(read);
        Assert.Null(problem);
    }

    private string Single => Path.Combine(_dir, ComparisonsFile.SingleName);

    // The file from before several comparisons were kept, as the game wrote it.
    [Fact]
    public void TheSingleComparisonsFileIsTakenOverOnce()
    {
        Directory.CreateDirectory(_dir);
        File.Copy(TestPaths.Fixtures(Path.Combine("old-comparison", "comparison.json")), Single);
        var (read, problem) = ComparisonsFile.Read(_dir);
        Assert.Null(problem);
        var record = Assert.Single(read);
        Assert.Equal("cmp-run-20261109-171713-2", record.Id);
        Assert.Equal(ComparisonKind.WhatIf, record.Kind);
        Assert.Equal(("run-20261109-171713-1", "run-20261109-171713-2"), (record.A, record.B));
        Assert.Equal(["MelvorBase:GolbinForest/MelvorBase:Golbin"], record.Targets);
        Assert.False(File.Exists(Single));
        Assert.True(File.Exists(ComparisonsFile.PathIn(_dir)));
        // From now on the list is the only source.
        Assert.Equal("cmp-run-20261109-171713-2", Assert.Single(ComparisonsFile.Read(_dir).Comparisons).Id);
    }

    // Its two runs are still ordinary run files that read.
    [Fact]
    public void TheTakenOverComparisonsRunsRead()
    {
        foreach (var id in new[] { "run-20261109-171713-1", "run-20261109-171713-2" })
        {
            var run = RunFile.Read(TestPaths.Fixtures(Path.Combine("old-comparison", id + RunFile.Extension)));
            Assert.Equal(id, run.Id);
            Assert.True(run.IsFinal);
        }
    }

    [Fact]
    public void TheListWinsOverALeftoverSingleFile()
    {
        ComparisonsFile.Write(_dir, [Sample()]);
        File.Copy(TestPaths.Fixtures(Path.Combine("old-comparison", "comparison.json")), Single);
        Assert.Equal("cmp-run-b", Assert.Single(ComparisonsFile.Read(_dir).Comparisons).Id);
        Assert.True(File.Exists(Single));
    }

    [Theory]
    [InlineData("{\"format\":2,\"comparison\":{\"a\":\"x\",\"b\":\"y\"}}")]
    [InlineData("{\"format\":1}")]
    [InlineData("not json")]
    public void ABadSingleFileIsLeftAndNothingIsKept(string text)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Single, text);
        var (read, problem) = ComparisonsFile.Read(_dir);
        Assert.Empty(read);
        Assert.NotNull(problem);
        Assert.Equal(text, File.ReadAllText(Single));
        Assert.False(File.Exists(ComparisonsFile.PathIn(_dir)));
    }

    [Theory]
    [InlineData("current", null, "current", "level=Strength:60", ComparisonKind.WhatIf)]
    [InlineData("MelvorBase:Loadout2", null, "MelvorBase:Loadout2", "food=none", ComparisonKind.WhatIf)]
    [InlineData("current", null, "MelvorBase:Loadout2", null, ComparisonKind.TwoSetups)]
    [InlineData("current", null, "MelvorBase:Loadout2", "level=Strength:60", ComparisonKind.TwoSetups)]
    [InlineData("current", "level=Defence:60", "current", null, ComparisonKind.TwoSetups)]
    [InlineData("current", "level=Defence:60", "current", "level=Strength:60", ComparisonKind.TwoSetups)]
    public void AWhatIfIsTheSameBaseWithChangesOnBOnly(
        string baseA,
        string? changesA,
        string baseB,
        string? changesB,
        ComparisonKind kind
    )
    {
        static Setup Side(string loadout, string? changes) =>
            new() { Loadout = loadout, Changes = changes == null ? null : ChangeList.Parse(changes) };
        Assert.Equal(kind, Comparison.KindOf(Side(baseA, changesA), Side(baseB, changesB)));
    }

    // A what-if swapped has its changes on A, so it is two setups; the seed and targets stay, as no side fights again.
    [Fact]
    public void ASwapExchangesTheRunsAndKeepsTheSeed()
    {
        var record = Sample();
        record.Kind = ComparisonKind.WhatIf;
        var a = new Run
        {
            Id = "run-a",
            Setup = new() { Loadout = "current" },
            GameBuild = "g",
            Cs2Mvid = "mvid",
        };
        var b = new Run
        {
            Id = "run-b",
            Setup = new() { Loadout = "current", Changes = ChangeList.Parse("level=Strength:60") },
            GameBuild = "g",
            Cs2Mvid = "mvid",
        };
        var shown = new ComparedRuns(record, a, b, []);
        var swapped = shown.Swapped("mvid");
        Assert.Same(b, swapped.A);
        Assert.Same(a, swapped.B);
        Assert.Equal(("run-b", "run-a", "cmp-run-a"), (swapped.Record.A, swapped.Record.B, swapped.Record.Id));
        Assert.Equal(ComparisonKind.TwoSetups, swapped.Record.Kind);
        Assert.Equal(record.Seed, swapped.Record.Seed);
        Assert.Equal(record.Targets, swapped.Record.Targets);
        Assert.Same(shown, swapped.SwappedFrom);
        Assert.Equal(ComparisonKind.WhatIf, swapped.Swapped("mvid").Record.Kind);
        Assert.Empty(swapped.Warnings);
    }

    [Fact]
    public void NoFileIsNoComparisonsAndNoProblem()
    {
        var (read, problem) = ComparisonsFile.Read(_dir);
        Assert.Empty(read);
        Assert.Null(problem);
    }

    [Theory]
    [InlineData("{\"format\":2,\"comparisons\":[{\"a\":\"x\",\"b\":\"y\"}]}")]
    [InlineData("not json")]
    public void AnotherFormatOrAnUnreadableFileIsLeftAsItIs(string text)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(ComparisonsFile.PathIn(_dir), text);
        var (read, problem) = ComparisonsFile.Read(_dir);
        Assert.Empty(read);
        Assert.NotNull(problem);
        Assert.Equal(text, File.ReadAllText(ComparisonsFile.PathIn(_dir)));
    }

    [Fact]
    public void EntriesWithoutBothRunsAreLeftOut()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(
            ComparisonsFile.PathIn(_dir),
            """
            {"format":1,"comparisons":[{"id":"cmp-x","a":"","b":"run-x"},
            {"id":"cmp-run-b","kind":"twoSetups","a":"run-a","b":"run-b","targets":["t:1"],"seed":5}]}
            """
        );
        var (read, problem) = ComparisonsFile.Read(_dir);
        Assert.Null(problem);
        Assert.Equal("cmp-run-b", Assert.Single(read).Id);
    }
}
