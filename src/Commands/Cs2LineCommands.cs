using System.Globalization;
using System.Text;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Commands;

/// <summary>
/// <c>cs2 line</c>: a side's whole setup, or a preset, as one line of text for another player, and reading such a
/// line back. A line that is refused changes nothing.
/// </summary>
internal static partial class Cs2LineCommands
{
    public static void Add(List<Subcommand> subs) =>
        subs.Add(
            new(
                "line",
                "cs2 line copy [a|b|sim] | cs2 line copy preset <name>"
                    + "\ncs2 line read <line>"
                    + "\ncs2 line use <line> as=a|b|preset|sim [name=<preset name>]"
                    + "\ncs2 line undo",
                ["as", "name"],
                [],
                Line
            )
        );

    private static void Line(CommandCall call, CommandArgs args)
    {
        switch (args.Word(0)?.ToLowerInvariant())
        {
            case "copy" when args.Word(1)?.ToLowerInvariant() == "sim":
                CopySimulation(call);
                return;
            case "copy":
                Copy(call, args);
                return;
            case "read" when IsSimulation(Text(args)):
                ReadSimulation(call, Text(args), use: false);
                return;
            case "read":
                Read(call, Text(args), null, null);
                return;
            case "use":
                var target =
                    args.String("as")?.ToLowerInvariant()
                    ?? throw new CommandException("say what to use it as: as=a, as=b, as=preset or as=sim");
                if (target is not ("a" or "b" or "preset" or "sim"))
                    throw new CommandException($"as={target}: use a, b, preset or sim");
                if (!SetupLine.TryRead(Text(args), out _, out var refused))
                    throw new CommandException("refused: " + Name(refused));
                if (IsSimulation(Text(args)) != (target == "sim"))
                    throw new CommandException(
                        target == "sim"
                            ? "not a simulation line: use it as a, b or preset"
                            : "a simulation line: use it as=sim"
                    );
                if (target == "sim")
                    ReadSimulation(call, Text(args), use: true);
                else
                    Read(call, Text(args), target, args.String("name"));
                return;
            case "undo":
                call.Ok(Panel.Simulate.SimulationPage.Undo() ? "restored" : "nothing to undo");
                return;
            default:
                throw new CommandException("usage: cs2 line copy|read|use (see cs2 help)");
        }
    }

    private static string Text(CommandArgs args) => args.Word(1) ?? throw new CommandException("give the line: CS2-…");

    private static void Copy(CommandCall call, CommandArgs args)
    {
        var game = LiveGame.Require();
        if (args.Word(1)?.ToLowerInvariant() == "preset")
        {
            var name = args.Word(2) ?? throw new CommandException("usage: cs2 line copy preset <name>");
            var preset = PanelState.Presets().Find(name) ?? throw new CommandException($"no preset '{name}'");
            var changes =
                preset.Read(out var problem)
                ?? throw new CommandException($"preset {preset.Name} can't be copied: {problem}");
            Copied(
                call,
                new LineContent(
                    SetupLine.Version,
                    LineKind.Preset,
                    LineIds.Build,
                    preset.Name,
                    LineIds.Preset(game, changes)
                )
            );
            return;
        }
        var side = args.Word(1)?.ToLowerInvariant() switch
        {
            null or "a" => PanelState.A,
            "b" => PanelState.B ?? throw new CommandException("there is no side B (see cs2 ui)"),
            var other => throw new CommandException($"'{other}': name the side, a or b"),
        };
        if (!side.CanChange)
            throw new CommandException("a saved run's setup can't be rebuilt, so it has no line");
        RunService.CheckSide(
            side.ToSetup(""),
            side.Changes,
            (check, error) =>
            {
                if (check == null)
                    call.Error(error?.Message ?? "the check failed");
                else
                    Copied(call, new LineContent(SetupLine.Version, LineKind.Setup, LineIds.Build, null, check.Line));
            }
        );
    }

    internal static void Copied(CommandCall call, LineContent content)
    {
        if (!SetupLine.TryWrite(content, out var text))
        {
            call.Error(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"the line would be {text.Length} characters, over {SetupLine.LengthOf(content.Kind)}: remove some changes"
                )
            );
            return;
        }
        Clipboard.Set(text);
        call.Ok(
            string.Create(CultureInfo.InvariantCulture, $"{text}\nlength={text.Length} entries={content.Entries.Count}")
        );
    }

    // Reads the line, checks its changes on this character, and uses it when asked to.
    private static void Read(CommandCall call, string line, string? useAs, string? presetName)
    {
        var game = LiveGame.Require();
        if (!SetupLine.TryRead(line, out var content, out var problem))
        {
            call.Error("refused: " + Name(problem));
            return;
        }
        if (!LineIds.TryResolve(game, content!, out var changes, out var missing))
        {
            call.Error("refused: " + Name(LineProblem.Malformed));
            return;
        }
        var head = new StringBuilder(
            string.Create(
                CultureInfo.InvariantCulture,
                $"kind={content!.Kind.ToString().ToLowerInvariant()} build={content.GameBuild} changes={changes.Count}"
            )
        );
        if (content.Name != null)
            head.Append(" name=").Append(content.Name);
        foreach (var note in missing)
            head.Append('\n').Append(note.English());
        if (useAs == "preset")
        {
            var name = presetName ?? content.Name ?? throw new CommandException("name the preset: name=<name>");
            var presets = PanelState.Presets();
            var result = presets.Save(name, changes);
            if (result != PresetResult.Saved)
                throw new CommandException($"not saved as a preset ({result}): pick another name with name=<name>");
            PanelState.SavePresets();
            call.Ok(head.Append("\npreset saved: ").Append(presets.Find(name)!.Name).ToString());
            return;
        }
        // A line's changes are tried on what side A starts from: this character as it is, or its loadout.
        var onto = new SideState(PanelState.A.Base, changes, PanelState.A.IgnoreRequirements);
        if (useAs != null)
            PanelState.SetSide(useAs == "a" ? SideLetter.A : SideLetter.B, onto);
        if (changes.IsEmpty)
        {
            call.Ok(head.ToString());
            return;
        }
        RunService.CheckSide(
            onto.ToSetup(""),
            changes,
            (check, error) =>
            {
                if (check == null)
                    call.Error(head + "\n" + (error?.Message ?? "the check failed"));
                else
                    call.Ok(
                        head
                            + "\n"
                            + SideText.Outcomes(useAs == "a" ? 'A' : 'B', check.Changes.Outcomes)
                            + (useAs == null ? "" : $"\nused as side {useAs}")
                    );
            }
        );
    }

    internal static string Name(LineProblem problem) =>
        problem switch
        {
            LineProblem.Empty => "empty",
            LineProblem.NotALine => "not a setup line",
            LineProblem.TooLong => "too long",
            LineProblem.Damaged => "damaged (cut short or mistyped)",
            LineProblem.NewerFormat => "made by a newer CS2",
            _ => "malformed",
        };
}
