namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// The wider run that the results on screen replaced, so a run of a few targets can say what it left out and lead
/// back to it. Kept for the session only: after a restart nothing was replaced on screen.
/// </summary>
/// <param name="RunId">A final, saved run.</param>
/// <param name="Targets">How many targets it has.</param>
public sealed record EarlierResults(string RunId, int Targets)
{
    /// <summary>
    /// The earlier results once the run shown changes from <paramref name="replaced"/> to the run
    /// <paramref name="shownId"/>, given the ones <paramref name="held"/> before. The replaced run becomes the earlier results when the shown
    /// one has fewer targets and the replaced one is final and saved; a run narrower still keeps the widest one
    /// held (a full run, one target, then another target: still the full run). They are forgotten when the shown
    /// run has as many targets or more, is the earlier run itself, or that run is no longer saved.
    /// </summary>
    /// <param name="replaced">The run that was on screen, or null at the session's first.</param>
    /// <param name="shownId">The id of the run now on screen.</param>
    /// <param name="shownTargets">Its target count; for a comparison, side A's.</param>
    /// <param name="isSaved">Whether a run id is on disk now.</param>
    public static EarlierResults? After(
        Run? replaced,
        string shownId,
        int shownTargets,
        EarlierResults? held,
        Func<string, bool> isSaved
    )
    {
        ArgumentNullException.ThrowIfNull(shownId);
        ArgumentNullException.ThrowIfNull(isSaved);
        if (held != null && (held.RunId == shownId || held.Targets <= shownTargets || !isSaved(held.RunId)))
            held = null;
        if (held != null)
            return held;
        return
            replaced is { IsFinal: true, Id.Length: > 0 }
            && replaced.Id != shownId
            && replaced.Targets.Count > shownTargets
            && isSaved(replaced.Id)
            ? new EarlierResults(replaced.Id, replaced.Targets.Count)
            : null;
    }
}
