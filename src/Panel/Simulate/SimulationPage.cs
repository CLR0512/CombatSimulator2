using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel.Simulate;

/// <summary>
/// What a simulation line sets on the Simulate page: both sides, the targets left out, and the settings that change
/// a run. Copies and where runs simulate are the player's own and never part of it.
/// </summary>
internal sealed record SimulationPage(
    SideState A,
    SideState? B,
    IReadOnlySet<string> Ignored,
    int Trials,
    ulong? Seed,
    bool Fresh,
    bool StopEarly,
    double StopPrecision,
    string Metric,
    TimeUnit Unit
)
{
    private static SimulationPage? _before;

    /// <summary>The page as it stands for the loaded character.</summary>
    public static SimulationPage Now()
    {
        var prefs = PanelState.Prefs;
        return new SimulationPage(
            PanelState.A,
            PanelState.B,
            new HashSet<string>(PanelState.Ignored, StringComparer.Ordinal),
            prefs.Trials,
            prefs.Seed,
            prefs.SimulateFresh,
            prefs.StopEarly,
            prefs.StopPrecision,
            prefs.Metric,
            prefs.Unit
        );
    }

    /// <summary>
    /// What a simulation line sets on the loaded character: each side's changes as the line holds them, tried on the
    /// character as it is (a line holds each side as it ends up, so not on a loadout); the ids this build lacks are
    /// in <paramref name="missingA"/>, <paramref name="missingB"/> and <paramref name="unknownTargets"/>. False when
    /// such an id comes from this same build: then the line is wrong, and nothing of it is used.
    /// </summary>
    public static bool TryRead(
        LineContent content,
        out SimulationPage? page,
        out List<Note> missingA,
        out List<Note> missingB,
        out int unknownTargets
    )
    {
        ArgumentNullException.ThrowIfNull(content);
        page = null;
        missingB = [];
        unknownTargets = 0;
        var sim = content.Simulation ?? throw new ArgumentException("not a simulation line", nameof(content));
        var game = LiveGame.Require();
        if (!LineIds.TryResolve(game, content, out var changesA, out missingA))
            return false;
        var changesB = ChangeList.Empty;
        if (sim.B != null && !LineIds.TryResolve(game, content with { Entries = sim.B }, out changesB, out missingB))
            return false;
        var (ignored, unknown) = LineIds.IgnoredKeys(game, sim.Ignored);
        unknownTargets = unknown;
        page = new SimulationPage(
            new SideState(SideBase.Current, changesA, sim.IgnoreRequirementsA),
            sim.B == null ? null : new SideState(SideBase.Current, changesB, sim.IgnoreRequirementsB),
            new HashSet<string>(ignored, StringComparer.Ordinal),
            sim.Trials,
            sim.Seed,
            sim.Fresh,
            sim.StopEarly,
            sim.StopPrecision,
            sim.Metric.Full ?? Metrics.MainXp,
            sim.TimeUnit
        );
        return true;
    }

    /// <summary>Forgets the page before the last use: a run started since, so Undo would undo more than the use.</summary>
    public static void Forget() => _before = null;

    /// <summary>Whether there is a page to go back to after a use.</summary>
    public static bool CanUndo => _before != null;

    /// <summary>
    /// Sets the page to <paramref name="page"/> through the page's own setters, keeping the page before for one
    /// <see cref="Undo"/>. Nothing runs.
    /// </summary>
    public static void Use(SimulationPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var before = Now();
        Apply(page);
        _before = before;
    }

    /// <summary>Puts the page back as it was before the last use; false when there is none.</summary>
    public static bool Undo()
    {
        if (_before is not { } before)
            return false;
        _before = null;
        Apply(before);
        return true;
    }

    private static void Apply(SimulationPage page)
    {
        PanelState.SetSide(SideLetter.A, page.A);
        PanelState.SetSide(SideLetter.B, page.B);
        var ignored = PanelState.Ignored;
        PanelState.SetIgnored([.. ignored.Where(k => !page.Ignored.Contains(k))], ignored: false);
        PanelState.SetIgnored([.. page.Ignored.Where(k => !ignored.Contains(k))], ignored: true);
        var prefs = PanelState.Prefs;
        prefs.Trials = page.Trials;
        prefs.Seed = page.Seed;
        prefs.SimulateFresh = page.Fresh;
        prefs.StopEarly = page.StopEarly;
        prefs.StopPrecision = page.StopPrecision;
        prefs.Metric = page.Metric;
        prefs.TimeUnit = TimeUnits.Name(page.Unit);
        PanelState.Update();
    }

    /// <summary>The settings that differ from <paramref name="now"/>, as <c>trials 1000 -> 500</c>.</summary>
    public IReadOnlyList<string> SettingsChanged(SimulationPage now) =>
        [.. Differences(now).Select(d => $"{d.Setting} {d.From} -> {d.To}")];

    /// <summary>Each setting that differs from <paramref name="now"/>: its key, and the values before and after.</summary>
    public IReadOnlyList<(string Setting, string From, string To)> Differences(SimulationPage now)
    {
        ArgumentNullException.ThrowIfNull(now);
        var changed = new List<(string, string, string)>();
        void Add<T>(string name, T from, T to, Func<T, string> text)
        {
            if (!EqualityComparer<T>.Default.Equals(from, to))
                changed.Add((name, text(from), text(to)));
        }
        static string SeedText(ulong? seed) =>
            seed?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "new";
        static string OnOff(bool on) => on ? "on" : "off";
        Add("trials", now.Trials, Trials, t => t.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Add("seed", now.Seed, Seed, SeedText);
        Add("fresh", now.Fresh, Fresh, OnOff);
        Add(
            "stop early",
            now.StopEarly ? now.StopPrecision : 0,
            StopEarly ? StopPrecision : 0,
            p => p == 0 ? "off" : $"±{p * 100:0.#}%"
        );
        Add("metric", now.Metric, Metric, m => m);
        Add("unit", now.Unit, Unit, TimeUnits.Name);
        return changed;
    }
}
