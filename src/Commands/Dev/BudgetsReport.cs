using System.Globalization;
using System.Text;
using MelvorMods.CombatSimulator2.Engine.Dev;

namespace MelvorMods.CombatSimulator2.Commands.Dev;

/// <summary>One budget's line in the bench's result. <c>Pass</c> is null when the bench has no verdict on it.</summary>
internal abstract record BudgetLine(string Name)
{
    public abstract bool? Pass { get; }

    /// <summary>The line after the budget's name.</summary>
    public abstract string Text { get; }

    protected static string Num(double value, string format = "0.#") =>
        value.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>The middle take, then the lowest and highest: <c>14.2 [12.1–30.5]</c>.</summary>
    protected static string Spread(IEnumerable<double> values, string format = "0.#")
    {
        var sorted = values.Order().ToList();
        return sorted.Count == 0
            ? "none"
            : $"{Num(Middle(sorted), format)} [{Num(sorted[0], format)}–{Num(sorted[^1], format)}]";
    }

    protected static double Middle(IReadOnlyList<double> sorted) => sorted[(sorted.Count - 1) / 2];
}

/// <summary>
/// A frames budget: each take's window judged against its control, and for a budget with a time to usable, each
/// take's repeats. A take's time is its middle repeat, so one collection of the game's own doesn't fail it; the
/// budget passes when most takes pass both.
/// </summary>
internal sealed record FramesLine(
    string Name,
    IReadOnlyList<FrameVerdict> Takes,
    bool Noisy,
    IReadOnlyList<IReadOnlyList<double>>? Usable = null,
    double? UsableLimit = null,
    string? Look = null
) : BudgetLine(Name)
{
    public override bool? Pass => FrameRule.Pass(Takes) && UsablePass;

    /// <summary>The worst frame of the middle take: what two looks' lines are compared by.</summary>
    public double Worst => Takes.Count == 0 ? 0 : Middle([.. Takes.Select(t => t.Worst).Order()]);

    private bool UsablePass
    {
        get
        {
            if (Usable == null || UsableLimit is not { } limit)
                return true;
            var failed = Usable.Count(take => take.Count == 0 || Middle([.. take.Order()]) > limit + 1e-9);
            return Usable.Count > 0 && 2 * failed <= Usable.Count;
        }
    }

    public override string Text
    {
        get
        {
            var text = new StringBuilder();
            text.Append("worst ").Append(Spread(Takes.Select(t => t.Worst)));
            text.Append(" slow ").Append(Spread(Takes.Select(t => (double)t.Slow), "0"));
            text.Append(" limit control worst ").Append(Spread(Takes.Select(t => t.ControlWorst)));
            text.Append(" slow ").Append(Spread(Takes.Select(t => (double)t.ControlSlow), "0"));
            if (Usable != null && UsableLimit is { } limit)
                text.Append(" usable ")
                    .Append(Spread(Usable.SelectMany(take => take), "0"))
                    .Append(" limit ")
                    .Append(Num(limit, "0"));
            text.Append(Pass == true ? " pass" : " fail");
            if (Noisy)
                text.Append(" noisy");
            if (Look != null)
                text.Append(" (").Append(Look).Append(')');
            return text.ToString();
        }
    }
}

/// <summary>
/// A figure per take against a limit it must not pass in any take. With no limit yet it is shown without a
/// verdict.
/// </summary>
internal sealed record ValueLine(
    string Name,
    IReadOnlyList<double> Takes,
    double? Limit,
    string Unit,
    string? Note = null
) : BudgetLine(Name)
{
    public override bool? Pass => Limit is { } limit && Takes.Count > 0 ? Takes.Max() <= limit + 1e-9 : null;

    public override string Text
    {
        get
        {
            var value = $"{Spread(Takes, "0")} {Unit}";
            var note = Note == null ? "" : $" ({Note})";
            return Pass is { } pass
                ? $"{value} limit {Num(Limit!.Value, "0")} {Unit} {(pass ? "pass" : "fail")}{note}"
                : $"{value} not measured (no limit yet){note}";
        }
    }
}

/// <summary>Windows that each pass or fail whole against the smoothness limits: all must pass.</summary>
internal sealed record LimitsLine(string Name, IReadOnlyList<(string Window, bool Pass)> Windows) : BudgetLine(Name)
{
    public override bool? Pass => Windows.Count > 0 && Windows.All(w => w.Pass);

    public override string Text =>
        string.Join(", ", Windows.Select(w => $"{w.Window} {(w.Pass ? "pass" : "fail")}"))
        + $" limit control {(Pass == true ? "pass" : "fail")}";
}

internal sealed record NotMeasuredLine(string Name, string Reason) : BudgetLine(Name)
{
    public override bool? Pass => null;

    public override string Text => $"not measured ({Reason})";
}

/// <summary>A budget a script measures: its last recorded value, never a verdict of this bench.</summary>
internal sealed record ElsewhereLine(string Name, string Recorded, string Script, string? Date) : BudgetLine(Name)
{
    public override bool? Pass => null;

    public override string Text => $"elsewhere {Recorded} ({Script}{(Date == null ? "" : ", " + Date)})";
}

internal static class BudgetsReport
{
    /// <summary>
    /// The header, then one line per budget. The header fails if any measured budget failed; a budget not measured
    /// is counted apart and never as passed.
    /// </summary>
    public static string Render(string id, IReadOnlyList<BudgetLine> lines, string gameBuild, DateTime date)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var measured = lines.Count(l => l.Pass != null);
        var passed = lines.Count(l => l.Pass == true);
        var notMeasured = lines.Count(l => l is NotMeasuredLine or ValueLine { Pass: null });
        var text = new StringBuilder(
            $"{id} {(passed == measured ? "pass" : "fail")} {passed}/{measured} budgets, {notMeasured} not measured, game {gameBuild}, {date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
        );
        foreach (var line in lines)
            text.Append('\n').Append(line.Name).Append(' ').Append(line.Text);
        return text.ToString();
    }

    /// <summary>What a script-measured budget shows: its recorded value, or that the script hasn't been run.</summary>
    public static BudgetLine Elsewhere(Budget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        var script = budget.Script ?? throw new ArgumentException($"{budget.Name} has no script", nameof(budget));
        return budget.Recorded is { } recorded
            ? new ElsewhereLine(budget.Name, recorded, script, budget.RecordedOn)
            : new NotMeasuredLine(budget.Name, $"run {script}");
    }

    /// <summary>
    /// A panel budget is taken in both looks and its row holds the worse: a fail before a pass, then the worse
    /// worst frame.
    /// </summary>
    public static BudgetLine Worse(BudgetLine a, BudgetLine b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Pass != b.Pass)
            return Rank(a) >= Rank(b) ? a : b;
        return (a, b) switch
        {
            (FramesLine fa, FramesLine fb) => fb.Worst > fa.Worst ? fb : fa,
            (ValueLine va, ValueLine vb) => vb.Takes.DefaultIfEmpty().Max() > va.Takes.DefaultIfEmpty().Max() ? vb : va,
            _ => a,
        };
    }

    // A fail says most, then a line with no verdict (it must not hide behind the other look's pass), then a pass.
    private static int Rank(BudgetLine line) =>
        line.Pass switch
        {
            false => 2,
            null => 1,
            true => 0,
        };
}
