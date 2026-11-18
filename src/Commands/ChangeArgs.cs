using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;

namespace MelvorMods.CombatSimulator2.Commands;

/// <summary>
/// The changes among a command's arguments: <c>kind=…</c>, or <c>a.kind=…</c> and <c>b.kind=…</c> to name the side.
/// A change without a side goes to the command's default side.
/// </summary>
internal static class ChangeArgs
{
    private static readonly string[] Kinds = [.. Enum.GetValues<ChangeKind>().Select(Change.KindName)];

    /// <summary>The option names that give a change, with and without a side.</summary>
    public static IReadOnlyList<string> Options { get; } =
    [.. Kinds, .. Kinds.Select(k => "a." + k), .. Kinds.Select(k => "b." + k)];

    public const string Forms =
        "level=<skill>:<level> equip=<slot>:<item>|none toolbelt=<slot>:<item>|none stance=<stance> spell=<spell> "
        + "imbuement=<id>|none food=<item>|none prayer=<prayer>:on|off potion=<item>|none star=<node>:on "
        + "guild=<skill>:<rank> shop=<purchase>:<count> quest=<quest>:done|open blocks=<master>:<enemy>[,<enemy>]|none "
        + "loadouts=<master>:<enemy>@<loadout>[,<enemy>@<loadout>]|none task=on|off taskfor=<area>/<enemy>:on|off|save "
        + "pet=<pet>:on|off bonfire=<tier>|out technique=<technique>:<level>; "
        + "a.<change> or b.<change> names the side";

    /// <summary>True when <paramref name="args"/> names a change.</summary>
    public static bool Any(CommandArgs args) => args.Raw.Any(raw => Split(raw) != null);

    /// <summary>
    /// Each side's changes in <paramref name="args"/>, short ids resolved against the live game's data: a short id
    /// that matches ids in several namespaces is an error listing them, as is a key given twice for one side. A
    /// change without a side goes to <paramref name="defaultSide"/>; with none, it is an error.
    /// </summary>
    public static (ChangeList A, ChangeList B) From(CommandArgs args, SideLetter? defaultSide)
    {
        ArgumentNullException.ThrowIfNull(args);
        var game = LiveGame.Require();
        var a = new List<Change>();
        var b = new List<Change>();
        foreach (var raw in args.Raw)
        {
            if (Split(raw) is not { } split)
                continue;
            var side =
                split.Side ?? defaultSide ?? throw new CommandException($"{raw}: name the side, a.{raw} or b.{raw}");
            if (!Change.TryParse(split.Change, out var parsed, out var error))
                throw new CommandException(error!);
            var change = Changes.Resolve(game, parsed!, split.Change);
            var list = side == SideLetter.A ? a : b;
            if (list.Any(c => c.Key == change.Key))
                throw new CommandException($"{raw}: that is already changed (give each change once)");
            list.Add(change);
        }
        return (ChangeList.Of(a), ChangeList.Of(b));
    }

    // "a.level=Attack:5" → (A, "level=Attack:5"); null when the argument isn't a change.
    private static (SideLetter? Side, string Change)? Split(string raw)
    {
        var eq = raw.IndexOf('=', StringComparison.Ordinal);
        if (eq <= 0)
            return null;
        var name = raw[..eq].ToLowerInvariant();
        SideLetter? side = null;
        if (name.StartsWith("a.", StringComparison.Ordinal))
            side = SideLetter.A;
        else if (name.StartsWith("b.", StringComparison.Ordinal))
            side = SideLetter.B;
        var kind = side == null ? name : name[2..];
        return Kinds.Contains(kind, StringComparer.Ordinal) ? (side, kind + raw[eq..]) : null;
    }
}
