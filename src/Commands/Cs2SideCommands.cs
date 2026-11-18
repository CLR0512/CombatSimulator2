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
/// <c>cs2 side</c>, <c>swap</c>, <c>start</c> and <c>effect</c>: the panel's two sides (what each starts from, its changes and its
/// requirements setting) and what a side's changes do to its stats and modifiers.
/// </summary>
internal static class Cs2SideCommands
{
    // The bridge stops a command after 10 s; a sheet read takes a copy build at most.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(9);

    public static void Add(List<Subcommand> subs) =>
        subs.AddRange([
            new(
                "side",
                "cs2 side a|b [base=current|<loadout>|run:<id>] [ignore-req=on|off] [change…] | cs2 side a|b clear | cs2 side a|b undo <change key> | cs2 side b off",
                ["base", "ignore-req", .. ChangeArgs.Options],
                [],
                Side,
                Purpose: "Sets a side's base, requirements and changes, and prints its check; with only the letter, prints the side"
            ),
            new("swap", "cs2 swap", [], [], Swap, Purpose: "Exchanges sides A and B"),
            new(
                "start",
                "cs2 start",
                [],
                [],
                Start,
                Purpose: "Presses the run control: simulates or compares the sides on the targets to run, or cancels the run going"
            ),
            new(
                "effect",
                "cs2 effect [a|b] [change…]",
                [.. ChangeArgs.Options],
                [],
                Effect,
                Purpose: "What the side differs in from the other side, or from its own base when there is one side; changes given here are tried in place of the side's own"
            ),
        ]);

    private static SideLetter Letter(string? word, SideLetter? fallback = null) =>
        word?.ToLowerInvariant() switch
        {
            "a" => SideLetter.A,
            "b" => SideLetter.B,
            null when fallback is { } letter => letter,
            _ => throw new CommandException($"'{word}': name the side, a or b"),
        };

    private static void Side(CommandCall call, CommandArgs args)
    {
        var game = LiveGame.Require();
        var letter = Letter(args.Word(0));
        var current = letter == SideLetter.A ? PanelState.A : PanelState.B;
        switch (args.Word(1)?.ToLowerInvariant())
        {
            case "off" when letter == SideLetter.B:
                PanelState.SetSide(SideLetter.B, null);
                call.Ok("side b none");
                return;
            case "clear":
                current = Existing(current).WithChanges(ChangeList.Empty);
                break;
            case "undo":
                // "level=Defence" reads as an option, "food" as a word: the key is whichever was given.
                var kinds = Enum.GetValues<ChangeKind>().Select(Change.KindName).ToHashSet(StringComparer.Ordinal);
                var text =
                    args.Word(2)
                    ?? args.Options.Where(o => kinds.Contains(o.Key))
                        .Select(o => o.Key + "=" + o.Value)
                        .FirstOrDefault()
                    ?? throw new CommandException("usage: cs2 side a|b undo <kind>[=<subject>]");
                var key = KeyOf(text);
                var undone = Existing(current);
                if (undone.Changes.Find(key) == null)
                    throw new CommandException(
                        $"side {args.Word(0)} has no change '{text}' (see cs2 side {args.Word(0)})"
                    );
                undone = undone.WithChanges(undone.Changes.Without(key));
                PanelState.SetSide(letter, undone);
                Describe(call, letter, undone);
                return;
            case null:
                break;
            case var other:
                throw new CommandException($"'{other}': use clear, undo <change key> or off (see cs2 help)");
        }
        // A side that doesn't exist yet starts as side A does.
        var side = current ?? new SideState(PanelState.A.Base, null, PanelState.A.IgnoreRequirements);
        if (args.String("base") is { } @base)
        {
            var parsed = SideBase.Parse(
                @base.StartsWith("run:", StringComparison.Ordinal) || @base == Setup.Current ? @base : Ids.Full(@base)
            );
            if (parsed.Kind == SideBaseKind.Loadout && LiveGame.Loadouts(game).All(l => l.Slot != parsed.Id))
                throw new CommandException($"no loadout '{@base}' (see cs2 setups)");
            if (parsed.Kind == SideBaseKind.SavedRun && RunFinder.Saved(parsed.Id!) == null)
                throw new CommandException($"no saved run '{parsed.Id}' (see cs2 runs)");
            side = side.WithBase(parsed);
        }
        if (args.String("ignore-req") is { } ignore)
            side = side.WithIgnoreRequirements(
                ignore.ToLowerInvariant() switch
                {
                    "on" => true,
                    "off" => false,
                    _ => throw new CommandException($"ignore-req={ignore}: use on or off"),
                }
            );
        var (onA, onB) = ChangeArgs.From(args, letter);
        if (!(letter == SideLetter.A ? onB : onA).IsEmpty)
            throw new CommandException("cs2 side changes one side: give the changes without the other side's letter");
        var changes = side.Changes;
        foreach (var change in letter == SideLetter.A ? onA : onB)
            changes = changes.With(change);
        if (!changes.IsEmpty && !side.CanChange)
            throw new CommandException("a saved run's setup can't be rebuilt, so it carries no changes");
        side = side.WithChanges(changes);
        PanelState.SetSide(letter, side);
        Describe(call, letter, side);
    }

    private static SideState Existing(SideState? side) =>
        side ?? throw new CommandException("there is no side B (cs2 side b base=…)");

    // "level=Defence" or "food": the key of a change, short ids resolved as a change's are.
    private static ChangeKey KeyOf(string text)
    {
        var eq = text.IndexOf('=', StringComparison.Ordinal);
        var kindName = eq < 0 ? text : text[..eq];
        var kind = Enum.GetValues<ChangeKind>()
            .Cast<ChangeKind?>()
            .FirstOrDefault(k => Change.KindName(k!.Value) == kindName);
        if (kind == null)
            throw new CommandException($"'{kindName}' isn't a kind of change");
        if (eq < 0)
            return new ChangeKey(kind.Value, null);
        // Any valid value gives the change its form; only its key is used.
        var value = kind.Value switch
        {
            ChangeKind.Level or ChangeKind.Shop or ChangeKind.Guild or ChangeKind.Technique => "1",
            ChangeKind.Quest => Change.Done,
            ChangeKind.Star or ChangeKind.Prayer or ChangeKind.Pet => Change.On,
            ChangeKind.SlayerTaskFor => Change.Save,
            _ => Change.None,
        };
        var raw = $"{kindName}={text[(eq + 1)..]}:{value}";
        if (!Change.TryParse(raw, out var parsed, out var error))
            throw new CommandException(error!);
        return Changes.Resolve(LiveGame.Require(), parsed!, raw).Key;
    }

    // The side as cs2 ui prints it, then its check when it has changes.
    private static void Describe(CommandCall call, SideLetter letter, SideState side)
    {
        var line = StateLines.SideLine(letter, side);
        if (side.Changes.IsEmpty)
        {
            call.Ok(line);
            return;
        }
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

    private static void Swap(CommandCall call, CommandArgs args)
    {
        Panel.Simulate.SideSwap.Swap();
        var shown = RunService.Instance.Comparison is { } comparison ? $"\ncomparison {comparison.Record.Id}" : "";
        call.Ok(
            StateLines.SideLine(SideLetter.A, PanelState.A)
                + "\n"
                + StateLines.SideLine(SideLetter.B, PanelState.B!)
                + shown
        );
    }

    private static void Start(CommandCall call, CommandArgs args)
    {
        if (!Cs2Modal.IsOpen || Cs2Modal.SimulateView is not { Ready: true } view)
            throw new CommandException("open the panel first (cs2 open)");
        if (view.Run.Press() is { } why)
            throw new CommandException("the run control is disabled: " + why);
        call.Ok(view.Run.Describe());
    }

    private static void Effect(CommandCall call, CommandArgs args)
    {
        var letter = Letter(args.Word(0), SideLetter.B);
        var a = PanelState.A;
        var b = PanelState.B;
        var (changesA, changesB) = ChangeArgs.From(args, letter);
        if (!changesA.IsEmpty)
            a = a.WithChanges(changesA);
        if (!changesB.IsEmpty)
            b = (b is { CanChange: true } ? b : new SideState(a.Base, null, a.IgnoreRequirements)).WithChanges(
                changesB
            );
        var side = letter == SideLetter.A ? a : b ?? throw new CommandException("there is no side B (see cs2 ui)");
        if (!side.CanChange)
            throw new CommandException("a saved run keeps no stats");
        // One side is measured against itself without its changes; two against each other.
        var other =
            letter == SideLetter.B ? a
            : b is { CanChange: true } ? b
            : a.WithChanges(ChangeList.Empty);
        MainThreadReply.ReplyWhen<IReadOnlyList<SetupSheet>>(
            call,
            done => RunService.ReadSetup([other.ToSetup(""), side.ToSetup("")], done),
            (sheets, error) =>
            {
                if (sheets == null)
                    call.Error($"the check copy failed: {error?.GetType().Name}: {error?.Message}");
                else
                    call.Ok(Text(letter, Model.Effect.Of(sheets[0], sheets[1])));
            },
            Timeout,
            $"the check copy took longer than {Timeout.TotalSeconds:0} s"
        );
    }

    private static string Text(SideLetter letter, Effect effect)
    {
        var text = new StringBuilder(
            string.Create(
                CultureInfo.InvariantCulture,
                $"effect {letter.ToString().ToLowerInvariant()} diffs={effect.Stats.Count} of {effect.AllStats} modifiers={effect.Modifiers.Count}"
            )
        );
        foreach (var stat in effect.Stats)
            text.Append(
                CultureInfo.InvariantCulture,
                $"\nstat {stat.Key} {stat.A?.Text ?? "—"} {stat.B?.Text ?? "—"} {Model.Effect.Difference(stat) ?? "—"}"
            );
        foreach (var modifier in effect.Modifiers)
            text.Append(
                CultureInfo.InvariantCulture,
                $"\nmodifier {(modifier.InA ? "a-only" : "b-only")} {modifier.Line.SourceName}: {modifier.Line.Description}"
            );
        return text.ToString();
    }
}
