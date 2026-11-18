using System.Globalization;
using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel;
using MelvorMods.CombatSimulator2.Panel.Dev;
using MelvorMods.Core;

namespace MelvorMods.CombatSimulator2.Commands.Dev;

/// <summary>Developer checks on the panel: the chart, the details pane, the bench's drivers and the banner.</summary>
internal static class UiDevCommands
{
    public static void Add(List<Subcommand> subs)
    {
        subs.Add(
            new(
                "chart",
                "cs2dev chart hover|click|shift|toggle <target> | cs2dev chart band|hover-band <area>",
                [],
                [],
                Chart,
                Purpose: "Hover, click or shift-click a bar, click its toggle or a band, fold all"
            )
        );
        subs.Add(
            new(
                "strip",
                "cs2dev strip <target> | cs2dev strip area=<area|dungeons> | cs2dev strip fold=<area|dungeons>",
                ["area", "fold"],
                [],
                Strip,
                Purpose: "Click a portrait, an area's name or its fold mark in the targets strip"
            )
        );
        subs.Add(
            new(
                "details",
                "cs2dev details scroll <node name|top|end> | cs2dev details widths [min px]",
                [],
                [],
                Details,
                Purpose: "Scroll the details pane to a node and list widths"
            )
        );
        subs.Add(
            new(
                "tooltip",
                "cs2dev tooltip <node name>|off",
                [],
                [],
                (call, args) =>
                {
                    var name = args.Word(0) ?? "off";
                    var control = name == "off" ? null : Cs2Modal.Root?.FindChild(name, true, false) as Godot.Control;
                    Panel.Widgets.InstantTooltips.Force(control);
                    call.Ok(
                        control != null ? "tooltip shown for " + name
                        : name == "off" ? "off"
                        : "no control " + name
                    );
                },
                Purpose: "Show a control's tooltip as if the pointer were on it"
            )
        );
        subs.Add(
            new(
                "drive",
                "cs2dev drive scroll <targets|history|modifiers|results> <0-1> | cs2dev drive rebuild <skin|language> | cs2dev drive scale | cs2dev drive restore | cs2dev drive alloc [result]",
                [],
                [],
                Drive,
                Purpose: "Scroll a list, rebuild the window or change its scale as the budgets bench does"
            )
        );
        subs.Add(
            new(
                "patches",
                "cs2dev patches",
                [],
                [],
                (call, _) => call.Ok(Patches()),
                NeedsGame: false,
                Purpose: "List the patches on Godot's resource loads, with the assembly each comes from"
            )
        );
        subs.Add(
            new(
                "outside",
                "cs2dev outside",
                [],
                [],
                (call, _) => call.Ok(PanelDrive.ClickOutside()),
                NeedsGame: false,
                Purpose: "Click the dimmed room just outside CS2's window, as the mouse would"
            )
        );
        subs.Add(
            new(
                "motion",
                "cs2dev motion",
                [],
                [],
                (call, _) => call.Ok(Motions()),
                NeedsGame: false,
                Purpose: "Print each animation's length at the motion level in use, and what it was before the levels"
            )
        );
        subs.Add(
            new(
                "banner",
                "cs2dev banner setup-error <reason> | cs2dev banner none",
                [],
                [],
                Banner,
                NeedsGame: false,
                Purpose: "Show the setup-error banner"
            )
        );
    }

    private static string Motions()
    {
        var level = MotionChoice.Level;
        var text = new System.Text.StringBuilder("ok motion ").Append(MotionTimes.Name(level));
        foreach (var kind in MotionTimes.Kinds)
        {
            var seconds = MotionTimes.Seconds(kind, level);
            text.Append('\n')
                .Append(kind.ToString().ToLowerInvariant())
                .Append(' ')
                .Append(
                    seconds > 0 ? seconds.ToString("0.00", CultureInfo.InvariantCulture) + " s"
                    : MotionTimes.Repeats(kind) ? "still"
                    : "at once"
                )
                .Append(MotionTimes.Repeats(kind) ? " repeating" : "")
                .Append(" before ")
                .Append(
                    MotionTimes.Before(kind) is { } before
                        ? before.ToString("0.00", CultureInfo.InvariantCulture) + " s"
                        : "none"
                );
        }
        return text.ToString();
    }

    private static void Chart(CommandCall call, CommandArgs args) =>
        call.Ok(
            (Cs2Modal.SimulateView is { Ready: true } view ? view : null)?.Results.Chart.Poke(
                args.Word(0) ?? "",
                args.Word(1) ?? ""
            )
                ?? "the chart isn't built"
        );

    private static void Strip(CommandCall call, CommandArgs args) =>
        call.Ok(
            (Cs2Modal.SimulateView is { Ready: true } view ? view : null)?.Strip.Poke(
                args.Word(0) ?? "",
                args.String("area"),
                args.String("fold")
            )
                ?? "the Simulate view isn't built"
        );

    private static void Details(CommandCall call, CommandArgs args) =>
        call.Ok(
            Cs2Modal.SimulateView is not { Ready: true } view ? "the Simulate view isn't built"
            : args.Word(0) == "widths" && args.Word(2) == "view"
                ? Panel.Results.Details.WidthsUnder(
                    view.Root,
                    float.Parse(args.Word(1) ?? "300", System.Globalization.CultureInfo.InvariantCulture)
                )
            : args.Word(0) == "widths"
                ? (
                    PanelState.ComparisonShown
                        ? view.CompareDetails.Widths(
                            float.Parse(args.Word(1) ?? "300", System.Globalization.CultureInfo.InvariantCulture)
                        )
                        : view.Details.Widths(
                            float.Parse(args.Word(1) ?? "300", System.Globalization.CultureInfo.InvariantCulture)
                        )
                )
            : PanelState.ComparisonShown ? view.CompareDetails.Reveal(args.Word(1) ?? "end")
            : view.Details.Reveal(args.Word(1) ?? "end")
        );

    private static void Drive(CommandCall call, CommandArgs args)
    {
        switch (args.Word(0))
        {
            case "scroll" when args.Word(1) is { } list:
                var fraction = double.Parse(args.Word(2) ?? "1", System.Globalization.CultureInfo.InvariantCulture);
                var scrolled = PanelDrive.ScrollTo(list, fraction);
                call.Ok($"{(scrolled ? "scrolled" : "not scrolled")} {PanelDrive.Describe(list)}");
                break;
            case "rebuild" when args.Word(1) is { } kind:
                PanelDrive.Rebuild(kind);
                call.Ok($"rebuilding as a {kind} change does (cs2dev drive restore)");
                break;
            case "scale":
                PanelDrive.ChangeScale();
                call.Ok($"scale {WindowScale.Percent(WindowScale.Drawn)}% (cs2dev drive restore)");
                break;
            case "alloc":
                // Each of CS2's frame callbacks called once more a frame for a while: what each allocates.
                (string Name, Action Call)[] callbacks =
                [
                    ("MainThread.Drain", MainThread.Drain),
                    ("LiveGame.Watch", Game.LiveGame.Watch),
                    ("RunService.OnFrame", Engine.RunService.Instance.OnFrame),
                    ("PerfRecorder.OnFrame", Engine.PerfRecorder.OnFrame),
                    ("Cs2Modal.OnFrame", Cs2Modal.OnFrame),
                    ("ModalHost.OnFrame", MelvorMods.Core.ModalHost.OnFrame),
                    // The open window's own parts, to see which of them the window's figure comes from.
                    ("HelpLayer.OnFrame", Panel.Help.HelpLayer.OnFrame),
                    ("SideChecker.OnFrame", Panel.SetupTab.SideChecker.OnFrame),
                    (
                        "InstantTooltips.OnFrame",
                        () =>
                        {
                            if (Cs2Modal.Root is { } root)
                                Panel.Widgets.InstantTooltips.OnFrame(root);
                        }
                    ),
                    ("SimulateView.OnFrame", () => Cs2Modal.SimulateView?.OnFrame()),
                ];
                if (args.Word(1) == "result")
                {
                    call.Ok(FrameAlloc.ProbeResult());
                    break;
                }
                FrameAlloc.Probe(callbacks, 1440);
                call.Ok("probing 1440 frames (cs2dev drive alloc result)");
                break;
            case "restore":
                PanelDrive.Restore();
                call.Ok("settings put back");
                break;
            default:
                throw new CommandException("usage: cs2dev drive scroll <list> <0-1> | rebuild <kind> | restore");
        }
    }

    // Every patch on the two loads the check copies serialise, so a patch left behind by a reload shows.
    private static string Patches()
    {
        var text = new System.Text.StringBuilder();
        var targets = typeof(Godot.ResourceLoader)
            .GetMethods()
            .Where(m => m.Name is nameof(Godot.ResourceLoader.LoadThreadedGet) or nameof(Godot.ResourceLoader.Load));
        foreach (var method in targets)
        {
            if (HarmonyLib.Harmony.GetPatchInfo(method) is not { } info)
                continue;
            text.Append(method.Name).Append('(').Append(method.GetParameters().Length).Append(")\n");
            foreach (var (kind, patches) in new[] { ("prefix", info.Prefixes), ("finalizer", info.Finalizers) })
            foreach (var patch in patches)
                text.Append("  ")
                    .Append(kind)
                    .Append(' ')
                    .Append(patch.owner)
                    .Append(' ')
                    .Append(patch.PatchMethod.DeclaringType?.FullName)
                    .Append(" asm#")
                    .Append(patch.PatchMethod.DeclaringType?.Assembly.GetHashCode())
                    .Append('\n');
        }
        return text.Length == 0 ? "no patches" : text.ToString();
    }

    private static void Banner(CommandCall call, CommandArgs args)
    {
        if (Cs2Modal.SimulateView is not { Ready: true } view)
            throw new CommandException("the Simulate view isn't built");
        if (args.Word(0) == "setup-error")
            view.ShowSetupError(string.Join(' ', args.Words.Skip(1)));
        else
            PanelState.SetBanner(null);
        call.Ok("ok banner " + (args.Word(0) ?? "none"));
    }
}
