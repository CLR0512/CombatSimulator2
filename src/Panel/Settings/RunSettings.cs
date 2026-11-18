using System.Globalization;
using Godot;
using MelvorMods.CombatSimulator2.Engine;
using MelvorMods.CombatSimulator2.Model;
using MelvorMods.CombatSimulator2.Panel.Widgets;

namespace MelvorMods.CombatSimulator2.Panel.Settings;

/// <summary>
/// How runs fight: trials, where, fresh, stopping early, copies and the seed. The Settings tab and the ☰ menu each
/// show one, over the same saved values; <see cref="Show"/> reads them again whenever either is shown, so a change in
/// one, or from a pasted simulation, is what the other shows.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001",
    Justification = "The scene tree owns and frees the nodes."
)]
internal sealed class RunSettings
{
    private static readonly int[] TrialChoices = [100, 250, 500, 1000, 2500, 5000, 10_000];
    private static readonly double[] Precisions = [0.02, 0.05, 0.1];

    private readonly Dropdown _trials;
    private readonly Dropdown _simulateIn;
    private readonly Button _fresh;
    private readonly Dropdown _stop;
    private readonly Dropdown _copies;
    private readonly LineEdit _seed;
    private readonly Button _newSeed;
    private ulong _fixedSeed = 1;

    /// <param name="prefix">The start of each control's node name, so two sets can be told apart.</param>
    /// <param name="help">Whether each row says what it does under its title; without, as a tooltip.</param>
    public RunSettings(VBoxContainer section, string prefix, bool help)
    {
        var prefs = PanelState.Prefs;
        _trials = SettingRow.Options(
            prefix + "Trials",
            TrialChoices.Select(t => t.ToString("#,0", Tr.Culture)),
            Index(TrialChoices, prefs.Trials),
            i => Saved(() => PanelState.Prefs.Trials = TrialChoices[i])
        );
        Row(section, Tr.Get("Trials per target"), Tr.Get("More: tighter ranges, longer runs."), _trials.Root, help);

        _simulateIn = SettingRow.Options(
            prefix == "Cs2Set" ? "Cs2SimulateIn" : prefix + "SimulateIn",
            [Tr.Get("CS2 host"), Tr.Get("Game process (may stutter)")],
            prefs.SimulateIn == Preferences.InGame ? 1 : 0,
            i => Saved(() => PanelState.Prefs.SimulateIn = i == 1 ? Preferences.InGame : Preferences.InHost)
        );
        Row(
            section,
            Tr.Get("Simulate in"),
            Tr.Get("CS2 host: a hidden second copy of the game, so yours doesn't slow."),
            _simulateIn.Root,
            help
        );

        _fresh = Cs2Check.Make(
            prefix + "Fresh",
            "",
            prefs.SimulateFresh,
            on => Saved(() => PanelState.Prefs.SimulateFresh = on)
        );
        Row(
            section,
            Tr.Get("Simulate fresh"),
            Tr.Get("Rerun every target. Off: reuse results with the same setup and settings."),
            _fresh,
            help
        );

        var stopLabels = new List<string> { Tr.Get("Off") };
        stopLabels.AddRange(Precisions.Select(p => Tr.Format("±{0}%", p * 100)));
        _stop = SettingRow.Options(
            prefix + "StopEarly",
            stopLabels,
            StopIndex(prefs),
            i =>
                Saved(() =>
                {
                    PanelState.Prefs.StopEarly = i > 0;
                    if (i > 0)
                        PanelState.Prefs.StopPrecision = Precisions[i - 1];
                })
        );
        Row(
            section,
            Tr.Get("Stop targets early"),
            Tr.Get("Stop a target once its kill rate is this precise. Faster; marked in results."),
            _stop.Root,
            help
        );

        var cores = Math.Max(1, System.Environment.ProcessorCount);
        var copyLabels = new List<string> { Tr.Format("Auto ({0})", RunService.Instance.Status.DefaultCopies) };
        copyLabels.AddRange(Enumerable.Range(1, cores).Select(n => n.ToString(Tr.Culture)));
        _copies = SettingRow.Options(
            prefix + "Copies",
            copyLabels,
            CopiesIndex(prefs, cores),
            i => Saved(() => PanelState.Prefs.Copies = i == 0 ? null : i)
        );
        Row(
            section,
            Tr.Get("Copies in parallel"),
            Tr.Format("Auto: cores − 2, at most 4 ({0} here).", RunService.Instance.Status.DefaultCopies),
            _copies.Root,
            help
        );

        _seed = new LineEdit
        {
            Name = prefix + "Seed",
            CustomMinimumSize = new Vector2(help ? 150 : 130, 0),
            PlaceholderText = Tr.Get("random"),
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled,
        };
        // A framed box, so it reads as a field in both looks; dimmed while a new seed is used each run.
        foreach (var state in new[] { "normal", "focus", "read_only" })
            PixelBox.State(_seed, state, InputBox(state));
        _seed.TextSubmitted += SeedEntered;
        _seed.FocusExited += () => SeedEntered(_seed.Text);
        _newSeed = Cs2Check.Make(
            prefix + "NewSeed",
            Tr.Get("New seed each run"),
            prefs.Seed == null,
            on =>
            {
                PanelState.Prefs.Seed = on ? null : _fixedSeed;
                ShowSeed();
                PanelState.Save();
            }
        );
        ShowSeed();
        var seedRow = Ui.Row(10);
        seedRow.AddChild(_seed);
        seedRow.AddChild(_newSeed);
        Row(section, Tr.Get("Seed"), Tr.Get("Same seed and setup, same results."), seedRow, help);
    }

    /// <summary>Reads every value again from the saved settings.</summary>
    public void Show()
    {
        var prefs = PanelState.Prefs;
        _trials.Select(Index(TrialChoices, prefs.Trials));
        _simulateIn.Select(prefs.SimulateIn == Preferences.InGame ? 1 : 0);
        Cs2Check.Set(_fresh, prefs.SimulateFresh);
        _stop.Select(StopIndex(prefs));
        _copies.Select(CopiesIndex(prefs, Math.Max(1, System.Environment.ProcessorCount)));
        Cs2Check.Set(_newSeed, prefs.Seed == null);
        ShowSeed();
    }

    private static void Saved(Action change)
    {
        change();
        PanelState.Save();
    }

    private static void Row(VBoxContainer section, string title, string help, Control control, bool withHelp)
    {
        SettingRow.Setting(section, title, withHelp ? help : "", control);
        if (!withHelp)
            control.TooltipText = help;
    }

    private static int StopIndex(Preferences prefs) =>
        prefs.StopEarly ? Math.Max(0, Array.IndexOf(Precisions, prefs.StopPrecision)) + 1 : 0;

    private static int CopiesIndex(Preferences prefs, int cores) => prefs.Copies is { } c ? Math.Clamp(c, 1, cores) : 0;

    private static int Index(int[] choices, int value)
    {
        var index = Array.IndexOf(choices, value);
        return index >= 0 ? index
            : Array.FindIndex(choices, c => c >= value) is var i and >= 0 ? i
            : choices.Length - 1;
    }

    private static ulong? ParseSeed(string text) =>
        ulong.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed) ? seed : null;

    private void SeedEntered(string text)
    {
        if (PanelState.Prefs.Seed == null)
            return;
        if (ParseSeed(text) is { } seed)
        {
            _fixedSeed = seed;
            PanelState.Prefs.Seed = seed;
            PanelState.Save();
        }
        ShowSeed();
    }

    // The seed in use: the number, editable; or empty with "random", read-only and dimmed.
    private void ShowSeed()
    {
        var fixedSeed = PanelState.Prefs.Seed;
        if (fixedSeed is { } seed)
            _fixedSeed = seed;
        _seed.Editable = fixedSeed != null;
        _seed.Text = fixedSeed?.ToString(CultureInfo.InvariantCulture) ?? "";
        _seed.Modulate = Colors.White with { A = fixedSeed != null ? 1f : 0.6f };
    }

    private static StyleBoxFlat InputBox(string state) =>
        new()
        {
            BgColor = Colors.Black with { A = state == "read_only" ? 0.25f : 0.5f },
            BorderColor = Colors.White with { A = state == "focus" ? 0.6f : 0.28f },
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 5,
            ContentMarginBottom = 5,
        };
}
