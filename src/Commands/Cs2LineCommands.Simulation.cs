using System.Globalization;
using System.Text;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel;
using MelvorMods.CombatSimulator2.Panel.Simulate;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Commands;

// The simulation kind: the whole Simulate page as one line, and using one on this character.
internal static partial class Cs2LineCommands
{
    private static bool IsSimulation(string text) =>
        SetupLine.TryRead(text, out var content, out _) && content!.Kind == LineKind.Simulation;

    private static void CopySimulation(CommandCall call)
    {
        LiveGame.Require(); // refuses without a loaded character
        var page = SimulationPage.Now();
        if (page.B is { CanChange: false })
            throw new CommandException("side B is a saved run: its setup can't be rebuilt, so it can't be shared");
        CheckLine(
            page.A,
            call,
            a =>
            {
                if (page.B == null)
                {
                    Copied(call, Sharing.SimulationContent(page, a, null));
                    return;
                }
                CheckLine(page.B, call, b => Copied(call, Sharing.SimulationContent(page, a, b)));
            }
        );
    }

    private static void CheckLine(SideState side, CommandCall call, Action<IReadOnlyList<LineEntry>> then) =>
        RunService.CheckSide(
            side.ToSetup(""),
            side.Changes,
            (check, error) =>
            {
                if (check == null)
                    call.Error(error?.Message ?? "the check failed");
                else
                    then(check.Line);
            }
        );

    // Reads a simulation line, checks both sides on this character, and sets the page when asked to.
    private static void ReadSimulation(CommandCall call, string line, bool use)
    {
        LiveGame.Require(); // refuses without a loaded character
        if (!SetupLine.TryRead(line, out var content, out var problem))
        {
            call.Error("refused: " + Name(problem));
            return;
        }
        var sim = content!.Simulation!;
        if (!SimulationPage.TryRead(content, out var read, out var missingA, out var missingB, out var unknown))
        {
            call.Error("refused: " + Name(LineProblem.Malformed));
            return;
        }
        var page = read!;
        var ignored = page.Ignored;
        var text = new StringBuilder(
            string.Create(
                CultureInfo.InvariantCulture,
                $"simulation build={content.GameBuild} sides={(sim.B == null ? 1 : 2)}"
            )
        );
        foreach (var note in missingA)
            text.Append("\nA ").Append(note.English());
        foreach (var note in missingB)
            text.Append("\nB ").Append(note.English());
        text.Append(CultureInfo.InvariantCulture, $"\nignored: {ignored.Count} ({unknown} unknown)");
        var settings = page.SettingsChanged(SimulationPage.Now());
        text.Append("\nsettings: ").Append(settings.Count == 0 ? "as now" : string.Join(", ", settings));
        if (content.GameBuild != LineIds.Build)
            text.Append(CultureInfo.InvariantCulture, $"\nMade on game build {content.GameBuild}: results may differ");
        Outcomes(
            page.A,
            'A',
            call,
            text,
            () =>
            {
                if (page.B == null)
                {
                    Done(call, text, page, use);
                    return;
                }
                Outcomes(page.B, 'B', call, text, () => Done(call, text, page, use));
            }
        );
    }

    private static void Outcomes(SideState side, char letter, CommandCall call, StringBuilder text, Action then)
    {
        if (side.Changes.IsEmpty)
        {
            then();
            return;
        }
        RunService.CheckSide(
            side.ToSetup(""),
            side.Changes,
            (check, error) =>
            {
                if (check == null)
                {
                    call.Error(text + "\n" + (error?.Message ?? "the check failed"));
                    return;
                }
                text.Append('\n').Append(SideText.Outcomes(letter, check.Changes.Outcomes));
                then();
            }
        );
    }

    private static void Done(CommandCall call, StringBuilder text, SimulationPage page, bool use)
    {
        if (use)
        {
            SimulationPage.Use(page);
            text.Append("\nused");
        }
        call.Ok(text.ToString());
    }
}
