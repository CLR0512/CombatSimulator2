namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// What the results hash covers: each target's state, reasons in English, trials, batches and fights.
/// </summary>
public static class ResultsHashInput
{
    public static byte[] Of(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return Of(run.Targets.Select(k => run.Results[k]));
    }

    /// <summary>The same input for results on their own, in the order given: a run of those targets hashes the same.</summary>
    public static byte[] Of(IEnumerable<TargetResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(results.Select(Part), RunFile.Options);
    }

    // A master's figures are its monsters': its entry adds theirs, and every other target's is as it always was.
    private static object Part(TargetResult r)
    {
        var reasons = r.Reasons.Select(n => n.English());
        if (r.Parts is not { } parts)
            return new
            {
                r.Key,
                r.State,
                Reasons = reasons,
                r.TrialsDone,
                r.Batches,
                r.Fights,
            };
        return new
        {
            r.Key,
            r.State,
            Reasons = reasons,
            r.TrialsDone,
            r.Blocked,
            Parts = parts.Select(Part),
        };
    }

    /// <summary>The short hash <c>cs2 result</c> prints for a run of exactly these results.</summary>
    public static string Brief(IEnumerable<TargetResult> results) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Of(results)))[..12];
}
