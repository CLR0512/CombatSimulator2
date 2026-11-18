using Godot;
using MelvorMods.CombatSimulator2.Game;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Results;

/// <summary>
/// The banners over the results: a cancelled run, a failure or a setup that can't be simulated, and a comparison's
/// warnings.
/// </summary>
internal sealed class ResultsBanners(VBoxContainer bannerHolder, VBoxContainer warnings)
{
    private readonly VBoxContainer _bannerHolder = bannerHolder;
    private readonly VBoxContainer _warnings = warnings;
    private BannerState? _bannerShown;
    private bool _bannerBuilt;
    private string? _warningsFor;

    /// <summary>Raised when the comparison's banner asks for another saved run.</summary>
    public event Action? PickAnotherBaseline;

    /// <summary>Raised by "Simulate the N again" with the keys that were cancelled.</summary>
    public event Action<IReadOnlyList<string>>? SimulateAgain;

    public event Action? UseCurrentSetup;

    /// <summary>
    /// Raised by the host-error banner's button, to simulate these targets again in the game's process.
    /// </summary>
    public event Action<IReadOnlyList<string>>? UseGameProcess;

    private string? _dismissedFor;

    public void RefreshWarnings(ComparedRuns comparison)
    {
        var key = comparison.Record.Id + "|" + string.Join("|", comparison.Warnings.Select(n => n.Msg));
        if (key == _warningsFor)
            return;
        _warningsFor = key;
        Ui.Clear(_warnings);
        // Read once, a warning is put away for those two runs, whichever is A: swapping them doesn't bring it back.
        var runs =
            string.CompareOrdinal(comparison.A.Id, comparison.B.Id) < 0
                ? comparison.A.Id + "|" + comparison.B.Id
                : comparison.B.Id + "|" + comparison.A.Id;
        if (comparison.Warnings.Count == 0 || runs == _dismissedFor)
            return;
        var gone = comparison.Warnings.Any(w => w.Msg == ComparisonWarnings.BaselineGone.Msg);
        var dismiss = Ui.Button(
            Tr.Get("Dismiss"),
            Look.Button4,
            "Cs2CompareWarningDismiss",
            () =>
            {
                _dismissedFor = runs;
                Ui.Clear(_warnings);
            }
        );
        Button[] actions = gone
            ?
            [
                Ui.Button(
                    Tr.Get("Pick another run"),
                    Look.Button8,
                    "Cs2CompareRepick",
                    () => PickAnotherBaseline?.Invoke()
                ),
                dismiss,
            ]
            : [dismiss];
        _warnings.AddChild(
            Banner.Make(
                "Cs2CompareWarning",
                gone ? Look.Deadly : Look.Risky,
                Art.File(Art.InfoPath),
                gone ? Tr.Get("Baseline gone") : Tr.Get("The sides differ in more than the setup"),
                string.Join("\n", comparison.Warnings.Select(Tr.Note)),
                actions
            )
        );
    }

    private static string FailureTitle(FailureKind kind) =>
        kind switch
        {
            FailureKind.HostCrash => Tr.Get("The simulator closed unexpectedly"),
            FailureKind.HostHang => Tr.Get("The simulator stopped responding"),
            FailureKind.HostStart => Tr.Get("The simulator couldn't start"),
            FailureKind.Run => Tr.Get("The run failed"),
            FailureKind.Target => Tr.Get("Some targets failed"),
            FailureKind.Save => Tr.Get("Couldn't save"),
            _ => Tr.Get("Couldn't open a saved run"),
        };

    public void Refresh()
    {
        var banner = PanelState.Banner;
        if (_bannerBuilt && Equals(banner, _bannerShown))
            return;
        _bannerBuilt = true;
        _bannerShown = banner;
        Ui.Clear(_bannerHolder);
        switch (banner)
        {
            case BannerState.Cancelled cancelled:
                var again = Ui.Button(
                    Tr.Format("Simulate the {0} again", cancelled.Count),
                    Look.Button8,
                    "Cs2BannerAgain",
                    () => SimulateAgain?.Invoke(cancelled.Keys)
                );
                again.Disabled = cancelled.Count == 0;
                _bannerHolder.AddChild(
                    Banner.Make(
                        "Cs2Banner",
                        Look.Risky,
                        Art.File(Art.InfoPath),
                        Tr.Get("Run cancelled"),
                        Tr.Plural(
                            "Finished results are kept; {0} target was cancelled.",
                            "Finished results are kept; {0} targets were cancelled.",
                            cancelled.Count,
                            cancelled.Count
                        ),
                        again,
                        Ui.Button(Tr.Get("Dismiss"), Look.Button4, "Cs2BannerDismiss", () => PanelState.SetBanner(null))
                    )
                );
                break;
            case BannerState.Failure failure:
                var buttons = new List<Button>
                {
                    Ui.Button(Tr.Get("Save bug report"), Look.Button8, "Cs2BannerReport", Report.ReportNote.Save),
                };
                if (failure.HostKind && failure.Keys.Count > 0)
                    buttons.Add(
                        Ui.Button(
                            Tr.Get("Simulate in the game's process (may stutter)"),
                            Look.Button8,
                            "Cs2UseGameProcess",
                            () => UseGameProcess?.Invoke(failure.Keys)
                        )
                    );
                buttons.Add(
                    Ui.Button(Tr.Get("Dismiss"), Look.Button4, "Cs2BannerDismiss", () => PanelState.SetBanner(null))
                );
                var title = FailureTitle(failure.Kind);
                var reason = Tr.Note(failure.Reason);
                _bannerHolder.AddChild(
                    Banner.Make(
                        "Cs2Banner",
                        Look.Deadly,
                        Art.File(Art.InfoPath),
                        title,
                        // A reason that only repeats the title says nothing more.
                        reason.Equals(title, StringComparison.OrdinalIgnoreCase)
                            ? ""
                            : reason,
                        [.. buttons]
                    )
                );
                break;
            case BannerState.SetupError error:
                var use = Ui.Button(
                    Tr.Get("Use Character as is"),
                    Look.Button8,
                    "Cs2BannerCurrent",
                    () => UseCurrentSetup?.Invoke()
                );
                _bannerHolder.AddChild(
                    Banner.Make(
                        "Cs2Banner",
                        Look.Deadly,
                        Art.File(Art.InfoPath),
                        Tr.Get("This setup can't be simulated"),
                        Tr.Note(error.Reason),
                        [use]
                    )
                );
                break;
        }
    }
}
