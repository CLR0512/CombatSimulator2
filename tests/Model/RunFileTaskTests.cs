using MelvorMods.CombatSimulator2.Model;
using Xunit;

namespace MelvorMods.CombatSimulator2.Tests;

public sealed class RunFileTaskTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cs2-tests-" + Guid.NewGuid().ToString("N"));

    public RunFileTaskTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private Run RoundTrip(Run run)
    {
        var path = Path.Combine(_dir, run.Id + RunFile.Extension);
        RunFile.Write(path, run);
        return RunFile.Read(path);
    }

    [Fact]
    public void AMonsterOnTaskKeepsItsMasterAndOneNoMasterGivesItsNote()
    {
        var run = ResultsModelTests.SampleRun();
        var keys = run.Targets.Take(2).ToList();
        run.Results[keys[0]].Task = "MelvorBase:Allevia";
        run.Results[keys[1]].TaskNote = Note.Of("no master gives it");
        var back = RoundTrip(run);
        Assert.Equal("MelvorBase:Allevia", back.Results[keys[0]].Task);
        Assert.Null(back.Results[keys[0]].TaskNote);
        Assert.Equal("no master gives it", back.Results[keys[1]].TaskNote!.English());
        Assert.Null(back.Results[keys[1]].Task);
    }

    [Fact]
    public void AMonsterAsTheSaveWritesNeither()
    {
        var run = ResultsModelTests.SampleRun();
        var json = System.Text.Json.JsonSerializer.Serialize(run.Results[run.Targets[0]], RunFile.Options);
        Assert.DoesNotContain("\"task", json, StringComparison.OrdinalIgnoreCase);
        var back = RoundTrip(run);
        Assert.All(back.Results.Values, r => Assert.Null(r.Task));
        Assert.All(back.Results.Values, r => Assert.Null(r.TaskNote));
    }
}
