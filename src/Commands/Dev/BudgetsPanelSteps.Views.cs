using System.Diagnostics;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel;
using MelvorMods.CombatSimulator2.Panel.Dev;
using MelvorMods.CombatSimulator2.Panel.SetupTab;
using MelvorMods.CombatSimulator2.Panel.Stats;
using static MelvorMods.CombatSimulator2.Commands.Dev.BudgetsBench;

namespace MelvorMods.CombatSimulator2.Commands.Dev;

// The steps on the views a setup is made and read in: the strip, the pickers, the Setup pages, the full stats.
internal static partial class BudgetsPanelSteps
{
    // How long an edit that never shows counts for: well past its limit.
    private const double NeverShownMs = 3000;

    // The Setup tab on a page, with side A's check in: a page drawn while its side is still being checked isn't
    // what a player switches to.
    private static IEnumerable<object?> SetupOn(string page)
    {
        foreach (var _ in OpenOn(Cs2Modal.Setup))
            yield return null;
        SetupTabView.ShowPage(page);
        foreach (var _ in Settle(1))
            yield return null;
        foreach (var _ in Until(() => !SideChecker.Checking(SideLetter.A), 5))
            yield return null;
    }

    private static IEnumerable<object?> ShopWithEveryPurchase()
    {
        foreach (var _ in SetupOn(SetupPage.Shop))
            yield return null;
        PanelDrive.ShowEveryEntry();
        foreach (var _ in Settle())
            yield return null;
    }

    private static IEnumerable<object?> StripOfEveryTarget()
    {
        foreach (var _ in OpenOn(Cs2Modal.Simulate, 1))
            yield return null;
        PanelDrive.ShowEveryTarget();
        PanelDrive.ShowStrip(true);
        foreach (var _ in Settle(1))
            yield return null;
    }

    private static IEnumerable<object?> FullStats()
    {
        foreach (var _ in OpenOn(Cs2Modal.Simulate))
            yield return null;
        PanelDrive.TwoSides();
        FullStatsView.Open(FullStatsView.Both);
        foreach (var _ in Settle(2))
            yield return null;
    }

    private static IEnumerable<object?> TwoSidesOnSimulate()
    {
        foreach (var _ in OpenOn(Cs2Modal.Simulate))
            yield return null;
        PanelDrive.TwoSides();
        foreach (var _ in Until(() => !SideChecker.Checking(SideLetter.B), 5))
            yield return null;
        foreach (var _ in Settle())
            yield return null;
    }

    private static IEnumerable<object?> OneSideOnSimulate()
    {
        foreach (var _ in OpenOn(Cs2Modal.Simulate))
            yield return null;
        PanelDrive.OneSide();
        foreach (var _ in Settle())
            yield return null;
    }

    private static readonly string[] SwitchedPages = [SetupPage.Astrology, SetupPage.Shop, SetupPage.Gear];

    private static IReadOnlyList<Interaction> ViewInteractions() =>
        [
            new(
                "open the Compare picker",
                ["open-picker"],
                false,
                OneSideOnSimulate,
                () =>
                    new BenchDrive()
                        .Beat(1, 0, "open-picker", () => PanelState.PickBaseline(true))
                        .Beat(1, 0.5, null, () => PanelState.PickBaseline(false))
                        .AtEnd(() =>
                        {
                            PanelState.PickBaseline(false);
                            PanelDrive.Restore();
                        })
            ),
            new(
                "open the full stats view",
                ["open-stats"],
                false,
                TwoSidesOnSimulate,
                () =>
                    new BenchDrive()
                        .Beat(1, 0, "open-stats", () => FullStatsView.Open(FullStatsView.Both))
                        .Beat(1, 0.5, null, () => Cs2Modal.ShowView(Cs2Modal.Simulate))
                        .AtEnd(() =>
                        {
                            Cs2Modal.ShowView(Cs2Modal.Simulate);
                            PanelDrive.Restore();
                        })
            ),
            new(
                "switch between Setup pages",
                ["page-switch"],
                false,
                () => SetupOn(SetupPage.Gear),
                () =>
                {
                    var next = 0;
                    return new BenchDrive()
                        .Beat(
                            0.5,
                            0,
                            "page-switch",
                            () => SetupTabView.ShowPage(SwitchedPages[next++ % SwitchedPages.Length])
                        )
                        .AtEnd(() => SetupTabView.ShowPage(SetupPage.Gear));
                }
            ),
            Scroll(PanelDrive.Shop, ShopWithEveryPurchase),
            new(
                "open and close the strip",
                ["strip-toggle"],
                false,
                StripOfEveryTarget,
                () =>
                {
                    var drive = new BenchDrive();
                    return drive
                        .Beat(
                            1,
                            0,
                            null,
                            () =>
                            {
                                if (!PanelDrive.ShowStrip(false))
                                    drive.Problem ??= "the Simulate view isn't built";
                            }
                        )
                        .Beat(1, 0.5, null, () => PanelDrive.ShowStrip(true))
                        .AtEnd(PanelDrive.Restore);
                }
            ),
            new(
                "an edit showing in the Effect panel",
                ["effect-update"],
                false,
                () => SetupOn(SetupPage.Gear),
                EffectDrive
            ),
        ];

    // A level edited every second and a half; each edit is timed until the Effect panel has checked it and shows
    // the result.
    private static BenchDrive EffectDrive()
    {
        var drive = new BenchDrive();
        var times = drive.Usable["effect-update"] = [];
        var clock = new Stopwatch();
        var edits = 0;
        var checking = false;
        return drive
            .EveryFrame(t =>
            {
                if (Cs2Modal.SetupTabBuilt is not { } setup)
                {
                    drive.Problem ??= "the Setup tab isn't built";
                    return;
                }
                if (clock.IsRunning)
                {
                    var state = setup.EffectState;
                    checking |= state == "checking";
                    if (checking && state != "checking" || clock.Elapsed.TotalMilliseconds >= NeverShownMs)
                    {
                        times.Add(clock.Elapsed.TotalMilliseconds);
                        clock.Reset();
                    }
                }
                if (clock.IsRunning || t < edits * 1.5 || t > Seconds - 1.5)
                    return;
                edits++;
                checking = false;
                PanelDrive.EditLevel(40 + edits % 2);
                clock.Restart();
            })
            .AtEnd(PanelDrive.Restore);
    }
}
