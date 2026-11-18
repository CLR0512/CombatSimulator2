using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Host;
using MelvorMods.CombatSimulator2.Panel;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Commands.Dev;

/// <summary>
/// Levers for what a game update can do to CS2: another build than the ones it was checked with, and a part of the
/// game it relies on gone.
/// </summary>
internal static class BuildLevers
{
    private static string? _realBuild;

    public static void Add(List<Subcommand> subs) =>
        subs.AddRange([
            new(
                "build",
                "cs2dev build <id>|real",
                [],
                [],
                Build,
                NeedsGame: false,
                Purpose: "Read the game's build as another id, not on the checked list, until real or a reload"
            ),
            new(
                "miss",
                "cs2dev miss patch <name>|dependency <name>|off",
                [],
                [],
                Miss,
                NeedsGame: false,
                Purpose: "Make one patch target or dependency read as missing from the next load on (names as cs2 deps prints them)"
            ),
        ]);

    private static void Build(CommandCall call, CommandArgs args)
    {
        var word = args.Word(0) ?? throw new CommandException("usage: cs2dev build <id>|real");
        _realBuild ??= LiveGame.SteamBuild;
        if (word == "real")
        {
            LiveGame.SteamBuild = _realBuild;
            LiveGame.Checked = LiveGame.CheckedFor(Usage.Manifest, LiveGame.GameVersion);
        }
        else
        {
            LiveGame.SteamBuild = word;
            // Another build is one the manifest doesn't list, whatever id it is given.
            LiveGame.Checked = Model.CheckedState.Of(word, null, LiveGame.GameVersion);
        }
        call.Ok(LiveGame.Checked.StatusLine);
    }

    private static void Miss(CommandCall call, CommandArgs args)
    {
        var folder = HostClient.DataRoot ?? throw new CommandException("no data folder yet");
        var path = Path.Combine(folder, Dependencies.DevMissFile);
        var kind = args.Word(0);
        if (kind == "off")
        {
            File.Delete(path);
            call.Ok("nothing missing from the next load");
            return;
        }
        if (kind is not ("patch" or "dependency") || args.Words.Count < 2)
            throw new CommandException("usage: cs2dev miss patch <name>|dependency <name>|off");
        var name = string.Join(' ', args.Words.Skip(1));
        var entry =
            Dependencies.DevFind(kind == "patch", name)
            ?? throw new CommandException($"no {kind} named '{name}' (see cs2 deps)");
        File.WriteAllText(path, $"{kind} {name}");
        call.Ok($"from the next load: {entry} missing → {entry.Feature}");
    }
}
