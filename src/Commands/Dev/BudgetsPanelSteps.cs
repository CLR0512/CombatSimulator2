using MelvorMods.CombatSimulator2.Common;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Engine.Dev;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel;
using MelvorMods.CombatSimulator2.Panel.Dev;
using static MelvorMods.CombatSimulator2.Commands.Dev.BudgetsBench;

namespace MelvorMods.CombatSimulator2.Commands.Dev;

/// <summary>
/// The budgets bench's panel steps: the closed and open controls, then everything a player does to the window, each
/// in a frame window of its own, in one look.
/// </summary>
internal static partial class BudgetsPanelSteps
{
    /// <summary>
    /// One thing a player does to the window, repeated through a frame window: the budgets it measures, the control
    /// it is judged against (the closed panel for opening and closing, else the open one), how the window is
    /// prepared and what is done during it.
    /// </summary>
    private sealed record Interaction(
        string Step,
        string[] Budgets,
        bool AgainstClosed,
        Func<IEnumerable<object?>> Prepare,
        Func<BenchDrive> Drive
    );

    // A window built anew has no Settings view, so opening it costs only what every open costs.
    private static IEnumerable<object?> FreshWindow()
    {
        Cs2Modal.Open(Cs2Modal.Simulate);
        foreach (var _ in Settle())
            yield return null;
        Cs2Modal.Close();
        Cs2Modal.Retext();
        Cs2Modal.Open(Cs2Modal.Simulate);
        foreach (var _ in Settle())
            yield return null;
        Cs2Modal.Close();
        foreach (var _ in Wait(0.5))
            yield return null;
    }

    private static IEnumerable<object?> HistoryShownThenClosed()
    {
        Cs2Modal.Open(Cs2Modal.History);
        foreach (var _ in Settle(1))
            yield return null;
        Cs2Modal.ShowView(Cs2Modal.Simulate);
        foreach (var _ in Settle())
            yield return null;
        Cs2Modal.Close();
        foreach (var _ in Wait(0.5))
            yield return null;
    }

    private static IEnumerable<object?> OpenOn(string view, double after = 0.5)
    {
        Cs2Modal.Open(view);
        foreach (var _ in Settle(after))
            yield return null;
    }

    private static Interaction Scroll(string list, string view) => Scroll(list, () => OpenOn(view, 1));

    private static Interaction Scroll(string list, Func<IEnumerable<object?>> prepare) =>
        new(
            $"scroll {list}",
            [$"scroll-{list}"],
            false,
            prepare,
            () =>
            {
                var drive = new BenchDrive();
                // Top to end and back over the window.
                return drive
                    .EveryFrame(t =>
                    {
                        if (!PanelDrive.ScrollTo(list, 1 - Math.Abs(2 * t / Seconds - 1)))
                            drive.Problem ??= $"the {list} list isn't shown or has nothing to scroll";
                    })
                    .AtEnd(PanelDrive.Restore);
            }
        );

    private static Interaction Rebuild(string kind) =>
        new(
            $"rebuild {kind}",
            [$"rebuild-{kind}"],
            false,
            () => OpenOn(Cs2Modal.Simulate),
            () =>
                new BenchDrive().Beat(2, 0, $"rebuild-{kind}", () => PanelDrive.Rebuild(kind)).AtEnd(PanelDrive.Restore)
        );

    private static IReadOnlyList<Interaction> Interactions() =>
        [
            new(
                "open and close",
                ["open", "close"],
                true,
                FreshWindow,
                () =>
                    new BenchDrive()
                        .Beat(1, 0, "open", () => Cs2Modal.Open(Cs2Modal.Simulate))
                        .Beat(1, 0.5, "close", Cs2Modal.Close)
            ),
            new(
                "reopen after the saved runs were shown",
                ["reopen-after-history"],
                true,
                HistoryShownThenClosed,
                () =>
                    new BenchDrive()
                        .Beat(1, 0, "reopen-after-history", () => Cs2Modal.Open())
                        .Beat(1, 0.5, null, Cs2Modal.Close)
            ),
            new(
                "first open",
                ["first-open"],
                true,
                () => OpenOn(Cs2Modal.Simulate),
                // Closed and dropped, so each open builds the window as the first one after a load does.
                () =>
                    new BenchDrive()
                        .Beat(
                            2,
                            0,
                            null,
                            () =>
                            {
                                Cs2Modal.Close();
                                Cs2Modal.Retext();
                            }
                        )
                        .Beat(2, 1, "first-open", () => Cs2Modal.Open(Cs2Modal.Simulate))
            ),
            new(
                "switch between Simulate and Settings",
                ["tab-settings", "tab-simulate"],
                false,
                () => OpenOn(Cs2Modal.Settings).Concat(OpenOn(Cs2Modal.Simulate)),
                () =>
                    new BenchDrive()
                        .Beat(1, 0, "tab-settings", () => Cs2Modal.ShowView(Cs2Modal.Settings))
                        .Beat(1, 0.5, "tab-simulate", () => Cs2Modal.ShowView(Cs2Modal.Simulate))
            ),
            new(
                "switch between Simulate and Setup",
                ["tab-setup"],
                false,
                () => SetupOn(SetupPage.Gear).Concat(OpenOn(Cs2Modal.Simulate)),
                () =>
                    new BenchDrive()
                        .Beat(1, 0, "tab-setup", () => Cs2Modal.ShowView(Cs2Modal.Setup))
                        .Beat(1, 0.5, null, () => Cs2Modal.ShowView(Cs2Modal.Simulate))
            ),
            Scroll(PanelDrive.Targets, StripOfEveryTarget),
            Scroll(PanelDrive.History, Cs2Modal.History),
            Scroll(PanelDrive.Modifiers, FullStats),
            Scroll(PanelDrive.Results, Cs2Modal.Simulate),
            new(
                "a run updating",
                ["run-updating"],
                false,
                () => OpenOn(Cs2Modal.Simulate),
                () =>
                {
                    var drive = new BenchDrive();
                    return drive
                        .EveryFrame(t =>
                        {
                            if (drive.Problem != null || RunService.Instance.IsActive || t > Seconds - 1)
                                return;
                            try
                            {
                                StartRun();
                            }
                            catch (CommandException e)
                            {
                                drive.Problem = e.Message;
                            }
                        })
                        .AtEnd(() =>
                        {
                            if (RunService.Instance.IsActive)
                                RunService.Instance.Cancel();
                        });
                }
            ),
            Rebuild(PanelDrive.Skin),
            Rebuild(PanelDrive.Language),
            new(
                "scale change",
                ["scale-change"],
                false,
                () => OpenOn(Cs2Modal.Simulate),
                () => new BenchDrive().Beat(2, 0, "scale-change", PanelDrive.ChangeScale).AtEnd(PanelDrive.Restore)
            ),
            .. ViewInteractions(),
        ];

    // Each take: a closed control, an open control, then every interaction, so a take's windows are judged against
    // controls taken minutes, not a quarter of an hour, before them.
    public static IEnumerable<object?> Run(string look)
    {
        var panel = WantsGroup(Budgets.Panel);
        var interactions = panel ? Interactions().Where(i => i.Budgets.Any(Wants)).ToList() : [];
        if (PanelState.Prefs.Skin != look)
            Cs2Modal.SetSkin(look);
        foreach (var _ in Wait(1.5))
            yield return null;
        if (!LookShown(look))
        {
            foreach (var name in interactions.SelectMany(i => i.Budgets).Where(Wants))
                Merge(new NotMeasuredLine(name, $"the {look} look can't be shown"));
            yield break;
        }
        if (panel)
        {
            // A full run on screen, so the results list and the chart cost what they cost a player.
            Step($"{look}: a run to fill the results");
            StartRun();
            foreach (var _ in Until(() => !RunService.Instance.IsActive, 300))
                yield return null;
        }
        var closed = new List<WindowStats>();
        var open = new List<WindowStats>();
        var allocated = new List<double>();
        var allocatedOpen = new List<double>();
        var verdicts = new Dictionary<string, List<FrameVerdict>>(StringComparer.Ordinal);
        var usable = new Dictionary<string, List<IReadOnlyList<double>>>(StringComparer.Ordinal);
        var problems = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var take = 0; take < Repeats; take++)
        {
            Step($"{look}: closed control, take {take + 1}");
            Cs2Modal.Close();
            foreach (var _ in Wait(1))
                yield return null;
            FrameAlloc.Take();
            foreach (var _ in Window(Seconds, null, closed.Add))
                yield return null;
            var (bytes, frames) = FrameAlloc.Take();
            allocated.Add(frames == 0 ? 0 : (double)bytes / frames);
            if (!panel)
                continue;
            Step($"{look}: open control, take {take + 1}");
            foreach (var _ in OpenOn(Cs2Modal.Simulate, 2))
                yield return null;
            FrameAlloc.Take();
            foreach (var _ in Window(Seconds, null, open.Add))
                yield return null;
            var (openBytes, openFrames) = FrameAlloc.Take();
            allocatedOpen.Add(openFrames == 0 ? 0 : (double)openBytes / openFrames);
            foreach (var interaction in interactions)
            {
                Step($"{look}: {interaction.Step}, take {take + 1}");
                foreach (var _ in interaction.Prepare())
                    yield return null;
                var drive = interaction.Drive();
                WindowStats? stats = null;
                foreach (var _ in Window(Seconds, drive, s => stats = s))
                    yield return null;
                foreach (var _ in Settle())
                    yield return null;
                var control = interaction.AgainstClosed ? closed[^1] : open[^1];
                foreach (var name in interaction.Budgets.Where(Wants))
                {
                    if (drive.Problem != null)
                        problems[name] = drive.Problem;
                    Add(verdicts, name, FrameRule.Judge(control, stats!));
                    if (drive.Usable.TryGetValue(name, out var times))
                        Add(usable, name, times);
                }
            }
        }
        if (Wants("closed-alloc"))
            Merge(new ValueLine("closed-alloc", allocated, 0, "bytes/frame", look));
        if (panel && Wants("open-alloc"))
            Merge(new ValueLine("open-alloc", allocatedOpen, 0, "bytes/frame", look));
        foreach (var interaction in interactions)
        foreach (var name in interaction.Budgets.Where(Wants))
        {
            var budget = Budget(name);
            Merge(
                problems.TryGetValue(name, out var problem) ? new NotMeasuredLine(name, problem)
                // Judged by how long it took alone: the check it waits for runs beside the game's frames.
                : budget.Kind == BudgetKind.Time
                    ? new ValueLine(
                        name,
                        [.. usable.GetValueOrDefault(name, []).Where(t => t.Count > 0).Select(t => t.Max())],
                        budget.Limit,
                        "ms",
                        look
                    )
                : new FramesLine(
                    name,
                    verdicts[name],
                    FrameRule.Noisy(interaction.AgainstClosed ? closed : open),
                    budget.UsableMs == null ? null : usable.GetValueOrDefault(name),
                    budget.UsableMs,
                    look
                )
            );
        }
    }

    private static void Add<T>(Dictionary<string, List<T>> lists, string name, T item)
    {
        if (!lists.TryGetValue(name, out var list))
            lists[name] = list = [];
        list.Add(item);
    }

    /// <summary>How many interaction windows a take has, for the progress count.</summary>
    public static int Count() => Interactions().Count(i => i.Budgets.Any(Wants));
}
