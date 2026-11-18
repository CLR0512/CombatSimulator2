using System.Globalization;
using System.Text;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Commands;

/// <summary><c>cs2 preset</c>: named lists of changes, shared by every character.</summary>
internal static class Cs2PresetCommands
{
    public static void Add(List<Subcommand> subs) =>
        subs.Add(
            new(
                "preset",
                "cs2 preset list"
                    + "\ncs2 preset save <name> [a|b] [replace] | cs2 preset save <name> <change…> [replace]"
                    + "\ncs2 preset use <name> [side=a|b]"
                    + "\ncs2 preset rename <name> <new name> | cs2 preset delete <name>",
                ["side", .. ChangeArgs.Options],
                ["replace"],
                Preset
            )
        );

    private static void Preset(CommandCall call, CommandArgs args)
    {
        var presets = PanelState.Presets();
        switch (args.Word(0)?.ToLowerInvariant())
        {
            case "list":
                call.Ok(List(presets));
                return;
            case "save":
                Save(call, args, presets);
                return;
            case "use":
                Use(call, args, presets);
                return;
            case "rename":
                var result = presets.Rename(Name(args, 1), Name(args, 2));
                if (result != PresetResult.Saved)
                    throw new CommandException(Problem(result, args.Word(1)!));
                PanelState.SavePresets();
                call.Ok($"preset renamed: {presets.Find(args.Word(2))!.Name}");
                return;
            case "delete":
                if (!presets.Delete(Name(args, 1)))
                    throw new CommandException(Problem(PresetResult.NotFound, args.Word(1)!));
                PanelState.SavePresets();
                call.Ok($"preset deleted: {args.Word(1)}");
                return;
            default:
                throw new CommandException("usage: cs2 preset list|save|use|rename|delete (see cs2 help)");
        }
    }

    // A name is one word here; the panel takes any text.
    private static string Name(CommandArgs args, int word) =>
        args.Word(word) ?? throw new CommandException("give the preset's name (see cs2 help)");

    private static string List(Presets presets)
    {
        var text = new StringBuilder(
            string.Create(CultureInfo.InvariantCulture, $"{presets.List.Count} presets (at most {Presets.Max})")
        );
        foreach (var preset in presets.List)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n{preset.Name} changes={preset.Count} {preset.Changes}");
            if (preset.Read(out var problem) == null)
                text.Append("  unreadable: ").Append(problem);
        }
        return text.ToString();
    }

    private static void Save(CommandCall call, CommandArgs args, Presets presets)
    {
        var name = Name(args, 1);
        ChangeList changes;
        if (ChangeArgs.Any(args))
        {
            var (onA, onB) = ChangeArgs.From(args, SideLetter.B);
            if (!onA.IsEmpty)
                throw new CommandException("a preset is one list of changes: give them without a side");
            changes = onB;
        }
        else
            changes = args.Word(2)?.ToLowerInvariant() switch
            {
                null or "b" => PanelState.B?.Changes ?? throw new CommandException("there is no side B (see cs2 ui)"),
                "a" => PanelState.A.Changes,
                var other => throw new CommandException($"'{other}': name the side, a or b"),
            };
        var result = presets.Save(name, changes, args.Flag("replace"));
        if (result is not (PresetResult.Saved or PresetResult.Replaced))
            throw new CommandException(Problem(result, name));
        PanelState.SavePresets();
        var saved = presets.Find(name)!;
        call.Ok(
            string.Create(
                CultureInfo.InvariantCulture,
                $"preset {(result == PresetResult.Replaced ? "replaced" : "saved")}: {saved.Name} changes={saved.Count} {saved.Changes}"
            )
        );
    }

    private static void Use(CommandCall call, CommandArgs args, Presets presets)
    {
        var name = Name(args, 1);
        var preset = presets.Find(name) ?? throw new CommandException(Problem(PresetResult.NotFound, name));
        var changes =
            preset.Read(out var problem)
            ?? throw new CommandException($"preset {preset.Name} can't be used: {problem}");
        var letter = args.String("side")?.ToLowerInvariant() switch
        {
            null or "b" => SideLetter.B,
            "a" => SideLetter.A,
            var other => throw new CommandException($"side={other}: use a or b"),
        };
        // The preset's changes go on top of the side's own; side B starts as side A does.
        var side =
            letter == SideLetter.A ? PanelState.A
            : PanelState.B is { CanChange: true } b ? b
            : new SideState(PanelState.A.Base, null, PanelState.A.IgnoreRequirements);
        var merged = side.Changes;
        foreach (var change in changes)
            merged = merged.With(change);
        side = side.WithChanges(merged);
        PanelState.SetSide(letter, side);
        var line = StateLines.SideLine(letter, side);
        RunService.CheckSide(
            side.ToSetup(""),
            side.Changes,
            (check, error) =>
            {
                if (check == null)
                    call.Error(line + "\n" + (error?.Message ?? "the check failed"));
                else
                    call.Ok(
                        line
                            + "\n"
                            + SideText.Checks(
                                letter == SideLetter.A ? check : null,
                                letter == SideLetter.B ? check : null
                            )
                    );
            }
        );
    }

    private static string Problem(PresetResult result, string name) =>
        result switch
        {
            PresetResult.NameTaken =>
                $"a preset is already named '{name}': add replace to overwrite it, or pick another name",
            PresetResult.NoName => "a preset needs a name",
            PresetResult.NoChanges => "there are no changes to save",
            PresetResult.Full => $"there are {Presets.Max} presets already: delete one first",
            _ => $"no preset '{name}' (see cs2 preset list)",
        };
}
