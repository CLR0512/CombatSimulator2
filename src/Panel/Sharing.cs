using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Panel;

/// <summary>
/// A side's setup or a preset exported as one line on the clipboard, for another player to import. Each returns null
/// once the line is on the clipboard, or why it isn't (too long to share, a preset this CS2 can't read).
/// </summary>
internal static class Sharing
{
    /// <summary>The side as its check found it: its gear, levels and pages as they end up.</summary>
    public static string? ExportSide(SideCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        return Export(new LineContent(SetupLine.Version, LineKind.Setup, LineIds.Build, null, check.Line));
    }

    /// <summary>The preset's name and changes; why not when this CS2 can't read its changes.</summary>
    public static string? ExportPreset(Preset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        if (LiveGame.Find() is not { } game)
            return Tr.Note(LiveGame.NoGame);
        if (preset.Read(out var problem) is not { } changes)
            return Tr.Format("Can't be exported: {0}", problem ?? "");
        return Export(
            new LineContent(
                SetupLine.Version,
                LineKind.Preset,
                LineIds.Build,
                preset.Name,
                LineIds.Preset(game, changes)
            )
        );
    }

    /// <summary>
    /// The Simulate page as one line: both sides as their checks found them, the targets left out and the settings;
    /// why not when a side isn't checked yet or is a saved run.
    /// </summary>
    public static string? ExportSimulation()
    {
        if (LiveGame.Find() == null)
            return Tr.Note(LiveGame.NoGame);
        var page = Simulate.SimulationPage.Now();
        if (page.B is { CanChange: false })
            return Tr.Get("Side B is a saved run: it can't be shared. Compare with a setup instead.");
        if (PanelState.CheckA is not { } a || (page.B != null && PanelState.CheckB is null))
            return Tr.Get("Still checking the sides: try again in a moment.");
        return Export(SimulationContent(page, a.Line, page.B == null ? null : PanelState.CheckB!.Line));
    }

    /// <summary>
    /// A simulation line's content for <paramref name="page"/> in the loaded game, its sides given as their checks found
    /// them.
    /// </summary>
    public static LineContent SimulationContent(
        Simulate.SimulationPage page,
        IReadOnlyList<LineEntry> a,
        IReadOnlyList<LineEntry>? b
    )
    {
        ArgumentNullException.ThrowIfNull(page);
        var game = LiveGame.Require();
        var sim = new LineSimulation(
            a,
            b,
            page.A.IgnoreRequirements,
            page.B?.IgnoreRequirements ?? false,
            LineIds.Ignored(game, page.Ignored),
            page.Trials,
            page.Seed,
            page.Fresh,
            page.StopEarly,
            page.StopPrecision,
            null,
            LineIds.Metric(page.Metric),
            page.Unit
        );
        return new LineContent(SetupLine.SimulationVersion, LineKind.Simulation, LineIds.Build, null, a)
        {
            Simulation = sim,
        };
    }

    private static string? Export(LineContent content)
    {
        if (!SetupLine.TryWrite(content, out var text))
            return Tr.Format(
                "Too long to share: {0} of {1} characters. Remove some changes.",
                text.Length.ToString(Tr.Culture),
                SetupLine.LengthOf(content.Kind).ToString(Tr.Culture)
            );
        Clipboard.Set(text);
        return null;
    }

    /// <summary>What the player is told once a line is on the clipboard.</summary>
    public static string Exported => Tr.Get("Copied!");

    /// <summary>Why an imported line can't be used, in the player's words.</summary>
    public static string Problem(LineProblem problem) =>
        problem switch
        {
            LineProblem.Empty => Tr.Get("The clipboard is empty."),
            LineProblem.NotALine => Tr.Get("The clipboard holds no CS2 setup."),
            LineProblem.TooLong => Tr.Get("Too long for a CS2 setup."),
            LineProblem.Damaged => Tr.Get("Damaged: cut short or changed after it was copied."),
            LineProblem.NewerFormat => Tr.Get("Made by a newer CS2: update to use it."),
            _ => Tr.Get("Not a setup CS2 can read."),
        };
}
