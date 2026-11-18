using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel;

/// <summary>
/// Words the panel shows in several places: metric names, time units and setups. Each is in the game's language, or
/// in English (<c>english: true</c>) for replies that report what the panel shows. Main thread.
/// </summary>
internal static class Words
{
    /// <summary>A relative change with its sign: +38%, −3%.</summary>
    public static string Gain(double fraction) =>
        (fraction >= 0 ? "+" : "−") + Widgets.Ui.Number(Math.Abs(fraction) * 100) + "%";

    /// <summary>A note in the game's language, or in English.</summary>
    public static string Render(Note note, bool english = false) => english ? note.English() : Tr.Note(note);

    /// <summary>A metric's label: "Kills", "XP: Attack".</summary>
    public static string Metric(Metric metric, bool english = false)
    {
        ArgumentNullException.ThrowIfNull(metric);
        var kind = Render(metric.Kind, english);
        return metric.Subject is { } subject
            ? Render(Note.Of("{0}: {1}", kind, Render(subject, english)), english)
            : kind;
    }

    /// <summary>
    /// A metric's name with its unit: "XP / h · Attack", "Kills / h", "Death rate"; without a unit, "XP · Attack".
    /// </summary>
    public static string MetricName(Metric metric, TimeUnit? unit, bool english = false)
    {
        ArgumentNullException.ThrowIfNull(metric);
        if (!metric.Timed)
            return Metric(metric, english);
        var kind = Render(metric.Kind, english);
        if (metric.Subject is not { } subjectNote)
        {
            return unit switch
            {
                null => kind,
                TimeUnit.Kill => Render(Note.Of("{0} / kill", kind), english),
                TimeUnit.Second => Render(Note.Of("{0} / s", kind), english),
                TimeUnit.Minute => Render(Note.Of("{0} / min", kind), english),
                TimeUnit.Hour => Render(Note.Of("{0} / h", kind), english),
                _ => Render(Note.Of("{0} / day", kind), english),
            };
        }
        var subject = Render(subjectNote, english);
        return unit switch
        {
            null => Render(Note.Of("{0} · {1}", kind, subject), english),
            TimeUnit.Kill => Render(Note.Of("{0} / kill · {1}", kind, subject), english),
            TimeUnit.Second => Render(Note.Of("{0} / s · {1}", kind, subject), english),
            TimeUnit.Minute => Render(Note.Of("{0} / min · {1}", kind, subject), english),
            TimeUnit.Hour => Render(Note.Of("{0} / h · {1}", kind, subject), english),
            _ => Render(Note.Of("{0} / day · {1}", kind, subject), english),
        };
    }

    /// <summary>A unit choice: "per kill", "per hour".</summary>
    public static string PerUnit(TimeUnit unit) =>
        unit switch
        {
            TimeUnit.Kill => Tr.Get("per kill"),
            TimeUnit.Second => Tr.Get("per second"),
            TimeUnit.Minute => Tr.Get("per minute"),
            TimeUnit.Hour => Tr.Get("per hour"),
            _ => Tr.Get("per day"),
        };

    /// <summary>The unit on a short button: "/kill", "/h".</summary>
    public static string PerUnitShort(TimeUnit unit) =>
        unit switch
        {
            TimeUnit.Kill => Tr.Get("/kill"),
            TimeUnit.Second => Tr.Get("/s"),
            TimeUnit.Minute => Tr.Get("/min"),
            TimeUnit.Hour => Tr.Get("/h"),
            _ => Tr.Get("/day"),
        };

    /// <summary>A value in seconds: "12.5 s".</summary>
    public static string Seconds(string value, bool english = false) => Render(Note.Of("{0} s", value), english);

    /// <summary>A stretch of game time in the largest unit that keeps it short: "40 s", "12 min", "12.2 h", "3.4 days".</summary>
    public static string Duration(double seconds)
    {
        if (!double.IsFinite(seconds))
            return Tr.Get("∞");
        return seconds switch
        {
            < 90 => Tr.Format("{0} s", Math.Round(seconds).ToString("0", Tr.Culture)),
            < 90 * 60 => Tr.Format("{0} min", Math.Round(seconds / 60).ToString("0", Tr.Culture)),
            < 48 * 3600 => Tr.Format("{0} h", (seconds / 3600).ToString("0.#", Tr.Culture)),
            _ => Tr.Format("{0} days", (seconds / 86400).ToString("#,0.#", Tr.Culture)),
        };
    }

    /// <summary>A small chance as a percentage with its significant digits: 0.0456%, 0.12%, 0.79%.</summary>
    public static string Chance(double chance) =>
        Tr.Format("{0}%", (chance * 100).ToString(chance < 0.001 ? "0.####" : "0.###", Tr.Culture));

    /// <summary>A setup as the panel names it: its loadout or "current", and whether it ignores requirements.</summary>
    public static string Setup(Setup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        var name = setup.Loadout == Model.Setup.Current ? Tr.Get("current", "loadout") : setup.Name;
        if (setup.IgnoreRequirements)
            name = Tr.Format("{0} (requirements ignored)", name);
        return setup.Changes is { } changes ? Tr.Note(ChangeNotes.Label(name, changes.Count)) : name;
    }
}
