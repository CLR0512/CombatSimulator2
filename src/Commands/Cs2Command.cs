using System.Globalization;
using System.Text;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Commands;

/// <summary>
/// One subcommand: its usage line, the options and flags it accepts, whether it needs a character, and what it is
/// for, which <c>help</c> shows after the usage when there is one.
/// </summary>
internal sealed record Subcommand(
    string Name,
    string Usage,
    string[] Options,
    string[] Flags,
    Action<CommandCall, CommandArgs> Run,
    bool NeedsGame = true,
    string? Purpose = null
);

/// <summary>Dispatches <c>name sub …</c> to a table of subcommands, with shared parsing and errors.</summary>
internal sealed class SubcommandTable(string command, IReadOnlyList<Subcommand> subs)
{
    internal void Handle(CommandCall call)
    {
        try
        {
            var name = call.Args.Count > 0 ? call.Args[0] : "help";
            if (name.Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                call.Ok(Help());
                return;
            }
            var sub =
                subs.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? throw new CommandException($"unknown subcommand '{name}' (try {command} help)");
            var args = CommandArgs.Parse(call.Args.Skip(1), sub.Options, sub.Flags);
            if (sub.NeedsGame)
                LiveGame.Require();
            sub.Run(call, args);
        }
        catch (CommandException e)
        {
            call.Error(e.Message);
        }
        // Anything else is a bug: the reply carries its message, the log its stack, for the report.
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Common.Log.Error($"{command} {string.Join(' ', call.Args)} failed", e);
            throw;
        }
    }

    private string Help()
    {
        var text = new StringBuilder($"{subs.Count + 1} subcommands");
        text.Append('\n').Append(command).Append(" help");
        foreach (var sub in subs)
        {
            text.Append('\n').Append(sub.Usage);
            if (sub.Purpose != null)
                text.Append("\n    ").Append(sub.Purpose);
        }
        return text.ToString();
    }
}

/// <summary><c>cs2</c>: the engine and panel state.</summary>
internal static class Cs2Command
{
    private static SubcommandTable? _table;

    /// <summary>Subcommands added by other parts of the mod before <see cref="Register"/>.</summary>
    public static List<Subcommand> Subs { get; } = [];

    internal static void Register(ModCommands commands)
    {
        Subs.InsertRange(
            0,
            [
                new("deps", "cs2 deps", [], [], Deps, NeedsGame: false),
                new("current", "cs2 current", [], [], Current),
                new("setups", "cs2 setups", [], [], Setups),
            ]
        );
        _table = new SubcommandTable("cs2", Subs);
        commands.Register("cs2", "cs2 <subcommand> …", "Combat Simulator 2", _table.Handle);
    }

    private static void Deps(CommandCall call, CommandArgs args)
    {
        var report = Dependencies.Resolve(LiveGame.Find());
        var text = new StringBuilder($"{report.Resolved}/{report.Total} resolved");
        if (!report.DataChecked)
            text.Append(" (types and members; data ids need a character)");
        foreach (var dep in report.Missing)
            text.Append(CultureInfo.InvariantCulture, $"\nmissing {dep} → {dep.Feature}");
        text.Append(
            CultureInfo.InvariantCulture,
            $"\n{Dependencies.GearSearch}: {(report.IsAvailable(Dependencies.GearSearch) ? "on" : "off")}"
        );
        foreach (var kind in Dependencies.UnknownKinds())
            text.Append("\nunknown ").Append(kind);
        foreach (var patch in Dependencies.Patches)
        {
            var found = !report.Missing.Contains(patch);
            text.Append(CultureInfo.InvariantCulture, $"\n{patch} → {patch.Feature}{(found ? "" : " (missing)")}");
        }
        call.Ok(text.ToString());
    }

    private static void Current(CommandCall call, CommandArgs args)
    {
        var current = LiveGame.Current(LiveGame.Require());
        call.Ok(current == null ? "not fighting" : $"{Model.Ids.BriefKey(current.Key)}\n{current.Kind} {current.Name}");
    }

    private static void Setups(CommandCall call, CommandArgs args)
    {
        var game = LiveGame.Require();
        var loadouts = LiveGame.Loadouts(game);
        var text = new StringBuilder($"{loadouts.Count + 1} setups");
        text.Append(CultureInfo.InvariantCulture, $"\ncurrent  {game.CharacterName} as is  unlocked");
        foreach (var loadout in loadouts)
            text.Append(
                CultureInfo.InvariantCulture,
                $"\n{Model.Ids.Brief(loadout.Slot)}  {loadout.Name}  {(loadout.Unlocked ? "unlocked" : "locked")}"
            );
        call.Ok(text.ToString());
    }
}
