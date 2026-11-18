using System.Globalization;
using System.Text;

namespace MelvorMods.CombatSimulator2.Model;

/// <summary>
/// A failure as a report records it and a rerun sees it: the kind's name, and the phase, target and host exit code
/// when known.
/// </summary>
public sealed record FailureFacts(string Kind, string? Phase = null, string? Target = null, int? ExitCode = null)
{
    public override string ToString()
    {
        var text = new StringBuilder("failure ").Append(Kind);
        if (Phase != null)
            text.Append(" phase ").Append(Phase);
        if (Target != null)
            text.Append(" target ").Append(Target);
        if (ExitCode is { } code)
            text.Append(" exit ").Append(code.ToString(CultureInfo.InvariantCulture));
        return text.ToString();
    }
}

/// <summary>
/// What a rerun of one run, side or search came to. <see cref="Where"/> is where it was rerun; <see cref="Notes"/>
/// carries what the maintainer should know beside the match.
/// </summary>
public sealed record ReplayOutcome(
    string Id,
    char? Side,
    bool Matched,
    string Expected,
    string Got,
    string Where,
    IReadOnlyList<string> Notes
)
{
    /// <summary>The reply's line: <c>replay &lt;id&gt; [side] in &lt;where&gt;: matched, &lt;got&gt;</c>.</summary>
    public string Line()
    {
        var side = Side is { } s ? " " + s : "";
        var text = $"replay {Id}{side} in {Where}: ";
        return Matched ? $"{text}matched, {Got}" : $"{text}not matched, {Got} (report {Expected})";
    }
}

/// <summary>When a rerun counts as the run the report recorded.</summary>
public static class ReplayMatch
{
    /// <summary>
    /// A failed run matches when one of the rerun's failures is of the same kind, in the same phase and on the same
    /// target where the report records them. A host crash matches whatever its exit code.
    /// </summary>
    public static bool Matches(FailureFacts expected, FailureFacts got)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(got);
        return expected.Kind == got.Kind
            && (expected.Phase == null || expected.Phase == got.Phase)
            && (expected.Target == null || expected.Target == got.Target);
    }

    public static ReplayOutcome Failure(
        string id,
        char? side,
        string where,
        FailureFacts expected,
        IReadOnlyList<FailureFacts> got,
        IReadOnlyList<string> notes
    )
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(got);
        var match = got.FirstOrDefault(g => Matches(expected, g));
        var shown = match ?? (got.Count > 0 ? got[0] : null);
        return new ReplayOutcome(
            id,
            side,
            match != null,
            expected.ToString(),
            shown?.ToString() ?? "no failure",
            where,
            notes
        );
    }

    /// <summary>A finished run matches on an equal results hash; a rerun that failed or ended without one doesn't.</summary>
    public static ReplayOutcome Hash(
        string id,
        char? side,
        string where,
        string expected,
        string? got,
        FailureFacts? gotFailure,
        IReadOnlyList<string> notes
    )
    {
        var shown =
            gotFailure != null ? gotFailure.ToString()
            : got != null ? "hash " + got
            : "no result";
        return new ReplayOutcome(
            id,
            side,
            gotFailure == null && got == expected,
            "hash " + expected,
            shown,
            where,
            notes
        );
    }

    /// <summary>A search matches on its state, best set, ties and every stage's counts.</summary>
    public static ReplayOutcome Search(
        string id,
        string where,
        SearchOutcome expected,
        SearchOutcome? got,
        IReadOnlyList<string> notes
    )
    {
        ArgumentNullException.ThrowIfNull(expected);
        var matched =
            got != null
            && got.State == expected.State
            && got.Best == expected.Best
            && got.Ties.SequenceEqual(expected.Ties)
            && got.Stages.SequenceEqual(expected.Stages);
        return new ReplayOutcome(
            id,
            null,
            matched,
            Describe(expected),
            got == null ? "no answer" : Describe(got),
            where,
            notes
        );
    }

    /// <summary>A failed search matches on the failure, and on the stage it failed at.</summary>
    public static ReplayOutcome SearchFailure(
        string id,
        string where,
        SearchFailure expected,
        SearchFailure? got,
        IReadOnlyList<string> notes
    )
    {
        ArgumentNullException.ThrowIfNull(expected);
        var matched =
            got != null
            && got.Stage == expected.Stage
            && Matches(
                new FailureFacts(expected.Kind, Target: expected.Target),
                new FailureFacts(got.Kind, Target: got.Target)
            );
        return new ReplayOutcome(
            id,
            null,
            matched,
            Describe(expected),
            got == null ? "no failure" : Describe(got),
            where,
            notes
        );
    }

    private static string Describe(SearchOutcome outcome)
    {
        var text = new StringBuilder(outcome.State.ToLowerInvariant());
        text.Append(", best set ").Append(outcome.Best ?? "none");
        if (outcome.Ties.Count > 0)
            text.Append(", ties ").AppendJoin(", ", outcome.Ties);
        text.Append(", stages ").AppendJoin(' ', outcome.Stages);
        return text.ToString();
    }

    private static string Describe(SearchFailure failure) =>
        $"failure {failure.Kind} at stage {failure.Stage.ToString(CultureInfo.InvariantCulture)}"
        + (failure.Target is { } t ? " target " + t : "");
}
