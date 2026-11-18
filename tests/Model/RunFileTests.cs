using System.IO.Compression;
using System.Text;
using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

/// <summary>A run's mark for a game build CS2 wasn't checked with, on disk and in the results hash.</summary>
public sealed class RunFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cs2-checked-" + Guid.NewGuid().ToString("N"));

    public RunFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private string PathOf(string name) => Path.Combine(_dir, name + RunFile.Extension);

    private static string Unzipped(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [Fact]
    public void AnUncheckedRunKeepsItsMark()
    {
        var run = ResultsModelTests.SampleRun();
        run.Checked = false;
        var path = PathOf(run.Id);
        RunFile.Write(path, run);
        Assert.False(RunFile.Read(path).Checked);
        Assert.False(RunFile.ReadHeader(path).Checked);
    }

    // A checked run writes nothing, so its file is what CS2 wrote before the mark existed.
    [Fact]
    public void ACheckedRunWritesNoMark()
    {
        var run = ResultsModelTests.SampleRun();
        var path = PathOf(run.Id);
        RunFile.Write(path, run);
        Assert.DoesNotContain("\"checked\"", Unzipped(path), StringComparison.Ordinal);
        Assert.Null(RunFile.Read(path).Checked);
        Assert.Null(RunFile.ReadHeader(path).Checked);
    }

    [Fact]
    public void AnOldFileReadsUnmarked()
    {
        const string old = """
            {"format":3,"header":{"id":"run-1","state":"finished","setup":"current","ignoreRequirements":false,
            "gameBuild":"b","seed":1,"targets":0,"startedAt":"2026-11-06T12:00:00Z","endedAt":null},
            "run":{"id":"run-1","setup":{"character":"Dev","loadout":"current","ignoreRequirements":false},
            "snapshotSha256":"","gameBuild":"b","seed":1,"trials":1000,"copies":0,"targets":[],"state":"finished",
            "startedAt":"2026-11-06T12:00:00Z","results":{}}}
            """;
        var path = PathOf("run-1");
        using (var file = File.Create(path))
        using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
            gzip.Write(Encoding.UTF8.GetBytes(old));
        Assert.Null(RunFile.Read(path).Checked);
        Assert.Null(RunFile.ReadHeader(path).Checked);
    }

    // The same setup and seed give the same results hash on a checked game and an unchecked one.
    [Fact]
    public void TheMarkIsNotPartOfTheResultsHash()
    {
        var run = ResultsModelTests.SampleRun();
        var unmarked = ResultsHashInput.Of(run);
        run.Checked = false;
        Assert.Equal(unmarked, ResultsHashInput.Of(run));
        run.Checked = true;
        Assert.Equal(unmarked, ResultsHashInput.Of(run));
    }

    [Fact]
    public void AKeptComparisonIsUncheckedWhenEitherSideIs()
    {
        var a = RunHeader.Of(ResultsModelTests.SampleRun());
        var b = RunHeader.Of(ResultsModelTests.SampleRun());
        var record = new Comparison { A = "a", B = "b" };
        Assert.False(new KeptComparison(record, a, b).Unchecked);
        b.Checked = false;
        Assert.True(new KeptComparison(record, a, b).Unchecked);
        Assert.False(new KeptComparison(record, a, null).Unchecked);
    }
}
