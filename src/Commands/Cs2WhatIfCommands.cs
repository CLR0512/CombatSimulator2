using System.Globalization;
using System.Text;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Commands;

/// <summary>
/// <c>cs2 whatif</c>: the setup against the same setup with changes, checked in a copy first; <c>check</c> only
/// checks them, <c>slots</c> lists what can be changed. A change goes to side B unless it names side A.
/// </summary>
internal static class Cs2WhatIfCommands
{
    public static void Add(List<Subcommand> subs) =>
        subs.Add(
            new(
                "whatif",
                "cs2 whatif <target…|current|selection|all> <change…> [a=<setup>] [ignore-req] [trials=1000] [seed=<u64>] [copies=<n>] [where=host|game] [fresh] [stop=<percent>]"
                    + "\ncs2 whatif check <change…> [a=<setup>] [ignore-req]"
                    + "\ncs2 whatif slots [a=<setup>]"
                    + "\n  changes: "
                    + ChangeArgs.Forms,
                ["a", "trials", "seed", "copies", "where", "stop", .. ChangeArgs.Options],
                ["ignore-req", "fresh"],
                WhatIf
            )
        );

    private static void WhatIf(CommandCall call, CommandArgs args)
    {
        switch (args.Word(0)?.ToLowerInvariant())
        {
            case "check":
                Check(call, args);
                return;
            case "slots":
                Slots(call, args);
                return;
            default:
                Run(call, args);
                return;
        }
    }

    private static Setup SetupA(CommandArgs args) => RunArgs.SetupOf(args.String("a"), args.Flag("ignore-req"));

    // Both sides start from the same base; each carries its own changes.
    private static (Setup A, Setup B) Sides(CommandArgs args)
    {
        var (changesA, changesB) = ChangeArgs.From(args, SideLetter.B);
        if (changesA.IsEmpty && changesB.IsEmpty)
            throw new CommandException("no changes: give " + ChangeArgs.Forms);
        var a = SetupA(args);
        a.Changes = changesA;
        var b = SetupA(args);
        b.Changes = changesB;
        return (a, b);
    }

    private static void Run(CommandCall call, CommandArgs args)
    {
        if (args.Words.Count == 0)
            throw new CommandException("usage: cs2 whatif <target…|all> <change…> (see cs2 help)");
        var (a, b) = Sides(args);
        var request = RunArgs.RequestFrom(args, a, args.Words);
        RunService.Instance.StartSides(
            request,
            b,
            start =>
            {
                var text = new StringBuilder(SideText.Checks(start.A, start.B));
                if (start.Comparison is not { } comparison)
                {
                    // Nothing that was asked for changed the setup: there are no two sides to compare.
                    call.Error(
                        text.Append(
                                CultureInfo.InvariantCulture,
                                $"\nno change can be applied: the sides are the same, {start.Run!.Id} started as a single run"
                            )
                            .ToString()
                    );
                    return;
                }
                // The comparison's id is side B's run, known once A has finished.
                if (comparison.BStarted)
                    text.Append(CultureInfo.InvariantCulture, $"\n{comparison.Record.Id} started");
                else
                    text.Append(
                        CultureInfo.InvariantCulture,
                        $"\nwhat-if started: A {comparison.A.Id}, B waiting, {comparison.Record.Targets.Count} targets, seed {comparison.Record.Seed}"
                    );
                if (comparison.IsFinal)
                    text.Append(CultureInfo.InvariantCulture, $"\n{comparison.Record.Id} final at once");
                call.Ok(text.ToString());
            },
            error => call.Error(error.Message)
        );
    }

    private static void Check(CommandCall call, CommandArgs args)
    {
        var (a, b) = Sides(args);
        SideText.Check(
            a,
            b,
            (checkA, checkB, error) =>
            {
                if (error != null)
                {
                    call.Error(error.Message);
                    return;
                }
                var timed = checkB ?? checkA!;
                call.Ok(
                    SideText.Checks(checkA, checkB)
                        + string.Create(
                            CultureInfo.InvariantCulture,
                            $"\nelapsed={timed.Changes.ElapsedMs:0} apply={timed.Changes.ApplyMs:0.00}"
                        )
                );
            }
        );
    }

    private static void Slots(CommandCall call, CommandArgs args)
    {
        var game = LiveGame.Require();
        RunService.CheckSide(
            SetupA(args),
            ChangeList.Empty,
            (side, error) =>
            {
                if (side == null)
                {
                    call.Error(error?.Message ?? "the check failed");
                    return;
                }
                var check = side.Changes;
                var state = check.Before;
                string Name(string id) => LiveGame.NameOf(game, id);
                var text = new StringBuilder("setup " + check.Setup);
                text.Append("\nlevels:");
                foreach (var level in state.Levels)
                    text.Append(
                        CultureInfo.InvariantCulture,
                        $"\n  {Ids.Brief(level.Skill)} {level.Level} (cap {level.Cap}, lowest {Changes.LowestLevel(level.Skill)})"
                    );
                text.Append("\nslots:");
                foreach (var slot in state.Slots)
                    text.Append(
                        CultureInfo.InvariantCulture,
                        $"\n  {Ids.Brief(slot.Slot)}  {(slot.Item is { } item ? $"{Ids.Brief(item)} ({Name(item)})" : "empty")}"
                    );
                text.Append("\nstance: ")
                    .Append(state.Stance is { } stance ? Ids.Brief(stance) : "?")
                    .Append(" (this weapon: ")
                    .Append(string.Join(", ", state.Stances.Select(Ids.Brief)))
                    .Append(')');
                text.Append(CultureInfo.InvariantCulture, $"\nprayer points: {state.PrayerPoints}");
                text.Append("\nspell: ")
                    .Append(state.Spell is { } spell ? Ids.Brief(spell) : "none")
                    .Append("  imbuement: ")
                    .Append(state.Imbuement is { } imbuement ? Ids.Brief(imbuement) : "none")
                    .Append("  elements: ")
                    .Append(state.Elements.Count == 0 ? "none" : string.Join(", ", state.Elements.Select(Ids.Brief)));
                text.Append("\nrune costs: ")
                    .Append(
                        state.RuneCosts.Count == 0
                            ? "none"
                            : string.Join(
                                ", ",
                                state.RuneCosts.Select(r =>
                                    string.Create(
                                        CultureInfo.InvariantCulture,
                                        $"{Ids.Brief(r.Item)} {r.PerCast} (held {r.Held})"
                                    )
                                )
                            )
                    );
                text.Append("\nprayer slots: ")
                    .Append(
                        string.Join(
                            ", ",
                            state.PrayerSlots.Select(s =>
                                $"{Ids.Brief(s.Slot)} {(s.Unlocked ? s.Prayer is { } p ? Ids.Brief(p) : "empty" : "locked")}"
                            )
                        )
                    );
                text.Append("\nfood: ").Append(state.Food is { } food ? $"{Ids.Brief(food)} ({Name(food)})" : "none");
                text.Append("\nprayers:");
                foreach (var prayer in state.Prayers)
                {
                    text.Append(
                        CultureInfo.InvariantCulture,
                        $"\n  {Ids.Brief(prayer.Prayer)} ({Name(prayer.Prayer)}) {(prayer.On ? "on" : "off")}"
                    );
                    if (prayer.Locked.Count > 0)
                        text.Append("  locked: ").Append(prayer.Locked.English("; "));
                }
                text.Append("\npotion: ")
                    .Append(state.PotionsInBuild ? "combat potions in this build" : "no combat potions in this build");
                call.Ok(text.ToString());
            }
        );
    }
}
