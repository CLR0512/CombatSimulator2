namespace MelvorMods.CombatSimulator2.Model;

public enum TimeUnit
{
    Kill,
    Second,
    Minute,
    Hour,
    Day,
}

/// <summary>Display scales for rates. Rates are per game second at heart; per kill divides by kills instead.</summary>
public static class TimeUnits
{
    public static double Seconds(TimeUnit unit) =>
        unit switch
        {
            TimeUnit.Second => 1,
            TimeUnit.Minute => 60,
            TimeUnit.Hour => 3600,
            TimeUnit.Day => 86400,
            _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "per kill has no length in seconds"),
        };

    /// <summary>
    /// A batch's denominator for a rate in <paramref name="unit"/>: kills, or game time in that unit.
    /// </summary>
    public static double Denominator(Tally tally, TimeUnit unit)
    {
        ArgumentNullException.ThrowIfNull(tally);
        return unit == TimeUnit.Kill ? tally.Kills : tally.GameSeconds / Seconds(unit);
    }

    public static string Name(TimeUnit unit) => unit.ToString().ToLowerInvariant();

    public static bool TryParse(string text, out TimeUnit unit) =>
        Enum.TryParse(text, true, out unit) && Enum.IsDefined(unit);
}
