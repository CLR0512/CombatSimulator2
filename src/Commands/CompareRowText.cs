using System.Globalization;
using System.Text;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Commands;

/// <summary>A comparison's rows as command text.</summary>
internal static class CompareRowText
{
    public static string Row(Difference d)
    {
        var text = new StringBuilder(Ids.BriefKey(d.Key));
        text.Append("  A=").Append(d.A == null ? "—" : Fmt.Range(d.A));
        text.Append("  B=").Append(d.B == null ? "—" : Fmt.Range(d.B));
        if (d.Delta != null)
            text.Append("  d=").Append(Fmt.Range(d.Delta));
        if (d.Percent != null)
            text.Append(
                CultureInfo.InvariantCulture,
                $"  pct={Fmt.N(d.Percent.Point * 100)}% [{Fmt.N(d.Percent.Low * 100)}, {Fmt.N(d.Percent.High * 100)}]"
            );
        text.Append("  ").Append(Unfinished(d) ?? Died(d) ?? Verdict(d.Verdict));
        if (d.RanOverA is { } a)
            text.Append(CultureInfo.InvariantCulture, $"  a-ran-over {a.RanOver}/{a.Trials}");
        if (d.RanOverB is { } b)
            text.Append(CultureInfo.InvariantCulture, $"  b-ran-over {b.RanOver}/{b.Trials}");
        var notes = new List<string>();
        if (d.ReasonsA != null)
            notes.Add("A: " + d.ReasonsA.English("; "));
        if (d.ReasonsB != null)
            notes.Add("B: " + d.ReasonsB.English("; "));
        if (d.SkillsDiffer)
            notes.Add("skills differ");
        if (notes.Count > 0)
            text.Append("  ").Append(string.Join(" | ", notes));
        return text.ToString();
    }

    /// <summary>
    /// One target's fight numbers for both sides, each with the difference and its range; a side without counts
    /// reads "not recorded" and a side whose trial ran over with none finished gives its reason, neither with a
    /// difference.
    /// </summary>
    public static string FightRows(Run a, Run b, string target)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var ra = a.Results.GetValueOrDefault(target);
        var rb = b.Results.GetValueOrDefault(target);
        var text = new StringBuilder("fight numbers:");
        var keys = FightNumbers.Keys.Where(k => k != FightNumbers.RanOver).ToList();
        foreach (var (key, d) in keys.Zip(Differences.ForTarget(a, b, target, keys, TimeUnit.Hour)))
        {
            text.Append("\n  ").Append(key).Append("  A=").Append(Side(ra, d.A)).Append("  B=").Append(Side(rb, d.B));
            if (d.Delta != null)
                text.Append("  d=").Append(Fmt.Range(d.Delta)).Append("  ").Append(Verdict(d.Verdict));
        }
        foreach (var (letter, result) in new[] { ("A", ra), ("B", rb) })
            if (result is { HasEstimates: false, Stall: { } stall })
                text.Append("\n  ").Append(letter).Append(": ").Append(StallReasons.Note(stall).English());
        return text.ToString();
    }

    private static string Side(TargetResult? result, Estimate? estimate) =>
        result switch
        {
            null => "—",
            { HasEstimates: false, Stall: not null } => "unfinished",
            { HasFightCounts: false } => "not recorded",
            _ => estimate == null ? "none" : Fmt.Range(estimate),
        };

    private static string Verdict(Verdict v) =>
        v switch
        {
            Model.Verdict.Better => "better",
            Model.Verdict.Worse => "worse",
            Model.Verdict.NoClearDifference => "no clear difference",
            _ => "—",
        };

    // A side whose trial ran over with none finished has no value to compare: its reason follows in the notes.
    private static string? Unfinished(Difference d) =>
        (d.StallA != null, d.StallB != null) switch
        {
            (true, true) => "both-unfinished",
            (true, false) => "a-unfinished",
            (false, true) => "b-unfinished",
            _ => null,
        };

    // A side that died every fight takes the verdict's place: its value was earned while dying.
    private static string? Died(Difference d) =>
        (d.DiedA, d.DiedB) switch
        {
            (true, true) => "both-died",
            (true, false) => "a-died",
            (false, true) => "b-died",
            _ => null,
        };
}
