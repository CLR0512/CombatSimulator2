using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class RunFileFormatTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cs2-tests-" + Guid.NewGuid().ToString("N"));

    public RunFileFormatTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    internal static string Format2Run => TestPaths.Fixtures(Path.Combine("format2", "run.json.gz"));

    [Fact]
    public void AFormat2RunReadsWithoutCounts()
    {
        var run = RunFile.Read(Format2Run);
        Assert.NotEmpty(run.Results);
        foreach (var result in run.Results.Values)
        {
            Assert.False(result.HasFightCounts);
            Assert.All(result.Batches, b => Assert.Null(b.Fight));
            Assert.All(result.Batches, b => Assert.Null(b.RanOver));
        }
        Assert.Contains(run.Results.Values, r => r.HasEstimates);
    }

    [Fact]
    public void AFormat2RunWritesBackAsFormat3WithoutTheNewMembers()
    {
        var run = RunFile.Read(Format2Run);
        var path = Path.Combine(_dir, run.Id + RunFile.Extension);
        RunFile.Write(path, run);
        var root = JsonNode.Parse(Unzip(path))!;
        Assert.Equal(3, (int)root["format"]!);
        var json = root.ToJsonString();
        Assert.DoesNotContain("\"fight\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ranOver\"", json, StringComparison.Ordinal);
        Assert.Equal(Json(run), Json(RunFile.Read(path)));
    }

    [Fact]
    public void AFormat3RunRoundTripsItsCounts()
    {
        var run = ResultsModelTests.SampleRun();
        foreach (var result in run.Results.Values)
        foreach (var batch in result.Batches)
        {
            batch.Fight = FightCountsTests.Sample();
            batch.RanOver = 1;
        }
        var path = Path.Combine(_dir, run.Id + RunFile.Extension);
        RunFile.Write(path, run);
        var back = RunFile.Read(path);
        Assert.Equal(Json(run), Json(back));
        Assert.All(back.Results.Values.Where(r => r.Batches.Count > 0), r => Assert.True(r.HasFightCounts));
    }

    [Fact]
    public void ANewerFormatIsRefused()
    {
        var run = ResultsModelTests.SampleRun();
        var path = Path.Combine(_dir, run.Id + RunFile.Extension);
        RunFile.Write(path, run);
        var root = JsonNode.Parse(Unzip(path))!;
        root["format"] = 4;
        Zip(path, root.ToJsonString());
        var error = Assert.Throws<RunFileException>(() => RunFile.Read(path));
        Assert.Contains("format 4", error.Message, StringComparison.Ordinal);
        Assert.Throws<RunFileException>(() => RunFile.ReadHeader(path));
    }

    private static string Json(Run run) => System.Text.Json.JsonSerializer.Serialize(run, RunFile.Options);

    private static string Unzip(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static void Zip(string path, string json)
    {
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.Fastest);
        gzip.Write(Encoding.UTF8.GetBytes(json));
    }
}
